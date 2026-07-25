using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Items.Models;
using Coflnet.Sky.Items.Models.Hypixel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Items.Services;

public sealed class SkyItemIconSync
{
    private const string PacksUrl = "https://api.hypixel.net/v2/resources/packs";
    private readonly IHttpClientFactory httpClientFactory;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly IConfiguration configuration;
    private readonly ILogger<SkyItemIconSync> logger;
    private string successfulSignature;

    public SkyItemIconSync(
        IHttpClientFactory httpClientFactory,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<SkyItemIconSync> logger)
    {
        this.httpClientFactory = httpClientFactory;
        this.scopeFactory = scopeFactory;
        this.configuration = configuration;
        this.logger = logger;
    }

    public async Task SyncAsync(IReadOnlyCollection<Models.Hypixel.Item> apiItems, CancellationToken cancellationToken)
    {
        var mappings = apiItems
            .Where(item => item.ItemModel?.StartsWith("hypixel_skyblock:", StringComparison.Ordinal) == true)
            .ToDictionary(item => item.Id, item => item.ItemModel, StringComparer.Ordinal);
        if (mappings.Count == 0)
        {
            logger.LogWarning("Hypixel returned no custom item model mappings");
            return;
        }

        var client = httpClientFactory.CreateClient();
        var packs = await client.GetFromJsonAsync<ResourcePacksResponse>(PacksUrl, cancellationToken);
        var pack = packs?.Packs
            .FirstOrDefault(candidate => string.Equals(candidate.Id, "SkyBlock", StringComparison.OrdinalIgnoreCase))
            ?.Versions.MaxBy(version => version.PackFormat)
            ?? throw new InvalidDataException("Hypixel returned no SkyBlock resource pack.");

        var signature = ComputeSignature(pack.Hash, mappings);
        if (signature == successfulSignature)
            return;

        var packBytes = await client.GetByteArrayAsync(pack.Url, cancellationToken);
        var actualHash = Convert.ToHexString(SHA1.HashData(packBytes)).ToLowerInvariant();
        if (!string.Equals(actualHash, pack.Hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Resource-pack SHA-1 mismatch: expected {pack.Hash}, got {actualHash}.");

        var renderedModels = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        using (var archive = new ZipArchive(new MemoryStream(packBytes, writable: false), ZipArchiveMode.Read))
        {
            var extractor = new ResourcePackIconExtractor(archive);
            foreach (var model in mappings.Values.Distinct(StringComparer.Ordinal))
                renderedModels[model] = extractor.Extract(model);
        }

        var failures = new ConcurrentBag<string>();
        var completed = new ConcurrentBag<string>();
        var uploadBase = configuration["STATIC_S3_URL"]?.TrimEnd('/')
            ?? "http://static-s3.s3:8000";
        var concurrency = Math.Max(1, configuration.GetValue("ICON_SYNC_CONCURRENCY", 8));

        await Parallel.ForEachAsync(mappings, new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = concurrency
        }, async (mapping, token) =>
        {
            var icon = renderedModels[mapping.Value];
            if (icon == null)
                return;
            try
            {
                using var content = new ByteArrayContent(icon);
                content.Headers.ContentType = new("image/png");
                var key = $"sky/icons/{Uri.EscapeDataString(mapping.Key)}";
                using var response = await client.PutAsync($"{uploadBase}/objects/{key}", content, token);
                response.EnsureSuccessStatusCode();
                completed.Add(mapping.Key);
            }
            catch (Exception exception)
            {
                failures.Add(mapping.Key);
                logger.LogError(exception, "Uploading resource-pack icon for {Tag}", mapping.Key);
            }
        });

        await UpdateIconUrls(completed, cancellationToken);
        if (!failures.IsEmpty)
            throw new InvalidOperationException($"Failed to upload {failures.Count} resource-pack icons.");

        successfulSignature = signature;
        logger.LogInformation("Synchronized {Count} Hypixel resource-pack icons from pack {Hash}", completed.Count, pack.Hash);
    }

    internal static string ComputeSignature(string packHash, IReadOnlyDictionary<string, string> mappings)
    {
        var value = new StringBuilder(packHash);
        foreach (var mapping in mappings.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            value.Append('\n').Append(mapping.Key).Append('=').Append(mapping.Value);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString()))).ToLowerInvariant();
    }

    private async Task UpdateIconUrls(IEnumerable<string> completedTags, CancellationToken cancellationToken)
    {
        var iconBase = configuration["STATIC_ICON_BASE_URL"]?.TrimEnd('/')
            ?? "https://static.coflnet.com";
        foreach (var batch in MoreLinq.Extensions.BatchExtension.Batch(completedTags.Distinct(), 100))
        {
            var tags = batch.ToList();
            using var scope = scopeFactory.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ItemDbContext>();
            var items = await context.Items.Where(item => tags.Contains(item.Tag)).ToListAsync(cancellationToken);
            foreach (var item in items)
                item.IconUrl = $"{iconBase}/sky/icons/{Uri.EscapeDataString(item.Tag)}";
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
