using System;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using NLog;

namespace KroModIx.Views;

/// <summary>
/// Custom-Chrome nach Avalonia-12-Konvention (Kroste-Standard):
/// BorderOnly + ExtendClientAreaToDecorationsHint. Lädt das App-Icon als
/// Window.Icon (try/catch — ohne Icon lauffähig).
/// </summary>
public class ChromeWindow : Window
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    protected ChromeWindow()
    {
        WindowDecorations = WindowDecorations.BorderOnly;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = -1;
        CanResize = true;

        TryLoadIcon();
    }

    private void TryLoadIcon()
    {
        try
        {
            var iconUri = new Uri("avares://KroModIx/Assets/kromodix.png");
            if (AssetLoader.Exists(iconUri))
                Icon = new WindowIcon(new Bitmap(AssetLoader.Open(iconUri)));
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "App-Icon konnte nicht geladen werden — Fenster ohne Icon.");
        }
    }
}
