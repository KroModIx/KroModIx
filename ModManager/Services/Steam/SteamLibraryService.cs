using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;

namespace ModManager.Services.Steam;

/// <summary>
/// Enumeriert Steam-Library-Roots und alle installierten Spiele. Auf Windows
/// aus <c>Program Files (x86)/Steam</c> (via Registry oder Default-Pfad) und
/// den Extra-Libraries aus <c>libraryfolders.vdf</c>. Auf Linux aus den bekannten
/// Home-Locations plus VDF-Extras plus Bazzite-Mount-Points (<c>/run/media/*/*</c>).
///
/// Generalisiert aus <c>LS-ModManager/LSModManager/Services/ModPathService.cs</c> —
/// dort war die Logik auf FS25 zugeschnitten, hier liefern wir eine reine
/// Discovery-Schicht, aus der Game-Plugins ihre spielspezifischen Pfade ableiten.
/// </summary>
public sealed class SteamLibraryService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    // Proton legt "My Documents" (XP-Style) statt "Documents" an.
    private static readonly string[] DocumentsFolderCandidates = ["My Documents", "Documents"];

    /// <summary>Alle Library-Roots (Ordner, in denen ein <c>steamapps/</c>-Unterordner
    /// existiert), dedupliziert und in Discovery-Reihenfolge.</summary>
    public IReadOnlyList<string> EnumerateLibraryRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var root in EnumerateLibraryRootCandidates())
        {
            if (!Directory.Exists(Path.Combine(root, "steamapps"))) continue;
            var normalized = root.TrimEnd(Path.DirectorySeparatorChar);
            if (seen.Add(normalized)) result.Add(normalized);
        }
        Log.Debug("Steam library roots: {N} gefunden", result.Count);
        return result;
    }

    /// <summary>Alle installierten Steam-Spiele über alle Library-Roots hinweg.
    /// Bazzite/Symlink-Falle: derselbe Root taucht via <c>/var/home/…</c> und
    /// <c>/home/…</c> beide auf; wir deduplizieren nach AppId (erste Auflösung gewinnt).</summary>
    public IReadOnlyList<InstalledSteamGame> EnumerateInstalledGames()
    {
        var seen = new HashSet<int>();
        var games = new List<InstalledSteamGame>();
        var roots = EnumerateLibraryRoots();
        foreach (var root in roots)
        {
            var appsDir = Path.Combine(root, "steamapps");
            IEnumerable<string> manifests;
            try { manifests = Directory.EnumerateFiles(appsDir, "appmanifest_*.acf"); }
            catch (Exception ex)
            {
                Log.Debug(ex, "Konnte {Dir} nicht auflisten", appsDir);
                continue;
            }

            foreach (var manifest in manifests)
            {
                var game = TryParseManifest(manifest, root);
                if (game is null) continue;
                if (!seen.Add(game.AppId)) continue;
                games.Add(game);
            }
        }
        Log.Info("Steam-Discovery: {Count} Spiele in {Roots} Library-Roots",
            games.Count, roots.Count);
        return games;
    }

    /// <summary>Findet für eine Steam-AppId den Proton-Präfix-Pfad (falls Linux).
    /// Auf Windows null.</summary>
    public string? FindProtonPrefix(int appId)
    {
        if (OperatingSystem.IsWindows()) return null;
        foreach (var root in EnumerateLibraryRoots())
        {
            var prefix = Path.Combine(root, "steamapps", "compatdata", appId.ToString(), "pfx");
            if (Directory.Exists(prefix)) return prefix;
        }
        return null;
    }

    /// <summary>Findet für eine Steam-AppId den User-Documents-Pfad im Proton-
    /// Präfix (z.B. für <c>My Games/FarmingSimulator2025</c>).</summary>
    public string? FindProtonUserDocumentsDir(int appId)
    {
        var prefix = FindProtonPrefix(appId);
        if (prefix is null) return null;
        foreach (var docs in DocumentsFolderCandidates)
        {
            var candidate = Path.Combine(prefix, "drive_c", "users", "steamuser", docs);
            if (Directory.Exists(candidate)) return candidate;
        }
        // Nicht existierender Documents-Pfad — wir liefern den bevorzugten
        // Kandidaten trotzdem zurück, damit das Game-Plugin ihn anlegen kann.
        return Path.Combine(prefix, "drive_c", "users", "steamuser", DocumentsFolderCandidates[0]);
    }

    // ---- interne Helfer ----------------------------------------------------

    private static InstalledSteamGame? TryParseManifest(string manifestPath, string libraryRoot)
    {
        try
        {
            var content = File.ReadAllText(manifestPath);
            string? appId = MatchAcfValue(content, "appid");
            string? name = MatchAcfValue(content, "name");
            string? installDir = MatchAcfValue(content, "installdir");
            if (appId is null || name is null || installDir is null)
            {
                Log.Debug("Manifest unvollständig: {Path}", manifestPath);
                return null;
            }
            if (!int.TryParse(appId, out var appIdInt)) return null;

            var fullInstallDir = Path.Combine(libraryRoot, "steamapps", "common", installDir);
            return new InstalledSteamGame(appIdInt, name, fullInstallDir, libraryRoot);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Manifest-Parse fehlgeschlagen: {Path}", manifestPath);
            return null;
        }
    }

    private static string? MatchAcfValue(string content, string key)
    {
        // ACF: "key"		"value" — Whitespace zwischen ist Tab, aber wir sind tolerant.
        var m = Regex.Match(content, "\"" + Regex.Escape(key) + "\"\\s+\"([^\"]*)\"",
            RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>Kandidatenliste — nicht dedupliziert, nicht auf Existenz geprüft.</summary>
    private static IEnumerable<string> EnumerateLibraryRootCandidates()
    {
        if (OperatingSystem.IsWindows())
        {
            // Standard-Pfad: C:\Program Files (x86)\Steam. Registry-Read wäre
            // sauberer, aber der Standard-Pfad deckt >95% ab und braucht keinen
            // Windows-only-Code-Pfad im Contract.
            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrEmpty(pf86))
            {
                var steam = Path.Combine(pf86, "Steam");
                yield return steam;
                foreach (var extra in ParseLibraryFolders(steam)) yield return extra;
            }
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrEmpty(pf))
                yield return Path.Combine(pf, "Steam");
            yield break;
        }

        // Linux
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var homeRoots = new List<string>();
        if (!string.IsNullOrEmpty(home))
        {
            homeRoots.Add(Path.Combine(home, ".steam", "steam"));
            homeRoots.Add(Path.Combine(home, ".steam", "root"));
            homeRoots.Add(Path.Combine(home, ".local", "share", "Steam"));
            homeRoots.Add(Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", "data", "Steam"));
        }
        foreach (var r in homeRoots) yield return r;

        foreach (var homeRoot in homeRoots)
            foreach (var extra in ParseLibraryFolders(homeRoot))
                yield return extra;

        foreach (var extra in ScanMountPointsForSteamLibraries())
            yield return extra;
    }

    /// <summary>Extrahiert alle <c>path</c>-Einträge aus <c>libraryfolders.vdf</c>.
    /// Bazzite/Fedora-Atomic-Falle: der VDF-Pfad ist oft <c>/var/home/user/…</c>
    /// (Symlink von <c>/home/user/…</c>) — beide Varianten zurückgeben.</summary>
    public static IEnumerable<string> ParseLibraryFolders(string steamRoot)
    {
        var candidates = new[]
        {
            Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"),
            Path.Combine(steamRoot, "config", "libraryfolders.vdf"),
        };

        foreach (var vdf in candidates)
        {
            if (!File.Exists(vdf)) continue;
            string content;
            try { content = File.ReadAllText(vdf); }
            catch (Exception ex)
            {
                Log.Debug(ex, "libraryfolders.vdf nicht lesbar: {Path}", vdf);
                continue;
            }

            foreach (Match m in Regex.Matches(content, "\"path\"\\s+\"([^\"]+)\"",
                         RegexOptions.IgnoreCase))
            {
                var path = m.Groups[1].Value.Replace(@"\\", @"\");
                yield return path;
                if (path.StartsWith("/var/home/", StringComparison.Ordinal))
                    yield return "/home/" + path["/var/home/".Length..];
            }
        }
    }

    private static IEnumerable<string> ScanMountPointsForSteamLibraries()
    {
        var mountRoots = new[] { "/run/media", "/mnt", "/media" };
        foreach (var mountRoot in mountRoots)
        {
            if (!Directory.Exists(mountRoot)) continue;
            IEnumerable<string> userDirs;
            try { userDirs = Directory.EnumerateDirectories(mountRoot); }
            catch { continue; }

            foreach (var userDir in userDirs)
            {
                IEnumerable<string> driveDirs;
                try { driveDirs = Directory.EnumerateDirectories(userDir); }
                catch { continue; }

                foreach (var driveDir in driveDirs)
                {
                    if (Directory.Exists(Path.Combine(driveDir, "steamapps")))
                        yield return driveDir;
                    IEnumerable<string> subDirs;
                    try { subDirs = Directory.EnumerateDirectories(driveDir); }
                    catch { continue; }
                    foreach (var sub in subDirs)
                        if (Directory.Exists(Path.Combine(sub, "steamapps")))
                            yield return sub;
                }
            }
        }
    }
}

/// <summary>Ein von Steam verwaltetes, installiertes Spiel.</summary>
/// <param name="AppId">Steam-App-ID.</param>
/// <param name="Name">Displayname aus <c>appmanifest_*.acf</c>.</param>
/// <param name="InstallDir">Absoluter Pfad zum Install-Verzeichnis.</param>
/// <param name="LibraryRoot">Library-Root, unter dem das Spiel liegt (für
/// Proton-Präfix-Auflösung nützlich).</param>
public sealed record InstalledSteamGame(int AppId, string Name, string InstallDir, string LibraryRoot);
