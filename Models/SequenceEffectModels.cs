using System;
using System.Collections.Generic;
using System.Linq;

namespace CrossingVoidZDTool;

/// <summary>
/// 特效层里的一帧。
///
/// **空帧是一等公民**：特效常常只有几帧有内容，其余整段时间都"没有特效"。
/// 两种写法都算空帧 —— 文件根本不存在，或者存在但整张全透明（用底板画的时候
/// 没动那几页，最容易是后者）。空帧在虚幻侧对应 **Flipbook 关键帧的空对象**，
/// 时间照占、什么都不画。
/// </summary>
internal sealed record SequenceEffectFrame(
    int Ordinal,
    string FileName,
    string FilePath,
    bool IsEmpty)
{
    public string DisplayName => IsEmpty ? "（空帧）" : FileName;
}

/// <summary>
/// 一个动作的**一个**特效层。
///
/// 帧率是动作的 <see cref="Multiplier"/> 倍（底板导出也是这个倍数），所以张数 = 动作总格数 × 倍数；
/// 每一张对应"导出底板"里同名的那一张，画完直接导回来就能对上号。
/// </summary>
internal sealed record SequenceEffectLayer(
    string LayerName,
    string AssetPrefix,
    int Multiplier,
    int ExpectedFrameCount,
    IReadOnlyList<SequenceEffectFrame> Frames,
    DateTime? ImportedAt)
{
    public static SequenceEffectLayer Empty(string layerName, string assetPrefix, int multiplier) =>
        new(layerName, assetPrefix, multiplier, ExpectedFrameCount: 0, [], null);

    public bool HasFrames => Frames.Count > 0;

    /// <summary>这一层按动作算该有多少张（动作总格数 × 倍数）；没算过就退化成"现有的张数"。</summary>
    public int FrameCount => ExpectedFrameCount > 0 ? ExpectedFrameCount : Frames.Count;

    /// <summary>空帧 = 该有但没有内容的那些（没画的、整张透明的都算）。</summary>
    public int EmptyFrameCount => Math.Max(0, FrameCount - Frames.Count);

    public string SummaryText => Frames.Count == 0
        ? "未导入"
        : EmptyFrameCount == 0
            ? $"{Frames.Count} 帧"
            : $"{Frames.Count} 帧（空 {EmptyFrameCount}）";
}

/// <summary>一次导入的结果（给界面和日志用）。</summary>
internal sealed record SequenceEffectImportResult(
    int ImportedFrames,
    int EmptyFrames,
    int IgnoredFrames,
    int ClearedFrames,
    string LayerFolderPath);
