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
│   └── OPTISENS.App/            # Avalonia 11 UI, OpenGL 3.3 renderer, Setup & Results views
└── tests/
    ├── OPTISENS.Core.Tests/     # 28 unit & integration tests (kinematics, BSI, GP, BO, session)
    └── OPTISENS.Ai.Tests/       # 8 unit tests (chat template, telemetry, tool parser & dispatcher)
```

---

## 1. Physical Units & Game Projection

OPTISENS operates exclusively in **`cm/360`** (centimeters of physical mouse travel required for a full 360° yaw rotation):

$$\text{degPerCount} = \frac{360 \times 2.54}{\text{DPI} \times \text{cm/360}}$$

In `GameProjection.cs`, `cm/360` converts cleanly to and from engine sensitivity units using each engine's standard yaw constant ($\text{yaw}$ in degrees/count at sensitivity `1.0`):

| Engine (`GameEngine`) | Yaw Constant | Conversion Formula |
| :--- | :--- | :--- |
| `Cs2Source` (CS2 / Source) | `0.022` | $\text{sens} = \dfrac{360 \times 2.54}{\text{DPI} \times \text{cm/360} \times \text{yaw}}$ |
| `ValorantUnreal` (Valorant / Unreal) | `0.070` | $\text{sens} = \dfrac{360 \times 2.54}{\text{DPI} \times \text{cm/360} \times \text{yaw}}$ |
| `Overwatch2` | `0.0066` | $\text{sens} = \dfrac{360 \times 2.54}{\text{DPI} \times \text{cm/360} \times \text{yaw}}$ |
| `CallOfDuty` | `0.0066` | $\text{sens} = \dfrac{360 \times 2.54}{\text{DPI} \times \text{cm/360} \times \text{yaw}}$ |

---

## 2. Raw Input Pipeline

- Raw samples (`TimestampUs`, `DxCounts`, `DyCounts`, `Buttons`) are pushed from the OS-specific input thread into a lock-free `SampleRing` (`65,536` capacity).
- `RoundController.Tick(nowS)` drains samples on each frame, updates the 3D viewport yaw/pitch using the active probe `cm/360`, and advances the round state machine (`RoundIntro` → `BlockTracking` → `BlockFlick` → `BlockDot` → `RoundScoring` → `Rest`).
- Any block with fewer than `MinBlockSamples` (`100`) samples is flagged as `InsufficientSamples` so corrupted telemetry does not poison the optimizer.

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
