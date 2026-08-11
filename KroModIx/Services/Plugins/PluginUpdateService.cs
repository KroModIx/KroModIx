using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.Services.Plugins;

/// <summary>
/// Prüft für jedes geladene Plugin die neueste Release-Version im
/// referenzierten GitHub-Repo (<see cref="PluginManifest.UpdateSource"/>).
/// Bei neuer Version bietet der Host eine „Update installieren"-Aktion —
/// wegen der geladenen Assembly ist ein App-Restart Pflicht (dies ist der
/// Unterschied zu <see cref="PluginInstaller.InstallLatestAsync"/>, der
/// beim Erstinstall live aktivieren kann).
/// </summary>
public sealed class PluginUpdateService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly PluginActivator _activator;
    private readonly List<PluginUpdateInfo> _available = new();
    private readonly object _lock = new();
    // Race-Guard: verhindert dass zwei parallele CheckAllAsync-Aufrufe (z. B.
    // Auto-Check beim Start UND "Jetzt prüfen" vom User) zeitgleich laufen und
    // ihre Ergebnisse ineinander mischen. Der zweite Caller wartet auf den
    // ersten und bekommt dessen Ergebnis geschenkt.
    private readonly SemaphoreSlim _checkGate = new(1, 1);
    // Persistenter Cache pro pluginId. Wird bei jedem erfolgreichen Check
    // aktualisiert. Bei API-Fehler (403 Rate-Limit, Netz-Timeout etc.) wird
    // der Cache als Fallback genutzt — sonst würde ein einziges Rate-Limit
    // dazu führen, dass alle bekannten Updates aus der UI verschwinden.
    // (Rate-Limit ist real: unauthenticated 60 req/h, bei 5 Plugins × einige
    // App-Starts + "Jetzt prüfen"-Klicks schnell erreicht.)
    private readonly string _cachePath = Path.Combine(AppPaths.ConfigRoot, "plugin-update-cache.json");
    private readonly Dictionary<string, CachedRelease> _cache = new(StringComparer.OrdinalIgnoreCase);

    public PluginUpdateService(PluginActivator activator)
    {
        _activator = activator;
        LoadCache();
    }

    private void LoadCache()
    {
        try
        {
            if (!File.Exists(_cachePath)) return;
            var json = File.ReadAllText(_cachePath);
            var items = JsonSerializer.Deserialize<Dictionary<string, CachedRelease>>(json);
            if (items is null) return;
            lock (_lock)
            {
                _cache.Clear();
                foreach (var kv in items) _cache[kv.Key] = kv.Value;
            }
            Log.Debug("Plugin-Update-Cache geladen: {N} Eintrag/Einträge", items.Count);
        }
        catch (Exception ex) { Log.Warn(ex, "Plugin-Update-Cache unlesbar — starte leer"); }
    }

    private void SaveCache()
    {
        try
        {
            Dictionary<string, CachedRelease> snapshot;
            lock (_lock) snapshot = new(_cache, StringComparer.OrdinalIgnoreCase);
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            var tmp = _cachePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _cachePath, overwrite: true);
        }
        catch (Exception ex) { Log.Warn(ex, "Plugin-Update-Cache-Save fehlgeschlagen"); }
    }

    public event EventHandler? UpdatesChanged;

    public IReadOnlyList<PluginUpdateInfo> AvailableUpdates
    {
        get { lock (_lock) return _available.ToList(); }
    }

    /// <summary>Prüft alle aktuell geladenen Plugins. Rückgabe: Anzahl gefundener Updates.
    /// Serialisiert parallele Aufrufe via SemaphoreSlim — der zweite Caller sieht
    /// dasselbe Ergebnis wie der erste (kein doppeltes Netz-Fetching, keine
    /// verdoppelten Update-Rows im UI).</summary>
    public async Task<int> CheckAllAsync(CancellationToken ct = default)
    {
        await _checkGate.WaitAsync(ct).ConfigureAwait(false);
        try { return await CheckAllInternalAsync(ct).ConfigureAwait(false); }
        finally { _checkGate.Release(); }
    }

    private async Task<int> CheckAllInternalAsync(CancellationToken ct)
    {
        var loaded = _activator.Loaded;
        var handler = new HttpClientHandler
        {
            Proxy = WebRequest.DefaultWebProxy,
            DefaultProxyCredentials = CredentialCache.DefaultCredentials,
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("KroModIx-PluginUpdateCheck");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        // Wenn ein GITHUB_TOKEN in der Env liegt (User setzt), nutzen — hebt
        // das Rate-Limit von 60 auf 5000 req/h. Optional, kein Zwang.
        var ghToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(ghToken))
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ghToken);

        int freshFetched = 0, cacheFallback = 0;
        bool rateLimited = false;
        foreach (var lp in loaded)
        {
            var us = lp.Manifest.UpdateSource;
            if (us is null || !string.Equals(us.Kind, "github", StringComparison.OrdinalIgnoreCase)
                           || string.IsNullOrWhiteSpace(us.Repo))
                continue;

            // Bei Rate-Limit-Treffer nicht weiter Requests feuern — wir würden
            // nur weitere 403er einsammeln. Nachfolgende Plugins nutzen ihren
            // Cache-Eintrag.
            if (rateLimited)
            {
                cacheFallback++;
                continue;
            }

            try
            {
                var url = $"https://api.github.com/repos/{us.Repo}/releases/latest";
                var release = await http.GetFromJsonAsync<GhRelease>(url, ct).ConfigureAwait(false);
                var latestTag = release?.TagName?.TrimStart('v');
                if (string.IsNullOrWhiteSpace(latestTag)) continue;

                var asset = release!.Assets?.FirstOrDefault(a =>
                    a.Name?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true);
                lock (_lock)
                {
                    _cache[lp.Manifest.Id] = new CachedRelease(
                        LatestTag: latestTag,
                        AssetUrl: asset?.BrowserDownloadUrl,
                        AssetName: asset?.Name,
                        ReleaseUrl: release.HtmlUrl,
                        CheckedAtUtc: DateTime.UtcNow);
                }
                freshFetched++;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden
                && (ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
                 || ex.Message.Contains("API rate", StringComparison.OrdinalIgnoreCase)))
            {
                Log.Warn("GitHub-API-Rate-Limit erreicht (60 req/h unauthenticated) — nutze " +
                    "gecachte Update-Info. Optional GITHUB_TOKEN als Env-Var setzen (5000 req/h).");
                rateLimited = true;
                cacheFallback++;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Update-Check für {Id} fehlgeschlagen — nutze Cache-Eintrag", lp.Manifest.Id);
                cacheFallback++;
            }
        }

        SaveCache();

        // Effektive Update-Liste aus Cache + Manifest-Version berechnen.
        // Cache enthält immer den letzten bekannten Stand — auch wenn dieser
        // Run keinen frischen Fetch geschafft hat (Rate-Limit/Netz).
        var updates = new List<PluginUpdateInfo>();
        foreach (var lp in loaded)
        {
            CachedRelease? cached;
            lock (_lock) _cache.TryGetValue(lp.Manifest.Id, out cached);
            if (cached is null) continue;
            if (!TryParseVersion(cached.LatestTag, out var latest)
                || !TryParseVersion(lp.Manifest.Version, out var current)
                || latest <= current) continue;
            updates.Add(new PluginUpdateInfo(
                PluginId: lp.Manifest.Id,
                PluginDisplayName: lp.Manifest.DisplayName,
                InstalledVersion: lp.Manifest.Version,
                LatestVersion: cached.LatestTag,
                AssetUrl: cached.AssetUrl,
                AssetName: cached.AssetName,
                ReleaseUrl: cached.ReleaseUrl));
        }
        var deduped = updates
            .GroupBy(u => u.PluginId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        lock (_lock)
        {
            _available.Clear();
            _available.AddRange(deduped);
        }
        UpdatesChanged?.Invoke(this, EventArgs.Empty);
        Log.Info("Plugin-Update-Check: {N} Update(s) verfügbar (fresh={Fresh}, cache-fallback={Cache})",
            deduped.Count, freshFetched, cacheFallback);
        return deduped.Count;
    }

    /// <summary>Lädt das Update-ZIP und tauscht die alte DLL im Plugin-Ordner
    /// atomar aus. Die neue Version wird beim nächsten App-Start geladen —
    /// ein Live-Ersatz der Assembly ist nicht möglich (LoadFrom hält die
    /// Datei im Prozess offen).</summary>
    public async Task<bool> InstallUpdateAsync(PluginUpdateInfo info, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(info.AssetUrl))
        {
            Log.Warn("Update-Install {Id}: kein ZIP-Asset im Release", info.PluginId);
            return false;
        }

        var loaded = _activator.Loaded.FirstOrDefault(l => l.Manifest.Id == info.PluginId);
        if (loaded is null)
        {
            Log.Warn("Update-Install {Id}: Plugin nicht mehr geladen", info.PluginId);
            return false;
        }
        // Das PluginDataDir des Hosts (nicht Plugin-DataDir!) enthält die DLL.
        // Wir müssen den Discovery-Ordner finden — der ist NICHT direkt in
        // LoadedPlugin gespeichert, deshalb suchen wir per Manifest-Id in beiden
        // Plugin-Roots.
        var pluginDir = FindPluginDir(info.PluginId);
        if (pluginDir is null)
        {
            Log.Warn("Update-Install {Id}: Plugin-Verzeichnis nicht auffindbar", info.PluginId);
            return false;
        }

        var handler = new HttpClientHandler
        {
            Proxy = WebRequest.DefaultWebProxy,
            DefaultProxyCredentials = CredentialCache.DefaultCredentials,
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("KroModIx-PluginUpdate");

        var tmpZip = Path.Combine(Path.GetTempPath(),
            $"modmanager-update-{info.PluginId}-{Guid.NewGuid():N}.zip");
        try
        {
            using (var response = await http.GetAsync(info.AssetUrl,
                       HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var output = File.Create(tmpZip);
                await input.CopyToAsync(output, ct).ConfigureAwait(false);
            }

            // Neue Version in einen Sibling-Ordner entpacken, danach atomar
            // die Dateien im Plugin-Dir überschreiben. Die geladene DLL bleibt
            // im Prozess offen — File.Copy funktioniert unter Linux (mmap-
            // basiert, lässt Ersetzen zu), unter Windows warnt es beim Öffnen
            // aber der Restart-Hinweis kommt sowieso.
            var stagingDir = Path.Combine(Path.GetTempPath(),
                $"modmanager-update-staging-{info.PluginId}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(stagingDir);
            ZipFile.ExtractToDirectory(tmpZip, stagingDir, overwriteFiles: true);

            foreach (var srcFile in Directory.EnumerateFiles(stagingDir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(stagingDir, srcFile);
                var dst = Path.Combine(pluginDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                try
                {
                    File.Copy(srcFile, dst, overwrite: true);
                }
                catch (IOException ex)
                {
                    // Windows-Fall: DLL ist geladen und gelockt. Fallback:
                    // .new-Datei danebenlegen, beim nächsten Start rename.
                    var pending = dst + ".new";
                    File.Copy(srcFile, pending, overwrite: true);
                    Log.Warn(ex, "Konnte {Dst} nicht direkt überschreiben — als {Pending} abgelegt, wird beim nächsten Start aktiviert",
                        dst, pending);
                }
            }
            try { Directory.Delete(stagingDir, recursive: true); } catch { /* ignore */ }

            Log.Info("Update installiert für {Id}: {Old} → {New}. Neustart nötig.",
                info.PluginId, info.InstalledVersion, info.LatestVersion);

            // Aus der Verfügbar-Liste entfernen — auch wenn ein Restart aussteht,
            // ist es "vorbereitet".
            lock (_lock) _available.RemoveAll(u => u.PluginId == info.PluginId);
            UpdatesChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Update-Install {Id} fehlgeschlagen", info.PluginId);
            return false;
        }
        finally
        {
            try { File.Delete(tmpZip); } catch { /* ignore */ }
        }
    }

    private static string? FindPluginDir(string pluginId)
    {
        foreach (var root in PluginRegistryScanner.EnumeratePluginRoots())
        {
            if (!Directory.Exists(root)) continue;
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var manifestPath = Path.Combine(dir, "plugin.json");
                if (!File.Exists(manifestPath)) continue;
                try
                {
                    var m = PluginManifest.FromFile(manifestPath);
                    if (m.Id == pluginId) return dir;
                }
                catch { /* skip broken */ }
            }
        }
        return null;
    }

    private static bool TryParseVersion(string s, out Version v)
    {
        int dash = s.IndexOf('-'); if (dash >= 0) s = s[..dash];
        int plus = s.IndexOf('+'); if (plus >= 0) s = s[..plus];
        return Version.TryParse(s, out v!);
    }

    private sealed class GhRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        [JsonPropertyName("assets")] public List<GhAsset>? Assets { get; set; }
    }

    private sealed class GhAsset
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
    }
}

public sealed record PluginUpdateInfo(
    string PluginId,
    string PluginDisplayName,
    string InstalledVersion,
    string LatestVersion,
    string? AssetUrl,
    string? AssetName,
    string? ReleaseUrl);

/// <summary>Cache-Eintrag pro pluginId — was war das letzte Release, das der
/// Service von GitHub gesehen hat. Wird persistiert in
/// <c>~/.config/KroModIx/plugin-update-cache.json</c>.</summary>
public sealed record CachedRelease(
    string LatestTag,
    string? AssetUrl,
    string? AssetName,
    string? ReleaseUrl,
    DateTime CheckedAtUtc);
