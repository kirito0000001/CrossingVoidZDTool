using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CrossingVoidZDTool.Services;

internal sealed class UnrealBridgeBaselineService
{
    public UnrealBridgeSyncState BuildFromChanges(
        string characterCode,
        string unrealProjectPath,
        IReadOnlyList<UnrealBridgeChange> changes,
        string templateCharacterCode = "")
    {
        var state = new UnrealBridgeSyncState
        {
            HashScheme = UnrealBridgeSyncState.SourceFileHashScheme,
            CharacterCode = characterCode.Trim(),
            UnrealProjectPath = string.IsNullOrWhiteSpace(unrealProjectPath)
                ? string.Empty
                : Path.GetFullPath(unrealProjectPath),
            TemplateCharacterCode = templateCharacterCode.Trim(),
            LastVerifiedAt = DateTimeOffset.Now
        };

        foreach (var change in changes.Where(change => change.ToolboxItem is not null && change.UnrealItem is not null))
        {
            var toolboxItem = change.ToolboxItem!;
            var unrealItem = change.UnrealItem!;
            state.Entries[change.StableId] = new UnrealBridgeSyncStateEntry(
                toolboxItem.ContentHash,
                unrealItem.ContentHash,
                unrealItem.SourceObjectPath,
                unrealItem.OriginIdentity,
                toolboxItem.ToolboxRelativePath,
                toolboxItem.NormalizedName);
        }

        return state;
    }

    /// <summary>
    /// 第五步序列同步成功后的增量基线提交。
    /// 只替换本次执行过的动作：这些动作下的旧条目整体移除，再写入复扫后仍然两侧配对的条目，
    /// 这样删掉的帧不会留下孤儿基线；未执行的动作保留原有基线。
    /// </summary>
    public UnrealBridgeSyncState MergeVerifiedSequenceState(
        UnrealBridgeSyncState? previousState,
        string characterCode,
        string unrealProjectPath,
        IReadOnlySet<string> executedActionStableIds,
        IReadOnlyList<UnrealBridgeChange> rescannedChanges)
    {
        ArgumentNullException.ThrowIfNull(executedActionStableIds);
        ArgumentNullException.ThrowIfNull(rescannedChanges);
        var state = new UnrealBridgeSyncState
        {
            HashScheme = UnrealBridgeSyncState.SourceFileHashScheme,
            CharacterCode = characterCode.Trim(),
            UnrealProjectPath = string.IsNullOrWhiteSpace(unrealProjectPath)
                ? string.Empty
                : Path.GetFullPath(unrealProjectPath),
            TemplateCharacterCode = previousState?.TemplateCharacterCode ?? string.Empty,
            LastVerifiedAt = DateTimeOffset.Now
        };
        if (previousState is not null)
        {
            foreach (var entry in previousState.Entries
                .Where(entry => !executedActionStableIds.Contains(GetOwningActionStableId(entry.Key))))
            {
                state.Entries[entry.Key] = entry.Value;
            }
        }

        foreach (var change in rescannedChanges)
        {
            if (change.Module != UnrealBridgeModule.SequenceFrames ||
                change.ToolboxItem is null ||
                change.UnrealItem is null ||
                !executedActionStableIds.Contains(GetOwningActionStableId(change.StableId)))
            {
                continue;
            }

            state.Entries[change.StableId] = new UnrealBridgeSyncStateEntry(
                change.ToolboxItem.ContentHash,
                change.UnrealItem.ContentHash,
                change.UnrealItem.SourceObjectPath,
                change.UnrealItem.OriginIdentity,
                change.ToolboxItem.ToolboxRelativePath,
                change.ToolboxItem.NormalizedName);
        }

        return state;
    }

    /// <summary>
    /// 去掉指定动作下的基线条目，得到用于「执行后复扫」的比较基线。
    /// 执行前的基线记的是旧的 Unreal 哈希，而这些资产刚被我们自己改写过；
    /// 继续拿它比较会让刚同步成功的帧全部判成冲突（targetChanged 恒为真），
    /// 于是"同步成功却报仍有差异"，这批帧的基线条目也会跟着丢失。
    /// 剔除之后它们会走「已在规范位置即视为已同步」的判定，复扫结果才是对的。
    /// </summary>
    public UnrealBridgeSyncState? WithoutActions(
        UnrealBridgeSyncState? state,
        IReadOnlySet<string> actionStableIds)
    {
        ArgumentNullException.ThrowIfNull(actionStableIds);
        if (state is null || actionStableIds.Count == 0)
        {
            return state;
        }

        var stripped = new UnrealBridgeSyncState
        {
            HashScheme = state.HashScheme,
            CharacterCode = state.CharacterCode,
            UnrealProjectPath = state.UnrealProjectPath,
            TemplateCharacterCode = state.TemplateCharacterCode,
            LastVerifiedAt = state.LastVerifiedAt
        };
        foreach (var entry in state.Entries
            .Where(entry => !actionStableIds.Contains(GetOwningActionStableId(entry.Key))))
        {
            stripped.Entries[entry.Key] = entry.Value;
        }

        return stripped;
    }

    /// <summary>序列条目所属的动作稳定 ID；非序列条目返回空串，不会被动作范围匹配到。</summary>
    private static string GetOwningActionStableId(string stableId)
    {
        if (SequenceFrameIdentity.IsActionStableId(stableId))
        {
            return stableId;
        }

        if (!SequenceFrameIdentity.IsFrameStableId(stableId))
        {
            return string.Empty;
        }

        var separator = stableId.LastIndexOf(':');
        return separator > SequenceFrameIdentity.FramePrefix.Length
            ? SequenceFrameIdentity.ActionPrefix + stableId[SequenceFrameIdentity.FramePrefix.Length..separator]
            : string.Empty;
    }

    public UnrealBridgeSyncState Build(
        string characterCode,
        string unrealProjectPath,
        UnrealBridgeSnapshot toolbox,
        UnrealBridgeSnapshot unreal,
        string templateCharacterCode = "")
    {
        var toolboxItems = toolbox.Items.ToDictionary(item => item.StableId, StringComparer.OrdinalIgnoreCase);
        var unrealItems = unreal.Items.ToDictionary(item => item.StableId, StringComparer.OrdinalIgnoreCase);
        var state = new UnrealBridgeSyncState
        {
            HashScheme = UnrealBridgeSyncState.SourceFileHashScheme,
            CharacterCode = characterCode.Trim(),
            UnrealProjectPath = string.IsNullOrWhiteSpace(unrealProjectPath)
                ? string.Empty
                : Path.GetFullPath(unrealProjectPath),
            TemplateCharacterCode = templateCharacterCode.Trim(),
            LastVerifiedAt = DateTimeOffset.Now
        };
        foreach (var stableId in toolboxItems.Keys.Intersect(unrealItems.Keys, StringComparer.OrdinalIgnoreCase))
        {
            var toolboxItem = toolboxItems[stableId];
            var unrealItem = unrealItems[stableId];
            state.Entries[stableId] = new UnrealBridgeSyncStateEntry(
                toolboxItem.ContentHash,
                unrealItem.ContentHash,
                unrealItem.SourceObjectPath,
                unrealItem.OriginIdentity,
                toolboxItem.ToolboxRelativePath,
                toolboxItem.NormalizedName);
        }

        return state;
    }

    public UnrealBridgeSyncState BuildSelected(
        string characterCode,
        string unrealProjectPath,
        UnrealBridgeSnapshot toolbox,
        UnrealBridgeSnapshot unreal,
        IReadOnlySet<string> selectedStableIds,
        UnrealBridgeSyncState? previousState = null,
        string templateCharacterCode = "")
    {
        ArgumentNullException.ThrowIfNull(selectedStableIds);
        var completeState = Build(
            characterCode,
            unrealProjectPath,
            toolbox,
            unreal,
            string.IsNullOrWhiteSpace(templateCharacterCode)
                ? previousState?.TemplateCharacterCode ?? string.Empty
                : templateCharacterCode);
        var selectedScope = selectedStableIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unrealItems = unreal.Items.ToDictionary(item => item.StableId, StringComparer.OrdinalIgnoreCase);
        var added = true;
        while (added)
        {
            added = false;
            foreach (var item in unreal.Items.Where(item =>
                         !string.IsNullOrWhiteSpace(item.ParentStableId) &&
                         selectedScope.Contains(item.ParentStableId)))
            {
                added |= selectedScope.Add(item.StableId);
            }
        }

        var state = new UnrealBridgeSyncState
        {
            HashScheme = UnrealBridgeSyncState.SourceFileHashScheme,
            CharacterCode = completeState.CharacterCode,
            UnrealProjectPath = completeState.UnrealProjectPath,
            TemplateCharacterCode = completeState.TemplateCharacterCode,
            LastVerifiedAt = completeState.LastVerifiedAt
        };
        if (previousState is not null)
        {
            foreach (var entry in previousState.Entries)
            {
                state.Entries[entry.Key] = entry.Value;
            }
        }

        foreach (var stableId in selectedScope)
        {
            if (completeState.Entries.TryGetValue(stableId, out var entry))
            {
                state.Entries[stableId] = entry;
            }
            else if (unrealItems.ContainsKey(stableId))
            {
                state.Entries.Remove(stableId);
            }
        }

        return state;
    }
}
