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
