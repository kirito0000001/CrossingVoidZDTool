using System;
using System.Collections.Generic;
using System.Linq;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 按资产命名把导出的图片和语音归类分桶。归类只看名字（语音那一路还会参考序列反推结果），
/// 不看磁盘、不读工程配置。
///
/// 从 <see cref="UnrealProjectSyncService"/> 里搬出来的：分类规则是这套工具里最容易判错、
/// 也最需要单独验的一段——分错桶不会报错，素材只是静默落进「其他图片」或「待分配语音」，
/// 得靠人自己发现。它原本埋在导出链路中段，非把整条链路跑起来碰不到。
///
/// 图片命名规则分两层：先按标准命名（'-' 分段，末段或倒数第二段是类型词）判，判不出来才退回
/// 关键词包含匹配。这个顺序不能反：标准命名里的角色名可能恰好含 "icon"、"item" 这类词。
/// </summary>
internal static class UnrealMaterialClassifier
{
    internal static IReadOnlyList<UnrealProjectSyncMaterialBucket> BuildMaterialBuckets(
        IReadOnlyList<UnrealProjectExportAsset> assets)
    {
        return assets
            .Where(asset => !IsObjectRedirector(asset))
            .Select(asset => (Kind: ClassifyMaterial(asset.AssetName), Asset: asset))
            .GroupBy(item => item.Kind.Key, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key == "OtherImage" ? 1 : 0)
            .ThenBy(group => group.First().Kind.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var kind = group.First().Kind;
                var views = group
                    .Select(item => item.Asset)
                    .OrderBy(asset => asset.AssetName, StringComparer.OrdinalIgnoreCase)
                    .Select(asset => new UnrealProjectSyncExportAssetView(
                        asset.AssetName,
                        asset.AssetClass,
                        asset.PackagePath,
                        asset.ObjectPath,
                        asset.SourceRoot,
                        asset.ExportedFilePath))
                    .ToArray();
                return new UnrealProjectSyncMaterialBucket(kind.DisplayName, kind.Key, views.Length, views);
            })
            .ToArray();
    }

    internal static IReadOnlyList<UnrealProjectSyncVoiceBucket> BuildVoiceBuckets(
        IReadOnlyList<UnrealProjectExportAsset> assets,
        UnrealProjectSyncSequenceFramesPreview sequencePreview)
    {
        var sequenceKinds = new Dictionary<string, VoiceMaterialKind>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in sequencePreview.Actions)
        {
            var kind = UnrealBridgeVoiceClassification.ClassifySequenceAction(action);
            foreach (var notify in action.SequenceSounds.Where(notify => notify.IsCharacterVoice))
            {
                var objectPath = UnrealProjectSyncService.NormalizeObjectPath(notify.SoundObjectPath);
                if (!string.IsNullOrWhiteSpace(objectPath) &&
                    (!sequenceKinds.TryGetValue(objectPath, out var existing) || existing == VoiceMaterialKind.Other))
                {
                    sequenceKinds[objectPath] = kind;
                }
            }
        }

        return assets
            .Where(asset => asset.AssetClass.Contains("SoundWave", StringComparison.OrdinalIgnoreCase))
            .Select(asset => (
                // 序列反推只认得九个动作码，标准动作表却有十九个：DefAtk、Defence、
                // Dodge、Idle、Move 这些一律返回 Other。以前只要反推命中就直接采用，
                // 于是一条失败语音只要被其中任何一个序列的 PlaySound 引用过，
                // 就被钉死成待分配——文件名里明写着 Defeat 也没用。
                // 反推给不出结论时要退回按名字识别。
                Kind: sequenceKinds.TryGetValue(UnrealProjectSyncService.NormalizeObjectPath(asset.ObjectPath), out var sequenceKind)
                    && sequenceKind != VoiceMaterialKind.Other
                    ? sequenceKind
                    : UnrealBridgeVoiceClassification.Classify(asset.PackagePath, asset.AssetName),
                Asset: asset))
            .GroupBy(item => item.Kind)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var spec = VoiceMaterialService.GetSpec(group.Key);
                var views = group
                    .Select(item => item.Asset)
                    .OrderBy(asset => asset.AssetName, StringComparer.OrdinalIgnoreCase)
                    .Select(asset => new UnrealProjectSyncExportAssetView(
                        asset.AssetName,
                        asset.AssetClass,
                        asset.PackagePath,
                        asset.ObjectPath,
                        asset.SourceRoot,
                        asset.ExportedFilePath))
                    .ToArray();
                return new UnrealProjectSyncVoiceBucket(
                    spec.DisplayName,
                    group.Key.ToString(),
                    views.Length,
                    views);
            })
            .ToArray();
    }

    internal static bool IsObjectRedirector(UnrealProjectExportAsset asset) =>
        asset.AssetClass.Contains("ObjectRedirector", StringComparison.OrdinalIgnoreCase);

    internal static (string Key, string DisplayName) ClassifyMaterial(string assetName)
    {
        if (TryClassifyStandardMaterialName(assetName, out var standardKind, out var isStandardName))
        {
            return standardKind;
        }

        if (isStandardName)
        {
            return ("OtherImage", "其他图片");
        }

        var name = assetName.Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase);
        if (name.Contains("itemicon", StringComparison.OrdinalIgnoreCase))
        {
            return ("ItemIcon", "图标-道具");
        }

        if (name.Contains("skillicon", StringComparison.OrdinalIgnoreCase))
        {
            return ("SkillIcon", "技能图标");
        }

        if (name.Contains("battleavatar", StringComparison.OrdinalIgnoreCase))
        {
            return ("BattleAvatar", "对局内头像");
        }

        if (name.Contains("fullmorphportrait", StringComparison.OrdinalIgnoreCase))
        {
            return ("FullMorphPortrait", "幻形完整立绘");
        }

        if (name.Contains("morphportrait", StringComparison.OrdinalIgnoreCase))
        {
            return ("MorphPortrait", "幻形立绘");
        }

        if (name.Contains("supportcutin", StringComparison.OrdinalIgnoreCase))
        {
            return ("SupportCutIn", "护援特写");
        }

        if (name.Contains("item", StringComparison.OrdinalIgnoreCase))
        {
            return ("ItemIcon", "图标-道具");
        }

        if (name.Contains("skill", StringComparison.OrdinalIgnoreCase))
        {
            return ("SkillIcon", "技能图标");
        }

        if (name.Contains("battle", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("avatar", StringComparison.OrdinalIgnoreCase))
        {
            return ("BattleAvatar", "对局内头像");
        }

        if (name.Contains("fullmorph", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("fullportrait", StringComparison.OrdinalIgnoreCase))
        {
            return ("FullMorphPortrait", "幻形完整立绘");
        }

        if (name.Contains("morph", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("portrait", StringComparison.OrdinalIgnoreCase))
        {
            return ("MorphPortrait", "幻形立绘");
        }

        if (name.Contains("background", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("bg", StringComparison.OrdinalIgnoreCase))
        {
            return ("Background", "背景图");
        }

        if (name.Contains("support", StringComparison.OrdinalIgnoreCase))
        {
            return ("SupportCutIn", "护援特写");
        }

        if (name.Contains("icon", StringComparison.OrdinalIgnoreCase))
        {
            return ("Icon", "头像");
        }

        return ("OtherImage", "其他图片");
    }

    internal static bool TryClassifyStandardMaterialName(
        string assetName,
        out (string Key, string DisplayName) kind,
        out bool isStandardName)
    {
        kind = default;
        isStandardName = false;
        var parts = assetName
            .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        isStandardName = true;
        if (TryResolveStandardMaterialKind(parts[^1], out kind))
        {
            return true;
        }

        if (parts.Length >= 3 && TryResolveStandardMaterialKind(parts[^2], out kind))
        {
            return true;
        }

        return false;
    }

    internal static bool TryResolveStandardMaterialKind(
        string type,
        out (string Key, string DisplayName) kind)
    {
        kind = type.ToLowerInvariant() switch
        {
            "itemicon" => ("ItemIcon", "图标-道具"),
            "skillicon" => ("SkillIcon", "技能图标"),
            "battleavatar" => ("BattleAvatar", "对局内头像"),
            "fullmorphportrait" => ("FullMorphPortrait", "幻形完整立绘"),
            "morphportrait" => ("MorphPortrait", "幻形立绘"),
            "background" => ("Background", "背景图"),
            "supportcutin" => ("SupportCutIn", "护援特写"),
            "icon" => ("Icon", "头像"),
            "otherimage" => ("OtherImage", "其他图片"),
            _ => default
        };

        return kind.Key is not null;
    }
}
