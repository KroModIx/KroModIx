using System;
using System.IO;
using NLog;

namespace KroModIx.Services;

/// <summary>
/// Einmalige Migration bestehender ModManager-Config nach KroModIx.
/// Wird beim App-Start VOR jedem Zugriff auf <see cref="AppPaths.ConfigRoot"/>
/// aufgerufen. No-op wenn:
/// <list type="bullet">
///   <item><c>~/.config/KroModIx/</c> bzw. <c>%APPDATA%\KroModIx\</c> bereits existiert
///     (neue Installation oder Migration schon durch), ODER</item>
///   <item><c>~/.config/ModManager/</c> bzw. <c>%APPDATA%\ModManager\</c> nicht existiert
///     (frischer Rechner, nichts zu migrieren).</item>
/// </list>
///
/// <para>Migration = Copy statt Move — das alte Verzeichnis bleibt als
/// Fallback bestehen, User können es manuell löschen. Ein Marker-File
/// <c>.migrated-to-kromodix</c> im alten Verzeichnis verhindert doppelte
/// Migration falls das neue Verzeichnis später gelöscht/neu angelegt wird.</para>
///
/// <para>Diese Klasse kann bei einem späteren Release entfernt werden
/// (frühestens KroModIx v2.0, wenn User genug Zeit hatten das Update
/// durchlaufen zu lassen).</para>
/// </summary>
public static class ConfigMigrationService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private const string LegacyAppName = "ModManager";
    private const string MigrationMarker = ".migrated-to-kromodix";

    public static void MigrateIfNeeded()
    {
        try
        {
            MigrateConfigDir();
            MigrateCacheDir();
            MigrateStateDir();
        }
        catch (Exception ex)
        {
            // Migration darf NIE den App-Start blocken. Bei Fehler: loggen und weiter.
            Log.Warn(ex, "Config-Migration fehlgeschlagen — App startet trotzdem");
        }
    }

    private static void MigrateConfigDir()
    {
        var legacy = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), LegacyAppName)
            : Path.Combine(XdgOrHome("XDG_CONFIG_HOME", ".config"), LegacyAppName);
        var target = AppPaths.ConfigRoot;
        MigrateDir(legacy, target, "config");
    }

    private static void MigrateCacheDir()
    {
        var legacy = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), LegacyAppName, "cache")
            : Path.Combine(XdgOrHome("XDG_CACHE_HOME", ".cache"), LegacyAppName);
        var target = AppPaths.CacheRoot;
        MigrateDir(legacy, target, "cache");
    }

    private static void MigrateStateDir()
    {
        var legacy = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), LegacyAppName, "state")
            : Path.Combine(XdgOrHome("XDG_STATE_HOME", Path.Combine(".local", "state")), LegacyAppName);
        var target = AppPaths.StateRoot;
        MigrateDir(legacy, target, "state");
    }

    /// <summary>Sub-Ordner die NICHT migriert werden. <c>plugins/</c> enthält
    /// die alten <c>ModManager.Plugins.*.dll</c>-Deployments — die sind
    /// obsolet, der neue Host lädt neue <c>KroModIx.Plugin.*.dll</c>-Bundles.
    /// User muss die Plugins einmal neu installieren (via Install-Karte oder
    /// per Deploy-Skript). Migration wäre kontraproduktiv.</summary>
    private static readonly string[] SkipSubDirs = { "plugins" };

    private static void MigrateDir(string legacy, string target, string kind)
    {
        if (!Directory.Exists(legacy)) return;
        if (File.Exists(Path.Combine(legacy, MigrationMarker))) return;

        Log.Info("Migriere ModManager-{Kind}: {Legacy} → {Target}", kind, legacy, target);
        Directory.CreateDirectory(target);
        int copied = 0, skipped = 0;

        // Files auf Root-Ebene mergen — vorhandene Files im Target werden
        // NICHT überschrieben (User's neue Config gewinnt).
        foreach (var file in Directory.EnumerateFiles(legacy))
        {
            var name = Path.GetFileName(file);
            if (name == MigrationMarker) continue;
            var dest = Path.Combine(target, name);
            if (File.Exists(dest))
            {
                Log.Debug("Migration {Kind}: {Name} existiert schon im Ziel, übersprungen", kind, name);
                skipped++;
                continue;
            }
            File.Copy(file, dest);
            copied++;
        }

        // Sub-Verzeichnisse: alle außer SkipSubDirs komplett rekursiv kopieren.
        foreach (var sub in Directory.EnumerateDirectories(legacy))
        {
            var subName = Path.GetFileName(sub);
            if (Array.Exists(SkipSubDirs, s => string.Equals(s, subName, StringComparison.OrdinalIgnoreCase)))
            {
                Log.Info("Migration {Kind}: Sub-Ordner {Sub} übersprungen (obsolet, neue Deployments überschreiben)", kind, subName);
                continue;
            }
            var subTarget = Path.Combine(target, subName);
            var (c, s) = CopyDirRecursive(sub, subTarget);
            copied += c; skipped += s;
        }

        WriteMarker(legacy);
        Log.Info("Migration {Kind} fertig: {Copied} kopiert, {Skipped} übersprungen (bereits vorhanden).",
            kind, copied, skipped);
    }

    private static (int copied, int skipped) CopyDirRecursive(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        int copied = 0, skipped = 0;
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file);
            var target = Path.Combine(dst, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target)) { skipped++; continue; }
            File.Copy(file, target);
            copied++;
        }
        return (copied, skipped);
    }

    private static void WriteMarker(string legacy)
    {
        try
        {
            File.WriteAllText(Path.Combine(legacy, MigrationMarker),
                $"Migriert nach KroModIx am {DateTime.UtcNow:o}. Dieses Verzeichnis kann gelöscht werden.\n");
        }
        catch { /* nicht schreibbar → Migration läuft beim nächsten Start halt nochmal, ist idempotent */ }
    }

    private static string XdgOrHome(string envVar, string fallbackRelative)
    {
        var v = Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrWhiteSpace(v)) return v;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, fallbackRelative);
    }
}
