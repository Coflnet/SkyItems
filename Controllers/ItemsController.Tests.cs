using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Items.Models;
using Coflnet.Sky.Items.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Coflnet.Sky.Items.Controllers;

public class ItemsControllerTests
{
    [Test]
    public void SearchUsesIndexedModifierCandidateSubquery()
    {
        var options = new DbContextOptionsBuilder<ItemDbContext>()
            .UseMySql("server=localhost;user=test;password=secret;database=items", new MariaDbServerVersion(new Version(10, 5, 5)))
            .Options;
        using var context = new ItemDbContext(options);
        var storage = new ItemMetaStorage();
        var service = new ItemService(context, NullLogger<ItemService>.Instance, storage);
        var controller = new ItemsController(service, context, NullLogger<ItemsController>.Instance, storage);

        var sql = controller.GetSelectForQueryTerm("dragon").ToQueryString();

        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Contain("`i`.`Id` IN ("));
            Assert.That(sql, Does.Not.Contain("INNER JOIN `Items`"));
        });
    }

    // Regression coverage for the "Karma I" bug: searching for a roman-numeral enchant level used
    // to return the highest tier (e.g. ENCHANTMENT_KARMA_4) instead of the requested one, because
    // every tier's naming-modifier alias ("karma i".."karma iv") shares the "karma i" prefix, so
    // the LIKE-based candidate matching ties them all and the OrderBy's exact-match boost never
    // applied. Below, the pure roman->tag helper is unit tested directly, and full Search/GetId
    // ranking is exercised against a real (SQLite in-memory) provider, since the ranking bug only
    // shows up once the query is actually executed and ties are broken.
    [TestCase("Karma I", "ENCHANTMENT_KARMA_1")]
    [TestCase("Karma IV", "ENCHANTMENT_KARMA_4")]
    [TestCase("Ultimate Wise V", "ENCHANTMENT_ULTIMATE_WISE_5")]
    [TestCase("Turbo-Wheat I", "ENCHANTMENT_TURBO_WHEAT_1")]
    [TestCase("Hyperion", null)] // no trailing roman numeral at all
    [TestCase("Foo CIVIL", null)] // trailing token round-trips to a different roman numeral (153 -> "XXXXXXXXXXXXXXXIII"), so it's rejected
    public void GetEnchantTagCandidateBuildsExpectedTagOrNull(string term, string expectedTag)
    {
        Assert.That(ItemsController.GetEnchantTagCandidate(term), Is.EqualTo(expectedTag));
    }

    [Test]
    public Task SearchRanksExactRomanNumeralAliasFirst_ForLowestTier() =>
        WithSeededContext(SeedKarmaEnchantTiers, async (_, controller) =>
        {
            var results = (await controller.Search("Karma I")).ToList();

            Assert.That(results.First().Tag, Is.EqualTo("ENCHANTMENT_KARMA_1"));
        });

    [Test]
    public Task GetIdRanksExactRomanNumeralAliasFirst_ForLowestTier() =>
        WithSeededContext(SeedKarmaEnchantTiers, async (context, controller) =>
        {
            var expectedId = context.Items.Single(i => i.Tag == "ENCHANTMENT_KARMA_1").Id;

            var id = await controller.GetId("Karma I");

            Assert.That(id, Is.EqualTo(expectedId));
        });

    [Test]
    public Task SearchStillRanksHighestTierFirst_WhenAskedExplicitly() =>
        WithSeededContext(SeedKarmaEnchantTiers, async (_, controller) =>
        {
            var results = (await controller.Search("Karma IV")).ToList();

            Assert.That(results.First().Tag, Is.EqualTo("ENCHANTMENT_KARMA_4"));
        });

    [Test]
    public Task SearchRanksExactRomanNumeralAliasFirst_ForMultiWordEnchantName() =>
        WithSeededContext(SeedUltimateWiseEnchantTiers, async (_, controller) =>
        {
            var results = (await controller.Search("Ultimate Wise I")).ToList();

            Assert.That(results.First().Tag, Is.EqualTo("ENCHANTMENT_ULTIMATE_WISE_1"));
        });

    private static void SeedKarmaEnchantTiers(ItemDbContext context)
    {
        // Inserted highest-tier-first (matching the order the live bug returned results in), so
        // without the fix the deterministic Id tiebreaker alone would still surface KARMA_4 first
        // for a "Karma I" search.
        foreach (var (level, roman) in new[] { (4, "iv"), (3, "iii"), (2, "ii"), (1, "i") })
        {
            context.Items.Add(new Item
            {
                Tag = $"ENCHANTMENT_KARMA_{level}",
                Name = $"karma {level} enchant",
                Modifiers = new HashSet<Modifiers> { new Modifiers { Slug = "alias", Value = $"karma {roman}" } }
            });
        }
    }

    private static void SeedUltimateWiseEnchantTiers(ItemDbContext context)
    {
        foreach (var (level, roman) in new[] { (4, "iv"), (3, "iii"), (2, "ii"), (1, "i") })
        {
            context.Items.Add(new Item
            {
                Tag = $"ENCHANTMENT_ULTIMATE_WISE_{level}",
                Name = $"ultimate wise {level} enchant",
                Modifiers = new HashSet<Modifiers> { new Modifiers { Slug = "alias", Value = $"ultimate wise {roman}" } }
            });
        }
    }

    // Regression coverage: FACTION_RABBIT_MOCKTAIL had no real name in the DB - its Name column was
    // literally the tag - so SkyApi sending along the item's NBT display name should be able to
    // replace it, while a genuinely curated name must never be overwritten.
    [Test]
    public void SanitizeItemNameStripsColorCodes()
    {
        Assert.That(ItemsController.SanitizeItemName("§aFaction Rabbit Mocktail"), Is.EqualTo("Faction Rabbit Mocktail"));
    }

    [TestCase("§6§lSELL §9Agility Shard", "Agility Shard")]
    [TestCase("§6§lBUY §9Enchanted Redstone", "Enchanted Redstone")]
    [TestCase("64x Enchanted Redstone", "Enchanted Redstone")]
    [TestCase("§a64x §fEnchanted Redstone", "Enchanted Redstone")]
    [TestCase("BUY 1,280x Enchanted Redstone", "Enchanted Redstone")]
    [TestCase("   ", null)]
    [TestCase(null, null)]
    [TestCase("§a§lBUY ", null)]
    public void SanitizeItemNameStripsPrefixes(string raw, string expected)
    {
        Assert.That(ItemsController.SanitizeItemName(raw), Is.EqualTo(expected));
    }

    [Test]
    public void ShouldReplaceNameRegressionForFactionRabbitMocktail()
    {
        var sanitized = ItemsController.SanitizeItemName("§aFaction Rabbit Mocktail");

        Assert.That(ItemsController.ShouldReplaceName("FACTION_RABBIT_MOCKTAIL", "FACTION_RABBIT_MOCKTAIL", sanitized), Is.True);
    }

    [Test]
    public void ShouldReplaceNameNeverOverwritesARealName()
    {
        var sanitized = ItemsController.SanitizeItemName("§aFaction Rabbit Mocktail");

        Assert.That(ItemsController.ShouldReplaceName("Rabbit's Special Mocktail", "FACTION_RABBIT_MOCKTAIL", sanitized), Is.False);
    }

    [Test]
    public void ShouldReplaceNameSetsNameWhenCurrentIsNull()
    {
        var sanitized = ItemsController.SanitizeItemName("§aFaction Rabbit Mocktail");

        Assert.That(ItemsController.ShouldReplaceName(null, "FACTION_RABBIT_MOCKTAIL", sanitized), Is.True);
    }

    [Test]
    public void ShouldReplaceNameIgnoresSanitizedNameThatIsJustTheTag()
    {
        Assert.That(ItemsController.ShouldReplaceName(null, "FACTION_RABBIT_MOCKTAIL", "Faction_Rabbit_Mocktail"), Is.False);
        Assert.That(ItemsController.ShouldReplaceName(null, "FACTION_RABBIT_MOCKTAIL", ""), Is.False);
    }

    [Test]
    public Task SetTextureForItemStoresSanitizedNameAndDoesNotOverwriteIcon() =>
        WithSeededContext(context =>
        {
            context.Items.Add(new Item { Tag = "FACTION_RABBIT_MOCKTAIL", Name = "FACTION_RABBIT_MOCKTAIL", IconUrl = "https://existing.example/icon.png" });
        }, async (context, controller) =>
        {
            await controller.SetTextureForItem("FACTION_RABBIT_MOCKTAIL", "http://textures.minecraft.net/texture/abc", "§aFaction Rabbit Mocktail");

            var item = context.Items.Single(i => i.Tag == "FACTION_RABBIT_MOCKTAIL");
            Assert.Multiple(() =>
            {
                Assert.That(item.Name, Is.EqualTo("Faction Rabbit Mocktail"));
                Assert.That(item.IconUrl, Is.EqualTo("https://existing.example/icon.png"), "must not overwrite an existing icon");
            });
        });

    [Test]
    public Task SetTextureForItemDoesNotThrowForUnknownTag() =>
        // Previously NRE'd on item.IconUrl for a tag that isn't in the DB.
        WithSeededContext(_ => { }, (_, controller) =>
            controller.SetTextureForItem("DOES_NOT_EXIST", "sometexture", "§aSome Name"));

    [Test]
    public Task SetTextureForItemAllowsNameOnlyUpdateWithNullTexture() =>
        WithSeededContext(context =>
        {
            context.Items.Add(new Item { Tag = "FACTION_RABBIT_CHASM", Name = "FACTION_RABBIT_CHASM", IconUrl = null });
        }, async (context, controller) =>
        {
            await controller.SetTextureForItem("FACTION_RABBIT_CHASM", null, "§aFaction Rabbit Chasm");

            var item = context.Items.Single(i => i.Tag == "FACTION_RABBIT_CHASM");
            Assert.Multiple(() =>
            {
                Assert.That(item.Name, Is.EqualTo("Faction Rabbit Chasm"));
                Assert.That(item.IconUrl, Is.Null);
            });
        });

    private static async Task WithSeededContext(Action<ItemDbContext> seed, Func<ItemDbContext, ItemsController, Task> body)
    {
        // A shared, kept-open connection is required for SQLite's ":memory:" database to survive
        // for the lifetime of the test instead of being torn down when the context closes it.
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<ItemDbContext>().UseSqlite(connection).Options;
        using var context = new CaseInsensitiveItemDbContext(options);
        context.Database.EnsureCreated();
        seed(context);
        context.SaveChanges();

        var storage = new ItemMetaStorage();
        var service = new ItemService(context, NullLogger<ItemService>.Instance, storage);
        var controller = new ItemsController(service, context, NullLogger<ItemsController>.Instance, storage);
        await body(context, controller);
    }

    /// <summary>
    /// MySQL's default collation is case-insensitive, so production sees "karma i" == "Karma I" for
    /// both the naming-modifier equality check and LIKE prefix matching that the ranking fix relies
    /// on. SQLite's default `=`/IN comparisons use the BINARY collation (case-sensitive), so this
    /// mirrors MySQL's semantics for exactly the columns the new ranking logic compares against.
    /// SQLite's LIKE operator is already case-insensitive for ASCII by default, matching MySQL there
    /// without any extra configuration.
    /// </summary>
    private class CaseInsensitiveItemDbContext : ItemDbContext
    {
        public CaseInsensitiveItemDbContext(DbContextOptions<ItemDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Modifiers>().Property(m => m.Value).UseCollation("NOCASE");
            modelBuilder.Entity<Item>().Property(i => i.Name).UseCollation("NOCASE");
            modelBuilder.Entity<Item>().Property(i => i.Tag).UseCollation("NOCASE");
            // Item.Id is annotated [Column(TypeName = "MEDIUMINT(9)")] for MySQL; SQLite only allows
            // AUTOINCREMENT on a column declared exactly as INTEGER, so override the store type for
            // this test-only context.
            modelBuilder.Entity<Item>().Property(i => i.Id).HasColumnType("INTEGER");
        }
    }
}
