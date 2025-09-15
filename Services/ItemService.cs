using System.Threading.Tasks;
using Coflnet.Sky.Items.Models;
using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Coflnet.Sky.Core;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;
using Newtonsoft.Json;
using MoreLinq;
using System.Text.RegularExpressions;

namespace Coflnet.Sky.Items.Services
{
    public class ItemMetaStorage
    {
        public ConcurrentQueue<string> ToTrimTags { get; set; } = new();
        public Dictionary<string, HashSet<string>> ModifiersCache { get; set; } = new();
        public DateTime LastUpdate { get; set; } = DateTime.MinValue;
    }
    public class ItemService
    {
        private ItemDbContext db;
        private ILogger<ItemService> logger;
        private static HashSet<string> irrelevantMod = new() { "uid", "uuid", "exp", "spawnedFor", "bossId", "uniqueId" };

        public static IEnumerable<string> IgnoredSlugs => irrelevantMod;
        private ItemMetaStorage storage;

        public ItemService(ItemDbContext db, ILogger<ItemService> logger, ItemMetaStorage toTrimQueue)
        {
            this.db = db;
            this.logger = logger;
            this.storage = toTrimQueue;
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

                    ConcurrentDictionary<(string, string, string), int> occurences = new();
                    ConcurrentDictionary<(string, string), int> descriptions = new();
                    foreach (var auction in itemsSample)
                    {
                        foreach (var nbt in auction.FlatenedNBT)
                        {
                            var key = (auction.Tag, nbt.Key, nbt.Value);
                            if (!irrelevantMod.Contains(nbt.Key))
                                occurences.AddOrUpdate(key, k => 1, (k, v) => v + 1);
                            else
                            {
                                // only store that the attribute exists on that item
                                var isPresentKey = (auction.Tag, nbt.Key, "exists");
                                occurences.AddOrUpdate(isPresentKey, k => 1, (k, v) => v + 1);
                            }
                        }
                        if (auction.NbtData.Data.ContainsKey("necromancer_souls"))
                        {
                            try
                            {
                                var heldSouls = auction.NbtData.Data["necromancer_souls"] as List<dynamic>;
                                foreach (var item in heldSouls)
                                {
                                    occurences.AddOrUpdate((auction.Tag, "necromancer_souls", item.mob_id.ToString()), k => 1, (k, v) => v + 1);
                                }
                            }
                            catch (Exception e)
                            {
                                logger.LogError(e, "failed to parse necromancer souls {souls}", JsonConvert.SerializeObject(auction.NbtData.Data["necromancer_souls"]));
                            }
                        }
                        foreach (var ench in auction.Enchantments)
                        {
                            var key = (auction.Tag, "!ench" + ench.Type.ToString().ToLower(), ench.Level.ToString());
                            occurences.AddOrUpdate(key, k => 1, (k, v) => v + 1);
                        }
                        occurences.AddOrUpdate((auction.Tag, "reforge", auction.Reforge.ToString()), k => 1, (k, v) => v + 1);
                        occurences.AddOrUpdate((auction.Tag, "count", auction.Count.ToString()), k => 1, (k, v) => v + 1);
                        occurences.AddOrUpdate((auction.Tag, "tier", auction.Tier.ToString()), k => 1, (k, v) => v + 1);
                        occurences.AddOrUpdate((auction.Tag, "name", ItemReferences.RemoveReforgesAndLevel(auction.ItemName)), k => 1, (k, v) => v + 1);

                        var descKey = (auction.Tag, auction.Context["lore"]);
                        descriptions.AddOrUpdate(descKey, k => 1, (k, v) => v + 1);
                    }
                    var selectTags = occurences.Keys.Select(o => o.Item1).ToHashSet();
                    var selectSlugs = occurences.Keys.Select(o => o.Item2).ToHashSet();
                    var selectValues = occurences.Keys.Select(o => o.Item3).ToHashSet();
                    var toUpdateList = await db.Modifiers.Where(m => selectTags.Contains(m.Item.Tag) && selectSlugs.Contains(m.Slug) && selectValues.Contains(m.Value))
                                        .Include(m => m.Item).ToListAsync();
                    var toDelete = toUpdateList.GroupBy(m => (m.Item.Tag, m.Slug, m.Value)).Where(g => g.Count() > 1).Select(g => g.OrderByDescending(v => v.Id).First()).ToHashSet();
                    foreach (var item in toDelete)
                    {
                        db.Modifiers.Remove(item);
                        logger.LogInformation($"Remove dupplicate {item.Item.Tag} {item.Slug} {item.Value}");
                    }
                    count += await db.SaveChangesAsync();

                    var toUpdate = toUpdateList.ToDictionary(e => (e.Item.Tag, e.Slug, e.Value));
                    await PerformUpdate(occurences, toUpdate);
                    count += await db.SaveChangesAsync();
                    await AddDescriptionsIfNotExisting(descriptions);
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

        private async Task AddDescriptionsIfNotExisting(ConcurrentDictionary<(string, string), int> descriptions)
        {
            var itemTags = descriptions.Keys.Select(d => d.Item1).ToHashSet();
            var descDict = descriptions.GroupBy(d => d.Key.Item1).ToDictionary(g => g.Key);
            var toAdd = await db.Items.Where(i => itemTags.Contains(i.Tag) && i.Descriptions.Count == 0).Include(i => i.Descriptions).ToListAsync();
            foreach (var item in toAdd)
            {
                var match = descDict[item.Tag].OrderByDescending(d => d.Value).FirstOrDefault();
                if (match.Key.Item1 == null)
                    continue;
                item.Descriptions.Add(new Description()
                {
                    Item = item,
                    Text = match.Key.Item2,
                    Occurences = match.Value
                });
            }
        }

        public async Task TrimModifiers()
        {
            if (storage.ToTrimTags.TryDequeue(out string itemTag))
            {
                try
                {
                    await TrimModifiers(itemTag);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "failed to trim modifiers for {tag}", itemTag);
                }
            }
        }

        private async Task TrimModifiers(string itemTag)
        {
            var select = db.Items.Where(i => i.Tag == itemTag).Include(i => i.Modifiers).SelectMany(i => i.Modifiers);
            var allMods = await select.Where(v => v.Value != null)
                        .GroupBy(m => new { m.Slug, m.Value })
                        .Select(i => new { i.Key, occured = i.Sum(m => m.FoundCount) })
                        .ToListAsync();
            var toTrim = allMods.GroupBy(m => m.Key.Slug).Where(m => m.Count() > 150 && m.All(i =>i.Key.Value == "exists" || float.TryParse(i.Key.Value, out _))).ToList();
            foreach (var group in toTrim)
            {
                var max = group.Max(i => float.Parse(i.Key.Value));
                var min = group.Min(i => float.Parse(i.Key.Value));
                foreach (var item in group.OrderByDescending(i => i.occured).Skip(148).Take(5))
                {
                    if (float.Parse(item.Key.Value) == max || float.Parse(item.Key.Value) == min)
                        continue;
                    var element = select.Where(m => m.Slug == item.Key.Slug && m.Value == item.Key.Value).FirstOrDefault();
                    db.Modifiers.Remove(element);
                    Console.WriteLine($"Removed {item.Key.Slug} {item.Key.Value} {item.occured}");
                }
                await db.SaveChangesAsync();
            }
            var uuids = allMods.Where(m => m.Key.Slug.EndsWith("uuid")).ToList();
            foreach (var toRemove in uuids.Batch(20))
            {
                foreach (var element in toRemove)
                {
                    var concrete = await select.Where(m => m.Slug == element.Key.Slug && m.Value == element.Key.Value).FirstOrDefaultAsync();
                    db.Modifiers.Remove(concrete);
                }
                logger.LogInformation("Removed {count} uuids, {key}", toRemove.Count(), toRemove.First().Key.Slug);
                await db.SaveChangesAsync();
            }
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
                else if (item.Key.Item2.EndsWith("uuid"))
                {
                    // ignore uuids
                }
                else
                {
                    mod = new Modifiers()
                    {
                        FoundCount = 1,
                        Item = await db.Items.Where(i => i.Tag == item.Key.Item1).FirstOrDefaultAsync(),
                        Slug = item.Key.Item2,
                        Value = item.Key.Item3.Truncate(150),

                        Type = long.TryParse(item.Key.Item3, out _) ? Modifiers.DataType.LONG : Modifiers.DataType.STRING
                    };
                    db.Modifiers.Add(mod);
                    Console.WriteLine("added new val for " + item.Key);
                    if (item.Key.Item3.Length > 150)
                        logger.LogWarning($"Value {item.Key.Item3} way too long");
                }
            }
        }

        private async Task<int> AddNewAuctionsIfAny(IEnumerable<SaveAuction> auctions, HashSet<string> tags)
        {
            var items = await db.Items.Where(i => tags.Contains(i.Tag)).Select(i => i.Tag).AsNoTracking().ToListAsync();
            foreach (var auction in auctions.ExceptBy(items, i => i.Tag))
            {
                var item = new Models.Item()
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
                await UpdateCategoryBasedOnDescription(auctions);
                await UpdateItemsOnAh(tags);
            }
            return count;
        }

        private async Task UpdateCategoryBasedOnDescription(IEnumerable<SaveAuction> auctions)
        {
            var categoryFor = new Dictionary<string, ItemCategory>();
            foreach (var auction in auctions.GroupBy(a => a.Tag).Select(g => g.First()))
            {
                if (!auction.Context.TryGetValue("lore", out string lore))
                {
                    continue;
                }
                if (lore.Contains("MEMENTO"))
                {
                    categoryFor[auction.Tag] = ItemCategory.MEMENTO;
                }
            }
            foreach (var item in await db.Items.Where(i => i.Category == ItemCategory.UNKNOWN && categoryFor.Keys.Contains(i.Tag)).ToListAsync())
            {
                item.Category = categoryFor[item.Tag];
                db.Update(item);
                logger.LogInformation("Updated category for {item} to {category}", item.Tag, item.Category);
            }
            await db.SaveChangesAsync();
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
                    logger.LogError("failed to set is on ah flag on " + item.Tag);
                }
            }
        }

        private void AddItemDetailsForAuction(SaveAuction auction, List<Models.Item> itemsWithDetails)
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

        private void UpdateModifiers(SaveAuction auction, Models.Item item)
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

        internal async Task<Dictionary<string, HashSet<string>>> GetAllModifiersAsync(string itemTag, bool force = false)
        {
            IQueryable<Modifiers> select = db.Items.Where(i => i.Tag == itemTag).Include(i => i.Modifiers).SelectMany(i => i.Modifiers);
            if (itemTag == "*")
            {
                if (storage.LastUpdate > DateTime.UtcNow.AddHours(-2) && !force)
                    return storage.ModifiersCache;
                var extraIgnore = new string[] { "initiator_player", "abr", "name", "recipient_id", "recipient_name", "alias", "players_clicked", "player" };
                var toIgnore = new HashSet<string>(ItemService.IgnoredSlugs.Concat(extraIgnore));
                select = db.Modifiers.Where(m => !toIgnore.Contains(m.Slug) && !EF.Functions.Like(m.Slug, "%uuid"));

            }
            var allMods = await select.Where(v => v.Value != null)
                        .GroupBy(m => new { m.Slug, m.Value })
                        .Select(i => new { i.Key, occured = i.Sum(m => m.FoundCount) })
                        .ToListAsync();

            if (itemTag != "*")
            {
                var any = allMods.GroupBy(m => m.Key.Slug).Where(m => m.Count() > 150 && m.All(i => int.TryParse(i.Key.Value, out _)) || m.Key.EndsWith("uuid")).ToList();
                if (any.Count > 0)
                    storage.ToTrimTags.Enqueue(itemTag);
            }

            var result = allMods.GroupBy(m => m.Key.Slug.StartsWith("!ench") ? m.Key.Slug.ToLower() : m.Key.Slug).ToDictionary(m => m.Key, m =>
            {
                var ordered = m
                    .OrderBy(m => int.TryParse(m.Key.Value, out int v)
                    ? (v < 10 ? v - 10_000_000 : 10 - m.Key.Value.Length - v / 1000)
                    : (m.Key.Value.Length - m.occured)).Select(m => m.Key.Value);
                return ordered.Take(189).Append(ordered.Last()).ToHashSet();
            });
            if (itemTag == "*")
            {
                storage.ModifiersCache = result;
                storage.LastUpdate = DateTime.UtcNow;
            }
            if (force)
            {
                foreach (var item in allMods.GroupBy(m => m.Key.Slug).Where(m => m.Count() > 850))
                {
                    Console.WriteLine($"Slug {item.Key} has {item.Count()} values on {itemTag}");
                }
                var tooMany = allMods.GroupBy(m => m.Key.Slug).Where(m => m.Count() > 1250).ToList();
                if (tooMany.Count() > 1)
                {
                    foreach (var item in tooMany)
                    {
                        var key = item.Key;
                        // group modifiers by item and keep only the numeric min and max per item, delete the rest
                        var modsWithItem = await db.Modifiers.Where(m => m.Slug == key).Include(m => m.Item).Take(20_000).ToListAsync();
                        var toRemove = new List<Modifiers>();

                        foreach (var group in modsWithItem.Where(m => m.Item != null).GroupBy(m => m.Item.Id))
                        {
                            var numeric = group
                                .Where(m => float.TryParse(m.Value, out _))
                                .Select(m => new { Mod = m, Val = float.Parse(m.Value) })
                                .OrderBy(x => x.Val)
                                .ToList();

                            if (numeric.Count <= 2)
                                continue;

                            var keepIds = new HashSet<int> { numeric.First().Mod.Id, numeric.Last().Mod.Id };

                            foreach (var entry in numeric)
                            {
                                if (!keepIds.Contains(entry.Mod.Id))
                                    toRemove.Add(entry.Mod);
                            }
                        }

                        if (toRemove.Count > 0)
                        {
                            db.Modifiers.RemoveRange(toRemove);
                            await db.SaveChangesAsync();
                            logger.LogInformation("Removed {count} modifiers for slug {slug}", toRemove.Count, key);
                        }
                    }
                }
            }
            return result;
        }

        internal async Task UpdateAliases()
        {
            foreach (var item in db.Items.Select(i => new { i.Name, i.Tag, i.Id }).Where(i=>i.Name != null).ToList())
            {
                if (!Regex.IsMatch(item.Name, @"\d\d\d"))
                    continue;
                var dbItem = await db.Items.Where(i => i.Id == item.Id)
                        .Include(i => i.Modifiers).FirstAsync();
                var numberpart = Regex.Match(item.Name, @"\d\d\d+").Value;
                if(dbItem.Modifiers.Any(m=>m.Slug == "alias" && m.Value == numberpart))
                    continue;
                dbItem.Modifiers.Add(new Modifiers()
                {
                    Slug = "alias",
                    Value = numberpart
                });
                logger.LogInformation("Alias for {tag} is {alias}", item.Tag, numberpart);
                await db.SaveChangesAsync();
            }
        }
    }
}
