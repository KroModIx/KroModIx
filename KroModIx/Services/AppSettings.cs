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

    /// <summary>REST-API aktivieren. Default aus. Kann per CLI (<c>--api-port</c>)
    /// zur Laufzeit überschrieben werden — dann startet die API auch bei
    /// <c>ApiEnabled=false</c>.</summary>
    public bool ApiEnabled { get; set; }

    /// <summary>Bind-Port für Kestrel (immer 127.0.0.1). Default 5100.</summary>
    public int ApiPort { get; set; } = 5100;

    /// <summary>Bearer-Token für die REST-API. Leer = alles 403. Muss vom User
    /// gesetzt werden, es gibt bewusst keinen Default — kein „aus Versehen offen".</summary>
    public string? ApiBearerToken { get; set; }

    /// <summary>Sidebar-Filter: wenn <c>true</c>, werden auch Spiele ohne Plugin
    /// angezeigt (ausgegraut / gedimmt). Default aus — der User sieht erstmal
    /// nur die Spiele mit denen er auch modden kann. Der frühere binäre
    /// „OnlyWithPlugin"-Filter ist durch dieses Feld ersetzt (invers).</summary>
    public bool SidebarShowAllGames { get; set; }

    /// <summary>Vom User überschriebene Cover-Bilder pro Spiel. Key ist der
    /// <c>GameEntry.Key</c> (z.B. <c>steam:2300320</c> oder <c>manual:xxx</c>),
    /// Wert ist der Pfad zum kopierten Bild in <see cref="AppPaths.UserCoverDir"/>.
    /// Wird über das Sidebar-Kontextmenü „Kachelbild ändern" gepflegt.</summary>
    public Dictionary<string, string> CustomGameCovers { get; set; } = new();

    /// <summary>Vom User über das Sidebar-Kontextmenü „Aus KroModIx entfernen"
    /// versteckte Spiele — GameEntry.Keys. Steam-Games werden dadurch nur
    /// ausgeblendet (nicht deinstalliert), Manual-Games werden aus
    /// <see cref="ManualGamesService"/> gelöscht (dann ist der Key hier
    /// überflüssig aber schadet nicht).</summary>
    public List<string> HiddenGameKeys { get; set; } = new();
}

public sealed class WindowStateDto
{
    public double Width { get; set; }
    public double Height { get; set; }
    public double? X { get; set; }
    public double? Y { get; set; }
    public bool Maximized { get; set; }
}
