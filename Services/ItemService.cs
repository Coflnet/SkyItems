using System.Threading.Tasks;
using Coflnet.Sky.Items.Models;
using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Items.Services
{
    public class ItemService
    {
        private ItemDbContext db;

        public ItemService(ItemDbContext db)
        {
            this.db = db;
        }

        public async Task<int> AddItemDetailsForAuction(SaveAuction auction)
        {
            var tag = auction.Tag;
            var item = await db.Items.Where(i => i.Tag == tag).FirstOrDefaultAsync();
            if (item == null)
            {
                item = new Item()
                {
                    Flags = ItemFlags.AUCTION,
                    Name = auction.ItemName,
                    Tag = tag,
                    Tier = auction.Tier
                };
                Enum.TryParse(auction.Category.ToString(), out ItemCategory category);
                item.Category = category;
                db.Add(item);
                await db.SaveChangesAsync();
            }
            // sample 
            if (auction.UId % 5 != 1)
                return 0;

            item = await db.Items.Where(i => i.Tag == tag).Include(i => i.Modifiers).Include(i => i.Descriptions).AsSplitQuery().FirstOrDefaultAsync();
            UpdateModifiers(auction, item);
            var name = ItemReferences.RemoveReforgesAndLevel(auction.ItemName);
            var nameProp = item.Modifiers.Where(m => m.Slug == "name" && m.Value == name).FirstOrDefault();
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
                db.Update(nameProp);
            }

            if (auction.UId % 5 != 1 || !auction.Context.ContainsKey("lore"))
            {
                return await db.SaveChangesAsync();
            }
            var text = auction.Context["lore"];
            var descMatch = item.Descriptions.Where(d => d.Text == text).FirstOrDefault();
            if (descMatch != null)
            {
                descMatch.Occurences++;
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
            return await db.SaveChangesAsync();
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
