namespace KroModIx.Plugin.Contracts;

/// <summary>Host-Aktionen für Plugins: externe URL/Verzeichnis öffnen,
/// im MainWindow navigieren.</summary>
public interface IHostShell
{
    void OpenExternalUrl(string url);

    void OpenDirectory(string path);

    /// <summary>Bittet den Host, im MainWindow zu einem Spiel und optional
    /// einem Tab zu springen. <paramref name="tabId"/>=null → aktueller Tab bleibt.</summary>
    void RequestNavigation(string gameId, string? tabId = null);
}
