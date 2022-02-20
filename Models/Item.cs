using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Newtonsoft.Json.Converters;
using hypixel;
using Microsoft.EntityFrameworkCore;

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
        [System.ComponentModel.DataAnnotations.MaxLength(44)]
        [DataMember(Name = "Name")]
        public string Name { get; set; }
        /// <summary>
        /// Category this item coresponds to
        /// </summary>
        /// <value></value>
        [DataMember(Name = "category")]
        [JsonConverter(typeof(StringEnumConverter))]
        public ItemCategory Category { get; set; }
        /// <summary>
        /// Tier/Rarity of this item
        /// </summary>
        /// <value></value>
        [JsonConverter(typeof(StringEnumConverter))]
        [DataMember(Name = "tier")]
        public hypixel.Tier Tier { get; set; }
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
        public int NpcSellPrice { get; set; }
        /// <summary>
        /// For how much this item can be bought from an npc
        /// </summary>
        /// <value></value>
        public int NpcBuyPrice { get; set; }
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
    }

    public class Description
    {
        public int Id { get; set; }
        public int Occurences { get; set; }
        public Item Item { get; set; }

        [MySqlCharSet("utf8")]
        [DataMember(Name = "text")]
        public string Text { get; set; }
    }
}