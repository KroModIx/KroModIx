using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace KroModIx.ViewModels;

/// <summary>v1.22.0: Aggregierter Mod-Update-Ueberblick. Listet alle
/// Sidebar-Kacheln mit <see cref="GameEntry.PendingUpdateCount"/> &gt; 0
/// und bietet pro Row „Zum Spiel wechseln" (setzt SelectedGame in der
/// Sidebar und schliesst das Fenster). Der User muss nicht mehr durch
/// die Sidebar scrollen um Update-Games zu finden.</summary>
public sealed partial class ModUpdatesOverviewViewModel : ObservableObject
{
    private readonly Action<GameEntry> _select;
    private readonly Action _close;

    public ObservableCollection<GameEntry> Rows { get; } = new();

    [ObservableProperty] private string _statusMessage = "";
    public bool HasRows => Rows.Count > 0;
    public int TotalUpdates => Rows.Sum(r => r.PendingUpdateCount);

    public ModUpdatesOverviewViewModel(
        IEnumerable<GameEntry> gamesWithUpdates,
        Action<GameEntry> selectGame,
        Action close)
    {
        _select = selectGame;
        _close = close;
        foreach (var g in gamesWithUpdates.OrderByDescending(g => g.PendingUpdateCount)
                                          .ThenBy(g => g.DisplayName))
            Rows.Add(g);
        StatusMessage = Rows.Count == 0
            ? "Keine ausstehenden Mod-Updates."
            : $"{Rows.Count} Spiel(e), {TotalUpdates} Mod-Update(s) insgesamt.";
    }

    [RelayCommand]
    private void JumpTo(GameEntry entry)
    {
        _select(entry);
        _close();
    }
}
