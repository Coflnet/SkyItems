
using System;
using System.Runtime.Serialization;

namespace Coflnet.Sky.Items.Models
{
    [DataContract]
    public class Modifiers
    {
        [IgnoreDataMember]
        public int Id { get; set; }
        [System.ComponentModel.DataAnnotations.MaxLength(40)]
        [DataMember(Name = "key")]
        public string Slug { get; set; }
        [System.ComponentModel.DataAnnotations.MaxLength(150)]
        [DataMember(Name = "value")]
        public string Value { get; set; }
        public DataType Type { get; set; }
        public int FoundCount { get; set; }
        [IgnoreDataMember]
        [System.Text.Json.Serialization.JsonIgnore]
        public Item Item { get; set; }
        public int ItemId { get; set; }

        public override bool Equals(object obj)
        {
            return obj is Modifiers modifiers &&
                   Slug == modifiers.Slug &&
                   Value == modifiers.Value;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Slug, Value);
        }

        public enum DataType
        {
            UNKOWN,
            STRING,
            LONG,
            FLOAT,
            BOOL,
            TIMESTAMP
        }
    }
}