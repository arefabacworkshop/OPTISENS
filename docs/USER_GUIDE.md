# OPTISENS User Guide

This guide walks through every screen and feature in **OPTISENS** from first launch to your calibrated sensitivity and AI Coach session.

---

## 1. First-Run Setup & Asset Bootstrapper

When you launch OPTISENS for the first time, it checks whether the AI Coach model (`LiquidAI/LFM3-1.7B-ONNX`, ~1.66 GB) is already present and valid.

- **Default Location**: `<installation-folder>/models/LFM3-1.7B-ONNX` (or your previously saved location in `settings.json`).
- **Custom Location**: Click **Choose…** to select any folder on another drive or shared directory, or click **Use default installation folder** to reset.
- **Download & Verification**: Click **Continue** to download all 7 required model files (`config.json`, `generation_config.json`, `tokenizer.json`, `tokenizer_config.json`, `chat_template.jinja`, `onnx/model_q4.onnx`, `onnx/model_q4.onnx_data`) with live progress percentage, transfer speed, and ETA.
- **Skip Option**: Click **Skip AI Coach for now** if you want to run calibration immediately without downloading the model.
- **Subsequent Launches**: Every time you open OPTISENS, it checks the configured folder first. If all files pass size verification, setup finishes automatically and opens the main app.

---

## 2. Hardware Profile

Before calibration begins, enter your physical setup accurately:

- **Mouse DPI** (`100` – `6400`): Your mouse sensor's active DPI setting. OPTISENS uses this to convert raw hardware counts into physical centimeters.
- **Mousepad Usable Width (cm)** (`5` – `120`): The horizontal space you comfortably sweep across. OPTISENS bounds the search range to `[10.0, min(60.0, 2 × padWidthCm)]` cm/360 so you never get an unplayable sensitivity.
- **Grip Style**: Choose your grip (`Palm`, `Claw`, `Fingertip`, or `Hybrid`) so the AI Coach can contextualize micro-adjustment vs. arm-sweep feedback.

Click **Start calibration** when ready.

---

## 3. Calibration Session

Each session runs up to **6 adaptive rounds** (stopping early if the Bayesian optimizer converges). Each round tests a candidate `cm/360` across three aiming blocks:

| Phase | Duration | What You Do |
| :--- | :--- | :--- |
| **Round Intro** | 3 s | Get ready; cursor locks to the 3D viewport. |
| **Block 1 — Tracking** | 15 s | Smoothly keep your crosshair on the moving 3D sphere. |
| **Block 2 — Flick** | 15 s | Snap and click targets as they appear across wider angles. |
| **Block 3 — Micro-Dot** | 10 s | Click small targets requiring fine micro-adjustments. |
| **Rest** | 10 s | Brief pause between rounds while OPTISENS scores your kinematics and picks the next probe sensitivity. |

> **Tip**: Press `Esc` during a session if you need to abort early. If at least one round was completed, partial results are still available.

### Developer Overlay (`--dev`)

Launch with `--dev` and press **`F12`** during a session to inspect:
- Render FPS & raw mouse sample rate (Hz)
- Active probe `cm/360`
- $\ge 10\text{ cm}$ physical swipe ruler verification (`LastSwipeCm360`) to confirm your DPI and `cm/360` match a physical ruler on your desk.

---

## 4. Results Dashboard & Game Projections

Once calibration finishes, the **Results Dashboard** displays:

- **Recommended `cm/360`**: Your optimal physical turn distance, highlighted alongside the high-confidence **recommendation band** (the range within `0.02` normalized BSI of the peak).
- **Gaussian Process Curve**: Visual plot of tested `cm/360` probes (observations) and the continuous predicted performance curve with uncertainty bounds.
- **Per-Round Breakdown**: Table showing each tested `cm/360`, **BSI** (0–100), **Accuracy**, **Jerk**, and **Flick Overshoot**.

---

## 5. Local AI Coach

Below your results, the embedded **OPTISENS AI Coach** (`LFM3-1.7B-ONNX`) runs 100% locally on your machine with no cloud API calls.

You can ask things like:
- *"Why did this sensitivity score highest for me?"*
- *"Convert my recommended cm/360 to Valorant and CS2."*
- *"Make it 5% slower and give me the new Overwatch 2 sensitivity."*
- *"My tracking felt shaky—what should I adjust?"*

The coach uses built-in tool calls to read your telemetry, compute exact engine sensitivities, and log adjustments safely.
