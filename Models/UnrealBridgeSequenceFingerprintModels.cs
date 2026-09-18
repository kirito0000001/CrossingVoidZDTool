using System;
using System.Collections.Generic;

namespace CrossingVoidZDTool;

/// <summary>
/// 「上次同步成功时，每条序列的素材长什么样」——用来补上**素材内容级**的校验。
///
/// 现在的检测看的是布局、精灵对应、帧位结构，同名图片被换掉内容是看不出来的
/// （两侧的帧哈希天生不可比：工具箱哈希源 PNG + JSON 载荷，Unreal 哈希图集 PNG + 分隔符载荷）。
/// 所以同步成功后把「这条序列用到的每一张源图的哈希」记下来，
/// 下次检测拿当前值和记录值比 —— 对不上就是「素材内容变了，需要重做」。
///
/// 存在角色自己的目录里（<c>tool/UnrealSync/sequence-content.json</c>），
/// 和会话缓存、同步基线放在一起：都是「这个角色这一版长什么样」的记录。
/// </summary>
internal sealed class UnrealBridgeSequenceContentFingerprints
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string CharacterCode { get; set; } = string.Empty;

    public DateTimeOffset RecordedAt { get; set; }

    /// <summary>动作稳定 ID（<c>sequence:sk2</c>）→ 那条序列的素材指纹。</summary>
    public Dictionary<string, UnrealBridgeSequenceActionFingerprint> Actions { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class UnrealBridgeSequenceActionFingerprint
{
    /// <summary>
    /// 这条序列用到的**全部**源图（自己的 + 借来的）按文件名排序后的内容摘要。
    ///
    /// 放进动作载荷里参与比对：和记录值不一致 = 素材内容变了（或者换了图），需要重建。
    /// </summary>
    public string ContentDigest { get; set; } = string.Empty;

    /// <summary>源图 → 内容哈希。整图换内容但文件名不变时靠它认出来。</summary>
    public Dictionary<string, string> SourceHashes { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}
