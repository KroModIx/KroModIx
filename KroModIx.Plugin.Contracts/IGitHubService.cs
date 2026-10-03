using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace KroModIx.Plugin.Contracts;

/// <summary>Eine Datei an einem GitHub-Release.</summary>
public sealed record GitHubAsset(string Name, string DownloadUrl, long SizeBytes);

/// <summary>Ein GitHub-Release.
///
/// <para><see cref="Assets"/> ist <b>leer</b>, wenn
/// <see cref="FromRedirectChase"/> wahr ist: dann wurde die Ausgabe ohne
/// API-Aufruf ermittelt und nur der Tag ist bekannt. Ein Aufrufer, der die
/// Namenskonvention seiner Assets kennt, baut die URL dann selbst über
/// <see cref="IGitHubService.BuildAssetUrl"/>. Das Feld existiert, damit der
/// Baukasten nicht vorgibt, Assets zu haben, die er nicht abrufen
/// konnte.</para></summary>
public sealed record GitHubRelease(
    string Tag,
    string Version,
    string? HtmlUrl,
    IReadOnlyList<GitHubAsset> Assets,
    bool FromRedirectChase);

/// <summary>Zentraler GitHub-Releases-Baukasten (v1.31.0+): die neueste
/// Ausgabe eines Repos und ihre Dateien finden.
///
/// <para><b>Warum im Host.</b> Nachgemessen am 03.10.2026: <b>sieben</b>
/// Stellen taten dasselbe — dreimal im Host (Host-Update,
/// Plugin-Update-Prüfung, Plugin-Installation) und viermal in Plugins
/// (UE4SS-Bootstrap in Icarus, BepInEx in Dyson Sphere Program, MelonLoader
/// in Schedule I, Update-Prüfung in Captain of Industry).</para>
///
/// <para><b>Der eigentliche Gewinn ist nicht die gesparte Zeile, sondern die
/// Raten-Sperre.</b> Ohne Anmeldung erlaubt die GitHub-API 60 Anfragen pro
/// Stunde. Jeder der sieben Verbraucher entdeckte das Limit für sich, und die
/// Plugin-Bootstraps wichen dann auf eine <b>fest hinterlegte</b> URL aus,
/// die mit jeder neuen Loader-Ausgabe weiter veraltet. Dieser Baukasten
/// merkt sich die Sperre <b>einmal für alle</b> Aufrufer und weicht auf den
/// Umleitungs-Pfad aus: ein Aufruf von
/// <c>github.com/&lt;repo&gt;/releases/latest</c> ohne Folgen der Umleitung
/// verrät den Tag im <c>Location</c>-Kopf — kein API-Aufruf, kein Limit,
/// immer aktuell.</para>
///
/// <para>Ein <c>GITHUB_TOKEN</c> in der Umgebung hebt das Limit auf 5000
/// Anfragen pro Stunde. Der Host nimmt es automatisch mit; ein Plugin muss
/// nichts dafür tun.</para></summary>
public interface IGitHubService
{
    /// <summary>Die neueste <b>stabile</b> Ausgabe (keine Vorab-Ausgaben,
    /// keine Entwürfe). Null, wenn das Repo keine hat oder weder API noch
    /// Umleitungs-Pfad etwas liefern.</summary>
    /// <param name="repo">In der Form <c>owner/name</c>.</param>
    Task<GitHubRelease?> GetLatestReleaseAsync(string repo, CancellationToken ct = default);

    /// <summary>Die letzten Ausgaben, neueste zuerst — für den Fall, dass die
    /// gesuchte Datei nicht an der allerneuesten hängt.
    ///
    /// <para>Geht <b>nur</b> über die API: der Umleitungs-Pfad kennt nur die
    /// neueste Ausgabe. Bei erreichter Raten-Sperre kommt eine leere Liste
    /// zurück — der Aufrufer soll dann auf
    /// <see cref="GetLatestReleaseAsync"/> zurückfallen, nicht
    /// scheitern.</para></summary>
    Task<IReadOnlyList<GitHubRelease>> GetReleasesAsync(string repo, int max = 15,
        bool includePrerelease = false, CancellationToken ct = default);

    /// <summary>Neueste stabile Ausgabe plus die erste Datei, deren Name das
    /// Prädikat erfüllt. Der häufigste Fall.
    ///
    /// <para>Kam die Ausgabe über den Umleitungs-Pfad (Raten-Sperre), sind
    /// keine Dateien bekannt und das Ergebnis ist <c>null</c> — der Aufrufer
    /// kann dann mit <see cref="GetLatestReleaseAsync"/> den Tag holen und
    /// die URL über <see cref="BuildAssetUrl"/> selbst bilden, sofern er die
    /// Namenskonvention kennt.</para></summary>
    Task<(GitHubRelease Release, GitHubAsset Asset)?> FindLatestAssetAsync(string repo,
        Func<string, bool> assetNameMatches, CancellationToken ct = default);

    /// <summary>Baut die Download-URL einer Release-Datei aus Repo, Tag und
    /// Dateiname. Für den Ausweichpfad, wenn nur der Tag bekannt ist und der
    /// Aufrufer die Namenskonvention seiner Dateien kennt.</summary>
    string BuildAssetUrl(string repo, string tag, string assetName);

    /// <summary>Ob die Raten-Sperre aktuell greift — rein informativ, etwa
    /// für einen Hinweis in der Oberfläche („GitHub-Limit erreicht, Angaben
    /// können veraltet sein"). Keine Voraussetzung für einen Aufruf: die
    /// Methoden weichen selbst aus.</summary>
    bool IsRateLimited { get; }
}

/// <summary>Default für Hosts &lt; v1.31.0.
///
/// <para>Antwortet durchweg leer statt zu werfen — anders als die
/// Baukästen, die Dateien im Spiel verändern. Hier ist das richtig: ein
/// nicht gefundenes Release heißt „kein Update verfügbar", und das ist ein
/// gültiger, folgenloser Zustand. Ein Aufrufer, der daraus einen Download
/// macht, prüft ohnehin auf null.</para></summary>
public sealed class NullGitHubService : IGitHubService
{
    public static readonly NullGitHubService Instance = new();
    private NullGitHubService() { }

    public bool IsRateLimited => false;

    public Task<GitHubRelease?> GetLatestReleaseAsync(string repo, CancellationToken ct = default)
        => Task.FromResult<GitHubRelease?>(null);

    public Task<IReadOnlyList<GitHubRelease>> GetReleasesAsync(string repo, int max = 15,
        bool includePrerelease = false, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<GitHubRelease>>(Array.Empty<GitHubRelease>());

    public Task<(GitHubRelease Release, GitHubAsset Asset)?> FindLatestAssetAsync(string repo,
        Func<string, bool> assetNameMatches, CancellationToken ct = default)
        => Task.FromResult<(GitHubRelease, GitHubAsset)?>(null);

    public string BuildAssetUrl(string repo, string tag, string assetName)
        => $"https://github.com/{repo}/releases/download/{tag}/{assetName}";
}
