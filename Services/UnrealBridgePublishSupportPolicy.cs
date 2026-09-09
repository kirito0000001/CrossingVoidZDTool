using System;
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
            // 以前只有序列帧能删，于是 Unreal 侧多出来的语音和图片只能一直挂在
            // 差异列表里，第三步的差异永远归不了零。两侧素材本来就该一一对应。
            if (change.Module is not (UnrealBridgeModule.SequenceFrames
                or UnrealBridgeModule.Voices
                or UnrealBridgeModule.BaseMaterials))
            {
                return false;
            }

            if (change.Module is UnrealBridgeModule.Voices or UnrealBridgeModule.BaseMaterials)
            {
                // 得知道删哪一个才敢删
                if (string.IsNullOrWhiteSpace(change.UnrealItem?.SourceObjectPath))
                {
                    return false;
                }

                // 「其他图片」是有意停在那儿的东西（还没归类、或压根不归工具箱管），
                // 不能因为工具箱这边没有同名文件就当成多余资产删掉。
                // 归了类却对不上的，才是真该清理的不合格素材。
                return !IsUnclassifiedImage(change);
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

    /// <summary>
    /// 这条 Unreal 侧素材是不是「其他图片」。
    ///
    /// 语义快照把素材分类塞在 PayloadJson 的第一段（分隔符 0x1F），
    /// 取值就是 BaseMaterialKind 的名字。这里只认 OtherImage 这一档——
    /// 读不出来时按「不是其他图片」处理会让它变成可删，风险太大，所以反过来：
    /// 拿不准就当成其他图片，宁可留着。
    /// </summary>
    private static bool IsUnclassifiedImage(UnrealBridgeChange change)
    {
        if (change.Module != UnrealBridgeModule.BaseMaterials)
        {
            return false;
        }

        var payload = change.UnrealItem?.PayloadJson ?? string.Empty;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return true;
        }

        var kind = payload.Split('\u001f', 2)[0].Trim();
        return string.IsNullOrEmpty(kind)
            || string.Equals(kind, nameof(BaseMaterialKind.OtherImage), StringComparison.OrdinalIgnoreCase);
    }
}
