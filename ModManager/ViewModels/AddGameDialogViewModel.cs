using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModManager.Services.Games;

namespace ModManager.ViewModels;

/// <summary>
/// VM für den „➕ Spiel hinzufügen"-Dialog. Sammelt die Formularwerte und
/// legt beim Bestätigen einen <see cref="ManualGameEntry"/> an.
/// Die File-Picker werden vom Code-Behind der View aufgerufen — der Dialog
/// wird zur Test-Zeit ohne UI-Thread instanziert.
/// </summary>
public sealed partial class AddGameDialogViewModel : ViewModelBase
{
    private readonly ManualGamesService _manual;

    public AddGameDialogViewModel(ManualGamesService manual)
    {
        _manual = manual;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _displayName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _installDir = string.Empty;

    [ObservableProperty]
    private string _executablePath = string.Empty;

    [ObservableProperty]
    private string _coverPath = string.Empty;

    /// <summary>Freitext, wird als int? geparst.</summary>
    [ObservableProperty]
    private string _steamAppIdText = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>Wird gesetzt, sobald der Dialog erfolgreich schließt.</summary>
    public ManualGameEntry? Result { get; private set; }

    public event EventHandler? RequestClose;

    private bool CanConfirm() =>
        !string.IsNullOrWhiteSpace(DisplayName) && !string.IsNullOrWhiteSpace(InstallDir);

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        ErrorMessage = null;
        int? steamAppId = null;
        if (!string.IsNullOrWhiteSpace(SteamAppIdText))
        {
            if (!int.TryParse(SteamAppIdText.Trim(), out var appId) || appId <= 0)
            {
                ErrorMessage = "Steam-AppId muss eine positive Zahl sein.";
                return;
            }
            steamAppId = appId;
        }

        Result = _manual.Add(
            DisplayName.Trim(),
            InstallDir.Trim(),
            string.IsNullOrWhiteSpace(ExecutablePath) ? null : ExecutablePath.Trim(),
            string.IsNullOrWhiteSpace(CoverPath) ? null : CoverPath.Trim(),
            steamAppId);
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        RequestClose?.Invoke(this, EventArgs.Empty);
    }
}
