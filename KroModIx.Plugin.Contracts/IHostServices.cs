using System.Net.Http;
using System.Threading.Tasks;
using NLog;

namespace KroModIx.Plugin.Contracts;

/// <summary>
/// Alle Ressourcen, die der Host einem Plugin bereitstellt. Wird beim
/// <see cref="IGameModPlugin.InitializeAsync"/> injiziert und ist über die
/// gesamte Plugin-Lebenszeit gültig.
/// </summary>
public interface IHostServices
{
    /// <summary>Plugin-eigener Logger (z.B. gefiltert auf <c>KroModIx.Plugin.LS25.*</c>).</summary>
    Logger Logger { get; }

    /// <summary>Persistenter Config-Ordner des Plugins
    /// (<c>~/.config/KroModIx/plugin-data/&lt;pluginId&gt;/</c>).</summary>
    string PluginDataDir { get; }

    /// <summary>Löschbarer Cache-Ordner des Plugins
    /// (<c>~/.cache/KroModIx/plugin-cache/&lt;pluginId&gt;/</c>).</summary>
    string PluginCacheDir { get; }

    ISecretProtection Secrets { get; }

    /// <summary>Fabriziert einen HttpClient mit User-Agent, System-Proxy
    /// (proxy-aware, wichtig für Arbeitslaptop mit Sophos) und default Timeouts.
    /// Plugins sollen NIE <c>new HttpClient()</c> nutzen.</summary>
    HttpClient CreateHttpClient(string? subsystem = null);

    IDialogService Dialogs { get; }
    INotificationSink Notifications { get; }
    ILocalization Localization { get; }
    IHostShell Shell { get; }

    /// <summary>Zentraler KI-Provider. Config (Provider/Endpoint/Modell/Key)
    /// und Setup-UI liegen im Host, Plugin ruft nur <see cref="IAiService.CompleteAsync"/>.
    /// Vor Nutzung <see cref="IAiService.IsAvailableAsync"/> prüfen und Nutzer
    /// auf Host-Einstellungen verweisen falls false.</summary>
    IAiService Ai { get; }

    /// <summary>Zentraler Nexus-Mods-Client (v1.14.0+). Personal-API-Key wird
    /// einmal im Host-Settings-Fenster („Nexus"-Tab) hinterlegt, alle Plugins
    /// die Nexus-Katalog/Downloads nutzen (Icarus, Cyberpunk 2077, künftige)
    /// teilen ihn. Bei älteren Hosts default-implementiert als leere
    /// Rate-Limit-freundliche No-Op.</summary>
    INexusService Nexus => NullNexusService.Instance;

    /// <summary>Zentraler Steam-Workshop-Client (v1.17.0+). Enumeriert
    /// lokal installierte Workshop-Items pro AppId und reichert sie
    /// (optional) mit Steam-Web-API-Metadaten an. LS25/Icarus/Satisfactory
    /// koennen einen einheitlichen „Workshop"-Tab bauen ohne die Pfad-
    /// Discovery jeweils selbst zu machen. Bei aelteren Hosts default =
    /// <see cref="NullWorkshopService.Instance"/>.</summary>
    IWorkshopService Workshop => NullWorkshopService.Instance;

    /// <summary>Startet einen benannten Progress-Scope (im Host-Statusbar sichtbar).
    /// Dispose beendet den Scope.</summary>
    IProgressScope BeginProgress(string title);

    /// <summary>Setzt für ein Manual-Game (identifiziert über <paramref name="installDir"/>,
    /// case-insensitive) den Cover-Bild-Pfad. Der Host aktualisiert die Sidebar-Kachel
    /// entsprechend. Für Steam-Games ohne Effekt (Steam-CDN gewinnt). Rückgabe:
    /// true wenn ein passender Manual-Eintrag gefunden und aktualisiert wurde.
    /// Contracts v1.9.3+ (bei älteren Hosts default-implementiert = no-op).</summary>
    bool TrySetManualGameCover(string installDir, string coverPath) => false;

    /// <summary>Fordert vom Host einen sofortigen Refresh der Update-Badges
    /// (grüner ↑ auf Sidebar-Kacheln). Wird von Plugins gerufen, wenn sich
    /// intern der Update-Status geändert hat — z. B. nach einem Auto-Install
    /// eines Updates. Ohne diesen Call zeigt die Sidebar bis zum nächsten
    /// periodischen Poll (60 s) noch den alten Zustand. Contracts v1.10.1+
    /// (bei älteren Hosts default-implementiert = no-op).</summary>
    Task RequestUpdateBadgeRefreshAsync() => Task.CompletedTask;

    /// <summary>Aktualisiert für ein Manual-Game den Installations-Pfad, wenn
    /// der Plugin (oder User) den Container-Ordner auf der Platte umbenennt/
    /// verschiebt. Der Host re-keyed den Manual-Game-Eintrag von
    /// <paramref name="oldInstallDir"/> auf <paramref name="newInstallDir"/>
    /// (case-insensitive), aktualisiert die Sidebar-Kachel und persistiert
    /// die Änderung. Wichtig: das Plugin ruft dies NACHDEM
    /// <c>Directory.Move</c> erfolgreich war. Rückgabe: true wenn ein
    /// passender Manual-Eintrag re-keyed wurde. Für Steam-Games no-op.
    /// Contracts v1.10.3+ (bei älteren Hosts default = no-op — Sidebar
    /// verwaist bis zum nächsten Neustart).</summary>
    bool TryRenameManualGame(string oldInstallDir, string newInstallDir) => false;
}
