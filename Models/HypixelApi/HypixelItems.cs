using System.Collections.Generic;
using Newtonsoft.Json;

namespace Coflnet.Sky.Items.Models.Hypixel
{
    public class Stats
    {
        [JsonProperty("DAMAGE")]
        public int? DAMAGE { get; set; }

        [JsonProperty("STRENGTH")]
        public int? STRENGTH { get; set; }

        [JsonProperty("DEFENSE")]
        public int? DEFENSE { get; set; }

        [JsonProperty("HEALTH")]
        public int? HEALTH { get; set; }

        [JsonProperty("CRITICAL_DAMAGE")]
        public int? CRITICALDAMAGE { get; set; }

        [JsonProperty("INTELLIGENCE")]
        public int? INTELLIGENCE { get; set; }

        [JsonProperty("WEAPON_ABILITY_DAMAGE")]
        public int? WEAPONABILITYDAMAGE { get; set; }
    }

    public class TieredStats
    {
        [JsonProperty("WALK_SPEED")]
        public List<int> WALKSPEED { get; set; }

        [JsonProperty("DEFENSE")]
        public List<int> DEFENSE { get; set; }
    }

    public class Dungeon
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("level")]
        public int Level { get; set; }
    }

    public class Skill
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("level")]
        public int Level { get; set; }
    }

    public class Requirements
    {
        [JsonProperty("dungeon")]
        public Dungeon Dungeon { get; set; }

        [JsonProperty("skill")]
        public Skill Skill { get; set; }

        [JsonProperty("slayer")]
        public Slayer Slayer { get; set; }

        [JsonProperty("heart_of_the_mountain")]
        public HeartOfTheMountain HeartOfTheMountain { get; set; }
    }

    public class Requirement
    {
        [JsonProperty("type")]
        public string Type { get; set; }
        // there are more but ommitted
    }

    public class HeartOfTheMountain
    {
        [JsonProperty("tier")]
        public int Tier { get; set; }
    }

    public class Slayer
    {
        [JsonProperty("slayer_boss_type")]
        public string SlayerBossType { get; set; }

        [JsonProperty("level")]
        public int Level { get; set; }
    }

    public class Essence
    {
        [JsonProperty("essence_type")]
        public string EssenceType { get; set; }

        [JsonProperty("costs")]
        public List<int> Costs { get; set; }
    }

    public class CatacombsRequirements
    {
        [JsonProperty("dungeon")]
        public Dungeon Dungeon { get; set; }
    }

    public class Item
    {
        [JsonProperty("material")]
        public string Material { get; set; }

        [JsonProperty("durability")]
        public int Durability { get; set; }

        [JsonProperty("skin")]
        public string Skin { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("furniture")]
        public string Furniture { get; set; }

        [JsonProperty("tier")]
        public string Tier { get; set; }

        [JsonProperty("museum")]
        public bool Museum { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("generator")]
        public string Generator { get; set; }

        [JsonProperty("generator_tier")]
        public int? GeneratorTier { get; set; }

        [JsonProperty("glowing")]
        public bool? Glowing { get; set; }

        [JsonProperty("category")]
        public string Category { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("stats")]
        public Stats Stats { get; set; }

        [JsonProperty("npc_sell_price")]
        public float? NpcSellPrice { get; set; }

        [JsonProperty("unstackable")]
        public bool? Unstackable { get; set; }

        [JsonProperty("color")]
        public string Color { get; set; }

        [JsonProperty("dungeon_item")]
        public bool? DungeonItem { get; set; }

        [JsonProperty("tiered_stats")]
        public TieredStats TieredStats { get; set; }

        [JsonProperty("gear_score")]
        public int? GearScore { get; set; }

        [JsonProperty("requirements")]
        public List<Requirement> Requirements { get; set; }

        [JsonProperty("essence")]
        public Essence Essence { get; set; }

        [JsonProperty("catacombs_requirements")]
        public List<Requirement> CatacombsRequirements { get; set; }

        [JsonProperty("ability_damage_scaling")]
        public double? AbilityDamageScaling { get; set; }
    }

    public class HypixelItems
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("lastUpdated")]
        public long LastUpdated { get; set; }

        [JsonProperty("items")]
        public List<Item> Items { get; set; }
    }
}