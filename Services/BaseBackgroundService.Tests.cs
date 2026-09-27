using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.Items.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Coflnet.Sky.Items.Services;

public class BaseBackgroundServiceTests
{
    [TestCase("SHARD_BURNINGSOUL", "Inferno Demonlord Shard")]
    [TestCase("SHARD_CINDER_BAT", "Cinderbat Shard")]
    [TestCase("SHARD_SEA_EMPEROR", "Loch Emperor Shard")]
    [TestCase("SHARD_ENDSTONE_PROTECTOR", "End Stone Protector Shard")]
    [TestCase("SHARD_TEST_ITEM", "Test item Shard")]
    public void GetShardNameUsesCanonicalMapping(string tag, string expected)
    {
        Assert.That(BaseBackgroundService.GetShardName(tag), Is.EqualTo(expected));
    }

    [TestCase("ENCHANTMENT_STRONG_MANA_7", false, "Strong Vitality 7")]
    [TestCase("ENCHANTMENT_STRONG_MANA_7", true, "Strong Vitality VII")]
    [TestCase("ENCHANTMENT_HARDENED_MANA_7", true, "Hardened Vitality VII")]
    [TestCase("ENCHANTMENT_FEROCIOUS_MANA_7", true, "Vivacious Vitality VII")]
    [TestCase("ENCHANTMENT_MANA_VAMPIRE_7", true, "Vampiric Vitality VII")]
    [TestCase("ENCHANTMENT_PRISTINE_5", true, "Prismatic V")]
    [TestCase("ENCHANTMENT_ULTIMATE_REITERATE_5", true, "Duplex V")]
    [TestCase("ENCHANTMENT_ULTIMATE_BOBBIN_TIME_3", true, "Bobbin' Time III")]
    [TestCase("ENCHANTMENT_ULTIMATE_BANK_5", true, "Bank V")]
    [TestCase("ENCHANTMENT_ULTIMATE_WISE_5", true, "Ultimate Wise V")]
    [TestCase("ENCHANTMENT_ULTIMATE_WISE_5", false, "Ultimate Wise 5")]
    public void GetEnchantmentNameUsesCanonicalMapping(string tag, bool romanLevel, string expected)
    {
        Assert.That(BaseBackgroundService.GetEnchantmentName(tag, romanLevel), Is.EqualTo(expected));
    }

    [TestCase("Strong_Vitality", "strong_mana")]
    [TestCase("Hardened_Vitality", "hardened_mana")]
    [TestCase("Vivacious_Vitality", "ferocious_mana")]
    [TestCase("Vampiric_Vitality", "mana_vampire")]
    [TestCase("Prismatic", "pristine")]
    [TestCase("Duplex", "ultimate_reiterate")]
    [TestCase("Bobbin'_Time", "ultimate_bobbin_time")]
    public void CoreMapsCurrentEnchantmentNameToStableKey(string currentName, string stableKey)
    {
        Assert.That(NBT.RenameEnchant(currentName), Is.EqualTo(stableKey));
    }

    [TestCase("https://sky.shiiyu.moe/api/item/HYPERION", true)]
    [TestCase("https://sky.shiiyu.moe", true)]
    [TestCase("https://skycrypt.coflnet.com/api/head/abc123", true)]
    [TestCase("https://static.coflnet.com/sky/skycrypt/api/item/HYPERION", false)]
    [TestCase(null, false)]
    public void IsLegacySkycryptUrlDetectsDeadHosts(string iconUrl, bool expected)
    {
        Assert.That(BaseBackgroundService.IsLegacySkycryptUrl(iconUrl), Is.EqualTo(expected));
    }

    [TestCase("https://sky.shiiyu.moe/api/item/HYPERION", "https://static.coflnet.com/sky/skycrypt/api/item/HYPERION")]
    [TestCase("https://skycrypt.coflnet.com/api/head/abc123", "https://static.coflnet.com/sky/skycrypt/api/head/abc123")]
    // regression: item.iconUrl is read directly by hypixel-react, so existing DB values pointing at
    // the now-dead skycrypt hosts must be rewritten onto the configured mirror, keeping the path.
    public void RewriteLegacySkycryptUrlKeepsPath(string iconUrl, string expected)
    {
        Assert.That(BaseBackgroundService.RewriteLegacySkycryptUrl(iconUrl, "https://static.coflnet.com/sky/skycrypt"), Is.EqualTo(expected));
    }

    [Test]
    public void RewriteLegacySkycryptUrlIsIdempotent()
    {
        var once = BaseBackgroundService.RewriteLegacySkycryptUrl("https://sky.shiiyu.moe/api/item/HYPERION", "https://static.coflnet.com/sky/skycrypt");
        var twice = BaseBackgroundService.RewriteLegacySkycryptUrl(once, "https://static.coflnet.com/sky/skycrypt");

        Assert.That(twice, Is.EqualTo(once));
    }

    [Test]
    public void RewriteLegacySkycryptUrlLeavesOtherUrlsUnchanged()
    {
        var url = "https://sky.coflnet.com/static/icon/HYPERION";

        Assert.That(BaseBackgroundService.RewriteLegacySkycryptUrl(url, "https://static.coflnet.com/sky/skycrypt"), Is.EqualTo(url));
    }

    // regression: item.iconUrl is what hypixel-react actually reads, so the startup fixup has to
    // touch the db rows themselves, not just the resolution code. Exercised against a real (SQLite
    // in-memory) DbContext, batched, and run twice to confirm the second pass is a no-op.
    [Test]
    public async Task RewriteLegacySkycryptIconUrls_RewritesDbRows_AndIsIdempotent()
    {
        const string mirror = "https://static.coflnet.com/sky/skycrypt";
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<ItemDbContext>().UseSqlite(connection).Options;
        using var context = new SqliteCompatibleItemDbContext(options);
        context.Database.EnsureCreated();
        context.Items.AddRange(
            new Models.Item { Tag = "HYPERION", IconUrl = "https://sky.shiiyu.moe/api/item/HYPERION" },
            new Models.Item { Tag = "OLD_HOST", IconUrl = "https://skycrypt.coflnet.com/api/head/abc123" },
            new Models.Item { Tag = "ALREADY_MIRROR", IconUrl = mirror + "/api/item/ALREADY_MIRROR" },
            new Models.Item { Tag = "NO_ICON", IconUrl = null });
        context.SaveChanges();

        await BaseBackgroundService.RewriteLegacySkycryptIconUrls(context, mirror);

        Assert.Multiple(() =>
        {
            Assert.That(context.Items.Single(i => i.Tag == "HYPERION").IconUrl, Is.EqualTo(mirror + "/api/item/HYPERION"));
            Assert.That(context.Items.Single(i => i.Tag == "OLD_HOST").IconUrl, Is.EqualTo(mirror + "/api/head/abc123"));
            Assert.That(context.Items.Single(i => i.Tag == "ALREADY_MIRROR").IconUrl, Is.EqualTo(mirror + "/api/item/ALREADY_MIRROR"));
            Assert.That(context.Items.Single(i => i.Tag == "NO_ICON").IconUrl, Is.Null);
        });

        // second (idempotent) pass should find nothing left to rewrite
        await BaseBackgroundService.RewriteLegacySkycryptIconUrls(context, mirror);

        Assert.That(context.Items.Single(i => i.Tag == "HYPERION").IconUrl, Is.EqualTo(mirror + "/api/item/HYPERION"));
    }

    /// <summary>
    /// Item.Id is annotated [Column(TypeName = "MEDIUMINT(9)")] for MySQL; SQLite only allows
    /// AUTOINCREMENT on a column declared exactly as INTEGER, so override the store type for this
    /// test-only context (same workaround as ItemsController.Tests.cs's CaseInsensitiveItemDbContext).
    /// </summary>
    private class SqliteCompatibleItemDbContext : ItemDbContext
    {
        public SqliteCompatibleItemDbContext(DbContextOptions<ItemDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Models.Item>().Property(i => i.Id).HasColumnType("INTEGER");
        }
    }
}
