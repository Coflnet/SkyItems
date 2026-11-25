using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using KGySoft.Drawing.Imaging;
using KGySoft.Drawing.SkiaSharp;
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
        using var bitmap = RenderItem(itemModel);
        if (bitmap == null)
            return null;

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public byte[] ExtractAnimatedGif(string itemModel)
    {
        // Use the first animated texture's timeline and keep the other layers on their first frames.
        string animatedPath = null;
        List<(int Index, int Ticks)> frames = null;
        int SelectAnimation(string path, JsonElement animation, int frameCount, int firstIndex)
        {
            if (animatedPath == null)
            {
                var schedule = ReadAnimationFrames(animation, frameCount);
                if (schedule.Count > 1)
                {
                    animatedPath = path;
                    frames = schedule;
                }
            }
            return firstIndex;
        }

        using var firstFrame = RenderItem(itemModel, SelectAnimation);
        if (frames == null)
            return null;

        using var output = new MemoryStream();
        using var encoder = new GifEncoder(output, new System.Drawing.Size(firstFrame.Width, firstFrame.Height))
        {
            RepeatCount = 0
        };
        var quantizer = OptimizedPaletteQuantizer.Octree().ConfigureBitLevel(8);
        void AddFrame(SKBitmap bitmap, int ticks)
        {
            using var pixels = bitmap.GetReadableBitmapData();
            using var indexed = pixels.Clone(KnownPixelFormat.Format8bppIndexed, quantizer);
            // Each Minecraft tick is five centiseconds, the unit used by GIF delays.
            encoder.AddImage(indexed, delay: ticks * 5, disposalMethod: GifGraphicDisposalMethod.RestoreToBackground);
        }

        AddFrame(firstFrame, frames[0].Ticks);
        foreach (var (index, ticks) in frames.Skip(1))
        {
            using var frame = RenderItem(itemModel, (path, _, _, firstIndex) => path == animatedPath ? index : firstIndex);
            AddFrame(frame, ticks);
        }
        encoder.FinalizeEncoding();
        return output.ToArray();
    }

    private static List<(int Index, int Ticks)> ReadAnimationFrames(JsonElement animation, int frameCount)
    {
        var frameTime = animation.TryGetProperty("frametime", out var duration) ? duration.GetInt32() : 1;
        var frames = animation.TryGetProperty("frames", out var declared) && declared.GetArrayLength() > 0
            ? declared.EnumerateArray().Select(frame => frame.ValueKind == JsonValueKind.Object
                ? (Index: frame.GetProperty("index").GetInt32(), Ticks: frame.TryGetProperty("time", out var time) ? time.GetInt32() : frameTime)
                : (Index: frame.GetInt32(), Ticks: frameTime))
            : Enumerable.Range(0, frameCount).Select(index => (Index: index, Ticks: frameTime));
        var schedule = new List<(int Index, int Ticks)>();
        foreach (var frame in frames)
        {
            if (schedule.Count > 0 && schedule[^1].Index == frame.Index)
                schedule[^1] = (frame.Index, schedule[^1].Ticks + frame.Ticks);
            else
                schedule.Add(frame);
        }
        return schedule;
    }

    private delegate int FrameSelector(string path, JsonElement animation, int frameCount, int firstIndex);

    private SKBitmap RenderItem(string itemModel, FrameSelector selectFrame = null)
    {
        var resource = ResourceLocation.Parse(itemModel, "minecraft");
        if (resource.Namespace == "minecraft")
            return null;

        using var document = ReadJson($"assets/{resource.Namespace}/items/{resource.Path}.json");
        if (!document.RootElement.TryGetProperty("model", out var model))
            throw new InvalidDataException($"Item declaration {itemModel} has no model.");

        return RenderNode(model, resource.Namespace, selectFrame);
    }

    private SKBitmap RenderNode(JsonElement node, string defaultNamespace, FrameSelector selectFrame)
    {
        var type = node.TryGetProperty("type", out var typeNode)
            ? typeNode.GetString()?.Replace("minecraft:", "", StringComparison.Ordinal)
            : "model";

        return type switch
        {
            "model" => RenderModel(node.GetProperty("model").GetString(), defaultNamespace, selectFrame),
            "select" => RenderNode(SelectGuiModel(node), defaultNamespace, selectFrame),
            "condition" => RenderNode(node.GetProperty("on_false"), defaultNamespace, selectFrame),
            "range_dispatch" => RenderNode(node.GetProperty("fallback"), defaultNamespace, selectFrame),
            "composite" => Composite(node.GetProperty("models"), defaultNamespace, selectFrame),
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

    private SKBitmap Composite(JsonElement models, string defaultNamespace, FrameSelector selectFrame)
    {
        var layers = models.EnumerateArray()
            .Select(model => RenderNode(model, defaultNamespace, selectFrame))
            .Where(bitmap => bitmap != null)
            .ToList();
        if (layers.Count == 0)
            return null;

        var result = new SKBitmap(layers.Max(layer => layer.Width), layers.Max(layer => layer.Height));
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);
        foreach (var layer in layers)
        {
            canvas.DrawBitmap(layer, 0, 0);
            layer.Dispose();
        }
        return result;
    }

    private SKBitmap RenderModel(string modelName, string defaultNamespace, FrameSelector selectFrame)
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

        using (bitmap)
        {
            using var metadata = ReadJson(path + ".mcmeta");
            var size = bitmap.Width;
            var frameIndex = 0;
            if (metadata.RootElement.TryGetProperty("animation", out var animation))
            {
                if (animation.TryGetProperty("frames", out var frames) && frames.GetArrayLength() > 0)
                {
                    var firstFrame = frames[0];
                    frameIndex = (firstFrame.ValueKind == JsonValueKind.Object ? firstFrame.GetProperty("index") : firstFrame).GetInt32();
                }
                if (selectFrame != null)
                    frameIndex = selectFrame(path, animation, bitmap.Height / size, frameIndex);
            }

            if (frameIndex < 0 || frameIndex >= bitmap.Height / size)
                throw new InvalidDataException($"Texture {path} has an invalid frame index {frameIndex}.");

            var frame = new SKBitmap();
            if (bitmap.ExtractSubset(frame, new SKRectI(0, frameIndex * size, size, (frameIndex + 1) * size)))
                return frame;
            frame.Dispose();
            throw new InvalidDataException($"Could not extract frame {frameIndex} of texture {path}.");
        }
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
