using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using KroModIx.Services.Games;

namespace KroModIx.ViewModels;

/// <summary>
/// UI-Repräsentation eines Spiels in der Sidebar. Bindet an eine große Cover-
/// Kachel mit optionalem Stern-Overlay.
/// </summary>
public sealed partial class GameEntry : ObservableObject
{
    public GameEntry(DiscoveredGame source)
    {
        Source = source;
    }

    public DiscoveredGame Source { get; }

    /// <summary>Schlüssel aus der Discovery (steam:&lt;appid&gt; oder manual:&lt;guid&gt;).</summary>
    public string Key => Source.Key;

    public string DisplayName => Source.DisplayName;

    [ObservableProperty]
    private Bitmap? _cover;

    /// <summary>
    /// Plugin-Status. <see cref="PluginState.Installed"/> = geladen und aktiv →
    /// gefüllter goldener Stern. <see cref="PluginState.Available"/> = im
    /// PluginIndex verfügbar aber nicht installiert (kommt in M4) → umrandet.
    /// <see cref="PluginState.None"/> = nichts bekannt → kein Stern.
    /// </summary>
    [ObservableProperty]
    private PluginState _pluginState = PluginState.None;

    /// <summary>Für die UI: „★" bei Installed, „☆" bei Available, „" sonst.</summary>
    public string StarSymbol => PluginState switch
    {
        PluginState.Installed => "★",
        PluginState.Available => "☆",
        _ => string.Empty,
    };

    /// <summary>UI-Hint für den Sidebar-Filter „Alle Spiele anzeigen":
    /// Spiele ohne Plugin werden bei aktivem Toggle mit reduzierter Opacity
    /// dargestellt (Graustufen-Look). Wird vom MainWindowViewModel gesetzt,
    /// nicht aus PluginState allein berechnet — der aktuelle Filter-Modus
    /// wird auch berücksichtigt.</summary>
    [ObservableProperty]
    private bool _isDimmed;

    /// <summary>Anzahl ausstehender Mod-Updates für dieses Spiel (aus
    /// <see cref="Services.Plugins.GameUpdateBadgeService"/>). 0 = kein Badge,
    /// &gt; 0 = grüner ↑-Badge oben rechts auf der Kachel.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdates))]
    [NotifyPropertyChangedFor(nameof(UpdateBadgeText))]
    private int _pendingUpdateCount;

    /// <summary>Tooltip-Text vom Plugin (z.B. „3 Mod-Updates bei modhoster.de"),
    /// wird auf den Badge gelegt.</summary>
    [ObservableProperty]
    private string? _updateBadgeTooltip;

    public bool HasUpdates => PendingUpdateCount > 0;
    public string UpdateBadgeText => PendingUpdateCount switch
    {
        0    => "",
        > 99 => "99+",
        var n => n.ToString(),
    };

    /// <summary>Convenience für XAML-Bindings: true wenn das Spiel manuell
    /// hinzugefügt wurde (steuert Sichtbarkeit von „Aus KroModIx entfernen"
    /// im Kontextmenü — Steam-Games können nur ausgeblendet werden, das ist
    /// dieselbe Aktion in der UI aber semantisch ein anderer Weg im VM).</summary>
    public bool IsManual => Source.Source == DiscoveredGameSource.Manual;

    partial void OnPluginStateChanged(PluginState value) => OnPropertyChanged(nameof(StarSymbol));
}

public enum PluginState
{
    None,
    Available,
    Installed,
}
