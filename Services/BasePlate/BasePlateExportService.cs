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

/// <summary>
/// 一次底板导出的进度（给全局进度条用）：<see cref="Percent"/> 是 0~100 的百分比。
///
/// 这里报百分比而不是「已完成 / 总数」，是因为这次导出混着两种单位：
/// 前半段每落一张 PNG 算一个单位，后半段是 PsdWriter 自己那套（组数 + 图层记录条数）。
/// 两个分母都不一样，让壳侧拿分子分母去除迟早会算错，所以在服务里一次算清。
/// </summary>
internal sealed record BasePlateExportProgress(double Percent, string Message);

/// <summary>一次底板导出的结果。</summary>
internal sealed record BasePlateExportResult(
    string OutputDirectory,
    int FrameCount,
    long TotalBytes,
    int RemovedStaleFiles,
    string PsdFilePath,
    long PsdBytes,
    int EffectLayerCount = 0);

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
/// 组里是那层 <c>原本帧</c>（对照底图）+ 一层 <c>特效</c> ——
/// 给画世界 / Photoshop 直接导入，省掉"一张张导、还分不清先后"，组里还能随便加层返工。</item>
/// <item>特效层默认是**空层**；走「导出底板（带特效）」时由
/// <c>effectFramePaths</c> 把**已经画好的特效帧**填进去（没画过的帧仍然是空层），
/// 于是导入 PSD 之后能直接在原有特效上接着改。</item>
/// <item>图层组默认**只有第 1 组亮着**（第 2 组往后关掉眼睛 + 折叠起来）——
/// 几十组一起亮着叠出来就是一张糊图，画的时候还得一个个点灭。
/// 要看哪一帧，展开那一组、点开眼睛就行。</item>
/// </list>
/// </summary>
internal sealed class BasePlateExportService
{
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    /// <summary>
    /// PNG 阶段占进度条的百分比：剩下的留给 PSD 那一段。
    /// PNG 是一张张复制/编码（纯写盘，快），PSD 是几百条图层记录逐行 RLE（慢得多），
    /// 所以大头留给后面，条子看起来才是匀速往前走。
    /// </summary>
    private const double PngPhasePercent = 40;

    /// <param name="effectFramePaths">
    /// 可选的**逐格特效帧**：键是格号（1 起，和图层组 <c>帧NNNN</c> 同号），值是那张特效 PNG 的路径。
    /// 传 null（默认）= 老行为，所有「特效」层都是空层。
    /// </param>
    /// <remarks>
    /// 这件事是**整块丢到后台线程**做的（2026-09-27 晓桀：「两个导出底板都会卡的无响应」）：
    /// 66 帧的 PNG 复制 + 66 个图层组逐行 RLE，之前全压在界面线程上，窗口自然一动不动。
    /// <c>Task.Run</c> 是这里唯一的让出点，重活全在 <see cref="ExportCore"/> 里；
    /// <see cref="IProgress{T}"/> 由壳侧用 <c>new Progress&lt;T&gt;(...)</c> 建，自带界面线程的
    /// <c>SynchronizationContext</c>，所以后台线程 <c>Report</c> 也照样回到界面上更新进度条。
    /// </remarks>
    public Task<BasePlateExportResult> ExportAsync(
        BasePlateExportPlan plan,
        string workspaceRootPath,
        IProgress<BasePlateExportProgress>? progress,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<int, string>? effectFramePaths = null)
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

        return Task.Run(
            () => ExportCore(plan, outputDirectory, effectFramePaths, progress, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// 真正的导出流程，只在后台线程上跑（别拿到界面线程上调）。
    /// 进度分两段：PNG 阶段占 <see cref="PngPhasePercent"/>%，之后的 PSD 阶段接着走到 100%
    /// （那一段自己有一套单位，由 <see cref="PsdWriter"/> 的进度回调报上来）。
    /// </summary>
    private static BasePlateExportResult ExportCore(
        BasePlateExportPlan plan,
        string outputDirectory,
        IReadOnlyDictionary<int, string>? effectFramePaths,
        IProgress<BasePlateExportProgress>? progress,
        CancellationToken cancellationToken)
    {
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
                PngPhasePercent * completed / Math.Max(1, plan.Frames.Count),
                $"{plan.FileNamePrefix} {completed}/{plan.Frames.Count}"));
        }

        WriteManifest(plan, outputDirectory);
        var (psdPath, psdBytes, effectLayerCount) = WritePsd(
            plan, outputDirectory, effectFramePaths, progress, cancellationToken);
        return new BasePlateExportResult(
            outputDirectory, plan.Frames.Count, totalBytes, removedStaleFiles, psdPath, psdBytes, effectLayerCount);
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
    /// 「特效」层例外：它取自 <paramref name="effectFramePaths"/>（已经画好的特效帧，
    /// 落点在动作目录的 <c>Effects/</c> 里），没给或那一格没画过就是空层。
    ///
    /// **可视性**（2026-09-27 加）：只有第 1 组亮着，后面的组全部关掉眼睛 + 折叠起来 ——
    /// 66 组一起亮着叠出来就是一张糊图，画的人还得一个个点灭。
    ///
    /// **背景**（2026-09-27 加）：组外面垫一层纯色 <c>#6B6B6B</c> 的「背景」（见
    /// <see cref="BasePlateExportPlanner.BackgroundLayerName"/>），就是画世界那块默认底色。
    /// </summary>
    private static (string Path, long Bytes, int EffectLayerCount) WritePsd(
        BasePlateExportPlan plan,
        string outputDirectory,
        IReadOnlyDictionary<int, string>? effectFramePaths,
        IProgress<BasePlateExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        // PSD 的形状（2026-09-26 定，晓桀：「图层组里默认就是原本帧，这样子才方便画，
        // 图层组就默认一个原本帧、默认一个空白层就行了」）：
        //
        //   一个图层组 = 一帧，组名 `帧0001`…，组里两层：
        //       原本帧   ← 这一帧的对照底图，**读回时按名字跳过**
        //       特效     ← 画在这一层（或再加层），读回时按组内顺序 source-over
        //
        // 对照底图**放在组里**而不是外面：展开一个组就能对着画，不用去底下一长串参考层里翻
        // （以前那版把 N 张参考层平铺在最下面，26 帧就是 26 张全亮着叠在一起）。
        // 组里能放任意多层：画的人不用合并，也就能返工。
        //
        // 「特效」层的内容（2026-09-27 加）：普通导出是空层；「导出底板（带特效）」
        // 把已有的特效帧填进来，于是重新导入 PSD 就能在原有特效上接着改，而不是从零再画一遍。
        var framePaths = plan.Frames
            .Select(frame => Path.Combine(
                outputDirectory, BasePlateExportPlanner.FormatFrameFileName(plan, frame)))
            .ToArray();
        var effectLayerCount = 0;
        var groups = plan.Frames
            .Select(frame =>
            {
                var effectImagePath = ResolveEffectImagePath(effectFramePaths, frame.OutputIndex);
                if (effectImagePath.Length > 0)
                {
                    effectLayerCount++;
                }

                return new PsdLayerGroup(
                    BasePlateExportPlanner.FormatFrameGroupName(frame.OutputIndex),
                    [
                        new PsdLayerSource(
                            BasePlateExportPlanner.BasePlateLayerName,
                            framePaths[frame.OutputIndex - 1]),
                        new PsdLayerSource(BasePlateExportPlanner.EffectLayerName, effectImagePath)
                    ],
                    // 第 1 组照旧亮着、展开着；第 2 组往后都关掉 + 折起来。
                    // 晓桀存回来的那份 PSD 里，被点灭的组同时也都是折叠的，照它写最稳。
                    Hidden: frame.OutputIndex > 1,
                    Collapsed: frame.OutputIndex > 1);
            })
            .ToArray();
        progress?.Report(new BasePlateExportProgress(
            PngPhasePercent,
            effectLayerCount > 0
                ? $"正在生成画世界工程（{groups.Length} 个图层组，其中 {effectLayerCount} 组带特效）"
                : $"正在生成画世界工程（{groups.Length} 个图层组）"));

        var path = Path.Combine(outputDirectory, BasePlateExportPlanner.FormatPsdFileName(plan));
        // 垫在最底下那层「背景」（晓桀 2026-09-27）：组**外面**的一层纯色，就是画世界给
        // 「没有背景层的文档」铺的那块底色 #6B6B6B（从截图里量的）。好处是画世界里看到的
        // 画面一模一样、不用改画法，而 PS / 别的软件打开、Windows 拿它当缩略图时底下有底，
        // 不再是透明格子。读回特效时组外面的图层一律不看，所以它不会被当成某一帧的内容。
        var backgroundLayer = PsdLayerSource.Solid(
            BasePlateExportPlanner.BackgroundLayerName,
            BasePlateExportPlanner.BackgroundRed,
            BasePlateExportPlanner.BackgroundGreen,
            BasePlateExportPlanner.BackgroundBlue);
        var bytes = PsdWriter.WriteGrouped(
            path,
            plan.CanvasWidth,
            plan.CanvasHeight,
            [backgroundLayer],
            groups,
            // 后半段进度全靠它：编完一组报一次、RLE 编完一条图层记录再报一次
            // （最后那条 RLE 尾巴才是整件事最慢的一段，只报"组"的话条子会提前停在 100%）。
            onProgress: (completed, total) => progress?.Report(new BasePlateExportProgress(
                PngPhasePercent + (100 - PngPhasePercent) * completed / Math.Max(1, total),
                $"正在生成画世界工程 {completed}/{total}")),
            cancellationToken: cancellationToken);
        return (path, bytes, effectLayerCount);
    }

    /// <summary>
    /// 取这一格该填进「特效」层的图；没有就返回空串（= <see cref="PsdWriter"/> 认的全透明空层）。
    ///
    /// 字典里写了文件却不在盘上只可能是外部删了 —— 记一条警告然后当空层，
    /// 不因为少一张特效帧就把整次导出打断。
    /// </summary>
    private static string ResolveEffectImagePath(
        IReadOnlyDictionary<int, string>? effectFramePaths,
        int outputIndex)
    {
        if (effectFramePaths is null ||
            !effectFramePaths.TryGetValue(outputIndex, out var imagePath) ||
            string.IsNullOrWhiteSpace(imagePath))
        {
            return string.Empty;
        }

        if (File.Exists(imagePath))
        {
            return imagePath;
        }

        ToolboxLog.Warn($"特效帧不在盘上了，这一格留空：「{imagePath}」");
        return string.Empty;
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
