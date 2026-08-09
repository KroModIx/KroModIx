namespace KroModIx.Services.Games;

/// <summary>Ein vom User manuell hinzugefügtes Spiel (Non-Steam oder Steam-Spiel
/// das nicht discovered wird). Persistiert in <c>~/.config/KroModIx/manual-games.json</c>.</summary>
public sealed class ManualGameEntry
{
    /// <summary>Stabile ID (GUID-string), einmalig beim Anlegen generiert. Wird für
    /// Sidebar-Sortierung / Selektion / Persistenz genutzt.</summary>
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Install-Verzeichnis des Spiels (Pflicht, existiert idealerweise).</summary>
    public string InstallDir { get; set; } = string.Empty;

    /// <summary>Absoluter Pfad zur Spiel-Executable (optional, für „Spiel starten").</summary>
    public string? ExecutablePath { get; set; }

    /// <summary>Optional gesetzt vom User — wird als Cover-Bild verwendet
    /// (überschreibt Auto-Steam-CDN).</summary>
    public string? CoverPath { get; set; }

    /// <summary>Wenn gesetzt: <see cref="GameCoverService"/> holt das Cover
    /// vom Steam-CDN, ohne dass Steam-Discovery greift.</summary>
    public int? SteamAppId { get; set; }
}
