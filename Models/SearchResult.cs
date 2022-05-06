using System.Collections.Generic;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Items.Models
{
    public class SearchResult
    {
        public string Tag { get; set; }
        public string Text { get; set; }
        public ItemFlags Flags { get; set; }
        public Tier Tier { get; set; }
        public Dictionary<string, string> Filters { get; set; }
    }
}