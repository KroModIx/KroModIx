using System;
using System.Collections.Generic;
using System.Linq;
using ModManager.Services.Steam;
using NLog;


namespace ModManager.Services.Games;

/// <summary>
/// Aggregiert Steam-Discovery und manuelle Spiele zu einer einheitlichen Liste.
/// Idempotent, deduplizierend: ein Steam-Spiel und ein manuelles Spiel mit
/// derselben <c>SteamAppId</c> werden zu einem Eintrag zusammengeführt (Steam gewinnt).
/// </summary>
public sealed class GameDiscoveryService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly SteamLibraryService _steam;
    private readonly ManualGamesService _manual;

    public GameDiscoveryService(SteamLibraryService steam, ManualGamesService manual)
    {
        _steam = steam;
        _manual = manual;
    }

    /// <summary>Fresh scan of Steam + manual entries. Nicht gecacht — Caller entscheidet Cadence.
    /// Steam-Tools (Runtimes, Proton, Redistributables) werden via
    /// <see cref="SteamGameFilter"/> ausgeblendet.</summary>
    public IReadOnlyList<DiscoveredGame> Discover()
    {
        var result = new List<DiscoveredGame>();
        var steamAppIdsSeen = new HashSet<int>();
        var toolsSkipped = 0;

        foreach (var s in _steam.EnumerateInstalledGames())
        {
            if (SteamGameFilter.IsTool(s.AppId, s.Name)) { toolsSkipped++; continue; }
            steamAppIdsSeen.Add(s.AppId);
            result.Add(new DiscoveredGame(
                Key: $"steam:{s.AppId}",
                DisplayName: s.Name,
                InstallDir: s.InstallDir,
                SteamAppId: s.AppId,
                ManualId: null,
                CustomCoverPath: null,
                Source: DiscoveredGameSource.Steam));
        }
        if (toolsSkipped > 0)
            Log.Debug("Discovery: {N} Steam-Tools ausgefiltert (Runtimes/Proton/Redistributables)", toolsSkipped);

        foreach (var m in _manual.All)
        {
            // Falls ein manueller Eintrag eine Steam-AppId hat, die schon von
            // Steam-Discovery gefunden wurde: Steam gewinnt. Der manuelle Eintrag
            // bleibt in der Persistenz erhalten (User hatte einen Grund ihn anzulegen),
            // taucht aber nicht doppelt in der Sidebar auf.
            if (m.SteamAppId is int appId && steamAppIdsSeen.Contains(appId)) continue;

            result.Add(new DiscoveredGame(
                Key: $"manual:{m.Id}",
                DisplayName: m.DisplayName,
                InstallDir: m.InstallDir,
                SteamAppId: m.SteamAppId,
                ManualId: m.Id,
                CustomCoverPath: m.CoverPath,
                Source: DiscoveredGameSource.Manual));
        }

        Log.Info("Discovery: {Total} Spiele ({Steam} via Steam, {Manual} manuell)",
            result.Count, result.Count(g => g.Source == DiscoveredGameSource.Steam),
            result.Count(g => g.Source == DiscoveredGameSource.Manual));
        return result;
    }
}
