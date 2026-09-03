using System;
using System.Linq;

namespace CrossingVoidZDTool.Services;

internal sealed class UnrealBridgeVerificationService
{
    public UnrealBridgeSyncState BuildVerifiedState(
        UnrealBridgeExecutionPlan plan,
        UnrealBridgeExecutionResult result,
        UnrealBridgeSnapshot toolbox,
        UnrealBridgeSnapshot unreal)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(result);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"虚幻同步执行失败，不能写入成功状态：{result.ErrorMessage}");
        }

        var results = result.Items.ToDictionary(item => item.StableId, StringComparer.OrdinalIgnoreCase);
        var incomplete = plan.Operations
            .Where(operation =>
                !results.TryGetValue(operation.StableId, out var itemResult) ||
                !itemResult.Succeeded)
            .Select(operation => operation.StableId)
            .ToArray();
        if (incomplete.Length > 0)
        {
            throw new InvalidOperationException($"以下同步项没有成功结果：{string.Join("、", incomplete)}。");
        }

        var toolboxItems = toolbox.Items.ToDictionary(item => item.StableId, StringComparer.OrdinalIgnoreCase);
        var unrealItems = unreal.Items.ToDictionary(item => item.StableId, StringComparer.OrdinalIgnoreCase);
        var state = new UnrealBridgeSyncState
        {
            CharacterCode = plan.CharacterCode,
            UnrealProjectPath = plan.UnrealProjectPath,
            TemplateCharacterCode = plan.TemplateCharacterCode,
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

        var missingVerifiedItems = plan.Operations
            .Where(operation => operation.Kind != UnrealBridgeOperationKind.Delete)
            .Where(operation => !state.Entries.ContainsKey(operation.StableId))
            .Select(operation => operation.StableId)
            .ToArray();
        if (missingVerifiedItems.Length > 0)
        {
            throw new InvalidOperationException(
                $"执行后重新扫描没有找到以下同步项：{string.Join("、", missingVerifiedItems)}。");
        }

        return state;
    }
}
