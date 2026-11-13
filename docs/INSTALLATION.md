# Installation & Packaging Guide

OPTISENS supports **Windows**, **Linux**, and **macOS** with native raw mouse input on every platform.

---

## Windows

### Option A: Official Setup Wizard (`.exe` via Inno Setup)

1. Install **Inno Setup 6** (if building the installer yourself; `iscc.exe` is auto-detected in standard install paths).
2. Build the standalone setup executable:

```powershell
powershell -ExecutionPolicy Bypass -File packaging/windows/build-setup.ps1
```

3. Run the generated installer in `dist/installer/OPTISENSSetup-0.5.2-win-x64.exe`.
4. Launch **OPTISENS** from the Start Menu or Desktop shortcut—the First-Run Setup screen will download or verify the AI Coach model.

### Option B: Portable / Single-File Publish

```powershell
dotnet publish src/OPTISENS.App/OPTISENS.App.csproj -r win-x64 -c Release `
  -p:PublishSingleFile=true -p:SelfContained=true -p:PublishTrimmed=true `
  -p:TrimMode=partial -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -o dist/win-x64
```

Win32 Raw Input (`WM_INPUT`) works out of the box with no extra permissions.

---

## Linux

### Option A: One-Command Terminal Installer

```bash
bash scripts/install.sh
```

This script:
1. Checks for the `.NET 10+` SDK.
2. Publishes a self-contained single-file `OPTISENS.App` binary (`linux-x64` or `linux-arm64`).
3. Installs to `~/.local/opt/optisens`, links `~/.local/bin/optisens`, and registers the desktop entry + icon.
4. Optionally installs the `udev` raw-input rule (`packaging/linux/install-udev.sh`) and launches OPTISENS.

### Option B: `.deb` Package (Debian / Ubuntu)

```bash
bash packaging/linux/build-deb.sh
sudo dpkg -i dist/optisens_*.deb
```

### Option C: AppImage

```bash
bash packaging/linux/build-appimage.sh
```

### Linux Raw Input Permissions (`evdev`)

OPTISENS reads `/dev/input/event*` directly for zero-latency, unaccelerated counts. If you see a permission error (`EACCES`), install the `udev` rule and add your user to the `input` group:

```bash
bash packaging/linux/install-udev.sh
```

Then **log out and log back in** for group membership to take effect.

---

## macOS

### Option A: One-Command Terminal Installer

```bash
bash scripts/install.sh
```

Publishes `OPTISENS.App` (`osx-arm64` on Apple Silicon or `osx-x64` on Intel), creates `/Applications/OPTISENS.app`, and launches it.

### Option B: `.pkg` / `.dmg` Packaging

- **Installer `.pkg`**: `bash packaging/macos/build-pkg.sh`
- **Disk Image `.dmg`**: `bash packaging/macos/build-dmg.sh`

### macOS Input Monitoring Permission

`IOHIDManager` requires explicit OS approval:
1. Open **System Settings → Privacy & Security → Input Monitoring**.
2. Enable **OPTISENS** (and relaunch if prompted).

---

## Offline / Pre-Downloading the AI Coach Model

If you want to pre-download `LiquidAI/LFM3-1.7B-ONNX` (~1.66 GB) before launching the app:

- **Windows (PowerShell)**:
  ```powershell
  powershell -ExecutionPolicy Bypass -File scripts/fetch-model.ps1 -Dest "models/LFM3-1.7B-ONNX"
  ```
- **Linux / macOS**:
  ```bash
  bash scripts/fetch-model.sh models/LFM3-1.7B-ONNX
  ```

You can also point OPTISENS at any pre-downloaded model directory using `--model-dir <path>` or the **Choose…** button on the First-Run Setup screen.
