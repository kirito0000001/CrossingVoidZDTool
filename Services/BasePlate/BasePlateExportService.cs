using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CrossingVoidZDTool.Services;

/// <summary>一次底板导出的进度（给全局进度条用）。</summary>
internal sealed record BasePlateExportProgress(int CompletedFrames, int TotalFrames, string Message);

/// <summary>一次底板导出的结果。</summary>
internal sealed record BasePlateExportResult(
    string OutputDirectory,
    int FrameCount,
    long TotalBytes,
    int RemovedStaleFiles);

/// <summary>
/// 底板导出的「写盘」这一半。
///
/// 规则（和界面上说的一致）：
/// <list type="bullet">
/// <item>非空白帧 = 把该帧的源图**原样复制**（不缩放、不重采样、保留 alpha）；</item>
/// <item>空白帧 = 生成一张同画布的**全透明** PNG，保住时间轴节奏；</item>
/// <item>导出目录**只放这一次的结果**：写之前先清空它（只允许清
/// <c>&lt;工作区&gt;/Export/&lt;角色&gt;/BasePlate/</c> 之下的目录，写错路径也不会误删别处）；</item>
/// <item>顺带写一份 <c>frames.csv</c>，记「第几张 → 原序列第几帧 / 原文件名 / 占几格」。</item>
/// </list>
/// </summary>
internal sealed class BasePlateExportService
{
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    public async Task<BasePlateExportResult> ExportAsync(
        BasePlateExportPlan plan,
        string workspaceRootPath,
        IProgress<BasePlateExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var outputDirectory = plan.OutputDirectory;
        // 护栏：只允许在 <工作区>/Export/<角色>/BasePlate/ 之下动手。
        // 这条目录是要**清空重写**的，路径算错一次就是删别人的东西。
        var basePlateRoot = Path.Combine(
            Path.GetFullPath(workspaceRootPath),
            CharacterFolderLayout.Export,
            plan.CharacterCode,
            BasePlateExportPlanner.FolderName);
        if (!CharacterWorkspaceService.IsPathInsideDirectory(outputDirectory, basePlateRoot))
        {
            throw new InvalidOperationException(
                $"底板导出目录不在工作区导出区里，拒绝写入：{outputDirectory}");
        }

        var removedStaleFiles = ClearOutputDirectory(outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var totalBytes = 0L;
        var completed = 0;
        foreach (var frame in plan.Frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetPath = Path.Combine(outputDirectory, BasePlateExportPlanner.FormatFrameFileName(plan, frame));
            totalBytes += frame.IsBlank
                ? WriteTransparentPng(targetPath, frame.CanvasWidth, frame.CanvasHeight)
                : CopySourceFrame(frame.SourceFilePath, targetPath);

            completed++;
            progress?.Report(new BasePlateExportProgress(
                completed,
                plan.Frames.Count,
                $"{plan.FileNamePrefix} {completed}/{plan.Frames.Count}"));
        }

        WriteManifest(plan, outputDirectory);
        await Task.CompletedTask;
        return new BasePlateExportResult(outputDirectory, plan.Frames.Count, totalBytes, removedStaleFiles);
    }

    /// <summary>清空导出目录里的文件（含子目录），返回删掉的文件数。</summary>
    private static int ClearOutputDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            if (AtomicFileWriter.TryDelete(file))
            {
                removed++;
            }
        }

        foreach (var sub in Directory.EnumerateDirectories(directory))
        {
            try
            {
                Directory.Delete(sub, recursive: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                ToolboxLog.Warn($"底板导出目录没有清干净：{sub}", error);
            }
        }

        return removed;
    }

    private static long CopySourceFrame(string sourcePath, string targetPath)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("底板导出时找不到源帧图。", sourcePath);
        }

        File.Copy(sourcePath, targetPath, overwrite: true);
        return new FileInfo(targetPath).Length;
    }

    private static long WriteTransparentPng(string targetPath, int width, int height)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
        }

        bitmap.Save(targetPath, ImageFormat.Png);
        return new FileInfo(targetPath).Length;
    }

    private static void WriteManifest(BasePlateExportPlan plan, string outputDirectory)
    {
        var builder = new StringBuilder();
        builder.AppendLine("输出帧,源帧序号,源文件名,占几格,起止秒,是否空白帧");
        var secondsPerOutputFrame = 1.0 / plan.OutputFps;
        foreach (var frame in plan.Frames)
        {
            var start = (frame.OutputIndex - 1) * secondsPerOutputFrame;
            var end = frame.OutputIndex * secondsPerOutputFrame;
            builder.AppendLine(string.Join(',',
                frame.OutputIndex.ToString(CultureInfo.InvariantCulture),
                frame.SourceFrameOrdinal.ToString(CultureInfo.InvariantCulture),
                Quote(frame.SourceFileName),
                frame.DurationFrames.ToString(CultureInfo.InvariantCulture),
                FormattableString.Invariant($"{start:0.###}-{end:0.###}"),
                frame.IsBlank ? "是" : "否"));
        }

        AtomicFileWriter.WriteAllText(
            Path.Combine(outputDirectory, BasePlateExportPlanner.ManifestFileName),
            builder.ToString(),
            Utf8WithBom);
    }

    private static string Quote(string value) =>
        value.Contains(',') ? $"\"{value}\"" : value;
}
