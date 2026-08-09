using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog;

namespace KroModIx.Services.Api;

/// <summary>
/// Kestrel-in-Process. Lebt neben der Avalonia-App und antwortet auf
/// <c>http://127.0.0.1:&lt;port&gt;</c>. UI-Zugriffe laufen im ApiEndpoints/HostUiActions
/// via <c>Dispatcher.UIThread</c>.
///
/// <para>Startet nur wenn <see cref="ApiOptions.Enabled"/> = true — Aufrufer
/// muss vorher <see cref="ApiOptionsResolver.Resolve"/> ausführen.</para>
///
/// <para>Bindet bewusst nur an Loopback. Wer die API von remote braucht:
/// SSH-Tunnel, kein Bind-Adress-Ändern.</para>
/// </summary>
public sealed class ApiHost : IAsyncDisposable
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly IServiceProvider _hostServices;
    private WebApplication? _webApp;

    public ApiHost(IServiceProvider hostServices) => _hostServices = hostServices;

    public bool IsRunning => _webApp is not null;

    public async Task StartAsync(ApiOptions options)
    {
        if (!options.Enabled)
        {
            Log.Info("API deaktiviert (settings.ApiEnabled=false und kein --api-port).");
            return;
        }
        if (_webApp is not null)
        {
            Log.Warn("ApiHost bereits gestartet.");
            return;
        }

        try
        {
            var builder = WebApplication.CreateSlimBuilder();
            // Kestrel-Default-Logs raus — wir loggen den API-Lifecycle über
            // unseren NLog. Interessante Fehler (500er) landen im UnhandledException-
            // Handler bzw. im NLog aus den Endpoints.
            builder.Logging.ClearProviders();
            builder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning);

            // CreateSlimBuilder liefert einen WebApplicationBuilder mit
            // ConfigureWebHostBuilder — das kann kein ConfigureKestrel direkt.
            // Sauberer Weg: Options-Pattern via Services.Configure — das
            // wirkt beim Server-Startup.
            builder.Services.Configure<KestrelServerOptions>(k =>
            {
                k.ListenLocalhost(options.Port, listen =>
                {
                    listen.Protocols = HttpProtocols.Http1;
                });
                k.AddServerHeader = false;
            });

            var app = builder.Build();

            // Bearer-Auth vor allen Endpoints. Middleware liest den erwarteten
            // Token bei jedem Request neu aus dem Options-Objekt — damit ein
            // Token-Wechsel in den Settings (Save + „Neu starten") sichtbar wird,
            // ohne dass wir Kestrel restart-en müssten (aktuell erwarten wir
            // aber Restart; Ansatz ist trotzdem robuster).
            app.Use(async (ctx, next) =>
            {
                await ApiBearerAuth.Enforce(ctx, options.BearerToken, next);
            });

            ApiEndpoints.MapAll(app, _hostServices);

            await app.StartAsync();
            _webApp = app;
            Log.Info("REST-API läuft auf http://127.0.0.1:{Port} (Bearer-Auth {Auth})",
                options.Port, string.IsNullOrWhiteSpace(options.BearerToken) ? "OHNE Token — alle Requests 403" : "aktiv");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ApiHost-Start fehlgeschlagen — App läuft ohne REST-API weiter.");
            _webApp = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_webApp is null) return;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await _webApp.StopAsync(cts.Token);
            await _webApp.DisposeAsync();
            Log.Info("REST-API gestoppt.");
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Fehler beim Stoppen der REST-API");
        }
        finally
        {
            _webApp = null;
        }
    }
}
