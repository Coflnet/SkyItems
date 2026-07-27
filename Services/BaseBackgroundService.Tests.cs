using Coflnet.Sky.Core;
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
}
