using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Items.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Coflnet.Sky.Items.Controllers;
using Coflnet.Sky.Core;
using System;
using Newtonsoft.Json;
using RestSharp;
using System.Collections.Generic;
using System.Linq;

namespace Coflnet.Sky.Items.Services
{

    public class BaseBackgroundService : BackgroundService
    {
        private IServiceScopeFactory scopeFactory;
        private IConfiguration config;
        private ILogger<BaseBackgroundService> logger;
        private Prometheus.Counter consumeCount = Prometheus.Metrics.CreateCounter("sky_items_consume", "How many messages were consumed");

        public BaseBackgroundService(
            IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<BaseBackgroundService> logger)
        {
            this.scopeFactory = scopeFactory;
            this.config = config;
            this.logger = logger;
        }
        /// <summary>
        /// Called by asp.net on startup
        /// </summary>
        /// <param name="stoppingToken">is canceled when the applications stops</param>
        /// <returns></returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // make sure all migrations are applied
            await Migrate();
            using var scope = scopeFactory.CreateScope();
            using (var context = scope.ServiceProvider.GetRequiredService<ItemDbContext>())
            {
                if (!context.Items.Any())
                {
                    await Task.Delay(1000);
                    logger.LogInformation("migrating old db");
                    await CopyOverItems(context);
                }
                logger.LogInformation("fixing");
                await FixItems(context);
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    logger.LogInformation("starting update from api");
                    await DownloadFromApi();
                    // bazaar is loaded every time as no bazaar events are consumed
                    await LoadBazaar();
                    logger.LogInformation("loaded bazaar data");
                }
                catch (Exception e)
                {
                    logger.LogError(e, "updating from api");
                }
            });

            var flipCons = Coflnet.Kafka.KafkaConsumer.ConsumeBatch<SaveAuction>(config["KAFKA_HOST"], config["TOPICS:NEW_AUCTION"], async batch =>
            {
                try
                {
                    //await Task.Delay(100);

                    using (var scope = scopeFactory.CreateScope())
                    {

                        var service = scope.ServiceProvider.GetRequiredService<ItemService>();
                        var sum = 0;
                        sum = await service.AddItemDetailsForAuctions(batch);

                        Console.WriteLine($"Info: updated {sum} entries");
                        consumeCount.Inc(batch.Count());
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, "consuming new auctions ");
                    throw;
                }
                GC.Collect();

            }, stoppingToken, "sky-items", 100);

            await flipCons;
            logger.LogInformation("consuming ended");
        }

        private async Task Migrate()
        {
            using var scope = scopeFactory.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ItemDbContext>();
            // make sure all migrations are applied
            await context.Database.MigrateAsync();
        }

        private async Task LoadBazaar()
        {
            Models.Hypixel.BazaarResponse apiItems = await GetBazaarData();
            var tags = apiItems.Products.Keys.ToHashSet();

            using var scope = scopeFactory.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ItemDbContext>();
            var items = await context.Items.Where(i => tags.Contains(i.Tag)).ToListAsync();
            foreach (var item in items)
            {
                item.Flags |= ItemFlags.BAZAAR;
                context.Update(item);
            }
            consumeCount.Inc(items.Count / 10);
            await context.SaveChangesAsync();
        }

        private static async Task<Models.Hypixel.BazaarResponse> GetBazaarData()
        {
            var client = new RestClient("https://api.hypixel.net");
            var request = new RestRequest("/skyblock/bazaar");
            var responseJson = await client.ExecuteAsync(request);
            var apiItems = JsonConvert.DeserializeObject<Models.Hypixel.BazaarResponse>(responseJson.Content);
            return apiItems;
        }

        private async Task DownloadFromApi()
        {
            var client = new RestClient("https://api.hypixel.net");
            var request = new RestRequest("/resources/skyblock/items");
            var responseJson = await client.ExecuteAsync(request);
            var items = JsonConvert.DeserializeObject<Models.Hypixel.HypixelItems>(responseJson.Content);
            foreach (var batch in MoreLinq.Extensions.BatchExtension.Batch(items.Items, 30))
            {
                using var scope = scopeFactory.CreateScope();
                using var context = scope.ServiceProvider.GetRequiredService<ItemDbContext>();
                await UpdateApiBatch(context, batch);
                consumeCount.Inc();
            }
            logger.LogInformation("updated api items");
        }

        private static async Task UpdateApiBatch(ItemDbContext context, IEnumerable<Models.Hypixel.Item> batch)
        {
            var batchLookup = batch.Select(b => b.Id).ToList();
            var batchInternal = await context.Items.Where(i => batchLookup.Contains(i.Tag)).Include(i => i.Modifiers).ToListAsync();
            foreach (var item in batch)
            {
                var match = batchInternal.Where(i => i.Tag == item.Id).FirstOrDefault();
                if (match == null)
                {
                    match = new Item();
                    match.Modifiers = new System.Collections.Generic.HashSet<Modifiers>();
                    match.Tag = item.Id;
                    context.Add(match);
                }
                match.Name = item.Name.Truncate(44);
                if (match.Name != item.Name && !match.Modifiers.Where(m => m.Slug == "abr").Any())
                {
                    match.Modifiers.Add(new Modifiers()
                    {
                        Slug = "name",
                        Value = item.Name // save full name
                    });
                }
                var parts = match.Name.Split(' ');
                if (parts.Length > 1 && !match.Modifiers.Where(m => m.Slug == "abr").Any())
                {
                    var abr = string.Join("", parts.Select(p => p[0]));
                    match.Modifiers.Add(new Modifiers()
                    {
                        Slug = "abr",
                        Value = abr
                    });
                }
                match.NpcSellPrice = item.NpcSellPrice ?? -1;
                match.MinecraftType = item.Material;
                match.Durability = (short)item.Durability;
                if (!string.IsNullOrEmpty(item.Tier))
                    match.Tier = Enum.Parse<Tier>(item.Tier);
                if (item.Glowing ?? false)
                    match.Flags |= ItemFlags.GLOWING;
                if (item.Museum)
                    match.Flags |= ItemFlags.MUSEUM;
                if (item.Skin != null)
                {
                    try
                    {
                        var skinString = item.Skin;
                        string id;
                        try
                        {
                            id = GetId(skinString);
                        }
                        catch
                        {
                            try
                            {
                                id = GetId(skinString + "=");
                            }
                            catch
                            {
                                id = GetId(skinString + "==");
                            }
                        }
                        if (!match.Modifiers.Where(m => m.Slug == "skin").Any())
                            match.Modifiers.Add(new Modifiers()
                            {
                                Slug = "skin",
                                Type = Modifiers.DataType.STRING,
                                Value = id
                            });
                    }
                    catch (Exception)
                    {
                        Console.WriteLine(item.Id);
                        throw;
                    }

                }
                var tag = item.Id;
                if (tag.EndsWith("_PERSONALITY"))
                    match.Category = ItemCategory.MINION_SKIN;
                else if (Enum.TryParse<ItemCategory>(item.Category, true, out ItemCategory cat))
                    match.Category = cat;
                else if (item.Furniture != null)
                    match.Category = ItemCategory.FURNITURE;
                else if (item.Generator != null)
                    match.Category = ItemCategory.GENERATOR;
                else if (tag.EndsWith("_ISLAND"))
                    match.Category = ItemCategory.PRIVATE_ISLAND;
                else if (tag.EndsWith("_ISLAND_CRYSTAL"))
                    match.Category = ItemCategory.ISLAND_CRYSTAL;
                else if (tag.EndsWith("_FRAGMENT"))
                    match.Category = ItemCategory.FRAGMENT;
                else if (item.Requirements?.Any(r => r.Type == "SLAYER") ?? false)
                    match.Category = ItemCategory.SLAYER;
                else if (item.Requirements?.Any(r => r.Type == "DUNGEON_TIER") ?? false)
                    match.Category = ItemCategory.DUNGEON;
                else if (item.Requirements?.Any(r => r.Type == "HEART_OF_THE_MOUNTAIN") ?? false)
                    match.Category = ItemCategory.DEEP_CAVERNS;
                else if (item.DungeonItem ?? false)
                    match.Category = ItemCategory.DUNGEON_ITEM;
                else AssignCategory(match);
            }
            await context.SaveChangesAsync();
        }

        public static void AssignCategory(Item item)
        {
            var tag = item.Tag;
            if (tag.EndsWith("_SACK"))
                item.Category = ItemCategory.SACK;
            else if (tag.EndsWith("_PORTAL"))
                item.Category = ItemCategory.PORTAL;
            else if (tag.EndsWith("_BACKPACK"))
                item.Category = ItemCategory.BACKPACK;
            else if (tag.EndsWith("TALISMAN_ENRICHMENT"))
                item.Category = ItemCategory.TALISMAN_ENRICHMENT;
            else if (tag.Contains("_THE_FISH"))
                item.Category = ItemCategory.THE_FISH;
            else if (tag.StartsWith("PET_SKIN"))
                item.Category = ItemCategory.PET_SKIN;
            else if (tag.StartsWith("PET_ITEM"))
                item.Category = ItemCategory.PET_ITEM;
            else if (tag.StartsWith("PET_"))
                item.Category = ItemCategory.PET;
            else if (item.Tag.StartsWith("RUNE_") && item.Category != ItemCategory.RUNE)
                item.Category = ItemCategory.RUNE;
            else if (item.Tag.StartsWith("DYE_") && item.Category != ItemCategory.ArmorDye)
                item.Category = ItemCategory.ArmorDye;
            else if (item.Tag.StartsWith("PET_SKIN_") && item.Category != ItemCategory.PET_SKIN)
                item.Category = ItemCategory.PET_SKIN;
            else if (item.Tag.EndsWith("_TRAVEL_SCROLL"))
                item.Category = ItemCategory.TRAVEL_SCROLL;
            else if(item.Tag == "TRUE_WARDEN")
                item.Category = ItemCategory.COSMETIC;
        }

        private static string GetId(string skinString)
        {
            dynamic skinData = JsonConvert.DeserializeObject(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(skinString)));
            var id = ((string)skinData.textures.SKIN.url).Replace("http://textures.minecraft.net/texture/", "");
            return id;
        }

        private static async Task CopyOverItems(ItemDbContext context)
        {
            using (var db = new HypixelContext())
            {
                var items = await db.Items.Include(i => i.Names).ToListAsync();
                foreach (var dbItem in items)
                {
                    //dbItem.Names = dbItem.Names.Where(n => n.Name != null).ToList();
                    try
                    {
                        var names = dbItem.Names.Select(i => new Modifiers()
                        {
                            FoundCount = i.OccuredTimes,
                            Slug = "name",
                            Type = Modifiers.DataType.STRING,
                            Value = ItemReferences.RemoveReforgesAndLevel(i.Name)
                        }).ToHashSet();
                        var name = dbItem.Name;
                        if (names.Count() > 0)
                            name = dbItem.Names.MaxBy(i => i.OccuredTimes).Name;
                        var item = new Item()
                        {
                            Id = dbItem.Id,
                            Tag = dbItem.Tag,
                            Tier = dbItem.Tier,
                            IconUrl = dbItem.IconUrl,
                            Flags = dbItem.IsBazaar ? ItemFlags.BAZAAR : ItemFlags.AUCTION,
                            Descriptions = new System.Collections.Generic.HashSet<Description>(){new Description(){
                                Text = dbItem.Description
                            }},
                            Modifiers = names,
                            Name = name
                        };
                        context.Add(item);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(JsonConvert.SerializeObject(dbItem, Formatting.Indented));
                        throw e;
                    }

                }
                await context.SaveChangesAsync();
            }
        }

        /// <summary>
        /// Clears differences between old and new db
        /// some tags were set to 0 for an unkown reason
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        private static async Task FixItems(ItemDbContext context)
        {
            Console.WriteLine("fixing items");
            using (var db = new HypixelContext())
            {
                var items = await db.Items.ToListAsync();
                var newItems = await context.Items.ToListAsync();

                // remove any nulls
                foreach (var item in await context.Items
                            .Include(i => i.Modifiers).Include(i => i.Descriptions).AsSplitQuery()
                            .Where(i => i.Tag == null && i.Id > 3213).ToListAsync())
                {
                    try
                    {

                        context.Remove(item);
                        var deleteCount = await context.SaveChangesAsync();
                        Console.WriteLine($"Deleted {item.Tag} {deleteCount}");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Could not delete " + item.Tag);
                        throw e;
                    }
                }

                var englishRegex = new System.Text.RegularExpressions.Regex("^[a-zA-Z0-9 ]*$");
                foreach (var item in newItems)
                {
                    if (item.Tag.StartsWith("PET_") && !item.Tag.StartsWith("PET_ITEM") && !item.Tag.StartsWith("PET_SKIN"))
                    {
                        if (item.Name != null && !englishRegex.IsMatch(item.Name))
                        {
                            item.Name = null;
                            Console.WriteLine($"throwing away name for {item.Tag} {item.Name}");
                            context.Update(item);
                        }
                    }
                    AssignCategory(item);
                }
                var newItemsUpdated = await context.SaveChangesAsync();
                Console.WriteLine($"Info: Updated {newItemsUpdated} items from item db");


                foreach (var dbItem in items)
                {
                    //dbItem.Names = dbItem.Names.Where(n => n.Name != null).ToList();
                    try
                    {
                        var newItem = newItems.Where(n => n.Id == dbItem.Id).First();
                        if (newItem.Tag != null)
                            continue; // nothing to fix
                        // remove all dupplicates
                        foreach (var item in newItems.Where(i => i.Tag == dbItem.Tag && i.Id > 3213))
                        {
                            var toRemove = await context.Items
                            .Include(i => i.Modifiers).Include(i => i.Descriptions).AsSplitQuery()
                            .Where(i => i.Id == item.Id).FirstAsync();
                            context.Remove(toRemove);
                        }
                        // assign tag back
                        newItem.Tag = dbItem.Tag;
                        context.Update(newItem);

                        var count = await context.SaveChangesAsync();
                        Console.WriteLine("updated entries " + count);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Could not migrate" + JsonConvert.SerializeObject(dbItem, Formatting.Indented));
                        throw e;
                    }
                }
            }
        }

        private ItemService GetService()
        {
            return scopeFactory.CreateScope().ServiceProvider.GetRequiredService<ItemService>();
        }
    }
}