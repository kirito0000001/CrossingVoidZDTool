using System;
using System.Linq;

namespace CrossingVoidZDTool.Services;

internal sealed class UnrealBridgeVerificationService
{
    public UnrealBridgeSyncState BuildVerifiedState(
        UnrealBridgeExecutionPlan plan,
        UnrealBridgeExecutionResult result,
        UnrealBridgeSnapshot toolbox,
        UnrealBridgeSnapshot unreal,
        UnrealBridgeSyncState? previousState = null)
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
        var verifiedStableIds = plan.Operations
            .Where(operation => operation.Kind is not (UnrealBridgeOperationKind.Delete or UnrealBridgeOperationKind.Consolidate))
            .Select(operation => operation.StableId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var state = new UnrealBridgeSyncState
        {
            HashScheme = UnrealBridgeSyncState.SourceFileHashScheme,
            CharacterCode = plan.CharacterCode,
            UnrealProjectPath = plan.UnrealProjectPath,
            TemplateCharacterCode = string.IsNullOrWhiteSpace(plan.TemplateCharacterCode)
                ? previousState?.TemplateCharacterCode ?? string.Empty
                : plan.TemplateCharacterCode,
            LastVerifiedAt = DateTimeOffset.Now
        };
        if (previousState is not null)
        {
            foreach (var entry in previousState.Entries)
            {
                state.Entries[entry.Key] = entry.Value;
            }
        }

        foreach (var stableId in verifiedStableIds)
        {
            if (!toolboxItems.TryGetValue(stableId, out var toolboxItem) ||
                !unrealItems.TryGetValue(stableId, out var unrealItem))
            {
                continue;
            }

            state.Entries[stableId] = new UnrealBridgeSyncStateEntry(
                toolboxItem.ContentHash,
                unrealItem.ContentHash,
                unrealItem.SourceObjectPath,
                unrealItem.OriginIdentity,
                toolboxItem.ToolboxRelativePath,
                toolboxItem.NormalizedName);
        }

        foreach (var operation in plan.Operations.Where(operation => operation.Kind == UnrealBridgeOperationKind.Delete))
        {
            state.Entries.Remove(operation.StableId);
        }

        var missingVerifiedItems = plan.Operations
            .Where(operation => operation.Kind is not (UnrealBridgeOperationKind.Delete or UnrealBridgeOperationKind.Consolidate))
            .Where(operation => !toolboxItems.ContainsKey(operation.StableId) || !unrealItems.ContainsKey(operation.StableId))
            .Select(operation => operation.StableId)
            .ToArray();
        if (missingVerifiedItems.Length > 0)
        {
            throw new InvalidOperationException(
                $"执行后重新扫描没有找到以下同步项：{string.Join("、", missingVerifiedItems)}。");
        }

        var failedConsolidations = plan.Operations
            .Where(operation => operation.Kind == UnrealBridgeOperationKind.Consolidate)
            .Where(operation =>
                unreal.Items.Any(item => SameObjectPath(item.SourceObjectPath, operation.SourceObjectPath)) ||
                !unreal.Items.Any(item => SameObjectPath(item.SourceObjectPath, operation.TargetObjectPath)))
            .Select(operation => operation.DisplayName)
            .ToArray();
        if (failedConsolidations.Length > 0)
        {
            throw new InvalidOperationException(
                $"以下素材重定向复扫验证失败：{string.Join("、", failedConsolidations)}。");
        }

        var failedDeletes = plan.Operations
            .Where(operation => operation.Kind == UnrealBridgeOperationKind.Delete)
            .Where(operation => unreal.Items.Any(item => SameObjectPath(item.SourceObjectPath, operation.SourceObjectPath)))
            .Select(operation => operation.DisplayName)
            .ToArray();
        if (failedDeletes.Length > 0)
        {
            throw new InvalidOperationException(
                $"以下素材删除后仍可被复扫发现：{string.Join("、", failedDeletes)}。");
        }

        return state;
    }

    private static bool SameObjectPath(string left, string right) =>
        string.Equals(NormalizeObjectPath(left), NormalizeObjectPath(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeObjectPath(string value)
    {
        var path = (value ?? string.Empty).Trim().Replace('\\', '/');
        var dotIndex = path.IndexOf('.', StringComparison.Ordinal);
        return dotIndex >= 0 ? path[..dotIndex] : path;
    }
}
