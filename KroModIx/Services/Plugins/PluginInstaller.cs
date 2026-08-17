using System;
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
/// Lädt ein Plugin-Release-Asset (ZIP mit DLL + plugin.json) von GitHub
/// und deployed es nach <see cref="AppPaths.UserPluginsDir"/>. Danach
/// kann der Host das Plugin via <see cref="PluginActivator.ActivateOneAsync"/>
/// live in den Prozess bringen — ohne App-Restart.
///
/// <para><b>v1.19.2 Fallback-Chain</b> gegen GitHub-Anonymous-Rate-Limit
/// (60/h — bei 7 aktiven Plugins schnell erschöpft):</para>
/// <list type="number">
/// <item>API-Call mit optional <c>GITHUB_TOKEN</c>-Env-Var (5000/h).</item>
/// <item>Bei 403/Netz-Fehler: <b>Redirect-Chase</b> auf
/// <c>github.com/&lt;Repo&gt;/releases/latest</c> — der 302-Location-Header
/// enthaelt den Tag. Kein API-Call, kein Rate-Limit.</item>
/// <item>Direkte CDN-Download-URL aus Repo + Tag + Konventions-Asset-Name
/// (<c>&lt;RepoBase&gt;-&lt;Version&gt;.zip</c>) — das ist das Namens-Schema
/// des Kroste-Plugin-Release-Workflows.</item>
/// </list>
/// </summary>
public sealed class PluginInstaller
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Sucht das neueste Release-ZIP im referenzierten GitHub-Repo,
    /// lädt es herunter und entpackt es in einen frischen Plugin-Ordner.</summary>
    public async Task<PluginInstallResult> InstallLatestAsync(
        PluginIndexEntry entry, CancellationToken ct = default)
    {
        if (entry.UpdateSource is null ||
            !string.Equals(entry.UpdateSource.Kind, "github", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(entry.UpdateSource.Repo))
        {
            Log.Warn("Plugin {Id}: keine gültige UpdateSource", entry.Id);
            return PluginInstallResult.Fail("Ungültige UpdateSource im PluginIndex-Eintrag.");
        }

        var repo = entry.UpdateSource.Repo;
        using var http = BuildHttpClient(timeoutSeconds: 60, allowAutoRedirect: true);
        using var redirectHttp = BuildHttpClient(timeoutSeconds: 15, allowAutoRedirect: false);

        // (1) API-Weg — nutzt GITHUB_TOKEN wenn gesetzt (5000/h).
        var (downloadUrl, assetName, tag, apiError) =
            await TryFindAssetViaApiAsync(http, repo, ct).ConfigureAwait(false);

        // (2) Fallback — Redirect-Chase (kein API-Call, kein Rate-Limit).
        if (downloadUrl is null)
        {
            Log.Info("Plugin-Install: API-Weg gescheitert ({Reason}) — versuche Redirect-Chase",
                apiError);
            var chased = await TryFindAssetViaRedirectAsync(redirectHttp, repo, ct).ConfigureAwait(false);
            if (chased.Url is not null)
            {
                downloadUrl = chased.Url;
                assetName = chased.AssetName;
                tag = chased.Tag;
            }
        }

        if (downloadUrl is null)
        {
            var msg = apiError is null
                ? "Konnte kein Release-Asset finden (weder API noch Redirect-Weg funktioniert)."
                : $"GitHub-API {apiError} und Redirect-Fallback lieferte keinen Tag. " +
                  "Tipp: GITHUB_TOKEN als Env-Var setzen (5000/h statt 60/h) oder 1h warten.";
            Log.Warn("Plugin-Install: {Msg}", msg);
            return PluginInstallResult.Fail(msg);
        }

        Log.Info("Plugin-Install: lade {Url} (Tag={Tag})", downloadUrl, tag);
        var tmpZip = Path.Combine(Path.GetTempPath(),
            $"modmanager-plugin-{entry.Id}-{Guid.NewGuid():N}.zip");
        try
        {
            using (var response = await http.GetAsync(downloadUrl,
                       HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                {
                    var msg = $"Download {downloadUrl} → HTTP {(int)response.StatusCode}. " +
                              (assetName is not null
                                  ? $"Vermutlich weicht das Asset-Naming ab (erwartet: {assetName})."
                                  : "Prüfe die Release-Assets im GitHub-Repo.");
                    Log.Warn("Plugin-Install: {Msg}", msg);
                    return PluginInstallResult.Fail(msg);
                }
                await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var output = File.Create(tmpZip);
                await input.CopyToAsync(output, ct).ConfigureAwait(false);
            }

            var targetDir = Path.Combine(AppPaths.UserPluginsDir, IdToFolderName(entry.Id));
            if (Directory.Exists(targetDir))
            {
                try { Directory.Delete(targetDir, recursive: true); }
                catch (Exception ex) { Log.Warn(ex, "Konnte alten Plugin-Ordner nicht löschen: {Dir}", targetDir); }
            }
            Directory.CreateDirectory(targetDir);
            ZipFile.ExtractToDirectory(tmpZip, targetDir, overwriteFiles: true);

            var manifestPath = Path.Combine(targetDir, "plugin.json");
            if (!File.Exists(manifestPath))
            {
                var msg = $"plugin.json fehlt im ZIP ({assetName}) — Release ist unvollstaendig gepackt.";
                Log.Warn("Plugin-Install: {Msg}", msg);
                return PluginInstallResult.Fail(msg);
            }

            var manifest = PluginManifest.FromFile(manifestPath);
            Log.Info("Plugin-Install: {Id} v{Ver} entpackt nach {Dir}",
                manifest.Id, manifest.Version, targetDir);
            return PluginInstallResult.Ok(new DiscoveredPlugin(manifest, targetDir, manifestPath));
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Plugin-Install: Fehler beim Entpacken");
            return PluginInstallResult.Fail($"Fehler beim Entpacken: {ex.Message}");
        }
        finally
        {
            try { File.Delete(tmpZip); } catch { /* ignore */ }
        }
    }

    // ---- API-Weg ----

    private async Task<(string? Url, string? AssetName, string? Tag, string? Error)>
        TryFindAssetViaApiAsync(HttpClient http, string repo, CancellationToken ct)
    {
        var apiUrl = $"https://api.github.com/repos/{repo}/releases/latest";
        Log.Info("Plugin-Install: API {Url}", apiUrl);
        try
        {
            using var resp = await http.GetAsync(apiUrl, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return (null, null, null, $"HTTP {(int)resp.StatusCode}");

            var release = await resp.Content.ReadFromJsonAsync<GhRelease>(cancellationToken: ct)
                .ConfigureAwait(false);
            if (release?.Assets is null || release.Assets.Count == 0)
                return (null, null, null, "Kein Release oder keine Assets");

            var zip = release.Assets.FirstOrDefault(a =>
                a.Name?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true);
            if (zip?.BrowserDownloadUrl is null)
                return (null, null, null, "Kein ZIP-Asset im Release");

            return (zip.BrowserDownloadUrl, zip.Name, release.TagName, null);
        }
        catch (Exception ex)
        {
            return (null, null, null, ex.Message);
        }
    }

    // ---- Redirect-Chase-Weg ----

    /// <summary>Ruft <c>github.com/{Repo}/releases/latest</c> ohne
    /// AutoRedirect und liest den Tag aus dem Location-Header:
    /// <c>Location: /{Owner}/{Repo}/releases/tag/vX.Y.Z</c>. Kein API-Call,
    /// kein Rate-Limit. Danach wird die konventions-basierte CDN-Download-URL
    /// gebaut: <c>/releases/download/{tag}/{RepoBasename}-{version}.zip</c>
    /// — der Kroste-Plugin-Release-Workflow packt das Asset mit exakt
    /// diesem Namen.</summary>
    private async Task<(string? Url, string? AssetName, string? Tag)>
        TryFindAssetViaRedirectAsync(HttpClient http, string repo, CancellationToken ct)
    {
        var latestUrl = $"https://github.com/{repo}/releases/latest";
        try
        {
            using var resp = await http.GetAsync(latestUrl, ct).ConfigureAwait(false);
            // GitHub liefert 302 mit Location auf /releases/tag/vX.Y.Z.
            var loc = resp.Headers.Location?.ToString() ?? "";
            var idx = loc.LastIndexOf("/tag/", StringComparison.Ordinal);
            if (idx < 0)
            {
                Log.Debug("Plugin-Install: Redirect-Location ohne /tag/-Segment: {Loc}", loc);
                return (null, null, null);
            }
            var tag = loc[(idx + "/tag/".Length)..].TrimEnd('/');
            var version = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag[1..] : tag;

            // Repo-Basename = alles nach dem letzten '/'. Fuer
            // "KroModIx/KroModIx.Plugin.ScheduleI" → "KroModIx.Plugin.ScheduleI".
            var slashIdx = repo.LastIndexOf('/');
            var repoBase = slashIdx >= 0 ? repo[(slashIdx + 1)..] : repo;
            var assetName = $"{repoBase}-{version}.zip";
            var url = $"https://github.com/{repo}/releases/download/{tag}/{assetName}";
            Log.Info("Plugin-Install: Redirect-Chase Tag={Tag}, Asset={Asset}", tag, assetName);
            return (url, assetName, tag);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Plugin-Install: Redirect-Chase fehlgeschlagen");
            return (null, null, null);
        }
    }

    // ---- Shared HttpClient-Setup ----

    private static HttpClient BuildHttpClient(int timeoutSeconds, bool allowAutoRedirect)
    {
        var handler = new HttpClientHandler
        {
            Proxy = WebRequest.DefaultWebProxy,
            DefaultProxyCredentials = CredentialCache.DefaultCredentials,
            AllowAutoRedirect = allowAutoRedirect,
        };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("KroModIx-PluginInstall");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        // Optional: GITHUB_TOKEN aus Env-Var → 5000 statt 60 req/h.
        // Analog PluginUpdateService v1.10.2 + MelonLoaderBootstrapper.
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrEmpty(token))
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private static string IdToFolderName(string id)
    {
        var chars = id.Select(c =>
            char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' ? c : '_');
        return new string(chars.ToArray());
    }

    private sealed class GhRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        [JsonPropertyName("assets")] public System.Collections.Generic.List<GhAsset>? Assets { get; set; }
    }

    private sealed class GhAsset
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
    }
}

/// <summary>Ergebnis einer Plugin-Installation. <see cref="ErrorMessage"/> ist
/// dazu da dem User im UI eine konkrete Ursache zeigen zu koennen —
/// „Download fehlgeschlagen, siehe Log" ist zu vage.</summary>
public sealed record PluginInstallResult(bool Success, DiscoveredPlugin? Plugin, string? ErrorMessage)
{
    public static PluginInstallResult Ok(DiscoveredPlugin plugin) => new(true, plugin, null);
    public static PluginInstallResult Fail(string message) => new(false, null, message);
}
