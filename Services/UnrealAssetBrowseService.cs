using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 在打开着的 Unreal 编辑器里定位资产。
///
/// 只走在线路径。离线启一个无头编辑器再让它「定位」到某个资产没有意义，
/// 所以编辑器没开时直接告诉用户，而不是白白花十几秒启动一个看不见的进程。
/// </summary>
internal sealed class UnrealAssetBrowseService
{
    public const string ScriptRelativePath = @"Tools\UnrealBridge\browse_unreal_assets.py";

    private readonly UnrealPythonTaskExecutionService _executionService = new();

    public string GetScriptPath() => Path.Combine(AppContext.BaseDirectory, ScriptRelativePath);

    /// <summary>编辑器开着且打开的正是当前项目时才可用。</summary>
    public bool CanBrowse(string editorPath, string projectPath) =>
        _executionService.ShouldUseRunningEditor(editorPath, projectPath);

    public async Task<UnrealAssetBrowseResult> BrowseAsync(
        string editorPath,
        string projectPath,
        IReadOnlyCollection<string> objectPaths,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(objectPaths);
        if (objectPaths.Count == 0)
        {
            return new UnrealAssetBrowseResult(false, 0, [], "没有可定位的资产。");
        }
        if (!CanBrowse(editorPath, projectPath))
        {
            return new UnrealAssetBrowseResult(
                false, 0, [], "需要先打开这个 Unreal 项目的编辑器，才能在里面定位资产。");
        }

        var scriptPath = GetScriptPath();
        if (!File.Exists(scriptPath))
        {
            return new UnrealAssetBrowseResult(false, 0, [], $"未找到定位脚本：{scriptPath}");
        }

        Directory.CreateDirectory(workingDirectory);
        var resultPath = Path.Combine(workingDirectory, "browse.result.json");
        // 结果路径是固定的，删不掉就意味着待会儿读到的是上一轮的定位结果。
        // 所以记下删没删掉，后面用写入时间再确认一次。
        var clearedStaleResult = AtomicFileWriter.TryDelete(resultPath);

        // 离线的 StartInfo 只是个载体：BuildLaunch 会把 ZD_ 开头的环境变量
        // 转发给在线作业，真正执行的是远程那一侧。
        var offlineStartInfo = new ProcessStartInfo { FileName = editorPath };
        offlineStartInfo.Environment["ZD_BROWSE_RESULT"] = resultPath;
        offlineStartInfo.Environment["ZD_BROWSE_OBJECT_PATHS"] = JsonSerializer.Serialize(
            objectPaths.ToArray(),
            AppJsonSerializerContext.Default.StringArray);

        var launch = _executionService.BuildLaunch(
            editorPath,
            projectPath,
            scriptPath,
            Path.Combine(workingDirectory, "browse.remote-job.json"),
            offlineStartInfo,
            useRunningEditor: true);

        var run = await UnrealProcessRunner.RunAsync(
            launch.StartInfo,
            TimeSpan.FromMinutes(5),
            "无法启动 Unreal 在线执行进程。",
            "Unreal 在线定位超时。",
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var output = run.Output;

        // 结果必须是这一轮写出来的。以前只判存在，于是上一轮删不掉的残留
        // 会被当成本次结果直接用掉，而且完全看不出来。
        if (!UnrealProcessRunner.IsFreshOutput(resultPath, run.StartedAtUtc))
        {
            var reason = File.Exists(resultPath) && !clearedStaleResult
                ? $"只留下删不掉的上一轮定位结果：{resultPath}"
                : "编辑器没有返回定位结果。";
            return new UnrealAssetBrowseResult(
                false, 0, [], $"{reason}{Environment.NewLine}{output.Trim()}");
        }

        try
        {
            var payload = JsonSerializer.Deserialize(
                File.ReadAllText(resultPath, Encoding.UTF8),
                AppJsonSerializerContext.Default.UnrealAssetBrowsePayload);
            if (payload is null)
            {
                return new UnrealAssetBrowseResult(false, 0, [], "定位结果为空。");
            }

            return new UnrealAssetBrowseResult(
                payload.Succeeded,
                payload.BrowsedCount,
                payload.MissingPaths ?? [],
                payload.ErrorMessage ?? string.Empty);
        }
        catch (JsonException ex)
        {
            return new UnrealAssetBrowseResult(false, 0, [], $"定位结果无法解析：{ex.Message}");
        }
    }
}

internal sealed record UnrealAssetBrowseResult(
    bool Succeeded,
    int BrowsedCount,
    IReadOnlyList<string> MissingPaths,
    string Message);

internal sealed class UnrealAssetBrowsePayload
{
    public int ProtocolVersion { get; set; } = 1;
    public bool Succeeded { get; set; }
    public string? ErrorMessage { get; set; }
    public int BrowsedCount { get; set; }
    public List<string>? MissingPaths { get; set; }
}
