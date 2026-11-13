# OPTISENS Development & Deviation Log

Format: `DATE | plan-section | expected | observed | decision | who`

- `2026-11-13 | §2.2 global.json | rollForward: latestFeature on 9.0.100 | Host has .NET 10 SDK (10.0.300, 10.0.402) only | Set rollForward: latestMajor in global.json (per D1: .NET 10 acceptable if SDK present) | agent`

- `2026-11-13 | §9.1 GlViewport | InvalidateVisual() | Avalonia 11.3 marks OpenGlControlBase.InvalidateVisual() obsolete (CS0618) | Use RequestNextFrameRendering() | agent`
- `2026-11-13 | §4.2 Win32Native.RAWMOUSE | FieldOffset(0/4/8/12/16) | Windows SDK tagRAWMOUSE has 2-byte alignment padding at offset 2 before ulButtons union | Use exact winuser.h FieldOffsets (0, 4, 6, 8, 12, 16, 20) | agent`
- `2026-11-13 | §8.3/§8.5 ORT-GenAI 0.10 + ORT 1.23 | Tokenizer(string), TensorElementType.Float32, GetTensorTypeAndShapeInfo() | ORT-GenAI 0.10 uses Tokenizer(Model) and Decode(ReadOnlySpan<int>); ORT 1.23 uses TensorElementType.Float, ElementDataType, GetTensorTypeAndShape(), CreateTensorValueWithData | Updated Lfm3Tokenizer and Lfm3Engine to match installed package APIs | agent`
- `2026-11-13 | §2.2 OPTISENS.App.csproj | BuiltInComInteropSupport=true with PublishTrimmed=true | .NET 9 ILLink fails with IL2026 on BuiltInComInteropSupport=true and IL2104 on Avalonia.DesignerSupport.dll | Set BuiltInComInteropSupport=false (Avalonia 11 uses MicroCom) and suppress IL2104 for Avalonia.DesignerSupport | agent`
