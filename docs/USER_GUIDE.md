# OPTISENS User Guide

This guide walks through every screen and feature in **OPTISENS** from launch to your calibrated sensitivity, 36+ game conversions, and optional AI Coach session.

---

## 1. Opt-In AI Coach & Storage Settings

OPTISENS starts with **AI Coach disabled by default**—zero forced downloads and instant startup. You can enable or reconfigure AI anytime from **Hardware Profile → Configure AI** or directly from the **Results Dashboard** without losing your calibration results:

- **OpenAI-Compatible API (`0 MB`)**: Connect OpenAI, OpenRouter, Groq, Ollama, LM Studio, or any `/v1/chat/completions` endpoint with live connection testing. On Windows, API keys are encrypted via DPAPI (`SecretProtector`).
- **Local Offline AI (`LiquidAI/LFM3-1.7B-ONNX`, ~1.66 GB)**: Download once into your chosen folder with live byte-weighted progress, ETA, HTTP `Range` resume if interrupted, and a **Delete Local Model** option to free disk space anytime.

---

## 2. Hardware Profile, History & Arena Customization

Before calibration begins, enter your physical setup (automatically remembered across launches):

- **Mouse DPI** (`100` – `32,000`, with quick-pick pills): Your mouse sensor's active DPI setting.
- **Mousepad Usable Width (cm)** (`5` – `120`): Bounds the optimizer search to `[10.0, min(60.0, 2 × padWidthCm)]` cm/360.
- **Grip Style**: Choose **Palm**, **Claw**, or **Fingertip** grip.
- **Input & DPI Check**: Swipe 10 cm on your mousepad to verify raw input polling rate (Hz) and physical DPI accuracy.
- **Recent Calibrations & Converter**: Re-open any saved session (up to 50 stored locally) and jump straight into the **36+ Game Converter** without re-calibrating.
- **30s Quick Warmup & Crosshair/FOV**: Run an unscored 30-second warmup or customize your horizontal FOV (`90°`–`120°`), crosshair style/color/scale, and audio volume.

---

## 3. Calibration Session

Each session runs up to **6 adaptive, iso-difficulty rounds** (`HORIZON`, `FLOW`, `ORBIT`, `WEAVE`, `PENDULUM`, `APEX`) with distinct tracking trajectories, flick geometries, and micro-dot clusters:

| Phase | Duration | What You Do |
| :--- | :--- | :--- |
| **Round Intro** | 3 s | Get ready; cursor locks to the 3D viewport. |
| **Block 1 — Tracking** | 15 s | Smoothly keep your crosshair on the moving 3D sphere (**no clicking**). |
| **Block 2 — Flick** | 15 s | Snap and left-click targets before their lifetime urgency ring expires. |
| **Block 3 — Micro-Dot** | 10 s | Left-click small targets requiring fine micro-adjustments. |
| **Rest** | 10 s | Brief pause showing Round BSI, accuracy, and next-round preview (`Space` skips rest). |

- Press **`Esc`** to pause/resume (timers freeze cleanly) or **`Alt+Tab`** (auto-pauses and unlocks the cursor). Press **`M`** to mute/unmute audio.
- Launch with `--dev` and press **`F12`** for render FPS, raw polling rate, dropped samples, active probe `cm/360`, and $\ge 10\text{ cm}$ swipe ruler verification.

---

## 4. Results Dashboard, 36+ Game Converter & ADS Calculator

- **Recommended `cm/360` / `in/360`**: Optimal physical turn distance with plausible confidence band, eDPI readout, interactive Gaussian Process posterior curve, and per-round kinematic breakdown.
- **36+ Game Sensitivity Converter**: Search by name, filter by category or pinned favorites, copy raw values or console config commands (`sensitivity "..."`), and use **Compare / Convert From Existing Game** to preview your current game's `cm/360`.
- **ADS / Scoped Calculator**: Monitor Distance Vertical (`0% Focal`, `75%`, `100%`, `133.3% CS2`) multipliers for CS2, Valorant, Overwatch 2, Apex Legends, and Call of Duty.
- **Export & Share**: Copy a formatted Markdown summary or export full session telemetry as JSON.

---

## 5. Optional AI Coach

When enabled, ask natural-language questions about your telemetry or adjustments:
- *"Why did this sensitivity score highest for me?"*
- *"What is my Apex Legends or Fortnite sensitivity?"* (uses the deterministic `get_game_sensitivity` tool across all 36+ games)
- *"Make it 5% slower and give me the new Overwatch 2 sensitivity."*
