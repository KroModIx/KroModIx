# KroModIx — Kroste Modding MatrIx

[![CI](https://github.com/KroModIx/KroModIx/actions/workflows/ci.yml/badge.svg)](https://github.com/KroModIx/KroModIx/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/KroModIx/KroModIx)](https://github.com/KroModIx/KroModIx/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

**Ein Mod-Manager für alle deine Steam-Spiele — plattformübergreifend
(Windows + Linux nativ, kein Wine), Steam-aware, per Plugin erweiterbar.**

Ein Fenster, links deine Spiele, rechts der spielspezifische Mod-Manager.
Kein Umschalten zwischen mehreren Tools mehr.

## Warum KroModIx?

Die meisten Mod-Manager sind Windows-only, closed-source oder brauchen Wine-
Bastelei auf Linux. Jedes Spiel verlangt sein eigenes Tool. KroModIx macht:

- **Steam-Library-Autodiscovery** (Windows + Linux/Bazzite, inkl. externe
  Platten via `libraryfolders.vdf`)
- **Nicht-Steam-Spiele manuell hinzufügen** — mit eigenem Cover, Executable-
  Pfad und optional Steam-AppId
- **Plugin pro Spiel** — jedes Plugin weiß wie es Mods für sein Spiel
  verwaltet (Katalog, Downloads, Install, Update-Discovery)
- **1-Klick-Plugin-Install** aus dem [PluginIndex](https://github.com/KroModIx/KroModIx.PluginIndex),
  live aktivierbar ohne App-Neustart
- **Automatische Plugin-Updates** aus GitHub-Releases (App-Neustart nötig
  weil die Assembly geladen ist)
- **Grüner ↑-Badge** auf einer Spielkachel wenn es Updates für installierte
  Mods oder neue Katalog-Einträge gibt
- **REST-API** für externe Steuerung (Screenshots, Clicks, Tab-Wechsel,
  Plugin-Actions — siehe unten)

## Installation

Aktuelles Release von der [Releases-Seite](https://github.com/KroModIx/KroModIx/releases):

**Windows:** `KroModIx-X.Y.Z-win-x64.zip` — entpacken, `KroModIx.exe`
starten. Self-contained, keine Installation nötig.

**Linux (AppImage, empfohlen):**
```bash
chmod +x KroModIx-*-x86_64.AppImage
./KroModIx-*-x86_64.AppImage
```

**Linux (tar.gz):** `KroModIx-X.Y.Z-linux-x64.tar.gz` entpacken, `./KroModIx`
starten.

## Bedienung

### Sidebar (links)

Zeigt alle installierten Steam-Spiele aus **allen** Library-Roots inkl.
externen Platten. Suchfeld filtert nach Titel, der „Alle Spiele"-Toggle
wechselt zwischen „nur mit Plugin" und „alle". Sortierung: Plugins zuerst,
dann alphabetisch. Steam-Tools (Runtimes, Proton-Versionen, Redistributables)
werden ausgeblendet.

**➕ Spiel hinzufügen** — für Nicht-Steam-Spiele: Name + Verzeichnis
(Pflicht) + Executable + Cover + optional Steam-AppId (lädt bei gesetzter
AppId das Cover automatisch aus dem Steam-CDN). Einträge persistieren in
`~/.config/KroModIx/manual-games.json`.

### Kachel-Overlays

- **★ (gefüllt gold)** = Plugin geladen und aktiv
- **☆ (umrandet gold)** = Plugin im PluginIndex verfügbar, noch nicht installiert
- **↑ (grün, oben rechts)** = Updates für installierte Mods oder neue
  Katalog-Einträge dieses Spiels

**Klick auf Spiel mit umrandetem Stern** → Install-Karte im Content-Bereich:
„Plugin verfügbar. ⬇ Installieren". Ein Klick lädt das Plugin aus dem
GitHub-Release, entpackt, aktiviert live — der Stern wird sofort gefüllt und
die Plugin-Tabs erscheinen.

**Klick auf Spiel mit gefülltem Stern** → die Plugin-Tabs rendern im
Content-Bereich (Katalog / Downloads / Installiert / Einstellungen — je
nach Plugin).

## Verfügbare Plugins

| Plugin | Repo | Was |
|---|---|---|
| Landwirtschafts-Simulator 25 | [KroModIx.Plugin.LS25](https://github.com/KroModIx/KroModIx.Plugin.LS25) | GIANTS ModHub + Hof Hirschfeld + modhoster, DDS-Preview, Backup/Restore, KI-Zusammenfassung |
| Satisfactory | [KroModIx.Plugin.Satisfactory](https://github.com/KroModIx/KroModIx.Plugin.Satisfactory) | ficsit.app-Katalog via GraphQL, `.smod`-Direct-Download, `.uplugin`-Manifest |
| Icarus | [KroModIx.Plugin.Icarus](https://github.com/KroModIx/KroModIx.Plugin.Icarus) | Nexus-Katalog, PAK-Manual + Steam-Workshop, Premium-Direct-Download |
| Ren'Py Assist | [KroModIx.Plugin.RenPyAssist](https://github.com/KroModIx/KroModIx.Plugin.RenPyAssist) | f95zone-Anbindung mit Login, Sub-Path-Rotation für Updates, RPA-Browser + Save-Editor, KrosteMod-Pipeline (Walkthrough/Cheat/Rename), Inline-Video-Playback |
| Dummy | [KroModIx.Plugin.Dummy](https://github.com/KroModIx/KroModIx.Plugin.Dummy) | Demo-Plugin für Entwicklungs-Tests |

Der Katalog steht in [KroModIx.PluginIndex](https://github.com/KroModIx/KroModIx.PluginIndex).
Eigenes Plugin? PR gegen die `plugins.json` schicken.

## REST-API

Der Host hat eine optionale REST-API zur externen Steuerung. Standardmäßig
aus (kein Netz-Listener). Anschalten:

```bash
KroModIx --api-port 8765 --api-token <secret> --auto-shutdown-after 5m
```

Oder persistent in `~/.config/KroModIx/settings.json` (`ApiEnabled: true`
+ `ApiBearerToken`).

Endpoints (Auszug):
- `GET /state` — aktueller App-Zustand (aktives Spiel, geladene Plugins)
- `GET /games` — alle Steam- und Manual-Spiele
- `POST /select-game { gameId }` — Sidebar-Kachel wählen
- `POST /select-tab { tabId }` — Plugin-Tab wechseln
- `POST /screenshot` — PNG-Screenshot des Hauptfensters
- `POST /events/click { elementId }` — auf Named-Control klicken
- `GET /badges` — aktuelle Update-Badges pro Steam-AppId

Auth via `Authorization: Bearer <token>`. Doku im (privaten) Repo
[KroModIx.RestApi](https://github.com/KroModIx/KroModIx.RestApi).

## Roadmap

| Milestone | Status | Was |
|---|---|---|
| M1 Foundation | ✅ | Shell, Tray, About/Settings, Self-Update via GitHub-Releases |
| M2 Steam-Discovery + Plugin-Loader | ✅ | Sidebar mit großen Cover-Kacheln, manueller Add, Plugin-Scan + Aktivierung |
| M3 LS25-Plugin | ✅ | Installiert-Tab, ModHub-Katalog, Detail-Dialog, KI-Zusammenfassung |
| M4 PluginIndex + Install-Karte + Live-Aktivierung | ✅ | 1-Klick-Install ohne Restart |
| M4.5 Plugin-Update-Service | ✅ | Update-Badge im Header + Install-Flow mit Restart-Hinweis |
| M5 Icarus-Plugin | ✅ | Nexus-Katalog, PAK-Manual + Steam-Workshop |
| M6 Satisfactory-Plugin | ✅ | ficsit.app GraphQL, `.smod`-Install |
| REST-API (Phase 2) | ✅ | External-Control, Screenshots, Clicks, Tab-Wechsel |
| KI-Erweiterung (Phase 3) | ✅ | Ollama + Cloud-Provider mit Modell-Discovery |
| Sidebar-Verhalten (Phase 4) | ✅ | Toasts, Auto-Cleanup, Kachel-Badges |
| M7 Ren'Py-Plugin | ✅ | RenPyAssist mit f95zone-Anbindung, RPA-Browser, KrosteMod-Pipeline (Walkthrough/Cheat/Rename/Translate), Inline-Video-Playback, animierte GIF-Cover, Screenshot-Timeline |
| Plugin-Update-Cache (v1.10.2) | ✅ | Rate-Limit-Fallback + persistenter Cache in `~/.config/KroModIx/plugin-update-cache.json` — Update-Info überlebt GitHub 403er + Restarts |
| Container-Rename-API (v1.10.3) | ✅ | Plugins können via `IHostServices.TryRenameManualGame` Container-Ordner umbenennen, Host re-keyed Sidebar-Kachel + Manual-Games-Store atomar |
| Plugin-Manager-Suchfilter (v1.10.4) | ✅ | Live-Filter über "Verfügbare Updates" + "Installierte Plugins" nach Name oder Plugin-ID |
| PluginIndex-Kategorien (v1.11.0) | ✅ | Genre-Chips im InstallCard (`categories`-Feld im `plugins.json`) |
| Multi-Host-Profile (v1.12.0) | ✅ | Export/Import von Plugin-Liste + Manual-Games als JSON zum Nachziehen auf einem zweiten Rechner (Settings-Fenster) |
| Favoriten + Update-Sortierung (v1.13.0) | ✅ | Rechtsklick-Toggle „⭐ Favorit" pro Sidebar-Kachel, Sortierung: Favoriten > Games mit Update > mit Plugin > Rest. Header-Badge „🎮 N Mod-Updates" |

## Entwicklung

```bash
dotnet build          # Build
dotnet test           # Tests
dotnet run --project KroModIx
```

Release: Tag `vX.Y.Z` setzen und pushen — GitHub-Action baut Windows-ZIP,
Linux-tar.gz und AppImage.

## Logs

Log-Files unter `logs/` neben der Executable (Tages-Archive, 14 Tage).
Passwörter und Tokens werden automatisch maskiert. Bei Issues die aktuelle
Log-Datei mitschicken.

## Lizenz

MIT — siehe [LICENSE](LICENSE).

---

☕ [buymeacoffee.com/kroste](https://buymeacoffee.com/kroste)
