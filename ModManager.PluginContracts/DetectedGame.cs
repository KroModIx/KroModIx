namespace ModManager.PluginContracts;

/// <summary>
/// Ein zur Laufzeit vom Host erkanntes Spiel (Steam oder manuell). Das Plugin
/// bekommt diese Instanz beim <see cref="IGameModPlugin.InitializeAsync"/> und
/// nutzt vor allem <see cref="InstallDir"/> und <see cref="UserDataDir"/>, statt
/// Pfade selbst zu berechnen — der Host löst <c>libraryfolders.vdf</c>,
/// Proton-Präfixe und Bazzite-Mount-Points zentral.
/// </summary>
/// <param name="Target">Das statische Ziel aus dem Plugin-Manifest.</param>
/// <param name="InstallDir">Absoluter Pfad zur Spiel-Installation (z.B.
/// <c>.../steamapps/common/Farming Simulator 25</c>).</param>
/// <param name="UserDataDir">Pfad zum User-Daten-Ordner des Spiels (z.B.
/// <c>My Games/FarmingSimulator2025</c>), oder <c>null</c> wenn nicht ermittelt.</param>
/// <param name="ProtonPrefix">Proton-Prefix-Pfad (nur Linux+Proton), sonst <c>null</c>.</param>
/// <param name="Runtime">Native, Proton oder Wine.</param>
/// <param name="Source">Woher der Host das Spiel kennt (Steam-Discovery vs.
/// manuell hinzugefügt).</param>
public sealed record DetectedGame(
    GameTarget Target,
    string InstallDir,
    string? UserDataDir,
    string? ProtonPrefix,
    RuntimeKind Runtime,
    GameSource Source);

public enum RuntimeKind
{
    Native,
    Proton,
    Wine,
}

public enum GameSource
{
    Steam,
    Manual,
}
