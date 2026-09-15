using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace CrossingVoidZDTool.Services.Atlas;

/// <summary>一次 Python 探测的结果。</summary>
/// <param name="FilePath">可用的 python.exe 绝对路径。</param>
/// <param name="Source">这一档是哪来的，用于日志和错误文案。</param>
internal sealed record AtlasPythonCandidate(string FilePath, string Source);

/// <summary>
/// 找到「能跑图集工具的 Python」。
///
/// 内置了一个 embeddable 解释器在 <c>Tools/Atlas/python/</c>（见方案 §5.4），
/// 但**内置是默认、不是唯一** —— 按三档依次找，允许覆盖：
///
/// <list type="number">
/// <item>整体设置里配的路径（给「我想用自己的 Python」留的口子）</item>
/// <item><c>Tools/Atlas/python/python.exe</c> ← 内置的，正常情况下走这条</item>
/// <item><c>py -3</c> / <c>python</c> / <c>python3</c>（兜底：内置目录被误删时还能活）</item>
/// </list>
///
/// **每一档都要真的探一下 `import PIL`**，不能只看文件在不在。
/// 原因是内置包最容易坏的方式是「解压看起来正常、一 import 就 ModuleNotFoundError」——
/// <c>python313._pth</c> 少写一行 `Lib/site-packages` 就正好是这个症状
/// （实测踩到过）。只看 <c>File.Exists</c> 的话，这个错要到用户点导出时才炸。
///
/// **全失败就报错，不静默回退**（README 的纪律）。
/// </summary>
internal static class AtlasPythonLocator
{
    /// <summary>探针要导入的模块。Pillow 是图集工具唯一的第三方依赖。</summary>
    private const string ProbeExpression = "import PIL; from PIL import Image; print(PIL.__version__)";

    /// <summary>探针超时。正常几十毫秒，给足余量；超时说明这个 python 有问题，直接换下一档。</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(20);

    /// <summary>在 PATH 上找命令的超时。where.exe 正常是瞬时返回。</summary>
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(10);

    /// <summary>内置解释器相对**可执行文件目录**的路径。</summary>
    private const string BundledRelativePath = @"Tools\Atlas\python\python.exe";

    /// <summary>兜底要找的系统 Python 命令名。</summary>
    private static readonly string[] SystemCommands = ["py", "python", "python3"];

    private static readonly object CacheLock = new();
    private static string? _cachedKey;
    private static AtlasPythonCandidate? _cachedCandidate;

    /// <summary>内置解释器的预期位置（相对工具箱可执行文件目录，不是相对源码目录）。</summary>
    public static string BundledPath =>
        Path.Combine(AppContext.BaseDirectory, BundledRelativePath);

    /// <summary>
    /// 设置一改就调它，让下一次 <see cref="Resolve"/> 重新探。
    /// 探测结果进程内缓存，因为每次导出都探一遍会白等几百毫秒。
    /// </summary>
    public static void InvalidateCache()
    {
        lock (CacheLock)
        {
            _cachedKey = null;
            _cachedCandidate = null;
        }
    }

    /// <summary>
    /// 依次试三档，返回第一个 `import PIL` 成功的。
    /// </summary>
    /// <param name="configuredPath">整体设置里的 Python 路径，可为空。</param>
    /// <exception cref="InvalidOperationException">
    /// 三档全失败时抛出。文案里必须含**去哪里配**，否则用户拿到「没找到 Python」不知道下一步做什么。
    /// </exception>
    public static AtlasPythonCandidate Resolve(string? configuredPath)
    {
        var key = configuredPath ?? string.Empty;
        lock (CacheLock)
        {
            if (_cachedCandidate is not null && string.Equals(_cachedKey, key, StringComparison.Ordinal))
            {
                return _cachedCandidate;
            }
        }

        var failures = new List<string>();
        foreach (var candidate in EnumerateCandidates(configuredPath))
        {
            if (Probe(candidate.FilePath, out var version, out var reason))
            {
                ToolboxLog.Info($"图集工具用 Python：{candidate.FilePath}（{candidate.Source}，Pillow {version}）");
                lock (CacheLock)
                {
                    _cachedKey = key;
                    _cachedCandidate = candidate;
                }

                return candidate;
            }

            failures.Add($"{candidate.Source}（{candidate.FilePath}）：{reason}");
        }

        throw new InvalidOperationException(
            "找不到能用的 Python 运行图集工具。已经试过这些位置："
            + string.Join("；", failures)
            + "。可以在「整体设置」里指定一个 Python 路径，"
            + "或确认工具箱目录下的 Tools\\Atlas\\python\\ 是否完整。");
    }

    /// <summary>把候选按优先级排出来。抽成公开的，好写「定位四分支」的测试。</summary>
    public static IEnumerable<AtlasPythonCandidate> EnumerateCandidates(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var trimmed = configuredPath.Trim();
            // 允许用户直接填解释器，也允许填目录（帮他拼上 python.exe）。
            var full = Directory.Exists(trimmed)
                ? Path.Combine(trimmed, "python.exe")
                : trimmed;
            yield return new AtlasPythonCandidate(full, "整体设置里指定的");
        }

        var bundled = BundledPath;
        if (File.Exists(bundled))
        {
            yield return new AtlasPythonCandidate(bundled, "工具箱内置");
        }

        foreach (var command in SystemCommands)
        {
            var found = TryFindOnPath(command);
            if (found is not null)
            {
                yield return new AtlasPythonCandidate(found, "系统 PATH 上的");
            }
        }
    }

    /// <summary>
    /// 起一次进程，真的 `import PIL` 一下。
    ///
    /// 用 <c>UnrealProcessRunner</c> 而不是自己起进程：它已经解决了
    /// 「超时要真的杀进程树」这件事（自己写的话，超时后会留下孤儿进程）。
    /// 探针本身很短，但一个卡住的 python 会一直占着，所以这点很重要。
    /// </summary>
    public static bool Probe(string pythonPath, out string version, out string failureReason)
    {
        version = string.Empty;
        failureReason = string.Empty;

        if (string.IsNullOrWhiteSpace(pythonPath))
        {
            failureReason = "路径为空";
            return false;
        }

        if (!File.Exists(pythonPath))
        {
            failureReason = "文件不存在";
            return false;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = pythonPath,
            Arguments = $"-c \"{ProbeExpression}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(pythonPath)) ?? string.Empty,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.Environment["PYTHONUTF8"] = "1";
        // embeddable 包不读环境变量，但系统 Python 会——显式隔离，免得用户的 PYTHONPATH
        // 把别的 Pillow 灌进来，测出来的「能跑」和实际跑的不是同一个。
        startInfo.Environment["PYTHONPATH"] = string.Empty;

        try
        {
            var result = UnrealProcessRunner
                .RunAsync(
                    startInfo,
                    ProbeTimeout,
                    startFailureMessage: $"无法启动 Python：{pythonPath}",
                    timeoutMessage: $"Python 探针超时（{ProbeTimeout.TotalSeconds:0} 秒）：{pythonPath}")
                .GetAwaiter()
                .GetResult();

            var output = (result.Output ?? string.Empty).Trim();
            if (result.ExitCode != 0 || output.Length == 0)
            {
                // 探针是少数「退出码可信」的场合：这里跑的是我们自己的表达式，
                // 没有 Unreal 那套「退出码恒为 0」的毛病。
                failureReason = output.Length > 0 ? FirstLine(output) : $"退出码 {result.ExitCode}";
                return false;
            }

            version = FirstLine(output);
            return true;
        }
        catch (Exception error)
        {
            // 探针失败是**预期内的正常分支**（要继续试下一档），所以不往日志里刷 Error，
            // 但要作为失败原因带出去——全失败时的报错文案要靠它。
            failureReason = FirstLine(error.Message);
            return false;
        }
    }

    private static string FirstLine(string text)
    {
        var line = text.Split('\n')[0].Trim();
        return line.Length > 200 ? line[..200] + "…" : line;
    }

    /// <summary>
    /// 在 PATH 上找某个命令。用 <c>where</c>（Windows 自带）而不是自己扫 PATH 目录，
    /// 因为 PATH 里可能混着 <c>.exe</c>/<c>.cmd</c>/微软商店的 App Execution Alias，
    /// 自己扫会漏掉后两种。
    ///
    /// 走 <see cref="UnrealProcessRunner"/> 而不是自己起进程：
    /// 仓库的守卫（<c>ProcessOrchestrationIsNotDuplicated</c>）明写着 Services 层
    /// 不许自己编排进程，理由是「取消杀不掉、读到上一轮结果」这两个坑——
    /// 这里同样适用（一个卡住的 where 会拖住整条导出链）。
    /// </summary>
    private static string? TryFindOnPath(string command)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "where.exe",
                Arguments = command,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
            };

            var result = UnrealProcessRunner
                .RunAsync(
                    startInfo,
                    LookupTimeout,
                    startFailureMessage: "无法执行 where.exe 查找 Python。",
                    timeoutMessage: "查找 Python 的命令超时。")
                .GetAwaiter()
                .GetResult();
            if (result.ExitCode != 0)
            {
                return null;
            }

            foreach (var line in (result.Output ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var path = line.Trim();
                // 优先认真正的 exe；py.exe 是启动器，探针能过就行。
                if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // 没有 where.exe 或者起不来：这一档就当没有，继续走后面的。
            return null;
        }
    }
}
