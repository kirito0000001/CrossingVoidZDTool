using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CrossingVoidZDTool.Services;

internal sealed class UnrealBridgeExecutorService
{
    public const int SupportedProtocolVersion = 1;
    public const string ExecuteScriptRelativePath = @"Tools\UnrealBridge\execute_unreal_bridge.py";

    public string GetExecuteScriptPath() =>
        Path.Combine(AppContext.BaseDirectory, ExecuteScriptRelativePath);

    public void SavePlan(string path, UnrealBridgeExecutionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(plan, AppJsonSerializerContext.Default.UnrealBridgeExecutionPlan),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, path, overwrite: true);
    }

    public ProcessStartInfo BuildProcessStartInfo(
        string editorPath,
        string projectPath,
        string planPath,
        string progressPath,
        string resultPath)
    {
        var normalizedEditorPath = Path.GetFullPath(editorPath);
        var normalizedProjectPath = Path.GetFullPath(projectPath);
        var normalizedPlanPath = Path.GetFullPath(planPath);
        var normalizedProgressPath = Path.GetFullPath(progressPath);
        var normalizedResultPath = Path.GetFullPath(resultPath);
        if (!File.Exists(normalizedEditorPath))
        {
            throw new FileNotFoundException("没有找到 UnrealEditor.exe。", normalizedEditorPath);
        }

        if (!File.Exists(normalizedProjectPath) ||
            !string.Equals(Path.GetExtension(normalizedProjectPath), ".uproject", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("请选择有效的 Unreal .uproject 文件。");
        }

        if (!File.Exists(normalizedPlanPath))
        {
            throw new FileNotFoundException("虚幻同步执行计划不存在。", normalizedPlanPath);
        }

        var scriptPath = GetExecuteScriptPath();
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException("工具箱内置的虚幻同步执行脚本不存在。", scriptPath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(normalizedProgressPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(normalizedResultPath)!);
        var commandPath = ResolveEditorCommandPath(normalizedEditorPath);
        var startInfo = new ProcessStartInfo
        {
            FileName = commandPath,
            Arguments = $"{Quote(normalizedProjectPath)} -run=pythonscript -script={Quote(scriptPath)} -unattended -nop4 -nosplash",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(commandPath) ?? string.Empty,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.Environment["ZD_BRIDGE_PLAN_PATH"] = normalizedPlanPath;
        startInfo.Environment["ZD_BRIDGE_PROGRESS_PATH"] = normalizedProgressPath;
        startInfo.Environment["ZD_BRIDGE_RESULT_PATH"] = normalizedResultPath;
        return startInfo;
    }

    public async Task<UnrealBridgeExecutionResult> ExecuteAsync(
        ProcessStartInfo startInfo,
        string progressPath,
        string resultPath,
        IProgress<UnrealBridgeExecutionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        TryDelete(progressPath);
        TryDelete(resultPath);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 Unreal Python 任务进程。");
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        var startedAt = DateTime.UtcNow;
        while (!process.HasExited)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                KillProcessTree(process);
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (DateTime.UtcNow - startedAt > TimeSpan.FromHours(1))
            {
                KillProcessTree(process);
                throw new TimeoutException("虚幻同步执行超过 1 小时，已终止命令进程。");
            }

            var current = TryLoadProgress(progressPath);
            if (current is not null)
            {
                progress?.Report(current);
            }

            await Task.Delay(750, cancellationToken);
        }

        var output = await standardOutput + await standardError;
        if (!File.Exists(resultPath))
        {
            throw new InvalidOperationException(
                $"虚幻同步没有生成结果文件。进程退出码：{process.ExitCode}。{Environment.NewLine}{output}");
        }

        var result = LoadResult(resultPath);
        if (process.ExitCode != 0 && result.Succeeded)
        {
            throw new InvalidOperationException(
                $"虚幻同步进程退出码为 {process.ExitCode}，但结果文件错误地标记为成功。");
        }

        return result;
    }

    public UnrealBridgeExecutionResult LoadResult(string path)
    {
        try
        {
            var result = JsonSerializer.Deserialize(
                File.ReadAllText(path, Encoding.UTF8),
                AppJsonSerializerContext.Default.UnrealBridgeExecutionResult)
                ?? throw new InvalidDataException("虚幻同步结果为空。");
            if (result.ProtocolVersion != SupportedProtocolVersion)
            {
                throw new InvalidDataException($"不支持的虚幻同步结果协议：{result.ProtocolVersion}。");
            }

            return result;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"虚幻同步结果无法解析：{path}", ex);
        }
    }

    private static UnrealBridgeExecutionProgress? TryLoadProgress(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(
                File.ReadAllText(path, Encoding.UTF8),
                AppJsonSerializerContext.Default.UnrealBridgeExecutionProgress);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string ResolveEditorCommandPath(string editorPath)
    {
        var folderPath = Path.GetDirectoryName(editorPath);
        var commandPath = string.IsNullOrWhiteSpace(folderPath)
            ? editorPath
            : Path.Combine(folderPath, "UnrealEditor-Cmd.exe");
        return File.Exists(commandPath) ? commandPath : editorPath;
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
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
        }
    }
}
