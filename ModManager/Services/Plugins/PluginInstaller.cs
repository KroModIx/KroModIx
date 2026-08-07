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
using ModManager.PluginContracts;
using NLog;

namespace ModManager.Services.Plugins;

/// <summary>
/// Lädt ein Plugin-Release-Asset (ZIP mit DLL + plugin.json) von GitHub
/// und deployed es nach <see cref="AppPaths.UserPluginsDir"/>. Danach
/// kann der Host das Plugin via <see cref="PluginActivator.ActivateOneAsync"/>
/// live in den Prozess bringen — ohne App-Restart.
/// </summary>
public sealed class PluginInstaller
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Sucht das neueste Release-ZIP im referenzierten GitHub-Repo,
    /// lädt es herunter und entpackt es in einen frischen Plugin-Ordner.</summary>
    public async Task<DiscoveredPlugin?> InstallLatestAsync(
        PluginIndexEntry entry, CancellationToken ct = default)
    {
        if (entry.UpdateSource is null ||
            !string.Equals(entry.UpdateSource.Kind, "github", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(entry.UpdateSource.Repo))
        {
            Log.Warn("Plugin {Id}: keine gültige UpdateSource", entry.Id);
            return null;
        }

        var apiUrl = $"https://api.github.com/repos/{entry.UpdateSource.Repo}/releases/latest";
        Log.Info("Plugin-Install: hole {Url}", apiUrl);
        var handler = new HttpClientHandler
        {
            Proxy = WebRequest.DefaultWebProxy,
            DefaultProxyCredentials = CredentialCache.DefaultCredentials,
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ModManager-PluginInstall");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        GhRelease? release;
        try
        {
            release = await http.GetFromJsonAsync<GhRelease>(apiUrl, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Plugin-Install: konnte Release-Metadaten nicht laden");
            return null;
        }
        if (release?.Assets is null || release.Assets.Count == 0)
        {
            Log.Warn("Plugin-Install: kein Release oder keine Assets in {Repo}", entry.UpdateSource.Repo);
            return null;
        }

        var zipAsset = release.Assets.FirstOrDefault(a =>
            a.Name?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true);
        if (zipAsset?.BrowserDownloadUrl is null)
        {
            Log.Warn("Plugin-Install: kein ZIP-Asset im Release {Tag} von {Repo}",
                release.TagName, entry.UpdateSource.Repo);
            return null;
        }

        Log.Info("Plugin-Install: lade {Url}", zipAsset.BrowserDownloadUrl);
        var tmpZip = Path.Combine(Path.GetTempPath(),
            $"modmanager-plugin-{entry.Id}-{Guid.NewGuid():N}.zip");
        try
        {
            using (var response = await http.GetAsync(zipAsset.BrowserDownloadUrl,
                       HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var output = File.Create(tmpZip);
                await input.CopyToAsync(output, ct).ConfigureAwait(false);
            }

            var targetDir = Path.Combine(AppPaths.UserPluginsDir, IdToFolderName(entry.Id));
            // Sauber überschreiben: alten Ordner erst löschen (Restart-Semantik
            // beim Update wird durch den Host geregelt — hier ist es nur der
            // Fresh-Install-Pfad).
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
                Log.Warn("Plugin-Install: plugin.json fehlt im ZIP {Zip}", zipAsset.Name);
                return null;
            }

            var manifest = PluginManifest.FromFile(manifestPath);
            Log.Info("Plugin-Install: {Id} v{Ver} entpackt nach {Dir}",
                manifest.Id, manifest.Version, targetDir);
            return new DiscoveredPlugin(manifest, targetDir, manifestPath);
        }
        finally
        {
            try { File.Delete(tmpZip); } catch { /* ignore */ }
        }
    }

    private static string IdToFolderName(string id)
    {
        // "kroste.ls25" → "kroste.ls25" (schon dateisystem-safe). Für Robustheit:
        // Dots erlauben, alles andere maskieren.
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
