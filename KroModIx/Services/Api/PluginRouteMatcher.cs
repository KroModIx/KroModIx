using System;
using System.Collections.Generic;
using KroModIx.Plugin.Contracts;

namespace KroModIx.Services.Api;

/// <summary>
/// Matched einen Request-Method/Path (relativ zum Plugin) gegen die vom Plugin
/// gemeldeten <see cref="PluginApiEndpoint"/>-Definitionen. Unterstützt
/// <c>{name}</c>-Segmente ohne Regex, das reicht für alles was Plugins realistisch
/// als Route deklarieren. Wenn wir mal Wildcards oder Regex brauchen: hier
/// erweitern, nicht die Contracts aufblähen.
/// </summary>
internal static class PluginRouteMatcher
{
    public readonly record struct Match(PluginApiEndpoint Endpoint, IReadOnlyDictionary<string, string?> RouteValues);

    public static Match? TryMatch(
        IReadOnlyList<PluginApiEndpoint> endpoints,
        string method,
        string relativePath)
    {
        var pathSegments = relativePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var ep in endpoints)
        {
            if (!string.Equals(ep.HttpMethod, method, StringComparison.OrdinalIgnoreCase)) continue;
            var epSegments = ep.RelativePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (epSegments.Length != pathSegments.Length) continue;

            var vals = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            bool ok = true;
            for (int i = 0; i < epSegments.Length; i++)
            {
                var seg = epSegments[i];
                if (seg.Length >= 2 && seg[0] == '{' && seg[^1] == '}')
                    vals[seg[1..^1]] = pathSegments[i];
                else if (!string.Equals(seg, pathSegments[i], StringComparison.OrdinalIgnoreCase))
                { ok = false; break; }
            }
            if (ok) return new Match(ep, vals);
        }
        return null;
    }
}
