using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.Services.GitHub;

/// <summary>Host-Implementierung von <see cref="IGitHubService"/>.
///
/// <para><b>Zwei Wege, einer davon ohne Limit.</b> Erst die API
/// (<c>api.github.com</c>) — sie liefert Tag und Dateiliste. Greift die
/// Raten-Sperre (60 Anfragen pro Stunde ohne Anmeldung), schaltet der Dienst
/// auf den Umleitungs-Pfad um: ein Aufruf von
/// <c>github.com/&lt;repo&gt;/releases/latest</c> <b>ohne</b> Folgen der
/// Umleitung verrät den Tag im <c>Location</c>-Kopf. Kein API-Aufruf, kein
/// Limit — aber auch keine Dateiliste, deshalb trägt das Ergebnis
/// <see cref="GitHubRelease.FromRedirectChase"/>.</para>
///
/// <para><b>Die Sperre gilt für alle Aufrufer gemeinsam.</b> Vorher entdeckte
/// jeder der sieben Verbraucher sie für sich und verbrannte dabei eine
/// Anfrage. Jetzt merkt sie sich der Dienst einmal und läuft eine Stunde
/// lang direkt über den Umleitungs-Pfad.</para></summary>
public sealed class HostGitHubServiceImpl : IGitHubService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Wie lange die Sperre gilt, bevor die API wieder probiert
    /// wird. GitHub setzt das Fenster auf eine Stunde; etwas Zugabe, damit
    /// der erste Versuch danach nicht gleich wieder ins Limit läuft.</summary>
    private static readonly TimeSpan RateLimitCooldown = TimeSpan.FromMinutes(65);

    private readonly Lock _lock = new();
    private DateTime? _rateLimitedUntil;

    /// <summary>Woher die HTTP-Handler kommen. In Produktion die echten mit
    /// Proxy-Behandlung; im Test eine Attrappe.
    ///
    /// <para>Nahtstelle statt echter Netz-Aufrufe im Test: die beiden Wege
    /// (API und Umleitungs-Pfad) und die Auswahl der richtigen Datei sind
    /// genau die Logik, die hier schiefgehen kann — und ein Test, der dafuer
    /// ins Netz greift, ist weder schnell noch verlaesslich.</para></summary>
    private readonly Func<bool, HttpMessageHandler>? _handlerFactory;

    public HostGitHubServiceImpl() { }

    /// <param name="handlerFactory">Bekommt <c>true</c> fuer den API-Weg und
    /// <c>false</c> fuer den Umleitungs-Pfad (der darf der Umleitung nicht
    /// folgen).</param>
    internal HostGitHubServiceImpl(Func<bool, HttpMessageHandler> handlerFactory)
        => _handlerFactory = handlerFactory;

    public bool IsRateLimited
    {
        get
        {
            lock (_lock)
            {
                if (_rateLimitedUntil is null) return false;
                if (DateTime.UtcNow < _rateLimitedUntil.Value) return true;
                _rateLimitedUntil = null;
                return false;
            }
        }
    }

    private void LatchRateLimit()
    {
        lock (_lock) _rateLimitedUntil = DateTime.UtcNow + RateLimitCooldown;
        Log.Warn("GitHub-API-Raten-Sperre erreicht (60 Anfragen/Stunde ohne Anmeldung) — " +
            "fuer {Minuten} Minuten nur noch der Umleitungs-Pfad. Ein GITHUB_TOKEN in der " +
            "Umgebung hebt das Limit auf 5000 Anfragen/Stunde.", RateLimitCooldown.TotalMinutes);
    }

    public async Task<GitHubRelease?> GetLatestReleaseAsync(string repo,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repo)) return null;

        if (!IsRateLimited)
        {
            try
            {
                var raw = await GetApiAsync<RawRelease>(
                    $"https://api.github.com/repos/{repo}/releases/latest", ct).ConfigureAwait(false);
                if (raw?.TagName is { Length: > 0 }) return Convert(raw);
                Log.Debug("GitHub: {Repo} hat keine neueste Ausgabe", repo);
            }
            catch (Exception ex)
            {
                Log.Info(ex, "GitHub-API fuer {Repo} nicht erreichbar — Umleitungs-Pfad", repo);
            }
        }

        return await TryRedirectChaseAsync(repo, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<GitHubRelease>> GetReleasesAsync(string repo, int max = 15,
        bool includePrerelease = false, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repo) || IsRateLimited) return [];
        try
        {
            var raws = await GetApiAsync<List<RawRelease>>(
                $"https://api.github.com/repos/{repo}/releases?per_page={Math.Clamp(max, 1, 100)}",
                ct).ConfigureAwait(false);
            if (raws is null) return [];
            return raws
                .Where(r => r.TagName is { Length: > 0 })
                .Where(r => includePrerelease || (!r.Prerelease && !r.Draft))
                .Select(Convert)
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Info(ex, "GitHub-Ausgabenliste fuer {Repo} nicht abrufbar", repo);
            return [];
        }
    }

    public async Task<(GitHubRelease Release, GitHubAsset Asset)?> FindLatestAssetAsync(
        string repo, Func<string, bool> assetNameMatches, CancellationToken ct = default)
    {
        var release = await GetLatestReleaseAsync(repo, ct).ConfigureAwait(false);
        if (release is null) return null;
        var asset = release.Assets.FirstOrDefault(a => assetNameMatches(a.Name));
        if (asset is not null) return (release, asset);

        if (release.FromRedirectChase)
        {
            // Erwartet: ueber den Umleitungs-Pfad gibt es keine Dateiliste.
            // Der Aufrufer kann mit dem Tag und BuildAssetUrl weiterarbeiten.
            Log.Debug("GitHub: {Repo} ueber Umleitungs-Pfad — keine Dateiliste, {Tag} bekannt",
                repo, release.Tag);
            return null;
        }

        // Neueste Ausgabe hat die gesuchte Datei nicht — in den letzten
        // Ausgaben weitersuchen. Real bei Loadern, die eine Ausgabe ohne das
        // gewuenschte Archiv veroeffentlichen.
        foreach (var older in await GetReleasesAsync(repo, ct: ct).ConfigureAwait(false))
        {
            var hit = older.Assets.FirstOrDefault(a => assetNameMatches(a.Name));
            if (hit is not null)
            {
                Log.Info("GitHub: passende Datei erst in {Tag} gefunden (nicht in der neuesten)",
                    older.Tag);
                return (older, hit);
            }
        }
        return null;
    }

    public string BuildAssetUrl(string repo, string tag, string assetName)
        => $"https://github.com/{repo}/releases/download/{tag}/{assetName}";

    /// <summary>Umleitungs-Pfad: <c>releases/latest</c> auf github.com
    /// antwortet mit einer Umleitung auf <c>releases/tag/&lt;tag&gt;</c>.
    /// Ohne Folgen der Umleitung steht der Tag im <c>Location</c>-Kopf —
    /// kein API-Aufruf, also kein Limit.</summary>
    private async Task<GitHubRelease?> TryRedirectChaseAsync(string repo,
        CancellationToken ct)
    {
        try
        {
            using var http = BuildClient(api: false);

            using var resp = await http.GetAsync($"https://github.com/{repo}/releases/latest", ct)
                .ConfigureAwait(false);
            var loc = resp.Headers.Location?.ToString() ?? "";
            var idx = loc.LastIndexOf("/tag/", StringComparison.Ordinal);
            if (idx < 0)
            {
                Log.Debug("Umleitungs-Pfad: Location ohne /tag/-Segment: {Loc}", loc);
                return null;
            }
            var tag = loc[(idx + "/tag/".Length)..].TrimEnd('/');
            if (tag.Length == 0) return null;

            Log.Info("GitHub: {Repo} ueber Umleitungs-Pfad auf {Tag} (ohne API-Aufruf)", repo, tag);
            return new GitHubRelease(
                Tag: tag,
                Version: StripV(tag),
                HtmlUrl: $"https://github.com/{repo}/releases/tag/{tag}",
                Assets: [],
                FromRedirectChase: true);
        }
        catch (Exception ex)
        {
            Log.Info(ex, "Umleitungs-Pfad fuer {Repo} fehlgeschlagen", repo);
            return null;
        }
    }

    /// <summary>Baut den HTTP-Client fuer einen der beiden Wege.</summary>
    /// <param name="api">true = API-Weg (folgt Umleitungen, schickt den
    /// Token mit), false = Umleitungs-Pfad (folgt NICHT, sonst waere der
    /// Location-Kopf weg).</param>
    private HttpClient BuildClient(bool api)
    {
        var handler = _handlerFactory?.Invoke(api) ?? new HttpClientHandler
        {
            Proxy = WebRequest.DefaultWebProxy,
            DefaultProxyCredentials = CredentialCache.DefaultCredentials,
            AllowAutoRedirect = api,
        };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("KroModIx-GitHub");
        if (!api) return http;

        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        // Ein Token in der Umgebung hebt das Limit von 60 auf 5000 Anfragen
        // pro Stunde. Hier einmal fuer alle Verbraucher.
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrEmpty(token))
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    /// <summary>Holt eine API-Antwort und erkennt dabei die Raten-Sperre.
    ///
    /// <para><b>Warum hier <see cref="HttpClient.SendAsync(HttpRequestMessage,
    /// CancellationToken)"/> und nicht <c>GetFromJsonAsync</c>:</b> die
    /// bequeme Variante ruft intern
    /// <see cref="HttpResponseMessage.EnsureSuccessStatusCode"/> und wirft
    /// eine <see cref="HttpRequestException"/>, deren Meldung <b>nur</b>
    /// „Response status code does not indicate success: 403 (Forbidden)."
    /// enthält — den Antworttext mit dem Hinweis auf das Limit nicht.
    /// Nachgemessen am 03.10.2026.</para>
    ///
    /// <para>Eine Prüfung über <c>ex.Message.Contains("rate limit")</c> kann
    /// deshalb <b>nie</b> zutreffen. Genau so stand sie vorher an drei
    /// Stellen im Host: die Sperre war toter Code, und jeder Verbraucher
    /// verbrannte bei jeder Prüfung eine Anfrage, auch wenn das Limit längst
    /// erreicht war. Hier wird stattdessen der Statuscode geprüft und der
    /// verbindliche Kopf <c>x-ratelimit-remaining</c>.</para></summary>
    private async Task<T?> GetApiAsync<T>(string url, CancellationToken ct)
    {
        using var http = BuildClient(api: true);
        using var resp = await http.GetAsync(url, ct).ConfigureAwait(false);

        if (await IsRateLimitResponseAsync(resp, ct).ConfigureAwait(false))
        {
            LatchRateLimit();
            return default;
        }
        if (!resp.IsSuccessStatusCode)
        {
            Log.Debug("GitHub-API {Url}: HTTP {Code}", url, (int)resp.StatusCode);
            return default;
        }
        return await resp.Content.ReadFromJsonAsync<T>(cancellationToken: ct).ConfigureAwait(false);
    }

    /// <summary>Ob die Antwort die Raten-Sperre meldet: 429 (sekundäres
    /// Limit) oder 403 zusammen mit <c>x-ratelimit-remaining: 0</c>. Ein 403
    /// <b>ohne</b> diesen Kopf ist etwas anderes (fehlende Rechte, privates
    /// Repo) und darf die Sperre nicht auslösen — sonst blockiert ein
    /// einzelnes Repo eine Stunde lang alle anderen.</summary>
    private static async Task<bool> IsRateLimitResponseAsync(HttpResponseMessage resp,
        CancellationToken ct)
    {
        if (resp.StatusCode == HttpStatusCode.TooManyRequests) return true;
        if (resp.StatusCode != HttpStatusCode.Forbidden) return false;
        if (resp.Headers.TryGetValues("x-ratelimit-remaining", out var values)
            && values.FirstOrDefault() is string remaining
            && int.TryParse(remaining, out var left))
            return left <= 0;
        // Kein Kopf da (etwa in einem Test oder hinter einem Proxy, der ihn
        // schluckt): ein 403 von api.github.com ist dann am wahrscheinlichsten
        // doch das Limit — aber nur, wenn der Antworttext es sagt.
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return body.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
               || body.Contains("API rate", StringComparison.OrdinalIgnoreCase);
    }

    internal static string StripV(string tag)
        => tag.StartsWith('v') || tag.StartsWith('V') ? tag[1..] : tag;

    private static GitHubRelease Convert(RawRelease r) => new(
        Tag: r.TagName!,
        Version: StripV(r.TagName!),
        HtmlUrl: r.HtmlUrl,
        Assets: (r.Assets ?? [])
            .Where(a => a.Name is { Length: > 0 } && a.BrowserDownloadUrl is { Length: > 0 })
            .Select(a => new GitHubAsset(a.Name!, a.BrowserDownloadUrl!, a.Size))
            .ToList(),
        FromRedirectChase: false);

    private sealed class RawRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
        [JsonPropertyName("draft")] public bool Draft { get; set; }
        [JsonPropertyName("assets")] public List<RawAsset>? Assets { get; set; }
    }

    private sealed class RawAsset
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }
    }
}
