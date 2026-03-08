using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tracer.Core.Entities;
using Tracer.Core.Options;
using Tracer.Infrastructure.Persistence;

namespace Tracer.Infrastructure.Services;

public sealed class OuiVendorLookupService(
    IDbContextFactory<TracerDbContext> dbContextFactory,
    IHttpClientFactory httpClientFactory,
    IOptions<OuiVendorOptions> ouiVendorOptions,
    ILogger<OuiVendorLookupService> logger)
{
    private static readonly IReadOnlyDictionary<string, (string VendorName, string? Country)> FallbackVendors =
        new Dictionary<string, (string VendorName, string? Country)>(StringComparer.OrdinalIgnoreCase)
        {
            ["00:1A:11"] = ("Google", "US"),
            ["00:1B:63"] = ("Apple", "US"),
            ["00:1D:D8"] = ("Cisco", "US"),
            ["00:25:9C"] = ("Apple", "US"),
            ["00:26:BB"] = ("Samsung", "KR"),
            ["00:50:56"] = ("VMware", "US"),
            ["04:52:C7"] = ("Microsoft", "US"),
            ["08:00:27"] = ("Oracle VirtualBox", "US"),
            ["18:65:90"] = ("Samsung", "KR"),
            ["28:CF:DA"] = ("Apple", "US"),
            ["3C:5A:B4"] = ("Google", "US"),
            ["40:B0:76"] = ("Intel", "US"),
            ["58:CB:52"] = ("Google", "US"),
            ["68:54:5A"] = ("Intel", "US"),
            ["74:E5:43"] = ("Apple", "US"),
            ["7C:B0:C2"] = ("Samsung", "KR"),
            ["A4:83:E7"] = ("Apple", "US"),
            ["B8:27:EB"] = ("Raspberry Pi", "GB"),
            ["C0:25:E9"] = ("Apple", "US"),
            ["D8:3A:DD"] = ("Google", "US"),
            ["DC:A6:32"] = ("Raspberry Pi", "GB"),
            ["F4:F5:D8"] = ("Apple", "US")
        };

    private readonly OuiVendorOptions _options = ouiVendorOptions.Value;
    private readonly ConcurrentDictionary<string, OuiVendorCacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _cacheLoaded;

    public static IReadOnlyCollection<OuiVendor> GetFallbackSeedVendors()
    {
        return FallbackVendors
            .Select(x => new OuiVendor
            {
                Prefix = x.Key,
                VendorName = x.Value.VendorName,
                Country = x.Value.Country,
                UpdatedAt = DateTimeOffset.UtcNow
            })
            .ToArray();
    }

    public string? ResolveVendorName(string? prefix)
    {
        var normalizedPrefix = NormalizePrefix(prefix);
        if (normalizedPrefix is null)
        {
            return null;
        }

        if (_cache.TryGetValue(normalizedPrefix, out var cached))
        {
            return cached.VendorName;
        }

        return FallbackVendors.TryGetValue(normalizedPrefix, out var fallback)
            ? fallback.VendorName
            : null;
    }

    public string? ResolveCountry(string? prefix)
    {
        var normalizedPrefix = NormalizePrefix(prefix);
        if (normalizedPrefix is null)
        {
            return null;
        }

        if (_cache.TryGetValue(normalizedPrefix, out var cached))
        {
            return cached.Country;
        }

        return FallbackVendors.TryGetValue(normalizedPrefix, out var fallback)
            ? fallback.Country
            : null;
    }

    public async Task WarmCacheAsync(CancellationToken cancellationToken)
    {
        if (_cacheLoaded)
        {
            return;
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var vendors = await dbContext.OuiVendors
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        _cache.Clear();
        foreach (var vendor in vendors)
        {
            _cache[vendor.Prefix] = new OuiVendorCacheEntry(vendor.VendorName, vendor.Country, vendor.UpdatedAt);
        }

        _cacheLoaded = true;
    }

    public async Task<OuiVendorCacheStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        await WarmCacheAsync(cancellationToken);
        var lastUpdatedUtc = _cache.Count == 0
            ? (DateTimeOffset?)null
            : _cache.Values.MaxBy(x => x.UpdatedAt)?.UpdatedAt;

        return new OuiVendorCacheStatus(_cache.Count, lastUpdatedUtc, _options.EnableRemoteRefresh);
    }

    public async Task<OuiVendorRefreshResult> RefreshAsync(CancellationToken cancellationToken)
    {
        await WarmCacheAsync(cancellationToken);

        if (!_options.EnableRemoteRefresh)
        {
            return new OuiVendorRefreshResult(0, false, "Remote refresh is disabled.");
        }

        var currentStatus = await GetStatusAsync(cancellationToken);
        if (currentStatus.LastUpdatedUtc.HasValue
            && currentStatus.LastUpdatedUtc.Value >= DateTimeOffset.UtcNow.AddDays(-Math.Max(1, _options.RefreshIntervalDays)))
        {
            return new OuiVendorRefreshResult(currentStatus.Count, false, "Vendor cache is already fresh.");
        }

        var client = httpClientFactory.CreateClient(nameof(OuiVendorLookupService));
        using var response = await client.GetAsync(_options.DownloadUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var vendors = ParseCsv(payload);

        if (vendors.Count == 0)
        {
            return new OuiVendorRefreshResult(0, false, "Vendor download returned no usable rows.");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.OuiVendors.ExecuteDeleteAsync(cancellationToken);

        foreach (var batch in vendors.Chunk(2000))
        {
            dbContext.OuiVendors.AddRange(batch);
            await dbContext.SaveChangesAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
        }

        _cache.Clear();
        foreach (var vendor in vendors)
        {
            _cache[vendor.Prefix] = new OuiVendorCacheEntry(vendor.VendorName, vendor.Country, vendor.UpdatedAt);
        }

        _cacheLoaded = true;
        logger.LogInformation("Refreshed OUI vendor cache with {VendorCount} entries.", vendors.Count);
        return new OuiVendorRefreshResult(vendors.Count, true, null);
    }

    public static string? NormalizePrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return null;
        }

        var hex = new string(prefix
            .Where(char.IsAsciiHexDigit)
            .Take(6)
            .ToArray());

        if (hex.Length != 6)
        {
            return null;
        }

        return string.Create(8, hex.ToUpperInvariant(), static (span, value) =>
        {
            span[0] = value[0];
            span[1] = value[1];
            span[2] = ':';
            span[3] = value[2];
            span[4] = value[3];
            span[5] = ':';
            span[6] = value[4];
            span[7] = value[5];
        });
    }

    private static List<OuiVendor> ParseCsv(string payload)
    {
        var rows = new List<OuiVendor>();
        var lines = payload.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length <= 1)
        {
            return rows;
        }

        var headers = SplitCsvLine(lines[0]);
        var assignmentIndex = headers.FindIndex(x => x.Equals("Assignment", StringComparison.OrdinalIgnoreCase));
        var orgNameIndex = headers.FindIndex(x => x.Equals("Organization Name", StringComparison.OrdinalIgnoreCase));
        var orgAddressIndex = headers.FindIndex(x => x.Equals("Organization Address", StringComparison.OrdinalIgnoreCase));

        if (assignmentIndex < 0 || orgNameIndex < 0)
        {
            return rows;
        }

        for (var index = 1; index < lines.Length; index++)
        {
            var columns = SplitCsvLine(lines[index]);
            if (columns.Count <= Math.Max(assignmentIndex, orgNameIndex))
            {
                continue;
            }

            var prefix = NormalizePrefix(columns[assignmentIndex]);
            if (prefix is null)
            {
                continue;
            }

            var vendorName = columns[orgNameIndex].Trim();
            if (string.IsNullOrWhiteSpace(vendorName))
            {
                continue;
            }

            var country = orgAddressIndex >= 0 && columns.Count > orgAddressIndex
                ? ExtractCountry(columns[orgAddressIndex])
                : null;

            rows.Add(new OuiVendor
            {
                Prefix = prefix,
                VendorName = vendorName,
                Country = country,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        return rows
            .GroupBy(x => x.Prefix, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static List<string> SplitCsvLine(string line)
    {
        var values = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];

            if (character == '"')
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (character == ',' && !inQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        values.Add(current.ToString());
        return values;
    }

    private static string? ExtractCountry(string organizationAddress)
    {
        if (string.IsNullOrWhiteSpace(organizationAddress))
        {
            return null;
        }

        var segments = organizationAddress
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return segments.Length == 0
            ? null
            : segments[^1];
    }

    private sealed record OuiVendorCacheEntry(string VendorName, string? Country, DateTimeOffset UpdatedAt);
}

public sealed record OuiVendorCacheStatus(int Count, DateTimeOffset? LastUpdatedUtc, bool RemoteRefreshEnabled);

public sealed record OuiVendorRefreshResult(int ImportedCount, bool Refreshed, string? Message);
