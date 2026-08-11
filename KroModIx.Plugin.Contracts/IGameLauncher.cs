using System.Threading;
using System.Threading.Tasks;

namespace KroModIx.Plugin.Contracts;

/// <summary>
/// Optional-Erweiterung für <see cref="IGameModPlugin"/> (Contracts v1.10.0+):
/// wenn ein Plugin dieses Interface implementiert, delegiert der Host den
/// Sidebar-Kachel-Doppelklick zunächst an das Plugin. Rückgabe true = Plugin
/// hat das Launch übernommen (z. B. Ren'Py-Assist öffnet bei verfügbarem
/// Update den f95zone-Thread im Browser statt das Spiel zu starten). Rückgabe
/// false = Plugin will nicht zuständig sein → Host macht Default-Launch
/// (Steam-URL / ExecutablePath).
///
/// <para>Wird auf einem UI-Thread aufgerufen (Doppelklick-Event). Wenn das
/// Plugin blockierendes I/O braucht, sollte es intern <c>Task.Run</c> nutzen
/// und den Task hier abwarten.</para>
///
/// <para>Framework-frei wie <see cref="IUpdateNotifier"/>: nur Contracts +
/// Standardbibliothek.</para>
/// </summary>
public interface IGameLauncher
{
    /// <summary>Versucht, das Spiel zu starten. Rückgabe true = Plugin hat's
    /// übernommen, Host macht keinen Default-Launch mehr. False = Plugin
    /// übergibt an Host-Default.</summary>
    Task<bool> TryLaunchAsync(DetectedGame game, CancellationToken cancellationToken);
}
