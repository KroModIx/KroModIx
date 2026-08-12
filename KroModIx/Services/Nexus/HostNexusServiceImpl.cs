using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.Services.Nexus;

/// <summary>Host-Impl von <see cref="INexusService"/> — ein einziger Nexus-
/// Mods-Client für alle Plugins (Contracts v1.14.0+, portiert aus dem
/// Icarus-Plugin v1.14 wo der Client zuerst lebte).
///
/// <para>API-Key kommt aus <see cref="NexusApiKeyStore"/> (DPAPI/libsecret-
/// verschlüsselt im Host-<c>AppSettings</c>). Ein <c>HttpClient</c>-Handle
/// wird über <c>IHttpClientFactory</c> mit User-Agent
/// „KroModIx/&lt;version&gt;" versorgt — Nexus verlangt UA-Header, sonst
/// 403. Kategorien werden pro Game-Slug in-memory gecacht (1 API-Call pro
/// Session pro Spiel).</para></summary>
public sealed class HostNexusServiceImpl : INexusService, IDisposable
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly HttpClient _http;
    private readonly NexusApiKeyStore _keyStore;
    private readonly ConcurrentDictionary<string, IReadOnlyList<NexusCategory>> _categoryCache = new();

    private volatile string _userName = "";
    private volatile bool _isPremium;

    public HostNexusServiceImpl(IHttpClientFactory httpFactory, NexusApiKeyStore keyStore)
    {
        _keyStore = keyStore;
        _http = httpFactory.CreateClient("nexus");
        _http.BaseAddress ??= new Uri("https://api.nexusmods.com/");
        if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
            _http.DefaultRequestHeaders.Add("User-Agent", "KroModIx/1.14 (github.com/KroModIx)");
        if (!_http.DefaultRequestHeaders.Contains("Accept"))
            _http.DefaultRequestHeaders.Add("Accept", "application/json");
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    public void Dispose() => _http.Dispose();

    public bool HasApiKey => !string.IsNullOrEmpty(_keyStore.GetApiKey());
    public string UserName => _userName;
    public bool IsPremium => _isPremium;

    public event EventHandler? ApiKeyChanged;

    /// <summary>Vom <see cref="NexusApiKeyStore"/> nach Save/Delete aufgerufen —
    /// signalisiert allen Plugins dass sich der Key-Status geändert hat
    /// (Katalog neu laden, Buttons re-enabled).</summary>
    internal void RaiseApiKeyChanged()
    {
        _categoryCache.Clear();
        _userName = "";
        _isPremium = false;
        ApiKeyChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<NexusValidateResult> ValidateAsync(CancellationToken ct = default)
    {
        var key = _keyStore.GetApiKey();
        if (string.IsNullOrEmpty(key))
            return new NexusValidateResult(false, "", false, "Kein API-Key konfiguriert.");
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "v1/users/validate.json");
            req.Headers.Add("apikey", key);
            using var resp = await _http.SendAsync(req, ct);
            LogRateLimit(resp);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return new NexusValidateResult(false, "", false,
                    $"HTTP {(int)resp.StatusCode}: {resp.ReasonPhrase}");

            var user = JsonSerializer.Deserialize<NexusUser>(body, JsonOpts);
            if (user is null)
                return new NexusValidateResult(false, "", false, "Leere Antwort von Nexus.");
            _userName = user.Name ?? "";
            _isPremium = user.IsPremium;
            // v1.14.2: Nach Validate erneut ApiKeyChanged feuern damit
            // Plugins den frisch ermittelten IsPremium-Status uebernehmen
            // koennen. Sequenz beim User-Save war: Save() → RaiseApiKeyChanged
            // (reset _isPremium=false) → Plugin liest false → Validate() setzt
            // _isPremium=true, aber kein Event mehr → Plugin-Rows stecken
            // mit IsPremium=false fest. Der zusaetzliche Event nach Validate
            // schliesst diese Luecke.
            ApiKeyChanged?.Invoke(this, EventArgs.Empty);
            return new NexusValidateResult(true, _userName, _isPremium,
                $"{_userName} (Premium: {(_isPremium ? "ja" : "nein")})");
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Nexus validate fehlgeschlagen");
            return new NexusValidateResult(false, "", false, ex.Message);
        }
    }

    public async Task<IReadOnlyList<NexusCatalogEntry>> GetLatestModsAsync(
        string gameSlug, string endpoint, CancellationToken ct = default)
    {
        var key = _keyStore.GetApiKey();
        if (string.IsNullOrEmpty(key)) return Array.Empty<NexusCatalogEntry>();
        if (endpoint is not ("latest_added" or "latest_updated" or "trending"))
            throw new ArgumentException(
                "endpoint muss latest_added | latest_updated | trending sein", nameof(endpoint));

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"v1/games/{gameSlug}/mods/{endpoint}.json");
            req.Headers.Add("apikey", key);
            using var resp = await _http.SendAsync(req, ct);
            LogRateLimit(resp);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadAsStringAsync(ct);
            var mods = JsonSerializer.Deserialize<List<NexusMod>>(body, JsonOpts) ?? new();
            var result = new List<NexusCatalogEntry>(mods.Count);
            foreach (var m in mods)
            {
                result.Add(new NexusCatalogEntry(
                    ModId: m.ModId,
                    Name: m.Name ?? "",
                    Author: m.User?.Name ?? m.Author ?? "",
                    Summary: m.Summary ?? "",
                    Category: "",
                    Version: m.Version ?? "",
                    PictureUrl: m.PictureUrl ?? "",
                    UpdatedUtc: FromUnixSeconds(m.UpdatedTimestamp ?? 0),
                    Downloads: 0,
                    Endorsements: (int)(m.EndorsementCount ?? 0),
                    Available: m.Available));
            }
            return result;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Nexus GetLatestModsAsync fehlgeschlagen: {Slug} {Endpoint}", gameSlug, endpoint);
            return Array.Empty<NexusCatalogEntry>();
        }
    }

    public async Task<IReadOnlyList<int>> GetUpdatedModIdsAsync(
        string gameSlug, string period, CancellationToken ct = default)
    {
        var key = _keyStore.GetApiKey();
        if (string.IsNullOrEmpty(key)) return Array.Empty<int>();
        if (period is not ("1d" or "1w" or "1m"))
            throw new ArgumentException("period muss 1d | 1w | 1m sein", nameof(period));

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"v1/games/{gameSlug}/mods/updated.json?period={period}");
            req.Headers.Add("apikey", key);
            using var resp = await _http.SendAsync(req, ct);
            LogRateLimit(resp);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadAsStringAsync(ct);
            var list = JsonSerializer.Deserialize<List<UpdatedRow>>(body, JsonOpts) ?? new();
            var ids = new HashSet<int>();
            foreach (var r in list) ids.Add(r.ModId);
            return new List<int>(ids);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Nexus GetUpdatedModIdsAsync fehlgeschlagen: {Slug} {Period}", gameSlug, period);
            return Array.Empty<int>();
        }
    }

    public async Task<NexusModDetail?> GetModDetailAsync(
        string gameSlug, int modId, CancellationToken ct = default)
    {
        var key = _keyStore.GetApiKey();
        if (string.IsNullOrEmpty(key)) return null;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"v1/games/{gameSlug}/mods/{modId}.json");
            req.Headers.Add("apikey", key);
            using var resp = await _http.SendAsync(req, ct);
            LogRateLimit(resp);
            if (!resp.IsSuccessStatusCode)
            {
                Log.Warn("Nexus detail HTTP {Code} für mod_id={Id}", (int)resp.StatusCode, modId);
                return null;
            }
            var body = await resp.Content.ReadAsStringAsync(ct);
            var m = JsonSerializer.Deserialize<NexusModFull>(body, JsonOpts);
            if (m is null) return null;
            return new NexusModDetail(
                ModId: m.ModId,
                Name: m.Name ?? "",
                Author: m.User?.Name ?? m.Author ?? "",
                Summary: m.Summary ?? "",
                DescriptionHtml: m.Description ?? "",
                Version: m.Version ?? "",
                PictureUrl: m.PictureUrl ?? "",
                CategoryId: m.CategoryId,
                CreatedUtc: FromUnixSeconds(m.CreatedTimestamp ?? 0),
                UpdatedUtc: FromUnixSeconds(m.UpdatedTimestamp ?? 0),
                EndorsementCount: (int)(m.EndorsementCount ?? 0),
                ContainsAdultContent: m.ContainsAdultContent,
                Available: m.Available,
                DomainName: m.DomainName ?? gameSlug);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Nexus GetModDetailAsync fehlgeschlagen: {Slug}/{Id}", gameSlug, modId);
            return null;
        }
    }

    public async Task<IReadOnlyList<NexusFileEntry>> GetFilesAsync(
        string gameSlug, int modId, CancellationToken ct = default)
    {
        var key = _keyStore.GetApiKey();
        if (string.IsNullOrEmpty(key)) return Array.Empty<NexusFileEntry>();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"v1/games/{gameSlug}/mods/{modId}/files.json");
            req.Headers.Add("apikey", key);
            using var resp = await _http.SendAsync(req, ct);
            LogRateLimit(resp);
            if (!resp.IsSuccessStatusCode) return Array.Empty<NexusFileEntry>();
            var body = await resp.Content.ReadAsStringAsync(ct);
            var wrap = JsonSerializer.Deserialize<NexusFilesResponse>(body, JsonOpts);
            if (wrap?.Files is null) return Array.Empty<NexusFileEntry>();
            var result = new List<NexusFileEntry>(wrap.Files.Count);
            foreach (var f in wrap.Files)
            {
                result.Add(new NexusFileEntry(
                    FileId: f.FileId,
                    Name: f.Name ?? "",
                    FileName: f.FileName ?? "",
                    Version: f.Version ?? "",
                    Description: f.Description ?? "",
                    CategoryId: f.CategoryId,
                    CategoryName: f.CategoryName ?? "",
                    IsPrimary: f.IsPrimary,
                    SizeInBytes: f.SizeInBytes ?? 0,
                    UploadedUtc: FromUnixSeconds(f.UploadedTimestamp ?? 0)));
            }
            return result;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Nexus GetFilesAsync fehlgeschlagen: {Slug}/{Id}", gameSlug, modId);
            return Array.Empty<NexusFileEntry>();
        }
    }

    public async Task<string?> GetDownloadLinkAsync(
        string gameSlug, int modId, long fileId, CancellationToken ct = default)
    {
        var key = _keyStore.GetApiKey();
        if (string.IsNullOrEmpty(key)) return null;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"v1/games/{gameSlug}/mods/{modId}/files/{fileId}/download_link.json");
            req.Headers.Add("apikey", key);
            using var resp = await _http.SendAsync(req, ct);
            LogRateLimit(resp);
            if (!resp.IsSuccessStatusCode)
            {
                Log.Warn("Nexus download_link HTTP {Code} — Premium-Key nötig? (mod={Mod}, file={File})",
                    (int)resp.StatusCode, modId, fileId);
                return null;
            }
            var body = await resp.Content.ReadAsStringAsync(ct);
            var arr = JsonSerializer.Deserialize<List<DownloadLink>>(body, JsonOpts);
            return arr is { Count: > 0 } ? arr[0].Uri : null;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Nexus GetDownloadLinkAsync fehlgeschlagen");
            return null;
        }
    }

    public async Task<IReadOnlyList<NexusCategory>> GetCategoriesAsync(
        string gameSlug, CancellationToken ct = default)
    {
        if (_categoryCache.TryGetValue(gameSlug, out var cached)) return cached;
        var key = _keyStore.GetApiKey();
        if (string.IsNullOrEmpty(key)) return Array.Empty<NexusCategory>();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"v1/games/{gameSlug}.json");
            req.Headers.Add("apikey", key);
            using var resp = await _http.SendAsync(req, ct);
            LogRateLimit(resp);
            if (!resp.IsSuccessStatusCode) return Array.Empty<NexusCategory>();
            var body = await resp.Content.ReadAsStringAsync(ct);
            var game = JsonSerializer.Deserialize<NexusGameInfo>(body, JsonOpts);
            if (game?.Categories is null) return Array.Empty<NexusCategory>();
            var result = new List<NexusCategory>(game.Categories.Count);
            foreach (var c in game.Categories)
                result.Add(new NexusCategory(
                    c.CategoryId,
                    c.Name ?? "",
                    c.ParentCategory == 0 ? null : c.ParentCategory));
            _categoryCache[gameSlug] = result;
            return result;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Nexus GetCategoriesAsync fehlgeschlagen: {Slug}", gameSlug);
            return Array.Empty<NexusCategory>();
        }
    }

    private static void LogRateLimit(HttpResponseMessage resp)
    {
        if (resp.Headers.TryGetValues("X-RL-Hourly-Remaining", out var h))
            Log.Debug("Nexus rate-limit hourly-remaining: {H}", string.Join(",", h));
    }

    private static DateTime FromUnixSeconds(long s) =>
        DateTimeOffset.FromUnixTimeSeconds(s).UtcDateTime;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    // ---- Nexus-JSON-DTOs (privat, portiert aus Icarus NexusApiClient) ----

    private sealed class NexusUser
    {
        public string Name { get; set; } = "";
        [JsonPropertyName("is_premium")] public bool IsPremium { get; set; }
    }
    private sealed class NexusMod
    {
        [JsonPropertyName("mod_id")] public int ModId { get; set; }
        public string? Name { get; set; }
        public string? Summary { get; set; }
        public string? Version { get; set; }
        public string? PictureUrl { get; set; }
        public string? Author { get; set; }
        public NexusModUser? User { get; set; }
        [JsonPropertyName("updated_timestamp")] public long? UpdatedTimestamp { get; set; }
        [JsonPropertyName("endorsement_count")] public long? EndorsementCount { get; set; }
        public bool Available { get; set; } = true;
    }
    private sealed class NexusModUser { public string? Name { get; set; } }
    private sealed class NexusModFull
    {
        [JsonPropertyName("mod_id")] public int ModId { get; set; }
        public string? Name { get; set; }
        public string? Summary { get; set; }
        public string? Description { get; set; }
        public string? Version { get; set; }
        public string? PictureUrl { get; set; }
        public string? Author { get; set; }
        public NexusModUser? User { get; set; }
        [JsonPropertyName("category_id")] public int CategoryId { get; set; }
        [JsonPropertyName("created_timestamp")] public long? CreatedTimestamp { get; set; }
        [JsonPropertyName("updated_timestamp")] public long? UpdatedTimestamp { get; set; }
        [JsonPropertyName("endorsement_count")] public long? EndorsementCount { get; set; }
        [JsonPropertyName("contains_adult_content")] public bool ContainsAdultContent { get; set; }
        public bool Available { get; set; } = true;
        [JsonPropertyName("domain_name")] public string? DomainName { get; set; }
    }
    private sealed class NexusFilesResponse { public List<NexusFile>? Files { get; set; } }
    private sealed class NexusFile
    {
        [JsonPropertyName("file_id")] public long FileId { get; set; }
        public string? Name { get; set; }
        [JsonPropertyName("file_name")] public string? FileName { get; set; }
        public string? Version { get; set; }
        public string? Description { get; set; }
        [JsonPropertyName("category_id")] public int CategoryId { get; set; }
        [JsonPropertyName("category_name")] public string? CategoryName { get; set; }
        [JsonPropertyName("is_primary")] public bool IsPrimary { get; set; }
        [JsonPropertyName("size_in_bytes")] public long? SizeInBytes { get; set; }
        [JsonPropertyName("uploaded_timestamp")] public long? UploadedTimestamp { get; set; }
    }
    private sealed class DownloadLink
    {
        [JsonPropertyName("URI")] public string? Uri { get; set; }
    }
    private sealed class NexusGameInfo { public List<NexusGameCategory>? Categories { get; set; } }
    private sealed class NexusGameCategory
    {
        [JsonPropertyName("category_id")] public int CategoryId { get; set; }
        public string? Name { get; set; }
        [JsonPropertyName("parent_category")]
        [JsonConverter(typeof(FalseOrIntConverter))]
        public int ParentCategory { get; set; }
    }
    private sealed class UpdatedRow
    {
        [JsonPropertyName("mod_id")] public int ModId { get; set; }
    }
    private sealed class FalseOrIntConverter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.False) return 0;
            if (reader.TokenType == JsonTokenType.True) return 0;
            if (reader.TokenType == JsonTokenType.Number) return reader.GetInt32();
            return 0;
        }
        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
            => writer.WriteNumberValue(value);
    }
}
