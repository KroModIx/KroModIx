using System.Threading.Tasks;
using Avalonia.Controls;

namespace ModManager.PluginContracts;

/// <summary>
/// Ein einzelner Tab, den ein Plugin für ein Spiel im MainWindow beisteuert.
/// Ein Plugin darf mehrere Tabs bereitstellen (z.B. „Installiert",
/// „Katalog", „Downloads"). Der Host rendert sie in der Reihenfolge von
/// <see cref="Order"/>.
/// </summary>
public interface IGameTabContribution
{
    /// <summary>Stabile Tab-ID (für Deep-Links via <see cref="IHostShell.RequestNavigation"/>).</summary>
    string Id { get; }

    string Label { get; }

    /// <summary>Emoji oder Kurzcode (z.B. „🧩", „📥"). Der Host rendert es links vom Label.</summary>
    string Icon { get; }

    /// <summary>Sortierreihenfolge innerhalb des Plugins. Kleiner = weiter links.</summary>
    int Order { get; }

    /// <summary>Erlaubt dem Plugin, Tabs kontextabhängig zu verstecken (z.B. „Nur online").</summary>
    bool IsVisible(DetectedGame game);

    /// <summary>Erzeugt den Tab-Content. Wird vom Host lazy beim ersten
    /// Anzeigen aufgerufen und danach gecacht.</summary>
    Control CreateView(DetectedGame game, IHostServices host);

    /// <summary>Wird gefeuert, wenn der User zu diesem Tab wechselt. Plugins
    /// nutzen das, um Watcher/Timer wieder anzuwerfen. Default: no-op.</summary>
    Task OnActivatedAsync() => Task.CompletedTask;

    /// <summary>Gegenstück zu <see cref="OnActivatedAsync"/>: Watcher pausieren.</summary>
    Task OnDeactivatedAsync() => Task.CompletedTask;
}
