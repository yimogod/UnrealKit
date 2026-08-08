# UnrealKit

UnrealKit is a desktop tool for Unreal Engine Android performance data capture and analysis. It provides both a graphical interface and a command-line interface, sharing the same core business logic.

## Features

- **Project Management** — Create and manage `.ukit` projects (UTF-8 INI format) with UE-style `Config/DefaultGame.ini` defaults.
- **Device Management** — Enumerate, connect (Wi-Fi), and select Android devices via ADB.
- **Launch Parameters** — Push presets (LLM, OpenGL, Vulkan, Trace, No Update) or custom commands to `uecommandline.txt`.
- **Capture** — Real-time capture of `dumpsys meminfo` and UE Saved data, archived to `Content/<Platform>/<Tag>/<Date>/<CaptureId>/`.
- **Parsing** — Offline parsing of Android meminfo and UE memreport files with structured diagnostics.
- **Export** — Export to CSV, TSV, and real XLSX with metadata, details, and diagnostics.
- **GUI (WPF)** — Full desktop application with 8 pages: Project, Devices, Launch Params, Capture, Parse, Results, Export, Log & Settings.
- **CLI** — Full coverage: `project create/info/validate`, `adb`, `app start`, `commandline push/delete`, `capture run/list/info`, `parse`, `export`. Machine-readable `--format json` supported.

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
unrealkit parse capture-list --project <project.ukit> [--platform <platform>] [--tag <tag>]
unrealkit parse capture-files --capture-dir <path>
unrealkit parse capture-meminfo --project <project.ukit> --capture <capture-id> [--file <filename>] [--analysis-id <id>]
unrealkit export meminfo --input <file> --output <file.csv|file.tsv|file.xlsx> [--include-details] [--capture-id <id>]
unrealkit export memreport --input <file> --output <file.csv|file.tsv|file.xlsx> [--include-details] [--capture-id <id>]
```

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
├─ Saved/          (re-generatable derived data)
├─ Intermediate/   (cache, temp)
└─ Script/         (build and run scripts)
```

## Building from Source

```batch
:: Debug build
Script\Build-Debug.bat

:: Run tests (72 passing)
dotnet test UnrealKit\UnrealKit.Tests

:: Self-contained publish (win-x64 standalone)
Script\Publish-Shipping.bat
```

## Known Limitations

- ADB path resolution: explicit path > project config > environment > PATH. Multiple adb versions may conflict; use project config to disambiguate.
- Agent analysis adapters are reserved but not yet implemented.
- Phase 2 features (static camera reports, baseline diff, historical trends, RenderDoc integration) are planned for future releases.

## License

MIT — see [LICENSE](LICENSE).
