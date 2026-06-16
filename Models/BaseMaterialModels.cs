using System;
using System.Collections.Generic;

namespace CrossingVoidZDTool;

internal enum BaseMaterialKind
{
    ItemIcon,
    Icon,
    BattleAvatar,
    SkillIcon,
    BuffIcon,
    MorphPortrait,
    FullMorphPortrait,
    Background,
    SupportCutIn,
    OtherImage
}

internal enum BaseMaterialStatus
{
    Missing,
    Ready,
    Invalid
}

internal sealed record BaseMaterialSpec(
    BaseMaterialKind Kind,
    string DisplayName,
    string FolderName,
    string FileSuffix,
    int Width,
    int Height,
    int MinimumCount = 1,
    bool IsSingle = false)
{
    public bool HasFixedSize => Width > 0 && Height > 0;

    public string TargetText => HasFixedSize ? $"目标尺寸 {Width}x{Height}" : "单张图，保留原尺寸";

    public string CountRequirementText => $"至少 {MinimumCount} 张";
}

internal sealed record BaseMaterialItem(
    BaseMaterialKind Kind,
    string DisplayName,
    string FilePath,
    string FileUri,
    string FileName,
    int Index,
    int RequiredWidth,
    int RequiredHeight,
    int ActualWidth,
    int ActualHeight,
    BaseMaterialStatus Status,
    string StatusText,
    DateTime UpdatedAt);

internal sealed record BaseMaterialSection(
    BaseMaterialSpec Spec,
    IReadOnlyList<BaseMaterialItem> Items,
    bool HasWarning,
    string StatusText);
