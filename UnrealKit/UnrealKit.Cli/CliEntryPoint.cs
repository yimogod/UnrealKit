using UnrealKit.Core.Adb;
using UnrealKit.Core.Devices;

namespace UnrealKit.Cli;

/// <summary>
/// 顶层动词路由与统一的失败处理。各动词的实现在 <c>Commands/</c> 下，
/// 参数读取、设备解析与输出格式化在 <c>Support/</c> 下。
/// </summary>
internal static class CliEntryPoint
{
    internal static async Task<int> RunAsync(string[] arguments)
    {
        if (arguments.Length == 0 || arguments[0] is "-h" or "--help" or "help")
        {
            CliUsage.Print();
            return 0;
        }

        try
        {
            return arguments[0].ToLowerInvariant() switch
            {
                "project" => await ProjectCommands.RunAsync(arguments[1..]),
                "adb" => await AdbCommands.RunAsync(arguments[1..]),
                "app" => await AppCommands.RunAsync(arguments[1..]),
                "commandline" => await CommandLineCommands.RunAsync(arguments[1..]),
                "devices" => await DeviceCommands.RunAsync(arguments[1..]),
                "capture" => await CaptureCommands.RunAsync(arguments[1..]),
                "parse" => await ParseCommands.RunAsync(arguments[1..]),
                "export" => await ExportCommands.RunAsync(arguments[1..]),
                "analyze" => await AnalyzeCommands.RunAsync(arguments[1..]),
                "renderdoc" => await RenderDocCommands.RunAsync(arguments[1..]),
                _ => FailUnknownCommand()
            };
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            WriteFailureDetails(exception);
            return 1;
        }
    }

    /// <summary>
    /// 可预期的失败只报错误消息与附加详情，不打印堆栈。
    /// 其它异常继续上抛，避免把编程错误伪装成用户错误。
    /// </summary>
    private static bool IsExpectedFailure(Exception exception) => exception
        is ArgumentException
        or InvalidOperationException
        or InvalidDataException
        or IOException
        or UnauthorizedAccessException
        or AdbCommandException
        or DeviceCommandException
        or TimeoutException;

    private static void WriteFailureDetails(Exception exception)
    {
        switch (exception)
        {
            case AdbCommandException adbException:
                CliOutput.WriteAdbFailure(adbException);
                break;
            case DeviceCommandException deviceException:
                CliOutput.WriteDeviceCommandFailure(deviceException);
                break;
            case AdbPathResolutionException pathException:
                CliOutput.WriteAdbPathDiagnostics(pathException.Resolution);
                break;
        }
    }

    private static int FailUnknownCommand()
    {
        Console.Error.WriteLine("Unknown command.");
        CliUsage.Print();
        return 2;
    }
}
