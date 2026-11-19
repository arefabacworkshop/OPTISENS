#!/usr/bin/env bash
set -euo pipefail

VERSION=${1:-0.5.8}
DIR=AppDir
rm -rf "$DIR"
mkdir -p "$DIR/usr/bin" "$DIR/usr/share/applications" "$DIR/usr/share/icons/hicolor/256x256/apps"
cp dist/linux-x64/OPTISENS.App "$DIR/usr/bin/"
cp packaging/linux/optisens.desktop "$DIR/usr/share/applications/"
cp src/OPTISENS.App/Assets/icon.png "$DIR/usr/share/icons/hicolor/256x256/apps/optisens.png"
cp packaging/linux/AppRun "$DIR/AppRun" && chmod +x "$DIR/AppRun"
cp packaging/linux/99-optisens-input.rules packaging/linux/install-udev.sh "$DIR/usr/bin/"

./appimagetool-x86_64.AppImage "$DIR" "OPTISENS-$VERSION-x86_64.AppImage"
