using System.Collections.Generic;
using System;
using System.Linq;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CrossingVoidZDTool;

internal enum UnrealProjectSyncExportScope
{
    Full,
    Normalization,
    CharacterMaterials,
    CharacterSequences
}

internal sealed record UnrealProjectSyncCheckItem(
    string Title,
    string Path,
    bool Exists,
    string Message)
{
    public string StatusText => Exists ? "通过" : "缺失";
}

internal sealed record UnrealPublishFoundationCheckItem(
    string DisplayName,
    string ExpectedPath,
    string ActualPath,
    bool IsCompliant,
    string Problem = "",
    string ExpectedType = "",
    string ActualType = "")
{
    public string StatusText => IsCompliant ? "合规" : string.IsNullOrWhiteSpace(Problem) ? "不合规" : Problem;
    public string CorrectName
    {
        get
        {
            var leaf = ExpectedPath.TrimEnd('/').Split('/').LastOrDefault() ?? string.Empty;
            var dotIndex = leaf.IndexOf('.', StringComparison.Ordinal);
            return dotIndex > 0 ? leaf[..dotIndex] : leaf;
        }
    }
    public string ExpectedSummaryText => string.IsNullOrWhiteSpace(ExpectedType)
        ? ExpectedPath
        : $"{ExpectedPath} · 类型 {ExpectedType}";
    public string DetailText => IsCompliant
        ? string.IsNullOrWhiteSpace(ExpectedType) ? "目录已经存在。" : $"资产类型：{ActualType}"
        : string.IsNullOrWhiteSpace(ActualPath)
            ? "未找到对应目录或资产。"
            : $"当前位于：{ActualPath}" +
              (string.IsNullOrWhiteSpace(ActualType) ? string.Empty : $"；类型：{ActualType}");
}

internal sealed record UnrealProjectSyncCheckResult(
    InfoBarSeverity Severity,
    string Title,
    string Message,
    bool CanSync,
    string UnrealContentPath,
    string TargetBaseMaterialContentPath,
    string TargetZdContentPath,
    string TargetCharacterItemContentPath,
    string LinkSkillLibraryObjectPath,
    string TargetBaseMaterialDiskPath,
    string TargetZdDiskPath,
    string TargetCharacterItemDiskPath,
    string LinkSkillLibraryDiskPath,
    string ExportDirectoryPath,
    string ExportScriptPath,
    string ExportManifestPath,
    bool ExportManifestExists,
    int ExportedAssetCount,
    DateTime? ExportGeneratedAt,
    IReadOnlyList<UnrealProjectSyncExportAssetView> ExportPreviewAssets,
    IReadOnlyList<UnrealProjectSyncCharacterCandidate> CharacterCandidates,
    IReadOnlyList<UnrealProjectSyncCheckItem> Items);

internal sealed record UnrealProjectSyncExportAssetView(
    string AssetName,
    string AssetClass,
    string PackagePath,
    string ObjectPath,
    string SourceRoot,
    string ExportedFilePath)
{
    public bool IsBlank => string.Equals(AssetClass, "BlankFrame", StringComparison.OrdinalIgnoreCase);

    public bool HasPreview => !string.IsNullOrWhiteSpace(ExportedFilePath) && System.IO.File.Exists(ExportedFilePath);

    public Microsoft.UI.Xaml.Visibility BlankVisibility => IsBlank
        ? Microsoft.UI.Xaml.Visibility.Visible
        : Microsoft.UI.Xaml.Visibility.Collapsed;

    public string FileUri
    {
        get
        {
            if (!HasPreview)
            {
                return string.Empty;
            }

            var info = new System.IO.FileInfo(ExportedFilePath);
            var version = Math.Max(info.LastWriteTimeUtc.Ticks, info.Length);
            return $"{new Uri(info.FullName).AbsoluteUri}?v={version}";
        }
    }

    public string PreviewStatusText => HasPreview ? "已导出预览图" : "未导出预览图";
}

internal sealed class UnrealProjectSyncCharacterCandidate : CrossingVoidZDTool.ViewModels.ObservableObject
{
    private bool _isSelected;

    public UnrealProjectSyncCharacterCandidate(
        string code,
        string displayName,
        string baseMaterialPath,
        string zdPath,
        int baseMaterialAssetCount,
        int zdAssetCount,
        UnrealProjectSyncCharacterInfoPreview characterInfo,
        UnrealProjectSyncSkillsPreview skillsPreview,
        UnrealProjectSyncSequenceFramesPreview sequenceFramesPreview,
        UnrealProjectSyncBuffsPreview buffsPreview,
        IReadOnlyList<UnrealProjectSyncMaterialBucket> materialBuckets,
        bool hasLatestData = false,
        IReadOnlyList<UnrealProjectSyncVoiceBucket>? voiceBuckets = null)
    {
        Code = code;
        DisplayName = displayName;
        BaseMaterialPath = baseMaterialPath;
        ZdPath = zdPath;
        BaseMaterialAssetCount = baseMaterialAssetCount;
        ZdAssetCount = zdAssetCount;
        CharacterInfo = characterInfo;
        SkillsPreview = skillsPreview;
        SequenceFramesPreview = sequenceFramesPreview;
        BuffsPreview = buffsPreview;
        MaterialBuckets = materialBuckets;
        VoiceBuckets = voiceBuckets ?? [];
        HasLatestData = hasLatestData;
    }

    public string Code { get; }

    public string DisplayName { get; }

    public string BaseMaterialPath { get; }

    public string ZdPath { get; }

    public int BaseMaterialAssetCount { get; }

    public int ZdAssetCount { get; }

    public UnrealProjectSyncCharacterInfoPreview CharacterInfo { get; }

    public UnrealProjectSyncSkillsPreview SkillsPreview { get; }

    public UnrealProjectSyncSequenceFramesPreview SequenceFramesPreview { get; }

    public UnrealProjectSyncBuffsPreview BuffsPreview { get; }

    public IReadOnlyList<UnrealProjectSyncMaterialBucket> MaterialBuckets { get; }

    public IReadOnlyList<UnrealProjectSyncVoiceBucket> VoiceBuckets { get; }

    public bool HasLatestData { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string StatusText => $"基础素材 {BaseMaterialAssetCount} / ZD {ZdAssetCount}";

    public string BaseMaterialCountText => $"基础素材 {BaseMaterialAssetCount}";

    public string ZdMaterialCountText => $"ZD素材 {ZdAssetCount}";

    public string DataFreshnessText => HasLatestData ? "详情已更新" : "不是最新数据";

    public Visibility StaleDataVisibility => HasLatestData ? Visibility.Collapsed : Visibility.Visible;
}

internal sealed record UnrealProjectSyncSkillsPreview(
    bool HasActorData,
    string ActorSourceText,
    string ActorReadStatusText,
    string LinkLibraryStatusText,
    IReadOnlyList<UnrealProjectSyncSkillSlotPreview> CoreSlots,
    UnrealProjectSyncSkillSlotPreview SupportSkillSlot,
    IReadOnlyList<UnrealProjectSyncLinkSkillPreview> LinkSkills)
{
    public string SummaryText => HasActorData
        ? $"主技能 {CoreSlots.Count(slot => slot.HasReadableData)} / {CoreSlots.Count}，护援技 {SupportSkillSlot.Stages.Count}，连携技 {LinkSkills.Count}"
        : "未读取到角色蓝图技能数据";

    public UnrealProjectSyncSkillSlotPreview? FirstCoreSlot => CoreSlots.Count > 0 ? CoreSlots[0] : null;

    public UnrealProjectSyncSkillSlotPreview? SecondCoreSlot => CoreSlots.Count > 1 ? CoreSlots[1] : null;

    public UnrealProjectSyncSkillSlotPreview? UltimateCoreSlot => CoreSlots.Count > 2 ? CoreSlots[2] : null;
}

internal sealed record UnrealProjectSyncSkillSlotPreview(
    string SlotKey,
    string DisplayName,
    bool IsDynamic,
    bool HasReadableData,
    string StatusText,
    IReadOnlyList<UnrealProjectSyncSkillStagePreview> Stages)
{
    public string CountText => IsDynamic
        ? HasReadableData
            ? $"{Stages.Count} 个形态"
            : StatusText
        : HasReadableData
            ? $"{Stages.Count} 个形态"
            : StatusText;
}

internal sealed record UnrealProjectSyncSkillStagePreview(
    int StageNumber,
    string PositionName,
    string TrueName,
    string Description,
    string PtCost,
    string AttackCapacity,
    string AutoPriority,
    string SkillState,
    string GuardState,
    string GuardValue,
    string IconObjectPath,
    string IconAssetName,
    string IconExportedFilePath,
    IReadOnlyList<UnrealProjectSyncSkillMultiplierPreview> Multipliers)
{
    public string Title => string.IsNullOrWhiteSpace(PositionName)
        ? $"形态 {StageNumber}"
        : $"形态 {StageNumber} / {PositionName}";

    public string DetailText =>
        $"真名 {TextOrEmpty(TrueName)} / Pt {TextOrEmpty(PtCost)} / 攻击容量 {TextOrEmpty(AttackCapacity)} / 状态 {TextOrEmpty(SkillState)} / 守备 {TextOrEmpty(GuardState)}";

    public bool HasExportedIcon => !string.IsNullOrWhiteSpace(IconExportedFilePath) && System.IO.File.Exists(IconExportedFilePath);

    public string IconText => string.IsNullOrWhiteSpace(IconObjectPath)
        ? "图标未读取"
        : HasExportedIcon
            ? $"{IconAssetName}  已匹配导出图"
            : $"{IconAssetName}  {IconObjectPath}";

    public string MultiplierText => Multipliers.Count == 0
        ? "倍率未读取"
        : string.Join("；", Multipliers.Select(level => $"Lv{level.Level} 物{level.PhysicalMultiplier} 异{level.EnergyMultiplier}"));

    private static string TextOrEmpty(string value) => string.IsNullOrWhiteSpace(value) ? "空" : value;
}

internal sealed record UnrealProjectSyncSkillMultiplierPreview(
    int Level,
    string PhysicalMultiplier,
    string EnergyMultiplier);

internal sealed record UnrealProjectSyncLinkSkillPreview(
    string MainCharacterName,
    string SupportCharacterCode,
    int SkillIndex,
    UnrealProjectSyncSkillSlotPreview SkillSlot)
{
    public string Title => $"连携技 {SkillIndex}";

    public string TargetText => string.IsNullOrWhiteSpace(SupportCharacterCode)
        ? "连携目标：未读取"
        : $"连携目标：{SupportCharacterCode}";
}

internal sealed record UnrealProjectSyncSupportSkillPreview(
    string SupportCharacterName,
    int SourceIndex,
    UnrealProjectSyncSkillSlotPreview SkillSlot);

internal sealed record UnrealProjectSyncBuffsPreview(
    bool HasData,
    string ReadStatusText,
    IReadOnlyList<UnrealProjectSyncBuffPreview> Buffs)
{
    public int TotalCount => Buffs.Count;

    public int ReadyCount => Buffs.Count(buff => buff.HasReadableData);

    public int MissingIconCount => Buffs.Count(buff => !buff.HasExportedIcon);

    public string SummaryText => HasData
        ? $"BUFF {ReadyCount} / {TotalCount}，缺图标 {MissingIconCount}"
        : "未读取到 BUFF 数据";
}

internal sealed record UnrealProjectSyncBuffPreview(
    string AssetName,
    string ObjectPath,
    bool HasReadableData,
    string ReadStatusText,
    string TaskName,
    string DisplayName,
    string Description,
    string DamageType,
    string GainType,
    string TaskPriority,
    int Count,
    int CompleteCount,
    int Power,
    int CompletePower,
    string TriggerTiming,
    string ConditionSummary,
    string IconObjectPath,
    string IconAssetName,
    string IconExportedFilePath)
{
    public string Title => ResolveBuffTitle();

    public string DetailLine1 => $"{TextOrEmpty(GainType)} / {TextOrEmpty(TaskPriority)}";

    public string DetailLine2 => $"层数 {Count}~{CompleteCount} / 强度 {Power}~{CompletePower}";

    public string PreviewDescription => ShortenDescription(Description);

    public bool HasExportedIcon => !string.IsNullOrWhiteSpace(IconExportedFilePath) && System.IO.File.Exists(IconExportedFilePath);

    public string IconUri => HasExportedIcon ? new Uri(IconExportedFilePath).AbsoluteUri : string.Empty;

    public Visibility IconVisibility => HasExportedIcon ? Visibility.Visible : Visibility.Collapsed;

    public Visibility NoIconVisibility => HasExportedIcon ? Visibility.Collapsed : Visibility.Visible;

    private string ResolveBuffTitle()
    {
        var assetTitle = CleanBuffAssetName(AssetName);
        var taskName = CleanBuffDisplayText(TaskName);
        if (!string.IsNullOrWhiteSpace(taskName) && !LooksLikeOwnerName(taskName) && LooksLikeBuffSpecificName(taskName))
        {
            return taskName;
        }

        var displayName = CleanBuffDisplayText(DisplayName);
        if (!string.IsNullOrWhiteSpace(displayName) && !LooksLikeOwnerName(displayName) && LooksLikeBuffSpecificName(displayName))
        {
            return displayName;
        }

        return assetTitle;
    }

    private bool LooksLikeOwnerName(string value)
    {
        var text = value.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var asset = AssetName.Trim();
        if (!string.IsNullOrWhiteSpace(asset) &&
            asset.Contains(text, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return text.Length <= 3 &&
            !text.Contains("BUFF", StringComparison.OrdinalIgnoreCase) &&
            !text.Contains("Buff", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeBuffSpecificName(string value)
    {
        var text = value.Trim();
        return text.Contains("BUFF", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Buff", StringComparison.OrdinalIgnoreCase) ||
            text.Length > 3;
    }

    private static string CleanBuffAssetName(string value)
    {
        var text = CleanBuffDisplayText(value);
        if (string.IsNullOrWhiteSpace(text))
        {
            return "未命名 BUFF";
        }

        text = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"^.+?_BUFF",
            "BUFF",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return text.Trim('_', '-', ' ');
    }

    private static string CleanBuffDisplayText(string value)
    {
        var text = value.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        if (text.Contains("PropertyBUFF", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("DreamTask", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Class", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return text;
    }

    private static string ShortenDescription(string value)
    {
        var text = CleanBuffDisplayText(value);
        if (string.IsNullOrWhiteSpace(text))
        {
            return "无介绍";
        }

        text = text.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        return text.Length <= 10 ? text : $"{text[..10]}...";
    }

    private static string TextOrEmpty(string value) => string.IsNullOrWhiteSpace(value) ? "未读取" : value.Trim();

}

internal sealed record UnrealProjectSyncSequenceFramesPreview(
    bool HasData,
    bool HasAnimMaps,
    string AnimMapsObjectPath,
    string ReadStatusText,
    IReadOnlyList<UnrealProjectSyncSequenceActionPreview> BaseActions,
    IReadOnlyList<UnrealProjectSyncSequenceActionPreview> SkillActions,
    IReadOnlyList<UnrealProjectSyncSequenceActionPreview> LinkActions,
    IReadOnlyList<UnrealProjectSyncSequenceActionPreview> OtherActions,
    IReadOnlyList<UnrealProjectSyncExportAssetView> OrphanSequences)
{
    public IReadOnlyList<UnrealProjectSyncSequenceActionPreview> Actions => BaseActions.Concat(SkillActions).Concat(LinkActions).Concat(OtherActions).ToArray();

    public int ReadableActionCount => Actions.Count(action => action.HasData);

    public int TotalActionCount => Actions.Count;

    public int AnimSequenceCount => Actions.Sum(action => action.AnimSequenceCount);

    public int FrameTextureCount => Actions.Sum(action => action.TextureCount);

    public string SummaryText => HasData
        ? $"动作 {ReadableActionCount} / {TotalActionCount}，数量 {FrameTextureCount}"
        : "未读取到序列帧数据";
}

internal sealed record UnrealProjectSyncSequenceActionPreview(
    string ActionCode,
    string DisplayName,
    string SourceProperty,
    string Category,
    string Target,
    bool HasData,
    IReadOnlyList<int> FormIndexes,
    int ReferencedSequenceCount,
    int AnimSequenceCount,
    int TextureCount,
    int SpriteCount,
    int FlipbookCount,
    double FramesPerSecond,
    IReadOnlyList<UnrealProjectSyncExportAssetView> OrderedFrames,
    IReadOnlyList<UnrealProjectSyncExportAssetView> PreviewFrames,
    IReadOnlyList<UnrealProjectSyncSequenceSoundNotifyPreview>? SoundNotifies = null,
    IReadOnlyList<UnrealProjectSyncExportAssetView>? OwnedAssets = null)
{
    public int FormIndex => FormIndexes.Count == 0 ? 1 : FormIndexes[0];

    public string Title => Category.Equals("link", StringComparison.OrdinalIgnoreCase) || FormIndex <= 1
        ? DisplayName
        : $"{DisplayName}-{FormIndex}";

    public string FormText => FormIndexes.Count == 0
        ? "形态未识别"
        : $"形态 {FormIndex}";

    public string CountText => $"数量 {TextureCount}";

    public string DetailLine1 => $"精灵 {SpriteCount} / {FormText}";

    public string DetailLine2 => $"帧率 {FormattedFps}";

    public string DetailText => $"{DetailLine1} / {DetailLine2}";

    public IReadOnlyList<UnrealProjectSyncExportAssetView> CardPreviewFrames => PreviewFrames.Take(2).ToArray();

    public bool HasFramePreview => OrderedFrames.Count > 0 || PreviewFrames.Count > 0;

    public Visibility NoFrameVisibility => HasFramePreview
        ? Visibility.Collapsed
        : Visibility.Visible;

    public string NoFrameText => HasData
        ? "序列存在，暂无帧图"
        : "未读取到序列帧";

    public string FpsText => $"帧率 {FormattedFps}";

    private string FormattedFps => FramesPerSecond > 0
        ? FramesPerSecond.ToString("0.##")
        : "12";

    public IReadOnlyList<UnrealProjectSyncSequenceSoundNotifyPreview> SequenceSounds => SoundNotifies ?? [];
}

internal sealed record UnrealProjectSyncSequenceSoundNotifyPreview(
    int FrameIndex,
    double TimeSeconds,
    int TrackIndex,
    string SoundObjectPath,
    string SoundAssetName,
    string SoundAssetClass,
    string ExportedFilePath,
    bool IsCharacterVoice,
    string SequenceObjectPath = "");

internal sealed record UnrealProjectSyncCharacterInfoPreview(
    string AssetName,
    string ObjectPath,
    bool HasItemData,
    string ReadMessage,
    string Name,
    string Description,
    IReadOnlyList<string> KeywordTags,
    IReadOnlyList<string> PassiveSkills,
    int FormLimit,
    int SkillCount,
    int Speed,
    int Health,
    int Attack,
    int PhysicalDefense,
    int EnergyDefense,
    int CriticalRate,
    int CriticalDamage,
    int Synchronize,
    bool Anti = false)
{
    public bool HasData => !string.IsNullOrWhiteSpace(ObjectPath);

    public string SourceText => HasData ? $"{AssetName}  {ObjectPath}" : "未找到角色物品数据";

    public string ReadStatusText => !HasData
        ? "未找到道具蓝图"
        : HasItemData
            ? "已读取 ItemData.CharData"
            : $"已找到道具蓝图，但未读出 ItemData：{ReadMessage}";

    public string DescriptionText => string.IsNullOrWhiteSpace(Description) ? "无" : Description;

    public string KeywordTagsText => KeywordTags.Count == 0 ? "无" : string.Join("、", KeywordTags);

    public string PassiveSkillsText => PassiveSkills.Count == 0 ? "无" : $"{PassiveSkills.Count} 条";

    public string ShapeSkillText => $"形态上限 {FormLimit} / 技能详情 {SkillCount} / 被动介绍 {PassiveSkillsText}";

    public string StatsText =>
        $"抗性 {(Anti ? "异能" : "物理")} / 速度 {Speed} / 生命 {Health} / 攻击 {Attack} / 物防 {PhysicalDefense} / 异防 {EnergyDefense} / 暴击 {CriticalRate} / 暴伤 {CriticalDamage} / 同步率 {Synchronize}";
}

internal sealed record UnrealProjectSyncMaterialBucket(
    string DisplayName,
    string Kind,
    int Count,
    IReadOnlyList<UnrealProjectSyncExportAssetView> Assets)
{
    public string CountText => $"{Count} 个";
}

internal sealed record UnrealProjectSyncVoiceBucket(
    string DisplayName,
    string Kind,
    int Count,
    IReadOnlyList<UnrealProjectSyncExportAssetView> Assets);

internal sealed record UnrealProjectSyncExportRunResult(
    int ExitCode,
    string ManifestPath,
    int AssetCount,
    string Output,
    /// <summary>
    /// 退出码非 0、但清单确实是这一轮新写出来的时候的诊断信息。
    /// 编辑器只要在别处报过错（例如某个蓝图编译不过）就会让 commandlet 返回非 0，
    /// 那跟导出成没成功无关。
    /// </summary>
    string Warning = "");

internal sealed class UnrealExportProgressState
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("detail")]
    public string Detail { get; set; } = string.Empty;

    [JsonPropertyName("percent")]
    public double Percent { get; set; }

    [JsonPropertyName("isIndeterminate")]
    public bool IsIndeterminate { get; set; }

    [JsonPropertyName("updatedAt")]
    public string UpdatedAt { get; set; } = string.Empty;
}

internal sealed class UnrealProjectExportManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 2;

    [JsonPropertyName("generatedAt")]
    public string GeneratedAt { get; set; } = string.Empty;

    [JsonPropertyName("projectPath")]
    public string ProjectPath { get; set; } = string.Empty;

    [JsonPropertyName("targets")]
    public string[] Targets { get; set; } = [];

    [JsonPropertyName("assets")]
    public List<UnrealProjectExportAsset> Assets { get; set; } = [];

    [JsonPropertyName("characterItems")]
    public List<UnrealProjectExportCharacterItem> CharacterItems { get; set; } = [];

    [JsonPropertyName("characterSummaries")]
    public List<UnrealProjectExportCharacterSummary> CharacterSummaries { get; set; } = [];

    [JsonPropertyName("summaryGeneratedAt")]
    public string SummaryGeneratedAt { get; set; } = string.Empty;

    [JsonPropertyName("characterActors")]
    public List<UnrealProjectExportCharacterActor> CharacterActors { get; set; } = [];

    [JsonPropertyName("characterSequences")]
    public List<UnrealProjectExportCharacterSequence> CharacterSequences { get; set; } = [];

    [JsonPropertyName("characterBuffs")]
    public List<UnrealProjectExportCharacterBuffSet> CharacterBuffs { get; set; } = [];

    [JsonPropertyName("linkSkillLibrary")]
    public UnrealProjectExportLinkSkillLibrary LinkSkillLibrary { get; set; } = new();

    [JsonPropertyName("supportSkillLibrary")]
    public UnrealProjectExportSupportSkillLibrary SupportSkillLibrary { get; set; } = new();

    [JsonPropertyName("teamSelect")]
    public UnrealProjectExportTeamSelect TeamSelect { get; set; } = new();
}

internal sealed class UnrealProjectExportTeamSelect
{
    [JsonPropertyName("objectPath")]
    public string ObjectPath { get; set; } = string.Empty;

    [JsonPropertyName("assetClass")]
    public string AssetClass { get; set; } = string.Empty;

    [JsonPropertyName("hasCharVoice")]
    public bool HasCharVoice { get; set; }

    [JsonPropertyName("readMessage")]
    public string ReadMessage { get; set; } = string.Empty;
}

internal sealed class UnrealProjectExportCharacterSummary
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;
}

internal sealed class UnrealProjectExportAsset
{
    [JsonPropertyName("assetName")]
    public string AssetName { get; set; } = string.Empty;

    [JsonPropertyName("assetClass")]
    public string AssetClass { get; set; } = string.Empty;

    [JsonPropertyName("packageName")]
    public string PackageName { get; set; } = string.Empty;

    [JsonPropertyName("packagePath")]
    public string PackagePath { get; set; } = string.Empty;

    [JsonPropertyName("objectPath")]
    public string ObjectPath { get; set; } = string.Empty;

    [JsonPropertyName("sourceRoot")]
    public string SourceRoot { get; set; } = string.Empty;

    [JsonPropertyName("exportedFilePath")]
    public string ExportedFilePath { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public Dictionary<string, string> Tags { get; set; } = [];
}

internal sealed class UnrealProjectExportCharacterItem
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("assetName")]
    public string AssetName { get; set; } = string.Empty;

    [JsonPropertyName("objectPath")]
    public string ObjectPath { get; set; } = string.Empty;

    [JsonPropertyName("assetClass")]
    public string AssetClass { get; set; } = string.Empty;

    [JsonPropertyName("hasItemData")]
    public bool HasItemData { get; set; }

    [JsonPropertyName("parentClass")]
    public string ParentClass { get; set; } = string.Empty;

    [JsonPropertyName("readMessage")]
    public string ReadMessage { get; set; } = string.Empty;

    [JsonPropertyName("itemData")]
    public UnrealProjectExportItemData ItemData { get; set; } = new();
}

internal sealed class UnrealProjectExportItemData
{
    [JsonPropertyName("hasCharData")]
    public bool HasCharData { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("keywords")]
    public List<string> Keywords { get; set; } = [];

    [JsonPropertyName("extraDescription")]
    public List<string> ExtraDescription { get; set; } = [];

    [JsonPropertyName("charData")]
    public UnrealProjectExportCharData CharData { get; set; } = new();
}

internal sealed class UnrealProjectExportCharData
{
    [JsonPropertyName("charShapeNow")]
    public int CharShapeNow { get; set; }

    [JsonPropertyName("charShapeHas")]
    public List<bool> CharShapeHas { get; set; } = [];

    [JsonPropertyName("skillNow")]
    public List<int> SkillNow { get; set; } = [];

    [JsonPropertyName("skillHave")]
    public List<bool> SkillHave { get; set; } = [];

    [JsonPropertyName("skillLevel")]
    public List<int> SkillLevel { get; set; } = [];

    [JsonPropertyName("skillDescription")]
    public List<string> SkillDescription { get; set; } = [];

    [JsonPropertyName("skillDataCount")]
    public int SkillDataCount { get; set; }

    [JsonPropertyName("speed")]
    public int Speed { get; set; }

    [JsonPropertyName("health")]
    public int Health { get; set; }

    [JsonPropertyName("attack")]
    public int Attack { get; set; }

    [JsonPropertyName("phyDefense")]
    public int PhyDefense { get; set; }

    [JsonPropertyName("magDefense")]
    public int MagDefense { get; set; }

    [JsonPropertyName("critical")]
    public int Critical { get; set; }

    [JsonPropertyName("criticalC")]
    public int CriticalC { get; set; }

    [JsonPropertyName("synchronize")]
    public int Synchronize { get; set; }

    [JsonPropertyName("anti")]
    public bool Anti { get; set; }
}

internal sealed class UnrealProjectExportCharacterActor
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("assetName")]
    public string AssetName { get; set; } = string.Empty;

    [JsonPropertyName("objectPath")]
    public string ObjectPath { get; set; } = string.Empty;

    [JsonPropertyName("hasActorData")]
    public bool HasActorData { get; set; }

    [JsonPropertyName("readMessage")]
    public string ReadMessage { get; set; } = string.Empty;

    [JsonPropertyName("targetShape")]
    public int TargetShape { get; set; } = 1;

    [JsonPropertyName("skillSlots")]
    public Dictionary<string, UnrealProjectExportSkillSlot> SkillSlots { get; set; } = [];
}

internal sealed class UnrealProjectExportSkillSlot
{
    [JsonPropertyName("slotKey")]
    public string SlotKey { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("icons")]
    public List<string> Icons { get; set; } = [];

    [JsonPropertyName("names")]
    public List<string> Names { get; set; } = [];

    [JsonPropertyName("descriptions")]
    public List<string> Descriptions { get; set; } = [];

    [JsonPropertyName("pointCosts")]
    public List<int> PointCosts { get; set; } = [];

    [JsonPropertyName("skillRates")]
    public List<UnrealProjectExportSkillRate> SkillRates { get; set; } = [];

    [JsonPropertyName("autoPriorities")]
    public List<int> AutoPriorities { get; set; } = [];

    [JsonPropertyName("skillStates")]
    public List<string> SkillStates { get; set; } = [];

    [JsonPropertyName("preformTypes")]
    public List<string> PreformTypes { get; set; } = [];

    [JsonPropertyName("preSkillValues")]
    public List<double> PreSkillValues { get; set; } = [];

    [JsonPropertyName("skillNames")]
    public List<string> SkillNames { get; set; } = [];

    [JsonPropertyName("attackCapacities")]
    public List<int> AttackCapacities { get; set; } = [];
}

internal sealed class UnrealProjectExportSkillRate
{
    [JsonPropertyName("physical")]
    public Dictionary<string, double> Physical { get; set; } = [];

    [JsonPropertyName("energy")]
    public Dictionary<string, double> Energy { get; set; } = [];
}

internal sealed class UnrealProjectExportCharacterSequence
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("animMapsObjectPath")]
    public string AnimMapsObjectPath { get; set; } = string.Empty;

    [JsonPropertyName("hasAnimMaps")]
    public bool HasAnimMaps { get; set; }

    [JsonPropertyName("hasData")]
    public bool HasData { get; set; }

    [JsonPropertyName("readMessage")]
    public string ReadMessage { get; set; } = string.Empty;

    [JsonPropertyName("animSequenceCount")]
    public int AnimSequenceCount { get; set; }

    [JsonPropertyName("frameTextureCount")]
    public int FrameTextureCount { get; set; }

    [JsonPropertyName("actions")]
    public List<UnrealProjectExportSequenceAction> Actions { get; set; } = [];

    /// <summary>
    /// 挂在该角色动画源上、却不在其规范 AnimSequences 目录里的序列。
    /// PaperZD 的动画源没有列表属性，"注册"就是序列自身的 AnimSource 指针，
    /// 所以这类序列可能躺在项目的任何角落，按目录扫描看不到。
    /// </summary>
    [JsonPropertyName("orphanSequences")]
    public List<UnrealProjectExportSequenceAsset> OrphanSequences { get; set; } = [];
}

internal sealed class UnrealProjectExportSequenceAction
{
    [JsonPropertyName("actionCode")]
    public string ActionCode { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("sourceProperty")]
    public string SourceProperty { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;

    [JsonPropertyName("hasData")]
    public bool HasData { get; set; }

    [JsonPropertyName("formIndexes")]
    public List<int> FormIndexes { get; set; } = [];

    [JsonPropertyName("referencedSequences")]
    public List<string> ReferencedSequences { get; set; } = [];

    [JsonPropertyName("animSequences")]
    public List<UnrealProjectExportSequenceAsset> AnimSequences { get; set; } = [];

    [JsonPropertyName("textureCount")]
    public int TextureCount { get; set; }

    [JsonPropertyName("spriteCount")]
    public int SpriteCount { get; set; }

    [JsonPropertyName("flipbookCount")]
    public int FlipbookCount { get; set; }

    [JsonPropertyName("flipbookPaths")]
    public List<string> FlipbookPaths { get; set; } = [];

    [JsonPropertyName("orderedSpritePaths")]
    public List<string> OrderedSpritePaths { get; set; } = [];

    [JsonPropertyName("framesPerSecond")]
    public double FramesPerSecond { get; set; }

    /// <summary>该动作在 Unreal 里占用的全部资产，用于发现已经断开引用的旧素材。</summary>
    [JsonPropertyName("ownedAssets")]
    public List<UnrealProjectExportSequenceAsset> OwnedAssets { get; set; } = [];

    [JsonPropertyName("orderedFrames")]
    public List<UnrealProjectExportSequenceAsset> OrderedFrames { get; set; } = [];

    [JsonPropertyName("previewFrames")]
    public List<UnrealProjectExportSequenceAsset> PreviewFrames { get; set; } = [];

    [JsonPropertyName("soundNotifies")]
    public List<UnrealProjectExportSequenceSoundNotify> SoundNotifies { get; set; } = [];
}

internal sealed class UnrealProjectExportSequenceSoundNotify
{
    [JsonPropertyName("frameIndex")]
    public int FrameIndex { get; set; }

    [JsonPropertyName("timeSeconds")]
    public double TimeSeconds { get; set; }

    [JsonPropertyName("trackIndex")]
    public int TrackIndex { get; set; }

    [JsonPropertyName("soundObjectPath")]
    public string SoundObjectPath { get; set; } = string.Empty;

    [JsonPropertyName("soundAssetName")]
    public string SoundAssetName { get; set; } = string.Empty;

    [JsonPropertyName("soundAssetClass")]
    public string SoundAssetClass { get; set; } = string.Empty;

    [JsonPropertyName("exportedFilePath")]
    public string ExportedFilePath { get; set; } = string.Empty;

    [JsonPropertyName("isCharacterVoice")]
    public bool IsCharacterVoice { get; set; }

    [JsonPropertyName("sequenceObjectPath")]
    public string SequenceObjectPath { get; set; } = string.Empty;
}

internal sealed class UnrealProjectExportSequenceAsset
{
    [JsonPropertyName("assetName")]
    public string AssetName { get; set; } = string.Empty;

    [JsonPropertyName("assetClass")]
    public string AssetClass { get; set; } = string.Empty;

    [JsonPropertyName("packagePath")]
    public string PackagePath { get; set; } = string.Empty;

    [JsonPropertyName("objectPath")]
    public string ObjectPath { get; set; } = string.Empty;

    [JsonPropertyName("exportedFilePath")]
    public string ExportedFilePath { get; set; } = string.Empty;

    [JsonPropertyName("isBlank")]
    public bool IsBlank { get; set; }
}

internal sealed class UnrealProjectExportLinkSkillLibrary
{
    [JsonPropertyName("objectPath")]
    public string ObjectPath { get; set; } = string.Empty;

    [JsonPropertyName("hasData")]
    public bool HasData { get; set; }

    [JsonPropertyName("readMessage")]
    public string ReadMessage { get; set; } = string.Empty;

    [JsonPropertyName("entries")]
    public Dictionary<string, UnrealProjectExportLinkSkillEntry> Entries { get; set; } = [];
}

internal sealed class UnrealProjectExportLinkSkillEntry
{
    [JsonPropertyName("mainCharacterName")]
    public string MainCharacterName { get; set; } = string.Empty;

    [JsonPropertyName("supportCharacterCode")]
    public string SupportCharacterCode { get; set; } = string.Empty;

    [JsonPropertyName("skill12Index")]
    public int Skill12Index { get; set; }

    [JsonPropertyName("skillSlot5Data")]
    public UnrealProjectExportSkillSlot SkillSlot5Data { get; set; } = new();
}

internal sealed class UnrealProjectExportSupportSkillLibrary
{
    [JsonPropertyName("objectPath")]
    public string ObjectPath { get; set; } = string.Empty;

    [JsonPropertyName("hasData")]
    public bool HasData { get; set; }

    [JsonPropertyName("readMessage")]
    public string ReadMessage { get; set; } = string.Empty;

    [JsonPropertyName("entries")]
    public Dictionary<string, UnrealProjectExportSupportSkillEntry> Entries { get; set; } = [];
}

internal sealed class UnrealProjectExportSupportSkillEntry
{
    [JsonPropertyName("supportCharacterName")]
    public string SupportCharacterName { get; set; } = string.Empty;

    [JsonPropertyName("supportCharacterCode")]
    public string SupportCharacterCode { get; set; } = string.Empty;

    [JsonPropertyName("sourceIndex")]
    public int SourceIndex { get; set; }

    [JsonPropertyName("skillSlot4Data")]
    public UnrealProjectExportSkillSlot SkillSlot4Data { get; set; } = new();
}

internal sealed class UnrealProjectExportCharacterBuffSet
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("buffFolderObjectPath")]
    public string BuffFolderObjectPath { get; set; } = string.Empty;

    [JsonPropertyName("hasData")]
    public bool HasData { get; set; }

    [JsonPropertyName("readMessage")]
    public string ReadMessage { get; set; } = string.Empty;

    [JsonPropertyName("buffs")]
    public List<UnrealProjectExportBuff> Buffs { get; set; } = [];
}

internal sealed class UnrealProjectExportBuff
{
    [JsonPropertyName("assetName")]
    public string AssetName { get; set; } = string.Empty;

    [JsonPropertyName("objectPath")]
    public string ObjectPath { get; set; } = string.Empty;

    [JsonPropertyName("hasReadableData")]
    public bool HasReadableData { get; set; }

    [JsonPropertyName("readMessage")]
    public string ReadMessage { get; set; } = string.Empty;

    [JsonPropertyName("taskName")]
    public string TaskName { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("damageType")]
    public string DamageType { get; set; } = string.Empty;

    [JsonPropertyName("gainType")]
    public string GainType { get; set; } = string.Empty;

    [JsonPropertyName("taskPriority")]
    public string TaskPriority { get; set; } = string.Empty;

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("completeCount")]
    public int CompleteCount { get; set; }

    [JsonPropertyName("power")]
    public int Power { get; set; }

    [JsonPropertyName("completePower")]
    public int CompletePower { get; set; }

    [JsonPropertyName("triggerTiming")]
    public string TriggerTiming { get; set; } = string.Empty;

    [JsonPropertyName("conditionSummary")]
    public string ConditionSummary { get; set; } = string.Empty;

    [JsonPropertyName("iconObjectPath")]
    public string IconObjectPath { get; set; } = string.Empty;

    [JsonPropertyName("iconAssetName")]
    public string IconAssetName { get; set; } = string.Empty;

    [JsonPropertyName("iconExportedFilePath")]
    public string IconExportedFilePath { get; set; } = string.Empty;
}
