# Project Kinesis — Implementation Blueprint (`plan.md`)

**Status:** FINAL — implementation-ready. **Target reader:** autonomous coding agent.
**Repo root:** `C:/Users/aref/Desktop/test sabi/OptiSense` (solution `Kinesis.sln`, namespaces `Kinesis.*`).

> **Scope contract of this document:** this is the plan only. Do not build the application until asked to execute this plan. When executing, implement exactly what is specified here. Every open design question has already been decided (§1). Where a value is an empirical tuning constant it is tagged `[TUNABLE]` and collected in §13.6; everything else is fixed. If a statement here conflicts with library documentation at implementation time, follow this document and record the deviation in `docs/DEVLOG.md`.

---

## 0. How to Use This Document

1. Read §1 (Decisions) once. Do not re-litigate anything in it.
2. Work milestones M1→M5 in order (§12). Each milestone lists files, code to write, and a **Verification** step that MUST be run before moving on.
3. Reference implementations are given inline as full C#. Copy them into the named files, then satisfy the listed acceptance tests (§11). Do not "improve" algorithms that have tests pinned against them.
4. All magic numbers live in `src/Kinesis.Core/Config/Constants.cs` (full listing in §13.6). Never inline a numeric literal outside that file unless it is `0`, `1`, `2`, or a GLSL vector constructor.
5. Units: **cm/360 is the only unit of sensitivity inside the engine** (§3). Game sensitivities are computed at the presentation edge only.

**Definition of Done (whole project):** `dotnet test` green; the three acceptance scenarios in §11.5 pass on Windows x64 with a real 1000 Hz mouse; `dotnet publish` produces a runnable single-file app per §10; the Results screen shows the GP curve, kinematic breakdown, per-game sensitivity cards, and a streamed AI coaching session with one successful tool call.

---

## 1. Locked Decisions (do not re-decide)

| # | Decision | Choice | Rationale / Evidence |
| --- | --- | --- | --- |
| D1 | Language/runtime | C# / .NET 9 (LTS). `net9.0` target everywhere. | .NET 10 acceptable if SDK present; 9 is the verified baseline; nothing here needs 10. |
| D2 | UI | Avalonia 11.x (`Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter`) | Per plan; Tier-1 on Win/Linux/macOS. |
| D3 | Bindings | **Compiled bindings only** (`x:CompileBindings="True"`, `x:DataType` on every view). | Required for safe trimming (D11). Reflection bindings break under the trimmer. |
| D4 | 3D viewport | Silk.NET OpenGL 3.3 core via `Avalonia.OpenGL.Controls.OpenGlControlBase`. | Wiring verified: `GL.GetApi(gl.GetProcAddress)` inside `OnOpenGlInit(GlInterface gl, int fb)` (kekekeks/Avalonia-Silk.NET-Example). |
| D5 | GP / BO | **Pure C#**, hand-rolled dense Cholesky in `Kinesis.Core`. No Math.NET. | ~25 lines of math vs. a reflection-heavy dependency; trimming-safe; deterministic. |
| D6 | AI model | `LiquidAI/LFM3-1.7B-ONNX`, CPU `model_q4` (`onnx/model_q4.onnx` 352 KB + `model_q4.onnx_data` 1.63 GB ≈ 1.66 GB total). | Official ONNX export; q4 is Liquid's recommended deployment quant. |
| D7 | AI inference runtime | **Direct `Microsoft.ML.OnnxRuntime` `InferenceSession` with a manual decode loop** (§8.5), NOT `OnnxRuntimeGenAI.Generator`. | Verified 2026-11: `LiquidAI/LFM3-1.7B-ONNX` ships **no `genai_config.json`** (HF repo tree checked); ORT-GenAI `genai_config` supports decoder types `llama/phi3/qwen2/qwen3/…` and KV caches only (`past_key_values.%d.key`), while LFM3 is a tri-hybrid with **conv caches `past_conv.N`** (13 short-conv + 5 sliding-window + 6 global layers). The Generator cannot drive this model. Liquid's own reference loop runs plain ONNX Runtime over cache tensors (docs.liquid.ai ONNX guide). |
| D8 | Tokenizer | `Microsoft.ML.OnnxRuntimeGenAI.Tokenizer` (loads the shipped `tokenizer.json` + `tokenizer_config.json`). Chat template rendered by our own `Lfm3ChatTemplate` (§8.4), byte-exact. | ORT-GenAI's tokenizer is HF-`tokenizer.json`-compatible native parsing; we depend on neither its Generator nor its template support. |
| D9 | Package refs | `Microsoft.ML.OnnxRuntimeGenAI` (pulls `Microsoft.ML.OnnxRuntime` transitively) + `Silk.NET.OpenGL` + Avalonia + `CommunityToolkit.Mvvm`. Nothing else. | Zero-dependency principle; one native stack. |
| D10 | Model asset distribution | Model folder is a **sidecar data directory** `<appdir>/models/LFM3-1.7B-ONNX/`, never embedded in the single-file exe. `--model-dir` CLI override. | A 1.66 GB blob inside `PublishSingleFile` extraction is hostile on every OS; the model is data, like a game asset. |
| D11 | Publish mode | Self-contained single-file **with trimming**, NOT Native AOT, for v1. | Avalonia + Silk.NET + ORT native libs under Native AOT is experimental; trimmed single-file self-contained meets "one file, no runtime install". Native AOT = stretch goal (§10.6). |
| D12 | Input capture | Raw counts only (Win32 `WM_INPUT`, Linux `evdev`, macOS `IOHIDManager`). Never `Cursor.Position`/pointer events. | cm/360 math requires OS-acceleration-free counts. macOS `CGEventTap` deltas are post-acceleration → HID-level reads are mandatory (deviation from plan text; CGEventTap is a documented degraded fallback, §4.4). |
| D13 | BSI weights | $w_1{=}0.40,\ w_2{=}0.25,\ w_3{=}0.20,\ w_4{=}0.15$; sub-metrics normalized to [0,1]; result clamped [0,1] then ×100. | Plan's formula left weights open; these are the locked defaults `[TUNABLE]`. |
| D14 | GP hyperparams | Inputs normalized $u=(x-b_{lo})/(b_{hi}-b_{lo})$, targets $y/100$; Matérn 5/2 with $\ell=0.25$, $\sigma_f^2=1$, $\sigma_n^2=0.05$; EI $\xi=0.01$; 150-point grid; stop when $\max\sigma<\varepsilon=0.05$; 2 anchors + ≤4 EI rounds, hard cap 6 rounds. | Plan values, made concrete; normalization keeps $K$ well-conditioned. |
| D15 | Probe order | Round 1 = bounds-min anchor, Round 2 = bounds-max anchor, rounds 3–6 = EI argmax. | Plan ("2 boundary anchor runs"). |
| D16 | Blinding | Active round shows NO sensitivity anywhere in UI (HUD shows only timer, round x/6, live score). Values revealed on Results only. | Plan ("zero visual feedback on active settings"). |
| D17 | AI sampling | `temperature=0.15`, `top_k=50`, `repetition_penalty=1.1`, `max_new_tokens=512`, thinking disabled (assistant prefill is the empty-think block). Inference only on Results/Coach screens — never during an active round. | Plan + Liquid's recommended 0.1/50/1.1 family. |
| D18 | Fatigue control | 40 s active per round × ≤6 rounds; round weight $\omega_r=\exp(-0.05\,(t_r-t_0)/60\,\mathrm{s})$ applied as GP observation noise $\sigma_{n,r}^2=\sigma_n^2/\omega_r$. | Plan risk table; decay enters the GP, NOT the BSI formula (keeps $y$ interpretable). |
| D19 | Determinism | Optimizer, scoring, projection math are pure functions with no timers; only `KinematicFeatureExtractor` consumes timestamps. | Testability; "deterministic" is the product's core claim. |
| D20 | Error surfacing | Every native failure (no mouse device, TCC denied, model missing) → guided remediation dialog; never a silent fallback. | Plan's permission-guide requirement, generalized. |

### 1.1 Research Log (facts verified 2026-11-13)

| Fact | Source |
| --- | --- |
| LFM3-1.7B: 1.73B params, 256K ctx, 128K vocab, 24 layers (13 short-conv + 5 sliding-window + 6 global attention); recommended sampling `temperature=0.1, top_k=50, repetition_penalty=1.1`; `enable_thinking=False` supported | docs.liquid.ai/lfm/models/lfm3-1.7b |
| Chat template: `<\|startoftext\|>`, `<\|im_start\|>role\n…<\|im_end\|>\n`, thinking `<\|think\|>…<\|/think\|>`, generation prompt `<\|im_start\|>assistant\n` + think-prefill | docs.liquid.ai/lfm/key-concepts/chat-template + raw `LiquidAI/LFM3-1.7B/chat_template.jinja` |
| Tool-call format (superset of qwen3_coder): tool schemas as JSON lines inside a `<tools>…</tools>` block appended to the system message; assistant tool calls in XML form (grammar in §8.6); tool results wrapped in the jinja `render_tool_response` open/close tags (byte-exact strings given in §8.4) in `tool`-role messages | `chat_template.jinja` macros `render_tool_calls` / `render_tool_response`, hexdump-verified |
| ONNX export I/O contract: inputs `input_ids, attention_mask, position_ids, logits_to_keep, past_conv.N, past_key_values.N.key/.value`; outputs `logits, present_conv.N, present.N.key/.value` | HF `LiquidAI/LFM3-1.7B-ONNX` README |
| No `genai_config.json` in `LiquidAI/LFM3-1.7B-ONNX` or `onnx-community/LFM3-1.7B-ONNX`; ORT-GenAI decoder types exclude LFM3 and its cache schema is KV-only | HF repo trees (API), onnxruntime.ai/docs/genai/reference/config.html |
| Avalonia↔Silk.NET wiring: subclass `OpenGlControlBase`, `GL.GetApi(gl.GetProcAddress)` in `OnOpenGlInit`, draw in `OnOpenGlRender(GlInterface gl, int fb)`, continuous frames via `Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Background)` | github.com/kekekeks/Avalonia-Silk.NET-Example |

---

## 2. Architecture

```
┌────────────────────────────────────────────────────────────────────────┐
│ Kinesis.App — Avalonia 11 (compiled bindings, MVVM)                    │
│  HardwareProfileView · CalibrationSessionView (GlViewport)             │
│  ResultsDashboardView (GP plot, projection cards, CoachTerminalView)   │
└───────────────▲────────────────────────────────────────▲───────────────┘
                │ immutable DTOs / events (channels)      │
┌───────────────┴──────────────────────────┐ ┌───────────┴───────────────┐
│ Kinesis.Core — pure domain (no UI/OS)    │ │ Kinesis.Ai                │
│  Units/        PhysicalTurnDistance      │ │  Lfm3Engine (ORT session, │
│  Projection/   GameProjection            │ │   manual cache decode loop│
│  Kinematics/   FeatureExtractor          │ │  Lfm3ChatTemplate         │
│  Scoring/      BsiScorer                 │ │  ToolCallParser           │
│  Optimization/ GpRegressor, EiAcquirer,  │ │  ToolDispatcher           │
│                  BayesianOptimizer       │ │  CoachSession, Telemetry  │
│  Session/      RoundController, scenar.  │ └───────────▲───────────────┘
└───────────────▲──────────────────────────┘             │ telemetry JSON
                │ RawMotionSample stream                 │ (Core DTOs)
┌───────────────┴──────────────────────────┐             │
│ Kinesis.Input.{Windows,Linux,Mac}        │─────────────┘
│  IRawInputService (per-OS impl)          │
└──────────────────────────────────────────┘
```

Dependency rule (enforced by project references): `App → {Core, Ai, Input.*}`; `Ai → Core`; `Input.* → Core`; `Core → nothing`. All cross-boundary traffic is `readonly record struct`s or `System.Threading.Channels`; no shared mutable state except `SessionState` behind atomic snapshot swaps.

Threading model:

| Thread | Owner | Work | Priority |
| --- | --- | --- | --- |
| UI | Avalonia | rendering, viewmodels | normal |
| GL render | Avalonia compositor callback | scene draw only; reads latest `ViewportState` snapshot | n/a |
| Input pump (1 per OS service) | `Kinesis.Input.*` | native capture → `SampleRing` + channel | `AboveNormal` |
| Session engine | `Kinesis.Core.Session` | round state machine, metric aggregation | `AboveNormal` |
| AI worker | `Kinesis.Ai` | token generation, cancellable | `BelowNormal` |
| Optimizer | async task between rounds | GP fit + EI probe selection | normal |

Data flow per input packet: native callback → `RawMotionSample{TUs,Dx,Dy,Buttons}` → (a) `ViewportState` accumulator (immutable snapshot swapped via `Interlocked`), (b) `SampleRing` (65 536-slot overwrite-oldest ring) consumed by `KinematicFeatureExtractor` at round end. Never let the UI thread touch raw samples.

### 2.1 Repository Layout (complete — create exactly these files)

```
OptiSense/
├── Kinesis.sln
├── Directory.Build.props
├── global.json
├── README.md
├── docs/DEVLOG.md
├── scripts/fetch-model.ps1            # §10.7 model download (Windows)
├── scripts/fetch-model.sh             # §10.7 model download (Linux/macOS)
├── packaging/
│   ├── linux/99-kinesis-input.rules   # §4.3
│   ├── linux/install-udev.sh          # §4.3
│   ├── linux/kinesis.desktop
│   ├── linux/AppRun
│   ├── linux/build-appimage.sh        # §10.4
│   ├── macos/Info.plist               # §10.5
│   └── macos/build-dmg.sh             # §10.5
├── assets/models/LFM3-1.7B-ONNX/      # runtime sidecar (downloaded, NOT in git; §10.7)
├── src/
│   ├── Kinesis.Core/
│   │   ├── Kinesis.Core.csproj
│   │   ├── Config/Constants.cs                    # §13.6 full listing
│   │   ├── Units/PhysicalTurnDistance.cs          # §3.3
│   │   ├── Projection/GameProjection.cs           # §3.4
│   │   ├── Input/IRawInputService.cs              # §4.1 (RawMotionSample, MouseButtons)
│   │   ├── Input/SampleRing.cs
│   │   ├── Kinematics/Trajectory.cs               # §5.4
│   │   ├── Kinematics/KinematicFeatureExtractor.cs
│   │   ├── Kinematics/Attempt.cs
│   │   ├── Scoring/BsiScorer.cs                   # §6.3
│   │   ├── Optimization/GpRegressor.cs            # §7.2
│   │   ├── Optimization/EiAcquirer.cs             # §7.3
│   │   ├── Optimization/BayesianOptimizer.cs      # §7.4
│   │   ├── Optimization/ProbeBounds.cs            # §7.5
│   │   ├── Session/OptimizationSession.cs         # §7.6
│   │   ├── Session/RoundController.cs             # §9.5
│   │   ├── Session/RoundPlan.cs
│   │   ├── Session/ScenarioState.cs
│   │   ├── Session/ViewportState.cs
│   │   └── Coaching/TelemetryPayload.cs           # §8.7
│   ├── Kinesis.Input.Windows/
│   │   ├── Kinesis.Input.Windows.csproj
│   │   ├── Win32Native.cs                         # §4.2
│   │   └── WindowsRawInputService.cs
│   ├── Kinesis.Input.Linux/
│   │   ├── Kinesis.Input.Linux.csproj
│   │   ├── LibcNative.cs                          # §4.3
│   │   └── EvdevInputService.cs
│   ├── Kinesis.Input.Mac/
│   │   ├── Kinesis.Input.Mac.csproj
│   │   ├── IOKitNative.cs                         # §4.4
│   │   └── HidInputService.cs
│   ├── Kinesis.Platform/
│   │   ├── Kinesis.Platform.csproj
│   │   └── CursorHelper.cs                        # §4.6 cursor lock/hide (round entry/exit)
│   ├── Kinesis.Ai/
│   │   ├── Kinesis.Ai.csproj
│   │   ├── Lfm3Engine.cs                          # §8.5
│   │   ├── Lfm3ChatTemplate.cs                    # §8.4
│   │   ├── ToolCallParser.cs                      # §8.6
│   │   ├── ToolDispatcher.cs                      # §8.6
│   │   ├── CoachSession.cs                        # §8.8
│   │   └── SamplingConfig.cs
│   └── Kinesis.App/
│       ├── Kinesis.App.csproj
│       ├── Program.cs                             # §10.1 (CLI args, --model-dir)
│       ├── App.axaml / App.axaml.cs               # contains explicit DataTemplates VM→View (trim-safe; no ViewLocator)
│       ├── Platform/PlatformInputFactory.cs
│       ├── ViewModels/{ViewModelBase,HardwareProfileViewModel,SessionViewModel,ResultsViewModel,CoachViewModel}.cs
│       ├── Views/{ShellView,HardwareProfileView,CalibrationSessionView,ResultsDashboardView,CoachTerminalView}.axaml(+.cs)
│       ├── Controls/GlViewport.cs                 # OpenGlControlBase subclass, §9.2
│       ├── Controls/GpPlotControl.cs              # §11.? plot widget — see §12 M5
│       ├── Rendering/{Scene,ShaderLib,Camera,SphereMesh}.cs   # §9.2
│       └── Assets/ (icon)
└── tests/
    ├── Kinesis.Core.Tests/  (xunit)
    │   ├── ProjectionTests.cs  TrajectoryTests.cs  BsiScorerTests.cs
    │   ├── GpRegressorTests.cs  OptimizerTests.cs  RoundControllerTests.cs
    │   └── FakeRawInputService.cs                 # §11.4 fixture
    └── Kinesis.Ai.Tests/    (xunit)
        ├── ChatTemplateTests.cs  ToolCallParserTests.cs
        └── Fixtures/ (telemetry.json, expected-prompt.txt)
```

All three `Kinesis.Input.*` projects compile on every host (the P/Invokes are declarations only); `PlatformInputFactory` selects at runtime. Do not add RID-conditional compilation.

### 2.2 Build Configuration (exact file contents)

`global.json`:
```json
{ "sdk": { "version": "9.0.100", "rollForward": "latestFeature" } }
```

`Directory.Build.props`:
```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>13</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <InvariantGlobalization>true</InvariantGlobalization>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <Deterministic>true</Deterministic>
  </PropertyGroup>
</Project>
```

Package versions — **pin to latest stable at implementation time**; known-good baselines (re-verify at restore, mark any bump in `docs/DEVLOG.md`):

| Package | Baseline | Used by |
| --- | --- | --- |
| `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter` | `11.3.x` | App |
| `Avalonia.Diagnostics` | `11.3.x` | App, `Condition="'$(Configuration)'=='Debug'"` only |
| `CommunityToolkit.Mvvm` | `8.4.x` | App |
| `Silk.NET.OpenGL` | `2.22.x` | App |
| `Microsoft.ML.OnnxRuntimeGenAI` | `0.10.x` (pulls `Microsoft.ML.OnnxRuntime`) | Ai |
| `Microsoft.NET.Test.Sdk` / `xunit` / `xunit.runner.visualstudio` | `17.x` / `2.9.x` / `2.8.x` | tests |

Project references: `App → Core, Ai, Platform, Input.Windows, Input.Linux, Input.Mac`; `Ai → Core`; `Input.* → Core`; `Platform → nothing` (raw P/Invoke only); tests reference their subject. `Core` has **zero** package references.

`src/Kinesis.App/Kinesis.App.csproj` (critical properties):
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <BuiltInComInteropSupport>true</BuiltInComInteropSupport>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
    <!-- publish defaults; §10 has per-OS commands -->
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
    <PublishTrimmed>true</PublishTrimmed>
    <TrimMode>partial</TrimMode>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
    <SuppressTrimAnalysisWarnings>false</SuppressTrimAnalysisWarnings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../Kinesis.Core/Kinesis.Core.csproj" />
    <ProjectReference Include="../Kinesis.Ai/Kinesis.Ai.csproj" />
    <ProjectReference Include="../Kinesis.Platform/Kinesis.Platform.csproj" />
    <ProjectReference Include="../Kinesis.Input.Windows/Kinesis.Input.Windows.csproj" />
    <ProjectReference Include="../Kinesis.Input.Linux/Kinesis.Input.Linux.csproj" />
    <ProjectReference Include="../Kinesis.Input.Mac/Kinesis.Input.Mac.csproj" />
  </ItemGroup>
</Project>
```

`TrimMode=partial` + `AvaloniaUseCompiledBindingsByDefault` is the verified-safe combination for Avalonia 11 (full `link` trimming pulls reflection paths in Skia/ORT — leave it alone). `app.manifest`: dpiAwareness `PerMonitorV2` (harmless for raw input, correct for window sizing).

---

## 3. Units & Projection Math (`Kinesis.Core/Units`, `Kinesis.Core/Projection`)

### 3.1 Invariant

`cm360` = centimeters of mouse travel for a full 360° in-game turn. Everything derives from

$$
\text{countsPer360} = \frac{\text{cm360}\cdot\text{DPI}}{2.54},\qquad
\text{degPerCount} = \frac{360\cdot 2.54}{\text{DPI}\cdot \text{cm360}}.
$$

Game sensitivity for engine yaw $\theta$ (degrees turned per count per 1.0 sens):

$$
\text{gameSens} = \frac{360\cdot 2.54}{\text{DPI}\cdot \text{cm360}\cdot \theta}
$$

### 3.2 Engine yaw registry (exact constants)

| Game / engine | $\theta$ | Notes |
| --- | --- | --- |
| CS2 / Source (`m_yaw` default) | `0.022` | also Apex, TF2, CS:GO |
| Valorant / Unreal | `0.070` | Valorant UI granularity 0.01; config accepts finer |
| Overwatch 2 | `0.0066` | |
| Call of Duty (MW/Warzone family) | `0.0066` | Quake-derived yaw, shared with OW |

Worked example, **DPI = 800, cm360 = 30** ($360\cdot2.54=914.4$):

| Game | computation | sens (4 dp) |
| --- | --- | --- |
| CS2 | $914.4/(800\cdot30\cdot0.022)=914.4/528$ | `1.7318` |
| Valorant | $914.4/(800\cdot30\cdot0.070)=914.4/1680$ | `0.5443` |
| Overwatch 2 | $914.4/(800\cdot30\cdot0.0066)=914.4/158.4$ | `5.7727` |
| Call of Duty | same as OW2 | `5.7727` |

Round-trip identity (test-pinned): `cm360 = 360*2.54 / (DPI * gameSens * yaw)`.

### 3.3 Code — `src/Kinesis.Core/Units/PhysicalTurnDistance.cs`

```csharp
namespace Kinesis.Core.Units;

/// <summary>Invariant sensitivity: centimeters of mouse travel per 360° turn.
/// Value semantics; always positive; constructed only via validated factories.</summary>
public readonly record struct PhysicalTurnDistance
{
    public double Cm360 { get; }

    private PhysicalTurnDistance(double cm360) => Cm360 = cm360;

    public static PhysicalTurnDistance FromCm(double cm360)
    {
        if (!double.IsFinite(cm360) || cm360 <= 0)
            throw new ArgumentOutOfRangeException(nameof(cm360), cm360, "cm/360 must be finite and > 0");
        return new PhysicalTurnDistance(cm360);
    }

    public double CountsPer360(double dpi) => Cm360 * dpi / 2.54;
    public double DegPerCount(double dpi) => 360.0 * 2.54 / (dpi * Cm360);
    public double RadPerCount(double dpi) => DegPerCount(dpi) * (Math.PI / 180.0);

    /// <summary>Centimeters of travel for a raw count delta (sign preserved).</summary>
    public double CountsToCm(double counts, double dpi) => counts * 2.54 / dpi;

    public override string ToString() => Cm360.ToString("F2") + " cm/360";
}
```

### 3.4 Code — `src/Kinesis.Core/Projection/GameProjection.cs`

```csharp
namespace Kinesis.Core.Projection;

public enum GameEngine { Cs2Source = 0, ValorantUnreal = 1, Overwatch2 = 2, CallOfDuty = 3 }

public static class GameProjection
{
    // Degrees per count per 1.0 game-sens unit. See §3.2 table.
    public static double YawDeg(GameEngine e) => e switch
    {
        GameEngine.Cs2Source      => 0.022,
        GameEngine.ValorantUnreal => 0.070,
        GameEngine.Overwatch2     => 0.0066,
        GameEngine.CallOfDuty     => 0.0066,
        _ => throw new ArgumentOutOfRangeException(nameof(e))
    };

    /// <summary>gameSens = 360*2.54 / (DPI * cm360 * yaw).</summary>
    public static double ToGameSens(GameEngine e, double dpi, double cm360)
        => 360.0 * 2.54 / (dpi * cm360 * YawDeg(e));

    /// <summary>cm360 = 360*2.54 / (DPI * gameSens * yaw).</summary>
    public static double ToCm360(GameEngine e, double dpi, double gameSens)
        => 360.0 * 2.54 / (dpi * gameSens * YawDeg(e));
}
```

---

## 4. Platform Input Subsystem (`Kinesis.Input.*`)

### 4.1 Common contract — `src/Kinesis.Core/Input/IRawInputService.cs` (Core holds the interface only)

```csharp
using System.Threading.Channels;

namespace Kinesis.Core.Input;

/// <summary>One OS-level relative motion report. Timestamps are microseconds,
/// monotonic (Windows: QPC; Linux: input_event.time; macOS: mach_absolute_time).</summary>
public readonly record struct RawMotionSample(long TimestampUs, int DxCounts, int DyCounts, MouseButtons Buttons);

[Flags]
public enum MouseButtons { None = 0, Left = 1, Right = 2, Middle = 4, X1 = 8, X2 = 16 }

public interface IRawInputService : IDisposable
{
    MouseButtons CurrentButtons { get; }          // atomic snapshot for click detection
    bool IsReceiving { get; }                     // ≥1 device delivering samples
    ChannelReader<RawMotionSample> Samples { get; }
    event Action<int>? DeviceCountChanged;        // drives D20 remediation dialog
    void Start();
    void Stop();
}
```

Shared: `src/Kinesis.Core/Input/SampleRing.cs` — fixed-capacity (65 536) overwrite-oldest ring of `RawMotionSample` with `long Sequence` bookkeeping so a round can bound its window. Pump threads write; the extractor snapshots on round end.

Factory: `src/Kinesis.App/Platform/PlatformInputFactory.cs` selects the impl via `OperatingSystem.IsWindows()/IsLinux()/IsMacOS()`.

### 4.2 Windows — `Kinesis.Input.Windows/WindowsRawInputService.cs`

Design: a **dedicated thread owns a message-only HWND**; `RegisterRawInputDevices` with `RIDEV_INPUTSINK` targets it. Capture is fully decoupled from Avalonia (no WndProc subclassing).

Native surface (`Win32Native.cs`, `LibraryImport` source-generated P/Invoke, `unsafe`):

```csharp
internal static partial class Win32Native
{
    internal const int WM_INPUT = 0x00FF, WM_INPUT_DEVICE_CHANGE = 0x00FE, WM_QUIT = 0x0012;
    internal const int RIDEV_INPUTSINK = 0x00000100, RIDEV_DEVNOTIFY = 0x00002000, RIDEV_REMOVE = 0x00000001;
    internal const int RID_INPUT = 0x10000003;
    internal const int RIM_TYPEMOUSE = 0;
    internal const int RI_MOUSE_LEFT_BUTTON_DOWN   = 0x0001, RI_MOUSE_LEFT_BUTTON_UP   = 0x0002;
    internal const int RI_MOUSE_RIGHT_BUTTON_DOWN  = 0x0004, RI_MOUSE_RIGHT_BUTTON_UP  = 0x0008;
    internal const int RI_MOUSE_MIDDLE_BUTTON_DOWN = 0x0010, RI_MOUSE_MIDDLE_BUTTON_UP = 0x0020;
    internal const int RI_MOUSE_BUTTON_4_DOWN      = 0x0040, RI_MOUSE_BUTTON_4_UP      = 0x0080;
    internal const int RI_MOUSE_BUTTON_5_DOWN      = 0x0100, RI_MOUSE_BUTTON_5_UP      = 0x0200;
    internal const int MOUSE_MOVE_RELATIVE = 0x00, MOUSE_MOVE_ABSOLUTE = 0x01;
    internal const int GIDC_ARRIVAL = 1, GIDC_REMOVAL = 2;
    internal static readonly IntPtr HWND_MESSAGE = new(-3);

    [StructLayout(LayoutKind.Sequential)]
    internal struct RAWINPUTDEVICE { public ushort usUsagePage, usUsage; public int dwFlags; public IntPtr hwndTarget; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RAWINPUTHEADER { public int dwType, dwSize; public IntPtr hDevice, wParam; }

    [StructLayout(LayoutKind.Explicit)]
    internal struct RAWMOUSE
    {
        [FieldOffset(0)]  public uint ulRawButtons;   // low 16: usButtonFlags, high 16: usButtonData
        [FieldOffset(4)]  public uint ulButtons;      // legacy union arm; unused
        [FieldOffset(8)]  public int  lLastX;
        [FieldOffset(12)] public int  lLastY;
        [FieldOffset(16)] public uint ulExtraInformation;
        public ushort ButtonFlags => (ushort)(ulRawButtons & 0xFFFF);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int pt_x, pt_y; }

    [LibraryImport("user32.dll")] internal static partial int RegisterRawInputDevices(
        [In] RAWINPUTDEVICE[] pRawInputDevices, int uiNumDevices, int cbSize);
    [LibraryImport("user32.dll")] internal static partial uint GetRawInputData(
        IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);
    [LibraryImport("user32.dll")] internal static partial IntPtr CreateWindowExW(
        uint exStyle, IntPtr className, IntPtr windowName, uint style, int x, int y,
        int width, int height, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);
    [LibraryImport("user32.dll")] internal static partial ushort RegisterClassW(ref WNDCLASSW lpWndClass);
    [LibraryImport("user32.dll")] internal static partial IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [LibraryImport("user32.dll")] internal static partial int GetMessageW(out MSG lpMsg, IntPtr hWnd, uint min, uint max);
    [LibraryImport("user32.dll")] internal static partial int TranslateMessage(ref MSG lpMsg);
    [LibraryImport("user32.dll")] internal static partial IntPtr DispatchMessageW(ref MSG lpMsg);
    [LibraryImport("user32.dll")] internal static partial IntPtr PostThreadMessageW(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);
    [LibraryImport("kernel32.dll")] internal static partial bool QueryPerformanceCounter(out long lpPerformanceCount);
    [LibraryImport("kernel32.dll")] internal static partial bool QueryPerformanceFrequency(out long lpFrequency);
    // WNDCLASSW: style, lpfnWndProc (delegate* unmanaged[Cdecl]<IntPtr, uint, IntPtr, IntPtr, IntPtr>),
    // cbClsExtra, cbWndExtra, hInstance, hIcon, hCursor, hbrBackground, lpszMenuName, lpszClassName
}
```

**Implementation steps (exact order):**

1. `Start()` → spawn `Thread(PumpMain)`: `IsBackground = true`, `Name = "kinesis-rawinput"`, priority `AboveNormal`.
2. `PumpMain`:
   a. `QueryPerformanceFrequency` → cache `_qpcFreq`.
   b. Register window class `"KinesisRawInput"` with `DefWindowProcW` as proc; keep the delegate/function-pointer alive in a static field.
   c. `CreateWindowExW(0, classAtom, 0, 0, 0, 0, 0, 0, HWND_MESSAGE, 0, 0, 0)` → `_msgHwnd`.
   d. `RAWINPUTDEVICE rid = { usUsagePage: 0x01 /* Generic Desktop */, usUsage: 0x02 /* Mouse */,
      dwFlags: RIDEV_INPUTSINK | RIDEV_DEVNOTIFY, hwndTarget: _msgHwnd }`; call `RegisterRawInputDevices([rid], 1, Marshal.SizeOf<RAWINPUTDEVICE>())`. Non-zero failure → `PlatformInputException` (D20 dialog).
   e. Message loop `GetMessageW` → `TranslateMessage` → `DispatchMessageW` until `WM_QUIT`.
3. `WM_INPUT` handler (no allocations beyond a pooled buffer):
   a. `QueryPerformanceCounter` **first** → `TimestampUs = (long)(qpc * 1_000_000.0 / _qpcFreq)`.
   b. Size query `GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, (uint)Unsafe.SizeOf<RAWINPUTHEADER>())`; rent `byte[size]` from `ArrayPool`.
   c. `ref header = ref Unsafe.As<byte, RAWINPUTHEADER>(ref buf[0])`; if `header.dwType != RIM_TYPEMOUSE` → return.
   d. `ref mouse = ref Unsafe.As<byte, RAWMOUSE>(ref buf[Unsafe.SizeOf<RAWINPUTHEADER>()])` — **use `Unsafe.SizeOf`, never hardcode** (header is 24 B on x64 and RAWMOUSE at offset 24 is correctly aligned).
   e. If `(mouse.ulRawButtons >> 16) & MOUSE_MOVE_ABSOLUTE != 0`… the flags live in `usFlags` = high word of the first union arm per the RAWINPUT docs; treat any sample with `usFlags & MOUSE_MOVE_ABSOLUTE` set as **absolute device → skip** (violates the invariant).
   f. Update `_buttons` from `ButtonFlags` (toggle on the down/up constants; `Volatile.Write`).
   g. `Samples.Writer.TryWrite(new RawMotionSample(ts, mouse.lLastX, mouse.lLastY, _buttons))`; return buffer to pool. Never block.
4. `WM_INPUT_DEVICE_CHANGE`: track `header.hDevice` arrivals (`GIDC_ARRIVAL`) and removals (`GIDC_REMOVAL`) in a `HashSet<IntPtr>` under lock → raise `DeviceCountChanged(count)`.
5. `Stop()`: `PostThreadMessageW(threadId, WM_QUIT, 0, 0)`, unregister device (`RIDEV_REMOVE`), join ≤ 500 ms, dispose.

**Pitfall notes (read before coding):** call `GetRawInputData` with `pData = null` first to learn the size; `RAWINPUT` mouse data follows the header immediately at `Unsafe.SizeOf<RAWINPUTHEADER>()`; no `Marshal.PtrToStructure` in the hot path (allocates); `Environment.TickCount` is NOT an acceptable timestamp; multiple `WM_INPUT` messages can coalesce per pump cycle — each message is one report, handle every one.

### 4.3 Linux — `Kinesis.Input.Linux/EvdevInputService.cs`

Design: direct libc reads from `/dev/input/event*` mouse nodes; no `libevdev`. Works identically on Wayland and X11 (bypasses the compositor).

Native surface (`LibcNative.cs`):

```csharp
internal static partial class LibcNative
{
    internal const int O_RDONLY = 0, O_NONBLOCK = 0x800, O_CLOEXEC = 0x80000;
    internal const int EV_SYN = 0x00, EV_KEY = 0x01, EV_REL = 0x02;
    internal const int SYN_REPORT = 0, REL_X = 0x00, REL_Y = 0x01;
    internal const int BTN_LEFT = 0x110, BTN_RIGHT = 0x111, BTN_MIDDLE = 0x112,
                       BTN_SIDE = 0x113, BTN_EXTRA = 0x114;
    internal const uint EVIOCGVERSION = 0x80044501;
    // EVIOCGBIT(ev, len) = _IOC(_IOC_READ, 'E', 0x20 + ev, len) — implement via _IOC helper

    [StructLayout(LayoutKind.Sequential)]
    internal struct InputEvent { public long Sec, Usec; public ushort Type, Code; public int Value; } // x64: 24 bytes

    [StructLayout(LayoutKind.Sequential)]
    internal struct PollFd { public int fd; public short events; public short revents; } // POLLIN = 0x001

    [LibraryImport("libc", EntryPoint = "open")]  internal static partial int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
    [LibraryImport("libc", EntryPoint = "read")]  internal static partial nint Read(int fd, void* buf, nuint count);
    [LibraryImport("libc", EntryPoint = "close")] internal static partial int Close(int fd);
    [LibraryImport("libc", EntryPoint = "ioctl")] internal static partial int Ioctl(int fd, nuint request, void* arg);
    [LibraryImport("libc", EntryPoint = "poll")]  internal static partial int Poll(PollFd* fds, nuint nfds, int timeout);
}
```

**Implementation steps:**

1. Discovery at `Start()` + `FileSystemWatcher("/dev/input")` (Created/Deleted): candidates = `/dev/input/by-id/*-event-mouse`, fallback brute-force `/dev/input/event0..31`. Validate: `open(O_RDONLY|O_NONBLOCK|O_CLOEXEC)` succeeds, `EVIOCGVERSION` succeeds, `EVIOCGBIT(0)` includes `EV_REL`, `EVIOCGBIT(EV_REL)` includes `REL_X|REL_Y`. Keep only passing fds.
2. Single reader thread with `poll()` (10 ms timeout) over all fds. Per fd accumulate `dx, dy` and pending button changes since the last `SYN_REPORT`.
3. `EV_SYN / SYN_REPORT` → if `dx != 0 || dy != 0 || buttonsChanged` → `Samples.Writer.TryWrite(new RawMotionSample((long)(ev.Sec * 1_000_000L + ev.Usec), dx, dy, buttons))`; reset accumulators. **Batching to `SYN_REPORT` is mandatory** — one logical report arrives as several `EV_REL` events.
4. `EV_KEY`: `BTN_LEFT/RIGHT/MIDDLE/SIDE/EXTRA` with `Value` 0/1 (2 = autorepeat → ignore) toggle `MouseButtons` (SIDE/EXTRA map to X1/X2).
5. Hotplug: on watcher events re-run discovery; removed fds closed; `DeviceCountChanged` raised.
6. `Stop()`: close fds, dispose watcher, join thread.

**Permissions (D20 remediation):** if any candidate returns `EACCES`, show the guided dialog offering to run via `pkexec` (shipped as `packaging/linux/install-udev.sh`):

```bash
sudo groupadd -f input
sudo usermod -aG input "$USER"
echo 'KERNEL=="event*", SUBSYSTEM=="input", MODE="0660", GROUP="input", TAG+="uaccess"' \
  | sudo tee /etc/udev/rules.d/99-kinesis-input.rules
sudo udevadm control --reload && sudo udevadm trigger
# re-login required
```

### 4.4 macOS — `Kinesis.Input.Mac/HidInputService.cs`

Design: `IOHIDManager` input-value callbacks deliver **raw HID usages** (pre-acceleration). Requires Input Monitoring permission (TCC). This deviates from the plan's `CGEventTap` because CGEvent deltas are post-acceleration and break the invariant (D12).

Native surface (`IOKitNative.cs`, `LibraryImport` into `IOKit.framework` / `CoreFoundation.framework`):

```csharp
internal static partial class IOKitNative
{
    internal const uint kHIDPage_GenericDesktop = 0x01, kHIDPage_Button = 0x09;
    internal const uint kHIDUsage_GD_Mouse = 0x02, kHIDUsage_GD_X = 0x30, kHIDUsage_GD_Y = 0x31;

    [LibraryImport("IOKit", EntryPoint = "IOHIDManagerCreate")]
    internal static partial IntPtr IOHIDManagerCreate(IntPtr allocator, uint options);
    [LibraryImport("IOKit", EntryPoint = "IOHIDManagerSetDeviceMatching")]
    internal static partial void IOHIDManagerSetDeviceMatching(IntPtr mgr, IntPtr matching);
    [LibraryImport("IOKit", EntryPoint = "IOHIDManagerOpen")]
    internal static partial int IOHIDManagerOpen(IntPtr mgr, uint options);
    [LibraryImport("IOKit", EntryPoint = "IOHIDManagerRegisterInputValueCallback")]
    internal static partial void IOHIDManagerRegisterInputValueCallback(IntPtr mgr, IntPtr callback, IntPtr context);
    [LibraryImport("IOKit", EntryPoint = "IOHIDManagerScheduleWithRunLoop")]
    internal static partial void IOHIDManagerScheduleWithRunLoop(IntPtr mgr, IntPtr runLoop, IntPtr mode);
    [LibraryImport("IOKit", EntryPoint = "IOHIDValueGetElement")]   internal static partial IntPtr IOHIDValueGetElement(IntPtr value);
    [LibraryImport("IOKit", EntryPoint = "IOHIDValueGetIntegerValue")] internal static partial int IOHIDValueGetIntegerValue(IntPtr value);
    [LibraryImport("IOKit", EntryPoint = "IOHIDElementGetUsagePage")] internal static partial uint IOHIDElementGetUsagePage(IntPtr elem);
    [LibraryImport("IOKit", EntryPoint = "IOHIDElementGetUsage")]     internal static partial uint IOHIDElementGetUsage(IntPtr elem);
    // CoreFoundation: CFDictionaryCreateMutable, CFStringCreateWithCString, CFNumberCreate,
    // CFRunLoopGetCurrent, CFRunLoopRun, CFRunLoopStop, mach_absolute_time, mach_timebase_info
}
```

**Implementation steps:**

1. Build matching dictionary `{"DeviceUsagePage": 0x01, "DeviceUsage": 0x02}` (CFNumber ints + CFDictionaryMutable; keys via `CFStringCreateWithCString`).
2. `IOHIDManagerCreate` → matching → `IOHIDManagerRegisterInputValueCallback` (`delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>`) → `IOHIDManagerScheduleWithRunLoop(mgr, CFRunLoopGetCurrent(), kCFRunLoopDefaultMode)` on a dedicated thread that runs `CFRunLoopRun()`.
3. Callback: `elem = IOHIDValueGetElement(v)`; switch on `(UsagePage, Usage)`:
   - `(0x01, 0x30)` → `dx += IOHIDValueGetIntegerValue(v)`; `(0x01, 0x31)` → `dy += …` and flush a `RawMotionSample` (X then Y arrive within one HID report; emit on Y, or after 2 ms elapsed — accumulate-then-flush).
   - `(0x09, usage 1..5)` → toggle `MouseButtons` (1→Left, 2→Right, 3→Middle, 4→X1, 5→X2).
   - Timestamp: `mach_absolute_time` scaled by `mach_timebase_info` → µs.
4. `IOHIDManagerOpen` non-zero (`kIOReturnNotPrivileged`) → D20 dialog: *"System Settings → Privacy & Security → Input Monitoring → enable Kinesis, then relaunch"*.

**Degraded fallback (documented, not default):** `CGEventTapCreate(kCGHIDEventTap, kCGHeadInsertEventTap, kCGEventTapOptionListenOnly, …)` reading `kCGMouseEventDeltaX/Y`. When active, the app MUST show a "raw-input degraded" banner (D12). Info.plist: `NSInputMonitoringUsageDescription` text; `codesign --options runtime` before notarization (§10.5).

### 4.5 Service-level verification (all platforms)

Manual check on real hardware: mark the mousepad, execute one swipe of exactly `cm360` cm (ruler), assert the viewport yaw changed by $360^\circ \pm 1\%$ using the dev overlay (§9.7). Unit level: `FakeRawInputService` (scripted `RawMotionSample` sequences) is the fixture for all session tests (§11.4).

### 4.6 Cursor confinement & visibility — `src/Kinesis.Platform/CursorHelper.cs` (M1)

Purpose: during an active round the OS cursor must stay over the viewport and be hidden, so ballistic flicks cannot click other applications and the player aims by feel (not by cursor). Wired into `RoundController` on round entry/exit (§9.5): `LockAndHide` at `RoundIntro` entry, `UnlockAndShow` at `RoundScoring` entry and on abort. Cross-platform cursor hiding inside the window is additionally done by `GlViewport.Cursor = Cursor.None` (Avalonia handles it); this helper covers the OS-level cursor outside the window and pointer-cursor association.

```csharp
using System.Runtime.InteropServices;

namespace Kinesis.Platform;

/// <summary>OS cursor confinement/hiding for active calibration rounds.
/// Windows: ClipCursor + ShowCursor. macOS: CGAssociateMouseAndMouseCursorPosition + hide/show.
/// Linux: confinement is compositor-managed (see notes); raw evdev button capture is global
/// regardless, so hit-testing is unaffected — helper is a documented no-op there.</summary>
public static partial class CursorHelper
{
    private static int _lockDepth;
    private static bool _cursorHidden;

    // ---- Windows ----
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [LibraryImport("user32.dll")]
    internal static partial int ClipCursor(ref RECT rect);

    [LibraryImport("user32.dll")]
    internal static partial int ShowCursor(bool bShow);

    [LibraryImport("user32.dll")]
    internal static partial int ClipCursor(nint zero);   // NULL overload to release

    // ---- macOS (CoreGraphics) ----
    [LibraryImport("CoreGraphics", EntryPoint = "CGAssociateMouseAndMouseCursorPosition")]
    internal static partial int CGAssociateMouseAndMouseCursorPosition(
        [MarshalAs(UnmanagedType.I1)] bool connected); // false = decouple cursor from mouse

    [LibraryImport("CoreGraphics", EntryPoint = "CGDisplayHideCursor")]
    internal static partial uint CGDisplayHideCursor(uint display);

    [LibraryImport("CoreGraphics", EntryPoint = "CGDisplayShowCursor")]
    internal static partial uint CGDisplayShowCursor(uint display);

    [LibraryImport("CoreGraphics", EntryPoint = "CGMainDisplayID")]
    internal static partial uint CGMainDisplayID();

    /// <summary>Confine the OS cursor to a 1-px box at (centerX, centerY) in *screen* coordinates
    /// and hide it. Idempotent (depth-counted). Call from the UI thread.</summary>
    public static void LockAndHide(int centerX, int centerY)
    {
        if (Interlocked.Increment(ref _lockDepth) > 1) return;
        if (OperatingSystem.IsWindows())
        {
            var r = new RECT { Left = centerX - 1, Top = centerY - 1, Right = centerX + 1, Bottom = centerY + 1 };
            ClipCursor(ref r);
            if (!_cursorHidden) { ShowCursor(false); _cursorHidden = true; }
        }
        else if (OperatingSystem.IsMacOS())
        {
            CGAssociateMouseAndMouseCursorPosition(false);   // decouple: flicks don't move the OS cursor
            CGDisplayHideCursor(CGMainDisplayID());
        }
        // Linux: no-op by design (see class summary). X11 pointer-grab is deliberately NOT used:
        // it requires the native X display handle from Avalonia and fails under Wayland; evdev
        // delivers button events globally, so round hit-testing never depended on window focus.
    }

    /// <summary>Restore cursor visibility/association. Safe to call without a prior lock.</summary>
    public static void UnlockAndShow()
    {
        if (Interlocked.Decrement(ref _lockDepth) > 0) return;
        _lockDepth = 0;
        if (OperatingSystem.IsWindows())
        {
            ClipCursor(0);                                   // release confinement
            if (_cursorHidden) { ShowCursor(true); _cursorHidden = false; }
        }
        else if (OperatingSystem.IsMacOS())
        {
            CGDisplayShowCursor(CGMainDisplayID());
            CGAssociateMouseAndMouseCursorPosition(true);    // re-couple
        }
    }
}
```

Notes: `centerX/centerY` are screen coordinates of the viewport center — the view computes them once at round entry (`this.TranslatePoint(new Point(w/2, h/2), null)` → add window origin). If `LockAndHide` throws (`DllNotFoundException` on an unsupported OS), catch at the call site, log to DEVLOG, and continue without confinement (raw capture still works — D20 says surface, not crash: show a one-line HUD warning "cursor not confined").

---

## 5. Kinematic Feature Extraction (`Kinesis.Core/Kinematics`)

### 5.1 Pipeline (order is mandatory)

`RawMotionSample` stream → `SampleRing` → **resample to uniform 1 ms grid** → central differences → per-block metric scans. Raw packets arrive event-driven with irregular $\Delta t$; finite differences (especially third-order jerk) over irregular timestamps are meaningless. Resampling is not optional.

**Displacement & resampling** (`TrajectoryResampler`, pure):

1. Convert counts → centimeters: $\Delta x_{cm} = \Delta x \cdot 2.54 / \text{DPI}$, same for $y$ (DPI is the profile's, validated $[100, 6400]$).
2. Cumulative path length per axis: $s_x(t_i) = \sum_{j\le i} \Delta x_{cm,j}$; keep the sample timestamps $t_i$ in **seconds** (`TimestampUs / 1e6`).
3. Uniform grid $t_k = t_0 + k\Delta$, $\Delta = 0.001$ s. Linearly interpolate $s_x, s_y$ onto the grid → $\tilde s_x[k], \tilde s_y[k]$.
4. Discard grid segments outside $[t_0, t_n]$; a block with $<100$ grid samples (100 ms) is invalid → round rejected with `RoundInvalidReason.InsufficientSamples` (resurface in Results as a data-quality note, D20).

**Differences (central, on the grid)** with $v$ in cm/s, $a$ in cm/s², $j$ in cm/s³:

$$
v[k] = \frac{\tilde s[k+1]-\tilde s[k-1]}{2\Delta},\quad
a[k] = \frac{v[k+1]-v[k-1]}{2\Delta},\quad
j[k] = \frac{a[k+1]-a[k-1]}{2\Delta}
$$

(One-sided differences at the grid edges.) The same operators run on the **view path** $Y[k]$ (degrees) and $X[k]$ (degrees) defined in §5.2 for task-space metrics.

### 5.2 Task-space path (probe sensitivity link)

Within a round the viewport renders at the probe sensitivity, so the view angles follow from physical travel:

$$
\text{degPerCm} = \frac{360}{\text{cm360}},\qquad
Y[k] = \tilde s_y[k]\cdot\text{degPerCm},\quad X[k] = \tilde s_x[k]\cdot\text{degPerCm}
$$

(Equivalently `Y[k]` comes straight from the camera yaw accumulator in §9.3 — extractor recomputes from samples to stay pure.)

### 5.3 Metric definitions (exact)

**Kinematic jerk $J$ (scoring variant, angular):** $J = \operatorname{mean}_k |j_Y[k]|$ over the tracking block's grid, in deg/s³.

> **Design correction (D21, supersedes plan §3.1 wording):** the plan computes jerk on physical displacement (cm/s³). Across rounds with *different probe sensitivities* that measure is not comparable: the same angular aiming path at cm360 = 50 moves $\tfrac{50}{30}\times$ more centimeters than at 30, mechanically inflating $J$ for slower probes and biasing the optimizer. Angular jerk $j_Y$ (equivalently physical jerk × degPerCm) is the biomechanical quantity the plan intends ("high values indicate over-sensitivity"): low cm360 amplifies tremor into view-space jerk. **Score with angular jerk** (constant `JRefDegPerS3`, §13.6); the physical jerk (cm/s³) is still computed and stored in telemetry for diagnostics. Both paths use the same difference operators, so implementation cost is one extra call.

$$
\text{NormalizedJerk} = \operatorname{clip}\!\left(\frac{J}{J_{\text{ref}}}, 0, 1\right)
$$

**Overshoot ratio $E_{\text{flick}}$** (per ballistic flick, target at signed azimuth $\theta^*$ relative to flick start, flick axis $\sigma = \operatorname{sign}(\theta^*)$):

1. $D[k] = \sigma\cdot\big(Y[k]-Y[k_{\text{start}}]\big)$ — view excursion along the flick axis.
2. $\text{peak} = \max_k D[k]$.
3. $E_{\text{flick}} = (\text{peak} - |\theta^*|)/|\theta^*|$, clipped to $[-1, +2]$ for scoring robustness (values beyond are data errors).

Positive = deceleration failure (probe too fast, i.e. cm360 too low); negative = premature stop or ROM limit (probe too slow).

**Micro-correction latency $T_{\text{adjust}}$** (per flick/dot attempt):

1. Angular speed on the grid: $\omega[k] = |Y'[k]|$ (deg/s, central difference).
2. $t_{\text{zero}}$ = first grid time $t \ge t_{\text{target-onset}}$ where $\omega < 5$ deg/s **continuously for 30 ms** (30 consecutive grid points).
3. $T_{\text{adjust}} = \max(0,\ t_{\text{click}} - t_{\text{zero}})$ in ms. If the attempt has no click: $T_{\text{adjust}}$ = 0 and the attempt counts as a miss (accuracy absorbs the penalty).

**Accuracy** per block (§9.4 defines attempts/hits): tracking → time-on-target fraction; flicks/dots → `hits / attempts`.

All sub-metric constants: §13.6.

### 5.4 Code — `src/Kinesis.Core/Kinematics/KinematicFeatureExtractor.cs`

```csharp
using Kinesis.Core.Input;

namespace Kinesis.Core.Kinematics;

public readonly record struct KinematicBlock(
    double[] GridT,       // seconds, dt = 0.001
    double[] ViewX,       // degrees
    double[] ViewY,       // degrees
    double[] PhysX,       // cm
    double[] PhysY);      // cm

public readonly record struct BlockMetrics(
    double JerkDegPerS3,      // mean |j_Y|, tracking block
    double JerkCmPerS3,       // physical-space jerk (telemetry only, D21)
    double MeanSpeedDegPerS,  // diagnostic
    double[] FlickErrors,     // one E_flick per flick attempt
    double[] AdjustLatencyMs, // one T_adjust per click attempt
    double TimeOnTargetFrac,  // tracking block
    int Hits, Attempts);      // flick/dot blocks

public static class Trajectory
{
    /// <summary>Irregular samples → uniform 1 ms grid. Pure. Throws on empty input.</summary>
    public static KinematicBlock Resample(
        IReadOnlyList<RawMotionSample> samples, double dpi, double cm360, double tStartS, double tEndS)
    {
        if (samples.Count == 0) throw new ArgumentException("empty sample window");
        double degPerCm = 360.0 / cm360;
        // 1) cumulative displacement in cm, absolute times in seconds
        var t = new double[samples.Count + 1];
        var sx = new double[samples.Count + 1];
        var sy = new double[samples.Count + 1];
        t[0] = samples[0].TimestampUs / 1e6; sx[0] = 0; sy[0] = 0;
        for (int i = 0; i < samples.Count; i++)
        {
            t[i + 1] = samples[i].TimestampUs / 1e6;
            sx[i + 1] = sx[i] + samples[i].DxCounts * 2.54 / dpi;
            sy[i + 1] = sy[i] + samples[i].DyCounts * 2.54 / dpi;
        }
        // 2) uniform grid
        double dt = Constants.ResampleDtS;                 // 0.001
        int n = (int)Math.Floor((tEndS - tStartS) / dt) + 1;
        var g = new KinematicBlock(new double[n], new double[n], new double[n], new double[n], new double[n]);
        int p = 0;
        for (int k = 0; k < n; k++)
        {
            double tk = tStartS + k * dt;
            g.GridT[k] = tk;
            while (p < t.Length - 2 && t[p + 1] < tk) p++;
            double span = t[p + 1] - t[p];
            double u = span <= 0 ? 0 : Math.Clamp((tk - t[p]) / span, 0, 1);
            g.PhysX[k] = sx[p] + (sx[p + 1] - sx[p]) * u;
            g.PhysY[k] = sy[p] + (sy[p + 1] - sy[p]) * u;
            g.ViewX[k] = g.PhysX[k] * degPerCm;
            g.ViewY[k] = g.PhysY[k] * degPerCm;
        }
        return g;
    }

    /// <summary>Central differences along one series. Returns same-length array (one-sided at edges).</summary>
    public static double[] Diff(double[] s, double dt)
    {
        var d = new double[s.Length];
        for (int i = 0; i < s.Length; i++)
        {
            d[i] = (i, s.Length - 1 - i) switch
            {
                (0, _) when s.Length > 1 => (s[1] - s[0]) / dt,
                (_, 0) when s.Length > 1 => (s[^1] - s[^2]) / dt,
                _ => (s[i + 1] - s[i - 1]) / (2 * dt)
            };
        }
        return d;
    }

    public static double MeanAbsJerk(double[] path, double dt)
    {
        var v = Diff(path, dt); var a = Diff(v, dt); var j = Diff(a, dt);
        double sum = 0; foreach (var x in j) sum += Math.Abs(x);
        return sum / j.Length;
    }
}

public sealed class KinematicFeatureExtractor
{
    /// <summary>Attempts = list of (targetOnsetS, clickS-or-null, signedTargetAzimuthDeg, blockKind).</summary>
    public BlockMetrics Extract(
        KinematicBlock block,
        IReadOnlyList<Attempt> attempts,
        double dt = 0.001)
    {
        double jerk = Trajectory.MeanAbsJerk(block.ViewY, dt);      // scoring variant (D21)
        double jerkPhys = Trajectory.MeanAbsJerk(block.PhysY, dt);  // telemetry only

        var eFlick = new List<double>();
        var tAdj = new List<double>();
        foreach (var at in attempts)
        {
            if (at.BlockKind == BlockKind.Flick)
                eFlick.Add(EFlick(block, at));
            if (at.ClickS is double click)
                tAdj.Add(TAdjustMs(block, at, click));
        }
        return new BlockMetrics(jerk, jerkPhys, MeanSpeed(block, dt),
            eFlick.ToArray(), tAdj.ToArray(), 0, 0, 0); // Tot fractions filled by caller per block kind
    }

    internal static double EFlick(KinematicBlock b, Attempt at)
    {
        double sigma = Math.Sign(at.SignedTargetAzimuthDeg);
        if (sigma == 0) sigma = 1;
        int k0 = IndexOfTime(b.GridT, at.TargetOnsetS);
        double y0 = b.ViewY[k0];
        double peak = double.MinValue;
        for (int k = k0; k < b.GridT.Length; k++)
            peak = Math.Max(peak, sigma * (b.ViewY[k] - y0));
        double target = Math.Abs(at.SignedTargetAzimuthDeg);
        double e = (peak - target) / target;
        return Math.Clamp(e, -1.0, 2.0);
    }

    internal static double TAdjustMs(KinematicBlock b, Attempt at, double clickS)
    {
        var omega = Trajectory.Diff(b.ViewY, 0.001).Select(Math.Abs).ToArray();
        int k0 = IndexOfTime(b.GridT, at.TargetOnsetS);
        int need = Constants.ZeroVelDwellMs;                   // 30 grid points at dt=0.001
        int run = 0;
        for (int k = k0; k < omega.Length; k++)
        {
            run = omega[k] < Constants.ZeroVelThresholdDegPerS ? run + 1 : 0;   // 5.0 deg/s
            if (run >= need)
            {
                double tZero = b.GridT[k - need + 1];
                return Math.Max(0, (clickS - tZero) * 1000.0);
            }
        }
        return 0; // never settled: latency unknown, accuracy penalizes
    }

    static double MeanSpeed(KinematicBlock b, double dt)
    {
        var v = Trajectory.Diff(b.ViewY, dt);
        double s = 0; foreach (var x in v) s += Math.Abs(x);
        return s / v.Length;
    }

    static int IndexOfTime(double[] t, double ts)
    {
        int k = (int)Math.Round((ts - t[0]) / 0.001);
        return Math.Clamp(k, 0, t.Length - 1);
    }
}

public enum BlockKind { Tracking, Flick, Dot }
public readonly record struct Attempt(
    BlockKind BlockKind,
    double TargetOnsetS,
    double? ClickS,
    double SignedTargetAzimuthDeg,   // relative to attempt start yaw
    double TargetRadiusDeg,
    bool Hit);
```

(Expose `Attempt` builders from the scenario engine §9.4; the extractor never looks at pixels.)

---

## 6. Biomechanical Stability Index (`Kinesis.Core/Scoring`)

### 6.1 Component normalization (all $\to [0,1]$)

| Symbol | Definition | Clamp |
| --- | --- | --- |
| $A$ | Accuracy = $\sum_b w_b A_b / \sum_b w_b$, block weights $w_b$ = block seconds (15/15/10) | [0,1] |
| $N$ | `NormalizedJerk` (§5.3) | [0,1] |
| $E$ | $\operatorname{mean} \|E_{\text{flick}}\|$ over flick attempts; no flicks in round → 0 | [0,1] |
| $T$ | $\operatorname{mean}(T_{\text{adjust}})/500\,\mathrm{ms}$ over click attempts; **no clicks at all → 0.5** (neutral, deterministic) | [0,1] |

### 6.2 Formula

Plan formula: $y = w_1 A + w_2 (1-N) - w_3 E - w_4 T$ has maximum $w_1+w_2=0.65$, which would waste the [0,1] range and confuse the "score /100" display. The shifted equivalent is used:

$$
y_{\text{norm}} = \operatorname{clip}\big(0.40\,A + 0.25\,(1-N) + 0.20\,(1-E) + 0.15\,(1-T),\ 0,\ 1\big),\qquad y = 100\,y_{\text{norm}}
$$

Identity (test-pinned): $y_{\text{plan}} = y_{\text{norm}} - (w_3+w_4)$; argmax over probe sensitivities is identical, GP behavior is affine-invariant up to the noise term.

### 6.3 Code — `src/Kinesis.Core/Scoring/BsiScorer.cs`

```csharp
using Kinesis.Core.Kinematics;

namespace Kinesis.Core.Scoring;

public readonly record struct BsiComponents(double Accuracy, double NormJerk, double EFlickAbs, double TAdjustNorm);
public readonly record struct RoundScore(double Cm360, double YNorm, BsiComponents Components, BlockMetrics[] Blocks);

public sealed class BsiScorer
{
    // Weights (D13); sum = 1.0
    public const double W1 = 0.40, W2 = 0.25, W3 = 0.20, W4 = 0.15;

    public RoundScore Score(double cm360, IReadOnlyList<ScoredBlock> blocks)
    {
        double wSum = 0, acc = 0;
        foreach (var b in blocks) { wSum += b.WeightSeconds; acc += b.WeightSeconds * b.Accuracy; }
        double A = wSum > 0 ? acc / wSum : 0;

        double N = blocks.Select(b => b.Metrics.JerkDegPerS3 / Constants.JRefDegPerS3)
                         .DefaultIfEmpty(0.5).Select(v => Math.Clamp(v, 0, 1)).Max();

        var eAll = blocks.SelectMany(b => b.Metrics.FlickErrors).Select(Math.Abs).ToArray();
        double E = eAll.Length > 0 ? eAll.Average() : 0;

        var tAll = blocks.SelectMany(b => b.Metrics.AdjustLatencyMs).ToArray();
        double T = tAll.Length > 0
            ? Math.Clamp(tAll.Average() / Constants.TAdjustRefMs, 0, 1)   // 500 ms
            : 0.5;

        var c = new BsiComponents(A, N, E, T);
        double y = Math.Clamp(W1 * A + W2 * (1 - N) + W3 * (1 - E) + W4 * (1 - T), 0, 1);
        return new RoundScore(cm360, y, c, blocks.Select(b => b.Metrics).ToArray());
    }
}

public readonly record struct ScoredBlock(BlockKind Kind, double WeightSeconds, double Accuracy, BlockMetrics Metrics);
```

Round weight (D18, applied by the optimizer, §7.6): $\omega_r = \exp(-0.05\,(t_r - t_0)/60)$, observation noise $\sigma_{n,r}^2 = 0.05 / \omega_r$.

---

## 7. Bayesian Optimizer (`Kinesis.Core/Optimization`)

All math in normalized space: $u = (x - b_{lo}) / (b_{hi} - b_{lo}) \in [0,1]$, $z = y_{\text{norm}} \in [0,1]$.

### 7.1 Kernel — Matérn 5/2

$$
k(u,u') = \sigma_f^2\left(1 + \frac{\sqrt{5}\,r}{l} + \frac{5 r^2}{3 l^2}\right)\exp\!\left(-\frac{\sqrt{5}\,r}{l}\right),\quad r = |u-u'|,\ \sigma_f^2=1,\ l=0.25
$$

### 7.2 Code — `src/Kinesis.Core/Optimization/GpRegressor.cs`

```csharp
namespace Kinesis.Core.Optimization;

/// <summary>Exact GP regression, dense Cholesky. n ≤ 8 → O(n³) is microseconds.</summary>
public sealed class GpRegressor
{
    private double[] _x = Array.Empty<double>(), _z = Array.Empty<double>(), _alpha = Array.Empty<double>();
    private double[,] _L = new double[0, 0];

    public int Count => _x.Length;

    public void Fit(IReadOnlyList<double> u, IReadOnlyList<double> z, IReadOnlyList<double> noiseVar)
    {
        int n = u.Count;
        _x = u.ToArray(); _z = z.ToArray();
        var k = new double[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                k[i, j] = Kernel(_x[i], _x[j]) + (i == j ? noiseVar[i] : 0);
        _L = Cholesky(k);                       // lower triangular
        _alpha = SolveLt(_L, _z);               // forward
        _alpha = SolveUt(Transpose(_L), _alpha); // back
    }

    public (double Mean, double Var) Predict(double u)
    {
        if (_x.Length == 0) return (0.5, 1.0);  // prior
        int n = _x.Length;
        var ks = new double[n];
        for (int i = 0; i < n; i++) ks[i] = Kernel(_x[i], u);
        double mean = 0;
        for (int i = 0; i < n; i++) mean += ks[i] * _alpha[i];
        var v = SolveLt(_L, ks);
        double var = Kernel(u, u);
        for (int i = 0; i < n; i++) var -= v[i] * v[i];
        return (mean, Math.Max(var, 0));
    }

    public static double Kernel(double a, double b)
    {
        double r = Math.Abs(a - b), l = Constants.MaternLengthScale;   // 0.25
        double s = Math.Sqrt(5.0) * r / l;
        return (1.0 + s + s * s / 3.0) * Math.Exp(-s);   // σ_f² = 1
    }

    /// <summary>In-place-style dense Cholesky A = L·Lᵀ. Throws on non-SPD (jitter is caller's job).</summary>
    internal static double[,] Cholesky(double[,] a)
    {
        int n = a.GetLength(0);
        var l = new double[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j <= i; j++)
            {
                double sum = a[i, j];
                for (int k = 0; k < j; k++) sum -= l[i, k] * l[j, k];
                if (i == j)
                {
                    if (sum <= 0) throw new InvalidOperationException($"non-SPD at {i}: {sum}");
                    l[i, i] = Math.Sqrt(sum);
                }
                else l[i, j] = sum / l[j, j];
            }
        return l;
    }

    internal static double[] SolveLt(double[,] l, double[] b)   // forward substitution
    {
        int n = b.Length; var x = new double[n];
        for (int i = 0; i < n; i++)
        {
            double s = b[i];
            for (int k = 0; k < i; k++) s -= l[i, k] * x[k];
            x[i] = s / l[i, i];
        }
        return x;
    }

    internal static double[] SolveUt(double[,] u, double[] b)   // back substitution
    {
        int n = b.Length; var x = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            double s = b[i];
            for (int k = i + 1; k < n; k++) s -= u[i, k] * x[k];
            x[i] = s / u[i, i];
        }
        return x;
    }

    static double[,] Transpose(double[,] m)
    {
        int n = m.GetLength(0); var t = new double[n, n];
        for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) t[i, j] = m[j, i];
        return t;
    }
}
```

### 7.3 Code — `src/Kinesis.Core/Optimization/EiAcquirer.cs`

```csharp
namespace Kinesis.Core.Optimization;

/// <summary>Expected Improvement over the incumbent best z⁺.</summary>
public static class EiAcquirer
{
    public static double Evaluate(double mean, double var, double best, double xi)
    {
        double sigma = Math.Sqrt(var);
        if (sigma < 1e-12) return Math.Max(0, mean - best - xi);
        double z = (mean - best - xi) / sigma;
        return (mean - best - xi) * NormCdf(z) + sigma * NormPdf(z);
    }

    public static double NormCdf(double z) => 0.5 * (1.0 + Erf(z / Math.Sqrt(2.0)));
    public static double NormPdf(double z) => Math.Exp(-0.5 * z * z) / Math.Sqrt(2.0 * Math.PI);

    /// <summary>Abramowitz–Stegun 7.1.26, |err| ≤ 1.5e-7.</summary>
    internal static double Erf(double x)
    {
        double t = 1.0 / (1.0 + 0.3275911 * Math.Abs(x));
        double y = 1.0 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592)
                         * t * Math.Exp(-x * x);
        return x >= 0 ? y : -y;
    }
}
```

### 7.4 Code — `src/Kinesis.Core/Optimization/BayesianOptimizer.cs`

```csharp
namespace Kinesis.Core.Optimization;

public sealed record OptimizationReport(
    double RecommendedCm360,
    double BandLoCm360, double BandHiCm360,   // {x : μ(x) ≥ μ* − 0.02}
    double PosteriorMean, double PosteriorStd,
    (double Cm360, double YNorm, double NoiseVar)[] Observations,
    (double Cm360, double Mean, double Std)[] Curve);   // 150 grid points, for the plot

public sealed class BayesianOptimizer
{
    private readonly double _lo, _hi;                       // cm360 bounds
    private readonly List<(double U, double Z, double NoiseVar)> _obs = new();
    private GpRegressor _gp = new();

    public BayesianOptimizer(double boundsLoCm360, double boundsHiCm360)
    {
        if (boundsHiCm360 <= boundsLoCm360) throw new ArgumentException("empty bounds");
        _lo = boundsLoCm360; _hi = boundsHiCm360;
    }

    public IReadOnlyList<(double Cm360, double YNorm, double NoiseVar)> Observations =>
        _obs.Select(o => (ToCm(o.U), o.Z, o.NoiseVar)).ToArray();

    /// <summary>D15: first two probes are the bounds anchors.</summary>
    public double SuggestNextProbeCm360()
    {
        if (_obs.Count == 0) return _lo;
        if (_obs.Count == 1) return _hi;
        Refit();
        double best = _obs.Max(o => o.Z);
        double bestU = 0.5, bestEi = double.NegativeInfinity;
        for (int i = 0; i < Constants.EiGridPoints; i++)                 // 150
        {
            double u = i / (double)(Constants.EiGridPoints - 1);
            var (m, v) = _gp.Predict(u);
            double ei = EiAcquirer.Evaluate(m, v, best, Constants.EiXi); // 0.01
            if (ei > bestEi) { bestEi = ei; bestU = u; }
        }
        return ToCm(bestU);
    }

    public void AddObservation(double cm360, double yNorm, double roundElapsedS, double sessionStartS)
    {
        double omega = Math.Exp(-Constants.FatigueLambda * (roundElapsedS - sessionStartS) / 60.0); // λ=0.05
        double noiseVar = Constants.GpNoiseFloor / omega;                  // 0.05 / ω_r
        _obs.Add((FromCm(cm360), yNorm, noiseVar));
    }

    /// <summary>ε-stop (D14): max posterior std over the grid below 0.05.</summary>
    public bool IsConverged
    {
        get
        {
            if (_obs.Count < 3) return false;
            Refit();
            double maxStd = 0;
            for (int i = 0; i < Constants.EiGridPoints; i++)
            {
                var (_, v) = _gp.Predict(i / (double)(Constants.EiGridPoints - 1));
                maxStd = Math.Max(maxStd, Math.Sqrt(v));
            }
            return maxStd < Constants.ConvergenceEps;                      // 0.05
        }
    }

    public OptimizationReport BuildReport()
    {
        Refit();
        int n = Constants.EiGridPoints;
        var curve = new (double, double, double)[n];
        double bestMean = double.NegativeInfinity, bestU = 0.5;
        for (int i = 0; i < n; i++)
        {
            double u = i / (double)(n - 1);
            var (m, v) = _gp.Predict(u);
            curve[i] = (ToCm(u), m, Math.Sqrt(v));
            if (m > bestMean) { bestMean = m; bestU = u; }
        }
        double bandLo = bestU, bandHi = bestU;
        foreach (var (x, m, _) in curve)
        {
            if (m >= bestMean - Constants.RecommendBandEps)              // 0.02
            { bandLo = Math.Min(bandLo, FromCm(x)); bandHi = Math.Max(bandHi, FromCm(x)); }
        }
        var (_, bestVar) = _gp.Predict(bestU);
        return new OptimizationReport(ToCm(bestU), ToCm(bandLo), ToCm(bandHi),
            bestMean, Math.Sqrt(bestVar),
            _obs.Select(o => (ToCm(o.U), o.Z, o.NoiseVar)).ToArray(), curve);
    }

    private void Refit()
    {
        _gp = new GpRegressor();
        _gp.Fit(_obs.Select(o => o.U).ToArray(),
                _obs.Select(o => o.Z).ToArray(),
                _obs.Select(o => o.NoiseVar).ToArray());
    }

    private double FromCm(double x) => Math.Clamp((x - _lo) / (_hi - _lo), 0, 1);
    private double ToCm(double u) => _lo + u * (_hi - _lo);
}
```

### 7.5 Bounds computation — `src/Kinesis.Core/Optimization/ProbeBounds.cs`

```csharp
public static class ProbeBounds
{
    /// <summary>A 180° turn costs cm360/2 of travel and must fit the usable pad width.</summary>
    public static (double Lo, double Hi) Compute(double padWidthCm)
    {
        double lo = Constants.BoundsMinCm360;                      // 10
        double hi = Math.Min(Constants.BoundsMaxCm360,             // 60
                             Constants.PadTurnFraction * padWidthCm); // 2.0 × pad width
        if (hi <= lo) throw new ArgumentException(
            $"mousepad {padWidthCm} cm is too small for calibration (need ≥ 5 cm usable width)");
        return (lo, hi);
    }
}
```

### 7.6 Orchestration — `src/Kinesis.Core/Session/OptimizationSession.cs`

```csharp
public sealed class OptimizationSession
{
    private readonly BayesianOptimizer _opt;
    private readonly DateTime _t0 = DateTime.UtcNow;
    private int _round;                       // 0-based, completed rounds

    public OptimizationSession(double padWidthCm)
    {
        var (lo, hi) = ProbeBounds.Compute(padWidthCm);
        _opt = new BayesianOptimizer(lo, hi);
    }

    public bool CanRunRound => _round < Constants.MaxRounds;        // 6
    public double NextProbeCm360() => _opt.SuggestNextProbeCm360();
    public void CompleteRound(RoundScore score)
    {
        _opt.AddObservation(score.Cm360, score.YNorm,
            (DateTime.UtcNow - _t0).TotalSeconds, 0);
        _round++;
    }
    public bool IsDone => !CanRunRound || _opt.IsConverged;
    public OptimizationReport Report() => _opt.BuildReport();
}
```

Loop contract consumed by the round controller (§9.5): `while (session.CanRunRound && !session.IsDone)` → probe → run round → `CompleteRound` → 10 s rest.

---

## 8. Embedded AI Coach (`Kinesis.Ai`)

### 8.1 Model assets (`models/LFM3-1.7B-ONNX/`, sidecar — D10)

Exact files (from `LiquidAI/LFM3-1.7B-ONNX`; fetch script in §10.7):

| File | Size | Purpose |
| --- | --- | --- |
| `onnx/model_q4.onnx` | 352 KB | graph |
| `onnx/model_q4.onnx_data` | 1.63 GB | int4 weights (external data, loaded automatically) |
| `tokenizer.json` | 17.9 MB | HF tokenizers BPE (128K vocab) |
| `tokenizer_config.json` | 14.6 KB | special tokens, template metadata |
| `config.json` | 1.6 KB | architecture (layer counts, cache layout) |
| `generation_config.json` | 146 B | EOS ids |
| `chat_template.jinja` | 12 KB | reference only — our renderer (§8.4) mirrors it; keep the file for cross-checking |

Startup validation (`Lfm3Engine.Load`): all files present, `model_q4.onnx_data` ≥ 1.5 GB, else D20 dialog "model incomplete — run fetch-model script or `--model-dir`". Never partially load.

### 8.2 Runtime stack (locked, D7/D8)

- Inference: `Microsoft.ML.OnnxRuntime.InferenceSession` over `model_q4.onnx`, CPU EP (threads = `Environment.ProcessorCount - 1`, `InterOpNumThreads = 1`, `GraphOptimizationLevel.ORT_ENABLE_ALL`). Manual decode loop (§8.5) drives the tri-hybrid cache contract (`past_conv.N` + `past_key_values.N.key/.value`).
- **Why not `OnnxRuntimeGenAI.Generator`** (verified 2026-11): the official ONNX export ships no `genai_config.json`; ORT-GenAI's decoder schema is KV-only (`past_key_values.%d.key`) over `llama/phi3/qwen2/qwen3/…` types and cannot express LFM3's 13 convolution caches. Evidence: HF repo trees of `LiquidAI/LFM3-1.7B-ONNX` and `onnx-community/LFM3-1.7B-ONNX`; onnxruntime.ai/docs/genai/reference/config.html.
- Tokenization: `Microsoft.ML.OnnxRuntimeGenAI.Tokenizer` over the same model folder (it reads `tokenizer.json`/`tokenizer_config.json` natively). We use **only** `Encode`/`Decode` from it; the chat template is ours (§8.4).
- Everything runs on the AI worker thread (D17: never during an active round); `CancellationToken` checked between decode steps.

### 8.3 Tokenizer wrapper — `src/Kinesis.Ai/Lfm3Tokenizer.cs`

```csharp
using Microsoft.ML.OnnxRuntimeGenAI;

namespace Kinesis.Ai;

public sealed class Lfm3Tokenizer : IDisposable
{
    private readonly Tokenizer _tok;
    public Lfm3Tokenizer(string modelDir) => _tok = new Tokenizer(modelDir);

    public int[] Encode(string text) => _tok.Encode(text).ToArray();
    public string Decode(IReadOnlyList<int> ids) => _tok.Decode(ids.ToArray());

    /// <summary>Streaming decode: O(n²) but n ≤ 512 — correct delta output, no partial-BPE bugs.</summary>
    public string DecodeDelta(IReadOnlyList<int> allIds, int alreadyShown)
        => Decode(allIds)[Math.Min(alreadyShown, Decode(allIds).Length)..];

    public void Dispose() => _tok.Dispose();
}
```

(API surface may vary slightly by package version — `Encode`/`Decode` are the stable members since 0.5; adjust signatures, keep semantics. `DecodeDelta` may cache the last decoded string instead of decoding twice.)

### 8.4 Chat template renderer — `src/Kinesis.Ai/Lfm3ChatTemplate.cs` (byte-exact)

**Grammar (hexdump-verified against `chat_template.jinja`, 2026-11-13):**

- BOS: `<|startoftext|>` (the model's `bos_token`), one per conversation, at the very start.
- Turn: `<|im_start|>` + role + `\n` + body + `<|im_end|>\n`. Roles: `system`, `user`, `assistant`, `tool`.
- System body = system prompt, then if tools are exposed: `\n\n<tools>\n` + one JSON tool-schema per line + `\n</tools>`.
- Thinking block at the start of an assistant body: `<|think|>…<|/think|>`. With thinking disabled (our config, D17) the block is the empty `<|think|></|think|>`.
- Assistant tool call (verbatim shape, `NAME`/`ARG`/`VALUE` substituted; closing tags below shown as C# `\u` escapes because raw forms break this document's tooling — the constants are byte-exact):

```
<|think|></|think|>
<function=NAME>
<parameter=ARG>
VALUE
+ "\u003c/parameter\u003e" + "\u003c/function\u003e" + "\u003c/tool_call\u003e"
```

Multiple calls in one assistant turn are separated by a blank line (`"\n\n"`).
- Tool result body (role `tool`): wrapper + payload + wrapper-close, per the constants table below.

**Tag constants — `src/Kinesis.Ai/Lfm3ChatTemplate.cs` (byte-exact; `\u` escapes compile to the true literals; verify with the hexdump appendix at the end of this section):**

```csharp
namespace Kinesis.Ai;

public sealed record ChatMessage(string Role, string Content, ToolCallRecord[]? ToolCalls = null);
public sealed record ToolCallRecord(string Name, (string Key, string Value)[] Arguments);

public static class Lfm3ChatTemplate
{
    public const string Bos        = "\u003c|startoftext|\u003e";   // model bos_token
    public const string TurnStart  = "\u003c|im_start|\u003e";
    public const string TurnEnd    = "\u003c|im_end|\u003e\n";
    public const string ThinkOpen  = "\u003cthink\u003e";
    public const string ThinkClose = "\u003c/think\u003e";
    public const string EmptyThink = ThinkOpen + ThinkClose;        // thinking disabled (D17)

    // tool-call grammar (superset of qwen3_coder)
    public const string ToolCallOpen   = "\u003ctool_call\u003e\n";
    public const string ToolCallClose  = "\u003c/tool_call\u003e";
    public const string FuncOpenFmt    = "\u003cfunction={0}\u003e\n";   // name
    public const string FuncClose      = "\u003c/function\u003e";
    public const string ParamOpenFmt   = "\u003cparameter={0}\u003e\n";  // arg name
    public const string ParamClose     = "\u003c/parameter\u003e";
    // tool-result wrapper (render_tool_response in chat_template.jinja)
    public const string ToolRespOpen   = "\u003ctool_response\u003e\n";
    public const string ToolRespClose  = "\n\u003c/tool_response\u003e";
    // tools block inside the system message
    public const string ToolsOpen      = "\u003ctools\u003e\n";
    public const string ToolsClose     = "\n\u003c/tools\u003e";

    /// <summary>Render the full conversation + generation prompt. Deterministic.</summary>
    public static string Render(IReadOnlyList<ChatMessage> messages, string systemPrompt,
                                IReadOnlyList<string> toolSchemaJsonLines)
    {
        var sb = new System.Text.StringBuilder(Bos);
        var body = systemPrompt;
        if (toolSchemaJsonLines.Count > 0)
            body += "\n\n" + ToolsOpen + string.Join("\n", toolSchemaJsonLines) + ToolsClose;
        sb.Append(TurnStart).Append("system\n").Append(body).Append(TurnEnd);

        foreach (var m in messages)
        {
            sb.Append(TurnStart).Append(m.Role).Append('\n');
            switch (m.Role)
            {
                case "assistant":
                    sb.Append(EmptyThink);                    // thinking disabled
                    sb.Append(m.Content);
                    if (m.ToolCalls is { Length: > 0 } calls)
                        for (int i = 0; i < calls.Length; i++)
                        {
                            if (i > 0) sb.Append("\n\n");
                            sb.Append(ToolCallOpen);
                            sb.AppendFormat(FuncOpenFmt, calls[i].Name);
                            foreach (var (k, v) in calls[i].Arguments)
                            {
                                sb.AppendFormat(ParamOpenFmt, k);
                                sb.Append(v).Append('\n');
                                sb.Append(ParamClose).Append('\n');
                            }
                            sb.Append(FuncClose).Append('\n');
                            sb.Append(ToolCallClose);
                        }
                    break;
                case "tool":
                    sb.Append(ToolRespOpen).Append(m.Content).Append(ToolRespClose);
                    break;
                default:
                    sb.Append(m.Content);
                    break;
            }
            sb.Append(TurnEnd);
        }

        sb.Append(TurnStart).Append("assistant\n").Append(EmptyThink);   // generation prompt
        return sb.ToString();
    }
}
```

**Hexdump cross-check (run once at implementation, compare against `chat_template.jinja` fetched from `LiquidAI/LFM3-1.7B`):** the distinct quoted tag literals in the jinja, as UTF-8 hex, are `3c7468696e6b3e` (`think-open`), `3c2f7468696e6b3e` (`think-close`), `3c746f6f6c5f726573706f6e73653e` + `3c2f746f6f6c5f726573706f6e73653e` (tool-response wrapper pair), `3c6164646974696f6e616c5f746f6f6c733e` (`additional-tools`, unused by us), `3c7c696d5f73746172747c3e` (`im-start`), `3c7c696d5f656e647c3e` (`im-end`). `ChatTemplateTests` (§11.3) pins the rendered output of a 4-message fixture to `Fixtures/expected-prompt.txt` — that test is the authoritative guard.

### 8.5 Engine — `src/Kinesis.Ai/Lfm3Engine.cs` (manual decode loop)

Ports Liquid's reference ONNX loop (docs.liquid.ai ONNX guide) to C#. Contract (from the model card): inputs `input_ids, attention_mask, position_ids, logits_to_keep, past_conv.N, past_key_values.N.key/.value`; outputs `logits, present_conv.N, present.N.key/.value`. Rename on recycle: `present_conv.N → past_conv.N`, `present.N.key → past_key_values.N.key`, `present.N.value → past_key_values.N.value`.

```csharp
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Kinesis.Ai;

public sealed class LlmResult { public required string Text; public required int[] TokenIds; public required bool EndedByEos; }

public sealed class Lfm3Engine : IDisposable
{
    private readonly InferenceSession _session;
    private readonly Lfm3Tokenizer _tokenizer;
    private readonly Dictionary<string, OrtValue> _caches = new();   // name → current cache
    private readonly List<string> _pastConv = new(), _pastKey = new(), _pastValue = new();
    private readonly int[] _eosIds;   // generation_config.json eos_token_id, int or array (§8.5)

    public Lfm3Engine(string modelDir)
    {
        var so = new SessionOptions();
        so.AppendExecutionProvider_CPU();
        so.InterOpNumThreads = 1;
        so.IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount - 1);
        so.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
        _session = new InferenceSession(Path.Combine(modelDir, "onnx", "model_q4.onnx"), so);
        _tokenizer = new Lfm3Tokenizer(modelDir);
        _eosIds = ReadEosIds(Path.Combine(modelDir, "generation_config.json"));

        foreach (var name in _session.InputMetadata.Keys)
        {
            if (name.StartsWith("past_conv.")) _pastConv.Add(name);
            else if (name.EndsWith(".key")) _pastKey.Add(name);
            else if (name.EndsWith(".value")) _pastValue.Add(name);
        }
        ResetCaches();
    }

    private void ResetCaches()
    {
        FreePins();
        foreach (var kv in _session.InputMetadata)
        {
            if (kv.Key is not ("input_ids" or "attention_mask" or "position_ids" or "logits_to_keep"))
            {
                _caches[kv.Key]?.Dispose();
                _caches[kv.Key] = CreateZero(kv.Value.ElementType, kv.Value.Dimensions, kv.Value.SymbolicDimensions);
            }
        }
    }

    // ---- cache buffer management (pinned, zero-copy OrtValues) ----
    private readonly List<GCHandle> _pins = new();
    private readonly List<Array> _pinBuffers = new();

    /// <summary>Zeroed cache tensor matching the declared input shape.
    /// Dynamic axes: a symbolic dim whose name contains "sequence" → 0 (initial EMPTY cache state);
    /// any other dynamic/symbolic dim → 1. Numeric dims are used as declared.
    /// The backing array is pinned for the lifetime of the returned OrtValue (zero-copy feed);
    /// the pin is released by FreePins() on ResetCaches/Dispose.</summary>
    private OrtValue CreateZero(TensorElementType type, int[] dims, string[]? symbolic)
    {
        var shape = new long[dims.Length];
        long total = 1;
        for (int i = 0; i < dims.Length; i++)
        {
            long d = dims[i];
            if (d <= 0)
                d = symbolic is not null && i < symbolic.Length &&
                    symbolic[i].Contains("sequence", StringComparison.OrdinalIgnoreCase) ? 0L : 1L;
            shape[i] = d;
            total *= d;                       // any 0-length sequence dim → total 0 (empty tensor)
        }

        Array buffer = type switch
        {
            TensorElementType.Float32 => new float[total],   // q4/q8 caches are fp32
            TensorElementType.Int64   => new long[total],
            TensorElementType.Float16 => new ushort[total],  // fp16 exports: IEEE binary16 bit patterns
            _ => throw new NotSupportedException($"cache element type {type} not handled")
        };
        return Adopt(buffer, shape, type);
    }

    /// <summary>Wrap an existing managed buffer as a pinned, zero-copy OrtValue tensor.</summary>
    private OrtValue Adopt(Array buffer, long[] shape, TensorElementType type)
    {
        var pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        _pins.Add(pin);
        _pinBuffers.Add(buffer);
        unsafe
        {
            // Maps to OrtCreateTensorWithDataAsOrtValue — the buffer must outlive the OrtValue.
            return OrtValue.CreateTensorValueFromMemory((void*)pin.AddrOfPinnedObject(), shape, type);
        }
        // If the installed ORT build lacks the void* overload, use the generic
        // OrtValue.CreateTensorValueFromMemory<T>(buffer.AsMemory(), shape) — the GCHandle pin
        // keeps the address stable for that overload as well.
    }

    private void FreePins()
    {
        foreach (var p in _pins) if (p.IsAllocated) p.Free();
        _pins.Clear();
        _pinBuffers.Clear();
    }

    /// <summary>Deep copy of a present* cache tensor into a new pinned OrtValue (the Run results
    /// set is disposed at the end of each decode step, so caches must own their memory).</summary>
    private OrtValue CloneTensor(OrtValue src)
    {
        var info = src.GetTensorTypeAndShapeInfo();
        long[] shape = info.Shape;
        long count = info.ElementCount;
        return info.ElementType switch
        {
            TensorElementType.Float32 => CopyTo(src.GetTensorDataAsSpan<float>(), new float[count], shape, TensorElementType.Float32),
            TensorElementType.Int64   => CopyTo(src.GetTensorDataAsSpan<long>(),   new long[count],  shape, TensorElementType.Int64),
            TensorElementType.Float16 => CopyTo(src.GetTensorDataAsSpan<ushort>(), new ushort[count], shape, TensorElementType.Float16),
            _ => throw new NotSupportedException($"cache element type {info.ElementType} not handled")
        };
    }

    private OrtValue CopyTo<T>(ReadOnlySpan<T> src, T[] dst, long[] shape, TensorElementType type) where T : unmanaged
    {
        src.CopyTo(dst);
        return Adopt(dst, shape, type);
    }

    /// <summary>One generation call. Streams via onToken callback; cancellable between steps.</summary>
    public LlmResult Generate(string prompt, int maxNewTokens, SamplingConfig cfg,
                              Action<string>? onToken = null, CancellationToken ct = default)
    {
        ResetCaches();
        var ids = _tokenizer.Encode(prompt).ToList();
        int promptLen = ids.Count;
        long total = promptLen;
        var generated = new List<int>();

        for (int step = 0; step < maxNewTokens && !ct.IsCancellationRequested; step++)
        {
            long[] feedIds = step == 0 ? ids.Select(i => (long)i).ToArray()
                                       : new[] { (long)generated[^1] };
            long feedLen = feedIds.Length;
            var feed = new Dictionary<string, OrtValue>
            {
                ["input_ids"]      = OrtValue.CreateTensorValueFromMemory(feedIds, new long[] { 1, feedLen }),
                ["attention_mask"] = OrtValue.CreateTensorValueFromMemory(
                                         Enumerable.Repeat(1L, (int)total).ToArray(), new long[] { 1, total }),
                ["position_ids"]   = OrtValue.CreateTensorValueFromMemory(
                                         step == 0 ? Enumerable.Range(0, promptLen).Select(i => (long)i).ToArray()
                                                   : new[] { total - 1 }, new long[] { 1, feedLen }),
                ["logits_to_keep"] = OrtValue.CreateTensorValueFromMemory(new long[] { 1 }, new long[] { 1 }),
            };
            foreach (var (name, val) in _caches) feed[name] = val;

            using var results = _session.Run(new RunOptions(), feed, _session.OutputNames);
            // 1) logits → next token
            var logits = results[0].GetTensorDataAsSpan<float>();   // [1, vocab] (logits_to_keep=1)
            int next = Sampler.Sample(logits, ids, generated, cfg);
            if (Array.IndexOf(_eosIds, next) >= 0) return Finish(generated, true);
            generated.Add(next); ids.Add(next); total++;
            var piece = _tokenizer.Decode(new[] { next });
            onToken?.Invoke(piece);

            // 2) recycle caches: present* → past* names
            for (int i = 1; i < results.Length; i++)
            {
                string outName = _session.OutputNames[i];
                string inName = outName.Replace("present_conv.", "past_conv.")
                                       .Replace("present.", "past_key_values.");
                _caches[inName]?.Dispose();
                _caches[inName] = CloneTensor(results[i]);   // copy out of the disposable results set
            }
        }
        return Finish(generated, false);
    }

    private LlmResult Finish(List<int> generated, bool eos)
        => new() { Text = _tokenizer.Decode(generated), TokenIds = generated.ToArray(), EndedByEos = eos };
    private static int[] ReadEosIds(string path)
    {
        // generation_config.json ships either {"eos_token_id": 2} or {"eos_token_id": [2, 3]}.
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty("eos_token_id", out var e))
            throw new InvalidDataException($"generation_config.json: eos_token_id missing ({path})");
        return e.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Number => new[] { e.GetInt32() },
            System.Text.Json.JsonValueKind.Array  => e.EnumerateArray().Select(x => x.GetInt32()).ToArray(),
            _ => throw new InvalidDataException(
                     $"generation_config.json: eos_token_id has unexpected kind {e.ValueKind} ({path})")
        };
    }
    public void Dispose()
    {
        foreach (var v in _caches.Values) v.Dispose();
        FreePins();
        _session.Dispose();
        _tokenizer.Dispose();
    }
}
```

Notes that MUST hold: caches are zero-tensors of the **declared** input shapes (from `_session.InputMetadata`), reshaped by ORT as the sequence grows — if a build of the model instead requires explicit growing cache buffers, follow the output `Dimensions` of `present*` each step and allocate to match (the Python reference feeds the outputs straight back; `Clone()` mirrors that). `logits_to_keep=1` keeps only the last position's logits. If `position_ids`/`logits_to_keep` are absent from `InputMetadata`, omit them (guard with `ContainsKey`).

`SamplingConfig` + `Sampler` (`src/Kinesis.Ai/SamplingConfig.cs`): `Temperature = 0.15`, `TopK = 50`, `RepetitionPenalty = 1.1`, `Seed` (int, default `Random.Shared.Next()`, injectable for tests). `Sampler.Sample`: (a) repetition penalty over `ids ∪ generated` — logits of seen tokens: `>0 ? /1.1 : *1.1`; (b) temperature divide; (c) keep top-50; (d) softmax; (e) sample with `Random(Seed)`. Greedy path (`Temperature=0`) used by tests.

### 8.6 Tool calling (`ToolCallParser`, `ToolDispatcher`)

**Schemas** (rendered as one JSON line each inside the `<tools>` block; `tool_to_json` shape = `{name, description, parameters}`):

```json
{"name": "adjust_sensitivity", "description": "Adjust the recommended sensitivity up or down by a percentage. Use when the player reports the current feel is too fast or too slow.", "parameters": {"type": "object", "properties": {"direction": {"type": "string", "enum": ["faster", "slower"]}, "percentage": {"type": "number"}}, "required": ["direction", "percentage"]}}
{"name": "set_specific_cm360", "description": "Set the recommended sensitivity to an exact cm/360 value.", "parameters": {"type": "object", "properties": {"cm360": {"type": "number"}}, "required": ["cm360"]}}
```

**Parser** (`ToolCallParser.Parse(string assistantText) → ToolCallRecord[]`): scan for `ToolCallOpen` … `ToolCallClose` regions; inside, read `\u003cfunction=([^\u003e\n]+)\u003e` then, until `\u003c/function\u003e`, repeated `\u003cparameter=([^\u003e\n]+)\u003e\n(.*?)\n\u003c/parameter\u003e` (singleline, non-greedy). Trim each VALUE; numbers parsed as invariant-culture doubles at dispatch. Malformed region → skip + log; never throw into the UI thread.

**Dispatcher semantics** (pure; returns new cm360 + a JSON result string):

| Call | Effect | Result JSON |
| --- | --- | --- |
| `adjust_sensitivity(faster, p)` | `cm360 *= (1 − p/100)` — *faster aim = lower cm/360* | `{"cm360": …, "note": "…"}` |
| `adjust_sensitivity(slower, p)` | `cm360 *= (1 + p/100)` | same |
| `set_specific_cm360(x)` | `cm360 = clamp(x, bounds)` | same |

All results clamp to `ProbeBounds`; each result also includes the projected per-game sensitivities (§3.4) so the model can quote them. Tool results go back to the model as a `tool` message whose content is the result JSON wrapped by `ToolRespOpen/Close` constants (the renderer does the wrapping). Max 3 tool round-trips per user turn (loop guard), then force a plain-text answer.

### 8.7 Telemetry payload — `src/Kinesis.Core/Coaching/TelemetryPayload.cs`

Serialized with `System.Text.Json` **source generation** (`JsonSerializerContext`, AOT-safe). Shape:

```json
{
  "session": { "dpi": 800, "pad_width_cm": 45, "grip": "arm", "bounds_cm360": [10, 60], "date_utc": "…" },
  "rounds": [
    { "probe_cm360": 10.0, "bsi": 41.2, "components": { "accuracy": 0.42, "norm_jerk": 0.61, "e_flick_abs": 0.22, "t_adjust_norm": 0.35 },
      "blocks": { "tracking": { "time_on_target": 0.42, "jerk_deg_s3": 8210.0 },
                  "flick": { "hits": 5, "attempts": 8, "e_flick_mean": 0.22 },
                  "dot": { "hits": 6, "attempts": 10, "t_adjust_ms_mean": 187.0 } } }
  ],
  "result": { "recommended_cm360": 27.4, "band": [25.1, 29.8], "posterior_mean": 0.63, "posterior_std": 0.03 },
  "projections": { "cs2": 1.2298, "valorant": 0.3874, "ow2": 4.0813, "cod": 4.0813 }
}
```

DTOs are flat `readonly record struct`s mirroring this exactly; JSON context = `TelemetryJsonContext`. The payload is embedded in the coach's user message as a fenced JSON block.

### 8.8 `CoachSession` (`src/Kinesis.Ai/CoachSession.cs`)

Owns the message history and the agent loop; UI never touches `Lfm3Engine` directly.

```csharp
public sealed class CoachSession
{
    private readonly Lfm3Engine _engine;
    private readonly ToolDispatcher _dispatcher;
    private readonly List<ChatMessage> _history = new();
    private readonly string _systemPrompt;                       // §8.9
    private readonly string[] _toolSchemas;                      // §8.6 JSON lines

    public CoachSession(Lfm3Engine engine, ToolDispatcher dispatcher, string systemPrompt, string[] toolSchemas) { … }

    /// <summary>Streaming user turn. Raises onToken(text) on the AI thread; result arrives via Task.</summary>
    public async Task<string> AskAsync(string userMessage, Action<string> onToken, CancellationToken ct)
    {
        _history.Add(new ChatMessage("user", userMessage));
        for (int trip = 0; trip < 3; trip++)
        {
            var prompt = Lfm3ChatTemplate.Render(_history, _systemPrompt, _toolSchemas);
            var res = await Task.Run(() => _engine.Generate(prompt, 512, SamplingConfig.Default, onToken, ct), ct);
            var calls = ToolCallParser.Parse(res.Text);
            if (calls.Length == 0) { _history.Add(new ChatMessage("assistant", res.Text)); return res.Text; }
            _history.Add(new ChatMessage("assistant", res.Text, calls));
            foreach (var call in calls)
                _history.Add(new ChatMessage("tool", _dispatcher.Execute(call)));
        }
        var final = await Task.Run(() => _engine.Generate(
            Lfm3ChatTemplate.Render(_history, _systemPrompt, _toolSchemas), 512, SamplingConfig.Default, onToken, ct), ct);
        _history.Add(new ChatMessage("assistant", final.Text));
        return final.Text;
    }
}
```

Startup: `CoachViewModel` constructs `CoachSession` **lazily on first Results navigation** (model load ≈ 2–5 s — show a spinner, never block the calibration flow).

### 8.9 System prompt (verbatim, `src/Kinesis.Ai/SystemPrompt.txt`, embedded as an Avalonia resource)

```
You are Kinesis Coach, an embedded biomechanical aiming analyst inside a sensitivity-calibration
desktop application for PC first-person-shooter players. You have completed a motor-control
calibration session for the player and received its telemetry as JSON.

Rules of conduct:
1. The invariant unit is cm/360 (centimeters of mouse travel per 360-degree turn). NEVER use any
   other sensitivity unit unless the player explicitly asks for a game's in-game number, in which
   case use the projections already present in the telemetry JSON.
2. Ground EVERY claim in the telemetry. Quote the concrete numbers (BSI and its components:
   accuracy, normalized jerk, |E_flick|, T_adjust) that support your diagnosis.
3. Interpretation guide:
   - |E_flick| > 0.15 positive: overshoot / deceleration failure -> sensitivity likely too fast
     (cm/360 too low).
   - |E_flick| < -0.10: premature stopping or range-of-motion limit -> sensitivity likely too slow.
   - High normalized jerk (> 0.6): muscular tension or over-correction; pair this with the flick
     error sign before concluding.
   - T_adjust > 350 ms: slow micro-corrections; fine-motor phase is struggling, often too slow.
4. When the player says the feel is "too fast" or "too slow", or asks to try a value, call the
   tool adjust_sensitivity or set_specific_cm360. State the resulting cm/360 after the tool
   returns, then give the projected game sensitivities from the tool result.
5. No medical, injury, or health claims. Motor-control language only. If asked about pain or
   injury, advise consulting a professional and return to aiming topics.
6. Style: terse, concrete, coach-like. Prefer 2-5 short sentences per turn. Use bullet lists only
   for multi-item breakdowns. Never speculate beyond the data; if the data is insufficient, say
   which metric is missing.
7. Conclude long diagnostics with ONE actionable next step (a specific cm/360 change or a
   re-test suggestion).
```

Model-facing message layout on first turn: system (this prompt) + `user` = "Here is the completed calibration telemetry:\n```json\n{…}\n```\nIntroduce the results in ≤60 words." Subsequent turns are free-form player chat.

---

## 9. Scenario Engine & 3D Calibration Viewport (`Kinesis.App/Rendering`, `Kinesis.Core/Session`)

### 9.1 GL integration (`src/Kinesis.App/Controls/GlViewport.cs`)

Pattern verified against kekekeks/Avalonia-Silk.NET-Example:

```csharp
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using Silk.NET.OpenGL;

public sealed class GlViewport : OpenGlControlBase
{
    private GL _gl = null!;
    private Scene _scene = null!;

    protected override void OnOpenGlInit(GlInterface gl, int fb)
    {
        _gl = GL.GetApi(gl.GetProcAddress);          // Silk.NET over Avalonia's context
        _scene = new Scene(_gl);                     // builds shaders, meshes, buffers
    }

    protected override void OnOpenGlDeinit(GlInterface gl, int fb)
    {
        _scene.Dispose();
        _gl.Dispose();
    }

    protected override unsafe void OnOpenGlRender(GlInterface gl, int fb)
    {
        _gl.Viewport(0, 0, (uint)Bounds.Width, (uint)Bounds.Height);
        _scene.Draw(ViewportState.Snapshot, (int)Bounds.Width, (int)Bounds.Height);
        Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Background); // continuous frames
    }
}
```

Context: OpenGL **3.3 core** (set `GlVersion` via `OpenGlControlBase`'s requested version; fall back to 3.2/ES3 only if init fails → D20 dialog "GPU/OpenGL 3.3 unavailable"). `Scene` owns all GL objects; created in `OnOpenGlInit`, destroyed in `OnOpenGlDeinit` — never touch GL from other threads.

### 9.2 Scene & shaders (`Rendering/{Scene,ShaderLib,SphereMesh,Camera}.cs`)

**Geometry (concrete):** `Rendering/SphereMesh.cs` generates the shared unit sphere once at GL init; `Rendering/Scene.cs` uploads it (VBO/EBO/VAO) once and reuses it for every target — zero per-frame allocations. Grid floor (line list, 1 m cells at z = −2 m) and screen-space crosshair (two 2-px lines via `glDrawArrays` in NDC, no camera) use their own small VAOs. Targets are spheres drawn at world positions derived from (azimuth, elevation, radius 8 m) via `uMvp` translation+scale; ≤12 spheres, no instancing.

**`Rendering/SphereMesh.cs` — complete, standalone (24 latitude × 24 longitude UV unit sphere, interleaved `[PX PY PZ NX NY NZ]`):**

```csharp
namespace Kinesis.App.Rendering;

/// <summary>Procedural UV-sphere geometry. Generate() runs ONCE at GL init (its array
/// allocations are the only ones in the pipeline); all drawing reuses the uploaded
/// VBO/EBO — zero per-frame allocations.</summary>
public static class SphereMesh
{
    public const int Latitudes = 24;
    public const int Longitudes = 24;

    /// <summary>Interleaved vertices [PX PY PZ NX NY NZ] + CCW triangle indices, unit sphere.
    /// Counts: (24+1)*(24+1) = 625 vertices → 3750 floats; 24*24*2 triangles → 3456 indices.</summary>
    public static (float[] Vertices, uint[] Indices) Generate()
    {
        int lat = Latitudes, lon = Longitudes;
        var vertices = new float[(lat + 1) * (lon + 1) * 6];
        int v = 0;
        for (int i = 0; i <= lat; i++)
        {
            double theta = Math.PI * i / lat;            // 0 .. π, pole to pole
            double st = Math.Sin(theta), ct = Math.Cos(theta);
            for (int j = 0; j <= lon; j++)
            {
                double phi = 2.0 * Math.PI * j / lon;    // 0 .. 2π
                float x = (float)(st * Math.Cos(phi));
                float y = (float)ct;
                float z = (float)(st * Math.Sin(phi));
                vertices[v++] = x; vertices[v++] = y; vertices[v++] = z;   // position
                vertices[v++] = x; vertices[v++] = y; vertices[v++] = z;   // normal = position (unit sphere)
            }
        }

        var indices = new uint[lat * lon * 6];
        int k = 0, stride = lon + 1;
        for (int i = 0; i < lat; i++)
        {
            for (int j = 0; j < lon; j++)
            {
                uint a = (uint)(i * stride + j);
                uint b = a + (uint)stride;
                indices[k++] = a;     indices[k++] = b;     indices[k++] = a + 1;
                indices[k++] = a + 1; indices[k++] = b;     indices[k++] = b + 1;
            }
        }
        return (vertices, indices);
    }
}
```

**`Rendering/Scene.cs` — Silk.NET buffer setup (call once from the constructor/`OnOpenGlInit`):**

```csharp
using Silk.NET.OpenGL;

namespace Kinesis.App.Rendering;

public sealed partial class Scene : IDisposable
{
    private readonly GL _gl;
    private uint _sphereVao, _sphereVbo, _sphereEbo;
    private int _sphereIndexCount;

    public Scene(GL gl) { _gl = gl; CreateSphere(); /* + grid & crosshair VAOs, shaders */ }

    private unsafe void CreateSphere()
    {
        var (vertices, indices) = SphereMesh.Generate();
        _sphereIndexCount = indices.Length;

        _sphereVao = _gl.GenVertexArray();
        _gl.BindVertexArray(_sphereVao);

        _sphereVbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _sphereVbo);
        fixed (float* vp = vertices)
            _gl.BufferData(BufferTargetARB.ArrayBuffer,
                           (nuint)(vertices.Length * sizeof(float)), vp, BufferUsageARB.StaticDraw);

        _sphereEbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _sphereEbo);
        fixed (uint* ip = indices)
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer,
                           (nuint)(indices.Length * sizeof(uint)), ip, BufferUsageARB.StaticDraw);

        const int stride = 6 * sizeof(float);
        _gl.EnableVertexAttribArray(0);                                       // location 0: position
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, stride, (void*)0);
        _gl.EnableVertexAttribArray(1);                                       // location 1: normal
        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, stride, (void*)(3 * sizeof(float)));

        _gl.BindVertexArray(0);
    }

    private unsafe void DrawSphere()
    {
        _gl.BindVertexArray(_sphereVao);
        _gl.DrawElements(PrimitiveType.Triangles, (uint)_sphereIndexCount, DrawElementsType.UnsignedInt, null);
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(_sphereVbo);
        _gl.DeleteBuffer(_sphereEbo);
        _gl.DeleteVertexArray(_sphereVao);
    }
}
```

**`ShaderLib` sources (GLSL 330 core — copy verbatim; targets use the normal attribute so locations 0 and 1 are both active):**

```glsl
// target.vert
#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec3 aNormal;
uniform mat4 uMvp;
out vec3 vNormal;
void main() {
    vNormal = aNormal;                 // spheres are translated/scaled, never rotated
    gl_Position = uMvp * vec4(aPos, 1.0);
}

// target.frag
#version 330 core
in vec3 vNormal;
out vec4 FragColor;
uniform vec3 uColor;
void main() {
    float ndl = max(dot(normalize(vNormal), normalize(vec3(0.35, 0.75, 0.55))), 0.0);
    FragColor = vec4(uColor * (0.55 + 0.45 * ndl), 1.0);
}

// grid.vert
#version 330 core
layout(location=0) in vec3 aPos;
uniform mat4 uMvp;
void main() { gl_Position = uMvp * vec4(aPos, 1.0); }

// grid.frag
#version 330 core
out vec4 FragColor;
uniform vec3 uColor;
void main() { FragColor = vec4(uColor, 1.0); }
```

Camera matrices: perspective (fovY = 103° horizontal-equivalent — compute `fovY = 2*atan(tan(103°/2) * h/w)`… simpler: fixed `fovY = 73.7°` vertical for a 16:9 viewport giving ≈103° horizontal; recompute from aspect: `fovY = 2*atan(tan(51.5° * π/180) * (h/w))`), near 0.1, far 100. View = `RotateX(-pitch) * RotateY(-yaw) * Translate(0,0,0)`. Matrices built by a tiny `Mat4` helper (no math library).

Colors from the theme palette via uniforms: targets `--c1`, hit flash `--success`, grid `--border` at 40% alpha.

### 9.3 Camera state & probe-sensitivity link (`ViewportState`)

```csharp
public sealed class ViewportState
{
    private static ViewportState _current;                 // swapped via Interlocked.Exchange
    public static ViewportState Snapshot => Volatile.Read(ref _current);

    public double YawDeg, PitchDeg;                        // accumulated from raw input
    public long SampleSeq;                                 // matches SampleRing.Sequence at capture
    public static void OnSample(in RawMotionSample s, double dpi, double cm360, ViewportState live)
    {
        double radPerCount = PhysicalTurnDistance.FromCm(cm360).RadPerCount(dpi);
        live.YawDeg += s.DxCounts * radPerCount * (180.0 / Math.PI);
        live.PitchDeg = Math.Clamp(live.PitchDeg - s.DyCounts * radPerCount * (180.0 / Math.PI), -89, 89);
        // wrap yaw into [0,360)
    }
}
```

The viewport turn rate is exactly the probe sensitivity: one pad-length of `cm360` cm of travel = 360° of yaw (this is the invariant the scenario metrics rely on). `SampleSeq` lets the extractor correlate the rendered path with `SampleRing` windows.

### 9.4 Scenarios — three blocks per round (40 s total)

Attempt records (`Kinematics/Attempt.cs`, §5.4) are emitted by these blocks; all angles are **view-space degrees**; a hit = crosshair center within `TargetRadiusDeg` of target center at the click timestamp (evaluated against the interpolated yaw/pitch at click time).

**Block A — Dynamic Sphere Tracking (15 s).** One sphere, radius 3.5°, on a Lissajous orbit around the current view:

$$
\theta_x(t) = 14^\circ \sin(2\pi f_1 t),\quad \theta_y(t) = 9^\circ \sin(2\pi f_2 t + \varphi),\quad f_1 = 0.10\,\text{Hz},\ f_2 = 0.07\,\text{Hz},\ \varphi = \pi/3
$$

Angular distance $\delta(t)$ between crosshair and sphere center → `time_on_target = fraction of frames with δ < 3.5° + 1° slack`. Accuracy for the block = time-on-target. Metrics: jerk (D21, from view path), mean speed. No clicks.

**Block B — Ballistic Wide Flicks (15 s).** Targets spawn sequentially at azimuth $\pm U(90°, 120°)$ (alternating side), elevation $U(-8°, +8°)$, radius 3°. Each target lives 2.4 s; a raw-button-1 edge within its life and inside its radius = hit (then next target immediately). Per attempt: `E_flick` (§5.3) and `T_adjust` recorded. Accuracy = hits / attempts (attempt = any target that appeared; missing the window = miss).

**Block C — Micro-Dot Clicking (10 s).** 10 targets placed uniformly in a $10°\times10°$ window re-centered on the current view per target (min separation 2.5°), radius 0.8°, life 1.8 s. Metrics: `T_adjust`, micro-jerk on the dot phase (last 300 ms before click). Accuracy = hits / attempts.

Scenario math is **pure** (`ScenarioState` structs updated by `Tick(double nowS)` with the timestamp passed in — D19); the renderer only reads the resulting target list. RNG seeded per round (`Seed = roundIndex * 7919 + sessionSalt`) so scenario geometry is reproducible in tests.

### 9.5 Round controller (`src/Kinesis.Core/Session/RoundController.cs`)

State machine (times relative to round start; total active = 40 s):

| State | Duration | Entry actions | Exit actions |
| --- | --- | --- | --- |
| `Idle` | — | wait for Start | — |
| `RoundIntro` | 3.0 s | HUD countdown "3…2…1", probe assigned **internally only** (D16); `CursorHelper.LockAndHide` (§4.6) | start `SampleRing` window |
| `BlockTracking` | 15.0 s | spawn sphere (§9.4 A) | seal window A |
| `BlockFlick` | 15.0 s | spawn flick target loop (§9.4 B) | seal window B |
| `BlockDot` | 10.0 s | spawn dot loop (§9.4 C) | seal window C |
| `RoundScoring` | ≤ 2 s (async) | `CursorHelper.UnlockAndShow` (§4.6); `Trajectory` resample + `KinematicFeatureExtractor` + `BsiScorer` → `RoundScore`; `OptimizationSession.CompleteRound` | decide next state |
| `Rest` | 10.0 s | "Rest — round N of M" HUD | — |
| `SessionComplete` | — | `OptimizationSession.Report()` → navigate to Results | — |
| `Aborted` | — | (Esc) — `CursorHelper.UnlockAndShow` (§4.6); seal open windows, mark `partial=true`, still produce Results | — |

Transitions (exact): `Idle → RoundIntro` on Start; `RoundIntro → BlockTracking`; blocks chained; `RoundScoring → Rest` if `session.CanRunRound && !session.IsDone`, else `SessionComplete`. The **probe for round r+1** = `session.NextProbeCm360()` computed in `RoundScoring`, applied to `ViewportState` rotation scale at `RoundIntro` entry. Click edge detection: `MouseButtons.Left` rising edge sampled per render tick against `IRawInputService.CurrentButtons`.

Fatigue/decay timestamps: `RoundScore` carries `t_r` = round start seconds since session start (D18 noise weighting is applied inside `BayesianOptimizer.AddObservation`).

### 9.6 HUD & blinding (D16)

Active-round HUD (Avalonia overlay over the GL control): centered countdown/timer bar, "Round N / ≤6" chip, live block accuracy (rolling, e.g. "On target 62%"), thin block-progress bar. **Never** show: probe cm360, bounds, GP state, or any derived value. Rest screen: same minus live metrics. Esc → confirmation "Abort and view results?".

### 9.7 Dev overlay (`--dev` launch flag only)

Toggle F12. Shows: FPS, input samples/s, dropped samples, current probe value (redacted unless overlay active — overlay implies development context), **measured cm360 of the last ≥10 cm swipe** = `360 * swipeCm / yawDeltaDeg` — this is the §4.5 ruler verification tool and doubles as the user's DPI sanity check. Disabled entirely in Release builds without `--dev` (guard with `#if DEBUG || KINESIS_DEV`).

---

## 10. Cross-Platform Packaging

### 10.1 `Program.cs` / CLI (`src/Kinesis.App/Program.cs`)

Args (parse with a 10-line loop, no library): `--model-dir <path>` (default `<appdir>/models/LFM3-1.7B-ONNX`), `--dev` (enables §9.7), `--skip-ai` (Results without coach — required for machines that cannot host the model; coach area shows "AI disabled"). Startup order: parse args → `PlatformInputFactory.Create()` → `Start()` (failures → D20 dialog) → `AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args)`. Model validation and load are **lazy** (first Results navigation, §8.8).

### 10.2 Windows (primary ship target)

```powershell
dotnet publish src/Kinesis.App/Kinesis.App.csproj -r win-x64 -c Release `
  -p:PublishSingleFile=true -p:SelfContained=true -p:PublishTrimmed=true `
  -p:TrimMode=partial -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -o dist/win-x64
# output: dist/win-x64/Kinesis.App.exe (single file) + models/ sidecar via scripts/fetch-model.ps1
```

Verify the trim didn't break Avalonia: run the published exe, walk every screen once (trim damage shows as missing-type crashes at first navigation). Signing (optional): `signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 …`. `app.manifest` declares `PerMonitorV2` DPI awareness and `longPathAware`.

### 10.3 Linux (portable binary)

```bash
dotnet publish src/Kinesis.App/Kinesis.App.csproj -r linux-x64 -c Release \
  -p:PublishSingleFile=true -p:SelfContained=true -p:PublishTrimmed=true \
  -p:TrimMode=partial -p:IncludeNativeLibrariesForSelfExtract=true -o dist/linux-x64
```

Ship `packaging/linux/99-kinesis-input.rules` + `install-udev.sh` (§4.3) alongside; the app's D20 dialog points at the script when `EACCES` is detected.

### 10.4 AppImage

`packaging/linux/build-appimage.sh`:

```bash
#!/usr/bin/env bash -eu
VERSION=${1:-0.1.0}
APP=Kinesis
DIR=AppDir
rm -rf "$DIR" && mkdir -p "$DIR/usr/bin" "$DIR/usr/share/applications" "$DIR/usr/share/icons/hicolor/256x256/apps"
cp dist/linux-x64/Kinesis.App "$DIR/usr/bin/"
cp packaging/linux/kinesis.desktop "$DIR/usr/share/applications/"
cp src/Kinesis.App/Assets/icon.png "$DIR/usr/share/icons/hicolor/256x256/apps/kinesis.png"
cp packaging/linux/AppRun "$DIR/AppRun" && chmod +x "$DIR/AppRun"
cp packaging/linux/99-kinesis-input.rules packaging/linux/install-udev.sh "$DIR/usr/bin/"
# appimagetool from https://appimage.github.io/appimagetool/
./appimagetool-x86_64.AppImage "$DIR" "Kinesis-$VERSION-x86_64.AppImage"
```

`AppRun`: `HERE="$(dirname "$(readlink -f "$0")")"; exec "$HERE/usr/bin/Kinesis.App" "$@"`.
`kinesis.desktop`: `Type=Application`, `Name=Kinesis`, `Exec=Kinesis.App`, `Icon=kinesis`, `Categories=Utility;Game;`. Model folder sits next to the AppImage (AppImage mounts read-only); the fetch script installs to `~/.local/share/kinesis/models/` and `Program.cs` must check that path as second default (add to §10.1 default chain: `--model-dir` > `$XDG_DATA_HOME/kinesis/models/LFM3-1.7B-ONNX` > `<appdir>/models/LFM3-1.7B-ONNX`).

### 10.5 macOS

`packaging/macos/build-dmg.sh` steps: create `Kinesis.app/Contents/{MacOS,Resources}`; copy `osx-arm64`/`osx-x64` publish output (`dotnet publish -r osx-arm64` with the same flags as §10.2) into `MacOS/`; `Info.plist` (shipped) with `CFBundleIdentifier = com.kinesis.app`, `NSHighResolutionCapable = true`, `NSInputMonitoringUsageDescription = "Kinesis reads raw mouse motion to calibrate aiming sensitivity."`; icon in `Resources`; then:

```bash
codesign --force --options runtime --entitlements packaging/macos/entitlements.plist \
  --sign "Developer ID Application: …" Kinesis.app
ditto -c -k --keepParent Kinesis.app Kinesis.zip
xcrun notarytool submit Kinesis.zip --keychain-profile kinesis --wait
xcrun stapler staple Kinesis.app
hdiutil create -volname Kinesis -srcfolder Kinesis.app -ov -format UDZO Kinesis.dmg
```

First-launch flow: if `IOHIDManagerOpen` fails (§4.4) → D20 dialog with the Input Monitoring path and a "Re-check" button; no silent degraded mode without the banner (D12).

### 10.6 Native AOT (stretch, not v1)

`-p:PublishAot=true` plus `<IsAotCompatible>true</IsAotCompatible>` in Core/Ai. Known blockers to resolve before claiming it works: Avalonia's AOT profile, Silk.NET function-pointer marshalling, ORT-GenAI native tokenizer interop, `System.Text.Json` source-gen already required (§8.7). Track in `docs/DEVLOG.md`; do not block M5 on it (D11).

### 10.7 Model fetch scripts (`scripts/fetch-model.sh` / `.ps1`)

```bash
#!/usr/bin/env bash -eu
DEST=${1:-models/LFM3-1.7B-ONNX}
mkdir -p "$DEST/onnx"
BASE=https://huggingface.co/LiquidAI/LFM3-1.7B-ONNX/resolve/main
for f in config.json generation_config.json tokenizer.json tokenizer_config.json chat_template.jinja; do
  curl -L "$BASE/$f" -o "$DEST/$f"
done
curl -L "$BASE/onnx/model_q4.onnx"      -o "$DEST/onnx/model_q4.onnx"
curl -L "$BASE/onnx/model_q4.onnx_data" -o "$DEST/onnx/model_q4.onnx_data"   # 1.63 GB
```

(PowerShell variant uses `Invoke-WebRequest`.) After download: assert `model_q4.onnx_data` ≥ 1.5 GB and `tokenizer.json` ≥ 17 MB (the §8.1 startup validation re-checks). Document LFM Open License v1.0 in `README.md` — the model is redistributed under it, not MIT.

---

## 11. Testing & Acceptance

### 11.1 Unit test matrix (`Kinesis.Core.Tests`, xunit — exact assertions)

| Test | Input | Assert |
| --- | --- | --- |
| `Projection_Cs2_WorkedExample` | DPI 800, cm360 30 | `ToGameSens(Cs2Source)` = `1.7318` ± 1e-4 |
| `Projection_Valorant_WorkedExample` | same | `0.5443` ± 1e-4 |
| `Projection_Ow2_WorkedExample` | same | `5.7727` ± 1e-4 |
| `Projection_RoundTrip` | sens→cm360→sens for all 4 engines × (DPI 400/800/1600) × cm360 (12/30/55) | identity ± 1e-9 |
| `DegPerCount_800_30` | | `0.0381` deg/count ± 1e-4 |
| `Trajectory_ConstantVelocity_ZeroJerk` | 1000 samples, dx=4 counts/1 ms, DPI 800 | mean |jerk| < 1e-6; v = 12.7 cm/s ± 1e-9 |
| `Trajectory_Resample_GridSpacing` | irregular timestamps (0.7–2.3 ms) | grid length & 1 ms spacing exact |
| `EFlick_Overshoot02` | synthetic: peak = 1.2 × target | `0.2` ± 1e-9 |
| `EFlick_Clamps` | peak = 5 × target | `2.0` (hi clip) |
| `TAdjust_SettledClick` | ω < 5°/s from t=1.0 s, click 1.25 s | `250` ms ± 2 ms |
| `TAdjust_NoSettle` | ω never drops | `0` |
| `Bsi_Perfect` | A=1,N=0,E=0,T=0 | `YNorm = 1.0` |
| `Bsi_Midpoint` | A=N=E=T=0.5 | `0.5` ± 1e-12 |
| `Bsi_ClampsNegative` | A=0,N=1,E=1,T=1 | `0.0` |
| `Kernel_Diagonal` | k(0.3, 0.3) | `1.0` (σ_f²=1) |
| `Kernel_Symmetric` | grid of pairs | k(a,b)=k(b,a) ± 1e-15 |
| `Cholesky_SPD3` | known 3×3 SPD | L·Lᵀ = A ± 1e-12 |
| `Cholesky_NonSPD_Throws` | [[1,2],[2,1]] | `InvalidOperationException` |
| `Gp_InterpolatesNoiseless` | 5 pts, noiseVar=0 (jitter 1e-10) | μ(xᵢ)=yᵢ ± 1e-6 |
| `Ei_AtOneSigma` | μ−y⁺−ξ = σ | EI = σ·φ(0) ± 1e-6 |
| `Ei_ZeroSigma` | var=0 | `max(0, μ−y⁺−ξ)` |
| `Optimizer_AnchorOrder` | 0 observations | probe 1 = lo, probe 2 = hi |
| `Optimizer_SyntheticLandscape` | y(x)=clamp(0.9−0.005(x−28)²,0,1), seeded noise 0.02, ≤6 rounds | final x* ∈ [26, 30]; report band contains 28 |
| `Optimizer_PadBound` | pad 30 cm | hi = min(60, 60) = 60; pad 12 cm → hi = 24 |
| `RoundController_Timeline` | scripted clock | states in order; windows sealed at 3/18/33/43 s ± 1 tick |
| `ToolDispatcher_Faster` | (faster, 10) at cm360 30 | `27.0` |
| `ToolDispatcher_Slower` | (slower, 10) at 30 | `33.0` |
| `ToolDispatcher_Clamp` | set_specific_cm360 999 | = bounds hi |

### 11.2 Synthetic fixtures

`tests/Kinesis.Core.Tests/Fixtures/`: generator `SyntheticRound.Generate(cm360, dpi, seed)` producing `RawMotionSample[]` + `Attempt[]` from a parametric aiming model (ballistic minimum-jerk trajectory + tremor sine + noise) — used by `Optimizer_SyntheticLandscape` and `RoundController` tests so the whole pipeline (trajectory → BSI → GP) is exercised without hardware.

### 11.3 AI tests (`Kinesis.Ai.Tests` — no model in CI)

| Test | Assert |
| --- | --- |
| `ChatTemplate_Golden` | 4-message fixture + 2 tool schemas renders **byte-identical** to `Fixtures/expected-prompt.txt` (generate the file once from the §8.4 renderer after manual review against `chat_template.jinja`; commit it) |
| `ChatTemplate_NoTools` | no `<tools>` block appears when schema list empty |
| `ToolCallParser_TwoCalls` | fixture assistant text → 2 records with exact names/args |
| `ToolCallParser_Malformed` | truncated call → empty array, no throw |
| `Telemetry_RoundTrip` | DTO → JSON → DTO equality |

Engine smoke test (local only, not CI): `KINESIS_MODEL_DIR` env set → generate 32 tokens from a fixed prompt with `Temperature=0` twice → outputs identical (greedy determinism).

### 11.4 Integration (`Kinesis.Core.Tests`, `FakeRawInputService`)

End-to-end without hardware: fake service replays `SyntheticRound` samples at 1 kHz virtual time; `RoundController` + `OptimizationSession` run 6 virtual rounds against the synthetic landscape of §11.1; assert `IsDone` state, report structure, and that round weights (D18) increased observation noise monotonically in `Report().Observations`.

### 11.5 Acceptance scenarios (manual, run before calling M5 done)

1. **Full calibration (Windows, real 1000 Hz mouse):** complete ≤6 rounds end-to-end in < 10 min; HUD shows no sensitivity values during rounds (D16); Results shows GP curve μ±σ with 2–6 observations, kinematic breakdown per round, four projection cards whose values match `gameSens` recomputed by hand (§3.2) for the entered DPI.
2. **Coach tool call:** on Results, type "make it about 5% slower"; the model invokes `adjust_sensitivity`; the recommended cm360 updates; the reply quotes the new projections from the tool result.
3. **Packaging smoke:** `dotnet publish` per §10.2 on a clean Windows VM; run the single exe with the model sidecar present → full flow works; move the model folder away → D20 remediation dialog appears (never a crash).

---

## 12. Milestones (ordered task lists — every task names its files)

### M1 — Platform input & core domain math
1. `Directory.Build.props`, `global.json`, `Kinesis.sln`, project skeletons (§2.1/§2.2). ✔ `dotnet build`.
2. `Config/Constants.cs` (§13.6), `Units/PhysicalTurnDistance.cs` (§3.3), `Projection/GameProjection.cs` (§3.4).
3. `Input/IRawInputService.cs` + `SampleRing.cs` (§4.1).
4. `Kinesis.Input.Windows` (§4.2) — and `Kinesis.Input.Linux` (§4.3), `Kinesis.Input.Mac` (§4.4) per available hardware; `PlatformInputFactory`.
5. `Kinesis.Platform/CursorHelper.cs` (§4.6): `LockAndHide`/`UnlockAndShow` wired to `RoundController` entry/exit events (§9.5); fallback HUD warning when the OS call fails.
6. `FakeRawInputService` + projection/units tests (§11.1). ✔ DoD: `dotnet test` green; §4.5 ruler check passes on the target machine (record the number in DEVLOG).

### M2 — 3D viewport & scenarios
1. `Controls/GlViewport.cs` (§9.1), `Rendering/{Scene,ShaderLib,Camera,SphereMesh,Mat4}.cs` (§9.2) — rotating test sphere first.
2. `Session/ViewportState.cs` (§9.3): raw samples move the camera at the probe scale; `--dev` overlay (§9.7).
3. `Session/ScenarioState.cs` + `Kinematics/Attempt.cs` (§9.4): three blocks, seeded RNG, hit detection.
4. `Session/RoundController.cs` (§9.5) driving blocks on a real clock; HUD overlay (§9.6).
✔ DoD: launch app → 6 blind rounds run against a scripted probe list; windows seal; Esc aborts cleanly.

### M3 — Kinematics, scoring, optimizer
1. `Kinematics/Trajectory.cs` + `KinematicFeatureExtractor.cs` (§5) with tests.
2. `Scoring/BsiScorer.cs` (§6) with tests.
3. `Optimization/{GpRegressor,EiAcquirer,BayesianOptimizer,ProbeBounds}.cs` (§7) with tests incl. `Optimizer_SyntheticLandscape`.
4. `Session/OptimizationSession.cs` (§7.6); wire `RoundController.RoundScoring` → real scoring.
✔ DoD: integration test §11.4 green; a live run converges to a plausible cm360 (sanity: within bounds band).

### M4 — Embedded AI coach
1. `Kinesis.Ai.csproj` + `Lfm3Tokenizer.cs` (§8.3); fetch model (§10.7) on the dev machine.
2. `Lfm3Engine.cs` (§8.5): smoke-generate 32 tokens twice (greedy) → identical.
3. `Lfm3ChatTemplate.cs` (§8.4) + golden test; `ToolCallParser.cs` + `ToolDispatcher.cs` (§8.6) + tests; `SamplingConfig.cs`.
4. `Coaching/TelemetryPayload.cs` (§8.7) + `CoachSession.cs` (§8.8) + `SystemPrompt.txt` (§8.9).
✔ DoD: a scripted telemetry JSON yields a coherent coaching intro; "5% slower" triggers the tool round-trip in a console harness (`--skip-ai` counterpart verified too).

### M5 — UI/UX, results, packaging
1. Shell & navigation (`App.axaml` with explicit `DataTemplate`s VM→View — trim-safe, **no reflection `ViewLocator`**; `ViewModels`): Hardware Profile → Session → Results. Compiled bindings everywhere (D3).
2. **HardwareProfileView** — form: DPI (100–6400), mousepad width cm (≥5), grip enum (wrist/claw/arm); validation summaries; "Start calibration" command.
3. **CalibrationSessionView** — `GlViewport` + HUD overlay (§9.6), start/abort commands.
4. **ResultsDashboardView**:
   - *GP plot*: **full implementation in M5-A** (`Controls/GpPlotControl.cs`): 95% confidence band $\mu \pm 1.96\sigma$ (`#2800E5FF`), posterior mean polyline (`#FF00E5FF`), observation scatter ellipses, dashed x* guideline, normalized `[u,z]` → pixel mapping via pure `MapX/MapY`.
   - *Kinematic breakdown*: per-round table (BSI, accuracy, norm jerk, |E_flick|, T_adjust) — Markdown-plain `DataGrid`/`ItemsControl`.
   - *Projection cards*: CS2 / Valorant / OW2 / CoD sens (4 dp) + DPI echo + copy button (`TopLevel.Clipboard.SetTextAsync`).
   - *Coach terminal* (`CoachTerminalView`): streaming `TextBlock` accumulation from `onToken` (marshalled via `Dispatcher.UIThread.Post`), input box, send command → `CoachViewModel.AskAsync`; tool-call events rendered as a dim "⚙ adjust_sensitivity(faster, 5)" line.
5. Dark theme (Fluent): `RequestedThemeVariant = Dark`; accent `#7C9FFF`; card surfaces `#1C1F26`; no light-mode work.
6. Packaging: §10.2–§10.5 + `README.md` (build, run, fetch-model, udev/TCC notes, license).
✔ DoD: §11.5 acceptance scenarios all pass; `dist/` artifacts documented in DEVLOG.

### M5-A — UI implementation code (complete; delivers M5 items 1–4)

Everything below is production code, not pseudocode. Files land in `src/Kinesis.App/` as named.

#### M5-A.1 `Controls/GpPlotControl.cs` (complete)

```csharp
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Kinesis.App.Controls;

/// <summary>One grid point of the GP posterior: u = normalized cm/360 in [0,1], z = BSI/100.</summary>
public sealed record GPPoint(double U, double Mean, double Std);

/// <summary>One tested trial: u = normalized probe, z = observed BSI/100.</summary>
public sealed record ObservationPoint(double U, double YNorm);

public sealed class GpPlotControl : Control
{
    // ---- bindable state ----
    public static readonly StyledProperty<IReadOnlyList<GPPoint>?> GridPredictionsProperty =
        AvaloniaProperty.Register<GpPlotControl, IReadOnlyList<GPPoint>?>(nameof(GridPredictions));

    public static readonly StyledProperty<IReadOnlyList<ObservationPoint>?> ObservationsProperty =
        AvaloniaProperty.Register<GpPlotControl, IReadOnlyList<ObservationPoint>?>(nameof(Observations));

    public IReadOnlyList<GPPoint>? GridPredictions
    {
        get => GetValue(GridPredictionsProperty);
        set => SetValue(GridPredictionsProperty, value);
    }

    public IReadOnlyList<ObservationPoint>? Observations
    {
        get => GetValue(ObservationsProperty);
        set => SetValue(ObservationsProperty, value);
    }

    static GpPlotControl() =>
        AffectsRender<GpPlotControl>(GridPredictionsProperty, ObservationsProperty);

    // ---- layout constants (logical px) ----
    private const double PadL = 44, PadR = 16, PadT = 16, PadB = 28;

    // ---- static brushes/pens (allocated once) ----
    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.Parse("#1C1F26"));
    private static readonly IPen BorderPen = new Pen(new SolidColorBrush(Color.Parse("#2A2E37")), 1);
    private static readonly IBrush BandBrush = new SolidColorBrush(Color.Parse("#2800E5FF")); // 95% band, 16% cyan
    private static readonly IPen MeanPen = new Pen(new SolidColorBrush(Color.Parse("#FF00E5FF")), 2);
    private static readonly IPen GuidePen = new Pen(new SolidColorBrush(Color.Parse("#88FFFFFF")), 1)
        { DashStyle = new DashStyle(new double[] { 4, 4 }, 0) };
    private static readonly IBrush DotBrush = new SolidColorBrush(Color.Parse("#FFB03A"));
    private static readonly IPen DotPen = new Pen(new SolidColorBrush(Color.Parse("#1C1F26")), 1);
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.Parse("#9AA3B2"));

    // ---- coordinate mapping: normalized [u, z] → control pixels (W, H) ----
    private double MapX(double u) => PadL + u * (Bounds.Width - PadL - PadR);
    private double MapY(double z) => Bounds.Height - PadB - z * (Bounds.Height - PadT - PadB);

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;

        // 1) background + border
        context.DrawRectangle(BackgroundBrush, BorderPen, new Rect(0, 0, w, h), 6, 6);

        var preds = GridPredictions;
        if (preds is null || preds.Count < 2) { DrawText(context, "waiting for optimizer…", PadL, h / 2); return; }

        // 2) 95% confidence band — closed StreamGeometry: μ+1.96σ forward, μ−1.96σ backward
        var band = new StreamGeometry();
        using (var gc = band.Open())
        {
            var f = preds[0];
            gc.BeginFigure(new Point(MapX(f.U), MapY(Clamp01(f.Mean + 1.96 * f.Std))), true);
            for (int i = 1; i < preds.Count; i++)
            {
                var p = preds[i];
                gc.LineTo(new Point(MapX(p.U), MapY(Clamp01(p.Mean + 1.96 * p.Std))));
            }
            for (int i = preds.Count - 1; i >= 0; i--)
            {
                var p = preds[i];
                gc.LineTo(new Point(MapX(p.U), MapY(Clamp01(p.Mean - 1.96 * p.Std))));
            }
            gc.EndFigure(true);   // closed
        }
        context.DrawGeometry(BandBrush, null, band);

        // 3) posterior mean curve μ(u)
        var mean = new StreamGeometry();
        using (var gc = mean.Open())
        {
            gc.BeginFigure(new Point(MapX(preds[0].U), MapY(Clamp01(preds[0].Mean))), false);
            for (int i = 1; i < preds.Count; i++)
                gc.LineTo(new Point(MapX(preds[i].U), MapY(Clamp01(preds[i].Mean))));
            gc.EndFigure(false);
        }
        context.DrawGeometry(null, MeanPen, mean);

        // 4) observed trials as scatter ellipses
        if (Observations is { } obs)
            foreach (var o in obs)
                context.DrawEllipse(DotBrush, DotPen, new Point(MapX(o.U), MapY(Clamp01(o.YNorm))), 3.5, 3.5);

        // 5) dashed vertical guideline at x* = argmax μ(u)
        int best = 0;
        for (int i = 1; i < preds.Count; i++)
            if (preds[i].Mean > preds[best].Mean) best = i;
        double bx = MapX(preds[best].U);
        context.DrawLine(GuidePen, new Point(bx, PadT), new Point(bx, h - PadB));
        DrawText(context, "x*", bx + 4, PadT + 2);

        // 6) axis labels
        DrawText(context, "cm/360 →", PadL, h - PadB + 8);
        DrawText(context, "BSI/100", 4, PadT);
    }

    private static void DrawText(DrawingContext context, string text, double x, double y)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                   Typeface.Default, 11, LabelBrush);
        context.DrawText(ft, new Point(x, y));
    }

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
}
```

#### M5-A.2 `Views/HardwareProfileView.axaml` + `.axaml.cs` (complete)

```xml
<UserControl x:Class="Kinesis.App.Views.HardwareProfileView"
             xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Kinesis.App.ViewModels"
             x:DataType="vm:HardwareProfileViewModel"
             x:CompileBindings="True">
  <StackPanel Margin="48" Spacing="14" MaxWidth="420" HorizontalAlignment="Left">
    <TextBlock Text="Hardware Profile" FontSize="28" FontWeight="SemiBold"/>
    <TextBlock Text="Kinesis measures your optimal cm/360 — physical turn distance. Enter your hardware exactly."
               TextWrapping="Wrap" Opacity="0.7"/>

    <TextBlock Text="Mouse DPI"/>
    <NumericUpDown Value="{Binding Dpi}" Minimum="100" Maximum="6400" Increment="50" FormatString="0"/>

    <TextBlock Text="Mousepad usable width (cm)"/>
    <NumericUpDown Value="{Binding PadWidthCm}" Minimum="5" Maximum="120" Increment="1" FormatString="0.0"/>

    <TextBlock Text="Grip style"/>
    <ComboBox ItemsSource="{Binding GripStyles}" SelectedItem="{Binding Grip}" HorizontalAlignment="Stretch"/>

    <TextBlock Text="{Binding ValidationError}" Foreground="#FF6B6B" TextWrapping="Wrap"/>

    <Button Content="Start calibration" Command="{Binding StartCalibrationCommand}"
            HorizontalAlignment="Stretch" HorizontalContentAlignment="Center" Padding="12"/>
  </StackPanel>
</UserControl>
```

```csharp
using Avalonia.Controls;

namespace Kinesis.App.Views;

public partial class HardwareProfileView : UserControl
{
    public HardwareProfileView() => InitializeComponent();
}
```

#### M5-A.3 `ViewModels/HardwareProfileViewModel.cs` (complete)

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kinesis.Core.Config;

namespace Kinesis.App.ViewModels;

public enum GripStyle { Wrist, Claw, Arm }

public readonly record struct HardwareProfile(double Dpi, double PadWidthCm, GripStyle Grip);

public partial class HardwareProfileViewModel : ViewModelBase
{
    // decimal for NumericUpDown.Value (decimal?) — compiled bindings need exact types
    [ObservableProperty] private decimal _dpi = Constants.DpiDefault;
    [ObservableProperty] private decimal _padWidthCm = (decimal)Constants.PadWidthDefaultCm;
    [ObservableProperty] private GripStyle _grip = GripStyle.Arm;
    [ObservableProperty] private string? _validationError;

    public IReadOnlyList<GripStyle> GripStyles { get; } = Enum.GetValues<GripStyle>();

    /// <summary>Raised with the validated profile; ShellViewModel navigates to the session page.</summary>
    public event Action<HardwareProfile>? ProfileReady;

    [RelayCommand]
    private void StartCalibration()
    {
        ValidationError = null;
        if (Dpi < Constants.DpiMin || Dpi > Constants.DpiMax)
        { ValidationError = $"DPI must be between {Constants.DpiMin} and {Constants.DpiMax}."; return; }
        if (PadWidthCm < (decimal)Constants.PadWidthMinCm)
        { ValidationError = $"Mousepad width must be at least {Constants.PadWidthMinCm} cm."; return; }
        ProfileReady?.Invoke(new HardwareProfile((double)Dpi, (double)PadWidthCm, Grip));
    }
}
```

#### M5-A.4 `Views/CalibrationSessionView.axaml` + `.axaml.cs` (complete)

```xml
<UserControl x:Class="Kinesis.App.Views.CalibrationSessionView"
             xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Kinesis.App.ViewModels"
             xmlns:controls="using:Kinesis.App.Controls"
             x:DataType="vm:SessionViewModel"
             x:CompileBindings="True">
  <Grid Background="#0E1116">

    <!-- 3D calibration viewport -->
    <controls:GlViewport x:Name="Viewport" Cursor="None"/>

    <!-- centered crosshair overlay (decoration only, never hit-tested) -->
    <Canvas IsHitTestVisible="False" HorizontalAlignment="Center" VerticalAlignment="Center">
      <Rectangle Width="2" Height="14" Fill="#E5FFFFFF" Canvas.Left="-1"  Canvas.Top="-16"/>
      <Rectangle Width="2" Height="14" Fill="#E5FFFFFF" Canvas.Left="-1"  Canvas.Top="2"/>
      <Rectangle Width="14" Height="2" Fill="#E5FFFFFF" Canvas.Left="-16" Canvas.Top="-1"/>
      <Rectangle Width="14" Height="2" Fill="#E5FFFFFF" Canvas.Left="2"   Canvas.Top="-1"/>
    </Canvas>

    <!-- HUD overlay (D16: no sensitivity values, ever) -->
    <Border VerticalAlignment="Bottom" HorizontalAlignment="Center" Margin="0,0,0,28"
            Background="#B012151B" CornerRadius="8" Padding="18,10">
      <StackPanel Spacing="6" MinWidth="300">
        <Grid ColumnDefinitions="*,Auto">
          <TextBlock Grid.Column="0" Text="{Binding RoundText}" Foreground="#E7ECF3"/>
          <TextBlock Grid.Column="1" Text="{Binding BlockStatus}" Foreground="#9AA3B2"/>
        </Grid>
        <ProgressBar Value="{Binding BlockProgress}" Minimum="0" Maximum="1" Height="4"/>
        <TextBlock Text="{Binding LiveStatus}" Foreground="#7C9FFF" FontSize="12"/>
      </StackPanel>
    </Border>
  </Grid>
</UserControl>
```

```csharp
using Avalonia.Controls;

namespace Kinesis.App.Views;

public partial class CalibrationSessionView : UserControl
{
    public CalibrationSessionView() => InitializeComponent();
}
```

`ViewModels/SessionViewModel.cs` (complete — HUD only; the engine pushes updates via `Dispatcher.UIThread.Post`):

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace Kinesis.App.ViewModels;

public partial class SessionViewModel : ViewModelBase
{
    [ObservableProperty] private string _roundText = "Round 1 / 6";
    [ObservableProperty] private string _blockStatus = "Get ready";
    [ObservableProperty] private double _blockProgress;               // 0..1, block timer bar
    [ObservableProperty] private string _liveStatus = "";             // e.g. "On target 62%"
}
```

#### M5-A.5 `Views/ResultsDashboardView.axaml` + `.axaml.cs` (complete)

```xml
<UserControl x:Class="Kinesis.App.Views.ResultsDashboardView"
             xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Kinesis.App.ViewModels"
             xmlns:views="using:Kinesis.App.Views"
             xmlns:controls="using:Kinesis.App.Controls"
             x:DataType="vm:ResultsViewModel"
             x:CompileBindings="True">
  <ScrollViewer>
    <StackPanel Margin="32" Spacing="20" MaxWidth="1000">

      <TextBlock Text="Calibration results" FontSize="28" FontWeight="SemiBold"/>

      <!-- hero cm/360 metric card -->
      <Border Background="#1C1F26" CornerRadius="12" Padding="24">
        <StackPanel Spacing="4">
          <TextBlock Text="RECOMMENDED" Opacity="0.6" FontSize="12" LetterSpacing="2"/>
          <TextBlock Text="{Binding HeroText}" FontSize="56" FontWeight="Bold" Foreground="#00E5FF"/>
          <TextBlock Text="{Binding BandText}" Opacity="0.7"/>
        </StackPanel>
      </Border>

      <!-- GP posterior plot -->
      <Border Background="#1C1F26" CornerRadius="12" Padding="12">
        <controls:GpPlotControl Height="360"
                                GridPredictions="{Binding Curve}"
                                Observations="{Binding Observations}"/>
      </Border>

      <!-- kinematic breakdown table -->
      <ItemsControl ItemsSource="{Binding Rounds}">
        <ItemsControl.ItemTemplate><DataTemplate x:DataType="vm:RoundRow">
          <Grid ColumnDefinitions="*,*,*,*,*" Margin="4">
            <TextBlock Grid.Column="0" Text="{Binding ProbeText}"/>
            <TextBlock Grid.Column="1" Text="{Binding BsiText}"/>
            <TextBlock Grid.Column="2" Text="{Binding AccuracyText}"/>
            <TextBlock Grid.Column="3" Text="{Binding JerkText}"/>
            <TextBlock Grid.Column="4" Text="{Binding FlickText}"/>
          </Grid>
        </DataTemplate></ItemsControl.ItemTemplate>
      </ItemsControl>

      <!-- projection copy cards -->
      <ItemsControl ItemsSource="{Binding Projections}">
        <ItemsControl.ItemsPanel><ItemsPanelTemplate>
          <UniformGrid Columns="4"/>
        </ItemsPanelTemplate></ItemsControl.ItemsPanel>
        <ItemsControl.ItemTemplate><DataTemplate x:DataType="vm:ProjectionCard">
          <Border Background="#1C1F26" CornerRadius="10" Margin="6" Padding="16">
            <StackPanel Spacing="6">
              <TextBlock Text="{Binding GameName}" Opacity="0.7"/>
              <TextBlock Text="{Binding SensText}" FontSize="24" FontWeight="SemiBold"/>
              <Button Content="Copy" Command="{Binding CopyCommand}" HorizontalAlignment="Stretch"/>
            </StackPanel>
          </Border>
        </DataTemplate></ItemsControl.ItemTemplate>
      </ItemsControl>

      <!-- embedded coach terminal -->
      <views:CoachTerminalView DataContext="{Binding Coach}"/>
    </StackPanel>
  </ScrollViewer>
</UserControl>
```

```csharp
using Avalonia;
using Avalonia.Controls;
using Kinesis.App.ViewModels;

namespace Kinesis.App.Views;

public partial class ResultsDashboardView : UserControl
{
    public ResultsDashboardView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) =>
        {
            if (DataContext is ResultsViewModel vm && TopLevel.GetTopLevel(this) is { Clipboard: { } clip })
                vm.Clipboard = clip;   // clipboard needs a TopLevel; hand it to the VM once
        };
    }
}
```

#### M5-A.6 `Views/CoachTerminalView.axaml` + `.axaml.cs` (complete)

```xml
<UserControl x:Class="Kinesis.App.Views.CoachTerminalView"
             xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Kinesis.App.ViewModels"
             x:DataType="vm:CoachViewModel"
             x:CompileBindings="True">
  <Border Background="#12151B" CornerRadius="12" Padding="16">
    <DockPanel>
      <Grid DockPanel.Dock="Bottom" ColumnDefinitions="*,Auto" Margin="0,12,0,0">
        <TextBox Grid.Column="0" Text="{Binding InputText}"
                 Watermark="Ask the coach… e.g. &quot;make it 5% slower&quot;"/>
        <Button Grid.Column="1" Content="Send" Command="{Binding SendCommand}" Margin="8,0,0,0"/>
      </Grid>
      <ScrollViewer>
        <ItemsControl ItemsSource="{Binding Transcript}">
          <ItemsControl.ItemTemplate><DataTemplate x:DataType="vm:ChatLine">
            <TextBlock Text="{Binding Text}" TextWrapping="Wrap"
                       Foreground="{Binding Brush}" Margin="0,4"/>
          </DataTemplate></ItemsControl.ItemTemplate>
        </ItemsControl>
      </ScrollViewer>
    </DockPanel>
  </Border>
</UserControl>
```

```csharp
using Avalonia.Controls;

namespace Kinesis.App.Views;

public partial class CoachTerminalView : UserControl
{
    public CoachTerminalView() => InitializeComponent();
}
```

#### M5-A.7 `ViewModels/ResultsViewModel.cs`, `CoachViewModel.cs`, `ViewModelBase.cs` (complete)

```csharp
using System.Collections.ObjectModel;
using Avalonia.Input.Platform;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kinesis.App.Controls;
using Kinesis.Core.Optimization;

namespace Kinesis.App.ViewModels;

public abstract class ViewModelBase : ObservableObject { }

public readonly record struct RoundRow(string ProbeText, string BsiText, string AccuracyText,
                                       string JerkText, string FlickText);

public partial class ProjectionCard : ObservableObject
{
    [ObservableProperty] private string _gameName = "";
    [ObservableProperty] private string _sensText = "";
    private readonly Func<IClipboard?> _clipboard;

    public ProjectionCard(string gameName, double sens, Func<IClipboard?> clipboard)
    {
        _gameName = gameName;
        _sensText = sens.ToString("F4");
        _clipboard = clipboard;
    }

    [RelayCommand]
    private async Task CopyAsync()
    {
        if (_clipboard() is { } clip) await clip.SetTextAsync(SensText);
    }
}

public partial class ResultsViewModel : ViewModelBase
{
    [ObservableProperty] private string _heroText = "";      // "27.4 cm/360"
    [ObservableProperty] private string _bandText = "";      // "plausible band 25.1 – 29.8 cm/360"
    [ObservableProperty] private IReadOnlyList<GPPoint>? _curve;
    [ObservableProperty] private IReadOnlyList<ObservationPoint>? _observations;

    public ObservableCollection<RoundRow> Rounds { get; } = new();
    public ObservableCollection<ProjectionCard> Projections { get; } = new();
    public CoachViewModel Coach { get; }
    public IClipboard? Clipboard { get; set; }               // injected by the view (M5-A.5)

    public ResultsViewModel(CoachViewModel coach) => Coach = coach;

    /// <summary>Bind the optimizer report to the UI. Pure mapping; runs on the UI thread.</summary>
    public void LoadReport(OptimizationReport report, double dpi)
    {
        HeroText = $"{report.RecommendedCm360:F1} cm/360";
        BandText = $"plausible band {report.BandLoCm360:F1} – {report.BandHiCm360:F1} cm/360";
        Curve = report.Curve.Select(c => new GPPoint(
            (c.Cm360 - report.Curve[0].Cm360) /
            (report.Curve[^1].Cm360 - report.Curve[0].Cm360), c.Mean, c.Std)).ToArray();
        Observations = report.Observations.Select(o => new ObservationPoint(
            (o.Cm360 - report.Curve[0].Cm360) /
            (report.Curve[^1].Cm360 - report.Curve[0].Cm360), o.YNorm)).ToArray();

        Projections.Clear();
        foreach (var (name, engine) in new (string, Kinesis.Core.Projection.GameEngine)[]
                 { ("CS2", Kinesis.Core.Projection.GameEngine.Cs2Source),
                   ("Valorant", Kinesis.Core.Projection.GameEngine.ValorantUnreal),
                   ("Overwatch 2", Kinesis.Core.Projection.GameEngine.Overwatch2),
                   ("Call of Duty", Kinesis.Core.Projection.GameEngine.CallOfDuty) })
            Projections.Add(new ProjectionCard(name,
                Kinesis.Core.Projection.GameProjection.ToGameSens(engine, dpi, report.RecommendedCm360),
                () => Clipboard));
    }
}

public partial class CoachViewModel : ViewModelBase
{
    private readonly Kinesis.Ai.CoachSession _session;
    [ObservableProperty] private string _inputText = "";
    [ObservableProperty] private bool _isBusy;

    public ObservableCollection<ChatLine> Transcript { get; } = new();

    public CoachViewModel(Kinesis.Ai.CoachSession session) => _session = session;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var text = InputText;
        InputText = "";
        IsBusy = true;
        Transcript.Add(new ChatLine("you", text, Brushes.Gray));
        var streamed = new System.Text.StringBuilder("coach: ");
        var line = new ChatLine("", string.Empty, Brushes.Cyan);
        Transcript.Add(line);
        try
        {
            var reply = await _session.AskAsync(text,
                token => Dispatcher.UIThread.Post(() =>
                {
                    streamed.Append(token);
                    line.Text = streamed.ToString();
                }),
                CancellationToken.None);
            line.Text = reply;
        }
        finally { IsBusy = false; }
    }

    private bool CanSend() => !IsBusy && !string.IsNullOrWhiteSpace(InputText);
}

public sealed partial class ChatLine : ObservableObject
{
    [ObservableProperty] private string _text;
    public IBrush Brush { get; }
    public ChatLine(string prefix, string text, IBrush brush)
    {
        _text = prefix.Length > 0 ? $"{prefix}: {text}" : text;
        Brush = brush;
    }
}
```

(`Dispatcher` = `Avalonia.Threading.Dispatcher`. `ChatLine.Text` is observable so streamed tokens update the bound `TextBlock` in place.)

#### M5-A.8 `App.axaml` data templates (complete; replaces `ViewLocator` — trim-safe, D3)

```xml
<Application.DataTemplates>
  <DataTemplate DataType="{x:Type vm:HardwareProfileViewModel}">
    <views:HardwareProfileView/>
  </DataTemplate>
  <DataTemplate DataType="{x:Type vm:SessionViewModel}">
    <views:CalibrationSessionView/>
  </DataTemplate>
  <DataTemplate DataType="{x:Type vm:ResultsViewModel}">
    <views:ResultsDashboardView/>
  </DataTemplate>
</Application.DataTemplates>
```

`ShellViewModel` (in `ViewModels/ShellViewModel.cs`) holds `ObservableObject CurrentPage` + `NavigateTo…` methods that swap `CurrentPage` between the three VMs; `HardwareProfileViewModel.ProfileReady` builds `SessionViewModel`/engine, and session completion calls `ResultsViewModel.LoadReport(...)` then navigates. No reflection anywhere (trimming-safe).

---

## 13. Appendices

### 13.1 Risk register (plan §5 + discovered risks)

| Risk | Impact | Likelihood | Mitigation (concrete) |
| --- | --- | --- | --- |
| High polling-rate mice (4–8 kHz) overload UI | High | Medium | Input pump on its own `AboveNormal` thread → lock-free ring; render coalesces snapshots; never per-sample UI work (§2). Test with a 8 kHz mouse: drop counter in dev overlay must stay 0. |
| Human fatigue skews late rounds | High | High | ≤6 rounds × 40 s + 10 s rests (< 8 min); noise inflation $\sigma_{n,r}^2=\sigma_n^2/\omega_r$ (D18); hard stop at `MaxRounds`. |
| Linux permission restrictions | Medium | High | udev rule + group script (§4.3), D20 dialog with one-click `pkexec`. |
| LFM3 latency on low-end CPUs | Medium | Low | D17: post-calibration only, `max_new_tokens=512`, greedy-fast path optional, `--skip-ai`. |
| **ORT-GenAI cannot drive LFM3** (discovered during research) | High | Certain | **Resolved**: manual `InferenceSession` loop (D7, §8.5); tokenizer-only dependency on ORT-GenAI. |
| Trimming breaks Avalonia/Skia/JSON | High | Medium | Compiled bindings (D3), `TrimMode=partial`, `SuppressTrimAnalysisWarnings=false`, publish smoke test (§10.2). |
| Model download corruption / size | Medium | Medium | Size assertions at fetch + startup (§8.1/§10.7); D20 remediation dialog. |
| macOS TCC denial | Medium | Medium | IOHIDManager `kIOReturnNotPrivileged` detection → guided dialog (§4.4/§10.5); degraded CGEventTap path requires explicit banner (D12). |
| Absolute-position devices pollute counts | Low | Low | Windows: skip `MOUSE_MOVE_ABSOLUTE` samples; Linux: evdev relative-only filter; macOS: HID mouse-only matching. |
| Timestamp non-monotonicity across devices (Linux multi-mouse) | Medium | Low | Per-fd offsets aligned on first SYN; single merged stream; extractor validates monotonicity and drops non-monotonic samples with a counter. |

### 13.2 Deviation log (fill `docs/DEVLOG.md`)

Template per entry: `DATE | plan-section | expected | observed | decision | who`. Every time reality forces a change from this document, log it before changing code.

### 13.3 Glossary

cm/360 (invariant sensitivity unit) · DPI (mouse counts per inch) · yaw θ (deg per count per sens) · BSI (0–100 round score) · GP-BO (Gaussian-process Bayesian optimization) · EI (expected improvement) · E_flick (flick overshoot ratio) · T_adjust (micro-correction latency) · anchor run (bounds endpoint probe) · probe (candidate cm360 tested in one round) · tri-hybrid (LFM3's conv/SWA/global layer mix).

### 13.4 Performance budgets (verify in dev overlay)

| Budget | Value |
| --- | --- |
| Input event → ring write | < 0.2 ms (no allocs) |
| GP fit + EI suggest (n ≤ 8) | < 5 ms on one core |
| Round scoring (40 s of samples) | < 50 ms |
| Render | 60 FPS with 8 kHz input; frame time < 12 ms |
| RAM with model loaded | < 2.5 GB |
| Cold start (no model load) | < 3 s |

### 13.5 Final done-checklist

- [ ] `dotnet test` green (Core + Ai).
- [ ] §11.5 acceptance 1–3 pass on the target machine.
- [ ] `dotnet publish` per-OS outputs run from `dist/`.
- [ ] Dev overlay ruler check (§4.5) within ±1 %.
- [ ] DEVLOG contains no unresolved deviations.
- [ ] README: build/run/model/permissions/licenses (LFM Open License v1.0 for model assets).

### 13.6 `src/Kinesis.Core/Config/Constants.cs` (full listing)

```csharp
namespace Kinesis.Core.Config;

/// <summary>All tunables in one place. [TUNABLE] values may be adjusted with DEVLOG entries; the rest are contractual.</summary>
public static class Constants
{
    // --- Input & trajectory ---
    public const int    SampleRingCapacity   = 65_536;
    public const double ResampleDtS          = 0.001;
    public const int    MinBlockSamples      = 100;      // <100 ms of data → round invalid

    // --- Kinematics (§5.3) ---
    public const double JRefDegPerS3         = 10_000.0; // [TUNABLE] "poor" end of jerk normalization
    public const double ZeroVelThresholdDegPerS = 5.0;   // [TUNABLE]
    public const int    ZeroVelDwellMs       = 30;       // [TUNABLE]
    public const double TAdjustRefMs         = 500.0;    // [TUNABLE]
    public const double EFlickClipLo         = -1.0;
    public const double EFlickClipHi         = 2.0;

    // --- BSI (§6, D13) ---
    public const double W1 = 0.40;                       // [TUNABLE] accuracy
    public const double W2 = 0.25;                       // [TUNABLE] 1 - normJerk
    public const double W3 = 0.20;                       // [TUNABLE] 1 - |E_flick|
    public const double W4 = 0.15;                       // [TUNABLE] 1 - T_adjust

    // --- Optimizer (§7, D14) ---
    public const double BoundsMinCm360      = 10.0;      // [TUNABLE]
    public const double BoundsMaxCm360      = 60.0;      // [TUNABLE]
    public const double PadTurnFraction     = 2.0;       // hi = min(max, 2 × pad width)
    public const double MaternLengthScale   = 0.25;      // in normalized units
    public const double GpNoiseFloor        = 0.05;      // σ_n² on y/100 scale
    public const double EiXi                = 0.01;
    public const int    EiGridPoints        = 150;
    public const double ConvergenceEps      = 0.05;      // max-σ stop
    public const double RecommendBandEps    = 0.02;      // μ* - band cutoff
    public const double FatigueLambda       = 0.05;      // per-minute noise inflation
    public const int    MaxRounds           = 6;

    // --- Scenarios (§9.4) & round timing (§9.5) ---
    public const double BlockTrackingS      = 15.0;
    public const double BlockFlickS         = 15.0;
    public const double BlockDotS           = 10.0;
    public const double RoundIntroS         = 3.0;
    public const double RestS               = 10.0;
    public const double SphereRadiusDeg     = 3.5;
    public const double SphereSlackDeg      = 1.0;
    public const double SphereAmpXDeg       = 14.0;
    public const double SphereAmpYDeg       = 9.0;
    public const double SphereFreqXHz       = 0.10;
    public const double SphereFreqYHz       = 0.07;
    public const double SpherePhaseRad      = Math.PI / 3.0;
    public const double FlickAzMinDeg       = 90.0;
    public const double FlickAzMaxDeg       = 120.0;
    public const double FlickElMaxDeg       = 8.0;
    public const double FlickTargetRadiusDeg = 3.0;
    public const double FlickTargetLifeS    = 2.4;
    public const double DotWindowDeg        = 10.0;
    public const double DotMinSepDeg        = 2.5;
    public const double DotRadiusDeg        = 0.8;
    public const double DotLifeS            = 1.8;
    public const int    DotCount            = 10;

    // --- AI (§8, D17) ---
    public const double AiTemperature       = 0.15;      // [TUNABLE]
    public const int    AiTopK              = 50;
    public const double AiRepetitionPenalty = 1.1;
    public const int    AiMaxNewTokens      = 512;
    public const int    MaxToolRoundTrips   = 3;

    // --- Profile validation (§12 M5) ---
    public const int    DpiMin = 100, DpiMax = 6400, DpiDefault = 800;
    public const double PadWidthMinCm = 5.0, PadWidthDefaultCm = 45.0;
}
```

*End of plan.*

