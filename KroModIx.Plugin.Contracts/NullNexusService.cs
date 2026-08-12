using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace KroModIx.Plugin.Contracts;

/// <summary>Null-Object-Implementation von <see cref="INexusService"/> — wird
/// vom default-Getter in <see cref="IHostServices.Nexus"/> zurückgegeben wenn
/// ein Plugin gegen einen älteren Host (&lt; v1.14.0) läuft. Alle Methoden
/// liefern leere Ergebnisse ohne Fehler; das Plugin sollte
/// <see cref="INexusService.HasApiKey"/> prüfen und den Katalog-View
/// entsprechend ausgrauen.</summary>
public sealed class NullNexusService : INexusService
{
    public static readonly NullNexusService Instance = new();
    private NullNexusService() { }

    public bool HasApiKey => false;
    public string UserName => string.Empty;
    public bool IsPremium => false;
    public event EventHandler? ApiKeyChanged { add { } remove { } }

    public Task<NexusValidateResult> ValidateAsync(CancellationToken ct = default)
        => Task.FromResult(new NexusValidateResult(false, "", false,
            "Nexus wird von diesem Host nicht unterstützt (bitte auf KroModIx v1.14.0+ aktualisieren)."));

    public Task<IReadOnlyList<NexusCatalogEntry>> GetLatestModsAsync(
        string gameSlug, string endpoint, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<NexusCatalogEntry>>(Array.Empty<NexusCatalogEntry>());

    public Task<IReadOnlyList<int>> GetUpdatedModIdsAsync(
        string gameSlug, string period, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<int>>(Array.Empty<int>());

    public Task<NexusModDetail?> GetModDetailAsync(
        string gameSlug, int modId, CancellationToken ct = default)
        => Task.FromResult<NexusModDetail?>(null);

    public Task<IReadOnlyList<NexusFileEntry>> GetFilesAsync(
        string gameSlug, int modId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<NexusFileEntry>>(Array.Empty<NexusFileEntry>());

    public Task<string?> GetDownloadLinkAsync(
        string gameSlug, int modId, long fileId, CancellationToken ct = default)
        => Task.FromResult<string?>(null);

    public Task<IReadOnlyList<NexusCategory>> GetCategoriesAsync(
        string gameSlug, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<NexusCategory>>(Array.Empty<NexusCategory>());
}
