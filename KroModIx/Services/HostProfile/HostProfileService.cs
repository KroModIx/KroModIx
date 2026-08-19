using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using KroModIx.Services.Games;
using KroModIx.Services.Plugins;
using KroModIx.Services.Storage;
using NLog;

namespace KroModIx.Services.HostProfile;

/// <summary>v1.12: Multi-Host-Setup — exportiert die Basis-Konfiguration eines
/// Rechners (installierte Plugins + Manual-Games) als JSON, damit ein zweiter
/// Rechner initial gleich eingerichtet werden kann. Import installiert die
/// fehlenden Plugins nach (Download via <see cref="PluginInstaller"/>) und
/// fügt Manual-Game-Einträge hinzu (dedup gegen bestehende via InstallDir).
///
/// <para>Was NICHT exportiert wird: Plugin-lokale Config, Cache, Secrets
/// (DPAPI/libsecret sind maschinen-gebunden). Der User loggt sich auf dem
/// zweiten Rechner manuell erneut ein (f95zone, Nexus). Auch nicht
/// exportiert: die installierten Mods pro Spiel — die kommen ueber die
/// jeweiligen Plugin-Katalog/Backup-Features (LS25.Backup etc.).</para>
///
/// <para>Kein Auto-Sync — nur manueller Export/Import. Auto-Sync waere ein
/// separates Backend (rclone/git-Remote), waere komplexer und selten
/// gebraucht (User haben typischerweise 1-2 Rechner).</para></summary>
public sealed class HostProfileService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly PluginRegistryScanner _scanner;
    private readonly ManualGamesService _manual;

    public HostProfileService(PluginRegistryScanner scanner, ManualGamesService manual)
    {
        _scanner = scanner;
        _manual = manual;
    }

    /// <summary>Sammelt den aktuellen Host-Zustand + schreibt JSON.</summary>
    public HostProfile Export()
    {
        var installed = _scanner.Scan()
            .Where(p => IsUnderUserPluginsDir(p.Directory))
            .Select(p => new HostProfilePluginRef(p.Manifest.Id, p.Manifest.Version))
            .OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var manuals = _manual.All
            .Select(g => new HostProfileManualGame(
                g.DisplayName,
                g.InstallDir,
                g.ExecutablePath,
                g.SteamAppId,
                g.Engine))
            .OrderBy(g => g.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new HostProfile
        {
            Schema = 1,
            ExportedAtUtc = DateTime.UtcNow,
            Plugins = installed,
            ManualGames = manuals,
        };
    }

    public void SaveToFile(HostProfile profile, string path)
    {
        JsonFileStore.WriteAtomic(path, JsonSerializer.Serialize(profile, JsonOpts));
        Log.Info("Host-Profile exportiert: {P} ({Plugins} Plugins, {Games} Manual-Games)",
            path, profile.Plugins.Count, profile.ManualGames.Count);
    }

    public HostProfile? LoadFromFile(string path)
    {
        string json;
        try { json = File.ReadAllText(path); }
        catch (Exception ex)
        {
            // IO-Fehler → null, aber KEIN Quarantine (User haette sonst
            // sein Export-JSON auf einem USB-Stick verloren wenn's kurz
            // klemmt).
            Log.Warn(ex, "Host-Profile nicht lesbar (temporaer?): {P}", path);
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<HostProfile>(json, JsonOpts);
        }
        catch (JsonException ex)
        {
            Log.Error(ex, "Host-Profile-JSON defekt: {P}", path);
            // Import-File — kein Quarantine (der User will das File selbst
            // reparieren, nicht dass wir es umbenennen).
            return null;
        }
    }

    /// <summary>Fügt fehlende Manual-Game-Einträge aus dem Profile ins lokale
    /// <see cref="ManualGamesService"/> ein (dedup gegen bestehende
    /// InstallDirs). Rückgabe: Anzahl neu angelegter Einträge. Existierende
    /// Games werden NICHT überschrieben.</summary>
    public int ImportManualGames(HostProfile profile)
    {
        int added = 0;
        var existingDirs = new HashSet<string>(
            _manual.All.Select(g => g.InstallDir),
            StringComparer.OrdinalIgnoreCase);
        foreach (var g in profile.ManualGames)
        {
            if (string.IsNullOrEmpty(g.InstallDir)) continue;
            if (existingDirs.Contains(g.InstallDir)) continue;
            _manual.Add(g.DisplayName, g.InstallDir, g.ExecutablePath,
                coverPath: null, steamAppId: g.SteamAppId, engine: g.Engine);
            existingDirs.Add(g.InstallDir);
            added++;
        }
        return added;
    }

    /// <summary>Berechnet welche Plugins im Profile fehlen — der Aufrufer
    /// nutzt den <see cref="PluginInstaller"/> um sie zu installieren.</summary>
    public IReadOnlyList<HostProfilePluginRef> MissingPlugins(HostProfile profile)
    {
        var installedIds = new HashSet<string>(
            _scanner.Scan().Select(p => p.Manifest.Id),
            StringComparer.OrdinalIgnoreCase);
        return profile.Plugins
            .Where(p => !installedIds.Contains(p.Id))
            .ToList();
    }

    private static bool IsUnderUserPluginsDir(string pluginDir)
    {
        try
        {
            var full = Path.GetFullPath(pluginDir);
            var root = Path.GetFullPath(AppPaths.UserPluginsDir);
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}

public sealed class HostProfile
{
    public int Schema { get; set; } = 1;
    public DateTime ExportedAtUtc { get; set; }
    public List<HostProfilePluginRef> Plugins { get; set; } = new();
    public List<HostProfileManualGame> ManualGames { get; set; } = new();
}

/// <summary>Ein Plugin-Verweis im Export — nur ID + installierte Version.
/// Beim Import prueft der Host den PluginIndex und laedt die neueste
/// verfuegbare Version (also nicht zwingend die exportierte Version).</summary>
public sealed record HostProfilePluginRef(string Id, string Version);

/// <summary>Ein Manual-Game im Export. Cover-Pfad wird bewusst NICHT exportiert
/// (ist maschinen-lokal). Beim Import setzt das Plugin bei Nutzung selbst
/// wieder einen Cover via <c>TrySetManualGameCover</c>.</summary>
public sealed record HostProfileManualGame(
    string DisplayName, string InstallDir, string? ExecutablePath,
    int? SteamAppId, string? Engine);
