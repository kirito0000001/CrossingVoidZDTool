using System;
using System.Collections.Generic;
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
        return toolboxItems.Keys
            .Union(unrealItems.Keys, StringComparer.OrdinalIgnoreCase)
            .Select(stableId => BuildChange(
                 stableId,
                 toolboxItems.GetValueOrDefault(stableId),
                 unrealItems.GetValueOrDefault(stableId),
                 direction,
                baseline))
            .OrderBy(change => change.Module)
            .ThenBy(change => change.StableId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

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
        UnrealBridgeSyncState? baseline)
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
