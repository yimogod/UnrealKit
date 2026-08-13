using UnrealKit.Core.Projects;

namespace UnrealKit.Core.RemoteControl;

/// <summary>
/// UE Web Remote Control 连接与指令映射配置。
/// 所有控制台指令统一经由 HTTP Remote Control 发送，不保留 Android am broadcast 路径。
/// </summary>
public sealed record RemoteControlOptions(
    int HttpPort,
    string ObjectPath,
    string FunctionName,
    string CommandParameterName)
{
    public const int DefaultHttpPort = 30010;
    public const string DefaultObjectPath = "/Script/Engine.Default__KismetSystemLibrary";
    public const string DefaultFunctionName = "ExecuteConsoleCommand";
    public const string DefaultCommandParameterName = "Command";

    public static RemoteControlOptions Default { get; } = new(
        DefaultHttpPort,
        DefaultObjectPath,
        DefaultFunctionName,
        DefaultCommandParameterName);

    public static RemoteControlOptions FromProjectSettings(ProjectSettings? settings)
    {
        if (settings is null)
        {
            return Default;
        }

        return new RemoteControlOptions(
            settings.RemoteControlHttpPort,
            settings.RemoteControlObjectPath,
            settings.RemoteControlFunctionName,
            settings.RemoteControlCommandParameter);
    }
}

/// <summary>
/// 一次 Remote Control 控制台指令调用所需的数据。
/// </summary>
public sealed record RemoteControlCommandRequest(
    int HttpPort,
    string ObjectPath,
    string FunctionName,
    string CommandParameterName,
    string Command);