using System;
using System.Collections.Generic;
using System.IO;
using ModManager.PluginContracts;
using NLog;

namespace ModManager.Services.Plugins;

/// <summary>
/// Findet alle Plugin-Manifeste (<c>plugin.json</c>) in den beiden Standard-
/// Plugin-Ordnern (bundled + user), OHNE die Plugin-Assemblies zu laden.
/// Discovery-Ergebnis ist die Voraussetzung für <see cref="PluginActivationPlanner"/>.
/// </summary>
public sealed class PluginRegistryScanner
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Alle gefundenen Manifeste. Wenn ein Manifest inhaltlich kaputt
    /// ist, wird es im Log gemeldet und übersprungen — nie App-Start blockieren.</summary>
    public IReadOnlyList<DiscoveredPlugin> Scan()
    {
        var result = new List<DiscoveredPlugin>();

        foreach (var pluginsRoot in EnumeratePluginRoots())
        {
            if (!Directory.Exists(pluginsRoot)) continue;
            foreach (var pluginDir in Directory.EnumerateDirectories(pluginsRoot))
            {
                var manifestPath = Path.Combine(pluginDir, "plugin.json");
                if (!File.Exists(manifestPath)) continue;
                try
                {
                    var manifest = PluginManifest.FromFile(manifestPath);
                    result.Add(new DiscoveredPlugin(manifest, pluginDir, manifestPath));
                }
                catch (Exception ex)
                {
                    Log.Warn(ex, "Plugin-Manifest defekt — übersprungen: {Path}", manifestPath);
                }
            }
        }

        Log.Info("Plugin-Scan: {Count} Manifest(e) gefunden", result.Count);
        return result;
    }

    /// <summary>Ordner in Discovery-Reihenfolge: bundled vor user (bei
    /// Konflikt gewinnt User via höhere SemVer, siehe <see cref="PluginActivationPlanner"/>).</summary>
    public static IEnumerable<string> EnumeratePluginRoots()
    {
        yield return AppPaths.BundledPluginsDir;
        yield return AppPaths.UserPluginsDir;
    }
}

/// <summary>Ein gefundenes Plugin (Manifest + Verzeichnis), noch nicht aktiviert.</summary>
public sealed record DiscoveredPlugin(PluginManifest Manifest, string Directory, string ManifestPath)
{
    /// <summary>Vollständiger Pfad zur Plugin-DLL.</summary>
    public string AssemblyPath => Path.Combine(Directory, Manifest.Assembly);
}
