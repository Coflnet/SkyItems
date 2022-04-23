using System.Threading.Tasks;
using Coflnet.Sky.Items.Models;
using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Coflnet.Sky.Core;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Items.Services
{
    public class ItemService
    {
        private ItemDbContext db;
        private ILogger<ItemService> logger;

        public ItemService(ItemDbContext db, ILogger<ItemService> logger)
        {
            this.db = db;
            this.logger = logger;
        }

        public async Task<int> AddItemDetailsForAuctions(IEnumerable<SaveAuction> auctions)
        {
            var tags = auctions.Select(a => a.Tag).Distinct().ToHashSet();
            var items = await db.Items.Where(i => tags.Contains(i.Tag)).Select(i => i.Tag).ToListAsync();
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
                else if (auction.Tag.StartsWith("PET_"))
                    item.Category = ItemCategory.PET;
                db.Add(item);
                Console.WriteLine("adding item " + item.Tag);
            }
            var count = await db.SaveChangesAsync();

            for (int i = 0; i < 3; i++)
                try
                {
                    var itemsSample = auctions.Take(auctions.Count() / 2);
                    var sampleTags = itemsSample.Select(i => i.Tag).ToHashSet();
                    var itemsWithDetails = await db.Items.Where(i => sampleTags.Contains(i.Tag))
                        .Include(i => i.Modifiers)
                        .Include(i => i.Descriptions)
                        .AsSplitQuery().ToListAsync();
                    foreach (var auction in itemsSample)
                    {
                        AddItemDetailsForAuction(auction, itemsWithDetails);
                    }
                    return count + await db.SaveChangesAsync();
                }
                catch (Exception e)
                {
                    if (i > 0)
                        logger.LogError(e, "saving batch sample");
                    await Task.Delay(new Random().Next(100, 60_000));
                    if (i == 2)
                        logger.LogInformation("giving up retry");
                }

            return count;
        }

        private async void AddItemDetailsForAuction(SaveAuction auction, List<Item> itemsWithDetails)
        {
            var tag = auction.Tag;
            var item = itemsWithDetails.Where(i => i.Tag == tag).FirstOrDefault();
            UpdateModifiers(auction, item);
            var name = ItemReferences.RemoveReforgesAndLevel(auction.ItemName);
            var nameProp = item.Modifiers.Where(m => m.Slug == "name" && m.Value == name).FirstOrDefault();
            // this has been seen on auction now
            if(!item.Flags.HasFlag(ItemFlags.AUCTION))
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
