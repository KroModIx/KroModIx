namespace KroModIx.Services;

/// <summary>Persistierte App-Einstellungen. Speicherort: <c>settings.json</c> im Config-Root.</summary>
public sealed class AppSettings
{
    /// <summary>ISO-Culture-Code (z.B. "de", "en"). Null = System-Default.</summary>
    public string? UiCulture { get; set; }

    /// <summary>
    /// Plugins, die der User explizit "immer aktivieren" markiert hat, auch wenn kein
    /// Ziel-Spiel installiert ist (nützlich für Development und Non-Steam-Games).
    /// </summary>
    public List<string> AlwaysActivePluginIds { get; set; } = new();

    /// <summary>Zuletzt selektiertes Spiel (GameId) in der Sidebar.</summary>
    public string? LastSelectedGameId { get; set; }

    /// <summary>Fenster-State für das Hauptfenster (Position/Größe).</summary>
    public WindowStateDto? MainWindow { get; set; }
}

public sealed class WindowStateDto
{
    public double Width { get; set; }
    public double Height { get; set; }
    public double? X { get; set; }
    public double? Y { get; set; }
    public bool Maximized { get; set; }
}
