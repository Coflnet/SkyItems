using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using SkiaSharp;

namespace Coflnet.Sky.Items.Services;

public sealed class ResourcePackIconExtractor
{
    private readonly IReadOnlyDictionary<string, ZipArchiveEntry> entries;

    public ResourcePackIconExtractor(ZipArchive archive)
    {
        entries = archive.Entries.ToDictionary(entry => entry.FullName, StringComparer.OrdinalIgnoreCase);
    }

    public byte[] Extract(string itemModel)
    {
        var resource = ResourceLocation.Parse(itemModel, "minecraft");
        if (resource.Namespace == "minecraft")
            return null;

        using var document = ReadJson($"assets/{resource.Namespace}/items/{resource.Path}.json");
        if (!document.RootElement.TryGetProperty("model", out var model))
            throw new InvalidDataException($"Item declaration {itemModel} has no model.");

        using var bitmap = RenderNode(model, resource.Namespace);
        if (bitmap == null)
            return null;

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private SKBitmap RenderNode(JsonElement node, string defaultNamespace)
    {
        var type = node.TryGetProperty("type", out var typeNode)
            ? typeNode.GetString()?.Replace("minecraft:", "", StringComparison.Ordinal)
            : "model";

        return type switch
        {
            "model" => RenderModel(node.GetProperty("model").GetString(), defaultNamespace),
            "select" => RenderNode(SelectGuiModel(node), defaultNamespace),
            "condition" => RenderNode(node.GetProperty("on_false"), defaultNamespace),
            "range_dispatch" => RenderNode(node.GetProperty("fallback"), defaultNamespace),
            "composite" => Composite(node.GetProperty("models"), defaultNamespace),
            _ => throw new NotSupportedException($"Unsupported item model type '{type}'.")
        };
    }

    private static JsonElement SelectGuiModel(JsonElement node)
    {
        if (node.TryGetProperty("cases", out var cases))
        {
            foreach (var candidate in cases.EnumerateArray())
            {
                var when = candidate.GetProperty("when");
                if ((when.ValueKind == JsonValueKind.String && when.GetString() == "gui")
                    || (when.ValueKind == JsonValueKind.Array && when.EnumerateArray().Any(value => value.GetString() == "gui")))
                    return candidate.GetProperty("model");
            }
        }
        return node.GetProperty("fallback");
    }

    private SKBitmap Composite(JsonElement models, string defaultNamespace)
    {
        var layers = models.EnumerateArray()
            .Select(model => RenderNode(model, defaultNamespace))
            .Where(bitmap => bitmap != null)
            .ToList();
        if (layers.Count == 0)
            return null;

        var result = new SKBitmap(layers.Max(layer => layer.Width), layers.Max(layer => layer.Height), true);
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);
        foreach (var layer in layers)
        {
            canvas.DrawBitmap(layer, 0, 0);
            layer.Dispose();
        }
        return result;
    }

    private SKBitmap RenderModel(string modelName, string defaultNamespace)
    {
        var resource = ResourceLocation.Parse(modelName, defaultNamespace);
        if (resource.Namespace == "minecraft")
            return null;

        var textures = FindTextures(resource, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        if (!textures.TryGetValue("layer0", out var textureName))
            throw new InvalidDataException($"Model {modelName} has no layer0 texture.");
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (textureName.StartsWith('#'))
        {
            var alias = textureName[1..];
            if (!aliases.Add(alias) || !textures.TryGetValue(alias, out textureName))
                throw new InvalidDataException($"Model {modelName} has an invalid texture alias.");
        }

        var texture = ResourceLocation.Parse(textureName, resource.Namespace);
        if (texture.Namespace == "minecraft")
            return null;

        var path = $"assets/{texture.Namespace}/textures/{texture.Path}.png";
        if (!entries.TryGetValue(path, out var entry))
            throw new FileNotFoundException($"Texture {path} was not found in the resource pack.", path);

        using var stream = entry.Open();
        var bitmap = SKBitmap.Decode(stream)
            ?? throw new InvalidDataException($"Texture {path} is not a valid PNG.");

        if (!entries.ContainsKey(path + ".mcmeta") || bitmap.Height <= bitmap.Width)
            return bitmap;

        var firstFrame = new SKBitmap(bitmap.Width, bitmap.Width, true);
        using (var canvas = new SKCanvas(firstFrame))
            canvas.DrawBitmap(bitmap, new SKRect(0, 0, bitmap.Width, bitmap.Width));
        bitmap.Dispose();
        return firstFrame;
    }

    private Dictionary<string, string> FindTextures(ResourceLocation model, HashSet<string> visited)
    {
        var path = $"assets/{model.Namespace}/models/{model.Path}.json";
        if (!visited.Add(path))
            throw new InvalidDataException($"Model inheritance cycle at {path}.");

        using var document = ReadJson(path);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (document.RootElement.TryGetProperty("parent", out var parentNode))
        {
            var parent = ResourceLocation.Parse(parentNode.GetString(), "minecraft");
            if (parent.Namespace != "minecraft")
                result = FindTextures(parent, visited);
        }
        if (document.RootElement.TryGetProperty("textures", out var textures))
            foreach (var texture in textures.EnumerateObject())
                result[texture.Name] = texture.Value.GetString();
        return result;
    }

    private JsonDocument ReadJson(string path)
    {
        if (!entries.TryGetValue(path, out var entry))
            throw new FileNotFoundException($"Resource {path} was not found in the resource pack.", path);
        using var stream = entry.Open();
        return JsonDocument.Parse(stream);
    }

    private readonly record struct ResourceLocation(string Namespace, string Path)
    {
        public static ResourceLocation Parse(string value, string defaultNamespace)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidDataException("Resource location is empty.");
            var split = value.Split(':', 2);
            var result = split.Length == 2
                ? new ResourceLocation(split[0], split[1])
                : new ResourceLocation(defaultNamespace, split[0]);
            if (result.Path.StartsWith('/') || result.Path.Split('/').Contains(".."))
                throw new InvalidDataException($"Invalid resource location '{value}'.");
            return result;
        }
    }
}
