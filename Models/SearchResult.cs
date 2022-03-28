using System.Collections.Generic;

namespace Coflnet.Sky.Items.Models
{
    public class SearchResult
    {
        public string Tag { get; set; }
        public string Text { get; set; }
        public ItemFlags Flags { get; set; }
        public Dictionary<string, string> Filters { get; set; }
    }
}