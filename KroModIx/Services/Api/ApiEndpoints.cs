using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using KroModIx.Services.Games;
using KroModIx.Services.Plugins;
using KroModIx.ViewModels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NLog;

namespace KroModIx.Services.Api;

/// <summary>
/// Definiert alle HTTP-Routen der KroModIx-REST-API. Getrennt vom
/// <see cref="ApiHost"/>-Lifecycle, damit die Routen auch in Tests direkt
/// gegen ein <see cref="WebApplication"/> gemounted werden können.
///
/// <para>Alle Endpoints erwarten dass sie hinter der
/// <see cref="ApiBearerAuth"/>-Middleware laufen — hier keine Auth-Checks mehr.</para>
/// </summary>
internal static class ApiEndpoints
{
    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public const string ApiVersion = "0.1.0";

    public static void MapAll(WebApplication app, IServiceProvider hostServices)
    {
        var ui = new HostUiActions(hostServices);

        app.MapGet("/state", async () =>
        {
            var mainVm = await ui.GetMainVmAsync();
            var win = await ui.GetMainWindowAsync();
            var activator = hostServices.GetRequiredService<PluginActivator>();
            var hostVersion = hostServices.GetRequiredService<HostUpdateService>().CurrentVersion;

            var state = new
            {
                version = hostVersion,
                apiVersion = ApiVersion,
                activeGameId = mainVm?.SelectedGame?.Key,
                loadedPlugins = activator.Loaded.Select(l => new
                {
                    id = l.Manifest.Id,
                    version = l.Manifest.Version,
                    displayName = l.Manifest.DisplayName,
                    supportsHostApiExtender = false,
                }).ToArray(),
                mainWindow = win is null ? null : new
                {
                    width = (int)Math.Round(win.ClientSize.Width),
                    height = (int)Math.Round(win.ClientSize.Height),
                    isMaximized = win.WindowState == Avalonia.Controls.WindowState.Maximized,
                    activeWindow = ActiveWindowKey(),
                },
            };
            return Results.Text(JsonSerializer.Serialize(state, Json), "application/json");
        });

        app.MapGet("/games", async (bool? onlyWithPlugin, string? search) =>
        {
            var mainVm = await ui.GetMainVmAsync();
            if (mainVm is null) return Results.Ok(Array.Empty<object>());

            var vms = mainVm.VisibleGames.AsEnumerable();
            if (onlyWithPlugin == true) vms = vms.Where(g => g.PluginState != PluginState.None);
            if (!string.IsNullOrWhiteSpace(search))
                vms = vms.Where(g => g.DisplayName.Contains(search!, StringComparison.OrdinalIgnoreCase));

            var payload = vms.Select(g => new
            {
                id = g.Key,
                displayName = g.DisplayName,
                hasPlugin = g.PluginState == PluginState.Installed,
                pluginState = g.PluginState switch
                {
                    PluginState.Installed => "active",
                    PluginState.Available => "available",
                    _ => "none",
                },
                pluginId = (string?)null,
                isSelected = ReferenceEquals(mainVm.SelectedGame, g),
                source = g.Source.Source == DiscoveredGameSource.Steam ? "steam" : "manual",
                installDir = g.Source.InstallDir,
            }).ToArray();

            return Results.Text(JsonSerializer.Serialize(payload, Json), "application/json");
        });

        app.MapPost("/select-game", async (HttpContext ctx) =>
        {
            var body = await JsonSerializer.DeserializeAsync<SelectGameRequest>(ctx.Request.Body, Json);
            if (body is null || string.IsNullOrWhiteSpace(body.GameId))
                return Problem(StatusCodes.Status400BadRequest, "Missing gameId", "Body erwartet: { \"gameId\": \"…\" }");

            var mainVm = await ui.GetMainVmAsync();
            if (mainVm is null) return Problem(StatusCodes.Status503ServiceUnavailable, "Main window not ready", "MainWindow-VM ist noch nicht initialisiert.");

            var target = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                mainVm.VisibleGames.FirstOrDefault(g => g.Key == body.GameId));
            if (target is null) return Problem(StatusCodes.Status404NotFound, "Game not found", $"Kein Spiel mit id='{body.GameId}' in der Sidebar.");

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => mainVm.SelectedGame = target);
            return Results.NoContent();
        });

        app.MapPost("/launch-game", async (HttpContext ctx) =>
        {
            SelectGameRequest? body = null;
            if (ctx.Request.ContentLength is > 0)
            {
                try { body = await JsonSerializer.DeserializeAsync<SelectGameRequest>(ctx.Request.Body, Json); }
                catch { /* body optional */ }
            }

            var mainVm = await ui.GetMainVmAsync();
            if (mainVm is null) return Problem(StatusCodes.Status503ServiceUnavailable, "Main window not ready", "MainWindow-VM ist noch nicht initialisiert.");

            if (body is not null && !string.IsNullOrWhiteSpace(body.GameId))
            {
                var target = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    mainVm.VisibleGames.FirstOrDefault(g => g.Key == body.GameId));
                if (target is null) return Problem(StatusCodes.Status404NotFound, "Game not found", $"Kein Spiel mit id='{body.GameId}'.");
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => mainVm.SelectedGame = target);
            }

            var launched = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (mainVm.SelectedGame is null || !mainVm.CanLaunchSelected) return (false, (string?)null);
                mainVm.LaunchSelectedGameCommand.Execute(null);
                return (true, mainVm.SelectedGame.Key);
            });

            if (!launched.Item1)
                return Problem(StatusCodes.Status409Conflict, "Nothing to launch", "Kein Spiel gewählt oder Spiel ohne Launch-Weg (weder SteamAppId noch Executable).");

            return Results.Accepted(value: new { gameId = launched.Item2, launched = true, processId = (int?)null, launchCommand = (string?)null });
        });

        app.MapPost("/open-window", async (HttpContext ctx) =>
        {
            var body = await JsonSerializer.DeserializeAsync<OpenWindowRequest>(ctx.Request.Body, Json);
            if (body is null || string.IsNullOrWhiteSpace(body.Window))
                return Problem(StatusCodes.Status400BadRequest, "Missing window key", "Body erwartet: { \"window\": \"settings|about|pluginManager|pluginUpdates\" }");
            try
            {
                await ui.OpenWindowAsync(body.Window);
                return Results.NoContent();
            }
            catch (ArgumentException ex)
            {
                return Problem(StatusCodes.Status400BadRequest, "Unknown window key", ex.Message);
            }
        });

        app.MapPost("/events/click", async (HttpContext ctx) =>
        {
            var body = await JsonSerializer.DeserializeAsync<ClickRequest>(ctx.Request.Body, Json);
            if (body is null || string.IsNullOrWhiteSpace(body.ElementId))
                return Problem(StatusCodes.Status400BadRequest, "Missing elementId", "Body erwartet: { \"elementId\": \"…\" }");
            var clickCount = body.ClickCount ?? 1;
            var res = await ui.ClickAsync(body.ElementId, clickCount);
            if (!res.Success)
                return Results.Json(new
                {
                    type = "about:blank",
                    title = res.Error,
                    status = 404,
                    detail = $"Kein Control mit Name='{body.ElementId}' im aktuellen Visual Tree.",
                    availableElementIds = res.Available,
                }, contentType: "application/problem+json", statusCode: 404);
            return Results.NoContent();
        });

        app.MapPost("/events/text", async (HttpContext ctx) =>
        {
            var body = await JsonSerializer.DeserializeAsync<TextRequest>(ctx.Request.Body, Json);
            if (body is null || string.IsNullOrWhiteSpace(body.ElementId))
                return Problem(StatusCodes.Status400BadRequest, "Missing elementId", "Body erwartet: { \"elementId\": \"…\", \"text\": \"…\" }");
            var res = await ui.SetTextAsync(body.ElementId, body.Text ?? string.Empty, body.SelectAll ?? true);
            if (!res.Success)
            {
                if (res.Available.Count > 0)
                    return Results.Json(new
                    {
                        type = "about:blank",
                        title = res.Error,
                        status = 404,
                        detail = $"Kein Control mit Name='{body.ElementId}' im aktuellen Visual Tree.",
                        availableElementIds = res.Available,
                    }, contentType: "application/problem+json", statusCode: 404);
                return Problem(StatusCodes.Status409Conflict, "Element ohne Text-Support", res.Error ?? "unsupported");
            }
            return Results.NoContent();
        });

        app.MapPost("/screenshot", async (HttpContext ctx, string? format, string? target) =>
        {
            var png = await ui.ScreenshotAsync(target ?? "main");
            if (png is null || png.Length == 0)
                return Problem(StatusCodes.Status409Conflict, "No renderable window", "Kein aktives Fenster oder Fenster hat Größe 0 (App startet noch?).");
            if (format == "json")
            {
                var b64 = Convert.ToBase64String(png);
                return Results.Json(new
                {
                    format = "png",
                    pngBase64 = b64,
                    capturedAt = DateTimeOffset.UtcNow,
                }, contentType: "application/json");
            }
            return Results.File(png, "image/png");
        });

        app.MapGet("/plugins", () =>
        {
            // Bis IHostApiExtender (Contracts v1.1.0) existiert bleibt das leer.
            // Der Endpoint existiert jetzt schon, damit Clients nicht zwischen
            // „Plugin-Extension noch nicht deployed" und „Endpoint fehlt" raten müssen.
            return Results.Json(Array.Empty<object>(), contentType: "application/json");
        });

        // Fallback für /plugins/{...}-Requests solange IHostApiExtender fehlt.
        app.MapMethods("/plugins/{**catchall}",
            new[] { "GET", "POST", "PUT", "DELETE", "PATCH" },
            (string catchall) => Problem(StatusCodes.Status404NotFound,
                "Plugin route not registered",
                $"Keine Plugin-Route '/plugins/{catchall}' registriert. Plugin-Extension-API kommt mit KroModIx.Plugin.Contracts >= 1.1.0."));
    }

    private static IResult Problem(int status, string title, string detail) =>
        Results.Json(new { type = "about:blank", title, status, detail },
            contentType: "application/problem+json",
            statusCode: status);

    private static string? ActiveWindowKey()
    {
        var lifetime = Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var focused = lifetime?.Windows.FirstOrDefault(w => w.IsActive && !ReferenceEquals(w, lifetime.MainWindow));
        return focused?.GetType().Name switch
        {
            "SettingsWindow" => "settings",
            "AboutWindow" => "about",
            "PluginUpdatesWindow" => "pluginUpdates",
            _ => null,
        };
    }

    private sealed record SelectGameRequest(string? GameId);
    private sealed record OpenWindowRequest(string? Window);
    private sealed record ClickRequest(string? ElementId, string? MouseButton, int? ClickCount);
    private sealed record TextRequest(string? ElementId, string? Text, bool? SelectAll);
}
