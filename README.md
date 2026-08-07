# Kroste ModManager

[![CI](https://github.com/Kroste/Mod-Manager/actions/workflows/ci.yml/badge.svg)](https://github.com/Kroste/Mod-Manager/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/Kroste/Mod-Manager)](https://github.com/Kroste/Mod-Manager/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

One mod manager for all your Steam games. Cross-platform (Windows + Linux native — no Wine wrappers), Steam-aware, extensible via plugins.

> **Status:** Milestone 5. Steam library auto-discovery + plugin catalog + 1-click plugin install with **live activation (no app restart)** + first two real game plugins (LS25, Icarus). Steam-Tools (Runtimes, Proton, Redistributables) are hidden from the sidebar.

## Motivation

Most game mod managers are Windows-only, closed-source, or need Wine acrobatics to run on Linux. Every game demands its own tool. Kroste ModManager is one desktop app that:

- Auto-discovers your Steam library (Windows + Linux/Bazzite, including external drives via `libraryfolders.vdf`)
- Lets you add non-Steam games manually
- Loads a game-specific plugin (LS25, Icarus, Satisfactory, …) that knows how to manage mods for that title
- Downloads and updates plugins from GitHub releases
- Runs on Windows and Linux natively

## Installation

Grab the latest release from the [Releases page](https://github.com/Kroste/Mod-Manager/releases):

**Windows:** `ModManager-X.Y.Z-win-x64.zip` — unzip, run `ModManager.exe`. Self-contained, no install needed.

**Linux (AppImage, recommended):**
```bash
chmod +x ModManager-*-x86_64.AppImage
./ModManager-*-x86_64.AppImage
```

**Linux (tar.gz):** unpack `ModManager-X.Y.Z-linux-x64.tar.gz` and run `./ModManager`.

## Bedienung

- **Sidebar (left)** lists all installed Steam games (auto-discovered from all Steam library roots including external drives). Search box filters by title, the „only games with plugin"-toggle hides everything without a loaded plugin. Sort order: games with a plugin first, then alphabetical.
- **Add non-Steam game** with the ➕ button: name + directory (mandatory) + executable + cover + optional Steam-AppId (auto-loads the cover from Steam CDN when set). Entries persist in `~/.config/ModManager/manual-games.json`.
- **Star overlay** on each tile: **filled gold ★** = plugin loaded and active · **outlined gold ☆** = plugin available in the [PluginIndex](https://github.com/Kroste/ModManager.PluginIndex), not yet installed · **no star** = no plugin known.
- **Click a game with an outlined star** → the content area shows an install card: "Plugin available (Kroste). ⬇ Install". One click downloads the plugin from its GitHub release, extracts it, and activates it live — no app restart. The star immediately turns filled and the plugin's tabs appear.
- **Click a game with a filled star** → the plugin's tabs render in the content area.
- **Plugin deployment (manual)**: drop `<pluginName>/plugin.json` plus its DLL into `~/.config/ModManager/plugins/<pluginName>/` (or `%APPDATA%\ModManager\plugins\<pluginName>\` on Windows) and restart. Live install (as above) is preferred.

## Available plugins

- [`Kroste/ModManager.Plugins.LS25`](https://github.com/Kroste/ModManager.Plugins.LS25) — Landwirtschafts-Simulator 25
- [`Kroste/ModManager.Plugins.Icarus`](https://github.com/Kroste/ModManager.Plugins.Icarus) — Icarus (RocketWerkz)
- [`Kroste/ModManager.Plugins.Dummy`](https://github.com/Kroste/ModManager.Plugins.Dummy) — Demo plugin (end-to-end proof)

The catalog is served from [`Kroste/ModManager.PluginIndex`](https://github.com/Kroste/ModManager.PluginIndex). Send a PR to add your own.

## Roadmap

- **M1 — Foundation** ✅ shell, tray, About + Settings, self-update via GitHub releases
- **M2 — Steam discovery + plugin loader** ✅ sidebar with large cover art, manual add, plugin scanning + activation
- **M3 — LS25 plugin** ✅ installed-mods tab (Enable/Disable/Uninstall). Catalogs in v0.2+.
- **M4 — Plugin index + install card + live activation** ✅ one-click install from GitHub, no restart.
- **M5 — Icarus plugin** ✅ PAK-mod skeleton.
- **M4.5 — Plugin update service** ✅ badge in the header + install flow with restart hint (assembly is loaded, can't be swapped hot).
- **M6 — Satisfactory plugin** (custom locator for Bottles/Wine).
- **M7 — Ren'Py plugin** (optional).

## Development

```bash
dotnet build   # build
dotnet test    # tests
dotnet run --project ModManager
```

Release: VS Code task **release (tag + push)** — checks the git state, sets a `vX.Y.Z` tag, pushes, and the release workflow builds the Windows ZIP, Linux tar.gz, and AppImage.

## Logs

Log files live in `logs/` next to the executable (daily archive, 14 days). Passwords and tokens are automatically masked. Attach the current log to any issue you file.

## License

MIT — see [LICENSE](LICENSE).

---

☕ Enjoying the tool? [Buy me a coffee](https://buymeacoffee.com/kroste)
