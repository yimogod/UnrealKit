# Changelog

All notable changes to UnrealKit.

## [Unreleased]

### Multi-Platform Projects (breaking)

One project can now be configured for several platforms at once. Previously `ProjectSettings.Platform` served two conflicting roles — which platform's fields are in effect, and which platform this operation targets. The second is a session choice, so keeping it in the versioned project config forced users to edit (and commit) the config every time they switched between Android and Win64 on the same UE project.

- **Breaking:** `Config/DefaultGame.ini` uses layout `SettingsVersion=2`, with one `[UnrealKit.Platform.<name>]` section per platform. There is **no automatic migration**: a v1 `Platform=Win64` project never had its Android fields filled in, so migrating could only guess, and a wrong device path makes a capture pull an empty directory while reporting success. Opening a v1 project fails with the field-by-field rewrite instructions. See `Doc/工程格式与配置.md`
- **Breaking:** `ProjectSettings.Platform`, `PackageName`, `Activity`, `DeviceGameRootTemplate`, `DeviceSavedRootTemplate`, `AdbPath`, `Win64Executable`, and `Win64WorkingDirectory` are replaced by `Android` / `Win64` platform profiles (`AndroidPlatformProfile`, `Win64PlatformProfile`). A null profile means "this project does not target that platform" — distinct from "configured but left blank"
- **Breaking:** `capture import --platform` is now required. Import involves no device to infer the platform from, and archive directories are partitioned by platform, so picking one for the user files data under the wrong platform
- `project create --platform` is repeatable and comma-separated (`--platform Android,Win64`), declaring which platforms to configure. Omitted, both get default profiles
- Existing `Content/` archives are unaffected — archive directories were already partitioned by platform

Platform differences now have a single exit point: `PlatformProfile.Resolve` returns a platform-neutral `PlatformTarget` (process identity, launch target, expanded device paths). `CaptureService` and `LaunchParameterService` consume that and contain no platform branches — five `if (settings.Platform == TargetPlatform.Win64)` checks are gone. Adding a platform means adding a `TargetPlatform` member, a `PlatformProfile` subclass, its INI mapping, and a device service; no call site changes.

- The target platform is decided by the selected device, not by config. `CaptureService` no longer rejects a device/config platform mismatch; it resolves that device's platform profile, or fails naming the unconfigured platform and listing the configured ones
- CLI device selection is unified across platforms: `--device` accepts any platform's identifier, `--platform` narrows enumeration, and an identifier present on multiple platforms requires `--platform` rather than being resolved arbitrarily. Per-platform enumeration failures (e.g. missing `adb`) are still reported as stated reasons
- GUI settings show Android and Win64 as parallel checkbox-gated groups instead of a target-platform dropdown with fields hidden by platform. The launch-parameter remote path resolves per selected device, so a multi-platform project no longer shows one platform's path while operating on another
- `CaptureManifest.PackageName` is replaced by `ResolvedTarget` (`PlatformTarget?`), recording which platform profile a capture actually used and what its templates expanded to. Null for imported archives, which involve no device

### Fixed
- `PlatformNames.TryParse` accepted numeric strings, so `Platform=99` parsed to an undefined `(TargetPlatform)99` instead of failing. The `Enum.IsDefined` guard ran before parsing, where it only ever checked the `default` value

### Build Layout
- Build output is centralized under `UnrealKit/Output/` instead of per-project `bin/` and `obj/` directories: `Output/Bin/<project>/<configuration>/<framework>/` and `Output/Obj/<project>/<configuration>/<framework>/`. Set via `BaseOutputPath` / `BaseIntermediateOutputPath` in `UnrealKit/Directory.Build.props`
- WPF XAML compilation temp projects (`UnrealKit.Desktop_<hash>_wpftmp`) now share the main project's intermediate directory, so they no longer accumulate one-off `obj` directories
- `DefaultItemExcludes` explicitly excludes `bin\**` and `obj\**`. The SDK only excludes the configured output paths from the source glob, so once those point at `Output\`, a leftover project-local `obj\` would otherwise be compiled in and fail with duplicate-attribute errors (CS0579)
- `Script/Run-Cli-*.bat` and `Script/Run-Gui-*.bat` updated to the new output paths. `Script/Publish-Shipping.bat` is unaffected because it passes `--output` explicitly. Existing per-project `bin/`/`obj/` directories can be deleted; both are already git-ignored

### Platform Abstraction (behavior changes)
- `PlatformNames` is now the single mapping between `TargetPlatform` and the `"Android"` / `"Win64"` contract strings used by archive directories and `.ukit`
- `IDeviceService.Supports(DeviceCapability)` replaces platform type checks. Unsupported operations throw `DeviceCapabilityNotSupportedException` instead of returning empty results — notably `StreamLogAsync` on Win64, which previously returned an empty stream indistinguishable from "connected, no logs yet"
- `AggregateDeviceProvider` reports per-platform enumeration failures instead of silently omitting a platform, so a missing `adb` surfaces as a stated reason in both CLI and GUI
- **Breaking:** an unrecognized `Platform` value in `Config/DefaultGame.ini` now fails with `Unsupported platform: '<value>'` instead of silently falling back to Android. A misspelling such as `Platform=Andriod` previously made a Win64 project capture as Android and report success. An absent value still uses the default
- **Breaking:** `capture import --platform` now defaults to the project's configured platform rather than always Android. Pass `--platform` explicitly to override
- `LaunchParameterService` and `ConsoleCommandService` now delegate to `IDeviceService`, so `commandline push/delete` and `app start` work on Win64 through the same code path as Android
- Console commands are now sent exclusively through UE Web Remote Control (`PUT /remote/object/call`) for both Android and Win64. Android no longer uses `am broadcast`; `IAdbService` now exposes `ForwardTcpAsync` for port forwarding
- New `Config/DefaultGame.ini` settings: `RemoteControlHttpPort` (default `30010`), `RemoteControlObjectPath`, `RemoteControlFunctionName`, and `RemoteControlCommandParameter`
- **Breaking:** `app console send` now requires `--project` because Remote Control endpoint/function configuration lives in the project config
- Win64 applications now launch with the working directory set to the executable's own directory, so UE resolves relative content paths identically under CLI and GUI

### Fixed
- A Remote Control HTTP timeout crashed the CLI with a bare stack trace instead of a reported error. `HttpClient` signals its timeout as `TaskCanceledException` (deriving from `OperationCanceledException`) with `TimeoutException` only in `InnerException`, so it was rethrown unwrapped and bypassed the expected-failure path. Timeouts are now `RemoteControlException` carrying the elapsed limit and target URI; caller-initiated cancellation still propagates as cancellation
- `RemoteControlService` allocated an undisposed `HttpClient` per instance, and the GUI builds a device service per operation, leaking a connection pool on every console command. The default client is now shared
- `adb forward` was re-issued on every console command, so a multi-step sequence spawned one redundant `adb` process per step and interleaved adb output into the sequence report. The forward now happens once per device; a failed forward is not recorded, so it retries on the next call
- An empty `RemoteControlObjectPath=`, `RemoteControlFunctionName=`, or `RemoteControlCommandParameter=` in `Config/DefaultGame.ini` bypassed the default-value fallback, because INI stores `Key=` as an empty string rather than absent. The project opened and validated cleanly and failed only at send time, naming a parameter instead of the config key. Empty now means absent for these three fields
- `capture run --skip-saved` was rejected as `Unsupported option: --skip-saved`. The flag was declared in the flag-options set but omitted from the allowed-options set, and option validation checks the allowlist first, so the documented flag could never be passed
- `Win64IntegrationTests` killed processes by the name `cmd`, terminating every `cmd.exe` on the machine — including child processes of tests running in parallel and the developer's own terminals. It now operates on a uniquely named copy. This was the cause of the intermittent `ProcessRunnerTests` timeout failure and of full-suite runs taking two minutes instead of two seconds
- `CaptureServiceTests` asserted against progress messages collected through `Progress<T>`, which dispatches callbacks to the thread pool; assertions could run before the callback and `List<T>` was written from pool threads. Now uses a synchronous `IProgress<T>` and a concurrent collection

### Static Camera HTML Report (Phase 2 P1)
- `StaticCameraHtmlReportService`: self-contained HTML report with device info, threshold-colored summary, collapsible per-camera detail cards, screenshot references, and diagnostics
- CLI: `parse static-camera --html-output <path>` generates HTML alongside text/json output
- WPF: "?? HTML ???" button on the static camera page with SaveFileDialog

### Trend Line Chart (Phase 2 P3)
- WPF trend page: new "Chart" tab with metric selector dropdown and Canvas-based line chart
- Zero external dependencies; pure WPF Canvas rendering with dot markers and date labels
- `TrendChartAxisLabel` record and `UpdateTrendChart()` method in `ShellViewModel`
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
