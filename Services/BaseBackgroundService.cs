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

namespace Coflnet.Sky.Items.Services
{

    public class BaseBackgroundService : BackgroundService
    {
        private IServiceScopeFactory scopeFactory;
        private IConfiguration config;
        private ILogger<BaseBackgroundService> logger;
        private Prometheus.Counter consumeCount = Prometheus.Metrics.CreateCounter("sky_base_conume", "How many messages were consumed");

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

            if (!context.Items.Any())
                await CopyOverItems(context);

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