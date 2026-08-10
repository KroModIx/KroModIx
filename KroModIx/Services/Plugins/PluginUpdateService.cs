using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
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

    public PluginUpdateService(PluginActivator activator)
    {
        _activator = activator;
    }

    public event EventHandler? UpdatesChanged;

    public IReadOnlyList<PluginUpdateInfo> AvailableUpdates
    {
        get { lock (_lock) return _available.ToList(); }
    }

    /// <summary>Prüft alle aktuell geladenen Plugins. Rückgabe: Anzahl gefundener Updates.</summary>
    public async Task<int> CheckAllAsync(CancellationToken ct = default)
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

        var updates = new List<PluginUpdateInfo>();
        foreach (var lp in loaded)
        {
            var us = lp.Manifest.UpdateSource;
            if (us is null || !string.Equals(us.Kind, "github", StringComparison.OrdinalIgnoreCase)
                           || string.IsNullOrWhiteSpace(us.Repo))
                continue;

            try
            {
                var url = $"https://api.github.com/repos/{us.Repo}/releases/latest";
                var release = await http.GetFromJsonAsync<GhRelease>(url, ct).ConfigureAwait(false);
                var latestTag = release?.TagName?.TrimStart('v');
                if (string.IsNullOrWhiteSpace(latestTag)) continue;

                if (TryParseVersion(latestTag, out var latest)
                    && TryParseVersion(lp.Manifest.Version, out var current)
                    && latest > current)
                {
                    var asset = release!.Assets?.FirstOrDefault(a =>
                        a.Name?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true);
                    updates.Add(new PluginUpdateInfo(
                        PluginId: lp.Manifest.Id,
                        PluginDisplayName: lp.Manifest.DisplayName,
                        InstalledVersion: lp.Manifest.Version,
                        LatestVersion: latestTag,
                        AssetUrl: asset?.BrowserDownloadUrl,
                        AssetName: asset?.Name,
                        ReleaseUrl: release.HtmlUrl));
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Update-Check für {Id} fehlgeschlagen", lp.Manifest.Id);
            }
        }

        // Dedup per PluginId — falls _activator.Loaded aus irgendeinem Grund
        // dieselbe pluginId doppelt liefert (z.B. nach unvollständigem
        // Hot-Swap-Attempt), soll die Update-Liste im UI trotzdem sauber sein.
        // Nehmen den ersten Treffer pro PluginId — bei mehreren gleichen
        // Einträgen ist eh dieselbe LatestVersion.
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
        Log.Info("Plugin-Update-Check: {N} Update(s) verfügbar (aus {Raw} Rohtreffern)",
            deduped.Count, updates.Count);
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
