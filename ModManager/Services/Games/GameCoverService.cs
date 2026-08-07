using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace ModManager.Services.Games;

/// <summary>
/// Löst das Cover-Bild eines Spiels auf mit 4-stufiger Priorität:
/// (1) Steam-Cache lokal (<c>~/.local/share/Steam/appcache/librarycache/&lt;appid&gt;_library_600x900.jpg</c>),
/// (2) Steam-CDN (<c>cdn.cloudflare.steamstatic.com</c>) — heruntergeladen und
///     in <c>~/.cache/ModManager/game-covers/</c> gespeichert,
/// (3) User-Custom-Bild (aus <see cref="ManualGameEntry.CoverPath"/>),
/// (4) Platzhalter (null → Consumer zeichnet generisches Icon).
///
/// Erst Cache, dann Netz: offline sofort verfügbar, kein Netz-Roundtrip pro Start.
/// </summary>
public sealed class GameCoverService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly string[] SteamCacheHomeCandidates =
    {
        "/.local/share/Steam/appcache/librarycache",
        "/.steam/steam/appcache/librarycache",
        "/.var/app/com.valvesoftware.Steam/data/Steam/appcache/librarycache",
    };

    public string CacheDir => AppPaths.GameCoverCacheDir;

    /// <summary>Liefert den Pfad zum Cover-Bild. Löst asynchron auf, weil
    /// Schritt (2) einen HTTP-Download machen kann. <paramref name="customPath"/>
    /// überschreibt Schritte (1) und (2), wenn gesetzt und existent.</summary>
    public async Task<string?> ResolveCoverAsync(int? steamAppId, string? customPath,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
            return customPath;

        if (steamAppId is null) return null;
        int appId = steamAppId.Value;

        // (a) Bereits von uns gecacht?
        var cached = FindCached(appId);
        if (cached is not null) return cached;

        // (b) Steam eigener Cache?
        var steamCached = FindSteamCached(appId);
        if (steamCached is not null)
        {
            // In unseren Cache kopieren, damit spätere Zugriffe unabhängig sind
            // (Steam räumt seinen Cache manchmal auf).
            try
            {
                var target = Path.Combine(CacheDir, appId + ".jpg");
                File.Copy(steamCached, target, overwrite: true);
                return target;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Steam-Cover konnte nicht kopiert werden — direkt aus Steam-Cache nutzen.");
                return steamCached;
            }
        }

        // (c) Vom Steam-CDN laden.
        return await DownloadFromCdnAsync(appId, ct).ConfigureAwait(false);
    }

    private string? FindCached(int appId)
    {
        foreach (var ext in new[] { ".jpg", ".jpeg", ".png" })
        {
            var p = Path.Combine(CacheDir, appId + ext);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    private static string? FindSteamCached(int appId)
    {
        if (OperatingSystem.IsWindows())
        {
            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrEmpty(pf86))
            {
                var dir = Path.Combine(pf86, "Steam", "appcache", "librarycache");
                var p = Path.Combine(dir, $"{appId}_library_600x900.jpg");
                if (File.Exists(p)) return p;
            }
            return null;
        }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var rel in SteamCacheHomeCandidates)
        {
            var dir = home + rel;
            var p = Path.Combine(dir, $"{appId}_library_600x900.jpg");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    private async Task<string?> DownloadFromCdnAsync(int appId, CancellationToken ct)
    {
        var url = $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/library_600x900.jpg";
        try
        {
            var handler = new HttpClientHandler
            {
                Proxy = WebRequest.DefaultWebProxy,
                DefaultProxyCredentials = CredentialCache.DefaultCredentials,
            };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ModManager-CoverFetch");

            using var response = await http.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Log.Debug("Kein Cover auf CDN für {AppId} (HTTP {Status})", appId, (int)response.StatusCode);
                return null;
            }
            var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            var target = Path.Combine(CacheDir, appId + ".jpg");
            await File.WriteAllBytesAsync(target, bytes, ct).ConfigureAwait(false);
            Log.Debug("Cover für {AppId} vom CDN geladen ({Bytes} B)", appId, bytes.Length);
            return target;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Cover-Download fehlgeschlagen für {AppId}", appId);
            return null;
        }
    }
}
