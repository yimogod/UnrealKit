namespace UnrealKit.Core.Adb;

/// <summary>
/// 「adb server 已在运行」的一次性标记，跨 <see cref="AdbService"/> 实例共享。
///
/// 不放在 AdbService 里：GUI 每次操作都新建一个 AdbService（见 ShellViewModel.CreateAdbService），
/// 标记若随实例走就等于每次操作都要重新确保一遍，比不做还多一次调用。
/// 标记的生命周期属于「本进程连着的那个 adb server」，因此由持有会话的那一层（Desktop 工厂、CLI）
/// 创建一个实例并传给所有 AdbService。
/// </summary>
public sealed class AdbServerLatch
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _started;

    /// <summary>构造一个尚未确保过的标记，首条命令前会执行一次 start-server。</summary>
    public AdbServerLatch()
    {
    }

    /// <summary>
    /// 构造一个已置位的标记：调用方自行保证 server 在运行，不希望这里再发 start-server。
    /// 供测试与「server 生命周期由外部管理」的场景使用。
    /// </summary>
    public static AdbServerLatch CreateStarted() => new() { _started = true };

    /// <summary>server 是否已确保启动过。</summary>
    public bool IsStarted => _started;

    /// <summary>
    /// 确保 server 已启动，只在首次真正执行 <paramref name="startAsync"/>。
    ///
    /// 启动失败不抛也不置位：server 起不来时紧随其后的真实命令会带着自己的退出码与 stderr 失败，
    /// 那条信息比这里的二手报错更有用；不置位则下一条命令还会再试一次。
    /// </summary>
    public async Task EnsureStartedAsync(Func<CancellationToken, Task<bool>> startAsync, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(startAsync);
        if (_started)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_started)
            {
                return;
            }

            _started = await startAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 清除标记。kill-server 之后必须调用：留着标记会让后续命令跳过 start-server，
    /// 把冷启动代价又推回那条命令自己。
    /// </summary>
    public void Reset() => _started = false;
}
