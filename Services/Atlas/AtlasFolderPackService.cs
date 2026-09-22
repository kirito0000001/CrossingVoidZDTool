using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CrossingVoidZDTool.Services.Atlas;

/// <summary>
/// 「创建图集」这个工具：**任意一个装 PNG 的目录** → 一张图集 + 坐标 json。
///
/// 和同步用的 <see cref="AtlasPackService"/> 是同一个底层打包器（`Tools/Atlas/ue_atlas.py`
/// ＋内置 Python），区别只在"输入从哪来"：那条链路按角色/动作算素材，这里由用户自己选目录。
/// 所以两边都遵守同一条约定 —— **成败看 report 文件，不看退出码**。
/// </summary>
internal sealed class AtlasFolderPackService
{
    private const string ToolScriptRelativePath = @"Tools\Atlas\ue_atlas.py";
    private const string ManifestFileName = "_atlas_manifest.json";
    private const string ReportFileName = "_atlas_report.json";

    public const string PackMode = "pack";
    public const string GridMode = "grid";

    /// <summary>清单里认的两种模式名（和 ue_atlas.py 的 --mode 一致）。</summary>
    public static IReadOnlyList<string> SupportedModes => [PackMode, GridMode];

    private static readonly TimeSpan PackTimeout = TimeSpan.FromMinutes(5);

    /// <summary>源目录里能打包的图：只认 PNG（和同步那条一致，PNG 才有确定的 alpha 语义）。</summary>
    public static IReadOnlyList<string> EnumerateSourceImages(string sourceFolderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFolderPath);
        if (!Directory.Exists(sourceFolderPath))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(sourceFolderPath, "*.png", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// 读 PNG 头里的宽高（IHDR）。同步计划要写图集尺寸，而它就在这张 PNG 里 ——
    /// 读 24 个字节比加载整张图便宜得多。
    /// </summary>
    public static (int Width, int Height) ReadPngSize(string pngPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pngPath);
        using var stream = File.OpenRead(pngPath);
        Span<byte> header = stackalloc byte[24];
        if (stream.Read(header) < 24)
        {
            throw new InvalidOperationException($"这张图不是完整的 PNG：{pngPath}");
        }

        var width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
        var height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
        return (width, height);
    }

    /// <summary>
    /// 生成给打包器的清单（纯函数，回归直接断言）。
    ///
    /// **不给每帧写名字**：让打包器按 <c>spritePrefix</c> + 补零序号生成（<c>图集名_0001</c>），
    /// 少一处"名字规则"分散在两个地方。拆分时用的就是这些名字。
    /// </summary>
    public static string BuildManifestJson(
        AtlasCreateRequest request,
        IReadOnlyList<string> sourceImages,
        Func<int, string>? spriteNameForIndex = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sourceImages);
        if (sourceImages.Count == 0)
        {
            throw new InvalidOperationException("这个目录里没有 PNG，没什么可打包的。");
        }

        var atlasName = ResolveAtlasName(request);
        var payload = new Dictionary<string, object?>
        {
            ["atlas"] = atlasName,
            ["spritePrefix"] = atlasName,
            ["mode"] = ResolveMode(request),
            ["trim"] = request.Trim,
            ["padding"] = Math.Max(0, request.Padding),
            ["extrude"] = 0,
            ["crop"] = true,
            ["frames"] = sourceImages
                .Select((path, position) => new Dictionary<string, object?>
                {
                    ["file"] = Path.GetFullPath(path),
                    ["index"] = position + 1,
                    // 没给命名规则就让打包器按 spritePrefix + 序号自己起名。
                    ["name"] = spriteNameForIndex?.Invoke(position + 1)
                })
                .ToArray()
        };
        if (IsGridMode(request))
        {
            payload["cols"] = Math.Max(0, request.Columns);
        }
        else
        {
            payload["maxSize"] = Math.Max(64, request.MaxSize);
        }

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>打包器的命令行（纯函数，回归直接断言）。</summary>
    public static string BuildArguments(
        string scriptPath,
        string manifestPath,
        string reportPath,
        AtlasCreateRequest request)
    {
        var arguments = new List<string>
        {
            Quote(scriptPath),
            "--manifest", Quote(manifestPath),
            "--report", Quote(reportPath),
            "-o", Quote(Path.GetFullPath(request.OutputDirectory)),
            "--name", ResolveAtlasName(request),
            "--mode", ResolveMode(request)
        };

        if (IsGridMode(request))
        {
            if (request.Columns > 0)
            {
                arguments.Add("--cols");
                arguments.Add(request.Columns.ToString(CultureInfo.InvariantCulture));
            }
        }
        else
        {
            arguments.Add("--max-size");
            arguments.Add(Math.Max(64, request.MaxSize).ToString(CultureInfo.InvariantCulture));
        }

        arguments.Add("--padding");
        arguments.Add(Math.Max(0, request.Padding).ToString(CultureInfo.InvariantCulture));
        if (request.Trim)
        {
            arguments.Add("--trim");
        }

        return string.Join(' ', arguments);
    }

    public async Task<AtlasCreateResult> PackAsync(
        AtlasCreateRequest request,
        string? configuredPythonPath = null,
        IProgress<AtlasPackProgress>? progress = null,
        CancellationToken cancellationToken = default,
        Func<int, string>? spriteNameForIndex = null,
        IReadOnlyList<string>? sourceImages = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        // 调用方给清单就用清单：**格子的顺序就是这个列表的顺序**，
        // 而精灵名是按顺序贴上去的，扫目录扫出多一张就会整条错位。
        // 自己没有这份清单的工具（用户手选目录）才退回扫目录。
        sourceImages ??= EnumerateSourceImages(request.SourceFolderPath);
        var atlasName = ResolveAtlasName(request);
        var startedAtUtc = DateTime.UtcNow;
        progress?.Report(new AtlasPackProgress(AtlasPackStage.Preparing, $"正在清点 {sourceImages.Count} 张图…"));

        var python = AtlasPythonLocator.Resolve(configuredPythonPath);
        var scriptPath = Path.Combine(AppContext.BaseDirectory, ToolScriptRelativePath);
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException("工具箱内置的图集脚本不存在，安装可能不完整。", scriptPath);
        }

        var outputDirectory = Path.GetFullPath(request.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var manifestPath = Path.Combine(outputDirectory, ManifestFileName);
        var reportPath = Path.Combine(outputDirectory, ReportFileName);
        await File.WriteAllTextAsync(
            manifestPath,
            BuildManifestJson(request, sourceImages, spriteNameForIndex),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);
        UnrealProcessRunner.TryClearStaleFile(reportPath);

        progress?.Report(new AtlasPackProgress(AtlasPackStage.Packing, $"正在打包 {sourceImages.Count} 张…"));
        var startInfo = new ProcessStartInfo
        {
            FileName = python.FilePath,
            Arguments = BuildArguments(scriptPath, manifestPath, reportPath, request),
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = outputDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.Environment["PYTHONUTF8"] = "1";
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        startInfo.Environment["PYTHONPATH"] = string.Empty;
        // 输出一律按 UTF-8 解：中文 Windows 的控制台代码页是 936，而 Python 侧写的是 UTF-8，
        // 不固定就必然乱码 —— 偏偏这些输出只在出错时才会被翻出来看。
        startInfo.StandardOutputEncoding = Encoding.UTF8;
        startInfo.StandardErrorEncoding = Encoding.UTF8;

        // 进程编排走 UnrealProcessRunner（超时、杀进程树、编码都在它那儿一份），
        // 不要在服务里再写一遍 —— 这是回归里那两条守卫盯的东西。
        var processResult = await UnrealProcessRunner.RunAsync(
            startInfo,
            PackTimeout,
            startFailureMessage: $"无法启动图集打包进程：{python.FilePath}",
            timeoutMessage: $"图集打包超时（{PackTimeout.TotalMinutes:0} 分钟）：{atlasName}",
            onPoll: elapsed => progress?.Report(new AtlasPackProgress(
                AtlasPackStage.Packing,
                $"正在打包 {sourceImages.Count} 张…（{elapsed.TotalSeconds:0} 秒）")),
            cancellationToken: cancellationToken);

        var report = AtlasPackService.ReadReport(reportPath, processResult.StartedAtUtc, atlasName);
        var atlasImagePath = Path.Combine(outputDirectory, $"{atlasName}.png");
        if (!File.Exists(atlasImagePath))
        {
            throw new InvalidOperationException(
                $"打包器没有写出图集图片（{atlasImagePath}）。{Environment.NewLine}{processResult.Output}");
        }

        var dataFilePath = Path.Combine(outputDirectory, $"{atlasName}.json");
        var size = new FileInfo(atlasImagePath);
        progress?.Report(new AtlasPackProgress(AtlasPackStage.Finished, "图集已生成"));
        return new AtlasCreateResult(
            outputDirectory,
            atlasImagePath,
            File.Exists(dataFilePath) ? dataFilePath : reportPath,
            sourceImages.Count,
            $"{size.Length / 1024.0 / 1024.0:0.##} MB");
    }

    private static bool IsGridMode(AtlasCreateRequest request) =>
        string.Equals(ResolveMode(request), GridMode, StringComparison.OrdinalIgnoreCase);

    private static string ResolveMode(AtlasCreateRequest request) =>
        string.Equals(request.Mode, GridMode, StringComparison.OrdinalIgnoreCase) ? GridMode : PackMode;

    /// <summary>图集名：用户填的；没填就用源目录名（去掉路径）。</summary>
    public static string ResolveAtlasName(AtlasCreateRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.AtlasName))
        {
            return SanitizeName(request.AtlasName);
        }

        var folderName = new DirectoryInfo(Path.GetFullPath(request.SourceFolderPath)).Name;
        return SanitizeName(folderName);
    }

    /// <summary>去掉文件名里不能用的字符，免得打包时直接失败。</summary>
    private static string SanitizeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Trim().Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "atlas" : cleaned;
    }

    /// <summary>Windows 命令行参数加引号；内部的引号按 cmd 的规矩翻倍。</summary>
    private static string Quote(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
