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
}
