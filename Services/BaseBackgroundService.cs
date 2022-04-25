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
                return;
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
                    await Task.Delay(100);

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
                match.Name = item.Name;
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
                if (item.Id.EndsWith("_PERSONALITY"))
                    match.Category = ItemCategory.MINION_SKIN;
                else if (Enum.TryParse<ItemCategory>(item.Category, true, out ItemCategory cat))
                    match.Category = cat;
                else if (item.Furniture != null)
                    match.Category = ItemCategory.FURNITURE;
                else if (item.Generator != null)
                    match.Category = ItemCategory.GENERATOR;
                else if (item.Id.EndsWith("_ISLAND"))
                    match.Category = ItemCategory.PRIVATE_ISLAND;
                else if (item.Id.EndsWith("_ISLAND_CRYSTAL"))
                    match.Category = ItemCategory.ISLAND_CRYSTAL;
                else if (item.Id.EndsWith("_FRAGMENT"))
                    match.Category = ItemCategory.FRAGMENT;
                else if (item.Requirements?.Slayer != null)
                    match.Category = ItemCategory.SLAYER;
                else if (item.Requirements?.Dungeon != null)
                    match.Category = ItemCategory.DUNGEON;
                else if (item.Requirements?.HeartOfTheMountain != null)
                    match.Category = ItemCategory.DEEP_CAVERNS;
                else if (item.Id.EndsWith("_SACK"))
                    match.Category = ItemCategory.SACK;
                else if (item.Id.EndsWith("_PORTAL"))
                    match.Category = ItemCategory.PORTAL;
                else if (item.Id.EndsWith("_BACKPACK"))
                    match.Category = ItemCategory.BACKPACK;
                else if (item.DungeonItem ?? false)
                    match.Category = ItemCategory.DUNGEON_ITEM;
                else if (item.Id.EndsWith("TALISMAN_ENRICHMENT"))
                    match.Category = ItemCategory.TALISMAN_ENRICHMENT;
                else if (item.Id.EndsWith("THE_FISH"))
                    match.Category = ItemCategory.THE_FISH;
                else if (item.Id.StartsWith("PET_SKIN"))
                    match.Category = ItemCategory.PET_SKIN;
                else if (item.Id.StartsWith("PET_"))
                    match.Category = ItemCategory.PET;
            }
            await context.SaveChangesAsync();
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
            using (var db = new HypixelContext())
            {
                var items = await db.Items.ToListAsync();
                var newItems = await context.Items.ToListAsync();
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
                            context.Remove(item);
                        }
                        // assign tag back
                        newItem.Tag = dbItem.Tag;
                        context.Update(newItem);

                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(JsonConvert.SerializeObject(dbItem, Formatting.Indented));
                        throw e;
                    }

                }
                foreach (var item in newItems.Where(i => i.Tag == null && i.Id > 3213))
                {
                    context.Remove(item);
                }
                var count = await context.SaveChangesAsync();
                Console.WriteLine("updated entries " + count);
            }
        }

        private ItemService GetService()
        {
            return scopeFactory.CreateScope().ServiceProvider.GetRequiredService<ItemService>();
        }
    }
}