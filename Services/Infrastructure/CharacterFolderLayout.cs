namespace CrossingVoidZDTool.Services;

/// <summary>
/// 角色工作区的目录布局。
///
/// 这些名字原本在五个服务里各自 <c>private const</c> 了一份，改一处目录名要翻五个文件，
/// 而且已经出现过拼写不一致（<c>ZDMaterialFolderName</c> 与 <c>ZdMaterialFolderName</c>）。
/// 它们是磁盘上的既有事实，不能随便改——集中放在这里，是为了让「改一处目录名」
/// 变成一次可见的决定，而不是散在各处的巧合。
///
/// 目录形状（<c>&lt;工作区&gt;/{Draft|Completed}/&lt;角色代号&gt;/</c> 之下）：
/// <code>
/// tool/            工具箱自己的数据：character.json、ZDToolboxData.json、ReferenceImages/
/// AssetMaterial/   基础图片
/// ZDMaterial/      序列帧（每个动作一个子目录，内含 Frames/ 和 sequence.json）
/// Sound/           语音，按分类分子目录
/// ExAsset/         附加资产
/// BUFF/            BUFF 图标与数据
/// </code>
/// </summary>
internal static class CharacterFolderLayout
{
    public const string Tool = "tool";
    public const string AssetMaterial = "AssetMaterial";
    public const string ZdMaterial = "ZDMaterial";
    public const string Sound = "Sound";
    public const string ExAsset = "ExAsset";
    public const string Buff = "BUFF";
    public const string ReferenceImages = "ReferenceImages";
    public const string Frames = "Frames";

    /// <summary>工作区根下的三个状态目录。</summary>
    public const string Draft = "Draft";
    public const string Completed = "Completed";
    public const string Export = "Export";

    /// <summary>角色目录下必须存在的素材子目录。</summary>
    public static readonly string[] RequiredSubFolders =
        [Tool, AssetMaterial, ZdMaterial, Sound, ExAsset, Buff];
}
