# Changelog

All notable changes to UnrealKit.

## [0.1.0] — 2026-08-09

### Core Infrastructure (M0–M1)
- Four-project solution: `UnrealKit.Core`, `UnrealKit.Cli`, `UnrealKit.Desktop`, `UnrealKit.Tests`
- `Directory.Build.props` with Nullable, implicit usings, latest C#, warnings-as-errors
- `.ukit` project descriptor (UTF-8 INI v1) with create/open/validate
- `Config/DefaultGame.ini` read/write with priority chain: built-in defaults < .ukit < Config/DefaultGame.ini < CLI/GUI explicit

### ADB & Device Operations (M2)
- `ProcessRunner` with parameterized invocation (`ArgumentList`), timeout, cancellation, process tree termination, streaming line-by-line output via `IProgress<ProcessOutput>`
- `AdbService`: `devices -l` parsing, version check, Wi-Fi connect/disconnect, auto-select single device
- `LaunchParameterService`: presets (LLM, LLM CSV, OpenGL, Vulkan, Trace, No Update) and custom arguments

### Capture & Archiving (M3)
- `CaptureService.CaptureAsync`: ADB `dumpsys meminfo` + optional Saved pull
- `CaptureManifest.json` with device serial/model, config snapshot, file list with SHA-256
- Import from local directories to organized `Content/<Platform>/<Tag>/<Date>/<CaptureId>/`
- `--skip-saved` flag to skip Saved pull

### Parsers (M4)
- `AndroidMemInfoParser`: App Summary, Detailed PSS, Dalvik, Objects with OEM column variants
- `UnrealMemReportParser`: Changelist, Wwise, Lua, Texture Streaming, Shader, RHI, LLM summary metrics
- Detail parsing: Textures (dimensions, format, memory), Render Targets, Objects (class, count, memory)
- Diagnostic codes with line numbers and suggested fixes (UMR101–UMR306, AMI210–AMI223)

### Export (M5)
- `MemInfoExportService`: CSV/TSV with summary and detail modes
- `MemReportExportService`: CSV/TSV with summary and detail (textures, render targets, objects)
- `XlsxMemInfoExportService`: Real XLSX workbook with Metadata, AndroidMemInfo, PSS Details, Dalvik, Objects, Diagnostics sheets
- `XlsxMemReportExportService`: Real XLSX workbook with Metadata, MemReport Summary, Textures, Render Targets, Objects, Diagnostics sheets

### CLI (M6)
- `project create/info/validate`, `adb version/devices/connect/disconnect`, `app start`
- `commandline push/delete` with presets and custom arguments
- `capture run` with `--skip-saved`, `--format json`; `capture list`/`capture info`
- `parse meminfo/memreport/capture-list/capture-files/capture-meminfo`
- `export meminfo/memreport` with `--include-details`, `--capture-id`
- `--format json` machine-readable output across all subcommands

### GUI (M7)
- 8-page WPF desktop application
- Project, Device, Launch Params, Capture, Parse, Results, Export, Log & Settings
- Real-time ADB output streaming to operation log
- All long-running ops use async APIs

### Tests & Release (M8)
- 72 unit tests: ProjectService, AdbService, parsers, CaptureService, export services, ProcessRunner, LaunchParameterService, CaptureAnalysisService, ShellViewModel
- End-to-end parse → export chain tests for meminfo and memreport across CSV, TSV, XLSX
- `README.md`, `CHANGELOG.md`, `LICENSE`
- `Script/Build-Debug.bat`, `Script/Build-Shipping.bat`, `Script/Publish-Shipping.bat`
- `Script/Run-Cli-Debug.bat`, `Script/Run-Cli-Shipping.bat`, `Script/Run-Gui-Debug.bat`, `Script/Run-Gui-Shipping.bat`
- `Doc/PlanM1.md` (phase 1 archive), `Doc/PlanM2.md` (phase 2 plan)
