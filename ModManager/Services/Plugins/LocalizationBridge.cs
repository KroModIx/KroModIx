using System;
using ModManager.Localization;
using ModManager.PluginContracts;

namespace ModManager.Services.Plugins;

/// <summary>Adapter: Host-<see cref="LocalizationService"/> nach Plugin-Contract
/// <see cref="ILocalization"/>.</summary>
public sealed class LocalizationBridge : ILocalization
{
    public LocalizationBridge()
    {
        LocalizationService.Instance.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(LocalizationService.Current)
                or nameof(LocalizationService.CurrentIso))
                CurrentChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    public string CurrentIso => LocalizationService.Instance.CurrentIso;
    public event EventHandler? CurrentChanged;
}
