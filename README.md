# UnrealKit

UnrealKit is a desktop tool for Unreal Engine Android performance data capture and analysis. It provides both a graphical interface and a command-line interface, sharing the same core business logic.

## Features

- **Project Management** — Create and manage `.ukit` projects (UTF-8 INI format) with UE-style `Config/DefaultGame.ini` defaults.
- **Device Management** — Enumerate, connect (Wi-Fi), and select Android devices via ADB.
- **Launch Parameters** — Push presets (LLM, OpenGL, Vulkan, Trace, No Update) or custom commands to `uecommandline.txt`.
- **Capture** — Real-time capture of `dumpsys meminfo` and UE Saved data, archived to `Content/<Platform>/<Tag>/<Date>/<CaptureId>/`.
- **Parsing** — Offline parsing of Android meminfo, UE memreport, and static camera performance logs with structured diagnostics.
- **Export** — Export to CSV, TSV, and real XLSX with metadata, details, and diagnostics.
- **GUI (WPF)** — Full desktop application with 8 pages: Project, Devices, Launch Params, Capture, Parse, Results, Export, Log & Settings.
- **Baseline Diff** — Compare a current meminfo, memreport, or static camera report against a baseline, with per-metric delta, direction, and explicit missing-value handling.
- **Historical Trends** — Aggregate a metric across many captures filtered by platform, tag, device, and date range; export to CSV, TSV, or multi-sheet XLSX.
- **CLI** — Full coverage: `project create/info/validate`, `adb`, `app start`, `commandline push/delete`, `capture run/list/info`, `parse`, `export`, `analyze diff/trend`. Machine-readable `--format json` supported.

## Prerequisites

- Windows 10+ (x64)
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (for development)
- Android Debug Bridge (`adb`) on PATH or project-configured path
- Android device with USB debugging enabled (for device operations)

## Quick Start

```batch
:: Build
Script\Build-Shipping.bat

:: Run GUI
Script\Run-Gui-Shipping.bat

:: Run CLI
Script\Run-Cli-Shipping.bat project create MyProject --name MyPerformance
Script\Run-Cli-Shipping.bat adb devices
```

## Solution Structure

```text
UnrealKit/
├─ UnrealKit.Core/       Domain models, config, ADB, capture, parsing, export
├─ UnrealKit.Cli/        CLI argument binding and console output
├─ UnrealKit.Desktop/    WPF views and ViewModels
└─ UnrealKit.Tests/      Unit tests and sample data
```

## CLI Reference

```text
unrealkit project create <dir> --name <name>
unrealkit project info <project.ukit> [--format json]
unrealkit project validate <project.ukit>
unrealkit adb version [--adb-path <path>]
unrealkit adb devices [--adb-path <path>]
unrealkit adb connect <host:port> [--adb-path <path>]
unrealkit adb disconnect <host:port> [--adb-path <path>]
unrealkit app start --project <project.ukit> --device <serial> [--adb-path <path>]
unrealkit commandline push --project <project.ukit> --device <serial> [--preset <name>] [--custom <args>] [--remote-path <path>] [--adb-path <path>]
unrealkit commandline delete --project <project.ukit> --device <serial> [--remote-path <path>] [--adb-path <path>]
unrealkit capture run --project <project.ukit> --device <serial|auto> [--tag <tag>] [--format text|json] [--skip-saved] [--adb-path <path>]
unrealkit capture import --project <project.ukit> --source <directory> [--platform <platform>] [--tag <tag>] [--capture-id <id>]
unrealkit parse meminfo --input <file> [--format text|json]
unrealkit parse memreport --input <file> [--format text|json]
unrealkit parse static-camera --input <log> --screenshots <dir> [--format json]
unrealkit parse capture-list --project <project.ukit> [--platform <platform>] [--tag <tag>]
unrealkit parse capture-files --capture-dir <path>
unrealkit parse capture-meminfo --project <project.ukit> --capture <capture-id> [--file <filename>] [--analysis-id <id>]
unrealkit export meminfo --input <file> --output <file.csv|file.tsv|file.xlsx> [--include-details] [--capture-id <id>]
unrealkit export memreport --input <file> --output <file.csv|file.tsv|file.xlsx> [--include-details] [--capture-id <id>]
unrealkit analyze diff --baseline <file> --current <file> [--source meminfo|win64-meminfo|memreport|static-camera] [--metrics <list>] [--only-changed] [--format text|json]
unrealkit analyze diff --project <project.ukit> --baseline <capture-id> --current <capture-id> [--baseline-file <filename>] [--current-file <filename>] [--source <source>] [--metrics <list>] [--only-changed] [--format text|json]
unrealkit analyze trend --project <project.ukit> [--source <source>] [--platform <platform>] [--tag <tag>] [--device <serial>] [--from <yyyy-MM-dd>] [--to <yyyy-MM-dd>] [--metrics <list>] [--file <filename>] [--output <file.csv|file.tsv|file.xlsx>] [--include-points] [--format text|json]
```

`analyze diff` compares a current report against a baseline of the same type. Metrics are keyed as `Group/Name`; `--metrics` accepts either a bare name or the full `Group/Name`, comma-separated or repeated. A metric present on only one side reports as missing rather than zero. Exit code is non-zero when either report fails to parse.

`analyze trend` follows the same metrics across every capture matching the filters, oldest to newest. `--from` / `--to` take `yyyy-MM-dd` and are inclusive. A capture whose input is ambiguous or unparsable is excluded with a specific diagnostic rather than silently skipped — pass `--file` to name the file to read from each capture. Per-point deltas step from the previous capture that had a value, so a gap in the middle is not read as a drop to zero.

## Capture Directory Convention

```text
<ProjectRoot>/
├─ <ProjectName>.ukit
├─ Config/
│  └─ DefaultGame.ini
├─ Content/
│  └─ <Platform>/<Tag>/<YYYY-MM-DD>/<CaptureId>/
│     ├─ CaptureManifest.json
│     ├─ MemInfo/
│     ├─ Saved/
│     └─ Logs/
├─ Saved/          (re-generatable derived data: Exports, Analysis, Reports, Logs)
└─ Intermediate/   (cache, temp)
```

`Script/` lives at the repository root, not inside a `.ukit` project.

## Building from Source

```batch
:: Debug build
Script\Build-Debug.bat

:: Run tests (126 passing)
dotnet test UnrealKit\UnrealKit.Tests

:: Self-contained publish (win-x64 standalone)
Script\Publish-Shipping.bat
```

## Known Limitations

- ADB path resolution: explicit path > project config > environment > PATH. Multiple adb versions may conflict; use project config to disambiguate.
- Agent analysis adapters are reserved but not yet implemented.
- Baseline diff and historical trends are available via CLI only; the WPF diff and trend pages are not yet implemented.
- Remaining Phase 2 features (static camera HTML reports, RenderDoc integration, agent analysis) are planned for future releases.

## License

MIT — see [LICENSE](LICENSE).

## Roadmap

- Phase 1 (complete): see [Doc/PlanM1.md](Doc/PlanM1.md)
- Phase 2 (in progress): see [Doc/PlanM2.md](Doc/PlanM2.md)

## Development Conventions

Contributor conventions live in [CLAUDE.md](CLAUDE.md), with per-domain detail under [Doc/](Doc).
