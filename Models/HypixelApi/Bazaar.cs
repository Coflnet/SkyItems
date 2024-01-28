using System.Collections.Generic;
using dev;
using Newtonsoft.Json;

namespace Coflnet.Sky.Items.Models.Hypixel
{
    public class BazaarResponse
    {
        [JsonProperty("products")]
        public Dictionary<string, ProductInfo> Products { get; set; }
    }
}