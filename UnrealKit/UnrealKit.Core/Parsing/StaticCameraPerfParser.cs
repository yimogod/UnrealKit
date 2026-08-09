using System.Globalization;
using UnrealKit.Core.Diagnostics;

namespace UnrealKit.Core.Parsing;

public sealed class StaticCameraPerfParser : IStaticCameraPerfParser
{
    public async Task<StaticCameraPerfParseResult> ParseFileAsync(string inputFilePath, CancellationToken cancellationToken = default)
    {
        return await ParseFileAsync(inputFilePath, null!, cancellationToken);
    }

    public async Task<StaticCameraPerfParseResult> ParseFileAsync(string inputFilePath, string screenshotsDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputFilePath);
        var fullPath = Path.GetFullPath(inputFilePath);
        if (Directory.Exists(fullPath)) throw new ArgumentException("Static camera perf input must be a file, not a directory.", nameof(inputFilePath));
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Static camera perf input file was not found.", fullPath);
        var lines = await File.ReadAllLinesAsync(fullPath, cancellationToken);
        return Parse(fullPath, lines, StaticCameraPerfConfig.Default, screenshotsDirectory);
    }

    public StaticCameraPerfParseResult Parse(string inputPath, IReadOnlyList<string> lines, string? screenshotsDirectory = null)
    {
        return Parse(inputPath, lines, StaticCameraPerfConfig.Default, screenshotsDirectory);
    }

    public StaticCameraPerfParseResult Parse(string inputPath, IReadOnlyList<string> lines, StaticCameraPerfConfig config, string? screenshotsDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(config);

        var diagnostics = new List<Diagnostic>();

        try
        {
            config.Validate();
        }
        catch (InvalidOperationException ex)
        {
            diagnostics.Add(Error("SCP100", ex.Message, inputPath, null, "Fix the threshold configuration so warning < error for all metrics."));
            return new StaticCameraPerfParseResult(inputPath, null, diagnostics);
        }

        var deviceInfo = ParseDeviceInfo(lines, config, inputPath, diagnostics);
        var perfLines = ExtractPerfLines(lines, config, inputPath, diagnostics);
        if (perfLines.Count == 0)
        {
            diagnostics.Add(Error("SCP101", "No Perf lines found between start and end markers.", inputPath, null,
                $"Ensure the log contains '{config.PerfStartTag}' and '{config.PerfEndTag}' markers with '{config.PerfLinePrefix}' lines between them."));
            return new StaticCameraPerfParseResult(inputPath, null, diagnostics);
        }

        var pointNum = ParsePointNum(perfLines, config, inputPath, diagnostics);
        if (pointNum is null)
        {
            diagnostics.Add(Error("SCP102", $"Could not parse '{config.PointNumTag}' from the first Perf line.", inputPath, 1,
                $"The first Perf line must contain '{config.PointNumTag}<number>'."));
            return new StaticCameraPerfParseResult(inputPath, null, diagnostics);
        }

        var frames = ParseFrames(perfLines, config, inputPath, diagnostics);
        var completeness = frames.Count < pointNum.Value ? StaticCameraPerfDataCompleteness.Truncated : StaticCameraPerfDataCompleteness.Complete;

        if (frames.Count == 0)
        {
            diagnostics.Add(Error("SCP103", "No valid camera frames could be parsed from the Perf lines.", inputPath, null,
                "Check that each camera has the expected 14-line data block."));
            return new StaticCameraPerfParseResult(inputPath, null, diagnostics);
        }

        string[]? screenshotPaths = null;
        if (!string.IsNullOrWhiteSpace(screenshotsDirectory) && Directory.Exists(screenshotsDirectory))
        {
            screenshotPaths = GetScreenshots(screenshotsDirectory);
            var desiredCount = frames.Count * config.ScreenshotsPerCamera;
            if (screenshotPaths.Length < desiredCount)
            {
                diagnostics.Add(Warning("SCP201", $"Expected at least {desiredCount} screenshots (={frames.Count} cameras x {config.ScreenshotsPerCamera}), but found {screenshotPaths.Length}.", inputPath, null,
                    "Check that all screenshots were captured and the directory is correct."));
            }

            AssignScreenshots(frames, screenshotPaths, config.ScreenshotsPerCamera);
        }

        var average = CalculateAverage(frames, pointNum.Value);

        if (completeness == StaticCameraPerfDataCompleteness.Truncated)
        {
            diagnostics.Add(Warning("SCP202", $"Data is truncated: expected {pointNum.Value} cameras but only {frames.Count} were fully parsed.", inputPath, null,
                "The log may have been cut off due to an application crash."));
        }

        if (screenshotPaths is not null)
        {
            var expectedScreenshotCount = pointNum.Value * config.ScreenshotsPerCamera;
            if (screenshotPaths.Length < expectedScreenshotCount)
            {
                diagnostics.Add(Warning("SCP203", $"Screenshot count ({screenshotPaths.Length}) is less than the expected {expectedScreenshotCount} for {pointNum.Value} cameras.", inputPath, null,
                    "Verify the screenshot directory contains all captured images."));
            }
        }

        var report = new StaticCameraPerfReport(pointNum.Value, frames.Count, completeness, deviceInfo, average, frames);
        return new StaticCameraPerfParseResult(inputPath, report, diagnostics);
    }

    private static StaticCameraPerfDeviceInfo ParseDeviceInfo(IReadOnlyList<string> lines, StaticCameraPerfConfig config, string inputPath, List<Diagnostic> diagnostics)
    {
        string? osPlatform = null, deviceMake = null, gpuVendor = null, vulkanVersion = null;
        bool? vulkanAvailable = null;

        foreach (var line in lines)
        {
            if (line.StartsWith(config.OsLogPrefix, StringComparison.Ordinal))
            {
                osPlatform = line[config.OsLogPrefix.Length..].Trim();
            }
            else if (line.Contains(config.DeviceMakeMarker, StringComparison.Ordinal))
            {
                deviceMake = ExtractSummaryValue(line);
            }
            else if (line.Contains(config.GpuVendorMarker, StringComparison.Ordinal))
            {
                gpuVendor = ExtractSummaryValue(line);
            }
            else if (line.Contains(config.VulkanAvailableMarker, StringComparison.Ordinal))
            {
                var raw = ExtractSummaryValue(line);
                vulkanAvailable = string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase) ? true :
                                  string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase) ? false : null;
            }
            else if (line.Contains(config.VulkanVersionMarker, StringComparison.Ordinal))
            {
                vulkanVersion = ExtractSummaryValue(line);
            }
        }

        return new StaticCameraPerfDeviceInfo(osPlatform, deviceMake, gpuVendor, vulkanAvailable, vulkanVersion);
    }

    private static string ExtractSummaryValue(string line)
    {
        var idx = line.LastIndexOf(": ", StringComparison.Ordinal);
        return idx >= 0 ? line[(idx + 2)..].Trim() : line.Trim();
    }

    private static IReadOnlyList<string> ExtractPerfLines(IReadOnlyList<string> lines, StaticCameraPerfConfig config, string inputPath, List<Diagnostic> diagnostics)
    {
        var result = new List<string>();
        var inSection = false;

        foreach (var line in lines)
        {
            if (line.Contains(config.PerfEndTag, StringComparison.Ordinal))
                break;

            if (!inSection)
            {
                if (line.Contains(config.PerfStartTag, StringComparison.Ordinal))
                    inSection = true;
                continue;
            }

            var idx = line.IndexOf(config.PerfLinePrefix, StringComparison.Ordinal);
            if (idx < 0) continue;

            result.Add(line[(idx + config.PerfLinePrefix.Length)..].TrimEnd());
        }

        return result;
    }

    private static int? ParsePointNum(IReadOnlyList<string> perfLines, StaticCameraPerfConfig config, string inputPath, List<Diagnostic> diagnostics)
    {
        if (perfLines.Count == 0) return null;

        var firstLine = perfLines[0];
        var idx = firstLine.IndexOf(config.PointNumTag, StringComparison.Ordinal);
        if (idx < 0) return null;

        var numStr = firstLine[(idx + config.PointNumTag.Length)..].Trim();
        if (!int.TryParse(numStr, NumberStyles.None, CultureInfo.InvariantCulture, out var pointNum) || pointNum <= 0)
        {
            diagnostics.Add(Error("SCP104", $"Invalid PointNum value: '{numStr}'.", inputPath, null, "PointNum must be a positive integer."));
            return null;
        }

        return pointNum;
    }

    private static List<StaticCameraPerfFrame> ParseFrames(IReadOnlyList<string> perfLines, StaticCameraPerfConfig config, string inputPath, List<Diagnostic> diagnostics)
    {
        var frames = new List<StaticCameraPerfFrame>();
        var lineIndex = 1; // Skip the PointNum line (index 0)

        while (lineIndex < perfLines.Count)
        {
            // Each camera data block is FramesPerCamera lines.
            // The block structure (matching the Python 14-line stride):
            //   lineIndex+0 : FocusCamera (not parsed, just skipped)
            //   lineIndex+1 : CamName
            //   lineIndex+2 : Stat Info (truncation check)
            //   lineIndex+3 : frame
            //   lineIndex+4 : game
            //   lineIndex+5 : draw
            //   lineIndex+6 : rhi
            //   lineIndex+7 : gpu
            //   lineIndex+8 : mem
            //   lineIndex+9 : dc
            //   lineIndex+10: prim
            //   lineIndex+11: End marker (skipped)
            //   lineIndex+12: next FocusCamera (skipped; overlaps with next block)
            //   lineIndex+13: next CamName (skipped; overlaps with next block)
            var blockStart = lineIndex;

            // Check if we have enough lines for a minimum parse (at least through CamName)
            if (blockStart + 1 >= perfLines.Count)
            {
                diagnostics.Add(Warning("SCP204", $"Incomplete camera data block starting at perf line {blockStart + 1}; log may be truncated.", inputPath, blockStart + 1,
                    "The log may have been cut off. Only fully parsed cameras are retained."));
                break;
            }

            // Truncation check: need at least through prim (blockStart + 10)
            if (blockStart + 10 >= perfLines.Count)
            {
                diagnostics.Add(Warning("SCP204", $"Incomplete camera data block at perf line {blockStart + 1}; log truncated mid-camera.", inputPath, blockStart + 1,
                    "The log may have been cut off."));
                break;
            }

            try
            {
                var cameraName = ParseNamedLine(perfLines[blockStart + 1], config.CameraNamePrefix);
                var frameTime = ParseNamedDouble(perfLines[blockStart + 3], config.FrameTimeTag);
                var gameTime = ParseNamedDouble(perfLines[blockStart + 4], config.GameTimeTag);
                var drawTime = ParseNamedDouble(perfLines[blockStart + 5], config.DrawTimeTag);
                var rhiTime = ParseNamedDouble(perfLines[blockStart + 6], config.RhiTimeTag);
                var gpuTime = ParseNamedDouble(perfLines[blockStart + 7], config.GpuTimeTag);
                var memBytes = ParseNamedLong(perfLines[blockStart + 8], config.MemoryTag);
                var dc = ParseNamedLong(perfLines[blockStart + 9], config.DrawCallTag);
                var prim = ParseNamedLong(perfLines[blockStart + 10], config.TriangleTag);

                frames.Add(new StaticCameraPerfFrame(
                    frames.Count,
                    cameraName,
                    frameTime,
                    gameTime,
                    drawTime,
                    rhiTime,
                    gpuTime,
                    memBytes,
                    dc,
                    prim,
                    Array.Empty<string>(),
                    blockStart + 1));
            }
            catch (FormatException ex)
            {
                diagnostics.Add(Warning("SCP206", $"Failed to parse camera frame data: {ex.Message}", inputPath, blockStart + 1, "Check the numeric format in the Perf line."));
            }

            lineIndex += config.FramesPerCamera;
        }

        return frames;
    }

    private static string ParseNamedLine(string line, string prefix)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return trimmed[prefix.Length..].Trim();
        return trimmed;
    }

    private static double ParseNamedDouble(string line, string tag)
    {
        var value = ParseNamedValue(line, tag);
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
            throw new FormatException($"Expected numeric value for '{tag}' but got: '{value}'");
        return result;
    }

    private static long ParseNamedLong(string line, string tag)
    {
        var value = ParseNamedValue(line, tag);
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
            throw new FormatException($"Expected integer value for '{tag}' but got: '{value}'");
        return result;
    }

    private static string ParseNamedValue(string line, string tag)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith(tag, StringComparison.OrdinalIgnoreCase))
            return trimmed[tag.Length..].Trim();
        // Fallback: take everything after the first space
        var idx = trimmed.IndexOf(' ');
        return idx >= 0 ? trimmed[(idx + 1)..].Trim() : trimmed;
    }

    private static string[] GetScreenshots(string directory)
    {
        return Directory.GetFiles(directory, "*.png", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFullPath)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AssignScreenshots(List<StaticCameraPerfFrame> frames, string[] screenshots, int screenshotsPerCamera)
    {
        var startIndex = Math.Max(0, screenshots.Length - frames.Count * screenshotsPerCamera);
        for (var i = 0; i < frames.Count && startIndex + (i + 1) * screenshotsPerCamera <= screenshots.Length; i++)
        {
            var camScreenshots = new List<string>();
            for (var j = 0; j < screenshotsPerCamera; j++)
            {
                camScreenshots.Add(screenshots[startIndex + i * screenshotsPerCamera + j]);
            }

            var existing = frames[i];
            frames[i] = existing with { Screenshots = camScreenshots };
        }
    }

    private static StaticCameraPerfAverage CalculateAverage(List<StaticCameraPerfFrame> frames, int cameraCount)
    {
        if (frames.Count == 0)
            return new StaticCameraPerfAverage(0, 0, 0, 0, 0, 0, 0, 0);

        var count = cameraCount > 0 ? cameraCount : frames.Count;
        double frameSum = 0, gameSum = 0, drawSum = 0, rhiSum = 0, gpuSum = 0;
        long memSum = 0, dcSum = 0, primSum = 0;

        foreach (var f in frames)
        {
            frameSum += f.FrameTimeMs;
            gameSum += f.GameTimeMs;
            drawSum += f.DrawTimeMs;
            rhiSum += f.RhiTimeMs;
            gpuSum += f.GpuTimeMs;
            memSum += f.MemoryBytes;
            dcSum += f.DrawCalls;
            primSum += f.Triangles;
        }

        return new StaticCameraPerfAverage(
            Math.Round(frameSum / count, 2),
            Math.Round(gameSum / count, 2),
            Math.Round(drawSum / count, 2),
            Math.Round(rhiSum / count, 2),
            Math.Round(gpuSum / count, 2),
            memSum / count,
            dcSum / count,
            primSum / count);
    }

    private static Diagnostic Error(string code, string message, string path, int? lineNumber, string? suggestedFix)
        => new(DiagnosticSeverity.Error, code, message, path, suggestedFix, lineNumber);

    private static Diagnostic Warning(string code, string message, string path, int? lineNumber, string? suggestedFix)
        => new(DiagnosticSeverity.Warning, code, message, path, suggestedFix, lineNumber);
}