using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.Services.Steam;

/// <summary>Host-Implementierung von <see cref="IWorkshopService"/>. Enumeriert
/// die <c>workshop/content/&lt;appId&gt;/</c>-Ordner in allen Steam-Library-
/// Roots (auch externe Platten via libraryfolders.vdf) und reichert die Items
/// optional mit Steam-Web-API-Metadaten an (<c>ISteamRemoteStorage/GetPublishedFileDetails</c>).
///
/// <para>Kein API-Key noetig — GetPublishedFileDetails ist ein oeffentliches
/// Endpoint fuer sichtbare Workshop-Items (public visibility). Rate-Limit
/// dennoch beachten: bis 200 Items pro Request, Batching macht der Impl.</para></summary>
public sealed class HostWorkshopServiceImpl : IWorkshopService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly SteamLibraryService _library;
    private readonly HttpClient _http;

    public HostWorkshopServiceImpl(SteamLibraryService library, HttpClient http)
    {
        _library = library;
        _http = http;
    }

    public bool IsAvailable => _library.EnumerateLibraryRoots().Count > 0;

    public Task<IReadOnlyList<WorkshopItem>> DiscoverAsync(int steamAppId, CancellationToken ct = default)
    {
        var items = new List<WorkshopItem>();
        // Dedup gegen Bazzite-Symlink-Split: /var/home vs /home fuehren auf
        // den gleichen Ordner. Wir kanonisieren via GetFullPath + FileInfo.
        var seenIds = new HashSet<ulong>();
        foreach (var root in _library.EnumerateLibraryRoots())
        {
            var workshopContentDir = Path.Combine(root, "steamapps", "workshop", "content", steamAppId.ToString());
            if (!Directory.Exists(workshopContentDir)) continue;

            foreach (var dir in EnumerateChildrenSafe(workshopContentDir))
            {
                var name = Path.GetFileName(dir);
                if (!ulong.TryParse(name, out var pfid)) continue;
                if (!seenIds.Add(pfid)) continue;

                long size = 0;
                DateTime? lastUpdated = null;
                try
                {
                    var info = new DirectoryInfo(dir);
                    lastUpdated = info.LastWriteTimeUtc;
                    foreach (var f in info.EnumerateFiles("*", SearchOption.AllDirectories))
                    {
                        try { size += f.Length; } catch { }
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Workshop-Item-Groesse fehlgeschlagen: {Dir}", dir);
                }
                items.Add(new WorkshopItem(
                    PublishedFileId: pfid,
                    LocalDir: dir,
                    SizeOnDiskBytes: size,
                    LastUpdatedLocalUtc: lastUpdated));
            }
        }
        Log.Debug("Workshop-Discovery AppId {AppId}: {N} Items", steamAppId, items.Count);
        return Task.FromResult<IReadOnlyList<WorkshopItem>>(items);
    }

    public async Task<IReadOnlyList<WorkshopItem>> EnrichAsync(
        IReadOnlyList<WorkshopItem> items, CancellationToken ct = default)
    {
        if (items.Count == 0) return items;
        // Steam-Web-API: GetPublishedFileDetails ist ein POST mit
        // form-urlencoded body: itemcount=N&publishedfileids[0]=id0&publishedfileids[1]=id1…
        // Batch bis 100 Items — bei >100 mehrere Requests + Merge.
        var enriched = new List<WorkshopItem>(items.Count);
        for (int i = 0; i < items.Count; i += 100)
        {
            var batch = items.Skip(i).Take(100).ToList();
            var byId = batch.ToDictionary(b => b.PublishedFileId);
            try
            {
                var form = new List<KeyValuePair<string, string>>
                {
                    new("itemcount", batch.Count.ToString()),
                };
                for (int j = 0; j < batch.Count; j++)
                    form.Add(new($"publishedfileids[{j}]", batch[j].PublishedFileId.ToString()));

                using var content = new FormUrlEncodedContent(form);
                using var resp = await _http.PostAsync(
                    "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/",
                    content, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    Log.Debug("Workshop-API HTTP {Code} — keine Metadaten fuer Batch {I}",
                        (int)resp.StatusCode, i);
                    foreach (var b in batch) enriched.Add(b);
                    continue;
                }
                var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var parsed = JsonSerializer.Deserialize<WorkshopResponse>(json, JsonOpts);
                if (parsed?.Response?.PublishedFileDetails is null)
                {
                    foreach (var b in batch) enriched.Add(b);
                    continue;
                }
                foreach (var det in parsed.Response.PublishedFileDetails)
                {
                    if (!ulong.TryParse(det.PublishedFileId, out var id)) continue;
                    if (!byId.TryGetValue(id, out var orig)) continue;
                    var updatedUtc = det.TimeUpdated > 0
                        ? DateTimeOffset.FromUnixTimeSeconds(det.TimeUpdated).UtcDateTime
                        : (DateTime?)null;
                    enriched.Add(orig with
                    {
                        Title = det.Title,
                        Description = det.Description,
                        PreviewUrl = det.PreviewUrl,
                        Author = det.Creator,
                        SubscriberCount = det.Subscriptions,
                        UpdatedUtc = updatedUtc,
                    });
                    byId.Remove(id);
                }
                // Nicht-gematchte durchreichen
                foreach (var leftover in byId.Values) enriched.Add(leftover);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Workshop-Enrichment fehlgeschlagen fuer Batch {I}", i);
                foreach (var b in batch) enriched.Add(b);
            }
        }
        return enriched;
    }

    private static IEnumerable<string> EnumerateChildrenSafe(string dir)
    {
        try { return Directory.EnumerateDirectories(dir); }
        catch { return Array.Empty<string>(); }
    }

    // ---- Steam-Web-API JSON DTOs ----
    private sealed class WorkshopResponse { public InnerResponse? Response { get; set; } }
    private sealed class InnerResponse
    {
        public int Result { get; set; }
        public int ResultCount { get; set; }
        public List<WorkshopDetail>? PublishedFileDetails { get; set; }
    }
    private sealed class WorkshopDetail
    {
        public string? PublishedFileId { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? PreviewUrl { get; set; }
        public string? Creator { get; set; }
        public int Subscriptions { get; set; }
        public long TimeUpdated { get; set; }
    }
}
