using System;
using System.Diagnostics;
using System.IO;
using ModManager.Services.Plugins;
using NLog;

namespace ModManager.Services.Games;

/// <summary>
/// Startet ein discovered Spiel. Reihenfolge: (1) Steam-Protokoll
/// <c>steam://run/&lt;AppId&gt;</c> wenn SteamAppId gesetzt (funktioniert
/// Windows + Linux/Proton, Steam kümmert sich um Prefix + Launch-Optionen),
/// (2) direkter Exe-Start via <see cref="ProcessStartInfo.ArgumentList"/>
/// wenn <see cref="DiscoveredGame.ExecutablePath"/> gesetzt (Manual-Games),
/// (3) Fehler.
///
/// <para>Ohne Steam-Protokoll wäre kein Proton-Handling möglich —
/// deshalb ist SteamAppId der bevorzugte Weg auch für manuell hinzugefügte
/// Spiele die eine Steam-AppId haben.</para>
/// </summary>
public sealed class GameLauncherService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public sealed record LaunchResult(bool Success, string Message);

    public LaunchResult Launch(DiscoveredGame game)
    {
        if (game.SteamAppId is int appId)
        {
            try
            {
                var uri = $"steam://run/{appId}";
                Log.Info("Launch via Steam: {Uri} ({Name})", uri, game.DisplayName);
                // xdg-open/Windows-Shell öffnet das steam://-Protokoll.
                // ArgumentList ist hier egal (URL ohne Whitespace), aber
                // konsistent mit dem Shell-Helper aus HostShellImpl.
                if (OperatingSystem.IsWindows())
                    Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
                else
                    StartLinuxOpen("xdg-open", uri);
                return new LaunchResult(true, $"Starte {game.DisplayName} via Steam …");
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Steam-Launch fehlgeschlagen für AppId {AppId}", appId);
                return new LaunchResult(false, $"Steam-Launch fehlgeschlagen: {ex.Message}");
            }
        }

        if (!string.IsNullOrWhiteSpace(game.ExecutablePath))
        {
            var exe = game.ExecutablePath!;
            if (!File.Exists(exe))
                return new LaunchResult(false, $"Executable nicht gefunden: {exe}");
            try
            {
                Log.Info("Launch via Executable: {Path} ({Name})", exe, game.DisplayName);
                var psi = new ProcessStartInfo(exe)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
                };
                Process.Start(psi);
                return new LaunchResult(true, $"Starte {game.DisplayName} …");
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Executable-Launch fehlgeschlagen: {Path}", exe);
                return new LaunchResult(false, $"Start fehlgeschlagen: {ex.Message}");
            }
        }

        return new LaunchResult(false,
            "Kein Launch-Weg konfiguriert — Steam-AppId oder Executable-Pfad fehlt.");
    }

    /// <summary>Kopie aus <see cref="HostShellImpl.StartLinuxOpen"/> —
    /// ArgumentList statt Arguments-String, damit Pfade mit Leerzeichen
    /// nicht am ersten Space gesplittet werden.</summary>
    private static void StartLinuxOpen(string command, string arg)
    {
        var psi = new ProcessStartInfo(command) { UseShellExecute = false };
        psi.ArgumentList.Add(arg);
        Process.Start(psi);
    }
}
