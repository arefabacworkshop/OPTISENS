#!/usr/bin/env bash
set -euo pipefail

VERSION=${1:-0.5.4}
RID=${2:-osx-arm64}

# Reuse build-dmg.sh publish + OPTISENS.app bundle creation
dotnet publish src/OPTISENS.App/OPTISENS.App.csproj -r "$RID" -c Release \
  -p:PublishSingleFile=true -p:SelfContained=true -p:PublishTrimmed=true \
  -p:TrimMode=partial -p:IncludeNativeLibrariesForSelfExtract=true -o "dist/$RID"

rm -rf OPTISENS.app
mkdir -p OPTISENS.app/Contents/MacOS OPTISENS.app/Contents/Resources
cp "dist/$RID/OPTISENS.App" OPTISENS.app/Contents/MacOS/
cp packaging/macos/Info.plist OPTISENS.app/Contents/
cp src/OPTISENS.App/Assets/icon.png OPTISENS.app/Contents/Resources/

mkdir -p dist/installer
pkgbuild --component OPTISENS.app \
  --install-location /Applications \
  --identifier com.optisens.app \
  --version "$VERSION" \
  "dist/installer/OPTISENS-${VERSION}-${RID}-component.pkg"

productbuild --package "dist/installer/OPTISENS-${VERSION}-${RID}-component.pkg" \
  "dist/installer/OPTISENS-${VERSION}-${RID}.pkg"

rm -f "dist/installer/OPTISENS-${VERSION}-${RID}-component.pkg"
echo "Built dist/installer/OPTISENS-${VERSION}-${RID}.pkg"
