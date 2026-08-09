namespace KroModIx.Services.Games;

/// <summary>
/// Ein vom Host discovered Spiel (Steam ODER manuell). Wird der Sidebar präsentiert,
/// mit Plugin-Status-Informationen angereichert. Frei von <c>KroModIx.Plugin.Contracts</c>-
/// Typen, damit sich UI und Discovery unabhängig vom Contract entwickeln können.
/// </summary>
/// <param name="Key">Eindeutiger UI-Schlüssel: bei Steam <c>steam:&lt;appId&gt;</c>,
/// bei Manual <c>manual:&lt;guid&gt;</c>.</param>
/// <param name="DisplayName">Anzeigename in der Sidebar.</param>
/// <param name="InstallDir">Absoluter Pfad zum Install-Verzeichnis.</param>
/// <param name="SteamAppId">Steam-App-ID (bei Manual falls vom User gesetzt).</param>
/// <param name="ManualId">Bei Manual: die <see cref="ManualGameEntry.Id"/>.</param>
/// <param name="CustomCoverPath">User-gesetztes Cover-Bild (bei Manual).</param>
/// <param name="ExecutablePath">Absoluter Pfad zur .exe (Manual-Games).
///   Für Steam-Games null — dort startet <see cref="Plugins.GameLauncherService"/>
///   via <c>steam://run/&lt;appId&gt;</c>.</param>
/// <param name="Source">Steam oder Manual.</param>
public sealed record DiscoveredGame(
    string Key,
    string DisplayName,
    string InstallDir,
    int? SteamAppId,
    string? ManualId,
    string? CustomCoverPath,
    DiscoveredGameSource Source,
    string? ExecutablePath = null);

public enum DiscoveredGameSource
{
    Steam,
    Manual,
}
