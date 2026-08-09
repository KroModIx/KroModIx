using System;
using Avalonia;
using KroModIx.Logging;
using KroModIx.Views;
using NLog;

namespace KroModIx;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // MaskingLayoutRenderer VOR dem ersten Logger-Aufruf registrieren —
        // sonst wird ${masked:...} nicht aufgelöst und die Log-Zeile ist kaputt.
        MaskingLayoutRenderer.Register();

        var log = LogManager.GetCurrentClassLogger();
        log.Info("KroModIx start (args: {Args})", string.Join(" ", args));

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
            log.Info("KroModIx exit");
            LogManager.Shutdown();
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
