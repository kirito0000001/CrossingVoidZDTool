using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CrossingVoidZDTool.Services;

internal sealed class UnrealAssetNormalizationService
{
    public IReadOnlyList<UnrealAssetNormalizationItem> Build(
        CharacterCard character,
        UnrealProjectSyncCharacterCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(candidate);

        var toolbox = new UnrealBridgeToolboxSnapshotService().BuildForSynchronization(character);
        var toolboxItems = toolbox.Items
            .Where(item => item.Module is UnrealBridgeModule.BaseMaterials or UnrealBridgeModule.Voices)
            .Where(item => !string.IsNullOrWhiteSpace(item.AssetPath))
            .ToArray();
        var unrealItems = candidate.MaterialBuckets
            .SelectMany(bucket => bucket.Assets.Select(asset => new
            {
                Module = UnrealBridgeModule.BaseMaterials,
                Category = bucket.Kind,
                asset.AssetName,
                asset.ObjectPath,
                asset.ExportedFilePath,
                ReferenceCount = 0
            }))
            .Concat(candidate.VoiceBuckets.SelectMany(bucket => bucket.Assets.Select(asset => new
            {
                Module = UnrealBridgeModule.Voices,
                Category = bucket.Kind,
                asset.AssetName,
                asset.ObjectPath,
                asset.ExportedFilePath,
                ReferenceCount = 0
            })))
            .ToArray();

        var result = new List<UnrealAssetNormalizationItem>();
        foreach (var unrealItem in unrealItems
                     .GroupBy(item => item.ObjectPath, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First())
                     .OrderBy(item => item.Module)
                     .ThenBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.AssetName, StringComparer.OrdinalIgnoreCase))
        {
            var moduleCandidates = toolboxItems
                .Where(item => item.Module == unrealItem.Module)
                .Select(item => new UnrealAssetNormalizationCandidate(item.StableId, Path.GetFileNameWithoutExtension(item.AssetPath), item.AssetPath))
                .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var candidates = toolboxItems
                .Where(item => item.Module == unrealItem.Module)
                .Where(item => string.Equals(ReadCategory(item.PayloadJson), unrealItem.Category, StringComparison.OrdinalIgnoreCase))
                .Select(item => new UnrealAssetNormalizationCandidate(item.StableId, Path.GetFileNameWithoutExtension(item.AssetPath), item.AssetPath))
                .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (candidates.Length == 0)
            {
                candidates = toolboxItems
                    .Where(item => item.Module == unrealItem.Module)
                    .Select(item => new UnrealAssetNormalizationCandidate(item.StableId, Path.GetFileNameWithoutExtension(item.AssetPath), item.AssetPath))
                    .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            var exactMatch = candidates.FirstOrDefault(item => string.Equals(
                NormalizeName(Path.GetFileNameWithoutExtension(item.AssetPath)),
                NormalizeName(unrealItem.AssetName),
                StringComparison.OrdinalIgnoreCase) &&
                IsCanonicalCharacterPath(unrealItem.ObjectPath, character.Code));
            exactMatch ??= moduleCandidates.FirstOrDefault(item => string.Equals(
                NormalizeName(Path.GetFileNameWithoutExtension(item.AssetPath)),
                NormalizeName(unrealItem.AssetName),
                StringComparison.OrdinalIgnoreCase) &&
                IsCanonicalCharacterPath(unrealItem.ObjectPath, character.Code));

            result.Add(new UnrealAssetNormalizationItem(
                unrealItem.ObjectPath,
                unrealItem.Module,
                unrealItem.Category,
                unrealItem.AssetName,
                unrealItem.ObjectPath,
                unrealItem.ExportedFilePath,
                unrealItem.ReferenceCount,
                candidates,
                exactMatch,
                exactMatch is not null));
        }

        return result;
    }

    private static string ReadCategory(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return "未分类";
        }

        var marker = "\"kind\":\"";
        var start = payload.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return "未分类";
        }

        start += marker.Length;
        var end = payload.IndexOf('"', start);
        return end > start ? payload[start..end] : "未分类";
    }

    private static string BuildSuggestedPath(string characterCode, UnrealBridgeSnapshotItem item)
    {
        var safeCode = Sanitize(characterCode);
        var safeName = Sanitize(Path.GetFileNameWithoutExtension(item.AssetPath));
        if (item.Module == UnrealBridgeModule.BaseMaterials)
        {
            if (string.Equals(ReadCategory(item.PayloadJson), BaseMaterialKind.BuffIcon.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return $"{UnrealProjectSyncService.TargetZdContentPath}/{safeCode}/BUFF/{safeName}.{safeName}";
            }

            return $"{UnrealProjectSyncService.TargetBaseMaterialContentPath}/{safeCode}/{safeName}.{safeName}";
        }

        if (item.Module == UnrealBridgeModule.Voices)
        {
            return $"{UnrealProjectSyncService.TargetZdContentPath}/{safeCode}/Sound/{ReadCategory(item.PayloadJson)}/{safeName}.{safeName}";
        }

        return $"{UnrealProjectSyncService.TargetZdContentPath}/{safeCode}/Material/{ReadAction(item.PayloadJson)}/{safeName}.{safeName}";
    }

    private static string ReadAction(string payload)
    {
        var marker = "\"actionCode\":\"";
        var start = payload.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return "未分类";
        }

        start += marker.Length;
        var end = payload.IndexOf('"', start);
        return end > start ? Sanitize(payload[start..end]) : "未分类";
    }

    private static string NormalizeName(string value) =>
        new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static bool IsCanonicalCharacterPath(string objectPath, string characterCode)
    {
        var normalizedPath = objectPath.Replace('\\', '/').TrimEnd('/');
        var marker = $"/{characterCode}/";
        // Unreal 的资产路径大小写不敏感：工程里目录是 /Game/ZD/misaka/ 而代号写作
        // Misaka 时，按 Ordinal 比会判成「不在规范位置」，条目变成待处理，
        // 第三步直接被拦住。本文件其余十处比较用的都是 OrdinalIgnoreCase，唯独这里没跟上。
        return normalizedPath.Contains(marker, StringComparison.OrdinalIgnoreCase);
    }

    private static string Sanitize(string value) =>
        new string((value ?? string.Empty).Select(character => char.IsLetterOrDigit(character) || character is '_' or '-' ? character : '_').ToArray()).Trim('_', '-');
}
