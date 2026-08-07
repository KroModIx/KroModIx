# Kroste ModManager

## Grundlagen

- **Was:** Übergeordneter Mod-Manager für Steam-Spiele unter Windows + Linux. Discovert installierte Spiele, lädt pro Spiel ein Game-Plugin (LS25, Icarus, Satisfactory, …) das die Mod-Verwaltung für dieses Spiel übernimmt.
- **Stack:** C# / .NET 10 / Avalonia 12.1, CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection, NLog (mit Secret-Masking), xunit.v3 + FluentAssertions 7.x
- **Struktur:** Flach (kein `src/`). Drei Projekte: `ModManager/` (Host-App), `ModManager.PluginContracts/` (NuGet-Package auf GitHub Packages — von Plugin-Repos via PackageReference konsumiert), `ModManager.Tests/`.
- **Konvention:** `.slnx`, Central Package Management (`Directory.Packages.props`), `TreatWarningsAsErrors`, MinVer (Tags `v*`), Kroste-Standard-Design (dunkles Theme + Kroste-Palette + ChromeWindow-Basis + Tray + AboutWindow), Localization EN+DE (Live-Umschaltung).
- **Repo:** `github.com/Kroste/Mod-Manager` (Slug mit Bindestrich). Assembly/Namespace ohne Bindestrich: `ModManager`.
- **Kommunikation:** Deutsch, „du". Lars entwirft, Claude implementiert.

## Aktueller Stand

**M2 (Steam-Discovery + Sidebar + Plugin-Loading) — abgeschlossen mit v0.2.0:**
- `ModManager.PluginContracts` inhaltlich gefüllt (flacher Namespace, C# 12 Records + Interfaces): `PluginManifest` (Schema 1, System.Text.Json), `PluginMetadata`, `PluginUpdateSource`, `GameTarget`, `DetectedGame`, `Platforms`, `RuntimeKind`, `GameSource`; `IGameModPlugin`, `IGameTabContribution`; Host-Contracts `IHostServices`, `ISecretProtection`, `IDialogService`, `INotificationSink`, `IProgressScope`, `ILocalization`, `IHostShell`. Wird beim Host-Release als NuGet-Package auf GitHub Packages veröffentlicht.
- Steam-Discovery: `SteamLibraryService` (extrahiert aus LS-ModManager `ModPathService`, generisiert — enumeriert Library-Roots, parst `appmanifest_*.acf`, findet Proton-Präfixe, deduped nach AppId gegen Bazzite-`/var/home`-Symlink-Duplikate). Auf Lars' Bazzite-System: 32 Steam-Spiele in 6 Library-Roots (Home + 2 externe Platten mit Symlink-Split).
- `GameCoverService`: 4-stufige Cover-Auflösung Steam-Cache → Steam-CDN → User-Custom → null, Cache in `~/.cache/ModManager/game-covers/`.
- `ManualGamesService`: JSON-Persistenz atomar in `~/.config/ModManager/manual-games.json` mit `.broken`-Backup.
- `GameDiscoveryService`: verheiratet Steam + Manual, dedupliziert auf identische SteamAppId (Steam gewinnt, Manual-Eintrag bleibt persistiert aber nicht doppelt sichtbar).
- Plugin-Loader-Kette: `PluginRegistryScanner` (findet plugin.json OHNE Assembly-Load), `PluginActivationPlanner` (matcht SteamAppIds + `AlwaysActivePluginIds`, prüft `MinHostVersion`, löst Konflikte via höhere SemVer), `PluginActivator` (`Assembly.LoadFrom` ohne LoadContext — Checkmk-Muster; unterstützt Runtime-Aktivierung für M4).
- Host-Impls der Plugin-Contracts: `HostServicesImpl` (plugin-scoped Logger/PluginDataDir/PluginCacheDir + Proxy-aware HttpClient-Factory), `HostShellImpl`, `LocalizationBridge`, `DialogServiceImpl` (StorageProvider für File/Folder-Picker + eigener Confirm-Dialog), `NotificationSinkImpl`, `StatusProgressCoordinator`.
- MainWindow-Sidebar mit **großen Cover-Kacheln** (176×238, Portrait 600×900 Steam-Library-Look), Suchfeld, „nur mit Plugin"-Toggle, „➕ Spiel hinzufügen"-Button, Sortierung Plugin-Spiele zuerst + alphabetisch, gefüllter goldener ★ bei Spielen mit geladenem Plugin. Content-Bereich mit TabControl für Plugin-Tabs oder Placeholder.
- `AddGameDialog`: Formular Name/Verzeichnis/Executable/Cover/optional SteamAppId, File-Picker für alle drei Pfade.
- `LastSelectedGameId` in Settings persistiert und beim Start wiederhergestellt.
- Release-Workflow um `publish-contracts`-Job erweitert: `dotnet pack` + `dotnet nuget push` auf `https://nuget.pkg.github.com/Kroste/index.json` mit `GITHUB_TOKEN` (packages:write).
- Tests: 16 grün — `AppPathsTests`, `SecretProtectionTests`, `PluginManifestTests`, `PluginActivationPlannerTests`, `ManualGamesServiceTests`.

**Begleit-Repo `Kroste/ModManager.Plugins.Dummy` (v0.1.0):** minimales Plugin als End-to-End-Beweis. Zwei Tabs („Hello", „Info") mit Kontext-Info + drei Test-Aktionen (Log, Notification, HTTP-Probe). Targets: CS2 (730), TF2 (440), Proton Experimental (1493710) als Linux-Dev-Fallback. Referenziert `Kroste.ModManager.PluginContracts` als PackageReference aus GitHub Packages, nuget.config mit Package-Source-Mapping (Pflicht bei CPM+multi-source).

## Roadmap

- **M3 — LS25-Plugin**: eigenes Repo `Kroste/ModManager.Plugins.LS25`, Kern-Logik aus LS-ModManager extrahiert (ModDescReader, ModInstallService, ModHubService, HofHirschfeldCatalogService, ModhosterCatalogService, CatalogCache, DdsToPngConverter, ModBackupService). Tab-Contributions: „Installiert", „ModHub", „Hof Hirschfeld", „modhoster", „Downloads".

- **M4 — PluginIndex + Install-Karte + Auto-Update**: Klick auf Spiel mit umrandetem Stern → Plugin herunterladen und **live aktivieren ohne Prozess-Restart** (`PluginActivator.ActivateAsync`). Restart-Hint nur bei Updates schon geladener Plugins.

- **M5** Icarus, **M6** Satisfactory (Bottles/Wine-Sonderweg), **M7** Ren'Py (optional).

## Referenz

- **Namensschema:** Repo-Slug `Mod-Manager` (mit Bindestrich, historisch existierender Ordnername). C#-Assembly/Namespace `ModManager` (ohne Bindestrich). Release-Asset-Prefix `ModManager` (in ZIP/tar.gz/AppImage-Dateinamen). URLs im HostUpdateService/AboutWindow zeigen auf `Kroste/Mod-Manager`.
- **Zwei-stufige Plugin-Discovery (kommt M2):** (1) `PluginRegistryScanner` liest alle `plugin.json` OHNE Assembly-Load (spart Startup-Zeit + RAM bei installierten aber ungenutzten Plugins). (2) `PluginActivator` lädt DLL erst, wenn Ziel-Spiel installiert ODER User „immer laden" markiert hat. (3) Neu installiertes Plugin wird via `ActivateAsync` live in den Prozess gebracht — kein Restart. (4) **Update** eines schon geladenen Plugins braucht weiter Restart (Assembly-DLL im Prozess gehalten, kein LoadContext).
- **Kein `AssemblyLoadContext`** für Plugin-Isolation. Grund: alle Kroste-Plugins teilen sich Contracts + Avalonia, Plugin-Views werden ins MainWindow eingebettet → im isolierten LoadContext gibt es InvalidCastException-Kaskaden bei Avalonia-Style-Resources und `TopLevel.GetTopLevel`. RenPack hat einen LoadContext, Checkmk Cockpit hat ihn wieder rausgeworfen — wir folgen Checkmk.
- **Mods = Content-Payloads.** `mod.json` + ZIP im GitHub-Release-Asset. Installer-Logik lebt im Game-Plugin (Plugin IST der Modmanager für sein Spiel). Kein DLL-Overhead pro Mod. Optionaler `installHookAssembly`-Slot in `mod.json` erst, wenn ein realer Sonderfall auftaucht.
- **PluginContracts als NuGet-Package** (nicht ProjectReference). Wird bei jedem Host-Release auf GitHub Packages veröffentlicht. Plugin-Repos referenzieren via `PackageReference` und pinnen `MinHostVersion` per SemVer.
- **Sidebar-Stern-Semantik** (kommt M2/M4): **gefüllt gold** = Plugin lokal installiert und aktiv · **umrandet gold** = Plugin verfügbar im PluginIndex, noch nicht installiert · **kein Stern** = kein Plugin bekannt. Sortierung: Plugin-Spiele zuerst.
- **Steam-Cover-Bilder** (M2): 4-stufige Priorität `~/.local/share/Steam/appcache/librarycache/<appid>_library_600x900.jpg` → `https://cdn.cloudflare.steamstatic.com/steam/apps/<appid>/library_600x900.jpg` (Cache in `~/.cache/ModManager/game-covers/`) → User-Custom → Platzhalter.
- **LS-ModManager und RenPack bleiben standalone.** Kein Breaking Change dort. ModManager läuft parallel, das LS25-Plugin (M3) extrahiert LS-ModManager-Kern-Logik. Später kann LS-ModManager in den Wartungsmodus.
- **Release-Slug in HostUpdateService:** die Exe-/Executable-Namen im publish sind `ModManager` (Windows: `ModManager.exe`, Linux: `ModManager`). Das steht auch im Batch/Bash-Skript des Self-Updates hart drin — bei Umbenennung mitziehen.
- **App-Icon Regen:** `python3 scripts/build_icon.py` — schreibt `ModManager/Assets/mod-manager.png` (256×256) + `.ico` (Multi-Res).
- **Planungs-Dokument:** `/home/OsteL/.claude/plans/ultrathink-through-this-step-hidden-lampson.md` — das komplette Architektur-Dokument mit allen Milestones und Design-Entscheidungen.
