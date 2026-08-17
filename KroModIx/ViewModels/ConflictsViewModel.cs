using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KroModIx.Plugin.Contracts;

namespace KroModIx.ViewModels;

/// <summary>v1.24.0: Konflikt-Fenster pro Spiel. Ruft den zentralen
/// <see cref="IConflictScanner"/> und listet Dateien mit &gt; 1 Owner.
/// Der Fix (Deaktivieren, Load-Order) bleibt Aufgabe des Users im
/// jeweiligen Plugin-Tab — hier wird nur sichtbar was kollidiert.</summary>
public sealed partial class ConflictsViewModel : ObservableObject
{
    private readonly IConflictScanner _scanner;
    private readonly string _gameKey;
    private readonly string _gameName;

    public ObservableCollection<ConflictRow> Rows { get; } = new();

    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _busy;
    public string GameHeader => $"⚠  Konflikte fuer „{_gameName}\"";

    public ConflictsViewModel(IConflictScanner scanner, string gameKey, string gameName)
    {
        _scanner = scanner;
        _gameKey = gameKey;
        _gameName = gameName;
        _ = ScanAsync();
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        Busy = true;
        try
        {
            StatusMessage = "Scan laeuft — Plugins werden nach ihren Mod-File-Karten gefragt…";
            var conflicts = await _scanner.ScanAsync(_gameKey).ConfigureAwait(true);
            Rows.Clear();
            foreach (var c in conflicts)
                Rows.Add(new ConflictRow(c));
            StatusMessage = Rows.Count == 0
                ? "✅ Keine Konflikte gefunden. Kein installierter Mod ueberschreibt Dateien eines anderen."
                : $"⚠ {Rows.Count} Konflikt-Datei(en) gefunden. Bei Datei-Ueberschreibung gewinnt in der Regel der lexikografisch letzte Mod — pruefe im Plugin-Tab was du deaktivieren willst.";
        }
        finally { Busy = false; }
    }
}

public sealed class ConflictRow
{
    public FileConflict Conflict { get; }
    public ConflictRow(FileConflict c) { Conflict = c; }
    public string RelativePath => Conflict.RelativePath;
    public string OwnersLabel =>
        string.Join(", ",
            Conflict.Owners.Select(o => $"{o.ModDisplayName} [{o.PluginDisplayName}]"));
    public int OwnerCount => Conflict.Owners.Count;
}
