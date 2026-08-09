using System;
using System.Threading.Tasks;
using NLog;

namespace KroModIx.Views;

/// <summary>
/// Kroste-Standard: unbehandelte Exceptions loggen (Fatal) statt still absterben.
/// AppDomain + TaskScheduler-Unobserved. Ein UI-Dialog wird bewusst nicht
/// erzwungen — Avalonia ist zum Zeitpunkt einiger Handler evtl. nicht mehr
/// disponibel; wichtig ist, dass der Grund im Log steht.
/// </summary>
public static class GlobalExceptionHandler
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static bool _installed;

    public static void Install()
    {
        if (_installed) return;
        _installed = true;

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Fatal(e.ExceptionObject as Exception, "Unbehandelte Exception (AppDomain).");

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Fatal(e.Exception, "Unbeobachtete Task-Exception.");
            e.SetObserved();
        };
    }
}
