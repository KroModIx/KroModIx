using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KroModIx.Services.Games;

namespace KroModIx.ViewModels;

/// <summary>VM für „🎮 Ordner mit Spielen scannen"-Wizard. User wählt einen
/// Root-Ordner, der Host scannt nach bekannten Engines (aktuell Ren'Py) und
/// legt bei Bestätigung pro erkanntem Container-Ordner ein Manual-Game an.
/// Jede Kachel bekommt den Engine-Slug (z. B. <c>renpy</c>) — der Plugin-
/// Aktivierungs-Planner matched darauf gegen <c>PluginManifest.Targets[].Engine</c>.</summary>
public sealed partial class AddFolderCollectionDialogViewModel : ViewModelBase
{
    private readonly ManualGamesService _manual;
    private readonly FolderEngineDetector _detector;

    public AddFolderCollectionDialogViewModel(ManualGamesService manual, FolderEngineDetector detector)
    {
        _manual = manual;
        _detector = detector;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _rootDir = "";

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _hasScanned;

    [ObservableProperty]
    private string _resultText = "Wähle einen Root-Ordner und klick 'Scannen'.";

    [ObservableProperty]
    private string? _errorMessage;

    public ObservableCollection<string> Samples { get; } = new();

    private EngineMatch? _match;

    /// <summary>Neu angelegte Kacheln (v0.3+: pro erkanntem Container ein Eintrag).
    /// Leer wenn User abgebrochen hat oder nichts neu war (Dedup).</summary>
    public IReadOnlyList<ManualGameEntry> Results { get; private set; } = Array.Empty<ManualGameEntry>();

    public event EventHandler? RequestClose;

    private bool CanScan() => !string.IsNullOrWhiteSpace(RootDir) && Directory.Exists(RootDir) && !IsScanning;
    private bool CanConfirm() => _match is not null && _match.ContainerCount > 0 && !IsScanning;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        ErrorMessage = null;
        Samples.Clear();
        _match = null;
        try
        {
            IsScanning = true;
            var matches = await Task.Run(() => _detector.Detect(RootDir));
            _match = matches.FirstOrDefault();
            HasScanned = true;
            if (_match is null || _match.ContainerCount == 0)
            {
                ResultText = "Keine bekannte Spiele-Engine im Ordner erkannt.";
                return;
            }
            foreach (var s in _match.Samples) Samples.Add(s);
            ResultText = $"{_match.DisplayName}: {_match.ContainerCount} Spiel(e) gefunden. " +
                $"Beim Import wird pro Spiel eine eigene Sidebar-Kachel angelegt.";
        }
        catch (Exception ex)
        {
            ErrorMessage = "Scan fehlgeschlagen: " + ex.Message;
        }
        finally
        {
            IsScanning = false;
            ScanCommand.NotifyCanExecuteChanged();
            ConfirmCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        if (_match is null) return;
        var items = _match.Containers
            .Select(path => (DisplayName: Path.GetFileName(path)!, InstallDir: path))
            .ToList();
        Results = _manual.AddBulk(items, _match.Engine);
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel()
    {
        Results = Array.Empty<ManualGameEntry>();
        RequestClose?.Invoke(this, EventArgs.Empty);
    }
}
