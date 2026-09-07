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
            // 空白帧没有源文件，但它在 Flipbook 里要占一个关键帧，属于可执行内容。
            // 按"必须存在源文件"判会让它变成待重定向项，而第五步没有规整工作区，
            // 这个条件永远满足不了，含空白帧的动作就整批同步不了。
            if (change.Module == UnrealBridgeModule.SequenceFrames)
            {
                return change.ToolboxItem is { AssetPath: var framePath } &&
                    (string.IsNullOrWhiteSpace(framePath) || File.Exists(framePath));
            }

            return change.ToolboxItem is { AssetPath: var newAssetPath } &&
                !string.IsNullOrWhiteSpace(newAssetPath) &&
                File.Exists(newAssetPath) &&
                change.Module is UnrealBridgeModule.BaseMaterials or UnrealBridgeModule.Voices;
        }

        return change.ToolboxItem is { AssetPath: var assetPath } &&
            !string.IsNullOrWhiteSpace(assetPath) &&
            File.Exists(assetPath) &&
            !string.IsNullOrWhiteSpace(change.UnrealItem?.SourceObjectPath);
    }
}
