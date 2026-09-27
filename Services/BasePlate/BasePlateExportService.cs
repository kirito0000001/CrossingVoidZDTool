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
    int RemovedStaleFiles,
    string PsdFilePath,
    long PsdBytes);

/// <summary>
/// 底板导出的「写盘」这一半。
///
/// 规则（和界面上说的一致）：
/// <list type="bullet">
/// <item>非空白帧 = 把该帧的源图**原样复制**（不缩放、不重采样、保留 alpha）；</item>
/// <item>空白帧 = 生成一张同画布的**全透明** PNG，保住时间轴节奏；</item>
/// <item>导出目录**只放这一次的结果**：写之前先清空它（只允许清
/// <c>&lt;工作区&gt;/Export/&lt;角色&gt;/BasePlate/</c> 之下的目录，写错路径也不会误删别处）；</item>
/// <item>顺带写一份 <c>frames.csv</c>，记「第几张 → 原序列第几帧 / 原文件名 / 占几格」；</item>
/// <item>再写一份**多图层 PSD**（<see cref="PsdWriter"/>）：**一帧一个图层组**（组名 <c>帧0001</c>…），
/// 组里是那层 <c>原本帧</c>（对照底图）+ 一张空的 <c>特效</c> 层 ——
/// 给画世界 / Photoshop 直接导入，省掉"一张张导、还分不清先后"，组里还能随便加层返工。</item>
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
        var (psdPath, psdBytes) = WritePsd(plan, outputDirectory, progress);
        await Task.CompletedTask;
        return new BasePlateExportResult(
            outputDirectory, plan.Frames.Count, totalBytes, removedStaleFiles, psdPath, psdBytes);
    }

    /// <summary>
    /// 把刚写出来的那批 PNG 再打成一份多图层 PSD：一帧一个图层组、按顺序排、空白帧也占一组。
    ///
    /// 组名直接取帧号（<c>帧0001</c>…），和 PNG 文件名、frames.csv 的「输出帧」列一对一，
    /// 画的时候展开哪个组就知道在画第几帧。组顺序是**第 1 帧在最下面**、往上依次叠
    /// （用户确认过画世界要的就是这个方向）。
    ///
    /// 图层来源是**刚落到盘上的 PNG**，不是内存里的另一份像素 —— 这样 PSD 和旁边那堆
    /// PNG 永远一致，不会出现"图集里是新的、PNG 是旧的"这种对不上的情况。
    /// </summary>
    private static (string Path, long Bytes) WritePsd(
        BasePlateExportPlan plan,
        string outputDirectory,
        IProgress<BasePlateExportProgress>? progress)
    {
        // PSD 的形状（2026-09-26 定，晓桀：「图层组里默认就是原本帧，这样子才方便画，
        // 图层组就默认一个原本帧、默认一个空白层就行了」）：
        //
        //   一个图层组 = 一帧，组名 `帧0001`…，组里两层：
        //       原本帧   ← 这一帧的对照底图，**读回时按名字跳过**
        //       特效     ← 空层，在这里（或再加层）画
        //
        // 对照底图**放在组里**而不是外面：展开一个组就能对着画，不用去底下一长串参考层里翻
        // （以前那版把 N 张参考层平铺在最下面，26 帧就是 26 张全亮着叠在一起）。
        // 组里能放任意多层：画的人不用合并，也就能返工。
        var framePaths = plan.Frames
            .Select(frame => Path.Combine(
                outputDirectory, BasePlateExportPlanner.FormatFrameFileName(plan, frame)))
            .ToArray();
        var groups = plan.Frames
            .Select(frame => new PsdLayerGroup(
                BasePlateExportPlanner.FormatFrameGroupName(frame.OutputIndex),
                [
                    new PsdLayerSource(
                        BasePlateExportPlanner.BasePlateLayerName,
                        framePaths[frame.OutputIndex - 1]),
                    new PsdLayerSource(BasePlateExportPlanner.EffectLayerName, string.Empty)
                ]))
            .ToArray();
        progress?.Report(new BasePlateExportProgress(
            plan.Frames.Count,
            plan.Frames.Count,
            $"正在生成画世界工程（{groups.Length} 个图层组）"));

        var path = Path.Combine(outputDirectory, BasePlateExportPlanner.FormatPsdFileName(plan));
        var bytes = PsdWriter.WriteGrouped(
            path, plan.CanvasWidth, plan.CanvasHeight, [], groups);
        return (path, bytes);
    }

    /// <summary>
    /// 清空导出目录里的文件（含子目录），返回删掉的文件数。
    ///
    /// ⚠️ **`Effect/` 子文件夹要留着**：里面是从 PSD 读回来的特效帧，是画出来的成果，
    /// 重导一次底板不该把它删了（晓桀 2026-09-25 把特效放到这儿就是为了一眼能看到）。
    /// </summary>
    private static int ClearOutputDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var preserved = BasePlateExportPlanner.ResolveEffectFolderPath(directory);
        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            if (CharacterWorkspaceService.IsPathInsideDirectory(file, preserved))
            {
                continue;
            }

            if (AtomicFileWriter.TryDelete(file))
            {
                removed++;
            }
        }

        foreach (var sub in Directory.EnumerateDirectories(directory))
        {
            // 只跳过那一个受保护目录本身；它底下（理论上有）别的子目录跟着它一起留。
            if (CharacterWorkspaceService.IsPathInsideDirectory(sub, preserved) ||
                CharacterWorkspaceService.IsPathInsideDirectory(preserved, sub))
            {
                continue;
            }

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
