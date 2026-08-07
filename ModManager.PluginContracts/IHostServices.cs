using System.Net.Http;
using NLog;

namespace ModManager.PluginContracts;

/// <summary>
/// Alle Ressourcen, die der Host einem Plugin bereitstellt. Wird beim
/// <see cref="IGameModPlugin.InitializeAsync"/> injiziert und ist über die
/// gesamte Plugin-Lebenszeit gültig.
/// </summary>
public interface IHostServices
{
    /// <summary>Plugin-eigener Logger (z.B. gefiltert auf <c>ModManager.Plugins.LS25.*</c>).</summary>
    Logger Logger { get; }

    /// <summary>Persistenter Config-Ordner des Plugins
    /// (<c>~/.config/ModManager/plugin-data/&lt;pluginId&gt;/</c>).</summary>
    string PluginDataDir { get; }

    /// <summary>Löschbarer Cache-Ordner des Plugins
    /// (<c>~/.cache/ModManager/plugin-cache/&lt;pluginId&gt;/</c>).</summary>
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

    /// <summary>Startet einen benannten Progress-Scope (im Host-Statusbar sichtbar).
    /// Dispose beendet den Scope.</summary>
    IProgressScope BeginProgress(string title);
}
