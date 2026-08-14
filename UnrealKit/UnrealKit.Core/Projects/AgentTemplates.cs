namespace UnrealKit.Core.Projects;

/// <summary>
/// 生成AGENTS.md的模版
/// </summary>
public static class AgentTemplates
{
    public const string AgentsMdFileName = "AGENTS.md";
    public const string SkillFileName = "SKILL.md";
    public const string SkillDirectory = ".codex/skills/ukit-analyze";

    /// <summary>
    /// 宪章的内容
    /// </summary>
    public static string AgentsMdContent(string projectName) => $@"# {projectName}  Performance Analysis

This is an UnrealKit performance capture project for Unreal Engine Android builds.
The data collected here is read-only; analysis results go to `Saved/`.

## Project structure

- `{projectName}.ukit`  Project descriptor
- `Config/DefaultGame.ini`  Package name, paths, launch presets
- `Content/`  Capture archives (authoritative, read-only)
- `Saved/`  Exports, reports, and analysis output
- `Intermediate/`  Temporary data

Each capture lives at `Content/<Platform>/<Tag>/<YYYY-MM-DD>/<CaptureId>/`
and contains a `CaptureManifest.json` with device info, timing, and input file hashes.

## Analysis workflow

1. List captures: read `Content/` directory tree or parse manifests.
2. Choose a capture and identify its input files (meminfo, memreport, static-camera log).
3. Parse the relevant file(s) using the rules in [Doc/解析导出与诊断.md].
4. Compare against a baseline (another capture) or historical trend when available.
5. Write the analysis report to `Saved/Analysis/<AnalysisId>/`.

## Diagnostic codes (summary)

| Prefix | Domain |
|--------|--------|
| UKIT*  | Project / infrastructure |
| AMI*   | Android meminfo parsing |
| UMR*   | UE memreport parsing |
| SCP*   | Static camera performance |
| RDC*   | RenderDoc integration |

Warnings (non-zero but usable result) must not be escalated to failures.
See [Doc/解析导出与诊断.md] for full code reference.

## Key rules

- **Raw data is read-only**: never modify, rename, or overwrite files in `Content/`.
- **No implicit selection**: if multiple captures or files match, ask explicitly.
- **Fail specifically**: if parsing fails, report what is missing and why.
- **Extension honesty**: `.xlsx` must be real XLSX; tab-delimited is `.tsv`, comma-delimited is `.csv`.
- **Non-destructive output**: default to timestamped directories; ask before overwriting.

## Analysis report format

Save reports to `Saved/Analysis/<yyyyMMdd-HHmmss>/` containing:

- `report.md`  Main analysis text
- `inputs.json`  List of captures, files, and versions used
- `diag.json`  Structured diagnostic summary (if applicable)

Conclusions must distinguish:
- **Fact** (measured value)
- **Rule-based judgment** (threshold comparison)
- **Inference / recommendation** (speculative)

## Constraints

- Analysis must be based on explicitly selected captures or files.
- Show the input scope, rules/prompts, and any external services before running.
- Prefer Core-parsed strong-typed data and controlled summaries over raw logs.
- The Agent may only generate analysis and reports, never delete, overwrite, or upload raw capture data without explicit user confirmation.
";

    /// <summary>
    /// 技能的内容
    /// </summary>
    public static string SkillMdContent => @"# ukit-analyze

Analyze Unreal Engine Android performance capture data from an UnrealKit project.

## Inputs

- A UnrealKit project directory (contains `.ukit` descriptor)
- One or more capture directories under `Content/<Platform>/<Tag>/<date>/<CaptureId>/`

## Process

1. Read `CaptureManifest.json` to understand the capture context.
2. Identify input files: meminfo `.txt`, memreport `.memreport`, or static-camera `.log`.
3. Parse the data according to the diagnostic code conventions (see AGENTS.md).
4. If comparing two captures, compute per-metric deltas with direction (higher-is-worse/lower-is-worse).
5. For trends, aggregate captures by tag/platform/device and compute series.
6. Generate a structured report in `Saved/Analysis/<id>/`.

## Output

- `report.md` with sections: Summary, Key Findings, Detailed Metrics, Recommendations
- `inputs.json` enumerating captures and files analyzed
- Optional `diag.json` with structured diagnostic data

## Diagnostic codes quick reference

- AMI001-AMI299: Android meminfo warnings/errors
- UMR001-UMR299: UE memreport warnings/errors
- SCP001-SCP299: Static camera performance warnings/errors
- RDC001-RDC099: RenderDoc execution issues

## Memory metrics (meminfo)

Key fields: TotalPssKb, NativeHeap, DalvikHeap, .so mmap, .apk mmap.
Higher is generally worse; direction is `higher_is_worse`.

## MemReport fields

Categories: Physical, Virtual, Texture, RenderTarget, StaticMesh, SkeletalMesh, etc.
Compare sizes in MB; direction is `higher_is_worse`.

## Static camera

Look for `!!!Do Perf Start!!!` / `!!!Do Perf End!!!` blocks with `PointNum:` markers.
Each point records frame timing, draw calls, and triangle counts.
Direction: `lower_is_worse` for frame time, GPU time, draw calls, and triangles.
";
}
