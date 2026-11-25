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
        return EncodePng(bitmap);
    }

    private static byte[] EncodePng(SKBitmap bitmap)
    {
        if (bitmap == null)
            return null;

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public (byte[] Image, bool Animated) ExtractIcon(string itemModel)
    {
        // Use the first animated texture's timeline and keep the other layers on their first frames.
        string animatedPath = null;
        List<(AnimationSample Sample, int Ticks)> frames = null;
        AnimationSample SelectAnimation(string path, JsonElement animation, int frameCount, int firstIndex)
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
            return new AnimationSample(firstIndex);
        }

        using var firstFrame = RenderItem(itemModel, SelectAnimation);
        if (frames == null)
            return (EncodePng(firstFrame), false);

        const int minimumGifSize = 64;
        var longestEdge = Math.Max(firstFrame.Width, firstFrame.Height);
        var upscale = longestEdge < minimumGifSize;
        var size = upscale
            ? new System.Drawing.Size(Math.Max(1, firstFrame.Width * minimumGifSize / longestEdge),
                Math.Max(1, firstFrame.Height * minimumGifSize / longestEdge))
            : new System.Drawing.Size(firstFrame.Width, firstFrame.Height);
        using var output = new MemoryStream();
        using var encoder = new GifEncoder(output, size)
        {
            RepeatCount = 0
        };
        var quantizer = OptimizedPaletteQuantizer.Octree().ConfigureBitLevel(8);
        void AddFrame(SKBitmap bitmap, int ticks)
        {
            using var scaled = upscale
                ? bitmap.Resize(bitmap.Info.WithSize(size.Width, size.Height), new SKSamplingOptions(SKFilterMode.Nearest))
                    ?? throw new InvalidOperationException("Could not resize the GIF frame.")
                : null;
            using var pixels = (scaled ?? bitmap).GetReadableBitmapData();
            using var indexed = pixels.Clone(KnownPixelFormat.Format8bppIndexed, quantizer);
            // Each Minecraft tick is five centiseconds, the unit used by GIF delays.
            encoder.AddImage(indexed, delay: ticks * 5, disposalMethod: GifGraphicDisposalMethod.RestoreToBackground);
        }

        AddFrame(firstFrame, frames[0].Ticks);
        foreach (var (sample, ticks) in frames.Skip(1))
        {
            using var frame = RenderItem(itemModel, (path, _, _, firstIndex) =>
                path == animatedPath ? sample : new AnimationSample(firstIndex));
            AddFrame(frame, ticks);
        }
        encoder.FinalizeEncoding();
        return (output.ToArray(), true);
    }

    private static List<(AnimationSample Sample, int Ticks)> ReadAnimationFrames(JsonElement animation, int frameCount)
    {
        var frameTime = animation.TryGetProperty("frametime", out var duration) ? duration.GetInt32() : 1;
        var frames = (animation.TryGetProperty("frames", out var declared) && declared.GetArrayLength() > 0
            ? declared.EnumerateArray().Select(frame => frame.ValueKind == JsonValueKind.Object
                ? (Index: frame.GetProperty("index").GetInt32(), Ticks: frame.TryGetProperty("time", out var time) ? time.GetInt32() : frameTime)
                : (Index: frame.GetInt32(), Ticks: frameTime))
            : Enumerable.Range(0, frameCount).Select(index => (Index: index, Ticks: frameTime))).ToArray();
        var interpolate = animation.TryGetProperty("interpolate", out var interpolation) && interpolation.ValueKind == JsonValueKind.True;
        var schedule = new List<(AnimationSample Sample, int Ticks)>();
        void AddSample(AnimationSample sample, int ticks)
        {
            if (schedule.Count > 0 && schedule[^1].Sample == sample)
                schedule[^1] = (sample, schedule[^1].Ticks + ticks);
            else
                schedule.Add((sample, ticks));
        }
        for (var i = 0; i < frames.Length; i++)
        {
            var (index, ticks) = frames[i];
            var nextIndex = frames[(i + 1) % frames.Length].Index;
            var blend = interpolate && nextIndex != index && ticks > 1;
            AddSample(new AnimationSample(index), blend ? 1 : ticks);
            if (blend)
                for (var tick = 1; tick < ticks; tick++)
                    AddSample(new AnimationSample(index, nextIndex, (double)tick / ticks), 1);
        }
        return schedule;
    }

    private readonly record struct AnimationSample(int Index, int NextIndex = 0, double Blend = 0);

    private delegate AnimationSample FrameSelector(string path, JsonElement animation, int frameCount, int firstIndex);

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
            AnimationSample sample = default;
            if (metadata.RootElement.TryGetProperty("animation", out var animation))
            {
                var firstIndex = 0;
                if (animation.TryGetProperty("frames", out var frames) && frames.GetArrayLength() > 0)
                {
                    var firstFrame = frames[0];
                    firstIndex = (firstFrame.ValueKind == JsonValueKind.Object ? firstFrame.GetProperty("index") : firstFrame).GetInt32();
                }
                sample = selectFrame?.Invoke(path, animation, bitmap.Height / size, firstIndex) ?? new AnimationSample(firstIndex);
            }

            if (sample.Index < 0 || sample.Index >= bitmap.Height / size)
                throw new InvalidDataException($"Texture {path} has an invalid frame index {sample.Index}.");

            if (sample.Blend > 0)
            {
                if (sample.NextIndex < 0 || sample.NextIndex >= bitmap.Height / size)
                    throw new InvalidDataException($"Texture {path} has an invalid frame index {sample.NextIndex}.");
                return BlendFrames(bitmap, sample);
            }

            var frame = new SKBitmap();
            var top = sample.Index * size;
            if (bitmap.ExtractSubset(frame, new SKRectI(0, top, size, top + size)))
                return frame;
            frame.Dispose();
            throw new InvalidDataException($"Could not extract frame {sample.Index} of texture {path}.");
        }
    }

    private static SKBitmap BlendFrames(SKBitmap bitmap, AnimationSample sample)
    {
        var size = bitmap.Width;
        var frame = new SKBitmap(size, size);
        byte Blend(byte from, byte to) => (byte)(from + (to - from) * sample.Blend);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var from = bitmap.GetPixel(x, sample.Index * size + y);
                var to = bitmap.GetPixel(x, sample.NextIndex * size + y);
                frame.SetPixel(x, y, new SKColor(Blend(from.Red, to.Red), Blend(from.Green, to.Green),
                    Blend(from.Blue, to.Blue), from.Alpha));
            }
        }
        return frame;
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
