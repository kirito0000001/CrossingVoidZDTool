using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CrossingVoidZDTool.Services.Atlas;

/// <summary>图集打包过程中的阶段，只为进度条文案服务。</summary>
internal enum AtlasPackStage
{
    Preparing,
    Packing,
    Finished,
}

internal sealed record AtlasPackProgress(AtlasPackStage Stage, string Message);

/// <summary>
/// 调图集工具打一张图集。
///
/// 三件事，顺序固定：
/// <list type="number">
/// <item>写清单（<c>--manifest</c>）</item>
/// <item>起进程（<c>ue_atlas.py</c>），复用 <see cref="UnrealProcessRunner"/></item>
/// <item>读回 report（<c>--report</c>）——**它是唯一的返回值**</item>
/// </list>
///
/// **工具箱永远不知道装箱参数**（padding / extrude / rotate / maxSize），
/// 一律用图集工具的默认值。这条边界划清楚，两边才能各自演进。
///
/// **判定成败看 report 文件，不看退出码。** 和 Unreal 侧同一个道理：
/// 退出码不可靠（`CONTEXT.md` 里写着），所以要用
/// <see cref="UnrealProcessRunner.IsFreshOutput"/> 确认「这个 report 是这一轮写的」，
/// 再确认 <c>ok:true</c>。缺任一条都算失败——否则会拿着上一轮的结果当成功。
/// </summary>
internal sealed class AtlasPackService
{
    /// <summary>单张图集的超时。实测十几帧的图集在一秒内跑完，给足余量。</summary>
    private static readonly TimeSpan PackTimeout = TimeSpan.FromMinutes(5);

    /// <summary>图集工具脚本相对**可执行文件目录**的路径。</summary>
    private const string ToolScriptRelativePath = @"Tools\Atlas\ue_atlas.py";

    /// <summary>清单与 report 的临时文件名（落在产物目录里，和产物一起清）。</summary>
    private const string ManifestFileName = "_atlas_manifest.json";
    private const string ReportFileName = "_atlas_report.json";

    /// <summary>产物目录里的中间文件名，清理时用得上。</summary>
    public static IReadOnlyList<string> TransientFileNames => [ManifestFileName, ReportFileName];

    /// <summary>
    /// 打一张图集。
    /// </summary>
    /// <param name="characterCode">角色代号，必须与 Unreal 角色目录名完全一致。</param>
    /// <param name="actionFolder">动作素材目录的绝对路径。</param>
    /// <param name="definition">动作定义。</param>
    /// <param name="formIndex">形态下标。</param>
    /// <param name="sequenceManifest">工具箱的序列帧清单。</param>
    /// <param name="outputDirectory">产物目录（调用方按 <see cref="AtlasDestination"/> 算好）。</param>
    /// <param name="configuredPythonPath">整体设置里的 Python 路径，可为空。</param>
    /// <param name="progress">进度回调，可为空。</param>
    public async Task<AtlasPackResult> PackAsync(
        string characterCode,
        string actionFolder,
        SequenceActionDefinition definition,
        int formIndex,
        SequenceFrameManifest sequenceManifest,
        string outputDirectory,
        string? configuredPythonPath = null,
        IProgress<AtlasPackProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(sequenceManifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        var startedAt = Stopwatch.StartNew();

        progress?.Report(new AtlasPackProgress(AtlasPackStage.Preparing, "正在生成图集清单…"));

        // 清单生成会校验素材完整性，缺图会在这里就报错——比等到 Unreal 侧才发现好得多。
        var manifest = AtlasManifestWriter.Build(
            characterCode, actionFolder, definition, formIndex, sequenceManifest);

        var python = AtlasPythonLocator.Resolve(configuredPythonPath);
        var scriptPath = Path.Combine(AppContext.BaseDirectory, ToolScriptRelativePath);
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException(
                "工具箱内置的图集脚本不存在，安装可能不完整。", scriptPath);
        }

        var outputFullPath = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputFullPath);
        var manifestPath = Path.Combine(outputFullPath, ManifestFileName);
        var reportPath = Path.Combine(outputFullPath, ReportFileName);
        AtlasManifestWriter.Write(manifestPath, manifest);

        // 清掉上一轮的 report：不清的话，万一这一轮进程起不来，
        // 我们会拿到一份陈旧的 report 当成功（这正是 IsFreshOutput 要防的，
        // 但能删干净就少一层依赖）。
        UnrealProcessRunner.TryClearStaleFile(reportPath);

        progress?.Report(new AtlasPackProgress(
            AtlasPackStage.Packing, $"正在打包 {manifest.Frames.Count} 帧…"));

        var pngPath = Path.Combine(outputFullPath, $"{manifest.Atlas}.png");
        var startInfo = new ProcessStartInfo
        {
            FileName = python.FilePath,
            // 参数按「原样传递、不做 shell 解析」给：ProcessStartInfo 的 Arguments
            // 是拼成一条命令行的，所以路径里的空格和引号必须自己处理干净。
            Arguments = string.Join(' ', [
                Quote(scriptPath),
                "--manifest", Quote(manifestPath),
                "--report", Quote(reportPath),
                "-o", Quote(outputFullPath),
                "--name", manifest.Atlas,
            ]),
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = outputFullPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.Environment["PYTHONUTF8"] = "1";
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        // 内置解释器不读环境变量，但系统 Python 会——隔离掉，免得用户的 PYTHONPATH
        // 把另一个 Pillow 灌进来，跑出「我这里好好的」那种问题。
        startInfo.Environment["PYTHONPATH"] = string.Empty;

        // 输出一律按 UTF-8 解。中文 Windows 的控制台代码页是 936，而 Python 侧
        // （PYTHONUTF8=1 之下）写的是 UTF-8，不固定就必然乱码——
        // 偏偏这些输出只在出错时才会被人翻出来看，等于故障时线索全丢。
        // 注意 UnrealProcessRunner 也会做同一件事，这里是显式写出来让守卫看得见。
        startInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
        startInfo.StandardErrorEncoding = System.Text.Encoding.UTF8;

        var result = await UnrealProcessRunner
            .RunAsync(
                startInfo,
                PackTimeout,
                startFailureMessage: $"无法启动图集工具：{python.FilePath}",
                timeoutMessage: $"图集打包超时（{PackTimeout.TotalMinutes:0} 分钟）：{manifest.Atlas}",
                onPoll: elapsed => progress?.Report(new AtlasPackProgress(
                    AtlasPackStage.Packing,
                    $"正在打包 {manifest.Frames.Count} 帧…（{elapsed.TotalSeconds:0} 秒）")),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var report = ReadReport(reportPath, result.StartedAtUtc, manifest.Atlas);

        // 图集工具的警告（例如「源帧尺寸不统一，锚点会抖」）不该让整件事失败，
        // 但必须让它出现在日志里——静默吞掉它就等于没做这个检查。
        if (report.WarningText is { Length: > 0 } warning)
        {
            ToolboxLog.Warn($"图集 {manifest.Atlas} 的警告：{warning}");
        }

        var width = report.Size?.W ?? 0;
        var height = report.Size?.H ?? 0;
        var imagePath = Path.Combine(outputFullPath, report.Image ?? $"{manifest.Atlas}.png");

        progress?.Report(new AtlasPackProgress(
            AtlasPackStage.Finished, $"图集完成：{width} × {height}"));

        ToolboxLog.Info(
            $"图集 {manifest.Atlas}：{manifest.Frames.Count} 帧 → {width}×{height}，"
            + $"输出 {imagePath}（{startedAt.Elapsed.TotalSeconds:0.0} 秒）");

        return new AtlasPackResult(
            manifest.Atlas,
            width,
            height,
            imagePath,
            report.FrameCount > 0 ? report.FrameCount : manifest.Frames.Count,
            outputFullPath,
            startedAt.Elapsed);
    }

    /// <summary>
    /// 读回 report 并判定成败。
    ///
    /// 三个条件缺一不可，任何一个不满足都算失败，且**失败原因里要带上进程输出**
    /// ——图集工具出错时的线索全在 stdout 里，不带出去就等于让人盲查。
    /// </summary>
    /// <exception cref="InvalidOperationException">report 不新鲜、读不出来、或 ok 为假。</exception>
    public static AtlasReport ReadReport(string reportPath, DateTime startedAtUtc, string atlasName)
    {
        if (!File.Exists(reportPath))
        {
            throw new InvalidOperationException(
                $"图集工具没有写出结果文件（{reportPath}）。");
        }

        if (!UnrealProcessRunner.IsFreshOutput(reportPath, startedAtUtc))
        {
            throw new InvalidOperationException(
                $"图集 {atlasName} 的结果文件不是本轮写出的（{reportPath}）。"
                + "上一次的旧结果不能被当作这次的产出，请重试。");
        }

        AtlasReport? report;
        try
        {
            report = JsonSerializer.Deserialize(
                File.ReadAllText(reportPath),
                AtlasJsonContext.Default.AtlasReport);
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"图集 {atlasName} 的结果文件读不出来（{reportPath}）：{error.Message}", error);
        }

        if (report is null)
        {
            throw new InvalidOperationException(
                $"图集 {atlasName} 的结果文件是空的（{reportPath}）。");
        }

        if (!report.Ok)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(report.Error)
                    ? $"图集 {atlasName} 打包失败，图集工具没有给出原因。"
                    : $"图集 {atlasName} 打包失败：{report.Error}");
        }

        if (report.Size is null || report.Size.W <= 0 || report.Size.H <= 0)
        {
            throw new InvalidOperationException(
                $"图集 {atlasName} 报了成功但没有给出有效尺寸（{reportPath}）。");
        }

        return report;
    }

    /// <summary>
    /// 算产物目录。
    ///
    /// <list type="bullet">
    /// <item><see cref="AtlasDestination.Export"/> → <c>&lt;工作区&gt;/Export/&lt;角色&gt;/Atlas/&lt;动作&gt;</c>
    /// —— 交付物，用户会拿走。</item>
    /// <item><see cref="AtlasDestination.Cache"/> → <c>&lt;角色目录&gt;/tool/AtlasCache/&lt;动作&gt;</c>
    /// —— 一次性缓存，用完即弃。</item>
    /// </list>
    /// </summary>
    public static string ResolveOutputDirectory(
        AtlasDestination destination,
        string projectRootPath,
        string characterFolder,
        string characterCode,
        string actionVariantCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionVariantCode);
        return destination switch
        {
            AtlasDestination.Export => Path.Combine(
                projectRootPath,
                CharacterFolderLayout.Export,
                characterCode,
                AtlasFolder,
                actionVariantCode),
            AtlasDestination.Cache => Path.Combine(
                characterFolder,
                CharacterFolderLayout.Tool,
                CharacterFolderLayout.AtlasCache,
                actionVariantCode),
            _ => throw new ArgumentOutOfRangeException(nameof(destination), destination, "未知的图集落点。"),
        };
    }

    /// <summary><c>Export/&lt;角色&gt;/</c> 下再分一层 <c>Atlas/</c>，和别的导出物分开。</summary>
    public const string AtlasFolder = "Atlas";

    /// <summary>
    /// 清掉一个动作的图集缓存目录里的内容。
    ///
    /// 缓存是**一次性**的——下一轮同步应该重打，而不是复用上一轮的，
    /// 因为素材可能已经改过了，而图集的产物无法自证它对应的是哪一版素材。
    /// </summary>
    public static void ClearCache(string cacheDirectory)
    {
        if (string.IsNullOrWhiteSpace(cacheDirectory) || !Directory.Exists(cacheDirectory))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(cacheDirectory))
            {
                AtomicFileWriter.TryDelete(file);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // 清不掉不该让同步失败：图集本来就会被重新生成覆盖掉。
            ToolboxLog.Warn($"图集缓存目录没有清干净：{cacheDirectory}", error);
        }
    }

    /// <summary>Windows 命令行参数加引号。内部的 <c>"</c> 按 cmd 的规矩翻倍。</summary>
    private static string Quote(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
