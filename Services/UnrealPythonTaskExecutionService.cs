using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace CrossingVoidZDTool.Services;

internal sealed record UnrealPythonTaskLaunch(
    ProcessStartInfo StartInfo,
    bool UsesRunningEditor);

internal sealed class UnrealPythonTaskExecutionService
{
    private const string RemoteRunnerRelativePath = @"Tools\UnrealBridge\run_remote_unreal_job.py";

    /// <summary>
    /// 远程执行脚本连不上编辑器时会带上的标记。
    ///
    /// 用它把「连不上编辑器」和「脚本自己失败」区分开：前者退回离线执行还有救，
    /// 后者退回去也是一样的错，白等一次编辑器冷启动。
    /// </summary>
    public const string RemoteUnavailableMarker = "ZD_REMOTE_UNAVAILABLE";

    /// <summary>这次失败是不是「编辑器不可用」，可以退回离线执行。</summary>
    public static bool IsRemoteUnavailable(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains(RemoteUnavailableMarker, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public UnrealPythonTaskLaunch BuildLaunch(
        string editorPath,
        string projectPath,
        string scriptPath,
        string remoteJobPath,
        ProcessStartInfo offlineStartInfo,
        bool? useRunningEditor = null)
    {
        ArgumentNullException.ThrowIfNull(offlineStartInfo);
        var normalizedEditorPath = Path.GetFullPath(editorPath);
        var normalizedProjectPath = Path.GetFullPath(projectPath);
        var normalizedScriptPath = Path.GetFullPath(scriptPath);
        if (!(useRunningEditor ?? ShouldUseRunningEditor(normalizedEditorPath, normalizedProjectPath)))
        {
            return new UnrealPythonTaskLaunch(offlineStartInfo, UsesRunningEditor: false);
        }

        var engineRoot = ResolveEngineRoot(normalizedEditorPath);
        var pythonPath = Path.Combine(engineRoot, "Binaries", "ThirdParty", "Python3", "Win64", "python.exe");
        var runnerPath = Path.Combine(AppContext.BaseDirectory, RemoteRunnerRelativePath);
        if (!File.Exists(pythonPath))
        {
            throw new FileNotFoundException("当前 Unreal 引擎没有找到远程执行所需的 Python。", pythonPath);
        }
        if (!File.Exists(runnerPath))
        {
            throw new FileNotFoundException("工具箱内置的 Unreal 在线执行脚本不存在。", runnerPath);
        }

        var environment = offlineStartInfo.Environment
            .Where(pair => pair.Key.StartsWith("ZD_", StringComparison.OrdinalIgnoreCase) && pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value!, StringComparer.OrdinalIgnoreCase);
        var job = new UnrealRemotePythonJob
        {
            EngineRoot = engineRoot,
            ProjectPath = normalizedProjectPath,
            ScriptPath = normalizedScriptPath,
            Environment = environment
        };
        var normalizedJobPath = Path.GetFullPath(remoteJobPath);
        Directory.CreateDirectory(Path.GetDirectoryName(normalizedJobPath)!);
        var temporaryPath = normalizedJobPath + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(job, AppJsonSerializerContext.Default.UnrealRemotePythonJob),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, normalizedJobPath, overwrite: true);

        var startInfo = new ProcessStartInfo
        {
            FileName = pythonPath,
            Arguments = $"{Quote(runnerPath)} --job {Quote(normalizedJobPath)}",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(pythonPath) ?? string.Empty,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.Environment["PYTHONUTF8"] = "1";
        return new UnrealPythonTaskLaunch(startInfo, UsesRunningEditor: true);
    }

    /// <summary>
    /// 只有「打开的正是当前选中项目、且用的是当前选中引擎」的编辑器才算在线执行目标。
    /// 以前这里只判断有没有 UnrealEditor 进程，于是开着别的项目（例如 DevTest）时
    /// 也会走在线路径，然后在连接阶段因为找不到匹配的编辑器而整个失败，
    /// 而这种情况本该直接退回离线执行。
    /// </summary>
    public bool ShouldUseRunningEditor(string? editorPath, string? projectPath)
    {
        var normalizedProjectPath = SafeFullPath(projectPath);
        if (string.IsNullOrWhiteSpace(normalizedProjectPath))
        {
            return false;
        }

        var engineRoot = SafeFullPath(TryResolveEngineRoot(editorPath));
        foreach (var commandLine in EnumerateRunningEditorCommandLines())
        {
            if (!MatchesProject(commandLine, normalizedProjectPath))
            {
                continue;
            }

            // 引擎根目录拿不到时不再额外设限，项目匹配已经足够定位目标编辑器。
            if (string.IsNullOrWhiteSpace(engineRoot) ||
                commandLine.Contains(engineRoot, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesProject(string descriptor, string normalizedProjectPath)
    {
        // 编辑器主窗口标题形如「Lvl_Xxx - CrossingVoid」，只带项目名不带路径。
        var projectName = Path.GetFileNameWithoutExtension(normalizedProjectPath);
        if (string.IsNullOrWhiteSpace(projectName))
        {
            return false;
        }

        return descriptor.Contains(normalizedProjectPath, StringComparison.OrdinalIgnoreCase) ||
            descriptor.Split('|')[^1]
                .Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(token => string.Equals(token, projectName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 取运行中编辑器的可识别信息：主模块路径（定位引擎）和主窗口标题（含项目名）。
    /// 不用 WMI 读命令行，避免为这一个判断给工具箱引入 System.Management 依赖。
    /// </summary>
    private static IEnumerable<string> EnumerateRunningEditorCommandLines()
    {
        List<string> descriptors = [];
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName("UnrealEditor");
        }
        catch
        {
            return descriptors;
        }

        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (process.HasExited)
                    {
                        continue;
                    }

                    var modulePath = string.Empty;
                    try
                    {
                        modulePath = process.MainModule?.FileName ?? string.Empty;
                    }
                    catch
                    {
                        // 32/64 位或权限差异会读不到主模块，标题仍然可用。
                    }

                    descriptors.Add($"{modulePath}|{process.MainWindowTitle}");
                }
                catch
                {
                    // 单个进程读不到就跳过，不影响其它候选。
                }
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }

        return descriptors;
    }

    private static string TryResolveEngineRoot(string? editorPath)
    {
        try
        {
            return string.IsNullOrWhiteSpace(editorPath)
                ? string.Empty
                : ResolveEngineRoot(Path.GetFullPath(editorPath));
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string SafeFullPath(string? value)
    {
        try
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : Path.GetFullPath(value);
        }
        catch
        {
            return value ?? string.Empty;
        }
    }

    private static string ResolveEngineRoot(string editorPath)
    {
        var editorDirectory = Path.GetDirectoryName(editorPath)
            ?? throw new InvalidOperationException("无法确定 UnrealEditor.exe 所在目录。");
        return Path.GetFullPath(Path.Combine(editorDirectory, "..", ".."));
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
