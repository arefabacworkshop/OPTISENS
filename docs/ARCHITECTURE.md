# Architecture & Mathematical Reference

This document describes the internal architecture of **OPTISENS**, its biomechanical scoring model, Gaussian-process optimizer, game projection formulas, and local AI Coach integration.

---

## Solution Layout

```text
OPTISENS.sln
├── src/
│   ├── OPTISENS.Core/           # Platform-agnostic domain logic, math, optimization, session state
│   ├── OPTISENS.Ai/             # Local ONNX GenAI engine, tokenizer, chat template, tool dispatcher
│   ├── OPTISENS.Platform/       # Cross-platform cursor lock/unlock/center helpers
│   ├── OPTISENS.Input.Windows/  # Win32 WM_INPUT raw mouse capture
│   ├── OPTISENS.Input.Linux/    # Linux evdev (/dev/input/event*) capture
│   ├── OPTISENS.Input.Mac/      # macOS IOKit IOHIDManager capture
│   └── OPTISENS.App/            # Avalonia 12 UI, OpenGL 3.3 renderer, Arena & Results views
└── tests/
    ├── OPTISENS.Core.Tests/     # 41 unit & integration tests (kinematics, BSI, GP, BO, projection, history)
    ├── OPTISENS.Ai.Tests/       # 16 unit tests (chat template, telemetry, tool parser & dispatcher)
    └── OPTISENS.App.Tests/      # 9 unit tests (Results, Setup & Shell ViewModels)
```

---

## 1. Physical Units, 36+ Game Catalog & ADS Projection

OPTISENS operates exclusively in **`cm/360`** (centimeters of physical mouse travel required for a full 360° yaw rotation):

$$\text{degPerCount} = \frac{360 \times 2.54}{\text{DPI} \times \text{cm/360}}$$

- **`GameProjection.cs` & `GameCatalog.cs`**: Converts `cm/360` ↔ in-game sensitivity across **36+ games** (including non-linear formulas for PUBG and Minecraft).
- **`AdsProjection.cs`**: Computes Monitor Distance Vertical (MDV) zoomed-to-hipfire multipliers (`0%` focal length through `133.3%` CS2 default).

---

## 2. Raw Input Pipeline & Persistence

- OS input services (`WindowsRawInputService`, `EvdevInputService`, `HidInputService`) push relative motion reports into bounded `16,384`-slot drop-oldest channels with `DroppedSamples` telemetry, preventing idle RAM growth.
- `RoundController.Tick(nowS)` drains samples into a lock-free `SampleRing` (`262,144` capacity, 8000 Hz safe), updates the 3D viewport yaw/pitch, and drives the 6 iso-difficulty `RoundVariant` drills (`RoundIntro` → `BlockTracking` → `BlockFlick` → `BlockDot` → `RoundScoring` → `Rest`).
- `SessionHistoryStore` persists up to 50 completed sessions in source-generated JSON, while `SecretProtector` encrypts stored API keys on Windows via DPAPI.

---

## 3. Kinematic Features & Biomechanical Skill Index (BSI)

After each round, `KinematicFeatureExtractor` resamples the raw trajectory at $\Delta t = 1\text{ ms}$ (`0.001 s`) and extracts:

- **Normalized Jerk**: Smoothness of angular motion normalized against $J_{\text{ref}} = 10{,}000\text{ deg/s}^3$.
- **Flick Error ($E_{\text{flick}}$)**: Signed overshoot/undershoot ratio relative to target distance (clipped to `[-1.0, 2.0]`).
- **Micro-Adjustment Time ($T_{\text{adjust}}$)**: Settling duration until velocity drops below $5.0\text{ deg/s}$ for at least $30\text{ ms}$ (normalized against $500\text{ ms}$).
- **Tracking & Hit Accuracy**: Fraction of time on target (Tracking) and hit rate (Flick & Micro-Dot).

`BsiScorer` combines these into a **0–100** composite score using weights:

- $W_1 = 0.40$ (Accuracy)
- $W_2 = 0.25$ ($1 - \text{normJerk}$)
- $W_3 = 0.20$ ($1 - |E_{\text{flick}}|$)
- $W_4 = 0.15$ ($1 - T_{\text{adjust}}$)

---

## 4. Gaussian-Process Bayesian Optimization

`BayesianOptimizer` searches over `ProbeBounds` (`[10.0, min(60.0, 2 * padWidthCm)]` cm/360):

- **Surrogate Model**: `GpRegressor` with a **Matérn 5/2** kernel (normalized length scale `0.25`) and observation noise floor $\sigma_n^2 = 0.05$, plus linear **fatigue noise inflation** ($\lambda = 0.05$ per minute).
- **Acquisition Function**: `EiAcquirer` evaluates **Expected Improvement (EI)** ($\xi = 0.01$) across `150` grid points to balance exploration of uncertain sensitivities and exploitation near the current optimum.
- **Stopping & Recommendation**: Converges when posterior max $\sigma \le 0.05$ or after `MaxRounds = 6`. Outputs the posterior mean optimum $\mu^*$ and the recommendation band where $\mu(x) \ge \mu^* - 0.02$.

---

## 5. Local AI Coach (`OPTISENS.Ai`)

- Powered by **`LiquidAI/LFM3-1.7B-ONNX`** (`model_q4.onnx`) loaded via `Microsoft.ML.OnnxRuntimeGenAI`.
- `TelemetryPayload` serializes the user's DPI, mousepad width, grip style, recommended `cm/360`, confidence band, and round breakdown into a compact JSON context.
- `ToolCallParser` and `ToolDispatcher` let the model invoke deterministic C# tools (such as sensitivity conversion across games and relative percentage adjustments) rather than hallucinating arithmetic.
