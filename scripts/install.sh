#!/usr/bin/env bash
set -euo pipefail

# One-command installer for Linux and macOS.
# Publishes OPTISENS.App, installs it with desktop/app shortcuts, configures permissions,
# and launches the app (where first-run setup downloads the AI model if not already present).

BOLD='\033[1m'
CYAN='\033[0;36m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
DIM='\033[2m'
RESET='\033[0m'

step() { echo -e "\n${CYAN}==>${RESET} ${BOLD}$1${RESET}"; }
ok()   { echo -e "  ${GREEN}✓${RESET} $1"; }
info() { echo -e "  ${DIM}$1${RESET}"; }
warn() { echo -e "  ${YELLOW}!${RESET} $1"; }

print_logo() {
  local palette=(51 45 39 75 105 141 75 39)
  local frames=1
  if [ -t 1 ]; then
    frames=8
    printf '\033[?25l'
    trap 'printf "\033[?25h"' EXIT
  fi

  local f c1 c2 c3 c4 c5 c6
  for ((f = 0; f < frames; f++)); do
    c1="\033[38;5;${palette[$(((f + 0) % 8))]}m"
    c2="\033[38;5;${palette[$(((f + 1) % 8))]}m"
    c3="\033[38;5;${palette[$(((f + 2) % 8))]}m"
    c4="\033[38;5;${palette[$(((f + 3) % 8))]}m"
    c5="\033[38;5;${palette[$(((f + 4) % 8))]}m"
    c6="\033[38;5;${palette[$(((f + 5) % 8))]}m"

    [ "$f" -gt 0 ] && printf '\033[9A'
    printf "${c1}    ╔═══╗   ${c2} ██████╗ ██████╗ ████████╗██╗███████╗███████╗███╗   ██╗███████╗\n"
    printf "${c1}   ╔╝ ┼ ╚╗  ${c3}██╔═══██╗██╔══██╗╚══██╔══╝██║██╔════╝██╔════╝████╗  ██║██╔════╝\n"
    printf "${c2}  ─╣  ◎  ├─ ${c4}██║   ██║██████╔╝   ██║   ██║███████╗█████╗  ██╔██╗ ██║███████╗\n"
    printf "${c3}   ╚╗ ┼ ╔╝  ${c5}██║   ██║██╔═══╝    ██║   ██║╚════██║██╔══╝  ██║╚██╗██║╚════██║\n"
    printf "${c4}    ╚═══╝   ${c6}╚██████╔╝██║        ██║   ██║███████║███████╗██║ ╚████║███████║\n"
    printf "${c5}            ${c6} ╚═════╝ ╚═╝        ╚═╝   ╚═╝╚══════╝╚══════╝╚═╝  ╚═══╝╚══════╝\n"
    printf "\033[0m"
    printf "  ${DIM}Precision Sensitivity & Kinematic Calibration Suite${RESET}\n"
    printf "  ${c3}──────────────────────────────────────────────────────────────────────────${RESET}\n\n"

    [ "$f" -lt $((frames - 1)) ] && sleep 0.12
  done

  if [ -t 1 ]; then
    printf '\033[?25h'
    trap - EXIT
  fi
}

OS="$(uname -s)"
ARCH="$(uname -m)"

clear 2>/dev/null || true
print_logo
echo -e "${BOLD}OPTISENS Installer${RESET} (${OS} / ${ARCH})"
echo "This will build, install, and launch OPTISENS."

# 1. Check .NET SDK
step "1/4 Checking .NET SDK"
if ! command -v dotnet >/dev/null 2>&1; then
  echo "ERROR: .NET SDK (9.0+) is required. Install from https://dot.net/download" >&2
  exit 1
fi
info "Found dotnet $(dotnet --version)"
ok ".NET SDK ready"

# 2. Determine RID & publish
step "2/4 Building standalone OPTISENS binary"
if [ "$OS" = "Linux" ]; then
  RID="linux-x64"
  [ "$ARCH" = "aarch64" ] && RID="linux-arm64"
elif [ "$OS" = "Darwin" ]; then
  RID="osx-arm64"
  [ "$ARCH" = "x86_64" ] && RID="osx-x64"
else
  echo "ERROR: Unsupported OS '$OS'. Use the Windows installer on Windows." >&2
  exit 1
fi

OUT_DIR="dist/$RID"
info "Target runtime: $RID -> $OUT_DIR"
dotnet publish src/OPTISENS.App/OPTISENS.App.csproj -r "$RID" -c Release \
  -p:PublishSingleFile=true -p:SelfContained=true -p:PublishTrimmed=true \
  -p:TrimMode=partial -p:IncludeNativeLibrariesForSelfExtract=true \
  -o "$OUT_DIR"
ok "Published $OUT_DIR/OPTISENS.App"

# 3. Install binary & desktop integration
step "3/4 Installing OPTISENS"
if [ "$OS" = "Linux" ]; then
  DEFAULT_DIR="$HOME/.local/opt/optisens"
  read -r -p "  Install directory [$DEFAULT_DIR]: " INPUT_DIR || true
  INSTALL_DIR="${INPUT_DIR:-$DEFAULT_DIR}"

  mkdir -p "$INSTALL_DIR" "$HOME/.local/bin" "$HOME/.local/share/applications" "$HOME/.local/share/icons/hicolor/256x256/apps"
  cp "$OUT_DIR/OPTISENS.App" "$INSTALL_DIR/OPTISENS.App"
  chmod +x "$INSTALL_DIR/OPTISENS.App"
  ln -sf "$INSTALL_DIR/OPTISENS.App" "$HOME/.local/bin/optisens"
  cp src/OPTISENS.App/Assets/icon.png "$HOME/.local/share/icons/hicolor/256x256/apps/optisens.png"

  sed "s|Exec=OPTISENS.App|Exec=$INSTALL_DIR/OPTISENS.App|" packaging/linux/optisens.desktop \
    > "$HOME/.local/share/applications/optisens.desktop"
  ok "Installed binary to $INSTALL_DIR/OPTISENS.App"
  ok "Created launcher symlink ~/.local/bin/optisens and desktop entry"

  step "4/4 Checking raw mouse input permissions (udev)"
  if [ -r /dev/input/mice ] || groups | grep -q '\binput\b'; then
    ok "Input permissions look good"
  else
    warn "Raw mouse capture (/dev/input/event*) requires 'input' group + udev rule."
    read -r -p "  Install udev rule now using sudo? [Y/n]: " DO_UDEV || true
    if [[ ! "${DO_UDEV:-Y}" =~ ^[Nn]$ ]]; then
      bash packaging/linux/install-udev.sh
      ok "udev rule installed (log out and back in if prompted)"
    else
      info "Skipped udev setup; you can run 'bash packaging/linux/install-udev.sh' later."
    fi
  fi

  echo -e "\n${GREEN}${BOLD}Installation complete!${RESET}"
  echo "Launching OPTISENS (first-run setup will check/download the AI Coach model)..."
  nohup "$INSTALL_DIR/OPTISENS.App" >/dev/null 2>&1 &

elif [ "$OS" = "Darwin" ]; then
  DEFAULT_DIR="/Applications"
  read -r -p "  Install directory [$DEFAULT_DIR]: " INPUT_DIR || true
  INSTALL_BASE="${INPUT_DIR:-$DEFAULT_DIR}"
  APP_BUNDLE="$INSTALL_BASE/OPTISENS.app"

  rm -rf "$APP_BUNDLE"
  mkdir -p "$APP_BUNDLE/Contents/MacOS" "$APP_BUNDLE/Contents/Resources"
  cp "$OUT_DIR/OPTISENS.App" "$APP_BUNDLE/Contents/MacOS/OPTISENS.App"
  chmod +x "$APP_BUNDLE/Contents/MacOS/OPTISENS.App"
  cp packaging/macos/Info.plist "$APP_BUNDLE/Contents/Info.plist"
  cp src/OPTISENS.App/Assets/icon.png "$APP_BUNDLE/Contents/Resources/icon.png"
  ok "Installed $APP_BUNDLE"

  step "4/4 Permissions note"
  info "On first launch, macOS will ask for Input Monitoring permission:"
  info "System Settings -> Privacy & Security -> Input Monitoring -> enable OPTISENS."

  echo -e "\n${GREEN}${BOLD}Installation complete!${RESET}"
  echo "Launching OPTISENS (first-run setup will check/download the AI Coach model)..."
  open "$APP_BUNDLE"
fi
