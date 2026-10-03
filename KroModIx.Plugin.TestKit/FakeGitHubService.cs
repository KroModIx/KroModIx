using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KroModIx.Plugin.Contracts;

namespace KroModIx.Plugin.TestKit;

/// <summary>Attrappe für <see cref="IGitHubService"/>: Ausgaben werden im
/// Test hinterlegt, nichts geht ins Netz.
///
/// <para><b>Warum eine Attrappe und nicht der echte Dienst.</b> Ein Test,
/// der GitHub wirklich fragt, ist langsam, braucht eine Verbindung und
/// zählt auf dasselbe Stundenkontingent von 60 Anfragen, das der Nutzer
/// braucht. Vor allem aber wäre er nicht wiederholbar: er prüft, was
/// GitHub heute als neueste Ausgabe von UE4SS ausliefert, nicht, was das
/// Plugin daraus macht.</para>
///
/// <para><b>Was man damit prüfen kann</b>, und wofür es sonst keinen Weg
/// gäbe: den <see cref="RateLimited"/>-Fall. Greift die Raten-Sperre, kennt
/// der echte Dienst nur den Tag und keine Dateiliste — ein Plugin muss die
/// URL dann selbst bilden. Genau dieser Zweig läuft im Alltag fast nie und
/// ist deshalb der, der unbemerkt kaputtgeht.</para></summary>
public class FakeGitHubService : IGitHubService
{
    private readonly Dictionary<string, List<GitHubRelease>> _releases =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Jede Abfrage mit dem angefragten Repo — um zu prüfen, dass
    /// ein Plugin einmal fragt und nicht bei jedem Aufbau der Oberfläche
    /// erneut.</summary>
    public List<string> Queries { get; } = [];

    /// <summary>Simuliert ein erschöpftes Stundenkontingent. Dann liefern
    /// <see cref="GetLatestReleaseAsync"/> eine Ausgabe <b>ohne</b> Dateien
    /// und mit <c>FromRedirectChase = true</c>,
    /// <see cref="GetReleasesAsync"/> eine leere Liste und
    /// <see cref="FindLatestAssetAsync"/> <c>null</c> — genau wie der echte
    /// Dienst auf dem Umleitungs-Pfad.</summary>
    public bool RateLimited { get; set; }

    public bool IsRateLimited => RateLimited;

    /// <summary>Hinterlegt eine Ausgabe. Mehrfach aufgerufen gilt die
    /// zuerst hinterlegte als die neueste — dieselbe Reihenfolge, die die
    /// GitHub-API liefert.</summary>
    public FakeGitHubService AddRelease(string repo, string tag, params string[] assetNames)
    {
        var assets = assetNames
            .Select(n => new GitHubAsset(n, BuildAssetUrl(repo, tag, n), 1024))
            .ToList();
        if (!_releases.TryGetValue(repo, out var list))
            _releases[repo] = list = [];
        list.Add(new GitHubRelease(tag, tag.TrimStart('v', 'V'), $"https://github.com/{repo}/releases/tag/{tag}",
            assets, FromRedirectChase: false));
        return this;
    }

    public Task<GitHubRelease?> GetLatestReleaseAsync(string repo, CancellationToken ct = default)
    {
        Queries.Add(repo);
        if (!_releases.TryGetValue(repo, out var list) || list.Count == 0)
            return Task.FromResult<GitHubRelease?>(null);
        var r = list[0];
        if (RateLimited)
            r = r with { Assets = Array.Empty<GitHubAsset>(), FromRedirectChase = true };
        return Task.FromResult<GitHubRelease?>(r);
    }

    public Task<IReadOnlyList<GitHubRelease>> GetReleasesAsync(string repo, int max = 15,
        bool includePrerelease = false, CancellationToken ct = default)
    {
        Queries.Add(repo);
        if (RateLimited || !_releases.TryGetValue(repo, out var list))
            return Task.FromResult<IReadOnlyList<GitHubRelease>>(Array.Empty<GitHubRelease>());
        return Task.FromResult<IReadOnlyList<GitHubRelease>>(list.Take(max).ToList());
    }

    public Task<(GitHubRelease Release, GitHubAsset Asset)?> FindLatestAssetAsync(string repo,
        Func<string, bool> assetNameMatches, CancellationToken ct = default)
    {
        Queries.Add(repo);
        if (RateLimited || !_releases.TryGetValue(repo, out var list))
            return Task.FromResult<(GitHubRelease, GitHubAsset)?>(null);
        foreach (var r in list)
        {
            var a = r.Assets.FirstOrDefault(x => assetNameMatches(x.Name));
            if (a is not null) return Task.FromResult<(GitHubRelease, GitHubAsset)?>((r, a));
        }
        return Task.FromResult<(GitHubRelease, GitHubAsset)?>(null);
    }

    public string BuildAssetUrl(string repo, string tag, string assetName)
        => $"https://github.com/{repo}/releases/download/{tag}/{assetName}";
}
