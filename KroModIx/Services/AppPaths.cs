using System;
using System.IO;

namespace KroModIx.Services;

/// <summary>
/// Zentrale Datei-Pfade für Config, Cache und Plugin-Verzeichnisse.
/// Plattformkonform (%APPDATA% + %LOCALAPPDATA% / $XDG_CONFIG_HOME + $XDG_CACHE_HOME).
/// </summary>
public static class AppPaths
{
    private const string AppName = "KroModIx";

    public static string ConfigRoot
    {
        get
        {
            var dir = OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName)
                : Path.Combine(XdgOrHome("XDG_CONFIG_HOME", ".config"), AppName);
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string CacheRoot
    {
        get
        {
            var dir = OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName, "cache")
                : Path.Combine(XdgOrHome("XDG_CACHE_HOME", ".cache"), AppName);
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string StateRoot
    {
        get
        {
            var dir = OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName, "state")
                : Path.Combine(XdgOrHome("XDG_STATE_HOME", Path.Combine(".local", "state")), AppName);
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>User-Plugin-Ordner. Jedes Plugin liegt in einem eigenen Unterordner.</summary>
    public static string UserPluginsDir
    {
        get
        {
            var dir = Path.Combine(ConfigRoot, "plugins");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Mit der App gebundelter Plugins-Ordner (neben der Exe).</summary>
    public static string BundledPluginsDir
        => Path.Combine(AppContext.BaseDirectory, "plugins");

    /// <summary>Cover-Bilder-Cache für die Spiele-Sidebar.</summary>
    public static string GameCoverCacheDir
    {
        get
        {
            var dir = Path.Combine(CacheRoot, "game-covers");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Update-Arbeitsverzeichnis für Self-Update (NIE AppContext.BaseDirectory —
    /// AppImage-Squashfs ist read-only, siehe RenPack/LS-ModManager UpdateService).</summary>
    public static string UpdateWorkDir
    {
        get
        {
            var dir = Path.Combine(StateRoot, "update");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static string XdgOrHome(string envVar, string fallbackRelative)
    {
        var v = Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrWhiteSpace(v)) return v;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, fallbackRelative);
    }
}
