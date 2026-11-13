# Project OPTISENS

Biomechanical aiming-sensitivity calibration desktop application (cm/360 invariant) using raw OS mouse input, Silk.NET OpenGL 3.3 scenarios, Gaussian-process Bayesian optimization (Matérn 5/2 + Expected Improvement), and an embedded LFM3-1.7B ONNX AI coach.

## Build & Test

```bash
dotnet build OPTISENS.sln
dotnet test OPTISENS.sln
```

## Run

```bash
dotnet run --project src/OPTISENS.App -- --dev
```

CLI flags:
- `--model-dir <path>`: override model sidecar directory (defaults to `$XDG_DATA_HOME/optisens/models/LFM3-1.7B-ONNX`, then `<appdir>/models/LFM3-1.7B-ONNX`).
- `--dev`: enables F12 dev overlay (FPS, sample rate, probe cm/360, >=10 cm swipe ruler check).
- `--skip-ai`: run Results without loading the embedded LFM3 model.

## Download the AI Coach Model

The coach uses `LiquidAI/LFM3-1.7B-ONNX` (`model_q4.onnx`, ~1.66 GB sidecar, not embedded in the binary):

- Windows: `pwsh -File scripts/fetch-model.ps1`
- Linux / macOS: `bash scripts/fetch-model.sh`

## Platform Input Permissions

- **Windows**: Raw Input (`WM_INPUT`) works out of the box.
- **Linux**: `evdev` reads `/dev/input/event*`. If `EACCES` occurs, run `bash packaging/linux/install-udev.sh` (installs `99-optisens-input.rules` and adds your user to `input`) and re-login.
- **macOS**: `IOHIDManager` requires **System Settings → Privacy & Security → Input Monitoring**.

## One-Command Install (Linux / macOS)

```bash
bash scripts/install.sh
```

Publishes `OPTISENS.App`, installs the app & desktop/app bundle shortcuts, walks through raw-input permissions, and launches the app (where first-run setup fetches the AI Coach model if needed).

## Publish (Single-File Trimmed)

```powershell
dotnet publish src/OPTISENS.App/OPTISENS.App.csproj -r win-x64 -c Release `
  -p:PublishSingleFile=true -p:SelfContained=true -p:PublishTrimmed=true `
  -p:TrimMode=partial -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -o dist/win-x64
```

## Licenses

Application code: project license.
Model weights (`LiquidAI/LFM3-1.7B-ONNX`): redistributed separately under the **LFM Open License v1.0** (not MIT).
