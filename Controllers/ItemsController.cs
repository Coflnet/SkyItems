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
using System.Threading;

namespace Coflnet.Sky.Items.Controllers
{
    /// <summary>
    /// Main Controller handling tracking
    /// </summary>
    [ApiController]
    [Route("[controller]")]
    public class ItemsController : ControllerBase
    {
        private const int MaxSearchCount = 100;
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
        public async Task<IEnumerable<SearchResult>> Search(string term, int count = 20, CancellationToken cancellationToken = default)
        {
            var select = GetSelectForQueryTerm(term);
            var prospects = await select
                    .Take(Math.Min(count, MaxSearchCount) * 3)
                    .Select(i => new
                    {
                        Name = i.Name ?? i.Modifiers.Where(m => (m.Slug == "name" || m.Slug == "alias") && m.Value != null)
                        .OrderByDescending(m => m.FoundCount)
                        .Select(m => m.Value).FirstOrDefault(),
                        i.Tag,
                        i.Flags,
                        i.Tier
                    })
                    .ToListAsync(cancellationToken);
            return prospects.Select(item =>
            {
                return new SearchResult()
                {
                    Tag = item.Tag,
                    // manual name takes priority over most common
                    Text = ImproveName(item.Name, item.Tag),
                    Flags = item.Flags,
                    Tier = item.Tier
                };
            });
        }

        /// <summary>
        /// Reduces an in game display name to the item name: drops upgrade stars, pet levels and a
        /// leading reforge ("Withered Hyperion ✪✪✪✪✪" becomes "Hyperion"). The reforge stays when it is
        /// part of the tag, e.g. "Wise Dragon Helmet" for WISE_DRAGON_HELMET.
        /// </summary>
        internal static string ImproveName(string fullName, string tag)
        {
            if (fullName == null)
                return null;
            if (tag == "GOD_POTION")
                return "God Potion (Legacy)";
            // master stars follow the normal ones ("✪✪✪✪✪➋"), the fragged marker is added back below for STARRED_ tags
            var noSpecialChars = fullName.Trim('✪', '➊', '➋', '➌', '➍', '➎').Replace("✦", "").Replace("⚚", "").Trim();
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
        public async Task<int> GetId(string term, CancellationToken cancellationToken = default)
        {
            IOrderedQueryable<Item> select = GetSelectForQueryTerm(term);
            return await select
                    .Take(2)
                    .Select(i => i.Id).FirstOrDefaultAsync(cancellationToken);
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
            var list = ItemService.IgnoredSlugs.Where(m => m != "uid").ToHashSet();
            string[] altTags = ["PET_SKIN_" + itemTag, itemTag];
            var res = await context.Items.Where(i => altTags.Contains(i.Tag))
                    .Include(i => i.Modifiers.Where(m => !list.Contains(m.Slug)))
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
        /// Returns all items that don't have an icon (or are missing a real display name)
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("/items/noicon")]
        [ResponseCache(Duration = 30, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<IEnumerable<Item>> ItemsWithoutIcon()
        {
            return await context.Items.Where(i =>
                    (i.IconUrl == null && (i.MinecraftType == null || i.MinecraftType == "SKULL_ITEM"))
                    || i.Name == null || i.Name == "" || i.Name == i.Tag)
                .ToListAsync();
        }

        /// <summary>
        /// Updates the icon url and/or display name for an item
        /// </summary>
        /// <param name="itemTag"></param>
        /// <param name="texture"></param>
        /// <param name="name">Raw (possibly Minecraft-formatted) display name to store when the item doesn't have a real one yet</param>
        /// <returns></returns>
        [HttpPost]
        [Route("/item/{itemTag}/texture")]
        public async Task SetTextureForItem(string itemTag, string texture, string name = null)
        {
            var item = await context.Items.Where(i => i.Tag == itemTag).FirstOrDefaultAsync();
            if (item == null)
            {
                logger.LogInformation("Tried to set texture/name for unknown item {tag}", itemTag);
                return;
            }
            var changed = false;
            if (item.IconUrl == null && !string.IsNullOrEmpty(texture))
            {
                if (texture.Contains("http://textures.minecraft.net/texture/"))
                    item.IconUrl = "https://mc-heads.net/head/" + texture.Replace("http://textures.minecraft.net/texture/", "");
                else
                    item.IconUrl = texture;
                logger.LogInformation("Updated icon for " + itemTag + " to " + item.IconUrl);
                changed = true;
            }
            var sanitizedName = ImproveName(SanitizeItemName(name), itemTag);
            if (ShouldReplaceName(item.Name, itemTag, sanitizedName))
            {
                item.Name = sanitizedName;
                logger.LogInformation("Updated name for " + itemTag + " to " + item.Name);
                changed = true;
            }
            if (!changed)
                return;
            context.Update(item);
            await context.SaveChangesAsync();
        }

        private static readonly Regex ColorCodePattern = new(@"§[0-9a-fklmnor]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex LeadingBuySellPattern = new(@"^(?:BUY|SELL)(?:\s+|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex LeadingAmountPattern = new(@"^[\d,]+x\s+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Strips Minecraft color codes, bazaar order prefixes ("BUY "/"SELL ") and leading stack
        /// amount prefixes ("64x ") from a raw item display name (as seen e.g. on bazaar order
        /// screens sent by the mod). Returns null if nothing usable remains.
        /// </summary>
        internal static string SanitizeItemName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
                return null;
            var name = ColorCodePattern.Replace(rawName, "").Trim();
            string previous;
            do
            {
                previous = name;
                name = LeadingBuySellPattern.Replace(name, "");
                name = LeadingAmountPattern.Replace(name, "");
                name = name.Trim();
            } while (name != previous);
            return string.IsNullOrEmpty(name) ? null : name;
        }

        /// <summary>
        /// Whether the currently stored <paramref name="name"/> is only a placeholder: missing, or the
        /// tag itself (upper case, spaces allowed in place of underscores). Every other name is a real one,
        /// usually from the hypixel api, and is never overwritten - also not "Hyperion", which equals its
        /// tag once case is ignored (true for about 3200 items) and used to be replaced by reforged
        /// display names like "Withered Hyperion ✪✪✪✪✪".
        /// </summary>
        internal static bool LooksLikeTag(string name, string tag)
        {
            if (string.IsNullOrWhiteSpace(name))
                return true;
            if (string.IsNullOrEmpty(tag))
                return false;
            return string.Equals(name.Trim().Replace(' ', '_'), tag.Trim(), StringComparison.Ordinal);
        }

        /// <summary>
        /// Decides whether a newly seen (already cleaned) name should be stored: only in place of a
        /// placeholder (see <see cref="LooksLikeTag"/>), and never a "name" that is itself just the raw
        /// tag ("FACTION_RABBIT_MOCKTAIL", "Faction_Rabbit_Mocktail"). A capitalised name without
        /// underscores is accepted even if it equals the tag once case is ignored ("Hyperion").
        /// </summary>
        internal static bool ShouldReplaceName(string currentName, string tag, string sanitizedNewName)
        {
            if (string.IsNullOrEmpty(sanitizedNewName) || IsRawTag(sanitizedNewName, tag))
                return false;
            return LooksLikeTag(currentName, tag);
        }

        private static bool IsRawTag(string newName, string tag)
        {
            if (!string.Equals(newName, tag, StringComparison.OrdinalIgnoreCase))
                return false;
            return newName.Contains('_') || !newName.Any(char.IsLower) || !newName.Any(char.IsUpper);
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
            var res = context.Items.Select(i=>new{i.Tag,i.Name}).AsAsyncEnumerable();

            await foreach (var item in res)
            {
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
            if (res != null
                && res.IconUrl?.StartsWith("https://static.coflnet.com/sky/icons/", StringComparison.Ordinal) != true
                && !res.Tag.StartsWith("PET") && !res.Tag.StartsWith("POTION") && !res.Tag.StartsWith("RUNE"))
                res.IconUrl = "https://sky.coflnet.com/static/icon/" + res.Tag;
        }

        internal IOrderedQueryable<Item> GetSelectForQueryTerm(string term)
        {
            var clearedSearch = Regex.Replace(term ?? "", @"[^\u0000-\u007F]+", "").Trim();
            short.TryParse(term, out short numericId);
            var tagified = term.ToUpper().Replace(' ', '_');
            if (tagified.EndsWith("_PET"))
                tagified = "PET_" + tagified.Replace("_PET", "");
            if (tagified == term && Regex.IsMatch(term, @"[a-zA-Z]"))
                return context.Items.Where(i => i.Tag == tagified).OrderBy(i => i.Id);
            var namingModifiers = new HashSet<string>() { "name", "alias", "abr" };
            var matchingModifierItemIds = context.Modifiers
                    .Where(m => namingModifiers.Contains(m.Slug) && (EF.Functions.Like(m.Value, clearedSearch + '%')
                        || EF.Functions.Like(m.Value, "Enchanted " + clearedSearch + '%')))
                    .Select(m => m.ItemId);
            // Only dedicated alias/abr modifiers count as a strong (rank 2) match; "name" modifiers hold every
            // observed auction display name including reforged ones ("Hyper Cow Head") and must not outrank real names.
            var aliasModifiers = new HashSet<string>() { "alias", "abr" };
            var aliasModifierItemIds = context.Modifiers
                    .Where(m => aliasModifiers.Contains(m.Slug) && (EF.Functions.Like(m.Value, clearedSearch + '%')
                        || EF.Functions.Like(m.Value, "Enchanted " + clearedSearch + '%')))
                    .Select(m => m.ItemId);
            // Roman-numeral enchant tiers ("Karma I".."Karma IV") all match the prefix/tag LIKEs above
            // ('_' is a LIKE wildcard, so "%KARMA_I%" hits every tier), so boost the exact
            // ENCHANTMENT_<NAME>_<level> tag. Hot path: keep this a plain parameter comparison
            // ("" never matches a tag) instead of another subquery or an inlined constant.
            var enchantTag = GetEnchantTagCandidate(clearedSearch) ?? "";
            var select = context.Items
                    .Where(item =>
                        matchingModifierItemIds.Contains(item.Id)
                        || EF.Functions.Like(item.Tag, "%" + tagified + '%')
                        || EF.Functions.Like(item.Name, clearedSearch + '%')
                        || item.Id == numericId
                    )
                    // rank: 0 exact, 1 canonical name prefix, 2 alias/abr prefix, 3 rest (reforged name modifiers, tag substring)
                    .OrderBy(item => item.Name == clearedSearch || item.Tag == tagified || item.Tag == enchantTag ? 0
                        : EF.Functions.Like(item.Name, clearedSearch + '%') ? 1
                        : aliasModifierItemIds.Contains(item.Id) ? 2 : 3)
                    .ThenBy(item => item.Name == null ? int.MaxValue : item.Name.Length)
                    .ThenBy(item => item.Id);
            return select;
        }

        private static readonly Regex TrailingRomanNumeral = new(@"^(.+?)\s+([IVXLC]+)$", RegexOptions.Compiled);

        /// <summary>
        /// Builds the ENCHANTMENT_&lt;NAME&gt;_&lt;level&gt; tag candidate for search terms ending in a
        /// roman numeral level, e.g. "Karma I" -&gt; ENCHANTMENT_KARMA_1, "Ultimate Wise V" -&gt;
        /// ENCHANTMENT_ULTIMATE_WISE_5, "Turbo-Wheat I" -&gt; ENCHANTMENT_TURBO_WHEAT_1.
        /// Returns null when the trailing token isn't a valid roman numeral (verified by round-tripping
        /// it through <see cref="Core.Roman"/>), so a term that merely ends in letters that happen to
        /// also form a valid roman numeral pattern never misfires into a bogus candidate - the caller
        /// only uses the candidate when it actually matches an item's tag, so an unexpected candidate
        /// here is harmless.
        /// </summary>
        internal static string GetEnchantTagCandidate(string term)
        {
            if (string.IsNullOrEmpty(term))
                return null;
            var match = TrailingRomanNumeral.Match(term.ToUpperInvariant());
            if (!match.Success)
                return null;
            var romanPart = match.Groups[2].Value;
            int level;
            try
            {
                level = Core.Roman.From(romanPart);
            }
            catch (KeyNotFoundException)
            {
                return null;
            }
            if (level <= 0 || Core.Roman.To(level) != romanPart)
                return null;
            var namePart = string.Join("_", match.Groups[1].Value.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries));
            if (namePart.Length == 0)
                return null;
            return $"ENCHANTMENT_{namePart}_{level}";
        }
    }
}
