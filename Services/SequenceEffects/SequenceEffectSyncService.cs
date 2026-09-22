using System;
using System.Collections.Generic;
using System.Linq;
using CrossingVoidZDTool.Services.Atlas;

namespace CrossingVoidZDTool.Services;

/// <summary>特效层里的一个**输出帧**（空帧也在列表里，只是没图）。</summary>
internal sealed record SequenceEffectSyncFrame(
    int OutputOrdinal,
    string SpriteAssetName,
    string FilePath,
    bool IsEmpty);

/// <summary>
/// 特效层同步出去长什么样：资产名、帧序、图集名、规范资产路径。
/// 计划、图集、差异过滤、内容指纹四处都读它 —— 各算一套的话迟早对不上。
/// </summary>
internal sealed record SequenceEffectSyncLayout(
    string LayerCode,
    string AtlasName,
    string FlipbookAssetName,
    string MaterialFolderPackagePath,
    double OutputFps,
    IReadOnlyList<SequenceEffectSyncFrame> Frames,
    IReadOnlyList<string> CanonicalAssetObjectPaths)
{
    /// <summary>有图的那些帧（空帧没有精灵资产）。</summary>
    public IReadOnlyList<SequenceEffectSyncFrame> FilledFrames =>
        Frames.Where(frame => !frame.IsEmpty).ToArray();
}

/// <summary>
/// 特效层怎么变成 Unreal 里的资产。
///
/// 形状和角色序列**同构**（用户拍板）：
/// <code>
/// &lt;动作&gt;_Effect_FrameNN_Sprite     ← 每个有图的输出帧一只精灵
/// &lt;动作&gt;_Effect_Flipbook           ← 整层一个翻转书（空帧 = 空关键帧，时间照占）
/// &lt;角色&gt;_&lt;动作&gt;_Effect            ← 这一层自己的图集贴图
/// </code>
/// 都落在**动作自己的 Material 目录**里（和角色序列同一层）。
///
/// 唯一和角色序列不同的是**帧率**：特效按动作帧率的 <see cref="BasePlateExportPlanner.Multiplier"/>
/// 倍逐帧（和导出的底板一一对应），所以 Flipbook 的 fps 也要翻倍。
/// </summary>
internal static class SequenceEffectSyncService
{
    public const string LayerSuffix = "Effect";

    /// <summary>层代号：<c>Sk2</c> → <c>Sk2_Effect</c>。</summary>
    public static string BuildLayerCode(string variantCode) => $"{variantCode}_{LayerSuffix}";

    /// <summary>这一层的图集名：<c>Misaka_Sk2_Effect</c>。</summary>
    public static string BuildAtlasName(string characterCode, string variantCode) =>
        $"{characterCode}_{variantCode}_{LayerSuffix}";

    /// <summary>Flipbook 资产名：<c>Sk2_Effect_Flipbook</c>。</summary>
    public static string BuildFlipbookName(string variantCode) => $"{variantCode}_{LayerSuffix}_Flipbook";

    /// <summary>
    /// 精灵名：<c>Sk2_Effect_Frame00_Sprite</c>。
    /// 序号从 0 起、两位补零，和角色序列那边（<c>Sk2_Frame00_Sprite</c>）同一套写法。
    /// </summary>
    public static string BuildSpriteName(string variantCode, int outputOrdinal) =>
        $"{variantCode}_{LayerSuffix}_Frame{Math.Max(0, outputOrdinal - 1):00}_Sprite";

    /// <summary>
    /// 这个资产路径是不是**特效层**的规范产物。
    ///
    /// 差异树里没有特效层的行（它随动作同步），所以 Unreal 侧"这个动作目录下多出来的资产"
    /// 要按**命名**认出它们是特效、不是需要清理的历史资产 —— 否则同步完特效，
    /// 第五步会立刻把它们列成"待删除"，一勾就删掉。
    /// </summary>
    public static bool IsEffectLayerAssetName(string? assetName)
    {
        var name = (assetName ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return false;
        }

        return (name.Contains($"_{LayerSuffix}_Frame", StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith("_Sprite", StringComparison.OrdinalIgnoreCase)) ||
               name.EndsWith($"_{LayerSuffix}_Flipbook", StringComparison.OrdinalIgnoreCase) ||
               // 图集贴图：<角色>_<动作>_Effect
               name.EndsWith($"_{LayerSuffix}", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 这一层同步出去的样子。**一张特效图都没有就返回 null**（没画特效的动作不参与同步）。
    /// </summary>
    public static SequenceEffectSyncLayout? TryBuildLayout(
        CharacterCard character,
        SequenceFrameSection section,
        SequenceActionDefinition definition,
        int formIndex,
        SequenceEffectLayer? layer,
        double actionFps)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(definition);
        if (layer is null || !layer.HasFrames)
        {
            return null;
        }

        var multiplier = Math.Max(1, layer.Multiplier);
        var variantCode = SequenceActionCatalog.GetVariantCode(definition, formIndex);
        var frames = new List<SequenceEffectSyncFrame>();
        var outputOrdinal = 0;
        foreach (var frame in section.Frames.OrderBy(item => item.Index))
        {
            var repeats = Math.Max(1, frame.DurationFrames) * multiplier;
            for (var slice = 0; slice < repeats; slice++)
            {
                outputOrdinal++;
                var effectFrame = layer.Frames.FirstOrDefault(item => item.Ordinal == outputOrdinal);
                var isEmpty = effectFrame is null || effectFrame.IsEmpty;
                frames.Add(new SequenceEffectSyncFrame(
                    outputOrdinal,
                    isEmpty ? string.Empty : BuildSpriteName(variantCode, outputOrdinal),
                    isEmpty ? string.Empty : effectFrame!.FilePath,
                    isEmpty));
            }
        }

        if (frames.All(frame => frame.IsEmpty))
        {
            return null;
        }

        var root = $"/Game/GameActor2D/{character.Code}";
        var materialFolder = $"{root}/Material/{SequenceActionCatalog.GetMaterialFolderName(definition, formIndex)}";
        var atlasName = BuildAtlasName(character.Code, variantCode);
        // **对象路径**（`<包>.<资产>`），不是归一化过的包路径：这份名单要原样交给
        // Python 当清理范围用，而清理那一步是拿"重建出来的资产路径"去比的。
        // 归一化会连大小写一起改掉，于是清理认不出自己刚建的那批（2026-09-22 实测：
        // 新建的图集/精灵/Flipbook 被整批删掉，紧接着读 Flipbook 就报实例为空）。
        var canonicalPaths = new List<string>
        {
            BuildObjectPath(materialFolder, BuildFlipbookName(variantCode)),
            BuildObjectPath(materialFolder, atlasName)
        };
        canonicalPaths.AddRange(frames
            .Where(frame => !frame.IsEmpty)
            .Select(frame => BuildObjectPath(materialFolder, frame.SpriteAssetName)));

        var fps = actionFps > 0 ? actionFps : SequenceFrameService.DefaultFps;
        return new SequenceEffectSyncLayout(
            BuildLayerCode(variantCode),
            atlasName,
            BuildFlipbookName(variantCode),
            materialFolder,
            fps * multiplier,
            frames,
            canonicalPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    /// <summary>`/Game/A/B` + `C` → `/Game/A/B/C.C`（Python 那侧用的对象路径写法）。</summary>
    private static string BuildObjectPath(string folder, string assetName) =>
        $"{folder}/{assetName}.{assetName}";
}
