using CommunityToolkit.Mvvm.ComponentModel;
using KroModIx.Plugin.Contracts;

namespace KroModIx.ViewModels;

/// <summary>Ein einzelner Toast-Eintrag im Overlay oben rechts. Wird vom
/// <see cref="MainWindowViewModel"/> in <c>ObservableCollection&lt;ToastItem&gt;</c>
/// gehalten und nach seiner Timeout-Dauer automatisch entfernt.</summary>
public sealed partial class ToastItem : ObservableObject
{
    /// <summary>Vom VM vergebene Id — nur für Dedup bei schnellen Refreshes gedacht.</summary>
    public int Id { get; }

    public string Message { get; }
    public NotificationLevel Level { get; }

    /// <summary>Emoji-Präfix pro Level. Der Toast-Container zeichnet zusätzlich
    /// einen farbigen Left-Border passend zum Level.</summary>
    public string Icon => Level switch
    {
        NotificationLevel.Info    => "ℹ️",
        NotificationLevel.Warning => "⚠️",
        NotificationLevel.Error   => "❌",
        _ => "•",
    };

    public string BadgeBrushKey => Level switch
    {
        NotificationLevel.Info    => "KrosteAccentBrush",
        NotificationLevel.Warning => "KrosteGoldBrush",
        NotificationLevel.Error   => "KrosteDangerBrush",
        _ => "KrosteAccentBrush",
    };

    public ToastItem(int id, string message, NotificationLevel level)
    {
        Id = id;
        Message = message;
        Level = level;
    }
}
