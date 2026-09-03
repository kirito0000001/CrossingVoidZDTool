using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CrossingVoidZDTool;

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
