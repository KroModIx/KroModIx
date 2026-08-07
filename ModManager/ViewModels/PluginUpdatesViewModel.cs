using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModManager.Services.Plugins;
using NLog;

namespace ModManager.ViewModels;

/// <summary>VM für das Plugin-Updates-Fenster. Listet alle verfügbaren Updates
/// und startet den Install-Flow pro Zeile. Restart-Hint kommt nach dem ersten
/// erfolgreichen Install.</summary>
public sealed partial class PluginUpdatesViewModel : ViewModelBase
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly PluginUpdateService _updates;

    public PluginUpdatesViewModel(PluginUpdateService updates)
    {
        _updates = updates;
        Refresh();
        _updates.UpdatesChanged += (_, _) => Refresh();
    }

    public ObservableCollection<UpdateRow> Rows { get; } = new();

    [ObservableProperty]
    private bool _restartHinted;

    [ObservableProperty]
    private string _statusMessage = "";

    private void Refresh()
    {
        Rows.Clear();
        foreach (var u in _updates.AvailableUpdates)
            Rows.Add(new UpdateRow(u));
        StatusMessage = Rows.Count == 0 ? "Keine Updates verfügbar." : "";
    }

    [RelayCommand]
    private async Task CheckNowAsync()
    {
        StatusMessage = "Prüfe …";
        int n = await _updates.CheckAllAsync();
        StatusMessage = n == 0 ? "Keine Updates verfügbar." : $"{n} Update(s) verfügbar.";
    }

    [RelayCommand]
    private async Task InstallAsync(UpdateRow? row)
    {
        if (row is null) return;
        row.Status = "Lade …";
        try
        {
            bool ok = await _updates.InstallUpdateAsync(row.Source);
            row.Status = ok ? "Installiert (Neustart nötig)" : "Fehlgeschlagen";
            if (ok) RestartHinted = true;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Update-Install-Klick für {Id} warf", row.Source.PluginId);
            row.Status = $"Fehler: {ex.Message}";
        }
    }
}

public sealed partial class UpdateRow : ObservableObject
{
    public UpdateRow(PluginUpdateInfo source) => Source = source;

    public PluginUpdateInfo Source { get; }
    public string DisplayName => Source.PluginDisplayName;
    public string VersionLabel => $"{Source.InstalledVersion}  →  {Source.LatestVersion}";
    public string AssetName => Source.AssetName ?? "";

    [ObservableProperty]
    private string _status = "";
}
