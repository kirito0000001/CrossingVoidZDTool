using System.IO;

namespace CrossingVoidZDTool.Services;

internal static class UnrealBridgePublishSupportPolicy
{
    public static bool CanExecute(UnrealBridgeChange change)
    {
        if (change.Kind is UnrealBridgeChangeKind.Unchanged or UnrealBridgeChangeKind.Conflict)
        {
            return false;
        }

        if (change.Module is not (UnrealBridgeModule.BaseMaterials or UnrealBridgeModule.SequenceFrames or UnrealBridgeModule.Voices))
        {
            return false;
        }

        if (change.Kind == UnrealBridgeChangeKind.DeleteCandidate)
        {
            return change.Module == UnrealBridgeModule.SequenceFrames &&
                !string.IsNullOrWhiteSpace(change.UnrealItem?.SourceObjectPath);
        }

        if (change.Kind == UnrealBridgeChangeKind.Added)
        {
            return change.ToolboxItem is { AssetPath: var newAssetPath } &&
                !string.IsNullOrWhiteSpace(newAssetPath) &&
                File.Exists(newAssetPath) &&
                change.Module is UnrealBridgeModule.BaseMaterials or UnrealBridgeModule.SequenceFrames or UnrealBridgeModule.Voices;
        }

        return change.ToolboxItem is { AssetPath: var assetPath } &&
            !string.IsNullOrWhiteSpace(assetPath) &&
            File.Exists(assetPath) &&
            !string.IsNullOrWhiteSpace(change.UnrealItem?.SourceObjectPath);
    }
}
