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
/// Root-Ordner, der Host scannt nach bekannten Engines (aktuell Ren'Py),
/// zeigt das Ergebnis und legt bei Bestätigung einen Manual-Game-Sammel-
/// Anker an. Der Anker bekommt <c>SteamAppId</c> aus der Engine-Konvention
/// (Ren'Py = 9000001) — der User sieht diese Zahl nie, sie ist intern
/// für den Plugin-Aktivierungs-Match.</summary>
public sealed partial class AddFolderCollectionDialogViewModel : ViewModelBase
{
    private readonly ManualGamesService _manual;
    private readonly FolderEngineDetector _detector;

    // Interner Anchor-AppId pro Engine — der Host + die Plugins wissen davon,
    // der User nie. Für neue Engines hier ergänzen.
    private static readonly Dictionary<string, int> EngineAppIds =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["renpy"] = 9000001,
        };

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
    private string _displayName = "";

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

    public ManualGameEntry? Result { get; private set; }

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
            ResultText = $"{_match.DisplayName}: {_match.ContainerCount} Spiel(e) gefunden.";
            if (string.IsNullOrWhiteSpace(DisplayName))
                DisplayName = $"{_match.DisplayName} Games";
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
        if (!EngineAppIds.TryGetValue(_match.Engine, out var appId))
        {
            ErrorMessage = $"Keine Anchor-AppId für Engine '{_match.Engine}' konfiguriert.";
            return;
        }
        var name = string.IsNullOrWhiteSpace(DisplayName) ? $"{_match.DisplayName} Games" : DisplayName.Trim();
        Result = _manual.Add(name, RootDir.Trim(), executablePath: null, coverPath: null, steamAppId: appId);
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        RequestClose?.Invoke(this, EventArgs.Empty);
    }
}
