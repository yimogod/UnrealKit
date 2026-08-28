namespace UnrealKit.Core.Console;

/// <summary>
/// 单条控制台指令。
/// </summary>
public sealed record ConsoleCommand(
    string Command,
    string? Tag = null,
    string? Label = null)
{
    public static ConsoleCommand Create(string command, string? tag = null, string? label = null) =>
        new(command.Trim(), tag?.Trim(), label?.Trim());
}

/// <summary>
/// 单条指令的执行结果。
/// </summary>

/// <summary>
/// 一次 cvar 读回的结果。
///
/// 不用「返回 double? / bool? 加约定 null 表示失败」：读回失败的原因（UE 未启动、
/// 响应里没有 <c>ReturnValue</c>）必须带到界面上，null 说不出是哪一种。
/// </summary>
public sealed record ConsoleVariableValue(
    bool Succeeded,
    double? NumberValue,
    bool? BoolValue,
    string? Error)
{
    public static ConsoleVariableValue Number(double value) => new(true, value, null, null);

    public static ConsoleVariableValue Bool(bool value) => new(true, null, value, null);

    public static ConsoleVariableValue Failed(string error) => new(false, null, null, error);

    /// <summary>用于界面展示的文本。数值去掉多余小数位，bool 用 cvar 惯用的 0/1。</summary>
    public string Display => this switch
    {
        { Succeeded: false } => Error ?? "读取失败。",
        { BoolValue: { } flag } => flag ? "1" : "0",
        { NumberValue: { } number } => number.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
        _ => string.Empty
    };
}

/// <summary>
/// 条件动作类型。
/// </summary>
public enum ConditionActionType
{
    SendCommand,
    CaptureTag,
    Fail,
    Retry
}

/// <summary>
/// 条件执行动作。
/// </summary>
public sealed record ConditionAction(
    ConditionActionType Type,
    string? Argument = null)
{
    public static ConditionAction Send(string command) => new(ConditionActionType.SendCommand, command);
    public static ConditionAction Capture(string tag) => new(ConditionActionType.CaptureTag, tag);
    public static ConditionAction Fail(string message) => new(ConditionActionType.Fail, message);
    public static ConditionAction Retry() => new(ConditionActionType.Retry);
}

/// <summary>
/// logcat 条件步：等待 logcat 中出现特定模式，然后执行动作。
/// </summary>
public sealed record LogcatConditionStep(
    string Pattern,
    ConditionAction Action,
    TimeSpan? Timeout = null)
{
    public static LogcatConditionStep Create(string pattern, ConditionAction action, TimeSpan? timeout = null) =>
        new(pattern.Trim(), action, timeout);
}public sealed record ConsoleCommandResult(
    ConsoleCommand Command,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt)
{
    public TimeSpan Duration => CompletedAt - StartedAt;

    public bool Succeeded => ExitCode == 0;
}

/// <summary>
/// 序列步骤类型。
/// </summary>
public enum SequenceStepType
{
    Command,
    Wait,
    Tag,
    Group
}

/// <summary>
/// 序列步骤定义。
/// </summary>
public sealed record SequenceStep(
    SequenceStepType Type,
    ConsoleCommand? Command = null,
    TimeSpan? WaitDuration = null,
    string? Marker = null,
    IReadOnlyList<SequenceStep>? Children = null)
{
    public static SequenceStep CreateCommand(string command, string? tag = null, string? label = null) =>
        new(SequenceStepType.Command, Command: ConsoleCommand.Create(command, tag, label));

    public static SequenceStep CreateWait(TimeSpan duration, string? label = null) =>
        new(SequenceStepType.Wait, WaitDuration: duration, Marker: label);

    public static SequenceStep CreateTag(string marker) =>
        new(SequenceStepType.Tag, Marker: marker);

    public static SequenceStep CreateGroup(string label, IReadOnlyList<SequenceStep> children) =>
        new(SequenceStepType.Group, Marker: label, Children: children);
}

/// <summary>
/// 指令序列定义。由命名步骤组成，支持指令、等待、标记和嵌套组。
/// </summary>
public sealed record CommandSequenceDefinition(
    string Name,
    string? Description,
    IReadOnlyList<SequenceStep> Steps)
{
    public static CommandSequenceDefinition Create(string name, string? description, IReadOnlyList<SequenceStep> steps) =>
        new(name.Trim(), description?.Trim(), steps);
}

/// <summary>
/// 序列中单步的执行结果。
/// </summary>
public sealed record SequenceStepResult(
    int StepIndex,
    SequenceStep? Step = null,
    ConsoleCommandResult? CommandResult = null,
    string? Error = null)
{
    public bool Succeeded => CommandResult?.Succeeded != false && Error is null;
}

/// <summary>
/// 整个指令序列的执行结果。
/// </summary>
public sealed record SequenceExecutionResult(
    CommandSequenceDefinition Sequence,
    IReadOnlyList<SequenceStepResult> StepResults,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt)
{
    public TimeSpan Duration => CompletedAt - StartedAt;

    public bool Succeeded => StepResults.All(result => result.Succeeded);

    public int TotalSteps => StepResults.Count;

    public int SuccessfulSteps => StepResults.Count(result => result.Succeeded);

    public int FailedSteps => StepResults.Count(result => !result.Succeeded);
}

/// <summary>
/// 序列执行请求。
/// </summary>
public sealed record SequenceExecutionRequest(
    CommandSequenceDefinition Sequence,
    string DeviceSerialNumber,
    string? PackageName = null,
    TimeSpan? Timeout = null);

/// <summary>
/// logcat 条件执行结果。
/// </summary>
public sealed record LogcatConditionResult(
    LogcatConditionStep Condition,
    string? MatchedLine,
    ConsoleCommandResult? CommandResult,
    bool TimedOut,
    string? Error)
{
    public bool Succeeded => !TimedOut && Error is null && (CommandResult?.Succeeded != false);

    public static LogcatConditionResult Success(LogcatConditionStep condition, string matchedLine, ConsoleCommandResult? commandResult = null) =>
        new(condition, matchedLine, commandResult, false, null);

    public static LogcatConditionResult Timeout(LogcatConditionStep condition) =>
        new(condition, null, null, true, $"Timed out waiting for pattern: {condition.Pattern}");

    public static LogcatConditionResult Cancelled(LogcatConditionStep condition) =>
        new(condition, null, null, false, "Cancelled.");
}