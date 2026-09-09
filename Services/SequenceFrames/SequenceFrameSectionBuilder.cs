using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 把一份清单摊成界面要用的帧格列表：解析每张图的真实尺寸、拼出带版本号的预览 URI、
/// 标注哪些帧格复用了同一张图，再汇总成一句状态文案。
///
/// 从 <see cref="SequenceFrameService"/> 里搬出来的——这一段是纯读，不改任何文件，
/// 却夹在一堆会写盘的编辑操作中间。
///
/// 两处不显眼但要紧的细节：
/// 预览 URI 后面挂的 <c>?v=</c> 是文件时间戳加长度，不带它的话替换同名帧之后
/// WinUI 的图片缓存会继续显示旧图；<see cref="AnnotateFrameReuse"/> 的分组顺序按
/// 首次出现的帧号排，界面上的复用配色才不会每次刷新都换一遍。
/// </summary>
internal static class SequenceFrameSectionBuilder
{
    public static SequenceFrameSection Build(
        CharacterCard character,
        SequenceFrameAction action,
        SequenceFrameManifest manifest,
        CancellationToken cancellationToken)
    {
        var frames = manifest.Frames
            .Select((entry, index) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.IsBlank)
                {
                    return CreateBlankFrameItem(character, entry, index + 1);
                }

                return CreateFrameItem(
                    SequenceActionFolderLayout.ResolveManifestPath(character, action, entry.RelativePath),
                    index + 1,
                    manifest.Frames.Count,
                    entry.DurationFrames,
                    SequenceActionFolderLayout.ResolveVoicePath(character, entry.VoiceRelativePath),
                    entry.SyncId);
            })
            .Where(frame => frame.IsBlank || File.Exists(frame.FilePath))
            .OrderBy(frame => frame.Index)
            .ToList();
        frames = AnnotateFrameReuse(frames);

        var invalidCount = frames.Count(frame => !frame.IsValid);
        var statusText = frames.Count == 0
            ? "未设置"
            : invalidCount > 0
                ? $"{frames.Count} 张，{invalidCount} 张尺寸不合规"
                : $"{frames.Count} 张，尺寸合规";
        return new SequenceFrameSection(action, frames, statusText, frames.Count == 0 || invalidCount > 0);
    }

    /// <summary>
    /// 标注复用：指向同一个文件的帧格算一组，给同一个配色下标，并记下这组出现在哪几帧。
    /// 分组按组内最小帧号排序——顺序稳定，配色才不会每次刷新都变。
    /// </summary>
    private static List<SequenceFrameItem> AnnotateFrameReuse(IReadOnlyList<SequenceFrameItem> frames)
    {
        var annotated = frames
            .Select(frame => frame with
            {
                ReuseCount = 1,
                ReuseOccurrence = 1,
                ReuseSourceIndex = 0,
                ReusePositionsText = string.Empty,
                ReuseColorIndex = -1
            })
            .ToList();
        var groups = annotated
            .Where(frame => !frame.IsBlank && !string.IsNullOrWhiteSpace(frame.FilePath))
            .GroupBy(frame => frame.FilePath, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.Min(frame => frame.Index))
            .ToList();

        for (var colorIndex = 0; colorIndex < groups.Count; colorIndex++)
        {
            var groupFrames = groups[colorIndex].OrderBy(frame => frame.Index).ToList();
            var positions = string.Join("、", groupFrames.Select(frame => frame.Index));
            for (var occurrenceIndex = 0; occurrenceIndex < groupFrames.Count; occurrenceIndex++)
            {
                var frameIndex = annotated.FindIndex(frame => frame.Index == groupFrames[occurrenceIndex].Index);
                annotated[frameIndex] = annotated[frameIndex] with
                {
                    ReuseCount = groupFrames.Count,
                    ReuseOccurrence = occurrenceIndex + 1,
                    ReuseSourceIndex = groupFrames[0].Index,
                    ReusePositionsText = positions,
                    ReuseColorIndex = colorIndex
                };
            }
        }

        return annotated;
    }

    private static SequenceFrameItem CreateFrameItem(
        string path,
        int sequenceIndex,
        int sequenceCount,
        int durationFrames,
        string voiceFilePath,
        string syncId)
    {
        var actualWidth = 0;
        var actualHeight = 0;
        try
        {
            using var image = Image.FromFile(path);
            actualWidth = image.Width;
            actualHeight = image.Height;
        }
        catch
        {
            // The item stays invalid and the UI will show its size as unreadable.
        }

        var info = new FileInfo(path);
        // 版本号进 URI：替换同名帧之后不带它的话，图片控件会继续显示缓存里的旧图。
        var version = $"{info.LastWriteTimeUtc.Ticks}-{info.Length}";
        var fileUri = $"{new Uri(info.FullName).AbsoluteUri}?v={Uri.EscapeDataString(version)}";
        return new SequenceFrameItem(
            info.FullName,
            fileUri,
            $"{MaterialSequenceNaming.FormatIndex(sequenceIndex, sequenceCount)}  {info.Name}",
            $"{info.FullName}|{version}",
            sequenceIndex,
            actualWidth,
            actualHeight,
            actualWidth == SequenceFrameSpec.RequiredWidth && actualHeight == SequenceFrameSpec.RequiredHeight,
            info.LastWriteTime,
            DurationFrames: Math.Clamp(durationFrames, 1, SequenceFrameSpec.MaxFrameDuration),
            VoiceFilePath: voiceFilePath,
            VoiceFileName: Path.GetFileName(voiceFilePath),
            SyncId: syncId);
    }

    private static SequenceFrameItem CreateBlankFrameItem(
        CharacterCard character,
        SequenceFrameManifestEntry entry,
        int sequenceIndex)
    {
        var voiceFilePath = SequenceActionFolderLayout.ResolveVoicePath(character, entry.VoiceRelativePath);
        return new SequenceFrameItem(
            string.Empty,
            string.Empty,
            "空白帧",
            $"blank|{sequenceIndex}",
            sequenceIndex,
            SequenceFrameSpec.RequiredWidth,
            SequenceFrameSpec.RequiredHeight,
            true,
            DateTime.MinValue,
            true,
            Math.Clamp(entry.DurationFrames, 1, SequenceFrameSpec.MaxFrameDuration),
            voiceFilePath,
            Path.GetFileName(voiceFilePath),
            SyncId: entry.SyncId);
    }
}
