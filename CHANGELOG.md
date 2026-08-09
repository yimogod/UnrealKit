# Changelog

All notable changes to UnrealKit.

## [Unreleased]

### Static Camera Performance (Phase 2 P1)
- `StaticCameraPerfParser`: `!!!Do Perf Start!!!` / `!!!Do Perf End!!!` sections, `PointNum:` camera count, 14-line per-camera data blocks, screenshot count validation
- All tags, structural parameters, and thresholds live in `StaticCameraPerfConfig`; `Validate()` enforces error thresholds strictly above warning thresholds
- DrawCall thresholds corrected to warning 400 / error 500. The legacy Python script used 500 for both, which made the warning tier unreachable
- New diagnostic code domain `SCP` (`SCP100`–`SCP104`, `SCP201`–`SCP204`, `SCP206`)
- CLI: `parse static-camera --input <log> [--screenshots <dir>] [--format text|json]`
- HTML report generation and the WPF static camera page are not included

### Baseline Diff (Phase 2 P2)
- `BaselineService` in the new `UnrealKit.Core.Analysis` namespace: compares a current report against a baseline of the same type. Both inputs are read-only
- Sources: `MemInfo`, `MemReport`, `StaticCamera`. The source is selected explicitly by the caller, never inferred from the file extension
- Per-metric `MetricDirection` (`LowerIsBetter` / `HigherIsBetter` / `Neutral`) drives the regressed/improved/changed assessment, so a memory drop reads as an improvement rather than a bare negative delta
- `MetricDiffStatus` distinguishes `Compared`, `MissingInBaseline`, `MissingInCurrent`, and `MissingInBoth`. A metric absent on one side reports as missing with a null delta, never as zero
- Static camera metrics are aligned by camera name, so a renamed or reordered camera reports as missing on both sides instead of comparing mismatched viewpoints
- A parse failure on either side produces no metrics, so no conclusion is drawn from half a comparison. Underlying parse diagnostics are carried through, prefixed with `[baseline]` or `[current]`
- New diagnostic code domain `BDF` (`BDF101`–`BDF103`, `BDF201`–`BDF203`)
- CLI: `analyze diff` with `--baseline` / `--current` (file paths, or capture IDs when `--project` is given), `--source`, `--metrics`, `--only-changed`, `--format text|json`
- Ambiguous inputs error out and list the candidates: multiple matching files in a capture archive, or a capture ID matching more than one archive
- The WPF diff page is not included

### Historical Trends (Phase 2 P3)
- `TrendService`: aggregates one series per metric across the captures matching a platform, tag, device, and date range, ordered oldest to newest. Capture archives are only read
- Reuses `MetricSample` and `MetricDirection` from the diff layer, so a metric's better/worse direction means the same thing in a trend as in a two-point comparison
- Per-point deltas step from the previous capture that had a value, so a gap mid-range is not read as a drop to zero and back
- Series statistics (`First`, `Last`, `Minimum`, `Maximum`, `Average`, `TotalDelta`, `TotalDeltaPercent`) ignore missing points rather than substituting zero. `TotalDelta` is null when fewer than two captures have a value, and the percentage is null when the first value is zero
- Metric order follows first appearance, so the oldest capture's layout leads and metrics introduced later are appended rather than dropped
- Every excluded capture produces a specific diagnostic instead of being silently skipped: `TRD101` named file absent, `TRD102` no file of the expected category, `TRD103` more than one candidate file, `TRD104` no manifest (under a device filter), `TRD105` unreadable manifest, `TRD202` report failed to parse
- One unparsable capture drops out as a warning without failing the range; its parse diagnostics are carried through at Warning severity, prefixed with the capture ID
- New diagnostic code domain `TRD` (`TRD101`–`TRD105`, `TRD201`–`TRD204`)
- `TrendExportService`: CSV/TSV with a per-series summary, an optional per-capture point section, and a diagnostics section
- `XlsxTrendExportService`: real XLSX workbook with Metadata, Trend Captures, Trend Series, Trend Points, and Diagnostics sheets
- Missing values are written as the explicit token `missing` in both text and XLSX output, never as 0 or a blank cell
- CLI: `analyze trend --project` with `--source`, `--platform`, `--tag`, `--device`, `--from`, `--to`, `--metrics`, `--file`, `--output`, `--include-points`, `--format text|json`
- The WPF trend page is not included

### Fixed
- The `*.log` ignore rule, meant for local diagnostic output, also excluded the static camera test samples under `TestData/StaticCamera/`, so those samples were never committed and the static camera tests could not pass on a fresh clone. Test sample logs are now negated from the rule and versioned

### Tests
- 126 tests passing (up from 79): 17 `BaselineServiceTests`, 18 `TrendServiceTests`, and 12 `TrendExportServiceTests`
- Diff coverage: delta and direction, percentage baselines, one-sided and two-sided missing metrics, camera renames, metric filtering, source mismatch, parse failure on either side
- Trend coverage: chronological ordering, tag/device/date filtering, missing points, deltas across a gap, ambiguous and absent input files, unparsable captures, single-capture ranges, inverted date ranges
- Export coverage: CSV and TSV delimiters, published column names, sheet names, summary-only versus point output, and missing values rendered as `missing`
- New `TestData/Baseline/` samples provide the "current" side, deliberately containing a metric missing on one side and a renamed camera. Trend tests assemble these samples into multi-capture project trees rather than adding more samples

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
