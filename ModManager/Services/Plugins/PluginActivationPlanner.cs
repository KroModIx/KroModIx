using System;
using System.Collections.Generic;
using System.Linq;
using ModManager.PluginContracts;
using ModManager.Services.Games;
using NLog;

namespace ModManager.Services.Plugins;

/// <summary>
/// Entscheidet, welche der von <see cref="PluginRegistryScanner"/> gefundenen
/// Manifeste tatsächlich aktiviert werden sollen. Kriterien:
/// <list type="number">
/// <item>Mindest-Host-Version erfüllt.</item>
/// <item>Mindestens ein <see cref="GameTarget.SteamAppId"/> ist installiert
/// ODER die Plugin-ID steht in <c>settings.AlwaysActivePluginIds</c>.</item>
/// <item>Bei Konflikt (mehrere Plugins mit derselben AppId): höhere SemVer gewinnt.</item>
/// </list>
/// </summary>
public sealed class PluginActivationPlanner
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly AppSettingsService _settings;

    public PluginActivationPlanner(AppSettingsService settings)
    {
        _settings = settings;
    }

    /// <summary>Für Runtime-Install (M4 „Install-Karte"): ein einzelnes Plugin
    /// entscheiden, ohne die installierte Games-Liste erneut zu iterieren.
    /// Nutzt <see cref="GameDiscoveryService"/> nicht — Caller liefert bereits
    /// gefilterte Info; wir prüfen nur MinHostVersion.</summary>
    public PluginActivationDecision PlanSingle(DiscoveredPlugin plugin, Version hostVersion)
    {
        if (!TryParseVersion(plugin.Manifest.MinHostVersion, out var minHost))
            return new PluginActivationDecision(plugin, false, ActivationSkipReason.HostTooOld,
                Array.Empty<DiscoveredGame>());
        if (hostVersion < minHost)
            return new PluginActivationDecision(plugin, false, ActivationSkipReason.HostTooOld,
                Array.Empty<DiscoveredGame>());
        return new PluginActivationDecision(plugin, true, ActivationSkipReason.None,
            Array.Empty<DiscoveredGame>());
    }

    /// <summary>Löst discovered Plugins gegen die aktuell installierten Spiele auf.</summary>
    public IReadOnlyList<PluginActivationDecision> Plan(
        IReadOnlyList<DiscoveredPlugin> discovered,
        IReadOnlyList<DiscoveredGame> games,
        Version hostVersion)
    {
        var installedAppIds = games
            .Where(g => g.SteamAppId is not null)
            .Select(g => g.SteamAppId!.Value)
            .ToHashSet();
        var forceActive = _settings.Current.AlwaysActivePluginIds
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var decisions = new List<PluginActivationDecision>();
        foreach (var plugin in discovered)
        {
            if (!TryParseVersion(plugin.Manifest.MinHostVersion, out var minHost))
            {
                Log.Warn("Plugin {Id} v{Ver}: minHostVersion '{MinHost}' unparsbar — übersprungen",
                    plugin.Manifest.Id, plugin.Manifest.Version, plugin.Manifest.MinHostVersion);
                continue;
            }
            if (hostVersion < minHost)
            {
                decisions.Add(new PluginActivationDecision(plugin, false,
                    ActivationSkipReason.HostTooOld,
                    MatchedGames: Array.Empty<DiscoveredGame>()));
                continue;
            }

            var matched = new List<DiscoveredGame>();
            foreach (var target in plugin.Manifest.Targets)
            {
                if (target.SteamAppId is int appId && installedAppIds.Contains(appId))
                    matched.AddRange(games.Where(g => g.SteamAppId == appId));
            }

            bool forced = forceActive.Contains(plugin.Manifest.Id);
            bool activate = matched.Count > 0 || forced;

            decisions.Add(new PluginActivationDecision(
                plugin, activate,
                activate
                    ? ActivationSkipReason.None
                    : (forced ? ActivationSkipReason.None : ActivationSkipReason.NoTargetInstalled),
                matched));
        }

        // Konflikt: mehrere aktivierte Plugins beanspruchen dieselbe AppId → höhere SemVer gewinnt.
        var duplicatesByAppId = decisions
            .Where(d => d.Activate)
            .SelectMany(d => d.Plugin.Manifest.Targets
                .Where(t => t.SteamAppId is not null)
                .Select(t => (t.SteamAppId!.Value, d)))
            .GroupBy(x => x.Value)
            .Where(g => g.Select(x => x.d.Plugin.Manifest.Id).Distinct().Count() > 1);

        foreach (var group in duplicatesByAppId)
        {
            var ordered = group
                .OrderByDescending(x => TryParseVersion(x.d.Plugin.Manifest.Version, out var v) ? v : new Version(0, 0))
                .ToList();
            var winner = ordered.First().d;
            Log.Warn("Plugin-Konflikt für AppId {AppId}: {N} Plugins beanspruchen es — {Winner} v{V} gewinnt",
                group.Key, group.Count(), winner.Plugin.Manifest.Id, winner.Plugin.Manifest.Version);
            foreach (var loser in ordered.Skip(1))
            {
                var idx = decisions.IndexOf(loser.d);
                decisions[idx] = loser.d with { Activate = false, SkipReason = ActivationSkipReason.LostConflict };
            }
        }

        Log.Info("Plan: {Active} von {Total} Plugin(s) werden aktiviert",
            decisions.Count(d => d.Activate), decisions.Count);
        return decisions;
    }

    private static bool TryParseVersion(string s, out Version v)
    {
        // Semver-Prerelease-Tail (-alpha, -rc.1, +metadata) abschneiden
        int dash = s.IndexOf('-');
        if (dash >= 0) s = s[..dash];
        int plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];
        return Version.TryParse(s, out v!);
    }
}

public enum ActivationSkipReason
{
    None,
    HostTooOld,
    NoTargetInstalled,
    LostConflict,
}

public sealed record PluginActivationDecision(
    DiscoveredPlugin Plugin,
    bool Activate,
    ActivationSkipReason SkipReason,
    IReadOnlyList<DiscoveredGame> MatchedGames);
