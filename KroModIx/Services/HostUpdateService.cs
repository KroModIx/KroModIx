using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace KroModIx.Services;

public sealed record UpdateCheckResult(
    bool UpdateAvailable,
    string CurrentVersion,
    string? LatestVersion,
    string? ReleaseUrl,
    string? AssetUrl,
    string? AssetName);

/// <summary>
/// Prüft GitHub-Releases des Hosts (KroModIx/KroModIx) auf neue Versionen und
/// bietet Self-Update (Kroste-Standard, proxy-aware). Adaptiert aus RenPack.
/// Muster: Download → Austausch-Skript → alte Instanz beenden → neue Instanz starten.
/// </summary>
public sealed class HostUpdateService
{
    // Repo ist github.com/KroModIx/KroModIx (Org-Slug, nicht User-Slug!) —
    // "Kroste/KroModIx" ergibt HTTP 404 und der Update-Check lief still
    // ins Leere. Verifiziert via `gh api repos/KroModIx/KroModIx`.
    private const string Owner = "KroModIx";
    private const string Repo = "KroModIx";
    // Namensschema des Assembly/Exe (weicht vom Repo-Slug ab).
    private const string ExeName = "KroModIx";

    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private UpdateCheckResult? _cached;

    public string CurrentVersion { get; } = ReadInformationalVersion();

    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default)
    {
        if (_cached is not null) return _cached;

        var sw = Stopwatch.StartNew();
        string url = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
        Log.Info("Update-Check gegen {Url}", url);

        string? latestTag = null;
        string? releaseUrl = null;
        string? assetUrl = null;
        string? assetName = null;

        try
        {
            using var http = BuildHttpClient(timeoutSeconds: 15);
            var release = await http.GetFromJsonAsync<GithubRelease>(url, ct).ConfigureAwait(false);
            latestTag = release?.TagName?.TrimStart('v');
            releaseUrl = release?.HtmlUrl;
            var asset = release is not null ? SelectAsset(release) : null;
            assetUrl = asset?.BrowserDownloadUrl;
            assetName = asset?.Name;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Update-Check API-Weg fehlgeschlagen — versuche Redirect-Chase");
            // v1.19.4: Rate-Limit-Fallback via Redirect-Chase (kein API-Call).
            // Analog PluginInstaller v1.19.2 + PluginUpdateService v1.19.3.
            var chased = await TryChaseLatestAsync(ct).ConfigureAwait(false);
            if (chased is not null)
            {
                latestTag = chased.Value.Tag;
                releaseUrl = chased.Value.ReleaseUrl;
                assetUrl = chased.Value.AssetUrl;
                assetName = chased.Value.AssetName;
            }
        }

        string current = StripMetadata(CurrentVersion);
        bool available = latestTag is not null
            && Version.TryParse(StripMetadata(latestTag), out var latest)
            && Version.TryParse(current, out var cur)
            && latest > cur;

        _cached = new UpdateCheckResult(
            available, CurrentVersion, latestTag, releaseUrl, assetUrl, assetName);
        Log.Info("Update-Check fertig ({Ms} ms): aktuell={Cur}, neuste={Latest}, verfuegbar={Avail}, asset={Asset}",
            sw.ElapsedMilliseconds, current, latestTag, available, assetName);
        return _cached;
    }

    /// <summary>v1.19.4 Rate-Limit-Fallback: ruft <c>github.com/{Owner}/{Repo}/releases/latest</c>
    /// ohne AutoRedirect. Der 302-Location-Header enthaelt <c>/tag/vX.Y.Z</c>;
    /// daraus wird das Konventions-Asset gebaut. KEIN API-Call, kein
    /// Rate-Limit. Bei fehlender/exotischer Plattform bleibt AssetUrl null,
    /// der User bekommt dann nur den Release-Seiten-Link statt Self-Update-
    /// Button (siehe AboutWindow-Panel).</summary>
    private static async Task<(string Tag, string ReleaseUrl, string? AssetUrl, string? AssetName)?>
        TryChaseLatestAsync(CancellationToken ct)
    {
        try
        {
            var handler = new HttpClientHandler
            {
                Proxy = WebRequest.DefaultWebProxy,
                DefaultProxyCredentials = CredentialCache.DefaultCredentials,
                AllowAutoRedirect = false,
            };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"{ExeName}-UpdateCheck");

            var latestUrl = $"https://github.com/{Owner}/{Repo}/releases/latest";
            using var resp = await http.GetAsync(latestUrl, ct).ConfigureAwait(false);
            var loc = resp.Headers.Location?.ToString() ?? "";
            var idx = loc.LastIndexOf("/tag/", StringComparison.Ordinal);
            if (idx < 0) { Log.Debug("Redirect-Chase: Location ohne /tag/-Segment: {Loc}", loc); return null; }
            var tag = loc[(idx + "/tag/".Length)..].TrimEnd('/');
            var version = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag[1..] : tag;

            // Konventions-Asset-Namen fuer den Host (Release-Workflow-Naming,
            // verifiziert via gh api repos/KroModIx/KroModIx/releases/latest):
            //   KroModIx-{ver}-win-x64.zip
            //   KroModIx-{ver}-linux-x64.tar.gz
            //   KroModIx-{ver}-x86_64.AppImage
            // Windows/AppImage bevorzugt (Self-Update-Support). Linux-tar.gz
            // ist Fallback wenn AppImage nicht existiert (kann bei manchen
            // Distros/Bazzite bevorzugt sein).
            string? assetName = null, assetUrl = null;
            if (OperatingSystem.IsWindows())
                assetName = $"KroModIx-{version}-win-x64.zip";
            else if (OperatingSystem.IsLinux())
                assetName = $"KroModIx-{version}-x86_64.AppImage";
            if (assetName is not null)
                assetUrl = $"https://github.com/{Owner}/{Repo}/releases/download/{tag}/{assetName}";

            var releaseUrl = $"https://github.com/{Owner}/{Repo}/releases/tag/{tag}";
            Log.Info("Redirect-Chase erfolgreich: Tag={Tag}, Asset={Asset}", tag, assetName ?? "<keins>");
            return (version, releaseUrl, assetUrl, assetName);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Redirect-Chase-Fallback fehlgeschlagen");
            return null;
        }
    }

    public async Task<string> DownloadAssetAsync(string url, string destinationPath,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        Log.Info("Update: Download {Url} → {Dest}", url, destinationPath);
        using var http = BuildHttpClient(timeoutSeconds: 300);
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"{ExeName}-Update");

        using var response = await http.GetAsync(url,
            HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength;
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var output = File.Create(destinationPath);
        var buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            done += read;
            if (total is > 0) progress?.Report((double)done / total.Value);
        }
        progress?.Report(1.0);
        Log.Info("Update: Download fertig ({Bytes} Bytes)", done);
        return destinationPath;
    }

    public void ApplyUpdateAndRestart(string downloadedAssetPath)
    {
        if (OperatingSystem.IsLinux()) ApplyUpdateLinux(downloadedAssetPath);
        else if (OperatingSystem.IsWindows()) ApplyUpdateWindows(downloadedAssetPath);
        else throw new PlatformNotSupportedException("Self-Update nur unter Windows/Linux.");
    }

    [SupportedOSPlatform("linux")]
    private static void ApplyUpdateLinux(string downloadedAssetPath)
    {
        string appImageEnv = Environment.GetEnvironmentVariable("APPIMAGE") ?? "";
        string ext = Path.GetExtension(downloadedAssetPath).ToLowerInvariant();
        string logPath = ResolveWritableLogPath();
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        string script;
        int pid = Environment.ProcessId;
        if (ext == ".appimage" && !string.IsNullOrEmpty(appImageEnv))
        {
            script = $@"#!/usr/bin/env bash
exec >>{Escape(logPath)} 2>&1
echo ""[$(date -Iseconds)] update: warte auf pid {pid}""
while kill -0 {pid} 2>/dev/null; do sleep 0.3; done
echo ""[$(date -Iseconds)] update: ersetze {Escape(appImageEnv)}""
cp -f {Escape(downloadedAssetPath)} {Escape(appImageEnv)}
chmod +x {Escape(appImageEnv)}
rm -f {Escape(downloadedAssetPath)}
echo ""[$(date -Iseconds)] update: starte neu""
setsid {Escape(appImageEnv)} >/dev/null 2>&1 &
";
        }
        else if (ext is ".gz" or ".tgz")
        {
            string baseDir = AppContext.BaseDirectory.TrimEnd('/');
            script = $@"#!/usr/bin/env bash
exec >>{Escape(logPath)} 2>&1
echo ""[$(date -Iseconds)] update: warte auf pid {pid}""
while kill -0 {pid} 2>/dev/null; do sleep 0.3; done
echo ""[$(date -Iseconds)] update: entpacke nach {baseDir}""
tar -xzf {Escape(downloadedAssetPath)} -C {Escape(baseDir)}
chmod +x {Escape(Path.Combine(baseDir, ExeName))}
rm -f {Escape(downloadedAssetPath)}
echo ""[$(date -Iseconds)] update: starte neu""
setsid {Escape(Path.Combine(baseDir, ExeName))} >/dev/null 2>&1 &
";
        }
        else
        {
            throw new NotSupportedException($"Unbekanntes Linux-Asset: {downloadedAssetPath}");
        }

        string scriptPath = Path.Combine(Path.GetTempPath(), $"modmanager-update-{pid}.sh");
        File.WriteAllText(scriptPath, script);
        File.SetUnixFileMode(scriptPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        Log.Info("Update: starte Installer-Skript {Script}", scriptPath);
        var psi = new ProcessStartInfo("/bin/bash", scriptPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        Process.Start(psi);
    }

    private static void ApplyUpdateWindows(string downloadedAssetPath)
    {
        int pid = Environment.ProcessId;
        string baseDir = AppContext.BaseDirectory.TrimEnd('\\');
        string workDir = Path.Combine(Path.GetTempPath(), $"modmanager-update-{pid}");
        string extractDir = Path.Combine(workDir, "new");
        string logPath = Path.Combine(workDir, "update.log");
        Directory.CreateDirectory(extractDir);

        Log.Info("Update: entpacke ZIP {Zip} → {Dir}", downloadedAssetPath, extractDir);
        ZipFile.ExtractToDirectory(downloadedAssetPath, extractDir, overwriteFiles: true);

        var top = Directory.GetDirectories(extractDir);
        var srcRoot = top.Length == 1 && Directory.GetFiles(extractDir).Length == 0
            ? top[0] : extractDir;

        string batPath = Path.Combine(workDir, "install.bat");
        string exePath = Path.Combine(baseDir, $"{ExeName}.exe");
        var batLines = new[]
        {
            "@echo off",
            $"echo [%DATE% %TIME%] update: warte auf pid {pid} >> \"{logPath}\"",
            $"powershell -NoProfile -Command \"Wait-Process -Id {pid} -ErrorAction SilentlyContinue\"",
            "timeout /t 1 /nobreak > nul",
            $"echo [%DATE% %TIME%] update: xcopy \"{srcRoot}\" -> \"{baseDir}\" >> \"{logPath}\"",
            $"xcopy /E /I /Y /Q \"{srcRoot}\\*\" \"{baseDir}\\\" >> \"{logPath}\" 2>&1",
            "if errorlevel 1 goto :fail",
            $"del \"{downloadedAssetPath}\" >nul 2>&1",
            $"echo [%DATE% %TIME%] update: starte {exePath} >> \"{logPath}\"",
            $"start \"\" \"{exePath}\"",
            "exit /b 0",
            ":fail",
            "echo [%DATE% %TIME%] update: fehlgeschlagen (errorlevel=%errorlevel%) >> \"" + logPath + "\"",
            "exit /b 1",
        };
        File.WriteAllLines(batPath, batLines);

        Log.Info("Update: starte Installer-Batch {Bat}", batPath);
        var psi = new ProcessStartInfo("cmd.exe", $"/c \"{batPath}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        Process.Start(psi);
    }

    private static string Escape(string p) => "'" + p.Replace("'", "'\\''") + "'";

    private static string ResolveWritableLogPath()
    {
        string? xdg = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        if (!string.IsNullOrEmpty(xdg))
            return Path.Combine(xdg, "KroModIx", "update.log");
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
            return Path.Combine(home, ".local", "state", "KroModIx", "update.log");
        return Path.Combine("/tmp", "modmanager-update.log");
    }

    private static GithubAsset? SelectAsset(GithubRelease release)
    {
        if (release.Assets is null || release.Assets.Count == 0) return null;
        if (OperatingSystem.IsWindows())
            return release.Assets.FirstOrDefault(a =>
                a.Name?.Contains("win-x64", StringComparison.OrdinalIgnoreCase) == true
                && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        if (OperatingSystem.IsLinux())
        {
            return release.Assets.FirstOrDefault(a =>
                    a.Name?.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase) == true)
                ?? release.Assets.FirstOrDefault(a =>
                    a.Name?.Contains("linux-x64", StringComparison.OrdinalIgnoreCase) == true
                    && a.Name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase));
        }
        return null;
    }

    private static HttpClient BuildHttpClient(int timeoutSeconds)
    {
        var handler = new HttpClientHandler
        {
            Proxy = WebRequest.DefaultWebProxy,
            DefaultProxyCredentials = CredentialCache.DefaultCredentials,
        };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"{ExeName}-UpdateCheck");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    private static string StripMetadata(string v)
    {
        int plus = v.IndexOf('+');
        if (plus >= 0) v = v[..plus];
        int dash = v.IndexOf('-');
        if (dash >= 0) v = v[..dash];
        return v;
    }

    private static string ReadInformationalVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        return asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? asm.GetName().Version?.ToString()
            ?? "0.0.0";
    }

    private sealed class GithubRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        [JsonPropertyName("assets")] public List<GithubAsset>? Assets { get; set; }
    }

    private sealed class GithubAsset
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
    }
}
