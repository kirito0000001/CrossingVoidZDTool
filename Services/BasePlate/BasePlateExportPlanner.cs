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
}
