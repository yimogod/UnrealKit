# Changelog

All notable changes to UnrealKit.

## [Unreleased]

### Console Command Channel Unifies On Web Remote Control HTTP
- Both Android and Win64 now send console commands over the engine's built-in Web Remote Control (`PUT http://127.0.0.1:{port}/remote/object/call`). The self-developed TCP channel (`TcpCommandTransport` and its UE-side `RemoteControlLite` plugin) is dropped, and the plugin directory is deleted separately — one protocol, one transport, no per-platform channel split
- `UnrealKit.Core.CommandChannel.ICommandTransport` now has a single `HttpCommandTransport` implementation, and `CommandTransportKind` has a single `Http` member. `CommandChannelOptions.CreateTransport()` takes no platform argument — the platform and the channel are no longer paired
- `ProjectSettings` reverts to the `RemoteControl*` fields for the channel: `RemoteControlHttpPort` (default `30010`, also the `adb forward` port on both ends), `RemoteControlObjectPath`, `RemoteControlFunctionName`, and `RemoteControlCommandParameter` — replacing `CommandTcpPort` / `AndroidCommandTransport` / `Win64CommandTransport`. An unrecognized or non-numeric port errors on project open instead of silently falling back; an empty value means absent and falls back to the default
- `adb forward` forwards `ICommandTransport.Port` rather than a separately-read config value, so the forwarded port and the connected port stay in lockstep
- **Android requires an engine-side edit.** The engine's `WebRemoteControl` module and `WebSocketNetworking.uplugin` carry a `PlatformAllowList` of `Mac` / `Win64` / `Linux` only, so an Android build does not compile the HTTP server at all. Adding `Android` to both allowlists is the user's responsibility, not the tool's; until then Android console commands fail with `UKC101` (connection refused) — the expected state, not a misconfiguration
- Diagnostic code domain `UKC`: `UKC101` connect failed or timed out, `UKC102` command failed (Remote Control returned a non-success HTTP status), `UKC103` protocol error (response missing, oversized, or not the expected shape). A non-success HTTP response is a command failure, never a silent success
- **Breaking:** `AdbDeviceService` and `Win64DeviceService` take `CommandChannelOptions` + `ICommandTransport`; `CommandChannelOptions` wraps `RemoteControlOptions`, and `RemoteControlService` / `IRemoteControlService` / `RemoteControlModels` are the HTTP client again
- Tests cover the HTTP request/response contract — payload shape (`objectPath`, `functionName`, `parameters.Command`, `generateTransaction`), non-success status, request validation before any network I/O, connection-refused detail surfacing, timeout-vs-cancellation, and the project-config round trip

### Download Latest Skips An Already-Present Build
- "下载最新" now checks whether the newest build directory already exists under `Intermediate/Download/<Platform>/<subdir>/` before connecting to the FTP server. If it is already there, the download is skipped with a `DWN007` "already up to date" informational diagnostic instead of deleting and re-downloading it. The local directory is the re-fetchable cache, so a present copy is treated as good unless the user removes it. New `DownloadDiagnosticCodes.AlreadyUpToDate` (`DWN007`); `DownloadResult.Succeeded` remains true for this path, and both the CLI `unrealkit download` and the WPF 安装包 page surface the skip distinctly

### Launch Application Stops The Running Instance First
- The launch-parameter page's "启动应用" now force-stops any running instance before starting it, so a hot-started app no longer keeps running the old `uecommandline.txt`. The stop is best-effort: an app that isn't running is logged as a warning and start still proceeds, never surfacing "nothing to stop" as a launch failure
- New `ILaunchParameterService.StopApplicationAsync`. It targets `PlatformTarget.ProcessIdentity` (package name on Android, process name on Win64) rather than `LaunchTarget`, which on Win64 is the full executable path and would never match `GetProcessesByName`

### Launch Parameter Presets Use Groups For Mutual Exclusion
- Replaced the per-preset `IsComposable` bool with launch parameter **groups**. A group declares a mode (`Exclusive` = at most one member selectable; `Coexist` = no constraint) plus its members, so the real conflict is expressible: `Render` group makes OpenGL and Vulkan mutually exclusive while either still composes with `Mem.LLM` / trace / remote-control presets. Ungrouped presets compose freely
- **Breaking:** `LaunchParameterPreset` drops `IsComposable`; `ProjectSettings` gains `LaunchParameterGroups` (`IReadOnlyList<LaunchParameterPresetGroup>`). `LaunchParameterPresetGroup(Name, Mode, Members)` with `LaunchParameterGroupMode` enum (`Coexist`/`Exclusive`). `BuildContent` now validates by group membership instead of a preset flag, so the earlier false exclusion (`Profile.RemoteControl` + `Mem.LLM` failing) no longer happens
- New `[UnrealKit.LaunchPresetGroups]` section in `Config/DefaultGame.ini` (`Render=Exclusive:Render.OpenGL,Render.Vulkan`), layered like the rest of the config — `BaseGame.ini` holds team defaults, the project overrides. An unrecognized mode or a missing `:` errors (`InvalidDataException`) rather than silently treating the group as exclusive. Built-in default is a single `Render` exclusive group
- The GUI launch-parameter list shows each preset's group label (`互斥组：Render` / `同组：…`); ungrouped presets show nothing

### Build Download From FTP
- New `unrealkit download` CLI verb plus a WPF "安装包" page: pull the latest build for a platform from FTP — the newest subdirectory by natural sort (numeric segments by value, text segments case-insensitive), downloading the single `.apk` (Android) or the whole subdirectory (Win64 exe + resources). `unrealkit download install` installs a local APK to a connected Android device
- FTP host/port/credentials live in a new shared `[UnrealKit.Ftp]` section (`Host`/`Port`/`Username`/`Password`) on `ProjectSettings.Ftp` (`FtpDownloadSettings`); each platform's parent directory is its own profile's `FtpPath`. Host or FtpPath blank fails before any network I/O, naming the missing field. `Port` empty falls back to `21`; a non-1–65535 value errors instead of silently defaulting
- New `UnrealKit.Core.Download` namespace: `FtpDownloadService` orchestration isolated behind `IFtpClient`/`IFtpClientFactory`, backed by `FluentFTP` (`AsyncFtpClient`) in `FluentFtpClientAdapter`. Downloads land under `Intermediate/Download/<Platform>/<subdir>/` — the deletable cache, never `Content/`
- New diagnostic code domain `DWN` (`DWN001` no subdirectory, `DWN002` connect failed, `DWN003` list failed, `DWN004` multiple apks, `DWN005` no apk, `DWN006` download failed). Multiple apks error out listing candidates rather than picking one (implicit-choice invariant)
- `Password` is sensitive: the GUI masks it with a `PasswordBox` and logs never print it. This is a documented exception to the "no secrets in project config" convention, since credentials in `DefaultGame.ini` were explicitly requested — keep the file out of public repositories
- `InstallApplication` capability added to `DeviceCapability`/`IDeviceService`; `AdbDeviceService` forwards to `adb -s <serial> install -r <apk>` (parameterized), `Win64DeviceService` reports it unsupported. GUI confirms before installing, showing the full device and package path

### Launch Parameter Remote Path Is Fixed Per Platform
- **Breaking:** removed `commandline push/delete --remote-path` and the GUI "远端 uecommandline.txt 路径" field. `uecommandline.txt` always lives at the platform's fixed game root (`{GameRootPath}/uecommandline.txt`), which UE itself decides — Android and Win64 already resolve this from `GameRootTemplate`/`WorkingDirectory`. A free-form override invited a path that diverged from the engine's actual read location, so push/delete silently missed the file the game was reading. `LaunchParameterRequest.RemotePathOverride`, `ILaunchParameterService.GetRemotePath`/`DeleteAsync` override parameters, and `LaunchParameterService.ValidateOverridePath` are removed.

### Platform Scope

A single analysis session targets one platform — this run looks at the Windows build, the next at Android. The GUI now has one place to say which, and the device list, capture archive list, and history trend all narrow to it.

- New platform selector in the GUI title bar, next to the Project and Log menus. It covers all three lists at once; putting it inside any one page would force the other two to ask again. Defaults to `All`, which filters nothing
- **The scope is a view filter, not a "current platform" setting.** Which platform an operation runs against is still derived from the selected device (`ProjectSettings.ResolveTarget`), exactly as before. `.ukit` v2 removed the `Platform` field for a reason — a config value and a selected device would be two contradicting sources of truth, and whichever won would be wrong in the other's terms. New `UnrealKit.Core.Projects.PlatformScope`; no `.ukit` or `DefaultGame.ini` field is added
- Scope persists per project in the project's own `Config/UserSetting.ini` (`[UnrealKit.Scope] Platform`), restored when that project opens. "Which platform I look at" belongs to the project, not to the installation — recording it app-wide would carry one project's scope into the next and silently hide the other platform's devices and archives. It sits beside `DefaultGame.ini` rather than inside it because that file is versioned project config and should not diff over who last viewed which platform. An unrecognized or stale value is treated as no record, and no record keeps the current scope rather than resetting it to `All`. With no project open the scope is a session-only choice and is not written anywhere. New `IUserSettingStore` / `UserSettingStore` and `UkitProject.UserSettingFilePath`
- Device enumeration narrows with the scope, matching the CLI's `--platform`: scoping to Win64 no longer starts `adb`, so a missing `adb` stops being a failure reason for local operations
- `ShellViewModel.Devices` keeps the unfiltered list and the new `ScopedDevices` is what the grid binds. Both are needed to tell "no devices on this platform" apart from "devices exist but the scope hides them", and the Devices page states which case it is instead of letting a shorter list read as a disconnection
- A selected device that falls outside a newly chosen scope is cleared; one that stays inside keeps its selection. Auto-selecting the single available device still happens, but now only within the scope — the scope is the user's explicit choice, so it is not the implicit selection `Doc/设备操作与文件安全.md` rules out

### Win64 Captures Were Invisible In Analysis Lists (bug fix)
- `CaptureAnalysisService.ListCaptureDirectoriesAsync` defaulted to the `Android` platform directory when no platform was passed, and every caller passed nothing: the GUI capture list and history trend, `unrealkit parse capture-list`, and `unrealkit analyze diff`. **Win64 archives under `Content/Win64/` were silently skipped** — neither listed nor reported, which reads as "never captured". Null now scans every platform directory
- Same-date archives are ordered by capture id as a tiebreak. Directory enumeration order comes from the filesystem, so sorting by date alone let "the most recent one" move between refreshes
- `unrealkit parse --capture <id>` now reports ambiguity instead of taking the first match, matching what `analyze diff` already did. Searching every platform makes one id resolvable to several archives, and picking one silently is the implicit choice invariant #4 forbids
- The GUI capture list states the total and the scope when it truncates at 200 entries; silently showing the first 200 made a missing archive read as "not captured"

### Device Aliases
- Device lists now show the device id explicitly labelled as such (Android: ADB serial; Win64: `localhost`), plus an optional alias configured per device. Same-model test devices were previously indistinguishable in the list — the id column and the self-reported model were all there was
- New `[UnrealKit.DeviceAliases]` section in `Config/DefaultGame.ini`, keyed by device id (`6d062c71=小米平板5-测试机A`). The key is the same value as `IDevice.Id` and the CLI's `--device`, so an alias resolves anywhere devices are listed without a second device query. Lookup is case-insensitive, matching `--device` matching. Aliases merge through the layered INI, so `BaseGame.ini` can hold team-wide aliases that a project overrides
- **Aliases are display-only and never participate in device selection.** Captures, launches, and console commands still resolve by device id, and that is what logs and archives record. Allowing selection by alias would turn "the same alias configured on two devices" into an implicit choice, which `Doc/设备操作与文件安全.md` rules out
- A device with no configured alias leaves the column empty rather than falling back to its id or model — a placeholder reads as "the alias is literally this"
- New `ProjectSettings.DeviceAliases` (`DeviceAliasMap`) and `UnrealKit.Core.Devices.DeviceDisplayInfo`. Alias resolution lives in Core so GUI and CLI share one rule; two independent implementations would drift and show the same device under different names in each interface. `DeviceDisplayInfo` deliberately does not implement `IDevice` — it is a display projection, and letting it pose as a device would let captures and commands accept it and bypass the real state from device enumeration. Operations take `DeviceDisplayInfo.Device`
- Blank entries are dropped on read: INI stores `Key=` as an empty string, which would otherwise surface as a device whose alias is empty
- `unrealkit devices` gains an optional `--project` — aliases live in project config, so without it the command lists devices alone rather than guessing a project, since the wrong guess would show another set of devices' aliases. It also now genuinely accepts `--adb-path`: the previous implementation read the option but rejected any argument at all before doing so, so it was unreachable
- The GUI Devices page has labelled 设备 id / 状态 / 型号 / 别名 columns and states where aliases are configured. The selected-device summary and the launch target summary append the alias after the id, never in place of it

### Reopen Last Project On Startup
- The GUI reopens the last project on launch. The path comes from the app's own settings file, `<program dir>\Config\EditorSetting.ini` (`[UnrealKit.RecentProject] LastProjectFilePath`), written whenever a project is opened or created. New `ApplicationPaths.AppConfigDir` and `IEditorSettingStore` / `EditorSettingStore` in `UnrealKit.Core.Runtime`
- Kept out of `.ukit` and `Config/DefaultGame.ini`: it cannot live in a project, since you have to know which project to open before you can read its config. It sits next to the distributed `BaseGame.ini`, and an unreadable file is treated as "no record" rather than blocking startup
- When the recorded project is missing or fails to open, a dialog shows the full path and the reason, and the shell returns to the "no project open" state so the user creates or opens one from the menu. The record is not cleared and no other project is substituted
- Failing to write the record degrades to an operation-log entry — the project is already open, so it is not reported as an open failure
- Restore runs on window `Loaded` (the alert needs a shown owner window) and only on first show, so a project the user switches to afterwards is not overwritten

### Device IP Addresses
- `IAdbService.GetIpAddressesAsync` returns a device's IPv4 addresses per interface (`DeviceIpAddress`, classified by `DeviceNetworkInterfaceKind`: WiFi / Cellular / UsbTethering / Vpn / Other). A device can hold several addresses at once, so the result is a list — callers pick by interface kind instead of the service guessing which one is wanted
- Kept out of `ListDevicesAsync`: it costs a real device shell call and fails on offline or unauthorized devices, which would make device-list refresh slower and add a failure point. `AdbDevice` is unchanged
- Queries `ip -f inet addr` first (gives interface name and prefix length), falling back to `ip route`'s `src` address (no prefix length) on firmware where the former is unavailable. `getprop dhcp.wlan0.ipaddress` is deliberately not used — it is frequently empty on current Android and would silently return a wrong answer. Loopback is excluded
- When neither command yields an address, `AdbDeviceAddressUnavailableException` lists the commands attempted, so "device is on no network" stays distinguishable from "the query never ran"
- New `unrealkit adb ip <serial> [--adb-path <path>]` prints one line per interface
- The GUI Devices page has a 获取 IP button, enabled only for a selected Android device in `device` state. Every interface goes to the operation log (`DeviceIp` category); the inline summary shows the WiFi address, falling back to all interfaces when there is no WiFi. The summary resets when the selected device changes, so one device's address is never read as another's

### Saved Directory Derived From Game Directory (breaking)
- **Breaking:** `AndroidPlatformProfile.SavedRootTemplate` and its `[UnrealKit.Platform.Android] SavedRootTemplate` INI key are removed, along with `AndroidPlatformProfile.DefaultSavedRootTemplate`. The device Saved path is now `GameRootTemplate` + `/Saved`, matching what Win64 already did and what UE itself lays out on disk. Two independently-configured paths could drift apart, and a Saved path pointing outside the game directory makes a capture pull an empty directory while reporting success
- `PlatformProfile.SavedDirectoryName` (`"Saved"`) is the single definition of that subdirectory name, shared by both platforms
- A leftover `SavedRootTemplate=` line in an existing `Config/DefaultGame.ini` is ignored, not an error — no rewrite is required. If it pointed somewhere other than `<GameRoot>/Saved`, the effective capture source changes, so check it before the next capture
- GUI settings show 设备 UE Game 路径模板 in place of the Saved template, with the derived Saved path displayed read-only underneath so the actual capture location stays visible

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
