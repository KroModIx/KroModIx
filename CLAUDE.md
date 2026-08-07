# Kroste ModManager

## Grundlagen

- **Was:** Übergeordneter Mod-Manager für Steam-Spiele unter Windows + Linux. Discovert installierte Spiele, lädt pro Spiel ein Game-Plugin (LS25, Icarus, Satisfactory, …) das die Mod-Verwaltung für dieses Spiel übernimmt.
- **Stack:** C# / .NET 10 / Avalonia 12.1, CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection, NLog (mit Secret-Masking), xunit.v3 + FluentAssertions 7.x
- **Struktur:** Flach (kein `src/`). Drei Projekte: `ModManager/` (Host-App), `ModManager.PluginContracts/` (NuGet-Package auf GitHub Packages — von Plugin-Repos via PackageReference konsumiert), `ModManager.Tests/`.
- **Konvention:** `.slnx`, Central Package Management (`Directory.Packages.props`), `TreatWarningsAsErrors`, MinVer (Tags `v*`), Kroste-Standard-Design (dunkles Theme + Kroste-Palette + ChromeWindow-Basis + Tray + AboutWindow), Localization EN+DE (Live-Umschaltung).
- **Repo:** `github.com/Kroste/Mod-Manager` (Slug mit Bindestrich). Assembly/Namespace ohne Bindestrich: `ModManager`.
- **Kommunikation:** Deutsch, „du". Lars entwirft, Claude implementiert.

## Aktueller Stand

**M1 (Fundament) — abgeschlossen mit v0.1.0:**
- Repo-Grundgerüst: `.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.gitignore`, `LICENSE` (MIT), `FUNDING.yml` (BMC-Handle `kroste`).
- Drei Projekte angelegt: `ModManager/`, `ModManager.PluginContracts/` (Placeholder — Inhalte kommen in M2), `ModManager.Tests/`.
- Utility-Services aus LS-ModManager/RenPack übernommen und generalisiert: `AppPaths` (Config/Cache/State-Roots, User- und Bundled-Plugin-Dirs), `AppSettingsService` (atomar tmp+move, `.broken`-Backup), `SecretProtection` (DPAPI/AES), `LocalizationService` + `LocalizedString` + `TrExtension` + `L`-Helper (EN + DE-resx), `MaskingLayoutRenderer` (Secrets-Maskierung fürs Log).
- Avalonia-Shell: `App.axaml` mit Kroste-Palette und vollem Style-Set, `Program.cs` (MaskingLayoutRenderer registriert VOR erstem Log-Aufruf), `ChromeWindow`-Basis, `TitleBar`-UserControl, `MainWindow` (Header + Sidebar-Placeholder + Content-Placeholder + Statusbar), `SettingsWindow` (Language-Selector mit Emoji-Flaggen und Live-Wechsel), `AboutWindow` (Version + Update-Check + GitHub + BMC + Log-Ordner öffnen), `TrayController` (Minimize→Hide, GC-Referenz in `App`), `GlobalExceptionHandler`, `HostUpdateService` (Self-Update Win-ZIP / Linux-AppImage / Linux-tar.gz, Repo `Kroste/Mod-Manager`).
- App-Icon: Puzzleteil-Motiv in Kroste-Gold auf dunklem Grund, `scripts/build_icon.py` (Pillow) erzeugt PNG + Multi-Res-ICO.
- GitHub Actions: `ci.yml` (build+test bei Push/PR), `release.yml` (Win-ZIP + linux-x64 tar.gz + AppImage bei Tag `v*`), `dependabot.yml` (FluentAssertions <8 gepinnt).
- Packaging: `packaging/linux/` (AppRun, build-appimage.sh, mod-manager.desktop). VSCode: `launch.json` + `tasks.json` (build/test/watch/clean/clean-hard/publish/release-tag+push).
- Tests: `AppPathsTests`, `SecretProtectionTests` (Roundtrip, Null-Handling, Format-Validierung).

## Roadmap

- **M2 — Steam-Discovery + Sidebar + Plugin-Loading (erste Sichtbarkeit)**
  - `SteamLibraryService` extrahiert aus `LS-ModManager/LSModManager/Services/ModPathService.cs` (libraryfolders.vdf, appmanifest_*.acf, Bazzite-Mount-Points).
  - `GameCoverService` (Steam-Cache → Steam-CDN → Custom → Platzhalter, Cache in `~/.cache/ModManager/game-covers/`).
  - `ManualGamesService` (JSON in `~/.config/ModManager/manual-games.json`, atomar).
  - Sidebar mit großen Cover-Kacheln + Suchfeld + „nur mit Plugin"-Toggle + „➕ Spiel hinzufügen".
  - `AddGameDialog` (Name/Verzeichnis/Executable/Cover/optional SteamAppId).
  - `ModManager.PluginContracts` mit `IGameModPlugin`, `IHostServices`, `IGameTabContribution`, `PluginManifest`, `DetectedGame`, `GameTarget`, etc.
  - NuGet-Publishing der PluginContracts als `Kroste.ModManager.PluginContracts.<version>.nupkg` auf GitHub Packages (Release-Workflow erweitern).
  - `PluginRegistryScanner` (liest `plugin.json` OHNE Assembly-Load), `PluginActivationPlanner` (matcht SteamAppIds), `PluginActivator` (Assembly.LoadFrom, kein LoadContext, unterstützt sowohl Startup-Aktivierung als auch Runtime-Aktivierung für M4).
  - Zweites Repo `Kroste/ModManager.Plugins.Dummy` als End-to-End-Proof.

- **M3 — LS25-Plugin**: eigenes Repo `Kroste/ModManager.Plugins.LS25`, Kern-Logik aus LS-ModManager extrahiert.

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
