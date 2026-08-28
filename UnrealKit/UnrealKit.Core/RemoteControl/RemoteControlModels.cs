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

/// <summary>
/// 读回 cvar 时的取值类型，决定调用哪个 UE getter。
/// UE 的 <c>UKismetSystemLibrary</c> 按返回类型分了几个 getter，没有「通用取值」入口，
/// 因此类型必须由调用方给出，不能从 cvar 名推断。
/// </summary>
public enum RemoteControlVariableType
{
    /// <summary>走 <c>GetConsoleVariableBoolValue</c>。</summary>
    Bool,

    /// <summary>走 <c>GetConsoleVariableFloatValue</c>。整数 cvar 也能用它读回（float 覆盖 int 值域）。</summary>
    Number
}

/// <summary>
/// 一次「读回 cvar 当前值」的调用数据。
///
/// 与 <see cref="RemoteControlCommandRequest"/> 走**同一个** HTTP 端点
/// （<c>PUT http://127.0.0.1:{port}/remote/object/call</c>）和同一个 objectPath，
/// 区别只在 functionName 与 parameters：发指令调 <c>ExecuteConsoleCommand</c>（参数 <c>Command</c>），
/// 读回调 <c>GetConsoleVariable*Value</c>（参数 <c>VariableName</c>），
/// cvar 当前值由响应 JSON 的顶层 <c>ReturnValue</c> 字段带回。
/// </summary>
public sealed record RemoteControlVariableQueryRequest(
    int HttpPort,
    string ObjectPath,
    string VariableName,
    RemoteControlVariableType VariableType)
{
    /// <summary>读回函数的参数名，UE 侧签名为 <c>GetConsoleVariable*Value(const FString&amp; VariableName)</c>。</summary>
    public const string VariableParameterName = "VariableName";

    /// <summary>响应 JSON 中承载函数返回值的顶层字段名。</summary>
    public const string ReturnValuePropertyName = "ReturnValue";

    public const string BoolFunctionName = "GetConsoleVariableBoolValue";
    public const string NumberFunctionName = "GetConsoleVariableFloatValue";

    /// <summary>
    /// 按 <see cref="VariableType"/> 选定的 UE 函数名。
    /// 这两个 getter 与 <c>ExecuteConsoleCommand</c> 同为 <c>UKismetSystemLibrary</c> 上的
    /// BlueprintCallable 静态函数，因此可复用同一 objectPath。
    /// </summary>
    public string FunctionName => VariableType == RemoteControlVariableType.Bool
        ? BoolFunctionName
        : NumberFunctionName;
}
