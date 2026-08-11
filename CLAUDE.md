# KroModIx

## Grundlagen

- **Was:** Übergeordneter Mod-Manager für Steam-Spiele unter Windows + Linux. Discovert installierte Spiele, lädt pro Spiel ein Game-Plugin (LS25, Icarus, Satisfactory, …) das die Mod-Verwaltung für dieses Spiel übernimmt.
- **Stack:** C# / .NET 10 / Avalonia 12.1, CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection, NLog (mit Secret-Masking), xunit.v3 + FluentAssertions 7.x
- **Struktur:** Flach (kein `src/`). Drei Projekte: `ModManager/` (Host-App), `KroModIx.Plugin.Contracts/` (NuGet-Package auf GitHub Packages — von Plugin-Repos via PackageReference konsumiert), `ModManager.Tests/`.
- **Konvention:** `.slnx`, Central Package Management (`Directory.Packages.props`), `TreatWarningsAsErrors`, MinVer (Tags `v*`), Kroste-Standard-Design (dunkles Theme + Kroste-Palette + ChromeWindow-Basis + Tray + AboutWindow), Localization EN+DE (Live-Umschaltung).
- **Repo:** `github.com/KroModIx/KroModIx` (Slug mit Bindestrich). Assembly/Namespace ohne Bindestrich: `ModManager`.
- **Kommunikation:** Deutsch, „du". Lars entwirft, Claude implementiert.

## Aktueller Stand

**M3–M5 (Live-Install + LS25 + Icarus) — abgeschlossen mit v0.3.0:**
- **Steam-Tools-Filter** (`SteamGameFilter`): Steam Linux Runtimes, alle Proton-Versionen 4.11–10.0, Steamworks Common Redistributables werden aus der Sidebar ausgeblendet. Bekannte AppIds als Blacklist + Namens-Präfixe (`Steam Linux Runtime`, `Proton `, `Steamworks Common Redistributables`) für neue/unbekannte Versionen. Auf Lars' Bazzite: 32 Manifeste → 8 Tools weg → 24 echte Spiele.
- **PluginIndex-Anbindung** (M4): Meta-Repo `KroModIx/KroModIx.PluginIndex` mit `plugins.json`; `PluginIndexService` lädt es beim App-Start (24 h Cache, Stale-Fallback bei Netzfehler). Matcht `steamAppIds` gegen installierte Spiele → **umrandeter goldener Stern** (`PluginState.Available`) auf der Sidebar-Kachel.
- **Install-Karte** (M4): Klick auf Kachel mit umrandetem Stern → Content-Bereich zeigt Karte „Plugin verfügbar. ⬇ Installieren". `PluginInstaller` löst das neueste GitHub-Release-Asset (ZIP), entpackt es nach `~/.config/KroModIx/plugins/<id>/`, `PluginActivator.ActivateOneAsync` bringt es **live in den laufenden Prozess** (kein App-Restart), Stern wechselt sofort zu gefüllt, TabHost rendert die Plugin-Tabs.
- **Erste zwei echte Game-Plugins** (M3 + M5):
  - **`KroModIx/KroModIx.Plugin.LS25` v0.1.0**: Extraktion des LS-ModManager-Kerns (ModDescReader für modDesc.xml, ModInstallService mit ZIP-Install/Enable/Disable/Uninstall via `.zip.disabled`-Rename). `Ls25PathResolver` nutzt vom Host geliefertes `DetectedGame.UserDataDir` (Proton-Docs auf Linux, Documents/My Games auf Windows). Tab „Installiert" mit Toolbar (Install-ZIP, Refresh, Ordner öffnen, Toggle, Uninstall mit Confirm). Katalog/KI/Backup in v0.2–v0.5 der Plugin-Roadmap.
  - **`KroModIx/KroModIx.Plugin.Icarus` v0.1.0**: PAK-Mod-Verwaltung für Icarus (Unreal Engine). `IcarusPathResolver` baut `<InstallDir>/Icarus/Content/Paks/~mods/`. Tab „Installiert" analog LS25 mit .pak/.pak.disabled-Toggle.
- Tests: 25 grün (+9 SteamGameFilterTests seit v0.2.0).

**M2 (Steam-Discovery + Sidebar + Plugin-Loading) — abgeschlossen mit v0.2.0:**
- `KroModIx.Plugin.Contracts` inhaltlich gefüllt (flacher Namespace, C# 12 Records + Interfaces): `PluginManifest` (Schema 1, System.Text.Json), `PluginMetadata`, `PluginUpdateSource`, `GameTarget`, `DetectedGame`, `Platforms`, `RuntimeKind`, `GameSource`; `IGameModPlugin`, `IGameTabContribution`; Host-Contracts `IHostServices`, `ISecretProtection`, `IDialogService`, `INotificationSink`, `IProgressScope`, `ILocalization`, `IHostShell`. Wird beim Host-Release als NuGet-Package auf GitHub Packages veröffentlicht.
- Steam-Discovery: `SteamLibraryService` (extrahiert aus LS-ModManager `ModPathService`, generisiert — enumeriert Library-Roots, parst `appmanifest_*.acf`, findet Proton-Präfixe, deduped nach AppId gegen Bazzite-`/var/home`-Symlink-Duplikate). Auf Lars' Bazzite-System: 32 Steam-Spiele in 6 Library-Roots (Home + 2 externe Platten mit Symlink-Split).
- `GameCoverService`: 4-stufige Cover-Auflösung Steam-Cache → Steam-CDN → User-Custom → null, Cache in `~/.cache/KroModIx/game-covers/`.
- `ManualGamesService`: JSON-Persistenz atomar in `~/.config/KroModIx/manual-games.json` mit `.broken`-Backup.
- `GameDiscoveryService`: verheiratet Steam + Manual, dedupliziert auf identische SteamAppId (Steam gewinnt, Manual-Eintrag bleibt persistiert aber nicht doppelt sichtbar).
- Plugin-Loader-Kette: `PluginRegistryScanner` (findet plugin.json OHNE Assembly-Load), `PluginActivationPlanner` (matcht SteamAppIds + `AlwaysActivePluginIds`, prüft `MinHostVersion`, löst Konflikte via höhere SemVer), `PluginActivator` (`Assembly.LoadFrom` ohne LoadContext — Checkmk-Muster; unterstützt Runtime-Aktivierung für M4).
- Host-Impls der Plugin-Contracts: `HostServicesImpl` (plugin-scoped Logger/PluginDataDir/PluginCacheDir + Proxy-aware HttpClient-Factory), `HostShellImpl`, `LocalizationBridge`, `DialogServiceImpl` (StorageProvider für File/Folder-Picker + eigener Confirm-Dialog), `NotificationSinkImpl`, `StatusProgressCoordinator`.
- MainWindow-Sidebar mit **großen Cover-Kacheln** (176×238, Portrait 600×900 Steam-Library-Look), Suchfeld, „nur mit Plugin"-Toggle, „➕ Spiel hinzufügen"-Button, Sortierung Plugin-Spiele zuerst + alphabetisch, gefüllter goldener ★ bei Spielen mit geladenem Plugin. Content-Bereich mit TabControl für Plugin-Tabs oder Placeholder.
- `AddGameDialog`: Formular Name/Verzeichnis/Executable/Cover/optional SteamAppId, File-Picker für alle drei Pfade.
- `LastSelectedGameId` in Settings persistiert und beim Start wiederhergestellt.
- Release-Workflow um `publish-contracts`-Job erweitert: `dotnet pack` + `dotnet nuget push` auf `https://nuget.pkg.github.com/KroModIx/index.json` mit `GITHUB_TOKEN` (packages:write).
- Tests: 16 grün — `AppPathsTests`, `SecretProtectionTests`, `PluginManifestTests`, `PluginActivationPlannerTests`, `ManualGamesServiceTests`.

**Begleit-Repo `KroModIx/KroModIx.Plugin.Dummy` (v0.1.0):** minimales Plugin als End-to-End-Beweis. Zwei Tabs („Hello", „Info") mit Kontext-Info + drei Test-Aktionen (Log, Notification, HTTP-Probe). Targets: CS2 (730), TF2 (440), Proton Experimental (1493710) als Linux-Dev-Fallback. Referenziert `KroModIx.Plugin.Contracts` als PackageReference aus GitHub Packages, nuget.config mit Package-Source-Mapping (Pflicht bei CPM+multi-source).

**M4.5 (PluginUpdateService) — abgeschlossen mit v0.4.0:**
- `PluginUpdateService`: prüft pro geladenem Plugin die aktuelle Version im `updateSource`-Repo auf GitHub. Async-Check läuft im Hintergrund am App-Start; `UpdatesChanged`-Event synchronisiert das MainWindow-Badge.
- Update-Badge im MainWindow-Header (nur sichtbar wenn Updates > 0, zeigt „↑ N", Kroste-Akzentfarbe). Klick öffnet `PluginUpdatesWindow` mit Liste (Alte→Neue Version, ⬇ Installieren pro Zeile, „Jetzt prüfen"-Button).
- Install-Flow: Download ZIP → Sibling-Staging-Ordner → File.Copy overwrite ins Plugin-Verzeichnis. Auf Windows: geladene DLL lockt sich; Fallback legt `.dll.new` daneben. Nach Install: Restart-Hint-Banner in gelb-rot im Fenster („⚠ Bitte die App neu starten…").
- End-to-End verifiziert mit lokal auf 0.1.0 gepinnt Dummy-Plugin vs. GitHub v0.1.1 → Update-Check meldet 1 verfügbares Update.

**M5–M6 (Icarus + Satisfactory) — abgeschlossen mit v1.5.x:**
- **`KroModIx/KroModIx.Plugin.Icarus`**: Nexus-Katalog, PAK-Manual + Steam-Workshop, Premium-Direct-Download.
- **`KroModIx/KroModIx.Plugin.Satisfactory`**: ficsit.app GraphQL-Katalog, `.smod`-Direct-Download, `.uplugin`-Manifest-Parsing.

**M7 (Ren'Py-Plugin) — abgeschlossen mit v1.9.0 (Host) + v0.10.1 (Plugin):**
- **`KroModIx/KroModIx.Plugin.RenPyAssist`**: f95zone-Anbindung mit Login, Sub-Path-Rotation für Updates, Save-Kopie beim Update, ZIP-Archivierung + Auto-Cleanup alter Sub-Ordner, RPA-Archive-Browser + Save-Editor + Media-Preview mit **Inline-Video-Playback** (ffmpeg-MJPEG-Stream, portiert aus RenPack — kein LibVLC), KrosteMod-Pipeline (Walkthrough/Cheat/Rename/Translate), KI-Übersetzung der Beschreibungen, Container-Ordner-Rename mit Host-Integration.
- **Host-Erweiterungen für RenPyAssist:**
  - v1.9.0: `GameTarget.Engine` als init-Property + engine-basiertes Matching für Manual-Games ohne SteamAppId
  - v1.9.3: `IHostServices.TrySetManualGameCover` — Plugin propagiert Cover an Host-Sidebar
  - v1.9.4: `PluginUpdateService`-Race-Guard (Semaphore) gegen Update-Row-Duplikate
  - v1.9.5: `TrySetManualGameCover`-Event immer feuern (Cover-Refresh nach Re-Save mit gleichem Path)
  - v1.10.0: `GameUpdateInfo.InstallDir` — engine-Multi-Tile-Match für Sidebar-Update-Badge
  - v1.10.1: `IHostServices.RequestUpdateBadgeRefreshAsync` — Plugin triggert Sidebar-Refresh nach lokalen State-Changes
  - v1.10.2: **Plugin-Update-Cache** — persistenter JSON-Cache in `~/.config/KroModIx/plugin-update-cache.json` mit Rate-Limit-Fallback (GitHub 60 req/h ohne Token). Bei 403 wird der Cache als Fallback genutzt statt `_available` zu leeren. Optionaler `GITHUB_TOKEN`-Env-Var-Support (5000 req/h).
  - v1.10.3: `IHostServices.TryRenameManualGame` + `ManualGameRenamed`-Event — Plugin kann Container-Ordner umbenennen, Host re-keyed Sidebar-Kachel + Manual-Games-Store atomar und baut die Detail-View neu.

## Roadmap

Der Kern-Manager + alle geplanten Game-Plugins sind fertig. Offene Punkte
sind projektspezifische Ausbauten:

- **LS25-Plugin-Feature-Polish**: DDS-Preview auf Linux stabilisieren
  (SixLabors-Path), Bulk-Import aus Steam-Workshop.
- **Ren'Py-Plugin v0.11+**: KrosteMod-Pipeline erweitern
  (Choice-Auto-Expand, Konditional-Cheats), Screenshot-Timeline im
  Save-Editor, animierte Cover mit `AvaloniaGif` in der Detail-View
  (Sidebar-Kachel bleibt Standbild via ffmpeg-thumbnail).
- **PluginIndex-Kuration**: eigene Kategorien pro Genre, Suchfilter im
  PluginUpdates-Window.
- **Multi-Host-Support**: gleiche Plugin-Installation auf mehreren
  Rechnern synchron halten (rclone-Backend? git-Remote?). Niedrige Prio.

## Referenz

- **Namensschema:** Repo-Slug `Mod-Manager` (mit Bindestrich, historisch existierender Ordnername). C#-Assembly/Namespace `ModManager` (ohne Bindestrich). Release-Asset-Prefix `ModManager` (in ZIP/tar.gz/AppImage-Dateinamen). URLs im HostUpdateService/AboutWindow zeigen auf `KroModIx/KroModIx`.
- **Zwei-stufige Plugin-Discovery (kommt M2):** (1) `PluginRegistryScanner` liest alle `plugin.json` OHNE Assembly-Load (spart Startup-Zeit + RAM bei installierten aber ungenutzten Plugins). (2) `PluginActivator` lädt DLL erst, wenn Ziel-Spiel installiert ODER User „immer laden" markiert hat. (3) Neu installiertes Plugin wird via `ActivateAsync` live in den Prozess gebracht — kein Restart. (4) **Update** eines schon geladenen Plugins braucht weiter Restart (Assembly-DLL im Prozess gehalten, kein LoadContext).
- **Kein `AssemblyLoadContext`** für Plugin-Isolation. Grund: alle Kroste-Plugins teilen sich Contracts + Avalonia, Plugin-Views werden ins MainWindow eingebettet → im isolierten LoadContext gibt es InvalidCastException-Kaskaden bei Avalonia-Style-Resources und `TopLevel.GetTopLevel`. RenPack hat einen LoadContext, Checkmk Cockpit hat ihn wieder rausgeworfen — wir folgen Checkmk.
- **Mods = Content-Payloads.** `mod.json` + ZIP im GitHub-Release-Asset. Installer-Logik lebt im Game-Plugin (Plugin IST der Modmanager für sein Spiel). Kein DLL-Overhead pro Mod. Optionaler `installHookAssembly`-Slot in `mod.json` erst, wenn ein realer Sonderfall auftaucht.
- **PluginContracts als NuGet-Package** (nicht ProjectReference). Wird bei jedem Host-Release auf GitHub Packages veröffentlicht. Plugin-Repos referenzieren via `PackageReference` und pinnen `MinHostVersion` per SemVer.
- **Sidebar-Stern-Semantik** (kommt M2/M4): **gefüllt gold** = Plugin lokal installiert und aktiv · **umrandet gold** = Plugin verfügbar im PluginIndex, noch nicht installiert · **kein Stern** = kein Plugin bekannt. Sortierung: Plugin-Spiele zuerst.
- **Steam-Cover-Bilder** (M2): 4-stufige Priorität `~/.local/share/Steam/appcache/librarycache/<appid>_library_600x900.jpg` → `https://cdn.cloudflare.steamstatic.com/steam/apps/<appid>/library_600x900.jpg` (Cache in `~/.cache/KroModIx/game-covers/`) → User-Custom → Platzhalter.
- **LS-ModManager und RenPack bleiben standalone.** Kein Breaking Change dort. ModManager läuft parallel, das LS25-Plugin (M3) extrahiert LS-ModManager-Kern-Logik. Später kann LS-ModManager in den Wartungsmodus.
- **Release-Slug in HostUpdateService:** die Exe-/Executable-Namen im publish sind `ModManager` (Windows: `ModManager.exe`, Linux: `ModManager`). Das steht auch im Batch/Bash-Skript des Self-Updates hart drin — bei Umbenennung mitziehen.
- **App-Icon Regen:** `python3 scripts/build_icon.py` — schreibt `ModManager/Assets/kromodix.png` (256×256) + `.ico` (Multi-Res).
- **Planungs-Dokument:** `/home/OsteL/.claude/plans/ultrathink-through-this-step-hidden-lampson.md` — das komplette Architektur-Dokument mit allen Milestones und Design-Entscheidungen.
