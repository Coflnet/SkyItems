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

        /// <summary>
        /// Creates a new instance of <see cref="ItemsController"/>
        /// </summary>
        /// <param name="service"></param>
        /// <param name="context"></param>
        public ItemsController(ItemService service, ItemDbContext context)
        {
            this.service = service;
            this.context = context;
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
        /// Tags to item ids maping
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [ResponseCache(Duration = 600, Location = ResponseCacheLocation.Any, NoStore = false)]
        [Route("ids")]
        public async Task<Dictionary<string, int>> InternalIds()
        {
            return await context.Items.Select(i => new { i.Tag, i.Id }).ToDictionaryAsync(i => i.Tag, i => i.Id);
        }
        /// <summary>
        /// Tags to item ids maping
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [ResponseCache(Duration = 600, Location = ResponseCacheLocation.Any, NoStore = false)]
        [Route("/item/{itemTag}/modifiers")]
        public async Task<Dictionary<string, HashSet<string>>> Modifiers(string itemTag)
        {
            var modifiers = await context.Items.Where(i => i.Tag == itemTag).Include(i => i.Modifiers).Select(i => i.Modifiers).FirstOrDefaultAsync();
            return modifiers.GroupBy(m => m.Slug).ToDictionary(m => m.Key, m => m.Select(m => m.Value).ToHashSet());
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
                        Name = i.Name == null ?
                        i.Modifiers.Where(m => m.Slug == "name" && m.Value != null)
                        .OrderByDescending(m => m.FoundCount)
                        .Select(m => m.Value).FirstOrDefault()
                        : i.Name,
                        i.Tag,
                        i.Flags
                    })
                    .AsSplitQuery()
                    .ToListAsync();
            return prospects.Select(item =>
            {
                return new SearchResult()
                {
                    Tag = item.Tag,
                    Text = item.Name,
                    Flags = item.Flags
                };
            });
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
                    .Take(1)
                    .Select(i => i.Id).FirstOrDefaultAsync();
        }

        private IOrderedQueryable<Item> GetSelectForQueryTerm(string term)
        {
            var clearedSearch = term;
            short.TryParse(term, out short numericId);
            var tagified = term.ToUpper().Replace(' ', '_');
            if (tagified.EndsWith("_pet"))
                tagified = "PET_" + tagified.Replace("_pet", "");
            var namingModifiers = new HashSet<string>() { "name", "alias", "abr" };
            var select = context.Items
                    .Include(item => item.Modifiers)
                    .Where(item =>
                        item.Modifiers
                        .Where(m => namingModifiers.Contains(m.Slug))
                        .Where(name => EF.Functions.Like(name.Value, clearedSearch + '%')
                        //    || EF.Functions.Like(name.Value, "Enchanted " + clearedSearch + '%')
                        || EF.Functions.Like(name.Value, '%' + term + '%')
                        ).Any()
                        || EF.Functions.Like(item.Tag, tagified + '%')
                        || item.Id == numericId
                    ).OrderBy(item => item.Name.Length / 2 - (item.Name.StartsWith(clearedSearch) ? 10000 : 0) - (item.Name == clearedSearch ? 10000000 : 0));
            return select;
        }
    }
}
