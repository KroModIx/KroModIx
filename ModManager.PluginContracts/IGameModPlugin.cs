using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ModManager.PluginContracts;

/// <summary>
/// Entry-Point-Interface pro Plugin-Assembly. Der <c>entryType</c> im Manifest
/// verweist auf eine Klasse, die dieses Interface implementiert und einen
/// parameterlosen Ctor hat.
///
/// Lebenszyklus:
/// <list type="number">
/// <item><see cref="Metadata"/> und <see cref="Targets"/> sind unmittelbar
/// nach dem Ctor gültig — noch bevor <see cref="InitializeAsync"/> läuft.</item>
/// <item>Der Host ruft <see cref="InitializeAsync"/> mit den bereits gefilterten
/// installierten <see cref="DetectedGame"/>s auf. Netzwerk-/DB-/Watcher-Setup
/// gehört hierhin.</item>
/// <item>Für jedes selektierte Spiel ruft der Host <see cref="GetTabContributions"/>
/// (lazy, erst wenn User das Spiel wählt).</item>
/// <item>Beim App-Shutdown oder Plugin-Deaktivieren läuft <see cref="ShutdownAsync"/>.</item>
/// </list>
/// </summary>
public interface IGameModPlugin
{
    PluginMetadata Metadata { get; }
    IReadOnlyList<GameTarget> Targets { get; }

    Task InitializeAsync(IHostServices host, IReadOnlyList<DetectedGame> activatedGames, CancellationToken ct);

    IEnumerable<IGameTabContribution> GetTabContributions(DetectedGame game);

    Task ShutdownAsync();
}
