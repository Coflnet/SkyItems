using System.Collections.Generic;
using Newtonsoft.Json;

namespace Coflnet.Sky.Items.Models.Hypixel
{
    public class BazaarResponse
    {
        [JsonProperty("products")]
        public Dictionary<string, object> Products { get; set; }
    }
}