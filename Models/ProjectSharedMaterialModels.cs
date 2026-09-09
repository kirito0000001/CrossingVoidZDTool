using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CrossingVoidZDTool;

/// <summary>
/// 共享素材的分类。会落进 <c>Shared/tool/shared-materials.json</c>。
///
/// 以前是按整数落盘的，往中间插一个成员就会让已有索引里所有靠后的分类整体错位一格——
/// BuffIcons 变成 EventImages，图标跑到事件图目录里去，而且不报错。
/// 改成按名字落盘之后，成员顺序就不再是磁盘契约了，插哪儿都行。
///
/// 旧索引照样读得回来：JsonStringEnumConverter 默认 allowIntegerValues=true，
/// 整数写法会按当前顺序解回同一个成员，所以这次改动不需要迁移，也不会让老文件失效。
/// （反过来不成立：新写出的字符串文件给旧版本工具读会抛 JsonException。）
/// 用泛型的 JsonStringEnumConverter&lt;T&gt; 而不是非泛型版本，是因为这个类型走源生成 + 裁剪。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProjectSharedMaterialCategory>))]
internal enum ProjectSharedMaterialCategory
{
    BuffIcons,
    EventImages,
    BattleUiImages,
    UnclassifiedImages,
    BattleEffects,
    BattleMusic,
    UiEffects,
    UnclassifiedAudio
}

internal sealed record ProjectSharedMaterialUsage(
    string CharacterCode,
    string ActionCode,
    int FrameIndex);

internal sealed class ProjectSharedMaterialItem
{
    public string Id { get; set; } = string.Empty;

    public ProjectSharedMaterialCategory Category { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string AssetClass { get; set; } = string.Empty;

    public string RelativePath { get; set; } = string.Empty;

    public string ContentHash { get; set; } = string.Empty;

    public List<string> SourceObjectPaths { get; set; } = [];

    public List<ProjectSharedMaterialUsage> Usages { get; set; } = [];

    [JsonIgnore]
    public string FilePath { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsReferenceOnly => string.IsNullOrWhiteSpace(RelativePath);
}

internal sealed class ProjectSharedMaterialIndex
{
    public int ProtocolVersion { get; set; } = 1;

    public List<ProjectSharedMaterialItem> Items { get; set; } = [];
}
