# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Grundlagen

- **Was:** Übergeordneter Mod-Manager für Steam-Spiele unter Windows + Linux. Discovert installierte Spiele, lädt pro Spiel ein Game-Plugin (LS25, Icarus, Satisfactory, Cyberpunk 2077, Ren'Py, …), das die Mod-Verwaltung für dieses Spiel übernimmt.
- **Stack:** C# / .NET 10 / Avalonia 12, CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection, NLog (mit Secret-Masking), xunit.v3 + FluentAssertions.
- **Struktur:** Flach, kein `src/`. Drei Projekte — `KroModIx/` (Host-App), `KroModIx.Plugin.Contracts/` (NuGet-Package auf GitHub Packages, von den Plugin-Repos via `PackageReference` konsumiert), `KroModIx.Tests/`.
- **Konventionen:** `.slnx` statt `.sln`, Central Package Management (`Directory.Packages.props` — Versionen **nie** im csproj), `TreatWarningsAsErrors`, `Nullable enable`, MinVer (Version kommt aus Git-Tags `v*`, nicht aus dem csproj).
- **Repo:** `github.com/KroModIx/KroModIx`. Assembly, RootNamespace und Release-Asset-Prefix sind einheitlich `KroModIx`.
- **Kommunikation:** Deutsch, „du". Lars entwirft, Claude implementiert.

## Befehle

```bash
dotnet build                  # Build (TreatWarningsAsErrors — Warnungen brechen den Build)
dotnet test                   # komplette Suite
dotnet run --project KroModIx # App starten
python3 scripts/build_icon.py # App-Icon regenerieren -> KroModIx/Assets/kromodix.png + .ico
```

Einzelner Test bzw. eine Testklasse:

```bash
dotnet test --filter "FullyQualifiedName~PluginIndexMatcherTests"
dotnet test --filter "FullyQualifiedName~PluginIndexMatcherTests.MatchesByEngineSlug"
```

Release: `scripts/release.sh` (bzw. `release.ps1`) fragt die neue Version ab, prüft auf saubere Working-Copy und ungepushte Commits, setzt den annotierten Tag `vX.Y.Z` und pusht ihn. Der Push löst `release.yml` aus: Windows-ZIP, Linux-tar.gz, AppImage und `dotnet nuget push` der Contracts auf GitHub Packages. Also **nie** von Hand taggen — das Skript macht die Vorprüfungen.

## Architektur

**Plugin-Lebenszyklus, drei Stufen.** Die Trennung ist bewusst und spart Startup-Zeit sowie RAM bei installierten, aber ungenutzten Plugins:

1. `PluginRegistryScanner` liest alle `plugin.json` **ohne** Assembly-Load.
2. `PluginActivationPlanner` matcht gegen erkannte Spiele, prüft `MinHostVersion` und löst Mehrfach-Treffer über die höhere SemVer auf.
3. `PluginActivator` lädt die DLL per `Assembly.LoadFrom` — auch zur Laufzeit, ohne App-Neustart.

**Kein `AssemblyLoadContext`.** Alle Plugins teilen sich Contracts und Avalonia, ihre Views werden ins MainWindow eingebettet. In einem isolierten LoadContext gibt das InvalidCastException-Kaskaden bei Avalonia-Style-Resources und `TopLevel.GetTopLevel`. Diese Entscheidung nicht ohne Not umdrehen.

**Spiel → Plugin-Matching** läuft ausschließlich über `PluginIndexMatcher`: SteamAppId **oder** Engine-Slug, case-insensitive. Manuell hinzugefügte Spiele haben keine AppId — wer nur auf `SteamAppId` matcht, verliert sie stillschweigend. `FindIndexEntryFor`, `CategoriesForGame` und der `PluginState.Available`-Sweep gehen alle über diese eine Stelle.

**Host-Services** liegen thematisch gruppiert unter `KroModIx/Services/`: `Plugins/`, `Games/`, `Steam/`, `Nexus/`, `Backup/`, `Conflicts/`, `Images/`, `Api/`, `Ai/`, `Storage/`, `Text/`, `HostProfile/`.

**Eingebaute REST-API** über Kestrel + Minimal-API. Bewusst als `FrameworkReference Microsoft.AspNetCore.App` eingebunden, nicht als PackageReference — das Runtime-Bundle steckt im .NET-10-SDK. Separate `Microsoft.Extensions.DependencyInjection`- oder `.Http`-PackageReferences lösen deshalb NU1510-Fehler aus; sie kommen über die FrameworkReference mit.

**Mods sind Content-Payloads**, keine Assemblies: `mod.json` + ZIP als GitHub-Release-Asset. Die Installer-Logik lebt im Game-Plugin — das Plugin *ist* der Modmanager für sein Spiel.

## Invarianten

- **Contracts nur additiv erweitern.** Plugin-Repos konsumieren sie als NuGet-Package und pinnen `MinHostVersion`. Ein Breaking Change zwingt jedes Plugin zum Nachziehen.
- **Konflikt-Scan ist Pull, nicht Push.** Plugins liefern über `IConflictSource.GetOwnedFilesAsync` on demand, der Host cached nichts. Ein Plugin ohne dieses Interface fehlt in der Konflikt-Liste — das ist gewollt, kein Cross-Cutting-Zwang.
- **Backups rollen nicht automatisch zurück.** Nach einem Snapshot kann der User bewusste Änderungen gemacht haben; ein blindes Rollback über die Snapshot-Grenze zerstört sie. Der Snapshot ist Sicherheitsnetz, die Auswahl trifft der User im UI.
- **Host-Release und PluginIndex-Push gehören zusammen.** Neue Matching-Felder (etwa `engines`) wirken erst, wenn `KroModIx.PluginIndex/plugins.json` sie ebenfalls enthält.
- **Auto-Install bleibt konservativ:** nur Plugins zu tatsächlich vorhandenen Spielen, nie auf Vorrat. Eine Deinstallation landet in `AppSettings.AutoInstallOptOutPluginIds`, sonst wäre sie beim nächsten Start wirkungslos.
- **Self-Update kennt Dateinamen.** Die Executable heißt `KroModIx` bzw. `KroModIx.exe`; das steht in den Batch-/Bash-Skripten des Self-Updates fest verdrahtet. Bei einer Umbenennung mitziehen.

## Referenz

- **Sidebar-Stern:** gefüllt gold = Plugin lokal installiert und aktiv · umrandet gold = im PluginIndex verfügbar, noch nicht installiert · kein Stern = kein Plugin bekannt. Plugin-Spiele werden zuerst sortiert.
- **Steam-Cover**, vierstufig: Steam-Cache → Steam-CDN → User-Custom → Platzhalter, gecacht unter `~/.cache/KroModIx/game-covers/`.
- **Pfade:** XDG-konform. Config in `~/.config/KroModIx/` (inkl. `plugins/`, `manual-games.json`), State in `~/.local/share/KroModIx/` (inkl. `backups/`), Cache in `~/.cache/KroModIx/`. Zentral in `AppPaths`; Tests isolieren über `XDG_CONFIG_HOME` / `XDG_STATE_HOME` auf ein TempDir.
- **Logs** unter `logs/` neben der Executable, Tagesarchive über 14 Tage, Passwörter und Tokens automatisch maskiert.
- **Nachbar-Repos:** `KroModIx.PluginIndex` (Katalog `plugins.json`), `KroModIx.Plugin.*` (ein Repo je Spiel), `KroModIx.Plugin.Dummy` (Referenz-Skelett).
- **Historie:** `docs/Historie.md` — Release-Journal seit M2 und die Plugin-Roadmap. Dort nachschlagen, wenn die Begründung hinter einer älteren Entscheidung fehlt.
