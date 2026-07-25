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
        var png = fixture.Extractor.Extract("test:item/icon");
        using var bitmap = SKBitmap.Decode(png);
        Assert.Multiple(() =>
        {
            Assert.That(bitmap.Width, Is.EqualTo(1));
            Assert.That(bitmap.Height, Is.EqualTo(1));
            Assert.That(bitmap.GetPixel(0, 0), Is.EqualTo(SKColors.Red));
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

    [Test]
    public void UsesFrameZeroForAnimatedTextures()
    {
        using var fixture = PackFixture.Create("""{"type":"model","model":"test:item/animated"}""", animated: true);
        var png = fixture.Extractor.Extract("test:item/icon");
        using var bitmap = SKBitmap.Decode(png);
        Assert.That(bitmap.Height, Is.EqualTo(1));
        Assert.That(bitmap.GetPixel(0, 0), Is.EqualTo(SKColors.Red));
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
            Assert.That(extractor.Extract(model), Is.Not.Empty, model);
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

        public static PackFixture Create(string itemModel, bool animated = false)
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
                AddPng(archive, "animated", new[] { SKColors.Red, SKColors.Blue });
                if (animated)
                    Add(archive, "assets/test/textures/item/animated.png.mcmeta", """{"animation":{"frames":[0,1]}}""");
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

        private static void AddPng(ZipArchive archive, string name, IReadOnlyList<SKColor> colors)
        {
            using var bitmap = new SKBitmap(1, colors.Count, true);
            for (var y = 0; y < colors.Count; y++)
                bitmap.SetPixel(0, y, colors[y]);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var target = archive.CreateEntry($"assets/test/textures/item/{name}.png").Open();
            data.SaveTo(target);
        }
    }
}
