using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using SkiaSharp;

namespace Coflnet.Sky.Items.Services;

[TestFixture]
public class ResourcePackIconExtractorTests
{
    [Test]
    public void RendersOrdinaryModelWithUnnamespacedReference()
    {
        using var fixture = PackFixture.Create("""{"type":"model","model":"item/red"}""");
        var (png, animated) = fixture.Extractor.ExtractIcon("test:item/icon");
        using var bitmap = SKBitmap.Decode(png);
        Assert.Multiple(() =>
        {
            Assert.That(bitmap.Width, Is.EqualTo(1));
            Assert.That(bitmap.Height, Is.EqualTo(1));
            Assert.That(bitmap.GetPixel(0, 0), Is.EqualTo(SKColors.Red));
            Assert.That(animated, Is.False);
        });
    }

    [Test]
    public void ResolvesInheritedTextureAlias()
    {
        using var fixture = PackFixture.Create("""{"type":"model","model":"test:item/aliased"}""");
        Assert.That(Pixel(fixture.Extractor.Extract("test:item/icon")), Is.EqualTo(SKColors.Red));
    }

    [TestCase("""{"type":"select","cases":[{"when":["gui","ground"],"model":{"type":"model","model":"test:item/red"}}],"fallback":{"type":"model","model":"test:item/blue"}}""")]
    [TestCase("""{"type":"condition","on_false":{"type":"model","model":"test:item/red"},"on_true":{"type":"model","model":"test:item/blue"}}""")]
    [TestCase("""{"type":"range_dispatch","fallback":{"type":"model","model":"test:item/red"},"entries":[]}""")]
    public void SelectsTheStaticGuiModel(string model)
    {
        using var fixture = PackFixture.Create(model);
        var png = fixture.Extractor.Extract("test:item/icon");
        Assert.That(Pixel(png), Is.EqualTo(SKColors.Red));
    }

    [Test]
    public void CompositesLayersInOrder()
    {
        using var fixture = PackFixture.Create("""{"type":"composite","models":[{"type":"model","model":"test:item/red"},{"type":"model","model":"test:item/blue"}]}""");
        var png = fixture.Extractor.Extract("test:item/icon");
        Assert.That(Pixel(png), Is.EqualTo(SKColors.Blue));
    }

    [TestCase("""{"animation":{}}""", 0)]
    [TestCase("""{"animation":{"frames":[1,0]}}""", 1)]
    [TestCase("""{"animation":{"frames":[{"index":1,"time":2},0]}}""", 1)]
    public void RendersTheFirstDeclaredAnimationFrame(string animation, int frameIndex)
    {
        using var fixture = PackFixture.Create("""{"type":"model","model":"test:item/animated"}""", animation);
        var png = fixture.Extractor.Extract("test:item/icon");
        using var bitmap = SKBitmap.Decode(png);
        Assert.Multiple(() =>
        {
            Assert.That(bitmap.Width, Is.EqualTo(2));
            Assert.That(bitmap.Height, Is.EqualTo(2));
            Assert.That(bitmap.GetPixel(0, 0).Alpha, Is.EqualTo(frameIndex == 0 ? byte.MaxValue : 0));
            Assert.That(bitmap.GetPixel(1, 0), Is.EqualTo(frameIndex == 0 ? SKColors.White : SKColors.Blue));
            Assert.That(bitmap.GetPixel(0, 1), Is.EqualTo(frameIndex == 0 ? SKColors.Black : SKColors.Lime));
            Assert.That(bitmap.GetPixel(1, 1).Alpha, Is.EqualTo(frameIndex == 0 ? byte.MaxValue : 0));
        });
    }

    [Test]
    public void EncodesGifWithDeclaredFrameOrderDurationsAndTransparency()
    {
        using var fixture = PackFixture.Create(
            """{"type":"composite","models":[{"type":"model","model":"test:item/red"},{"type":"model","model":"test:item/animated"}]}""",
            """{"animation":{"frametime":2,"frames":[1,{"index":1,"time":3},0,1]}}""");
        var (gif, animated) = fixture.Extractor.ExtractIcon("test:item/icon");
        using var data = SKData.CreateCopy(gif);
        using var codec = SKCodec.Create(data);
        Assert.Multiple(() =>
        {
            Assert.That(animated, Is.True);
            Assert.That(codec.EncodedFormat, Is.EqualTo(SKEncodedImageFormat.Gif));
            Assert.That(codec.Info.Size, Is.EqualTo(new SKSizeI(64, 64)));
            Assert.That(codec.RepetitionCount, Is.EqualTo(-1));
            Assert.That(codec.FrameInfo.Select(frame => frame.Duration), Is.EqualTo(new[] { 250, 100, 100 }));
        });

        var colors = new[] { SKColors.Blue, SKColors.White, SKColors.Blue };
        for (var index = 0; index < colors.Length; index++)
        {
            using var bitmap = new SKBitmap(codec.Info.WithAlphaType(SKAlphaType.Premul));
            Assert.That(codec.GetPixels(bitmap.Info, bitmap.GetPixels(), new SKCodecOptions(index)),
                Is.EqualTo(SKCodecResult.Success));
            Assert.Multiple(() =>
            {
                Assert.That(bitmap.GetPixel(31, 31), Is.EqualTo(SKColors.Red));
                Assert.That(bitmap.GetPixel(32, 31), Is.EqualTo(colors[index]));
                Assert.That(bitmap.GetPixel(63, 0), Is.EqualTo(colors[index]));
                Assert.That(bitmap.GetPixel(32, 32).Alpha, Is.EqualTo(index == 1 ? byte.MaxValue : 0));
            });
        }
    }

    [Test]
    public void InterpolatesRgbBetweenDeclaredFramesAndPreservesHoldsAndSourceAlpha()
    {
        using var fixture = PackFixture.Create(
            """{"type":"model","model":"test:item/animated"}""",
            """{"animation":{"frametime":2,"interpolate":true,"frames":[1,{"index":1,"time":3},0]}}""");
        var (gif, animated) = fixture.Extractor.ExtractIcon("test:item/icon");
        using var data = SKData.CreateCopy(gif);
        using var codec = SKCodec.Create(data);
        Assert.That(animated, Is.True);
        Assert.That(codec.FrameInfo.Select(frame => frame.Duration), Is.EqualTo(new[] { 150, 50, 50, 50, 50 }));

        var colors = new[] { SKColors.Blue, new SKColor(85, 85, 255), new SKColor(170, 170, 255),
            SKColors.White, new SKColor(127, 127, 255) };
        var alphas = new[] { 0, 0, 0, 255, 255 };
        for (var index = 0; index < colors.Length; index++)
        {
            using var bitmap = new SKBitmap(codec.Info.WithAlphaType(SKAlphaType.Premul));
            Assert.That(codec.GetPixels(bitmap.Info, bitmap.GetPixels(), new SKCodecOptions(index)),
                Is.EqualTo(SKCodecResult.Success));
            Assert.Multiple(() =>
            {
                Assert.That(bitmap.GetPixel(32, 0), Is.EqualTo(colors[index]));
                Assert.That(bitmap.GetPixel(0, 0).Alpha, Is.EqualTo(alphas[index]));
            });
        }
    }

    [Test]
    public void CompositesAnimatedLayersWithTransparency()
    {
        using var fixture = PackFixture.Create(
            """{"type":"composite","models":[{"type":"model","model":"test:item/red"},{"type":"model","model":"test:item/animated"}]}""",
            """{"animation":{"frames":[1,0]}}""");
        var png = fixture.Extractor.Extract("test:item/icon");
        using var bitmap = SKBitmap.Decode(png);
        Assert.Multiple(() =>
        {
            Assert.That(bitmap.GetPixel(0, 0), Is.EqualTo(SKColors.Red));
            Assert.That(bitmap.GetPixel(1, 0), Is.EqualTo(SKColors.Blue));
            Assert.That(bitmap.GetPixel(1, 1).Alpha, Is.Zero);
        });
    }

    [Test]
    public void SkipsVanillaModels()
    {
        using var fixture = PackFixture.Create("""{"type":"model","model":"minecraft:item/paper"}""");
        Assert.That(fixture.Extractor.Extract("test:item/icon"), Is.Null);
    }

    [Test]
    public void MissingCustomModelFails()
    {
        using var fixture = PackFixture.Create("""{"type":"model","model":"test:item/missing"}""");
        Assert.Throws<FileNotFoundException>(() => fixture.Extractor.Extract("test:item/icon"));
    }

    [Test]
    public void CurrentHypixelPackRendersEveryCustomMapping()
    {
        var packPath = Environment.GetEnvironmentVariable("HYPIXEL_PACK_TEST_PATH");
        var itemsPath = Environment.GetEnvironmentVariable("HYPIXEL_ITEMS_TEST_PATH");
        if (string.IsNullOrWhiteSpace(packPath) || string.IsNullOrWhiteSpace(itemsPath))
            Assert.Ignore("Set HYPIXEL_PACK_TEST_PATH and HYPIXEL_ITEMS_TEST_PATH to run the live-pack fixture.");

        using var itemDocument = JsonDocument.Parse(File.ReadAllBytes(itemsPath));
        var models = itemDocument.RootElement.GetProperty("items").EnumerateArray()
            .Where(item => item.TryGetProperty("item_model", out _))
            .Select(item => item.GetProperty("item_model").GetString())
            .Where(model => model?.StartsWith("hypixel_skyblock:", StringComparison.Ordinal) == true)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        using var pack = ZipFile.OpenRead(packPath);
        var extractor = new ResourcePackIconExtractor(pack);

        foreach (var model in models)
            Assert.That(extractor.ExtractIcon(model).Image, Is.Not.Empty, model);
    }

    private static SKColor Pixel(byte[] png)
    {
        using var bitmap = SKBitmap.Decode(png);
        return bitmap.GetPixel(0, 0);
    }

    private sealed class PackFixture : IDisposable
    {
        private readonly MemoryStream stream;
        private readonly ZipArchive archive;
        public ResourcePackIconExtractor Extractor { get; }

        private PackFixture(MemoryStream stream, ZipArchive archive)
        {
            this.stream = stream;
            this.archive = archive;
            Extractor = new ResourcePackIconExtractor(archive);
        }

        public static PackFixture Create(string itemModel, string animation = null)
        {
            var stream = new MemoryStream();
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                Add(archive, "assets/test/items/item/icon.json", "{\"model\":" + itemModel + "}");
                AddModel(archive, "red");
                AddModel(archive, "blue");
                AddModel(archive, "animated");
                Add(archive, "assets/test/models/item/template.json",
                    """{"textures":{"layer0":"#icon"}}""");
                Add(archive, "assets/test/models/item/aliased.json",
                    """{"parent":"test:item/template","textures":{"icon":"test:item/red"}}""");
                AddPng(archive, "red", new[] { SKColors.Red });
                AddPng(archive, "blue", new[] { SKColors.Blue });
                AddPng(archive, "animated", new[]
                {
                    SKColors.Red, SKColors.White,
                    SKColors.Black, SKColors.Yellow,
                    SKColors.Transparent, SKColors.Blue,
                    SKColors.Lime, SKColors.Transparent
                }, width: 2);
                if (animation != null)
                    Add(archive, "assets/test/textures/item/animated.png.mcmeta", animation);
            }
            stream.Position = 0;
            var readArchive = new ZipArchive(stream, ZipArchiveMode.Read);
            return new PackFixture(stream, readArchive);
        }

        public void Dispose()
        {
            archive.Dispose();
            stream.Dispose();
        }

        private static void AddModel(ZipArchive archive, string name) =>
            Add(archive, $"assets/test/models/item/{name}.json",
                "{\"parent\":\"minecraft:item/generated\",\"textures\":{\"layer0\":\"test:item/" + name + "\"}}");

        private static void Add(ZipArchive archive, string path, string value)
        {
            using var writer = new StreamWriter(archive.CreateEntry(path).Open());
            writer.Write(value);
        }

        private static void AddPng(ZipArchive archive, string name, IReadOnlyList<SKColor> colors, int width = 1)
        {
            using var bitmap = new SKBitmap(width, colors.Count / width);
            for (var index = 0; index < colors.Count; index++)
                bitmap.SetPixel(index % width, index / width, colors[index]);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var target = archive.CreateEntry($"assets/test/textures/item/{name}.png").Open();
            data.SaveTo(target);
        }
    }
}
