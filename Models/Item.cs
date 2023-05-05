using System.Runtime.Serialization;
using System.Collections.Generic;
using Coflnet.Sky.Core;
using System;

namespace Coflnet.Sky.Items.Models
{
    public class Item
    {
        [System.ComponentModel.DataAnnotations.Schema.Column(TypeName = "MEDIUMINT(9)")]
        [IgnoreDataMember]
        public int Id { get; set; }

        private string _tag;

        [System.ComponentModel.DataAnnotations.MaxLength(44)]
        [DataMember(Name = "tag")]
        public string Tag
        {
            get
            {
                return _tag;
            }
            set
            {
                _tag = value.Truncate(44);
            }
        }
        /// <summary>
        /// Default name this item is known by
        /// </summary>
        /// <value></value>
        [System.ComponentModel.DataAnnotations.MaxLength(80)]
        [DataMember(Name = "Name")]
        public string Name { get; set; }
        /// <summary>
        /// Category this item coresponds to
        /// </summary>
        /// <value></value>
        [DataMember(Name = "category")]
        public ItemCategory Category { get; set; }
        /// <summary>
        /// Tier/Rarity of this item
        /// </summary>
        /// <value></value>
        [DataMember(Name = "tier")]
        public Tier Tier { get; set; }
        /// <summary>
        /// Fallback icon url
        /// </summary>
        /// <value></value>
        [DataMember(Name = "iconUrl")]
        public string IconUrl { get; set; }

        /// <summary>
        /// minecraft type name of item (`MATERIAL` in the hypixel api)
        /// </summary>
        /// <value></value>
        [System.ComponentModel.DataAnnotations.MaxLength(25)]
        [DataMember(Name = "minecraftType")]
        public string MinecraftType { get; set; }
        /// <summary>
        /// For how much this item sells at npc
        /// </summary>
        /// <value></value>
        public float NpcSellPrice { get; set; }
        /// <summary>
        /// For how much this item can be bought from an npc
        /// </summary>
        /// <value></value>
        public float NpcBuyPrice { get; set; }
        /// <summary>
        /// Durability property of item
        /// </summary>
        /// <value></value>
        public short Durability { get; set; }
        /// <summary>
        /// Different flags for this item
        /// </summary>
        [DataMember(Name = "Flags")]
        public ItemFlags Flags { get; set; }
        /// <summary>
        /// Tracked modifiers/additional attributes
        /// </summary>
        /// <value></value>
        [DataMember(Name = "modifiers")]
        public HashSet<Modifiers> Modifiers { get; set; }
        /// <summary>
        /// Sample of seen descriptions
        /// </summary>
        /// <value></value>
        [DataMember(Name = "descriptions")]
        public HashSet<Description> Descriptions { get; set; }
        /// <summary>
        /// When the item was first found
        /// </summary>
        /// <value></value>
        [DataMember(Name = "firstSeen")]
        public DateTime FirstSeen { get; set; } = DateTime.Now;
    }
}