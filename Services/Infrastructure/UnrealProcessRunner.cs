using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 一次 Unreal 子进程运行的结果。<see cref="StartedAtUtc"/> 是判断
/// 结果文件「是不是这一轮写的」的基准时刻，不是给用户看的时间。
/// </summary>
internal sealed record UnrealProcessResult(int ExitCode, string Output, DateTime StartedAtUtc);

/// <summary>
/// 起 Unreal 命令进程、轮询进度文件、超时/取消收尾、拿回输出。
///
/// 这套编排原本在四个服务里各写了一份（同步执行、蓝图置入、基础配置、角色导出），
/// 除了轮询间隔各不相同（1000/750/500/500 毫秒）之外，还有两个共同的坑：
///
/// 一是**取消时进程杀不掉**。四份代码都在循环顶部写了
/// <c>if (ct.IsCancellationRequested) KillProcessTree(...)</c>，但取消几乎总是落在
/// <c>await Task.Delay(..., ct)</c> 里——它直接抛 OperationCanceledException 掀掉整个循环，
/// 那句检查永远轮不到执行。而 <c>using var process</c> 的 Dispose 只释放句柄、不结束进程，
/// 于是 UnrealEditor-Cmd.exe 变成孤儿继续占着工程锁，下一次同步根本起不来。
/// 这里把等待整段包进 try，取消和超时都在 catch 里先杀进程树再往外抛。
///
/// 二是**子进程输出编码没固定**。除蓝图置入外都只开了重定向没设编码，
/// .NET 于是按父进程的 OEM 代码页解码（中文 Windows 是 936），而 Unreal 的 Python
/// 层写的是 UTF-8（在线执行路径还显式设了 PYTHONUTF8=1），结果所有中文日志必然乱码——
/// 偏偏这些输出只在出错时才会被人看，等于故障时线索全丢。
/// </summary>
internal static class UnrealProcessRunner
{
    /// <summary>
    /// 轮询间隔。取四份实现里最细的一档：单次开销只是一个 FileInfo 取时间戳，
    /// 而 1000 毫秒那一档会让进度条明显跟不上 Unreal 侧的阶段切换。
    /// 取消响应速度不受这个值影响——取消是 Task.Delay 自己中断的，不用等下一轮。
    /// </summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// 起进程、等它跑完，把退出码和合并后的输出带回来。
    ///
    /// 这里**不判成败**：commandlet 的退出码不可靠，判定规则各条链路也不一样，
    /// 所以交给 <paramref name="verdict"/>，调用方自己决定什么算失败。
    /// </summary>
    /// <param name="onPoll">每轮轮询回调，参数是已等待时长；用来转发进度。</param>
    /// <param name="verdict">
    /// 可选的成败判定：返回 null 算成功，返回非空字符串就当失败原因抛出。
    /// 之所以做成回调而不是在这里统一判退出码，见 <see cref="UnrealProjectSyncService"/>
    /// 里「以清单新鲜度判成败」那一段的注释。
    /// </param>
    public static async Task<UnrealProcessResult> RunAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        string startFailureMessage,
        string timeoutMessage,
        Action<TimeSpan>? onPoll = null,
        Func<UnrealProcessResult, string?>? verdict = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ApplyUtf8Redirection(startInfo);

        // 先记时刻再起进程：结果文件的新鲜度要跟这个时刻比，
        // 记晚了就可能把进程刚写出来的结果误判成上一轮的残留。
        var startedAtUtc = DateTime.UtcNow;
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(startFailureMessage);

        // 输出流必须在进程还活着的时候就开始读：Unreal 的日志量足够填满管道缓冲区，
        // 读晚了子进程会阻塞在写 stdout 上，表现就是「跑到一半不动了」。
        // 这两个任务故意不传 cancellationToken——取消时我们要杀进程，管道随之关闭，
        // 读取自然结束；传了 token 反而会在取消路径上留下没人 await 的失败任务。
        var outputTask = startInfo.RedirectStandardOutput
            ? process.StandardOutput.ReadToEndAsync()
            : Task.FromResult(string.Empty);
        var errorTask = startInfo.RedirectStandardError
            ? process.StandardError.ReadToEndAsync()
            : Task.FromResult(string.Empty);

        try
        {
            while (!process.HasExited)
            {
                var elapsed = DateTime.UtcNow - startedAtUtc;
                if (elapsed >= timeout)
                {
                    throw new TimeoutException(timeoutMessage);
                }

                onPoll?.Invoke(elapsed);
                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            }

            // 进程正好在取消的同一瞬间退出时也要认取消，否则会接着去解析
            // 用户已经放弃的这一轮结果，还把它当有效数据用下去。
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception error) when (error is OperationCanceledException or TimeoutException)
        {
            KillProcessTree(process);
            // 杀完等一下读取任务收尾，免得 process 被 Dispose 时管道读到一半炸出
            // 没人接的异常，把真正的取消/超时原因盖掉。
            await DrainAsync(outputTask, errorTask).ConfigureAwait(false);
            throw;
        }

        var output = await outputTask.ConfigureAwait(false) + await errorTask.ConfigureAwait(false);
        var result = new UnrealProcessResult(process.ExitCode, output, startedAtUtc);
        var failureReason = verdict?.Invoke(result);
        if (!string.IsNullOrEmpty(failureReason))
        {
            throw new InvalidOperationException(failureReason);
        }

        return result;
    }

    /// <summary>
    /// 带进度文件转发的重载：进度文件的读取与去重都交给
    /// <see cref="UnrealProgressFileWatcher{TState}"/>，调用方只管给类型信息。
    /// </summary>
    public static Task<UnrealProcessResult> RunAsync<TState>(
        ProcessStartInfo startInfo,
        string progressFilePath,
        IProgress<TState>? progress,
        JsonTypeInfo<TState> progressTypeInfo,
        TimeSpan timeout,
        string startFailureMessage,
        string timeoutMessage,
        Func<UnrealProcessResult, string?>? verdict = null,
        CancellationToken cancellationToken = default)
        where TState : class
    {
        var watcher = new UnrealProgressFileWatcher<TState>(progressFilePath, progress, progressTypeInfo);
        return RunAsync(
            startInfo,
            timeout,
            startFailureMessage,
            timeoutMessage,
            _ => watcher.Poll(),
            verdict,
            cancellationToken);
    }

    /// <summary>
    /// 结果文件是不是这一轮写出来的。
    ///
    /// 判成败只看 <c>File.Exists</c> 是不够的：结果路径是固定的，而清场用的删除
    /// 会被占用等 IO 异常吞掉，删不掉时留在那儿的一定是上一次运行的结果——
    /// 于是这一轮明明没写出结果，却拿着上一轮的内容当成功往下走。
    /// </summary>
    public static bool IsFreshOutput(string path, DateTime startedAtUtc) =>
        TryGetLastWriteUtc(path) > startedAtUtc;

    /// <summary>取文件最后写入时间；取不到就当没有，交给调用方判失败。</summary>
    public static DateTime TryGetLastWriteUtc(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return DateTime.MinValue;
        }

        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }

    /// <summary>
    /// 清掉上一轮留下的进度/结果文件，并告诉调用方删干净了没有。
    /// 删不掉不是致命错误——新鲜度校验才是真正的兜底——但调用方可以据此把
    /// 「残留文件删不掉」写进报错，免得用户对着一份陈旧结果排查半天。
    /// </summary>
    public static bool TryClearStaleFile(string path) => AtomicFileWriter.TryDelete(path);

    private static void ApplyUtf8Redirection(ProcessStartInfo startInfo)
    {
        // UseShellExecute 为真时设编码会在 Process.Start 直接抛异常，先挡掉。
        if (startInfo.UseShellExecute)
        {
            return;
        }

        if (startInfo.RedirectStandardOutput)
        {
            startInfo.StandardOutputEncoding = Encoding.UTF8;
        }

        if (startInfo.RedirectStandardError)
        {
            startInfo.StandardErrorEncoding = Encoding.UTF8;
        }
    }

    private static async Task DrainAsync(Task<string> outputTask, Task<string> errorTask)
    {
        try
        {
            await outputTask.ConfigureAwait(false);
            await errorTask.ConfigureAwait(false);
        }
        catch
        {
            // 收尾用的等待，任何失败都不该盖掉外面正在抛的取消/超时。
        }
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // 清理失败不该盖掉原本的取消/超时信号。
        }
    }
}

/// <summary>
/// 进度文件转发器：文件的写入时间没变就不重复上报，
/// 免得每轮轮询都把同一段文案再推一次给 UI。
/// </summary>
internal sealed class UnrealProgressFileWatcher<TState>
    where TState : class
{
    private readonly string _path;
    private readonly IProgress<TState>? _progress;
    private readonly JsonTypeInfo<TState> _typeInfo;
    private DateTime _lastWriteUtc = DateTime.MinValue;

    public UnrealProgressFileWatcher(string path, IProgress<TState>? progress, JsonTypeInfo<TState> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        _path = path ?? string.Empty;
        _progress = progress;
        _typeInfo = typeInfo;
    }

    public void Poll()
    {
        if (_progress is null || string.IsNullOrWhiteSpace(_path))
        {
            return;
        }

        try
        {
            var info = new FileInfo(_path);
            if (!info.Exists || info.LastWriteTimeUtc <= _lastWriteUtc)
            {
                return;
            }

            _lastWriteUtc = info.LastWriteTimeUtc;
            var state = JsonSerializer.Deserialize(File.ReadAllText(_path, Encoding.UTF8), _typeInfo);
            if (state is not null)
            {
                _progress.Report(state);
            }
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            // 正在被写的进度文件读不全是常态，下一轮再读。
        }
    }
}
