# Architecture & Build Environment

> Moved from CLAUDE.md. For build commands, see the main [CLAUDE.md](../CLAUDE.md).

-----

## 目錄結構

```
UE5CEDumper/
├── CLAUDE.md
├── .gitmodules
│
├── dll/                            ← C++ DLL (injected)
│   ├── CMakeLists.txt
│   └── src/
│       ├── dllmain.cpp
│       ├── ExportAPI.cpp / .h      ← C ABI export
│       ├── PipeServer.cpp / .h     ← Named Pipe IPC server
│       ├── PipeProtocol.h          ← JSON command/response 定義
│       ├── OffsetFinder.cpp / .h
│       ├── ObjectArray.cpp / .h
│       ├── FNamePool.cpp / .h
│       ├── UStructWalker.cpp / .h
│       └── Memory.cpp / .h
│
├── docs/                           ← 相關文件
│
├── ui/                             ← C# Avalonia UI App
│   ├── UE5DumpUI.csproj
│   ├── App.axaml / App.axaml.cs
│   ├── Models/
│   │   ├── UObjectNode.cs          ← Tree node model
│   │   ├── FieldInfo.cs            ← Property grid model
│   │   └── EngineState.cs          ← GObjects/GNames 等狀態
│   ├── Services/
│   │   ├── PipeClient.cs           ← Named Pipe 連線 + 收發
│   │   └── DumpService.cs          ← 業務邏輯封裝
│   ├── ViewModels/
│   │   ├── MainWindowViewModel.cs
│   │   ├── ObjectTreeViewModel.cs
│   │   ├── ClassStructViewModel.cs
│   │   ├── PointerPanelViewModel.cs
│   │   └── HexViewViewModel.cs
│   └── Views/
│       ├── MainWindow.axaml / .cs
│       ├── ObjectTreePanel.axaml / .cs
│       ├── ClassStructPanel.axaml / .cs
│       ├── PointerPanel.axaml / .cs
│       └── HexViewPanel.axaml / .cs
│
├── scripts/
│   ├── ue5dump.lua                 ← CE 主腳本（注入 + 啟動 pipe）
│   └── utils.lua
│
└── vendor/
    ├── UE5Dumper/                  ← git submodule（僅參考）
    ├── GSpots/                     ← git submodule（僅參考）
    └── Dumper-7/                   ← git submodule（僅參考）
```

### Git Submodule 設定

```bash
git submodule add https://github.com/Encryqed/Dumper-7 vendor/Dumper-7
git submodule add --force https://github.com/Spuckwaffel/UEDumper.git vendor/UEDumper
git submodule add --force https://github.com/Do0ks/GSpots.git vendor/GSpots
```

-----

## Build 環境

### DLL（C++）

- **IDE**: Visual Studio 2026 (v18, MSVC 19.50): generate related .sln files
- **C++ Standard**: C++23
- **Target**：x64 Release DLL、Statically-linked DLL (static CRT, `/MT` / `/MTd`)
- **Flags**: `/utf-8 /W4 /permissive-`
- **Compiler**: MSVC v145 (cl.exe)
- **Build**: CMake 3.25+ with Ninja generator
- **依賴**：nlohmann/json（header-only，放 vendor/）
- **Compile script**: All commands needed should find through vswhere, not fixed path

### CMakeLists.txt

```cmake
cmake_minimum_required(VERSION 3.20)
project(UE5Dumper)

set(CMAKE_CXX_STANDARD 23)

add_library(UE5Dumper SHARED
    dll/src/dllmain.cpp
    dll/src/ExportAPI.cpp
    dll/src/PipeServer.cpp
    dll/src/OffsetFinder.cpp
    dll/src/ObjectArray.cpp
    dll/src/FNamePool.cpp
    dll/src/UStructWalker.cpp
    dll/src/Memory.cpp
)

target_include_directories(UE5Dumper PRIVATE dll/src vendor/nlohmann)
target_compile_definitions(UE5Dumper PRIVATE UNICODE _UNICODE)
target_link_libraries(UE5Dumper PRIVATE ws2_32)
```

### UI App（C# Avalonia）

- **.NET 8**
- **Avalonia 11.x**
- **ReactiveUI + ReactiveUI.Fody**
- **單檔發布**（`PublishSingleFile` + `SelfContained`，約 60~80MB）

```xml
<!-- UE5DumpUI.csproj 重點設定 -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.*" />
    <PackageReference Include="Avalonia.Desktop" Version="11.*" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="11.*" />
    <PackageReference Include="ReactiveUI.Fody" Version="*" />
    <PackageReference Include="ReactiveUI" Version="*" />
  </ItemGroup>
</Project>
```
