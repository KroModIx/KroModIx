using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace KroModIx.Plugin.Contracts;

/// <summary>Zentraler Nexus-Mods-Client, geteilt zwischen allen Plugins die
/// den Nexus-Katalog nutzen (Icarus, Cyberpunk 2077, künftige). Der User
/// pflegt seinen Personal-API-Key EINMAL im Host-Settings-Fenster; alle
/// Plugins profitieren.
///
/// <para>Rate-Limits: 2500 requests/h für Personal-Keys (250 anonymous).
/// Der Host loggt bei jedem Call die verbleibenden Slots aus dem
/// <c>X-RL-Hourly-Remaining</c>-Header — Plugin muss nicht selbst
/// throtteln, sollte aber Batches (z. B. Katalog-Full-Warmup) sparsam
/// designen.</para>
///
/// <para><b>Contracts v1.14.0+.</b> Bei älteren Hosts default-implementiert
/// als „kein Key konfiguriert" — Plugin fällt sauber auf leere Antworten
/// zurück, kein Crash.</para></summary>
public interface INexusService
{
    /// <summary>Ist ein API-Key im Host-Settings hinterlegt? Wenn false,
    /// sollte das Plugin die Katalog-Views ausgrauen und auf den
    /// Host-Settings-Tab „Nexus" verweisen.</summary>
    bool HasApiKey { get; }

    /// <summary>Name des Users aus der letzten <see cref="ValidateAsync"/>-
    /// Antwort. Leer wenn nie validiert.</summary>
    string UserName { get; }

    /// <summary>Premium-Flag aus der letzten <see cref="ValidateAsync"/>-
    /// Antwort. Steuert ob <see cref="GetDownloadLinkAsync"/> erlaubt ist
    /// (Nexus erlaubt Direct-Download nur für Premium-Accounts).</summary>
    bool IsPremium { get; }

    /// <summary>Feuert nach <see cref="ValidateAsync"/> — Plugins die
    /// Katalog-Refreshes anhängig vom API-Key haben, sollten hier neu
    /// laden.</summary>
    event EventHandler? ApiKeyChanged;

    Task<NexusValidateResult> ValidateAsync(CancellationToken ct = default);

    /// <summary>Katalog-Kurzliste. <paramref name="endpoint"/> muss
    /// <c>"latest_added"</c>, <c>"latest_updated"</c> oder <c>"trending"</c>
    /// sein — die drei „Top-20"-Nexus-Endpoints.</summary>
    Task<IReadOnlyList<NexusCatalogEntry>> GetLatestModsAsync(
        string gameSlug, string endpoint, CancellationToken ct = default);

    /// <summary>Alle Mod-IDs die im Zeitraum <paramref name="period"/>
    /// (<c>1d</c> | <c>1w</c> | <c>1m</c>) neu oder aktualisiert wurden.
    /// Enthält nur IDs — Details via <see cref="GetModDetailAsync"/>.</summary>
    Task<IReadOnlyList<int>> GetUpdatedModIdsAsync(
        string gameSlug, string period, CancellationToken ct = default);

    Task<NexusModDetail?> GetModDetailAsync(
        string gameSlug, int modId, CancellationToken ct = default);

    Task<IReadOnlyList<NexusFileEntry>> GetFilesAsync(
        string gameSlug, int modId, CancellationToken ct = default);

    /// <summary>S3-Presigned-URL für Direct-Download. Nur Premium-Keys —
    /// bei Non-Premium liefert die Methode <c>null</c> (der Aufrufer sollte
    /// den User dann auf den Browser-Download-Flow verweisen).</summary>
    Task<string?> GetDownloadLinkAsync(
        string gameSlug, int modId, long fileId, CancellationToken ct = default);

    Task<IReadOnlyList<NexusCategory>> GetCategoriesAsync(
        string gameSlug, CancellationToken ct = default);
}

/// <summary>Ergebnis von <see cref="INexusService.ValidateAsync"/>.
/// <c>Valid=false</c> wenn Key fehlt, ungültig oder Netz-Fehler.</summary>
public sealed record NexusValidateResult(bool Valid, string UserName, bool IsPremium, string Message);

/// <summary>Ein Katalog-Eintrag (Kurzform, aus latest_added/updated/trending).</summary>
public sealed record NexusCatalogEntry(
    int ModId, string Name, string Author, string Summary, string Category,
    string Version, string PictureUrl, DateTime UpdatedUtc, int Downloads,
    int Endorsements, bool Available);

/// <summary>Volles Mod-Detail (aus <c>/v1/games/{slug}/mods/{id}.json</c>).
/// Enthält gegenüber <see cref="NexusCatalogEntry"/> u. a. die HTML-
/// Beschreibung, Kategorie-ID (via <see cref="NexusCategory"/> auf Namen
/// mappbar), Created-Zeitpunkt und Adult-Content-Flag.</summary>
public sealed record NexusModDetail(
    int ModId, string Name, string Author, string Summary, string DescriptionHtml,
    string Version, string PictureUrl, int CategoryId, DateTime CreatedUtc,
    DateTime UpdatedUtc, int EndorsementCount, bool ContainsAdultContent,
    bool Available, string DomainName);

/// <summary>Ein Download-File eines Mods (Main / Update / Optional / Old).</summary>
public sealed record NexusFileEntry(
    long FileId, string Name, string FileName, string Version, string Description,
    int CategoryId, string CategoryName, bool IsPrimary, long SizeInBytes,
    DateTime UploadedUtc);

/// <summary>Kategorie-Definition eines Games. <see cref="ParentCategoryId"/>
/// ist <c>null</c> für Top-Level-Kategorien, sonst die ID der Eltern-Kategorie
/// (Nexus hat teilweise 2-Ebenen-Hierarchie).</summary>
public sealed record NexusCategory(int CategoryId, string Name, int? ParentCategoryId);
