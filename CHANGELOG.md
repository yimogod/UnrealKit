# Changelog

All notable changes to UnrealKit.

## [0.1.0] — 2026-08-09

### Core Infrastructure (M0–M1)
- Four-project solution: `UnrealKit.Core`, `UnrealKit.Cli`, `UnrealKit.Desktop`, `UnrealKit.Tests`
- `Directory.Build.props` with Nullable, implicit usings, latest C#, warnings-as-errors
- `.ukit` project descriptor (UTF-8 INI v1) with create/open/validate
- `Config/DefaultGame.ini` read/write with priority chain: built-in defaults < .ukit < Config/DefaultGame.ini < CLI/GUI explicit

### ADB & Device Operations (M2)
- `ProcessRunner` with parameterized invocation (`ArgumentList`), timeout, cancellation, process tree termination
- `AdbService`: `devices -l` parsing, version check, Wi-Fi connect/disconnect, auto-select single device
- `LaunchParameterService`: presets (LLM, LLM CSV, OpenGL, Vulkan, Trace, No Update) and custom arguments

### Capture & Archiving (M3)
- `CaptureService.CaptureAsync`: ADB `dumpsys meminfo` + optional Saved pull
- `CaptureManifest.json` with device serial/model, config snapshot, file list with SHA-256
- Import from local directories to organized `Content/<Platform>/<Tag>/<Date>/<CaptureId>/`
- `--skip-saved` flag to skip Saved pull

### Parsers (M4)
- `AndroidMemInfoParser`: App Summary, Detailed PSS, Dalvik, Objects with OEM column variants, K-unit handling, thousands separators
- `UnrealMemReportParser`: Changelist, Wwise, Lua, Texture Streaming, Shader, RHI, LLM summary metrics
- Detail parsing: Textures (dimensions, format, memory), Render Targets, Objects (class, count, memory)
- Diagnostic codes with line numbers and suggested fixes (UMR101–UMR306, AMI210–AMI223)
- 7 meminfo samples + 1 memreport golden sample for regression testing

### Export (M5)
- `MemInfoExportService`: CSV/TSV with summary and detail modes
- `MemReportExportService`: CSV/TSV with summary and detail (textures, render targets, objects)
- `XlsxMemInfoExportService`: Real XLSX workbook with Metadata, AndroidMemInfo, PSS Details, Dalvik, Objects, Diagnostics sheets
- `XlsxMemReportExportService`: Real XLSX workbook with Metadata, MemReport Summary, Textures, Render Targets, Objects, Diagnostics sheets
- Earliest/latest timestamp and capture ID provenance in all exports

### CLI (M6)
- `project create/info/validate`, `adb version/devices/connect/disconnect`, `app start`
- `commandline push/delete` with presets and custom arguments
- `capture run` with `--skip-saved`, `--format json`; `capture list`/`capture info`
- `parse meminfo/memreport/capture-list/capture-files/capture-meminfo`
- `export meminfo/memreport` with `--include-details`, `--capture-id`
- `--format json` machine-readable output across all subcommands
- Explicit device selection required; no implicit defaults

### GUI (M7)
- 8-page WPF desktop application
- Project: create/open, project info & validation
- Device: scan, select, connect status, launch app
- Launch Params: presets, push → launch → delete cycle with confirmation dialogs
- Capture: tag selection, pull content preview, progress & log
- Parse: meminfo + memreport file selection, parse execution, diagnostic display
- Results: capture browse, file list, meminfo summary
- Export: input/output selection, IncludeDetails toggle, CSV/TSV/XLSX export
- Log & Settings: operation log, project config editor, ADB path config
- All long-running ops use async APIs; UI never blocks

### Tests (M8)
- 72 unit tests covering: ProjectService, AdbService, AndroidMemInfoParser, UnrealMemReportParser, CaptureService, MemInfoExportService, MemReportExportService, XlsxMemInfoExportService, XlsxMemReportExportService, ProcessRunner, LaunchParameterService, CaptureAnalysisService, DesktopShellViewModel
- End-to-end parse→export chain tests for meminfo and memreport across CSV, TSV, XLSX
- Build scripts: Debug, Shipping, Publish (self-contained win-x64)
