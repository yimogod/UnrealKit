using System.Globalization;
using UnrealKit.Core.Diagnostics;

namespace UnrealKit.Core.ActorControl;

/// <summary>
/// 解析 UE <c>obj list class=Actor</c> 写入日志的最后一个结果区段。
/// UE 日志可能包含历史调用，因此只使用最后一个标记；从标记后的第 4 行起，读取到
/// <c>Objects (Total:</c> 前一行。每一行先丢弃 UE 的时间/线程前缀（最后一个 <c>]</c> 之前）。
/// </summary>
public sealed class RuntimeActorLogParser
{
    private const string SectionMarker = "Obj List: Class=Actor";
    private const string SectionTerminator = "Objects (Total:";
    private const int FirstDataLineOffset = 4;

    public RuntimeActorLogParseResult Parse(string inputPath, IReadOnlyList<string> lines)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentNullException.ThrowIfNull(lines);

        var diagnostics = new List<Diagnostic>();
        var markerIndex = FindLastIndex(lines, line => line.Contains(SectionMarker, StringComparison.OrdinalIgnoreCase));
        if (markerIndex < 0)
        {
            diagnostics.Add(Error(RuntimeActorDiagnosticCodes.ActorListSectionMissing,
                $"The log does not contain '{SectionMarker}'.", inputPath));
            return new RuntimeActorLogParseResult(inputPath, [], diagnostics);
        }

        var firstDataIndex = markerIndex + FirstDataLineOffset;
        if (firstDataIndex >= lines.Count)
        {
            diagnostics.Add(Error(RuntimeActorDiagnosticCodes.ActorListTerminatorMissing,
                $"The Actor list beginning on line {markerIndex + 1} has no data section or '{SectionTerminator}' terminator.", inputPath, markerIndex + 1));
            return new RuntimeActorLogParseResult(inputPath, [], diagnostics);
        }

        var terminatorIndex = FindFirstIndex(lines, firstDataIndex,
            line => line.Contains(SectionTerminator, StringComparison.OrdinalIgnoreCase));
        if (terminatorIndex < 0)
        {
            diagnostics.Add(Error(RuntimeActorDiagnosticCodes.ActorListTerminatorMissing,
                $"The Actor list beginning on line {markerIndex + 1} is missing '{SectionTerminator}'.", inputPath, markerIndex + 1));
            return new RuntimeActorLogParseResult(inputPath, [], diagnostics);
        }

        var actors = new List<RuntimeActorEntry>();
        for (var index = firstDataIndex; index < terminatorIndex; index++)
        {
            var effectiveLine = ExtractEffectiveLine(lines[index]);
            if (string.IsNullOrWhiteSpace(effectiveLine))
            {
                continue;
            }

            if (!TryParseActor(effectiveLine, index + 1, out var actor))
            {
                diagnostics.Add(Error(RuntimeActorDiagnosticCodes.ActorListRowInvalid,
                    "Expected 'Object NumKB MaxKB ResExcKB ResExcDedSysKB ResExcDedVidKB ResExcUnkKB'.",
                    inputPath, index + 1));
                continue;
            }

            actors.Add(actor);
        }

        return new RuntimeActorLogParseResult(inputPath, actors, diagnostics);
    }

    public async Task<RuntimeActorLogParseResult> ParseFileAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        var fullPath = Path.GetFullPath(inputPath);
        var lines = await File.ReadAllLinesAsync(fullPath, cancellationToken);
        return Parse(fullPath, lines);
    }

    private static bool TryParseActor(string effectiveLine, int lineNumber, out RuntimeActorEntry actor)
    {
        var fields = effectiveLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 8
            || !decimal.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var numKb)
            || !decimal.TryParse(fields[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var maxKb)
            || !decimal.TryParse(fields[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var resExcKb)
            || !decimal.TryParse(fields[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var resExcDedSysKb)
            || !decimal.TryParse(fields[6], NumberStyles.Float, CultureInfo.InvariantCulture, out var resExcDedVidKb)
            || !decimal.TryParse(fields[7], NumberStyles.Float, CultureInfo.InvariantCulture, out var resExcUnkKb))
        {
            actor = null!;
            return false;
        }

        actor = new RuntimeActorEntry(
            fields[0], fields[1], numKb, maxKb, resExcKb, resExcDedSysKb, resExcDedVidKb, resExcUnkKb, lineNumber);
        return true;
    }

    private static string ExtractEffectiveLine(string line)
    {
        var lastBracket = line.LastIndexOf(']');
        return (lastBracket >= 0 ? line[(lastBracket + 1)..] : line).Trim();
    }

    private static int FindLastIndex(IReadOnlyList<string> lines, Func<string, bool> predicate)
    {
        for (var index = lines.Count - 1; index >= 0; index--)
        {
            if (predicate(lines[index])) return index;
        }

        return -1;
    }

    private static int FindFirstIndex(IReadOnlyList<string> lines, int startIndex, Func<string, bool> predicate)
    {
        for (var index = startIndex; index < lines.Count; index++)
        {
            if (predicate(lines[index])) return index;
        }

        return -1;
    }

    private static Diagnostic Error(string code, string message, string path, int? lineNumber = null) =>
        new(DiagnosticSeverity.Error, code, message, path, LineNumber: lineNumber);
}
