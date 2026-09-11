using System.Text.Json;
using UnrealKit.Core.PakScan;
using UnrealKit.Core.Parsing;

namespace UnrealKit.Cli;

/// <summary>
/// 各解析结果的文本/JSON 呈现。`parse` 与 `export` 共用同一套写法，
/// 保证同一份报告在两个命令下显示一致。
/// </summary>
internal static class ParseResultWriters
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    internal static void WriteMemInfo(AndroidMemInfoParseResult result, bool json)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, IndentedJson));
            return;
        }

        if (result.Report is not null)
        {
            Console.WriteLine($"Input: {result.InputPath}");
            Console.WriteLine($"Process: {result.Report.ProcessName} (pid {result.Report.ProcessId})");
            Console.WriteLine($"App Summary TOTAL: {result.Report.Summary.TotalPssKb} KB");
        }

        CliOutput.WriteDiagnostics(result.Diagnostics);
    }

    internal static void WriteWin64MemInfo(Win64MemInfoParseResult result, bool json)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, IndentedJson));
            return;
        }

        if (result.Report is not null)
        {
            var counters = result.Report.Counters;
            Console.WriteLine($"Input: {result.InputPath}");
            Console.WriteLine($"Process: {result.Report.ProcessName} (pid {result.Report.ProcessId})");
            Console.WriteLine($"Working set: {MetricFormatting.Bytes(counters.WorkingSetBytes)}");
            Console.WriteLine($"Private memory: {MetricFormatting.Bytes(counters.PrivateMemoryBytes)}");
            Console.WriteLine($"Virtual memory: {MetricFormatting.Bytes(counters.VirtualMemoryBytes)}");
            Console.WriteLine($"Peak working set: {MetricFormatting.Bytes(counters.PeakWorkingSetBytes)}");
            Console.WriteLine($"Threads: {counters.ThreadCount}, Handles: {counters.HandleCount}");
            if (!string.IsNullOrWhiteSpace(counters.TotalProcessorTime))
            {
                Console.WriteLine($"Total processor time: {counters.TotalProcessorTime}");
            }
        }

        CliOutput.WriteDiagnostics(result.Diagnostics);
    }

    internal static void WriteMemReport(UnrealMemReportParseResult result, bool json)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, IndentedJson));
            return;
        }

        if (result.Report is not null)
        {
            Console.WriteLine($"Input: {result.InputPath}");
            Console.WriteLine($"Changelist: {result.Report.Changelist}");
            Console.WriteLine();
            Console.WriteLine("Summary Metrics:");
            foreach (var metric in result.Report.Summary.Metrics)
            {
                // 缺失与格式非法是不同的失败，分别标注而不是都当作 0。
                var status = metric.Status switch
                {
                    UnrealMemReportMetricStatus.Parsed => $"{metric.ValueKb} KB",
                    UnrealMemReportMetricStatus.Missing => "MISSING",
                    UnrealMemReportMetricStatus.Invalid => $"INVALID ({metric.RawValue})",
                    _ => "?"
                };
                Console.WriteLine($"  [{metric.Group}] {metric.Name}: {status}");
            }

            if (result.Report.Textures.Count > 0)
            {
                Console.WriteLine($"\nTextures: {result.Report.Textures.Count}");
            }

            if (result.Report.RenderTargets.Count > 0)
            {
                Console.WriteLine($"Render Targets: {result.Report.RenderTargets.Count}");
            }

            if (result.Report.Objects.Count > 0)
            {
                Console.WriteLine($"Objects: {result.Report.Objects.Count}");
            }
        }

        CliOutput.WriteDiagnostics(result.Diagnostics);
    }

    internal static void WriteStaticCamera(StaticCameraPerfParseResult result, bool json)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, IndentedJson));
            return;
        }

        if (result.Report is not null)
        {
            var report = result.Report;
            Console.WriteLine($"Input: {result.InputPath}");
            Console.WriteLine($"Cameras: {report.ParseCameraCount} of {report.CameraCount} ({(report.Completeness == StaticCameraPerfDataCompleteness.Complete ? "complete" : "truncated")})");
            Console.WriteLine();
            Console.WriteLine("Device Info:");
            if (report.DeviceInfo.OsPlatform is not null)
            {
                Console.WriteLine($"  OS: {report.DeviceInfo.OsPlatform}");
            }

            if (report.DeviceInfo.DeviceMake is not null)
            {
                Console.WriteLine($"  Device: {report.DeviceInfo.DeviceMake}");
            }

            if (report.DeviceInfo.GpuVendor is not null)
            {
                Console.WriteLine($"  GPU: {report.DeviceInfo.GpuVendor}");
            }

            if (report.DeviceInfo.VulkanAvailable.HasValue)
            {
                Console.WriteLine($"  Vulkan: {report.DeviceInfo.VulkanVersion ?? "available"}");
            }

            Console.WriteLine();
            Console.WriteLine("Averages:");
            Console.WriteLine($"  Frame: {report.Average.FrameTimeMs} ms");
            Console.WriteLine($"  Game:  {report.Average.GameTimeMs} ms");
            Console.WriteLine($"  Draw:  {report.Average.DrawTimeMs} ms");
            Console.WriteLine($"  RHI:   {report.Average.RhiTimeMs} ms");
            Console.WriteLine($"  GPU:   {report.Average.GpuTimeMs} ms");
            Console.WriteLine($"  DC:    {report.Average.DrawCalls}");
            Console.WriteLine($"  Prim:  {report.Average.Triangles:N0}");
            Console.WriteLine();
            Console.WriteLine("Per-Camera:");
            foreach (var frame in report.Frames)
            {
                Console.WriteLine($"  [{frame.Index}] {frame.CameraName}: Frame={frame.FrameTimeMs}ms Game={frame.GameTimeMs}ms Draw={frame.DrawTimeMs}ms RHI={frame.RhiTimeMs}ms GPU={frame.GpuTimeMs}ms DC={frame.DrawCalls} Prim={frame.Triangles:N0}");
                if (frame.Screenshots.Count > 0)
                {
                    Console.WriteLine($"       Screenshots: {frame.Screenshots.Count}");
                }
            }
        }

        CliOutput.WriteDiagnostics(result.Diagnostics);
    }

    internal static void WritePakScanHtml(PakScanResult result, string? outputFile)
    {
        var stamp   = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var texPath = outputFile ?? Path.Combine(Path.GetTempPath(), $"PakScanTextures_{stamp}.html");
        var base_   = Path.ChangeExtension(texPath, null);
        var smPath  = base_ + "_StaticMeshes.html";
        var skmPath = base_ + "_SkeletalMeshes.html";

        HtmlTableReport.WriteAndOpen(PakScanHtmlBuilder.Build(result),             texPath);
        HtmlTableReport.WriteAndOpen(PakScanStaticMeshHtmlBuilder.Build(result),   smPath);
        HtmlTableReport.WriteAndOpen(PakScanSkeletalMeshHtmlBuilder.Build(result), skmPath);
        CliOutput.WriteDiagnostics(result.Diagnostics);
    }

    internal static void WritePakScan(PakScanResult result, bool json, string? outputFile)
    {
        if (json)
        {
            var jsonText = JsonSerializer.Serialize(result, IndentedJson);
            if (outputFile is not null)
                File.WriteAllText(outputFile, jsonText);
            else
                Console.WriteLine(jsonText);
            return;
        }

        if (result.Report is not null)
        {
            var report = result.Report;

            // Textures section
            var texHeader = "Name\tPath\tSizeX\tSizeY\tFormat\tLodBias\tLodGroup\tNumMips\tEstimatedSizeBytes";
            var texLines  = report.Textures.Select(t =>
                $"{t.Name}\t{t.ObjectPath}\t{t.SizeX}\t{t.SizeY}\t{t.PixelFormat}\t{t.LodBias}\t{t.LodGroup}\t{t.NumMips}\t{t.EstimatedSizeBytes}");

            // StaticMesh section
            var smHeader = "Name\tPath\tLodCount\tMaterialCount";
            var smLines  = report.StaticMeshes.Select(m =>
                $"{m.Name}\t{m.ObjectPath}\t{m.LodCount}\t{m.MaterialCount}");

            // SkeletalMesh section
            var skmHeader = "Name\tPath\tLodCount\tMaterialCount\tBoneCount";
            var skmLines  = report.SkeletalMeshes.Select(m =>
                $"{m.Name}\t{m.ObjectPath}\t{m.LodCount}\t{m.MaterialCount}\t{m.BoneCount}");

            if (outputFile is not null)
            {
                File.WriteAllLines(outputFile, texLines.Prepend(texHeader));
                var smFile  = Path.ChangeExtension(outputFile, null) + "_StaticMeshes.tsv";
                var skmFile = Path.ChangeExtension(outputFile, null) + "_SkeletalMeshes.tsv";
                File.WriteAllLines(smFile,  smLines.Prepend(smHeader));
                File.WriteAllLines(skmFile, skmLines.Prepend(skmHeader));
                Console.WriteLine($"Pak scan complete: {report.TextureCount} texture(s) → {outputFile}");
                Console.WriteLine($"                   {report.StaticMeshCount} StaticMesh → {smFile}");
                Console.WriteLine($"                   {report.SkeletalMeshCount} SkeletalMesh → {skmFile}");
            }
            else
            {
                Console.WriteLine(texHeader);
                foreach (var line in texLines)
                    Console.WriteLine(line);

                Console.WriteLine();
                Console.WriteLine(smHeader);
                foreach (var line in smLines)
                    Console.WriteLine(line);

                Console.WriteLine();
                Console.WriteLine(skmHeader);
                foreach (var line in skmLines)
                    Console.WriteLine(line);

                Console.WriteLine();
                Console.WriteLine($"Total: {report.TextureCount} Texture2D / {report.StaticMeshCount} StaticMesh / {report.SkeletalMeshCount} SkeletalMesh / {report.TotalAssetsScanned} total assets scanned");
                Console.WriteLine($"Directory: {report.InputDirectory}");
            }
        }

        CliOutput.WriteDiagnostics(result.Diagnostics);
    }
}
