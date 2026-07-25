using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Coflnet.Sky.Items.Models.Hypixel;

public sealed class ResourcePacksResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("packs")]
    public List<ResourcePack> Packs { get; set; } = new();
}

public sealed class ResourcePack
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("versions")]
    public List<ResourcePackVersion> Versions { get; set; } = new();
}

public sealed class ResourcePackVersion
{
    [JsonPropertyName("packFormat")]
    public int PackFormat { get; set; }

    [JsonPropertyName("hash")]
    public string Hash { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; }
}
