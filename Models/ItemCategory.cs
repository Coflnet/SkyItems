namespace Coflnet.Sky.Items.Models
{
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    /*
    Retrieving items not fitting any category js filter
    apiResponse.items.filter(a=>!a.category && !a.furniture && !a.generator && !a.id.endsWith("ISLAND_CRYSTAL")&& !a.id.endsWith("ISLAND")&& !a.id.endsWith("PERSONALITY")
    && !a.id.endsWith("FRAGMENT")&& !a.id.endsWith("BACKPACK")&& !a.id.endsWith("_SACK")&& !a.id.endsWith("_PORTAL")
    && (!a.requirements || !a.requirements.slayer) &&(!a.requirements || !a.requirements.heart_of_the_mountain) 
    && !a.id.startsWith("ENCHANTED_") && !a.id.startsWith("TALISMAN_ENRICHMENT") && !a.dungeon_item && !a.name.endsWith("the Fish") )
    */
    public enum ItemCategory
    {
        UNKNOWN,
        PET_ITEM,
        SWORD,
        CHESTPLATE,
        HELMET,
        REFORGE_STONE,
        COSMETIC,
        AXE,
        LEGGINGS,
        ACCESSORY,
        BOW,
        TRAVEL_SCROLL,
        BOOTS,
        HOE,
        BAIT,
        FISHING_ROD,
        DUNGEON_PASS,
        ARROW,
        SPADE,
        SHEARS,
        PICKAXE,
        ARROW_POISON,
        WAND,
        DRILL,
        FISHING_WEAPON,
        GAUNTLET,
        FURNITURE,
        /// <summary>
        /// Minions
        /// </summary>
        GENERATOR,
        MINION_SKIN,
        PRIVATE_ISLAND,
        ISLAND_CRYSTAL,
        FRAGMENT,
        SLAYER,
        /// <summary>
        /// Items requiring dungeon level
        /// </summary>
        DUNGEON,
        /// <summary>
        /// Items optained in dungeons
        /// </summary>
        DUNGEON_ITEM,
        SACK,
        PORTAL,
        DEEP_CAVERNS,
        BACKPACK,
        TALISMAN_ENRICHMENT,
        THE_FISH,
        /// <summary>
        /// introduced late 
        /// </summary>
        PET,
        PET_SKIN,
        RUNE,
        ArmorDye,
        /// <summary>
        /// Items not from skyblock, stairs, colored glass and clay etc.
        /// </summary>
        Vanilla,
        /// <summary>
        /// Any item whichs name is null
        /// </summary>
        NullNamed,
        CLOAK,
        NECKLACE,
        BELT,
        GLOVES,
        BRACELET,
        InfernoMinionFuel,
        MEMENTO
    }
}