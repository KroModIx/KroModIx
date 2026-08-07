using System;
using Avalonia;
using ModManager.Logging;
using ModManager.Views;
using NLog;

namespace ModManager;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // MaskingLayoutRenderer VOR dem ersten Logger-Aufruf registrieren —
        // sonst wird ${masked:...} nicht aufgelöst und die Log-Zeile ist kaputt.
        MaskingLayoutRenderer.Register();

        var log = LogManager.GetCurrentClassLogger();
        log.Info("ModManager start (args: {Args})", string.Join(" ", args));

        GlobalExceptionHandler.Install();

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            log.Fatal(ex, "App terminated with unhandled exception");
            throw;
        }
        finally
        {
            log.Info("ModManager exit");
            LogManager.Shutdown();
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
