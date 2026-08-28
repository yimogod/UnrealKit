using UnrealKit.Core.Operations;
using UnrealKit.Core.Processes;

namespace UnrealKit.Core.RemoteControl;

/// <summary>
/// UE Web Remote Control HTTP 客户端抽象。
/// </summary>
public interface IRemoteControlService
{
    /// <summary>
    /// 向运行中的 UE 进程发送一条控制台指令。
    /// HTTP 非成功状态或网络失败以 <see cref="RemoteControlException"/> 表达。
    /// </summary>
    Task<ProcessExecutionResult> SendConsoleCommandAsync(
        RemoteControlCommandRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 读回一个 cvar 在运行中的 UE 进程里的当前值。
    ///
    /// 与 <see cref="SendConsoleCommandAsync"/> 是同一个 HTTP 端点的两种调用：
    /// 这里调 UE 的 <c>GetConsoleVariable*Value</c>，当前值由响应 body 的顶层
    /// <c>ReturnValue</c> 字段带回，原始 body 落在
    /// <see cref="ProcessExecutionResult.StandardOutput"/>，由上层解析成强类型。
    /// HTTP 非成功状态或网络失败以 <see cref="RemoteControlException"/> 表达。
    /// </summary>
    Task<ProcessExecutionResult> QueryConsoleVariableAsync(
        RemoteControlVariableQueryRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
