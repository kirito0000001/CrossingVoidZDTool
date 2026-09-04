using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CrossingVoidZDTool.Services;

internal sealed class UnrealBridgeDiffService
{
    public IReadOnlyList<UnrealBridgeChange> Compare(
        UnrealBridgeSnapshot toolbox,
        UnrealBridgeSnapshot unreal,
        UnrealBridgeDirection direction,
        UnrealBridgeSyncState? baseline)
    {
        ArgumentNullException.ThrowIfNull(toolbox);
        ArgumentNullException.ThrowIfNull(unreal);

        var toolboxItems = ToItemMap(toolbox.Items, "工具箱");
        var unrealItems = ToItemMap(unreal.Items, "Unreal");
        if (direction == UnrealBridgeDirection.PublishToUnreal)
        {
            AlignCanonicalMaterialItems(toolboxItems, unrealItems, toolbox.CharacterCode);
        }
        return toolboxItems.Keys
            .Union(unrealItems.Keys, StringComparer.OrdinalIgnoreCase)
            .Select(stableId => BuildChange(
                stableId,
                toolboxItems.GetValueOrDefault(stableId),
                unrealItems.GetValueOrDefault(stableId),
                direction,
                baseline,
                direction == UnrealBridgeDirection.PublishToUnreal &&
                    toolboxItems.TryGetValue(stableId, out var matchedToolboxItem) &&
                    unrealItems.TryGetValue(stableId, out var matchedUnrealItem) &&
                    IsMigrationSafePair(matchedToolboxItem, matchedUnrealItem, toolbox.CharacterCode)))
            .OrderBy(change => change.Module)
            .ThenBy(change => change.StableId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AlignCanonicalMaterialItems(
        IDictionary<string, UnrealBridgeSnapshotItem> toolboxItems,
        IDictionary<string, UnrealBridgeSnapshotItem> unrealItems,
        string characterCode)
    {
        if (string.IsNullOrWhiteSpace(characterCode))
        {
            return;
        }

        var claimedUnrealIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var toolboxPair in toolboxItems.ToArray())
        {
            var toolboxItem = toolboxPair.Value;
            if (unrealItems.ContainsKey(toolboxPair.Key) ||
                toolboxItem.Module is not (UnrealBridgeModule.BaseMaterials or UnrealBridgeModule.Voices))
            {
                continue;
            }

            var canonicalPath = BuildCanonicalObjectPath(characterCode, toolboxItem);
            if (string.IsNullOrWhiteSpace(canonicalPath))
            {
                continue;
            }

            var matchingPair = unrealItems.FirstOrDefault(pair =>
                !claimedUnrealIds.Contains(pair.Key) &&
                pair.Value.Module == toolboxItem.Module &&
                SameObjectPath(pair.Value.SourceObjectPath, canonicalPath));
            if (string.IsNullOrWhiteSpace(matchingPair.Key))
            {
                var expectedFolder = GetCanonicalFolder(characterCode, toolboxItem);
                var expectedName = NormalizeAssetName(GetToolboxAssetName(toolboxItem));
                var fallbackMatches = unrealItems
                    .Where(pair =>
                        !claimedUnrealIds.Contains(pair.Key) &&
                        pair.Value.Module == toolboxItem.Module &&
                        string.Equals(GetObjectFolder(pair.Value.SourceObjectPath), expectedFolder, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(NormalizeAssetName(GetObjectAssetName(pair.Value.SourceObjectPath)), expectedName, StringComparison.Ordinal))
                    .ToArray();
                matchingPair = fallbackMatches.Length == 1 ? fallbackMatches[0] : default;
            }
            if (string.IsNullOrWhiteSpace(matchingPair.Key))
            {
                continue;
            }

            unrealItems.Remove(matchingPair.Key);
            unrealItems[toolboxPair.Key] = matchingPair.Value with { StableId = toolboxPair.Key };
            claimedUnrealIds.Add(toolboxPair.Key);
        }
    }

    private static string BuildCanonicalObjectPath(string characterCode, UnrealBridgeSnapshotItem toolboxItem)
    {
        var relative = toolboxItem.ToolboxRelativePath.Replace('\\', '/').Trim('/');
        var fileName = Path.GetFileNameWithoutExtension(
            string.IsNullOrWhiteSpace(relative) ? toolboxItem.AssetPath : relative);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = toolboxItem.NormalizedName;
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        if (toolboxItem.Module == UnrealBridgeModule.Voices &&
            relative.StartsWith("Sound/", StringComparison.OrdinalIgnoreCase))
        {
            var relativeFolder = relative[..relative.LastIndexOf('/')];
            return $"/Game/GameActor2D/{characterCode}/{relativeFolder}/{fileName}.{fileName}";
        }

        if (toolboxItem.Module == UnrealBridgeModule.BaseMaterials &&
            relative.StartsWith("AssetMaterial/BuffIcon/", StringComparison.OrdinalIgnoreCase))
        {
            return $"/Game/GameActor2D/{characterCode}/BUFF/{fileName}.{fileName}";
        }

        if (toolboxItem.Module == UnrealBridgeModule.BaseMaterials &&
            relative.StartsWith("AssetMaterial/", StringComparison.OrdinalIgnoreCase))
        {
            return $"/Game/AssetMaterial/ImageS/CharaterS/{characterCode}/{fileName}.{fileName}";
        }

        return string.Empty;
    }

    private static bool SameObjectPath(string left, string right) =>
        string.Equals(NormalizeObjectPath(left), NormalizeObjectPath(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeObjectPath(string value)
    {
        var path = (value ?? string.Empty).Trim().Replace('\\', '/');
        var dotIndex = path.IndexOf('.', StringComparison.Ordinal);
        return dotIndex >= 0 ? path[..dotIndex] : path;
    }

    private static string GetCanonicalFolder(string characterCode, UnrealBridgeSnapshotItem toolboxItem)
    {
        var relative = toolboxItem.ToolboxRelativePath.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(characterCode) || string.IsNullOrWhiteSpace(relative))
        {
            return string.Empty;
        }

        var separator = relative.LastIndexOf('/');
        if (separator < 0)
        {
            return string.Empty;
        }

        var folder = relative[..separator];
        if (toolboxItem.Module == UnrealBridgeModule.Voices &&
            relative.StartsWith("Sound/", StringComparison.OrdinalIgnoreCase))
        {
            return $"/Game/GameActor2D/{characterCode}/{folder}";
        }

        if (toolboxItem.Module == UnrealBridgeModule.BaseMaterials &&
            relative.StartsWith("AssetMaterial/BuffIcon/", StringComparison.OrdinalIgnoreCase))
        {
            return $"/Game/GameActor2D/{characterCode}/BUFF";
        }

        if (toolboxItem.Module == UnrealBridgeModule.BaseMaterials &&
            relative.StartsWith("AssetMaterial/", StringComparison.OrdinalIgnoreCase))
        {
            return $"/Game/AssetMaterial/ImageS/CharaterS/{characterCode}";
        }

        return string.Empty;
    }

    private static bool IsMigrationSafePair(
        UnrealBridgeSnapshotItem toolboxItem,
        UnrealBridgeSnapshotItem unrealItem,
        string characterCode)
    {
        if (toolboxItem.Module is not (UnrealBridgeModule.BaseMaterials or UnrealBridgeModule.Voices) ||
            unrealItem.Module != toolboxItem.Module ||
            string.IsNullOrWhiteSpace(unrealItem.SourceObjectPath))
        {
            return false;
        }

        var expectedFolder = GetCanonicalFolder(characterCode, toolboxItem);
        var expectedName = NormalizeAssetName(GetToolboxAssetName(toolboxItem));
        return !string.IsNullOrWhiteSpace(expectedFolder) &&
            string.Equals(GetObjectFolder(unrealItem.SourceObjectPath), expectedFolder, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                NormalizeAssetName(GetObjectAssetName(unrealItem.SourceObjectPath)),
                expectedName,
                StringComparison.Ordinal);
    }

    private static string GetToolboxAssetName(UnrealBridgeSnapshotItem item) =>
        Path.GetFileNameWithoutExtension(
            string.IsNullOrWhiteSpace(item.ToolboxRelativePath)
                ? item.AssetPath
                : item.ToolboxRelativePath);

    private static string GetObjectFolder(string objectPath)
    {
        var package = NormalizeObjectPath(objectPath);
        var separator = package.LastIndexOf('/');
        return separator > 0 ? package[..separator] : string.Empty;
    }

    private static string GetObjectAssetName(string objectPath)
    {
        var package = NormalizeObjectPath(objectPath);
        var separator = package.LastIndexOf('/');
        return separator >= 0 ? package[(separator + 1)..] : package;
    }

    private static string NormalizeAssetName(string value) =>
        new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static Dictionary<string, UnrealBridgeSnapshotItem> ToItemMap(
        IReadOnlyList<UnrealBridgeSnapshotItem> items,
        string sourceName)
    {
        var result = new Dictionary<string, UnrealBridgeSnapshotItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.StableId))
            {
                throw new InvalidOperationException($"{sourceName}快照包含空的稳定 ID。");
            }

            if (!result.TryAdd(item.StableId, item))
            {
                throw new InvalidOperationException($"{sourceName}快照包含重复稳定 ID：{item.StableId}。");
            }
        }

        return result;
    }

    private static UnrealBridgeChange BuildChange(
        string stableId,
        UnrealBridgeSnapshotItem? toolboxItem,
        UnrealBridgeSnapshotItem? unrealItem,
        UnrealBridgeDirection direction,
        UnrealBridgeSyncState? baseline,
        bool isMigrationSafePair)
    {
        var sourceItem = direction == UnrealBridgeDirection.PublishToUnreal ? toolboxItem : unrealItem;
        var targetItem = direction == UnrealBridgeDirection.PublishToUnreal ? unrealItem : toolboxItem;
        var referenceItem = sourceItem ?? targetItem
            ?? throw new InvalidOperationException($"无法解析差异项：{stableId}。");
        UnrealBridgeSyncStateEntry? stateEntry = null;
        baseline?.Entries.TryGetValue(stableId, out stateEntry);
        var isRename = IsRename(direction, toolboxItem, unrealItem, stateEntry);

        UnrealBridgeChangeKind kind;
        if (sourceItem is not null && targetItem is null)
        {
            kind = UnrealBridgeChangeKind.Added;
        }
        else if (sourceItem is null)
        {
            kind = UnrealBridgeChangeKind.DeleteCandidate;
        }
        else if (stateEntry is not null)
        {
            var baselineSourceHash = direction == UnrealBridgeDirection.PublishToUnreal
                ? stateEntry.ToolboxHash
                : stateEntry.UnrealHash;
            var baselineTargetHash = direction == UnrealBridgeDirection.PublishToUnreal
                ? stateEntry.UnrealHash
                : stateEntry.ToolboxHash;
            var sourceChanged = !string.Equals(sourceItem.ContentHash, baselineSourceHash, StringComparison.OrdinalIgnoreCase);
            var targetChanged = !string.Equals(targetItem!.ContentHash, baselineTargetHash, StringComparison.OrdinalIgnoreCase);
            kind = targetChanged
                ? UnrealBridgeChangeKind.Conflict
                : isRename
                    ? UnrealBridgeChangeKind.Renamed
                    : sourceChanged
                        ? UnrealBridgeChangeKind.Updated
                        : UnrealBridgeChangeKind.Unchanged;
        }
        else if (isMigrationSafePair && baseline is null)
        {
            kind = UnrealBridgeChangeKind.Unchanged;
        }
        else if (string.Equals(sourceItem.ContentHash, targetItem!.ContentHash, StringComparison.OrdinalIgnoreCase))
        {
            kind = isRename ? UnrealBridgeChangeKind.Renamed : UnrealBridgeChangeKind.Unchanged;
        }
        else
        {
            kind = UnrealBridgeChangeKind.Conflict;
        }

        return new UnrealBridgeChange(
            stableId,
            referenceItem.Module,
            referenceItem.DisplayName,
            kind,
            toolboxItem,
            unrealItem,
            kind is UnrealBridgeChangeKind.Added or UnrealBridgeChangeKind.Updated or UnrealBridgeChangeKind.Renamed);
    }

    private static bool IsRename(
        UnrealBridgeDirection direction,
        UnrealBridgeSnapshotItem? toolboxItem,
        UnrealBridgeSnapshotItem? unrealItem,
        UnrealBridgeSyncStateEntry? stateEntry)
    {
        if (direction == UnrealBridgeDirection.PublishToUnreal)
        {
            var toolboxPathChanged = stateEntry is not null &&
                toolboxItem is not null &&
                !string.IsNullOrWhiteSpace(stateEntry.ToolboxRelativePath) &&
                !string.IsNullOrWhiteSpace(toolboxItem.ToolboxRelativePath) &&
                !string.Equals(
                    stateEntry.ToolboxRelativePath,
                    toolboxItem.ToolboxRelativePath,
                    StringComparison.OrdinalIgnoreCase);
            if (toolboxPathChanged)
            {
                return true;
            }

            return toolboxItem is not null &&
                unrealItem is not null &&
                toolboxItem.Module == UnrealBridgeModule.Voices &&
                UnrealBridgeVoicePathPolicy.TryBuildCanonicalObjectPath(
                    unrealItem.SourceObjectPath,
                    toolboxItem.PayloadJson,
                    toolboxItem.NormalizedName,
                    out var canonicalPath) &&
                !string.Equals(canonicalPath, unrealItem.SourceObjectPath, StringComparison.OrdinalIgnoreCase);
        }

        return stateEntry is not null &&
            unrealItem is not null &&
            !string.IsNullOrWhiteSpace(stateEntry.UnrealObjectPath) &&
            !string.IsNullOrWhiteSpace(unrealItem.SourceObjectPath) &&
            !string.Equals(
                stateEntry.UnrealObjectPath,
                unrealItem.SourceObjectPath,
                StringComparison.OrdinalIgnoreCase);
    }
}
