using System;
using System.Collections.Generic;
using System.Linq;
using KroModIx.Services.Games;

namespace KroModIx.Services.Plugins;

/// <summary>Entscheidet welche PluginIndex-Plugins der Host ungefragt
/// nachinstallieren darf. Reine Mengenlogik, kein IO — die Ausfuehrung macht
/// <see cref="PluginAutoInstallService"/>.
///
/// <para>Ein Plugin wird nur geplant wenn ALLE Bedingungen gelten:</para>
/// <list type="number">
/// <item>Mindestens ein Spiel in der Sidebar matcht es
///   (<see cref="PluginIndexMatcher"/>: SteamAppId ODER Engine-Slug) — der
///   Host laedt nie Plugins „auf Vorrat".</item>
/// <item>Es ist lokal nicht installiert (weder user- noch bundled-Ordner).</item>
/// <item>Der User hat es nicht in der Plugin-Verwaltung deinstalliert
///   (Opt-out-Liste in den AppSettings).</item>
/// <item>Es steht nicht in der Retry-Sperre eines kuerzlich gescheiterten
///   Versuchs — ohne die wuerde ein Rechner mit GitHub-Rate-Limit bei jedem
///   Discovery-Refresh erneut anfragen.</item>
/// <item>Der Index-Eintrag hat eine brauchbare GitHub-UpdateSource, sonst
///   kann <see cref="PluginInstaller"/> ohnehin nichts holen.</item>
/// </list>
/// </summary>
public static class PluginAutoInstallPlanner
{
    /// <summary>Die nachzuinstallierenden Eintraege, dedupliziert und stabil
    /// nach Id sortiert (deterministische Reihenfolge fuer Log und Tests).</summary>
    public static IReadOnlyList<PluginIndexEntry> Plan(
        PluginIndex? index,
        IReadOnlyList<DiscoveredGame> games,
        IReadOnlyCollection<string> installedPluginIds,
        IReadOnlyCollection<string> optedOutPluginIds,
        IReadOnlyCollection<string> alreadyAttemptedIds)
    {
        if (index is null || index.Plugins.Count == 0 || games.Count == 0)
            return Array.Empty<PluginIndexEntry>();

        var blocked = new HashSet<string>(installedPluginIds, StringComparer.OrdinalIgnoreCase);
        blocked.UnionWith(optedOutPluginIds);
        blocked.UnionWith(alreadyAttemptedIds);

        var byId = new Dictionary<string, PluginIndexEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in games)
            foreach (var entry in PluginIndexMatcher.EntriesFor(index, game))
            {
                if (string.IsNullOrWhiteSpace(entry.Id)) continue;
                if (blocked.Contains(entry.Id)) continue;
                if (!IsInstallable(entry)) continue;
                byId.TryAdd(entry.Id, entry);
            }

        return byId.Values
            .OrderBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Spiegelt die Vorbedingung von <see cref="PluginInstaller.InstallLatestAsync"/>
    /// — ohne GitHub-Repo waere der Download-Versuch nur ein Log-Rauschen.</summary>
    private static bool IsInstallable(PluginIndexEntry entry)
        => entry.UpdateSource is { } src
           && string.Equals(src.Kind, "github", StringComparison.OrdinalIgnoreCase)
           && !string.IsNullOrWhiteSpace(src.Repo);
}
