#!/usr/bin/env bash
# Baut das AppImage aus einem fertigen linux-x64-Publish-Ordner.
# Aufruf: packaging/linux/build-appimage.sh <version> <publish-dir>
# --appimage-extract-and-run nötig, weil im CI kein FUSE verfügbar ist.
set -euo pipefail

VERSION="$1"
PUBLISH_DIR="$2"
APPDIR="AppDir"

rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin"
cp -r "$PUBLISH_DIR"/* "$APPDIR/usr/bin/"
cp packaging/linux/kromodix.desktop "$APPDIR/"
cp ModManager/Assets/kromodix.png "$APPDIR/"
cp packaging/linux/AppRun "$APPDIR/AppRun"
chmod +x "$APPDIR/AppRun" "$APPDIR/usr/bin/ModManager"

curl -sSL -o appimagetool \
  https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
chmod +x appimagetool
./appimagetool --appimage-extract-and-run "$APPDIR" "ModManager-${VERSION}-x86_64.AppImage"
echo "AppImage gebaut: ModManager-${VERSION}-x86_64.AppImage"
