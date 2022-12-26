using System.Threading.Tasks;
using Coflnet.Sky.Items.Models;
using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Coflnet.Sky.Core;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Coflnet.Sky.Items.Services
{
    public class ItemService
    {
        private ItemDbContext db;
        private ILogger<ItemService> logger;
        private static HashSet<string> irrelevantMod = new() { "uid", "uuid", "exp", "spawnedFor", "bossId" };

        public static IEnumerable<string> IgnoredSlugs => irrelevantMod;

        public ItemService(ItemDbContext db, ILogger<ItemService> logger)
        {
            this.db = db;
            this.logger = logger;
        }

        public async Task<int> AddItemDetailsForAuctions(IEnumerable<SaveAuction> auctions)
        {
            var tags = auctions.Select(a => a.Tag).Distinct().ToHashSet();
            int count = await AddNewAuctionsIfAny(auctions, tags);

            for (int i = 0; i < 3; i++)
                try
                {
                    var itemsSample = auctions.Take(auctions.Count());
                    var sampleTags = itemsSample.Select(i => i.Tag).ToHashSet();
                    /*var itemsWithDetails = await db.Items.Where(i => sampleTags.Contains(i.Tag))
                        .Include(i => i.Modifiers)
                        .Include(i => i.Descriptions)
                        .AsSplitQuery().ToListAsync();
                    logger.LogInformation($"Loaded entries from db: " + itemsWithDetails.Sum(i => i.Modifiers.Count + i.Descriptions.Count));
                    foreach (var auction in itemsSample)
                    {
                        AddItemDetailsForAuction(auction, itemsWithDetails);
                    }*/

                    ConcurrentDictionary<(string, string, string), int> occurences = new();
                    ConcurrentDictionary<(string, string), int> descriptions = new();
                    foreach (var auction in itemsSample)
                    {
                        foreach (var nbt in auction.FlatenedNBT)
                        {
                            var key = (auction.Tag, nbt.Key, nbt.Value);
                            if (!irrelevantMod.Contains(nbt.Key))
                                occurences.AddOrUpdate(key, k => 1, (k, v) => v + 1);
                        }
                        foreach (var ench in auction.Enchantments)
                        {
                            var key = (auction.Tag, "!ench" + ench.Type.ToString(), ench.Level.ToString());
                            occurences.AddOrUpdate(key, k => 1, (k, v) => v + 1);
                        }
                        occurences.AddOrUpdate((auction.Tag, "reforge", auction.Reforge.ToString()), k => 1, (k, v) => v + 1);
                        occurences.AddOrUpdate((auction.Tag, "name", ItemReferences.RemoveReforgesAndLevel(auction.ItemName)), k => 1, (k, v) => v + 1);

                        var descKey = (auction.Tag, auction.Context["lore"]);
                        descriptions.AddOrUpdate(descKey, k => 1, (k, v) => v + 1);
                    }
                    var selectTags = occurences.Keys.Select(o => o.Item1).ToHashSet();
                    var selectSlugs = occurences.Keys.Select(o => o.Item2).ToHashSet();
                    var selectValues = occurences.Keys.Select(o => o.Item3).ToHashSet();
                    var toUpdateList = await db.Modifiers.Where(m => selectTags.Contains(m.Item.Tag) && selectSlugs.Contains(m.Slug) && selectValues.Contains(m.Value))
                                        .Include(m => m.Item).ToListAsync();
                    var toDelete = toUpdateList.GroupBy(m => (m.Item.Tag, m.Slug, m.Value)).Where(g => g.Count() > 1).Select(g => g.OrderByDescending(v=>v.Id).First()).ToHashSet();
                    foreach (var item in toDelete)
                    {
                        db.Modifiers.Remove(item);
                        logger.LogInformation($"Remove dupplicate {item.Item.Tag} {item.Slug} {item.Value}");
                    }
                    count += await db.SaveChangesAsync();

                    var toUpdate = toUpdateList.ToDictionary(e => (e.Item.Tag, e.Slug, e.Value));
                    await PerformUpdate(occurences, toUpdate);
                    // todo update descriptions

                    return count + await db.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    logger.LogError($"concurrency expcetpion of {ex.Entries.Count} count");
                    await Task.Delay(new Random().Next(100, 10_000));
                }
                catch (Exception e)
                {
                    if (i == 2)
                    {
                        logger.LogInformation("giving up retry");
                        return count;
                    }
                    if (i > 0)
                        logger.LogError(e, "saving batch sample");
                    await Task.Delay(new Random().Next(100, 2_000));
                }

            return count;
        }

        private async Task PerformUpdate(ConcurrentDictionary<(string, string, string), int> occurences, Dictionary<(string Tag, string Slug, string Value), Modifiers> toUpdate)
        {
            Console.WriteLine("toupdate size " + toUpdate.Count);
            foreach (var item in occurences)
            {
                if (toUpdate.TryGetValue(item.Key, out Modifiers mod))
                {
                    mod.FoundCount += item.Value;
                }
                else
                {
                    mod = new Modifiers()
                    {
                        FoundCount = 1,
                        Item = await db.Items.Where(i => i.Tag == item.Key.Item1).FirstOrDefaultAsync(),
                        Slug = item.Key.Item2,
                        Value = item.Key.Item3,

                        Type = long.TryParse(item.Key.Item3, out _) ? Modifiers.DataType.LONG : Modifiers.DataType.STRING
                    };
                    db.Modifiers.Add(mod);
                    Console.WriteLine("added new val for " + item.Key);
                }

            }
        }

        private async Task<int> AddNewAuctionsIfAny(IEnumerable<SaveAuction> auctions, HashSet<string> tags)
        {
            var items = await db.Items.Where(i => tags.Contains(i.Tag)).Select(i => i.Tag).AsNoTracking().ToListAsync();
            foreach (var auction in auctions.ExceptBy(items, i => i.Tag))
            {
                var item = new Item()
                {
                    Flags = ItemFlags.AUCTION,
                    Name = auction.ItemName,
                    Tag = auction.Tag,
                    Tier = auction.Tier
                };
                if (Enum.TryParse(auction.Category.ToString(), out ItemCategory category))
                    item.Category = category;
                else
                    BaseBackgroundService.AssignCategory(item);
                db.Add(item);
                Console.WriteLine("adding item " + item.Tag);
            }
            var count = await db.SaveChangesAsync();
            if (Random.Shared.Next() % 10 == 0)
            {
                await UpdateItemsOnAh(tags);
            }
            return count;
        }

        private async Task UpdateItemsOnAh(HashSet<string> tags)
        {
            var onAh = await db.Items.Where(i => tags.Contains(i.Tag) && !i.Flags.HasFlag(ItemFlags.AUCTION)).ToListAsync();
            foreach (var item in onAh)
            {
                try
                {
                    item.Flags |= ItemFlags.AUCTION;
                    logger.LogInformation(item.Tag + " is on ah");
                    await db.SaveChangesAsync();
                }
                catch (System.Exception)
                {
                    logger.LogError("failed to set flag on " + item.Tag);
                }
            }
        }

        private void AddItemDetailsForAuction(SaveAuction auction, List<Item> itemsWithDetails)
        {
            var tag = auction.Tag;
            var item = itemsWithDetails.Where(i => i.Tag == tag).FirstOrDefault();
            UpdateModifiers(auction, item);
            var name = ItemReferences.RemoveReforgesAndLevel(auction.ItemName);
            var nameProp = item.Modifiers.Where(m => m.Slug == "name" && m.Value == name).FirstOrDefault();
            // this has been seen on auction now
            if (!item.Flags.HasFlag(ItemFlags.AUCTION))
                item.Flags |= ItemFlags.AUCTION;

            if (nameProp == null)
            {
                nameProp = new Modifiers()
                {
                    Slug = "name",
                    Value = name,
                    Type = Modifiers.DataType.STRING
                };
                item.Modifiers.Add(nameProp);
                db.Update(item);
            }
            else
            {
                nameProp.FoundCount++;
                if (nameProp.Id != 0)
                    db.Update(nameProp);
            }

            if (auction.UId % 5 != 1 || !auction.Context.ContainsKey("lore"))
            {
                return;
            }
            var text = auction.Context["lore"];
            var descMatch = item.Descriptions.Where(d => d.Text == text).FirstOrDefault();
            if (descMatch != null)
            {
                descMatch.Occurences++;
                if (descMatch.Id != 0)
                    db.Update(descMatch);
            }
            else
            {
                descMatch = new Description()
                {
                    Item = item,
                    Text = text
                };
                item.Descriptions.Add(descMatch);
                db.Add(descMatch);
            }
        }

        private void UpdateModifiers(SaveAuction auction, Item item)
        {
            foreach (var nbtField in auction.FlatenedNBT)
            {
                var list = item.Modifiers.Where(m => m.Slug == nbtField.Key).ToList();
                var value = list.Where(m => m.Value == nbtField.Value).FirstOrDefault();
                if (value != null)
                {
                    value.FoundCount++;
                    if (value.Id != 0)
                        db.Update(value);
                    continue;
                }
                if (list.Count >= 200)
                {
                    var neverReoccured = list.Where(l => l.FoundCount == 0).FirstOrDefault();
                    if (neverReoccured == null)
                        continue; // too many values already

                    db.Remove(neverReoccured);
                }
                value = new Modifiers()
                {
                    Slug = nbtField.Key,
                    Value = nbtField.Value,
                    Type = long.TryParse(nbtField.Value, out _) ? Modifiers.DataType.LONG : Modifiers.DataType.STRING
                };
                item.Modifiers.Add(value);
                db.Add(value);
                db.Update(item);
            }
        }
    }
}
