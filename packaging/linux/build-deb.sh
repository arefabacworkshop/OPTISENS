#!/usr/bin/env bash
set -euo pipefail

VERSION=${1:-0.5.8}
ARCH=amd64
PKG_DIR="dist/deb/optisens_${VERSION}_${ARCH}"

dotnet publish src/OPTISENS.App/OPTISENS.App.csproj -r linux-x64 -c Release \
  -p:PublishSingleFile=true -p:SelfContained=true -p:PublishTrimmed=true \
  -p:TrimMode=partial -p:IncludeNativeLibrariesForSelfExtract=true -o dist/linux-x64

rm -rf "$PKG_DIR"
mkdir -p "$PKG_DIR/DEBIAN" \
         "$PKG_DIR/opt/optisens" \
         "$PKG_DIR/usr/local/bin" \
         "$PKG_DIR/usr/share/applications" \
         "$PKG_DIR/usr/share/icons/hicolor/256x256/apps" \
         "$PKG_DIR/etc/udev/rules.d"

cp dist/linux-x64/OPTISENS.App "$PKG_DIR/opt/optisens/OPTISENS.App"
chmod 755 "$PKG_DIR/opt/optisens/OPTISENS.App"
ln -sf /opt/optisens/OPTISENS.App "$PKG_DIR/usr/local/bin/optisens"

cp packaging/linux/optisens.desktop "$PKG_DIR/usr/share/applications/optisens.desktop"
sed -i 's|Exec=OPTISENS.App|Exec=/opt/optisens/OPTISENS.App|' "$PKG_DIR/usr/share/applications/optisens.desktop"
cp src/OPTISENS.App/Assets/icon.png "$PKG_DIR/usr/share/icons/hicolor/256x256/apps/optisens.png"
cp packaging/linux/99-optisens-input.rules "$PKG_DIR/etc/udev/rules.d/99-optisens-input.rules"

cat > "$PKG_DIR/DEBIAN/control" <<EOF
Package: optisens
Version: $VERSION
Section: utils
Priority: optional
Architecture: $ARCH
Maintainer: OPTISENS
Description: AI-assisted open-source sensitivity finder
EOF

cat > "$PKG_DIR/DEBIAN/postinst" <<'EOF'
#!/usr/bin/env bash
set -e
groupadd -f input
udevadm control --reload-rules || true
udevadm trigger || true
echo "OPTISENS installed. Add your user to the 'input' group if raw mouse access is denied: sudo usermod -aG input \$USER"
EOF
chmod 755 "$PKG_DIR/DEBIAN/postinst"

mkdir -p dist/installer
dpkg-deb --build "$PKG_DIR" "dist/installer/optisens_${VERSION}_${ARCH}.deb"
echo "Built dist/installer/optisens_${VERSION}_${ARCH}.deb"
