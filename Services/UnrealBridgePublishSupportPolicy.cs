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
            // 语音以前不许删，于是 Unreal 侧多出来的语音只能一直挂在差异列表里，
            // 第三步的差异永远归不了零。两侧素材本来就该一一对应，放开。
            // 图片（BaseMaterials）暂时仍不放开：它可能被工具箱不知道的资产引用着，
            // 误删的代价比语音高，等确认后再单独放。
            if (change.Module is not (UnrealBridgeModule.SequenceFrames or UnrealBridgeModule.Voices))
            {
                return false;
            }

            if (change.Module == UnrealBridgeModule.Voices)
            {
                // 得知道删哪一个才敢删
                return !string.IsNullOrWhiteSpace(change.UnrealItem?.SourceObjectPath);
            }

            // 空白帧在 Unreal 里没有对应资产（Flipbook 里 sprite 为 null 的关键帧），
            // 它的"删除"靠同步时重建 Flipbook 完成，本来就没有对象路径可言。
            // 以前一律要求 SourceObjectPath 非空，于是含空白帧的动作一旦被勾选，
            // 整批同步就卡在"包含尚未完成重定向的同步项"，而且当时日志里毫无线索。
            if (SequenceFrameIdentity.IsBlankFramePayload(change.UnrealItem?.PayloadJson))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(change.UnrealItem?.SourceObjectPath);
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
