using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace KroModIx.Plugin.Contracts;

/// <summary>Zentraler Steam-Workshop-Client, geteilt zwischen allen Plugins
/// die Workshop-Content konsumieren (LS25, Icarus, Satisfactory, kuenftige).
/// Enumeriert lokal installierte Workshop-Items pro AppId und (optional)
/// reichert sie mit Metadaten aus der Steam-Web-API an
/// (<c>ISteamRemoteStorage/GetPublishedFileDetails</c> — public, kein Key
/// noetig fuer public Items).
///
/// <para><b>Contracts v1.17.0+.</b> Bei aelteren Hosts default-implementiert
/// als „kein Workshop-Content" — Plugin faellt sauber auf leere Antworten
/// zurueck, kein Crash.</para></summary>
public interface IWorkshopService
{
    /// <summary>Ist Steam auf dem Host installiert und ist mindestens ein
    /// Workshop-Content-Root bekannt? Wenn false: Plugin sollte die
    /// Workshop-Tab-View ausgrauen und einen Hinweis zeigen.</summary>
    bool IsAvailable { get; }

    /// <summary>Zaehlt alle lokal installierten Workshop-Items fuer die
    /// angegebene <paramref name="steamAppId"/>. Enumeriert jeden bekannten
    /// SteamLibrary-Root (Home + externe Platten) und dedupt via Bazzite-
    /// <c>/var/home</c>-Symlink-Split.
    ///
    /// <para>Optional: Steam-Web-API-Enrichment (Titel, Beschreibung, Preview-
    /// URL) via <see cref="EnrichAsync"/> — separater Call, damit die Discovery
    /// auch offline funktioniert.</para></summary>
    Task<IReadOnlyList<WorkshopItem>> DiscoverAsync(
        int steamAppId, CancellationToken ct = default);

    /// <summary>Reichert eine Liste von <see cref="WorkshopItem"/>s mit
    /// Web-API-Metadaten an (Title, Description, PreviewUrl, Author,
    /// SubscriberCount, UpdatedUtc). Nutzt <c>GetPublishedFileDetails</c>
    /// (Steam-Web-API, kein API-Key noetig fuer public Items). Batch-Grosse
    /// bis 100 pro Request.</summary>
    Task<IReadOnlyList<WorkshopItem>> EnrichAsync(
        IReadOnlyList<WorkshopItem> items, CancellationToken ct = default);
}

/// <summary>Ein lokal installiertes Workshop-Item. <see cref="LocalDir"/>
/// ist der absolute Pfad zum Item-Ordner, <see cref="PublishedFileId"/> ist
/// die Steam-Workshop-Item-ID (Ordnername unter workshop/content/&lt;appId&gt;/).
/// Titel/Beschreibung/PreviewUrl sind null bis <see cref="IWorkshopService.EnrichAsync"/>
/// gerufen wurde.</summary>
public sealed record WorkshopItem(
    ulong PublishedFileId,
    string LocalDir,
    long SizeOnDiskBytes,
    DateTime? LastUpdatedLocalUtc,
    string? Title = null,
    string? Description = null,
    string? PreviewUrl = null,
    string? Author = null,
    int? SubscriberCount = null,
    DateTime? UpdatedUtc = null);

/// <summary>Default-Impl fuer Hosts &lt; v1.17.0 die die Contract-Methode
/// nicht implementieren. Meldet sich als „nicht verfuegbar" — Plugin
/// muss damit klarkommen.</summary>
public sealed class NullWorkshopService : IWorkshopService
{
    public static readonly NullWorkshopService Instance = new();
    private NullWorkshopService() { }
    public bool IsAvailable => false;
    public Task<IReadOnlyList<WorkshopItem>> DiscoverAsync(int steamAppId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<WorkshopItem>>(Array.Empty<WorkshopItem>());
    public Task<IReadOnlyList<WorkshopItem>> EnrichAsync(IReadOnlyList<WorkshopItem> items, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<WorkshopItem>>(Array.Empty<WorkshopItem>());
}
