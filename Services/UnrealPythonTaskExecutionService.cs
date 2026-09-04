using System;
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
        if (!(useRunningEditor ?? ShouldUseRunningEditor()))
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

    public bool ShouldUseRunningEditor() => HasRunningInteractiveEditor();

    private static bool HasRunningInteractiveEditor()
    {
        try
        {
            var processes = Process.GetProcessesByName("UnrealEditor");
            try
            {
                return processes.Any(process => !process.HasExited);
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }
        catch
        {
            return true;
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
