using System;
using System.Collections.Generic;
using System.Linq;

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

    public string CountRequirementText => MinimumCount > 0 ? $"至少 {MinimumCount} 张" : "可留空";
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

internal sealed record BaseMaterialSlot(
    int Index,
    string PlayerLabel,
    string RoleLabel,
    BaseMaterialItem? Item)
{
    public string DisplayName => $"{PlayerLabel}{RoleLabel}";

    public bool HasItem => Item is not null;

    public bool IsMissing => Item is null;

    public string FileName => Item?.FileName ?? "等待导入";

    public string StatusText => Item?.StatusText ?? "未设置";
}

internal sealed record BaseMaterialSlotGroup(
    string PlayerLabel,
    IReadOnlyList<BaseMaterialSlot> Slots);

internal sealed record BaseMaterialSection(
    BaseMaterialSpec Spec,
    IReadOnlyList<BaseMaterialItem> Items,
    bool HasWarning,
    string StatusText)
{
    public bool HasFixedSlots => Spec.Kind == BaseMaterialKind.BattleAvatar;

    public bool UsesStandardLayout => !HasFixedSlots;

    public IReadOnlyList<BaseMaterialSlotGroup> SlotGroups { get; } = BuildSlotGroups(Spec, Items);

    public int MissingCount => HasFixedSlots
        ? SlotGroups.SelectMany(group => group.Slots).Count(slot => slot.IsMissing)
        : Math.Max(0, Spec.MinimumCount - Items.Count);

    public int InvalidCount => Items.Count(item => item.Status == BaseMaterialStatus.Invalid);

    public int ExtraCount => HasFixedSlots
        ? Math.Max(0, Items.Count - SlotGroups.SelectMany(group => group.Slots).Count(slot => slot.HasItem))
        : 0;

    private static IReadOnlyList<BaseMaterialSlotGroup> BuildSlotGroups(
        BaseMaterialSpec spec,
        IReadOnlyList<BaseMaterialItem> items)
    {
        if (spec.Kind != BaseMaterialKind.BattleAvatar)
        {
            return [];
        }

        BaseMaterialSlot Slot(int index, string player, string role) =>
            new(index, player, role, items.FirstOrDefault(item => item.Index == index));

        return
        [
            new BaseMaterialSlotGroup("1P", [Slot(1, "1P", "主战"), Slot(2, "1P", "护援")]),
            new BaseMaterialSlotGroup("2P", [Slot(3, "2P", "主战"), Slot(4, "2P", "护援")])
        ];
    }
}
