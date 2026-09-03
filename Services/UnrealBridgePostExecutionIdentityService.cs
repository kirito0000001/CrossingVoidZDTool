using System;
using System.Linq;

namespace CrossingVoidZDTool.Services;

internal sealed class UnrealBridgePostExecutionIdentityService
{
    public UnrealBridgeSnapshot Restore(
        UnrealBridgeSnapshot rescanned,
        UnrealBridgeExecutionResult result)
    {
        var successfulPaths = result.Items
            .Where(item => item.Succeeded && !string.IsNullOrWhiteSpace(item.ObjectPath))
            .GroupBy(item => Normalize(item.ObjectPath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().StableId, StringComparer.OrdinalIgnoreCase);
        return new UnrealBridgeSnapshot(
            rescanned.CharacterCode,
            rescanned.Items.Select(item =>
                successfulPaths.TryGetValue(Normalize(item.SourceObjectPath), out var stableId)
                    ? item with { StableId = stableId }
                    : item).ToArray());
    }

    private static string Normalize(string value) => value.Trim().Replace('\\', '/');
}
