using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace KroModIx.Plugin.Contracts;

/// <summary>
/// Optional-Erweiterung für <see cref="IGameModPlugin"/> (Contracts v1.7.0):
/// Signalisiert dem Host, dass für einzelne Zielspiele Mod-Updates verfügbar
/// sind. Der Host rendert daraufhin einen grünen ↑-Badge auf der jeweiligen
/// Sidebar-Kachel (analog zum goldenen ★ für „Plugin verfügbar").
///
/// <para>Wenn ein Plugin dieses Interface implementiert, fragt der Host
/// nach der Aktivierung <see cref="GetPendingUpdatesAsync"/> ab und danach
/// alle 30 Minuten (bzw. bei manuellem Refresh). Der Rückgabewert ist eine
/// pro-Spiel-Aufzählung — jedes Plugin darf sein eigenes Update-Konzept
/// haben (ModHub-Katalog bei LS25, F95zone-Thread-Watch bei RenPyAssist,
/// Nexus-API bei Icarus, etc.).</para>
///
/// <para>Framework-frei wie <see cref="IHostApiExtender"/>: kein Bezug zu
/// Kestrel, Avalonia oder sonst was — nur Contracts + Standardbibliothek.</para>
/// </summary>
public interface IUpdateNotifier
{
    /// <summary>Liefert pro Zielspiel die Anzahl bereitstehender Updates.
    /// Spiele ohne Updates dürfen weggelassen oder mit
    /// <see cref="GameUpdateInfo.PendingCount"/> = 0 zurückgegeben werden —
    /// der Host filtert das raus. Wird auf einem Worker-Thread aufgerufen
    /// (nicht dem UI-Thread), das Plugin darf blockierende I/O machen.</summary>
    Task<IReadOnlyList<GameUpdateInfo>> GetPendingUpdatesAsync(CancellationToken cancellationToken);
}

/// <summary>Update-Info für ein einzelnes Spiel des Plugins.</summary>
/// <param name="SteamAppId">Steam-App-ID des Spiels, für das die Updates gelten.
///   Muss mit einem <see cref="GameTarget.SteamAppId"/> des meldenden Plugins
///   übereinstimmen — sonst ignoriert der Host den Eintrag.</param>
/// <param name="PendingCount">Anzahl der bereitstehenden Updates. Der Host
///   zeigt „N" im Badge; bei &gt; 99 wird auf „99+" gekürzt.</param>
/// <param name="Summary">Optionaler Tooltip-Text für den Badge (z.B.
///   „3 Mod-Updates bei modhoster.de"). Wenn null: Host zeigt Default-Text.</param>
public sealed record GameUpdateInfo(int SteamAppId, int PendingCount, string? Summary = null)
{
    /// <summary>Alternativer Match-Key für Manual-Games ohne SteamAppId
    /// (v1.10.0+). Wenn gesetzt, matched der Host über den InstallDir
    /// (case-insensitive) statt über die SteamAppId — nötig für Ren'Py-
    /// Kacheln und andere Engine-basierte Multi-Tile-Setups. Init-only
    /// Property statt Ctor-Param, damit die 3-Positional-Args-Signatur
    /// binary-kompatibel zu Contracts &lt;=1.9.x bleibt.</summary>
    public string? InstallDir { get; init; }
}
