using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Items.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using System.Linq;
using Microsoft.Extensions.Logging;
using Coflnet.Sky.Items.Controllers;
using hypixel;
using System;
using Newtonsoft.Json;
using RestSharp;

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
            using var scope = scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ItemDbContext>();
            // make sure all migrations are applied
            await context.Database.MigrateAsync();

            await DownloadFromApi(context);

            if (!context.Items.Any())
            {
                await CopyOverItems(context);
                
            }

            var flipCons = Coflnet.Kafka.KafkaConsumer.ConsumeBatch<SaveAuction>(config["KAFKA_HOST"], config["TOPICS:NEW_AUCTION"], async batch =>
            {
                await Task.Delay(1000);
                var service = GetService();
                foreach (var lp in batch)
                {
                    await service.AddItemDetailsForAuction(lp);
                }
                consumeCount.Inc(batch.Count());
            }, stoppingToken, "skybase");

            await Task.WhenAll(flipCons);
        }

        private async Task DownloadFromApi(ItemDbContext context)
        {
            var client = new RestClient("https://api.hypixel.net");
            var request = new RestRequest("/resources/skyblock/items");
            var responseJson = await client.ExecuteAsync(request);
            var items = JsonConvert.DeserializeObject<Models.Hypixel.HypixelItems>(responseJson.Content);
            foreach (var item in items.Items)
            {
                var match = await context.Items.Where(i=>i.Tag == item.Id).Include(i=>i.Modifiers).FirstOrDefaultAsync();
                match.Name = item.Name;
                match.NpcSellPrice = item.NpcSellPrice ?? -1;
                match.MinecraftType = item.Material;
                match.Durability = (short)item.Durability;
                match.Tier = Enum.Parse<Tier>(item.Tier);
                if(item.Glowing ?? false)
                    match.Flags |= ItemFlags.GLOWING;
                if(item.Museum)
                    match.Flags |= ItemFlags.MUSEUM;
                if(item.Skin != null)
                {
                    if(!match.Modifiers.Where(m=>m.Slug == "skin").Any())
                        match.Modifiers.Add(new Modifiers()
                        {
                            Slug = "skin",
                            Type = Modifiers.DataType.STRING,
                            Value = item.Skin
                        });
                }
                if(Enum.TryParse<ItemCategory>(item.Category,true, out ItemCategory cat))
                    match.Category = cat;
                else if(item.Furniture != null)
                    match.Category = ItemCategory.FURNITURE;
                else if(item.Generator != null)
                    match.Category = ItemCategory.GENERATOR;
                else if(item.Id.EndsWith("_PERSONALITY"))
                    match.Category = ItemCategory.MINION_SKIN;
                else if(item.Id.EndsWith("_ISLAND"))
                    match.Category = ItemCategory.PRIVATE_ISLAND;
                else if(item.Id.EndsWith("_ISLAND_CRYSTAL"))
                    match.Category = ItemCategory.ISLAND_CRYSTAL;
                else if(item.Id.EndsWith("_FRAGMENT"))
                    match.Category = ItemCategory.FRAGMENT;
                else if(item.Requirements?.Slayer != null)
                    match.Category = ItemCategory.SLAYER;
                else if(item.Requirements?.Dungeon != null)
                    match.Category = ItemCategory.DUNGEON;
                else if(item.Requirements?.HeartOfTheMountain != null)
                    match.Category = ItemCategory.DEEP_CAVERNS;
                else if(item.Id.EndsWith("_SACK"))
                    match.Category = ItemCategory.SACK;
                else if(item.Id.EndsWith("_PORTAL"))
                    match.Category = ItemCategory.PORTAL;
                else if(item.Id.EndsWith("_BACKPACK"))
                    match.Category = ItemCategory.BACKPACK;
                else if(item.DungeonItem ?? false)
                    match.Category = ItemCategory.DUNGEON_ITEM;
                else if(item.Id.EndsWith("TALISMAN_ENRICHMENT"))
                    match.Category = ItemCategory.TALISMAN_ENRICHMENT;
                else if(item.Id.EndsWith("THE_FISH"))
                    match.Category = ItemCategory.THE_FISH;

            }
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
                            Tag = dbItem.Tag,
                            Tier = dbItem.Tier,
                            IconUrl = dbItem.IconUrl,
                            Flags = ItemFlags.AUCTION,
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

        private ItemService GetService()
        {
            return scopeFactory.CreateScope().ServiceProvider.GetRequiredService<ItemService>();
        }
    }
}