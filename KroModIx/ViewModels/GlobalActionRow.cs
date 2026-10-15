using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.ViewModels;

/// <summary>Eine Plugin-Aktion als Menüeintrag.
///
/// <para>Der Wrapper existiert, damit die View ein <c>ICommand</c> binden
/// kann und damit ein Fehler im Plugin hier endet statt im Host: wirft
/// <see cref="IGlobalAction.InvokeAsync"/>, wird das als Toast gemeldet und
/// protokolliert — die Sidebar bleibt bedienbar.</para></summary>
public sealed partial class GlobalActionRow : ObservableObject
{
    private readonly IGlobalAction _action;
    private readonly string _pluginId;
    private readonly Action<string, NotificationLevel, TimeSpan?> _toast;
    private readonly Logger _log;

    public GlobalActionRow(IGlobalAction action, string pluginId,
        Action<string, NotificationLevel, TimeSpan?> toast, Logger log)
    {
        _action = action;
        _pluginId = pluginId;
        _toast = toast;
        _log = log;
    }

    public string Id => $"{_pluginId}:{_action.Id}";

    /// <summary>Icon und Beschriftung in einer Zeile — das Menü zeigt nur
    /// einen Header.</summary>
    public string Label => string.IsNullOrWhiteSpace(_action.Icon)
        ? _action.Label
        : $"{_action.Icon}  {_action.Label}";

    public string? Description => _action.Description;

    /// <summary>Wird beim Öffnen des Menüs ausgewertet, nicht gecacht.</summary>
    public bool IsEnabled
    {
        get
        {
            try { return _action.IsEnabled; }
            catch (Exception ex)
            {
                _log.Debug(ex, "Plugin {Id}: IsEnabled warf — Aktion wird deaktiviert", _pluginId);
                return false;
            }
        }
    }

    [RelayCommand]
    private async Task InvokeAsync()
    {
        try
        {
            _log.Info("Globale Plugin-Aktion ausgelöst: {Id}", Id);
            await _action.InvokeAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.Warn(ex, "Globale Plugin-Aktion {Id} warf", Id);
            _toast($"{_action.Label}: {ex.Message}", NotificationLevel.Error, null);
        }
    }
}
