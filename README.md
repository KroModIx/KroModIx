# Kroste ModManager

[![CI](https://github.com/Kroste/Mod-Manager/actions/workflows/ci.yml/badge.svg)](https://github.com/Kroste/Mod-Manager/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/Kroste/Mod-Manager)](https://github.com/Kroste/Mod-Manager/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

One mod manager for all your Steam games. Cross-platform (Windows + Linux native — no Wine wrappers), Steam-aware, extensible via plugins.

> **Status:** Milestone 1 (Foundation). The app boots, discovers zero games (Steam discovery arrives in M2), and provides the shared shell — window chrome, tray, About + Settings, self-update. Real game plugins land from M3 onward.

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

## Roadmap

- **M1 — Foundation** *(current)*: shell, tray, About + Settings, self-update via GitHub releases
- **M2 — Steam discovery + plugin loader**: sidebar shows all installed Steam games with cover art, manual add for non-Steam games, plugin manifest scanning + activation, dummy plugin proves the chain
- **M3 — LS25 plugin**: first real plugin, ports the LS-ModManager core (ModHub / Hof Hirschfeld / modhoster catalogs, ZIP install, backup)
- **M4 — Plugin index + install card + auto-update**: click a game with a golden star → install its plugin live from GitHub, no restart needed
- **M5 — Icarus plugin**
- **M6 — Satisfactory plugin** (custom locator for Bottles/Wine)
- **M7 — Ren'Py plugin** (optional)

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
