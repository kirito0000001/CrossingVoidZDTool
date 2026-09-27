using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 底板导出里的一**张**输出图。
///
/// 底板 = 给特效绘制对照用的逐帧 PNG：动作本来 10fps、每格有持续时间，
/// 导出时按倍数把每格再切细，画特效的人就有了和动作严格对齐的参考底。
/// </summary>
internal sealed record BasePlateOutputFrame(
    int OutputIndex,
    int SourceFrameOrdinal,
    string SourceFilePath,
    string SourceFileName,
    int DurationFrames,
    bool IsBlank,
    int CanvasWidth,
    int CanvasHeight);

/// <summary>一次底板导出的完整计划（纯数据，可直接断言）。</summary>
internal sealed record BasePlateExportPlan(
    string OutputDirectory,
    string FileNamePrefix,
    string CharacterCode,
    string ActionVariantCode,
    double ActionFps,
    int Multiplier,
    double OutputFps,
    double DurationSeconds,
    int CanvasWidth,
    int CanvasHeight,
    IReadOnlyList<BasePlateOutputFrame> Frames);

/// <summary>
/// 底板导出的「算什么」这一半：输出几张、每张对应哪一帧、画布多大、落哪。
///
/// 拆成纯函数是为了能在回归里直接断言 —— 这些规则（尤其"每格要重复几次"）
/// 一旦算错，画出来的特效会在时序上整体偏移，而画的人不一定看得出来。
/// </summary>
internal static class BasePlateExportPlanner
{
    /// <summary>
    /// 底板按动作帧率的 **2 倍** 导出。
    ///
    /// 界面上刻意不给填倍数、也不给填 fps：动作帧率是"每格几秒"的唯一真相，
    /// 让人再填一个 fps 迟早会有人填出对不上的数。要改倍率就改这一处。
    /// </summary>
    public const int Multiplier = 2;

    /// <summary><c>&lt;工作区&gt;/Export/&lt;角色&gt;/BasePlate/</c>，和 Atlas 并列。</summary>
    public const string FolderName = "BasePlate";

    /// <summary>导出目录里那份「哪张图对应哪一帧」的对照表。</summary>
    public const string ManifestFileName = "frames.csv";

    /// <summary>导出目录里那份多图层 PSD 的后缀（给画世界 / PS 导入用）。</summary>
    public const string PsdExtension = ".psd";

    /// <summary>
    /// 底板目录里、给**读回来的特效帧**用的子文件夹：<c>&lt;动作&gt;-2x/Effect/</c>。
    ///
    /// 晓桀 2026-09-25：「重新导入/从 PSD 读回的特效就在底板文件夹里面再开一个文件夹，
    /// 这样子方便我查看」—— 特效帧和它对照的底板 PNG、那份 PSD 挨在一起，
    /// 不用在工具箱内部的目录里翻。
    ///
    /// ⚠️ 这个子文件夹**不受"重新导出底板时清空目录"的影响**（见 <c>BasePlateExportService</c>）：
    /// 里面的特效是画出来的成果，重导一次底板不该把它删了。
    /// </summary>
    public const string EffectFolderName = "Effect";

    /// <summary>PSD 里一组 = 一帧的组名，形如 <c>帧0001</c>。</summary>
    public static string FormatFrameGroupName(int outputIndex) =>
        $"帧{outputIndex.ToString("0000", System.Globalization.CultureInfo.InvariantCulture)}";

    /// <summary>
    /// 组里那层**原本帧**（对照底图）的层名。
    ///
    /// 晓桀 2026-09-26：「图层组里默认就是原本帧，这样子才方便画，图层组就默认一个原本帧、
    /// 默认一个空白层就行了」—— 把对照底图放进**这一帧自己的组**里，展开一个组就能对着画，
    /// 不用去底下一长串参考层里翻。
    ///
    /// ⚠️ 读回时**按这个名字跳过它**（不跳过的话对照底图会被合并进特效帧，
    /// 表现是「特效里多了个角色」）。所以这一层**别改名**：改了就会被当成分内的内容。
    /// </summary>
    public const string BasePlateLayerName = "原本帧";

    /// <summary>PSD 里给画特效留的那张空层。<b>组内</b>，读回时和用户自己加的层一起合并。</summary>
    public const string EffectLayerName = "特效";

    /// <summary>
    /// 垫在最底下、**不进任何图层组**的那层「背景」（纯色 <c>#6B6B6B</c>）。
    ///
    /// 晓桀 2026-09-27：「然后默认的背景是这个颜色」—— 画世界给「没有背景层的文档」
    /// 铺的默认底色量出来就是 <c>#6B6B6B</c>，那就干脆在文件里写一层真背景：
    /// 在画世界里看到的画面一样（它不再自己铺那块灰），而 PS / 别的软件打开也不再是格子底。
    ///
    /// ⚠️ 读回特效时**组外面的图层一律不看**（见 <c>SequenceEffectPsdImportService.CollectGroups</c>），
    /// 所以这一层不会被当成"某一帧画的内容"。
    /// </summary>
    public const string BackgroundLayerName = "背景";

    /// <summary>「背景」层的颜色，就是画世界那块默认底色。</summary>
    public const byte BackgroundRed = 0x6B;

    /// <summary>「背景」层的颜色，就是画世界那块默认底色。</summary>
    public const byte BackgroundGreen = 0x6B;

    /// <summary>「背景」层的颜色，就是画世界那块默认底色。</summary>
    public const byte BackgroundBlue = 0x6B;

    /// <summary>组的落点：<c>&lt;底板目录&gt;/Effect/</c>。</summary>
    public static string ResolveEffectFolderPath(string outputDirectory) =>
        Path.Combine(outputDirectory, EffectFolderName);

    /// <summary>
    /// 挑 PSD 时默认打开的目录 —— 就是这个动作的**底板目录**（<c>&lt;动作&gt;-2x/</c>）。
    ///
    /// 晓桀 2026-09-27：「浏览器默认打开底板的位置，然后我可以自行选择 PSD 导入」——
    /// 导出、画、存回原处的人，点两下就能读回来。
    /// 这个动作还没导出过底板时退到上一级 <c>BasePlate/</c>：那儿至少能看到几个
    /// <c>&lt;动作&gt;-2x</c> 目录，比"上次挑过的目录"更接近他要找的地方。
    /// </summary>
    public static string ResolvePsdPickerStartFolder(BasePlateExportPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Directory.Exists(plan.OutputDirectory)
            ? plan.OutputDirectory
            : Path.GetDirectoryName(plan.OutputDirectory) ?? plan.OutputDirectory;
    }

    /// <summary>
    /// 落点：<c>&lt;工作区&gt;/Export/&lt;角色&gt;/BasePlate/&lt;动作&gt;-&lt;倍数&gt;x/</c>。
    /// 目录名带倍数，是因为换倍率导出的帧数不一样，混在一起会互相覆盖。
    /// </summary>
    public static string ResolveOutputDirectory(
        string workspaceRootPath,
        string characterCode,
        string actionVariantCode,
        int multiplier = Multiplier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(characterCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionVariantCode);
        ArgumentOutOfRangeException.ThrowIfLessThan(multiplier, 1);
        return Path.Combine(
            Path.GetFullPath(workspaceRootPath),
            CharacterFolderLayout.Export,
            characterCode,
            FolderName,
            $"{actionVariantCode}-{multiplier}x");
    }

    public static BasePlateExportPlan Build(
        string workspaceRootPath,
        string characterCode,
        string actionVariantCode,
        IReadOnlyList<SequenceFrameItem> frames,
        double actionFps,
        int multiplier = Multiplier)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentOutOfRangeException.ThrowIfLessThan(multiplier, 1);
        if (frames.Count == 0)
        {
            throw new InvalidOperationException("这个动作没有素材帧，导不出底板。");
        }

        var fps = actionFps > 0 ? actionFps : 10;
        // 画布取非空白帧里最大的那张：空白帧要生成同尺寸的透明图，尺寸不一致时以大的为准，
        // 免得把别帧的内容裁掉。
        var canvasWidth = frames.Where(frame => !frame.IsBlank).Select(frame => frame.ActualWidth).DefaultIfEmpty(0).Max();
        var canvasHeight = frames.Where(frame => !frame.IsBlank).Select(frame => frame.ActualHeight).DefaultIfEmpty(0).Max();
        if (canvasWidth <= 0 || canvasHeight <= 0)
        {
            canvasWidth = frames.Select(frame => frame.ActualWidth).DefaultIfEmpty(0).Max();
            canvasHeight = frames.Select(frame => frame.ActualHeight).DefaultIfEmpty(0).Max();
        }

        var output = new List<BasePlateOutputFrame>();
        for (var index = 0; index < frames.Count; index++)
        {
            var frame = frames[index];
            // 一格 = 1/fps 秒；倍数 N 就是把这一格再切成 N 份。
            var repeat = Math.Max(1, frame.DurationFrames) * multiplier;
            for (var slice = 0; slice < repeat; slice++)
            {
                output.Add(new BasePlateOutputFrame(
                    OutputIndex: output.Count + 1,
                    SourceFrameOrdinal: index + 1,
                    SourceFilePath: frame.IsBlank ? string.Empty : frame.FilePath,
                    SourceFileName: frame.IsBlank ? "（空白帧）" : frame.FileName,
                    DurationFrames: frame.DurationFrames,
                    IsBlank: frame.IsBlank,
                    CanvasWidth: canvasWidth > 0 ? canvasWidth : frame.ActualWidth,
                    CanvasHeight: canvasHeight > 0 ? canvasHeight : frame.ActualHeight));
            }
        }

        var prefix = $"{characterCode}_{actionVariantCode}";
        return new BasePlateExportPlan(
            OutputDirectory: ResolveOutputDirectory(workspaceRootPath, characterCode, actionVariantCode, multiplier),
            FileNamePrefix: prefix,
            CharacterCode: characterCode,
            ActionVariantCode: actionVariantCode,
            ActionFps: fps,
            Multiplier: multiplier,
            OutputFps: fps * multiplier,
            DurationSeconds: output.Count / (fps * multiplier),
            CanvasWidth: canvasWidth,
            CanvasHeight: canvasHeight,
            Frames: output);
    }

    /// <summary>输出帧的规范文件名：<c>&lt;角色&gt;_&lt;动作&gt;_0001.png</c>。</summary>
    public static string FormatFrameFileName(BasePlateExportPlan plan, BasePlateOutputFrame frame) =>
        $"{plan.FileNamePrefix}_{frame.OutputIndex:0000}.png";

    /// <summary>
    /// 多图层 PSD 的文件名：<c>&lt;角色&gt;_&lt;动作&gt;-2x.psd</c>。
    /// 带上倍数，是因为换倍率导出的图层数不一样，名字一样会让人分不清手上这份是哪一版。
    /// </summary>
    public static string FormatPsdFileName(BasePlateExportPlan plan) =>
        $"{plan.FileNamePrefix}-{plan.Multiplier}x{PsdExtension}";
}
