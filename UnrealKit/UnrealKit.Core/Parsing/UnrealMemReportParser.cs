using UnrealKit.Core.Diagnostics;
using System.Text.RegularExpressions;

namespace UnrealKit.Core.Parsing;

public sealed class UnrealMemReportParser : IUnrealMemReportParser
{
    public async Task<UnrealMemReportParseResult> ParseFileAsync(string inputFilePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputFilePath);
        var fullPath = Path.GetFullPath(inputFilePath);
        if (Directory.Exists(fullPath)) throw new ArgumentException("Input must be a file, not a directory.", nameof(inputFilePath));
        if (!File.Exists(fullPath)) throw new FileNotFoundException("MemReport input file was not found.", fullPath);
        return Parse(fullPath, await File.ReadAllLinesAsync(fullPath, cancellationToken));
    }

    public UnrealMemReportParseResult Parse(string inputPath, IReadOnlyList<string> lines)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentNullException.ThrowIfNull(lines);
        var changelistLine = lines.FirstOrDefault(line => line.StartsWith("Changelist:", StringComparison.OrdinalIgnoreCase));
        if (changelistLine is null)
        {
            return new UnrealMemReportParseResult(inputPath, null,
                [new Diagnostic(DiagnosticSeverity.Error, "UMR101", "Missing required Changelist metadata.", inputPath, "Select a complete UE memreport file.")]);
        }

        var changelist = changelistLine.Split(':', 2)[1].Trim();
        var diagnostics = new List<Diagnostic>();
        var summary = new UnrealMemReportSummary(ParseMetrics(inputPath, lines, diagnostics));
        var sections = ParseDetails(inputPath, lines, diagnostics);
        return new UnrealMemReportParseResult(inputPath, new UnrealMemReport(changelist, summary, sections.Textures, sections.RenderTargets, sections.Objects, sections.TextureDetails, sections.TextureStats), diagnostics);
    }

    private static IReadOnlyList<UnrealMemReportMetric> ParseMetrics(string inputPath, IReadOnlyList<string> lines, List<Diagnostic> diagnostics)
    {
        var definitions = new[]
        {
            ("Wwise", "SoundEngine Reserved"), ("Wwise", "SoundBank"), ("Lua", "Lua Memory"),
            ("Texture Streaming", "Average Required PoolSize"), ("Texture Streaming", "Wanted Mips"), ("Texture Streaming", "NonStreaming Mips"),
            ("Shader", "Shader"), ("RHI", "RHI Buffer"), ("RHI", "RHI Texture"), ("RHI", "RHI Render Target"),
            ("LLM Platform", "FMalloc"), ("LLM Platform", "Total"), ("LLM Full", "Total")
        };
        var values = new Dictionary<string, UnrealMemReportMetric>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < lines.Count; index++)
        {
            var parts = lines[index].Split(':', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2) continue;
            var definition = definitions.FirstOrDefault(item => string.Equals(item.Item2, parts[0], StringComparison.OrdinalIgnoreCase));
            if (definition.Item2 is null) continue;
            if (values.ContainsKey(definition.Item2))
            {
                diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "UMR201", $"Duplicate summary metric: {definition.Item2}.", inputPath, "The first value is retained.", index + 1));
                continue;
            }

            var tokens = parts[1].Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (!TryParseKb(tokens, out var valueKb))
            {
                diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "UMR202", $"Invalid memory value for {definition.Item2}.", inputPath, "Use a numeric value and memory unit.", index + 1));
                values.Add(definition.Item2, new UnrealMemReportMetric(definition.Item1, definition.Item2, null, parts[1], UnrealMemReportMetricStatus.Invalid, index + 1));
                continue;
            }

            values.Add(definition.Item2, new UnrealMemReportMetric(definition.Item1, definition.Item2, valueKb, parts[1], UnrealMemReportMetricStatus.Parsed, index + 1));
        }

        return definitions.Select(item => values.TryGetValue(item.Item2, out var value) ? value : new UnrealMemReportMetric(item.Item1, item.Item2, null, null, UnrealMemReportMetricStatus.Missing, null)).ToArray();
    }

    private static bool TryParseKb(IReadOnlyList<string> tokens, out long valueKb)
    {
        valueKb = 0;
        if (tokens.Count == 0 || !decimal.TryParse(tokens[0].Replace(",", string.Empty, StringComparison.Ordinal), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var value)) return false;
        var unit = tokens.Count > 1 ? tokens[1].ToUpperInvariant() : "KB";
        var multiplier = unit switch
        {
            "B" => 1m / 1024m,
            "KB" or "KIB" or "K" => 1m,
            "MB" or "MIB" or "M" => 1024m,
            "GB" or "GIB" or "G" => 1024m * 1024m,
            _ => 0m
        };
        if (multiplier == 0m) return false;
        valueKb = decimal.ToInt64(decimal.Round(value * multiplier, 0, MidpointRounding.AwayFromZero));
        return true;
    }

    private static DetailSections ParseDetails(string inputPath, IReadOnlyList<string> lines, List<Diagnostic> diagnostics)
    {
        var textures = new List<UnrealMemReportTexture>();
        var renderTargets = new List<UnrealMemReportRenderTarget>();
        var objects = new List<UnrealMemReportObject>();
        var section = DetailSection.None;
        var inListTexturesBlock = false;

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];

            // Skip the structured ListTextures block entirely — it is parsed separately by ParseListTextures.
            if (line.Contains("Begin command \"ListTextures\"", StringComparison.OrdinalIgnoreCase)) { inListTexturesBlock = true; continue; }
            if (line.Contains("End command \"ListTextures\"", StringComparison.OrdinalIgnoreCase)) { inListTexturesBlock = false; continue; }
            if (inListTexturesBlock) continue;

            var detectedSection = DetectSection(line);
            if (detectedSection != DetailSection.None)
            {
                section = detectedSection;
                continue;
            }

            if (section == DetailSection.None || IsTableDecoration(line)) continue;
            var lineNumber = index + 1;
            switch (section)
            {
                case DetailSection.Textures:
                    if (TryParseResource(line, lineNumber, out var texture)) textures.Add(texture);
                    else if (LooksLikeDetailRow(line)) diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "UMR304", "Invalid texture detail row.", inputPath, "Use a resource name and, where available, dimensions and memory unit.", lineNumber));
                    break;
                case DetailSection.RenderTargets:
                    if (TryParseResource(line, lineNumber, out var renderTarget)) renderTargets.Add(new UnrealMemReportRenderTarget(renderTarget.Name, renderTarget.Width, renderTarget.Height, renderTarget.Format, renderTarget.MemoryKb, renderTarget.RawLine, renderTarget.LineNumber));
                    else if (LooksLikeDetailRow(line)) diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "UMR305", "Invalid render target detail row.", inputPath, "Use a resource name and, where available, dimensions and memory unit.", lineNumber));
                    break;
                case DetailSection.Objects:
                    if (TryParseObject(line, lineNumber, out var memoryObject)) objects.Add(memoryObject);
                    else if (LooksLikeDetailRow(line)) diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "UMR306", "Invalid object detail row.", inputPath, "Use a class name and object count or memory value.", lineNumber));
                    break;
            }
        }

        AddMissingSectionDiagnostic(inputPath, diagnostics, "UMR301", "texture", textures.Count);
        AddMissingSectionDiagnostic(inputPath, diagnostics, "UMR302", "render target", renderTargets.Count);
        AddMissingSectionDiagnostic(inputPath, diagnostics, "UMR303", "object", objects.Count);

        var (textureDetails, textureStats) = ParseListTextures(lines);
        return new DetailSections(textures, renderTargets, objects, textureDetails, textureStats);
    }

    // Parses the block between "MemReport: Begin command "ListTextures"" and
    // "MemReport: End command "ListTextures"". Lines before "Total size:" are
    // per-texture rows; lines starting with "Total " are stats lines.
    private static (IReadOnlyList<UnrealMemReportTextureDetail> Details, IReadOnlyList<UnrealMemReportTextureStat> Stats) ParseListTextures(IReadOnlyList<string> lines)
    {
        const string beginMarker = "Begin command \"ListTextures\"";
        const string endMarker = "End command \"ListTextures\"";
        const string totalPrefix = "Total size:";

        var details = new List<UnrealMemReportTextureDetail>();
        var stats = new List<UnrealMemReportTextureStat>();

        var inBlock = false;
        var pastTotalSize = false;

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (!inBlock)
            {
                if (line.Contains(beginMarker, StringComparison.OrdinalIgnoreCase)) inBlock = true;
                continue;
            }

            if (line.Contains(endMarker, StringComparison.OrdinalIgnoreCase)) break;

            if (!pastTotalSize && line.TrimStart().StartsWith(totalPrefix, StringComparison.OrdinalIgnoreCase))
            {
                pastTotalSize = true;
            }

            if (pastTotalSize)
            {
                if (TryParseTextureStat(line, index + 1, out var stat)) stats.Add(stat);
            }
            else
            {
                if (TryParseTextureDetail(line, index + 1, out var detail)) details.Add(detail);
            }
        }

        return (details, stats);
    }

    // Parses a texture detail row from ListTextures block.
    // Format: CookedW x CookedH (CookedKB KB, CookedBias), InMemW x InMemH (InMemKB KB), Format, LODGroup, Name, Streaming, UnknownRef, VT, UsageCount, NumMips, Uncompressed
    // CookedBias may be "?" so we cannot split on commas — the parenthesised groups are extracted by regex first.
    private static readonly Regex TextureDetailPattern = new(
        @"^(?<cw>\d+)\s*[xX×]\s*(?<ch>\d+)\s*\(\s*(?<ckb>[\d.]+)\s*KB\s*,\s*(?<bias>[^)]+)\)\s*,\s*(?<iw>\d+)\s*[xX×]\s*(?<ih>\d+)\s*\(\s*(?<ikb>[\d.]+)\s*KB\s*\)\s*,\s*(?<tail>.+)$",
        RegexOptions.Compiled);

    private static bool TryParseTextureDetail(string line, int lineNumber, out UnrealMemReportTextureDetail detail)
    {
        detail = default!;
        var trimmed = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return false;

        var m = TextureDetailPattern.Match(trimmed);
        if (!m.Success) return false;

        var tail = m.Groups["tail"].Value.Split(',');
        detail = new UnrealMemReportTextureDetail(
            m.Groups["cw"].Value, m.Groups["ch"].Value,
            m.Groups["ckb"].Value, m.Groups["bias"].Value.Trim(),
            m.Groups["iw"].Value, m.Groups["ih"].Value,
            m.Groups["ikb"].Value,
            tail.Length > 0 ? tail[0].Trim() : string.Empty,
            tail.Length > 1 ? tail[1].Trim() : string.Empty,
            tail.Length > 2 ? tail[2].Trim() : string.Empty,
            tail.Length > 3 ? tail[3].Trim() : string.Empty,
            tail.Length > 4 ? tail[4].Trim() : string.Empty,
            tail.Length > 5 ? tail[5].Trim() : string.Empty,
            tail.Length > 6 ? tail[6].Trim() : string.Empty,
            tail.Length > 7 ? tail[7].Trim() : string.Empty,
            tail.Length > 8 ? tail[8].Trim() : string.Empty,
            line, lineNumber);
        return true;
    }

    // Parses "Total PF_* size: InMem= X MB  OnDisk= Y MB" and
    // "Total TEXTUREGROUP_* size: InMem= X MB  OnDisk= Y MB"
    private static bool TryParseTextureStat(string line, int lineNumber, out UnrealMemReportTextureStat stat)
    {
        stat = default!;
        var trimmed = line.Trim();
        var match = Regex.Match(trimmed,
            @"^Total\s+(?<label>\S+)\s+size:\s+InMem=\s*(?<inmem>[\d.]+)\s*MB\s+OnDisk=\s*(?<ondisk>[\d.]+)\s*MB",
            RegexOptions.IgnoreCase);
        if (!match.Success) return false;
        stat = new UnrealMemReportTextureStat(match.Groups["label"].Value, match.Groups["inmem"].Value, match.Groups["ondisk"].Value, line, lineNumber);
        return true;
    }

    private static void AddMissingSectionDiagnostic(string inputPath, List<Diagnostic> diagnostics, string code, string sectionName, int rowCount)
    {
        if (rowCount == 0)
        {
            diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, code, $"No {sectionName} detail rows were found.", inputPath, "Confirm that the memreport includes the corresponding detail command output."));
        }
    }

    private static DetailSection DetectSection(string line)
    {
        var normalized = line.Trim();
        if (normalized.Contains("render target", StringComparison.OrdinalIgnoreCase)) return DetailSection.RenderTargets;
        if (normalized.Contains("listing all textures", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith("texture memory", StringComparison.OrdinalIgnoreCase) || normalized.Equals("textures", StringComparison.OrdinalIgnoreCase)) return DetailSection.Textures;
        if (normalized.StartsWith("obj list", StringComparison.OrdinalIgnoreCase) || normalized.Contains("object list", StringComparison.OrdinalIgnoreCase) || normalized.Equals("objects", StringComparison.OrdinalIgnoreCase)) return DetailSection.Objects;
        return DetailSection.None;
    }

    private static bool TryParseResource(string line, int lineNumber, out UnrealMemReportTexture resource)
    {
        resource = default!;
        var trimmed = line.Trim();
        if (IsHeaderRow(trimmed)) return false;
        var name = ExtractNamedValue(trimmed, "Name") ?? ExtractResourceName(trimmed);
        if (string.IsNullOrWhiteSpace(name)) return false;
        var dimensions = Regex.Match(trimmed, @"(?<width>\d+)\s*[xX×]\s*(?<height>\d+)");
        var format = ExtractNamedValue(trimmed, "Format") ?? Regex.Match(trimmed, @"\bPF_[A-Za-z0-9_]+\b", RegexOptions.IgnoreCase).Value;
        var width = ParseInt(dimensions, "width");
        var height = ParseInt(dimensions, "height");
        var memoryKb = ExtractMemoryKb(trimmed);
        if (width is null && height is null && string.IsNullOrWhiteSpace(format) && memoryKb is null) return false;
        resource = new UnrealMemReportTexture(name, width, height, string.IsNullOrWhiteSpace(format) ? null : format, memoryKb, line, lineNumber);
        return true;
    }

    private static bool TryParseObject(string line, int lineNumber, out UnrealMemReportObject memoryObject)
    {
        memoryObject = default!;
        var trimmed = line.Trim();
        if (IsHeaderRow(trimmed)) return false;
        var className = ExtractNamedValue(trimmed, "Class") ?? ExtractNamedValue(trimmed, "ClassName") ?? ExtractFirstTableValue(trimmed);
        if (string.IsNullOrWhiteSpace(className)) return false;
        var count = ExtractLongValue(trimmed, "Count") ?? ExtractLongValue(trimmed, "Num") ?? ExtractLongValue(trimmed, "Objects");
        var memoryKb = ExtractMemoryKb(trimmed) ?? ExtractKbValue(trimmed, "NumKBytes") ?? ExtractKbValue(trimmed, "MemoryKB");
        if (count is null && memoryKb is null) return false;
        memoryObject = new UnrealMemReportObject(className, count, memoryKb, line, lineNumber);
        return true;
    }

    private static string? ExtractResourceName(string line)
    {
        var tableValue = ExtractFirstTableValue(line);
        if (tableValue is not null) return tableValue;
        var name = Regex.Replace(line, @"\b\d+\s*[xX×]\s*\d+\b", string.Empty);
        name = Regex.Replace(name, @"[-+]?\d[\d,.]*\s*(?:B|KB|KiB|MB|MiB|GB|GiB)\b", string.Empty, RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"\bPF_[A-Za-z0-9_]+\b", string.Empty, RegexOptions.IgnoreCase).Trim(' ', ',', '|', ':', '-');
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static string? ExtractFirstTableValue(string line)
    {
        if (!line.Contains('|')) return null;
        return line.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static string? ExtractNamedValue(string line, string name)
    {
        var match = Regex.Match(line, $@"\b{Regex.Escape(name)}\s*[:=]\s*(?<value>[^|,;]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    private static long? ExtractLongValue(string line, string name)
    {
        var value = ExtractNamedValue(line, name);
        return value is not null && long.TryParse(value.Replace(",", string.Empty, StringComparison.Ordinal), out var parsed) ? parsed : null;
    }

    private static long? ExtractKbValue(string line, string name)
    {
        var value = ExtractNamedValue(line, name);
        return value is not null && decimal.TryParse(value.Replace(",", string.Empty, StringComparison.Ordinal), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? decimal.ToInt64(decimal.Round(parsed, 0, MidpointRounding.AwayFromZero)) : null;
    }

    private static long? ExtractMemoryKb(string line)
    {
        var matches = Regex.Matches(line, @"(?<value>[-+]?\d[\d,.]*)\s*(?<unit>B|KB|KiB|MB|MiB|GB|GiB)\b", RegexOptions.IgnoreCase);
        if (matches.Count == 0) return null;
        var match = matches[^1];
        return TryParseKb([match.Groups["value"].Value, match.Groups["unit"].Value], out var valueKb) ? valueKb : null;
    }

    private static int? ParseInt(Match match, string groupName) => match.Success && int.TryParse(match.Groups[groupName].Value, out var value) ? value : null;

    private static bool IsTableDecoration(string line) => string.IsNullOrWhiteSpace(line) || line.All(character => character is '-' or '=' or '+' or '|' or ' ' or '\t');

    private static bool IsHeaderRow(string line) => line.Contains("name", StringComparison.OrdinalIgnoreCase) && (line.Contains("memory", StringComparison.OrdinalIgnoreCase) || line.Contains("size", StringComparison.OrdinalIgnoreCase) || line.Contains("count", StringComparison.OrdinalIgnoreCase));

    private static bool LooksLikeDetailRow(string line) => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("Log", StringComparison.OrdinalIgnoreCase) && !line.StartsWith("Changelist:", StringComparison.OrdinalIgnoreCase);

    private sealed record DetailSections(
        IReadOnlyList<UnrealMemReportTexture> Textures,
        IReadOnlyList<UnrealMemReportRenderTarget> RenderTargets,
        IReadOnlyList<UnrealMemReportObject> Objects,
        IReadOnlyList<UnrealMemReportTextureDetail> TextureDetails,
        IReadOnlyList<UnrealMemReportTextureStat> TextureStats);

    private enum DetailSection
    {
        None,
        Textures,
        RenderTargets,
        Objects
    }
}
