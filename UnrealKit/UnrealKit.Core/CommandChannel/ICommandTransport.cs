using UnrealKit.Core.Operations;
using UnrealKit.Core.Processes;

namespace UnrealKit.Core.CommandChannel;

/// <summary>
/// 向运行中的 UE 客户端发送控制台指令的传输通道。
///
/// 当前只有 <see cref="HttpCommandTransport"/> 一个实现：Android 与 Win64 都走
/// 引擎自带的 Web Remote Control HTTP 服务。抽象只接受命令文本：端口、协议格式
/// 都是通道自身的细节，让调用方按通道类型分支准备参数，等于把平台判断又搬回上层。
/// </summary>
public interface ICommandTransport
{
    /// <summary>该通道使用的传输方式，用于日志与失败提示。</summary>
    CommandTransportKind Kind { get; }

    /// <summary>
    /// 通道在设备上监听的端口。Android 侧的 <c>adb forward</c> 需要它——
    /// 由通道自己给出，调用方不必按 <see cref="Kind"/> 再选一次端口，
    /// 否则转发的端口与实际连接的端口会各自取值而对不上。
    /// </summary>
    int Port { get; }

    /// <summary>
    /// 发送一条控制台指令。
    /// 连接失败、命令执行失败、协议异常一律以 <see cref="CommandTransportException"/> 表达，
    /// 并带上 <c>UKC*</c> 诊断码；调用方主动取消仍以取消语义上抛。
    /// </summary>
    Task<ProcessExecutionResult> SendConsoleCommandAsync(
        string command,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 读回一个 cvar 的当前值。返回值原始 body 落在
    /// <see cref="ProcessExecutionResult.StandardOutput"/>，解析交给上层。
    /// 失败与 <see cref="SendConsoleCommandAsync"/> 同构：一律 <see cref="CommandTransportException"/> 带 <c>UKC*</c> 码。
    /// </summary>
    Task<ProcessExecutionResult> QueryConsoleVariableAsync(
        string variableName,
        ConsoleVariableType variableType,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
