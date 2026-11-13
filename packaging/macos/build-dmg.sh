#!/usr/bin/env bash
set -euo pipefail

RID=${1:-osx-arm64}
dotnet publish src/OPTISENS.App/OPTISENS.App.csproj -r "$RID" -c Release \
  -p:PublishSingleFile=true -p:SelfContained=true -p:PublishTrimmed=true \
  -p:TrimMode=partial -p:IncludeNativeLibrariesForSelfExtract=true -o "dist/$RID"

rm -rf OPTISENS.app
mkdir -p OPTISENS.app/Contents/MacOS OPTISENS.app/Contents/Resources
cp "dist/$RID/OPTISENS.App" OPTISENS.app/Contents/MacOS/
cp packaging/macos/Info.plist OPTISENS.app/Contents/
cp src/OPTISENS.App/Assets/icon.png OPTISENS.app/Contents/Resources/

# Signing & notarization (requires Developer ID certificate & notarytool keychain profile):
# codesign --force --options runtime --entitlements packaging/macos/entitlements.plist --sign "Developer ID Application: ..." OPTISENS.app
# ditto -c -k --keepParent OPTISENS.app OPTISENS.zip
# xcrun notarytool submit OPTISENS.zip --keychain-profile optisens --wait
# xcrun stapler staple OPTISENS.app
hdiutil create -volname OPTISENS -srcfolder OPTISENS.app -ov -format UDZO OPTISENS.dmg
