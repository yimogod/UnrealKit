using UnrealKit.Core.Adb;
using UnrealKit.Core.Operations;

namespace UnrealKit.Core.Console;

/// <summary>
/// 控制台指令服务，封装单条和批量指令发送。
/// </summary>
public interface IConsoleCommandService
{
    /// <summary>
    /// 向指定设备发送单条控制台指令。
    /// </summary>
    Task<ConsoleCommandResult> SendAsync(
        string serialNumber,
        ConsoleCommand command,
        string? packageName = null,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 读回一个 cvar 在运行中的 UE 进程里的当前值。
    /// 通道失败与响应格式异常都归到 <see cref="ConsoleVariableValue.Failed"/>，不抛异常——
    /// 批量刷新时单个 cvar 读不到不应中断其余项。
    /// </summary>
    Task<ConsoleVariableValue> QueryVariableAsync(
        string serialNumber,
        string variableName,
        CommandChannel.ConsoleVariableType variableType,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行指令序列，按步骤顺序推进。
    /// </summary>
    Task<SequenceExecutionResult> RunSequenceAsync(
        SequenceExecutionRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 等待 logcat 中出现指定模式，然后执行对应动作。返回匹配行或超时。
    /// </summary>
    Task<LogcatConditionResult> RunConditionalAsync(
        string serialNumber,
        LogcatConditionStep condition,
        string? packageName = null,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
