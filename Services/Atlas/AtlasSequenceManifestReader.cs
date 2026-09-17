using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CrossingVoidZDTool.Services.Atlas;

/// <summary>
/// 读图集工具写出来的 <c>&lt;图集名&gt;_sequence.json</c>。
///
/// 同步序列需要它，是因为「一张图集切 N 个 Sprite」这件事必须知道
/// **每一格在图集贴图里的矩形**，而这个矩形只有打包器知道 ——
/// 装箱是它做的，框在哪只有它说了算。
///
/// 三处刻意的严格，都是为了不在 Unreal 里才发现切错：
/// <list type="bullet">
/// <item><b>必须比本轮打包新。</b>和 report 同一个道理 —— 陈旧的文件会让上一轮的矩形
/// 被当成这一轮的结果，切出来的精灵全是错位，而且看着「成功了」。</item>
/// <item><b>不能是空表。</b>零格的图集没有意义，当场报错比产出一张空图强。</item>
/// <item><b>按序号索引。</b>清单（我们写）和帧表（它写）之间唯一可靠的键是序号；
/// 名字虽然是同一套规则生成的，但装箱器内部会临时挂排序前缀再剥掉，
/// 拿名字做键等于把两边的实现细节绑在一起。</item>
/// </list>
/// </summary>
internal static class AtlasSequenceManifestReader
{
    /// <summary>帧表文件名：<c>&lt;图集名&gt;_sequence.json</c>，由打包器决定，不归我们改。</summary>
    public static string GetManifestPath(string outputDirectory, string atlasName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(atlasName);
        return Path.Combine(outputDirectory, atlasName + "_sequence.json");
    }

    /// <summary>
    /// 读帧表并校验。
    /// </summary>
    /// <param name="path">帧表路径（<see cref="GetManifestPath"/> 算出来的）。</param>
    /// <param name="startedAtUtc">本轮打包的开始时刻，用来确认这份帧表不是上一轮留下的。</param>
    /// <exception cref="InvalidOperationException">
    /// 文件缺失、不是本轮写的、读不出来、或一帧都没有时抛出。
    /// </exception>
    public static AtlasSequenceManifest Read(string path, DateTime startedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"图集没有写出帧表（{path}）。没有它就无法确定每一格在图集里的位置。");
        }

        if (!UnrealProcessRunner.IsFreshOutput(path, startedAtUtc))
        {
            throw new InvalidOperationException(
                $"图集的帧表不是本轮写出的（{path}）。"
                + "继续用下去会按上一轮的矩形切图，精灵会整体错位。");
        }

        AtlasSequenceManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize(
                File.ReadAllText(path),
                AtlasJsonContext.Default.AtlasSequenceManifest);
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"图集帧表读不出来（{path}）。", error);
        }

        if (manifest is null)
        {
            throw new InvalidOperationException($"图集帧表是空的（{path}）。");
        }

        if (manifest.Frames.Count == 0)
        {
            throw new InvalidOperationException($"图集帧表里一帧都没有（{path}）。");
        }

        return manifest;
    }

    /// <summary>
    /// 按序号建索引。序号重复时取第一条 —— 打包器写出来的序号本就应该唯一，
    /// 真出现重复是它那边的 bug，这里不替它猜，只保证取用是确定的。
    /// </summary>
    public static IReadOnlyDictionary<int, AtlasSequenceFrame> IndexByOrdinal(AtlasSequenceManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return manifest.Frames
            .GroupBy(frame => frame.Index)
            .ToDictionary(group => group.Key, group => group.First());
    }
}
