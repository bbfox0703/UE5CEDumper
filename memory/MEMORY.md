# UE5CEDumper Project Memory

## Key AOT Fix (Critical)
**Problem**: Native AOT publish produced EXE that crashed immediately on startup with
`NullReferenceException` on compositor background thread — before `App.Initialize()`.

**Root Cause**: Avalonia on Windows defaults to WinUI Composition (Windows.UI.Composition)
via MicroCom COM interop. This COM path crashes in Native AOT.

**Fix** (in `Program.cs`):
```csharp
.With(new Win32PlatformOptions
{
    CompositionMode = [Win32CompositionMode.RedirectionSurface]
})
```
Forces software GDI redirection surface, bypassing the WinUI Composition COM path entirely.
This is identical to what AOBMaker (working AOT reference project at D:\Github\AOBMaker) does.

## AOT Configuration Summary
See `ui/UE5DumpUI/UE5DumpUI.csproj` for the full config. Key items:
- `TrimMode=partial` — conservative trimming
- 14x `TrimmerRootAssembly` for all Avalonia platform/rendering assemblies
- `BuiltInComInteropSupport=false` — use MicroCom instead of built-in COM
- `NoWarn IL3053;IL2104` — suppress third-party AOT warnings
- `DisableAvaloniaDataAnnotationValidation()` in `App.axaml.cs`
- No `LogToTrace()` in `Program.cs` — AOT unsafe

## Build Commands
- `build publish` → Native AOT EXE to dist/ (~38 MB)
- `build release` → self-contained single-file EXE
- `build debug` → debug build

## Reference Project
D:\Github\AOBMaker — same Avalonia+AOT architecture, known working. Use as reference.
