# Changelog

All notable changes to **OPTISENS** are documented in this file.

## [0.5.4] - 2026-11-19

### Fixed
- **AI Coach Streaming & Tool-Call Leakage (`CoachOutputSanitizer` / `CoachSession` / `ToolDispatcher`):**
  - Added streaming and final output sanitization to strip `<think>`, `<tool_call>`, `<function>`, `<parameter>` tags (including split or unclosed trailing tags) and raw markdown (`**`, `__`, `` ` ``).
  - Filtered unknown/hallucinated tool calls before execution to prevent `"Unknown tool"` retry loops, and normalized common tool-name aliases.
- **AI Coach Repetition Degeneration (`Sampler` / `Lfm3Engine` / `Constants` / `SystemPrompt.txt`):**
  - Scoped `RepetitionPenalty` (`1.18`) to the last 64 generated tokens instead of the full prompt/JSON context.
  - Added an n-gram repetition loop breaker in `Lfm3Engine` to stop and trim repeating token cycles automatically.
  - Tuned `AiTemperature` (`0.20`), `AiTopK` (`40`), `AiMaxNewTokens` (`180`), and tightened `SystemPrompt.txt`.

### Changed
- **Application Typography & Coach Chat (`App.axaml` / `CoachTerminalView` / Views):**
  - Fixed root `Inter` font resolution (`fonts:Inter#Inter`) and added reusable `.chat-prose`, `.eyebrow`, and `.mono-tabular` (`Cascadia Mono, JetBrains Mono, SF Mono, Consolas`) styles.
  - Updated AI Coach transcript to clean `13.5px` `Inter` prose with auto-scroll on new messages.

## [0.5.3] - 2026-11-19

### Added
- **Interactive GP Posterior Plot (`GpPlotControl`):** Hover crosshair with live `cm/360`, predicted `BSI`, and `±95% CI` tooltip; click-to-preview any sensitivity on the curve; min/max `cm/360` axis labels; `SelectedCm360` marker and highlighted observation ring.
- **Interactive Results Dashboard (`ResultsDashboardView` / `ResultsViewModel`):**
  - Manual `cm/360` fine-tuning slider with **Reset to Optimal** button.
  - Editable **DPI** field on the results screen for instant game sensitivity recalculation.
  - `"Copied!"` confirmation feedback on game projection **Copy** buttons.
  - Clickable **Kinematic Breakdown** round rows that highlight the corresponding trial dot on the GP plot and preview its probe `cm/360`.
- **Pre-Session Input & DPI Check (`HardwareProfileView` / `HardwareProfileViewModel`):** Live raw mouse distance (`cm`), polling rate (`Hz`), and sample counter with **Reset** so users can verify DPI before starting calibration.
- **Cross-Platform Audio (`SoundEffectsService`):** Added async `afplay` (macOS) and `paplay`/`aplay` (Linux) playback for synthesized hit, miss, and phase-change cues alongside Windows `winmm.dll`.
- **AI Coach Controls & Memory Management (`Lfm3Engine` / `CoachViewModel`):** Added CPU Eco / DirectML backend toggle, **Free RAM** button (`UNLOADED` state with 5-minute idle auto-unload and transparent reload preserving transcript), and streaming BPE `StreamDecoder`.

### Changed
- **ONNX Runtime & Memory Optimization:** Upgraded `Microsoft.ML.OnnxRuntime` and `Microsoft.ML.OnnxRuntime.DirectML` to `1.24.4`, defaulted inference backend to `CpuEco`, eliminated per-token LOH allocations, capped `CoachSession` history to 14 messages, and ensured previous Coach/Session resources are disposed before recalibration.
- **Calibration Arena & Kinematics:** Updated `Scene`, `RoundController`, `ScenarioState`, and `KinematicFeatureExtractor` for 360° arena ribs, flick target tether rendering, `ViewX` yaw-axis `EFlick`/`TAdjustMs` scoring, and inter-round rest/streak/abort HUD flow.

### Removed
- Removed temporary planning documents (`plan.md`, `COMPLETION_PLAN.md`, `RAM_OPTIMIZATION_PLAN.md`).
