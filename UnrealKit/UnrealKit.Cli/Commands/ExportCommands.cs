using UnrealKit.Core.Export;
using UnrealKit.Core.Parsing;

namespace UnrealKit.Cli;

/// <summary>
/// `unrealkit export meminfo|memreport`：解析后导出。
/// 输出扩展名决定格式，`.xlsx` 走真实工作簿写出，其余走分隔文本。
/// </summary>
internal static class ExportCommands
{
    internal static async Task<int> RunAsync(string[] arguments)
    {
        if (arguments.Length == 0)
        {
            return FailUsage();
        }

        var subCommand = arguments[0].ToLowerInvariant();
        if (subCommand is not ("meminfo" or "memreport"))
        {
            return FailUsage();
        }

        var options = arguments[1..];
        CliOptions.EnsureOnly(
            options,
            CliOptions.Allowed("--input", "--output", "--include-details", "--capture-id"),
            CliOptions.Allowed("--include-details"));

        var input = CliOptions.GetRequired(options, "--input");
        var output = CliOptions.GetRequired(options, "--output");
        var includeDetails = CliOptions.HasFlag(options, "--include-details");
        var captureId = CliOptions.GetOptional(options, "--capture-id");

        return subCommand == "meminfo"
            ? await ExportMemInfoAsync(input, output, includeDetails, captureId)
            : await ExportMemReportAsync(input, output, includeDetails, captureId);
    }

    private static async Task<int> ExportMemInfoAsync(string input, string output, bool includeDetails, string? captureId)
    {
        var result = await new AndroidMemInfoParser().ParseFileAsync(input);
        if (!result.IsSuccess)
        {
            ParseResultWriters.WriteMemInfo(result, false);
            return 1;
        }

        var request = new MemInfoExportRequest(result, output, DateTimeOffset.UtcNow, includeDetails, captureId);
        var exported = IsXlsx(output)
            ? await new XlsxMemInfoExportService().ExportAsync(request)
            : await new MemInfoExportService().ExportAsync(request);
        Console.WriteLine(exported.OutputFilePath);
        return 0;
    }

    private static async Task<int> ExportMemReportAsync(string input, string output, bool includeDetails, string? captureId)
    {
        var result = await new UnrealMemReportParser().ParseFileAsync(input);
        if (!result.IsSuccess)
        {
            ParseResultWriters.WriteMemReport(result, false);
            return 1;
        }

        var request = new MemReportExportRequest(result, output, DateTimeOffset.UtcNow, includeDetails, captureId);
        var exported = IsXlsx(output)
            ? await new XlsxMemReportExportService().ExportAsync(request)
            : await new MemReportExportService().ExportAsync(request);
        Console.WriteLine(exported.OutputFilePath);
        return 0;
    }

    private static bool IsXlsx(string output) => output.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);

    private static int FailUsage()
    {
        Console.Error.WriteLine("Usage: unrealkit export meminfo --input <meminfo.txt> --output <results.csv|results.tsv|results.xlsx> [--include-details] [--capture-id <capture-id>]");
        Console.Error.WriteLine("       unrealkit export memreport --input <memreport.txt> --output <results.csv|results.tsv|results.xlsx> [--include-details] [--capture-id <capture-id>]");
        return 2;
    }
}
