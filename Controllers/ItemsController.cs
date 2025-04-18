using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Coflnet.Sky.Items.Models;
using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using System.Collections;
using System.Collections.Generic;
using Coflnet.Sky.Items.Services;
using System.Text.RegularExpressions;

namespace Coflnet.Sky.Items.Controllers
{
    /// <summary>
    /// Main Controller handling tracking
    /// </summary>
    [ApiController]
    [Route("[controller]")]
    public class ItemsController : ControllerBase
    {
        private readonly ItemService service;
        private readonly ItemDbContext context;
        private readonly ILogger<ItemsController> logger;
        private readonly ItemMetaStorage toTrimQueue;

        /// <summary>
        /// Creates a new instance of <see cref="ItemsController"/>
        /// </summary>
        /// <param name="service"></param>
        /// <param name="context"></param>
        /// <param name="logger"></param>
        /// <param name="toTrimQueue"></param>
        public ItemsController(ItemService service, ItemDbContext context, ILogger<ItemsController> logger, ItemMetaStorage toTrimQueue)
        {
            this.service = service;
            this.context = context;
            this.logger = logger;
            this.toTrimQueue = toTrimQueue;
        }

        /// <summary>
        /// Returns all items in a category
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("category/{category}/items")]
        [ResponseCache(Duration = 3600 / 2, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<IEnumerable<string>> GetItemsForCategory(ItemCategory category)
        {
            if (category == ItemCategory.NullNamed)
                category = ItemCategory.Vanilla;
            return await context.Items.Where(c => c.Category == category).Select(i => i.Tag).ToListAsync();
        }

        /// <summary>
        /// Returns all available categories
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("categories")]
        [ResponseCache(Duration = 3600 * 6, Location = ResponseCacheLocation.Any, NoStore = false)]
        public IEnumerable<ItemCategory> AddDetailsForAuction()
        {
            return Enum.GetValues<ItemCategory>();
        }
        /// <summary>
        /// All tags of items on the bazaar
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any, NoStore = false)]
        [Route("bazaar/tags")]
        public async Task<IEnumerable<string>> BazaarItems()
        {
            return await context.Items.Where(i => i.Flags.HasFlag(ItemFlags.BAZAAR)).Select(i => i.Tag).ToListAsync();
        }

        /// <summary>
        /// All known items
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any, NoStore = false)]
        [Route("")]
        public async Task<IEnumerable<Item>> GetAllItems()
        {
            return await context.Items.ToListAsync();
        }

        /// <summary>
        /// Tags to item ids maping
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [ResponseCache(Duration = 600, Location = ResponseCacheLocation.Any, NoStore = false)]
        [Route("ids")]
        public async Task<Dictionary<string, int>> InternalIds()
        {
            return await context.Items.Select(i => new { i.Tag, i.Id }).Where(i => i.Tag != null).ToDictionaryAsync(i => i.Tag, i => i.Id);
        }
        /// <summary>
        /// Tags to item ids maping
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [ResponseCache(Duration = 1800, Location = ResponseCacheLocation.Any, NoStore = false)]
        [Route("/item/{itemTag}/modifiers/all")]
        public async Task<Dictionary<string, HashSet<string>>> Modifiers(string itemTag)
        {
            return await service.GetAllModifiersAsync(itemTag);
        }
        /// <summary>
        /// modifiers for a specific item
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [ResponseCache(Duration = 1800, Location = ResponseCacheLocation.Any, NoStore = false)]
        [Route("/item/{itemTag}/modifiers/m/{slug}")]
        public async Task<List<string>> Modifiers(string itemTag, string slug)
        {
            return await context.Modifiers.Where(m => m.Item == context.Items.Where(i => i.Tag == itemTag).FirstOrDefault() && m.Slug == slug).OrderByDescending(i => i.FoundCount).Select(i => i.Value).ToListAsync();
        }

        /// <summary>
        /// Searches for an item
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("search/{term}")]
        [ResponseCache(Duration = 10, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<IEnumerable<SearchResult>> Search(string term, int count = 20)
        {
            var select = GetSelectForQueryTerm(term);
            var prospects = await select
                    .Take(count * 3)
                    .Select(i => new
                    {
                        Name = i.Name,
                        CommonName = i.Modifiers.Where(m => (m.Slug == "name" || m.Slug == "alias")&& m.Value != null)
                        .OrderByDescending(m => m.FoundCount)
                        .Select(m => m.Value).FirstOrDefault(),
                        i.Tag,
                        i.Flags,
                        i.Tier
                    })
                    .AsSplitQuery()
                    .ToListAsync();
            return prospects.Select(item =>
            {
                return new SearchResult()
                {
                    Tag = item.Tag,
                    // manual name takes priority over most common
                    Text = ImproveName(item.Name != null ? item.Name : item.CommonName, item.Tag),
                    Flags = item.Flags,
                    Tier = item.Tier
                };
            });
        }

        private string ImproveName(string fullName, string tag)
        {
            if (fullName == null)
                return null;
            if (tag == "GOD_POTION")
                return "God Potion (Legacy)";
            var noSpecialChars = fullName.Trim('✪').Replace("✦", "");
            if (fullName.Contains("Rune"))
                noSpecialChars = noSpecialChars.TrimEnd('I').TrimEnd();
            var words = tag.Replace("STARRED_", "").Split('_');
            if (!words.Any(w => Core.ItemReferences.reforges.Contains(w.ToLower())))
                noSpecialChars = Core.ItemReferences.RemoveReforgesAndLevel(noSpecialChars);
            if (tag.StartsWith("STARRED_"))
                noSpecialChars = "⚚ " + noSpecialChars;
            return System.Text.RegularExpressions.Regex.Replace(noSpecialChars, @"\[Lvl \d{1,3}\] ", "").Trim(); ;
        }

        /// <summary>
        /// Retrieves the item id for an item
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("search/{term}/id")]
        [ResponseCache(Duration = 10, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<int> GetId(string term)
        {
            IOrderedQueryable<Item> select = GetSelectForQueryTerm(term);
            return await select
                    .Take(2)
                    .Select(i => i.Id).FirstOrDefaultAsync();
        }

        /// <summary>
        /// Gets a list of the newest items on ah
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("items/ah/new")]
        [ResponseCache(Duration = 600, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<IEnumerable<ItemPreview>> GetNewAhItems()
        {
            return await context.Items.Where(i => i.Flags.HasFlag(ItemFlags.AUCTION) || i.Flags.HasFlag(ItemFlags.BAZAAR))
                    .OrderByDescending(o => o.Id)
                    .Select(i => new ItemPreview()
                    {
                        Name = i.Name ?? i.Tag,
                        Tag = i.Tag
                    })
                    .Take(60)
                    .ToListAsync();
        }

        /// <summary>
        /// Gets the information about a given item
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("/item/{itemTag}")]
        [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = new string[] { "preventUrlMigration" })]
        public async Task<Item> GetItemInfo(string itemTag, bool preventUrlMigration = false)
        {
            string[] altTags = ["PET_SKIN_" + itemTag, itemTag];
            var res = await context.Items.Where(i => altTags.Contains(i.Tag))
                    .Include(i => i.Modifiers.Where(m => !ItemService.IgnoredSlugs.Contains(m.Slug)))
                    .FirstOrDefaultAsync();
            if (res == null)
                return null;
            FixNameIfNull(res);
            if (!preventUrlMigration)
                MigrateUrl(res);
            else
                res.Modifiers = null;
            if (res.Name == null)
                res.Name = res.Tag;
            return res;
        }

        /// <summary>
        /// Returns all items that don't have an icon
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("/items/noicon")]
        [ResponseCache(Duration = 30, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<IEnumerable<Item>> ItemsWithoutIcon()
        {
            return await context.Items.Where(i => i.IconUrl == null && (i.MinecraftType == null || i.MinecraftType == "SKULL_ITEM")).ToListAsync();
        }

        /// <summary>
        /// Updates the icon url for an item
        /// </summary>
        /// <param name="itemTag"></param>
        /// <param name="texture"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("/item/{itemTag}/texture")]
        public async Task SetTextureForItem(string itemTag, string texture)
        {
            var item = await context.Items.Where(i => i.Tag == itemTag).FirstOrDefaultAsync();
            if (item.IconUrl != null)
                return; // don't overwrite existing urls
            if (texture.Contains("http://textures.minecraft.net/texture/"))
                item.IconUrl = "https://mc-heads.net/head/" + texture.Replace("http://textures.minecraft.net/texture/", "");
            else
                item.IconUrl = texture;
            context.Update(item);
            logger.LogInformation("Updated icon for " + itemTag + " to " + item.IconUrl);
            await context.SaveChangesAsync();
        }

        [HttpGet]
        [Route("/item/{itemTag}/descriptions")]
        public async Task<IEnumerable<string>> GetDescriptions(string itemTag)
        {
            return await context.Items.Where(i => i.Tag == itemTag).SelectMany(i => i.Descriptions)
                .OrderByDescending(m => m.Occurences).Select(m => m.Text).Take(10).ToListAsync();
        }

        /// <summary>
        /// Gets names of items
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("/item/names")]
        [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async IAsyncEnumerable<ItemPreview> GetItemInfo()
        {
            var res = context.Items
                    .Include(i => i.Modifiers.Where(m => m.Slug == "name")).AsAsyncEnumerable();

            await foreach (var item in res)
            {
                FixNameIfNull(item);
                yield return new(item.Tag, item.Name);
            }
        }
        /// <summary>
        /// Returns all items that can be sold to npc
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("/items/npcSell")]
        [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<Dictionary<string, float>> GetItemsWithNpcSell()
        {
            return await context.Items.Where(i => i.NpcSellPrice > 0).ToDictionaryAsync(i => i.Tag, i => i.NpcSellPrice);
        }

        /// <summary>
        /// Recently added items
        /// </summary>
        [HttpGet]
        [Route("/items/recent")]
        [ResponseCache(Duration = 120, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = ["dayAge"])]
        public async Task<IEnumerable<string>> GetRecentlyAdded(double dayAge = 10)
        {
            var minTime = DateTime.UtcNow.AddDays(-dayAge);
            return await context.Items.Where(i => i.FirstSeen > minTime).Select(i => i.Tag).ToListAsync();
        }

        [HttpGet]
        [Route("/attribute/groups")]
        [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<Dictionary<ItemCategory, IEnumerable<string>>> GetAttributeGroups()
        {
            var modLookup = new HashSet<string>(Core.Constants.AttributeKeys);
            var data = await context.Items.Where(i => i.Modifiers.Where(m => modLookup.Contains(m.Slug)).Any())
                .Select(i => new { i.Category, i.Tag }).ToListAsync();
            return data.GroupBy(m => m.Category)
            .ToDictionary(m => m.Key, m => m.Select(i => i.Tag));
        }

        private static void FixNameIfNull(Item res)
        {
            if (res.Name == null)
                res.Name = res.Modifiers?.Where(m => m.Slug == "name" && m.Value != null)
                        .OrderByDescending(m => m.FoundCount)
                        .Select(m => m.Value).FirstOrDefault();
        }

        private static void MigrateUrl(Item res)
        {
            if (res != null && !res.Tag.StartsWith("PET") && !res.Tag.StartsWith("POTION") && !res.Tag.StartsWith("RUNE"))
                res.IconUrl = "https://sky.coflnet.com/static/icon/" + res.Tag;
        }

        private IOrderedQueryable<Item> GetSelectForQueryTerm(string term)
        {
            var clearedSearch = term;
            short.TryParse(term, out short numericId);
            var tagified = term.ToUpper().Replace(' ', '_');
            if (tagified.EndsWith("_PET"))
                tagified = "PET_" + tagified.Replace("_PET", "");
            if (tagified == term && Regex.IsMatch(term, @"[a-zA-Z]"))
                return context.Items.Where(i => i.Tag == tagified).OrderBy(i => i.Id);
            var namingModifiers = new HashSet<string>() { "name", "alias", "abr" };
            var select = context.Items
                    .Include(item => item.Modifiers)
                    .Where(item =>
                        context.Modifiers
                        .Where(m => namingModifiers.Contains(m.Slug) && (EF.Functions.Like(m.Value, clearedSearch + '%')
                            || EF.Functions.Like(m.Value, "Enchanted " + clearedSearch + '%')) && m.Item == item
                        // || EF.Functions.Like(name.Value, '%' + term + '%')
                        ).Any()
                        || EF.Functions.Like(item.Tag, "%" + tagified + '%')
                        || EF.Functions.Like(item.Name, clearedSearch + '%')
                        || item.Id == numericId
                    ).AsSplitQuery()
                    .OrderBy(item => (item.Name.Length / 2) - (item.Name.StartsWith(clearedSearch) ? 1 : 0) - (item.Name == clearedSearch || item.Tag == tagified ? 10000000 : 0));
            var sql = select.ToQueryString();
            return select;
        }
    }
}
