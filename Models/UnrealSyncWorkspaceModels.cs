using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.IO;
using System.Text.Json;
using CrossingVoidZDTool.ViewModels;

namespace CrossingVoidZDTool;

internal enum UnrealSyncSourceKind
{
    SharedMaterial,
    UnrealCharacter,
    DraftCharacter
}

internal sealed record UnrealSyncSourceItem(
    UnrealSyncSourceKind Kind,
    string DisplayName,
    string SecondaryText,
    string SearchText,
    UnrealProjectSyncCharacterCandidate? UnrealCandidate = null,
    CharacterCard? DraftCharacter = null,
    bool IsAvailable = true)
{
    public bool IsSharedMaterial => Kind == UnrealSyncSourceKind.SharedMaterial;

    public string AvailabilityText => IsAvailable ? SecondaryText : $"{SecondaryText} · 待支持";
}

internal sealed record UnrealSyncPublishStageItem(
    UnrealBridgePublishStage Stage,
    string DisplayName,
    string DetailText,
    bool IsAvailable)
{
    public string StatusText => IsAvailable ? "可用" : "待制作";
}

internal sealed class UnrealSyncSelectionTreeItem : ObservableObject
{
    private bool? _isChecked;
    private bool _isUpdatingChildren;

    public event EventHandler? GroupSelectionChanged;

    public UnrealSyncSelectionTreeItem(
        string stableId,
        string displayName,
        string detailText,
        string statusText,
        bool isChecked,
        bool requiresAttention,
        bool isSelectable = true,
        UnrealBridgeModule? module = null,
        UnrealBridgeChange? change = null,
        IEnumerable<UnrealSyncSelectionTreeItem>? children = null)
    {
        StableId = stableId;
        DisplayName = displayName;
        DetailText = detailText;
        StatusText = statusText;
        _isChecked = isChecked;
        RequiresAttention = requiresAttention;
        IsSelectable = isSelectable;
        Module = module;
        Change = change;
        Children = new ObservableCollection<UnrealSyncSelectionTreeItem>(children ?? []);
        _visibleChildren = Children.ToArray();
        foreach (var child in Children)
        {
            child.PropertyChanged += Child_PropertyChanged;
        }

        if (Children.Count > 0)
        {
            UpdateCheckedStateFromChildren();
        }
    }

    public string StableId { get; }

    public string DisplayName { get; private set; }

    public string DetailText { get; private set; }

    public string StatusText { get; }

    public bool RequiresAttention { get; }

    public bool IsSelectable { get; }

    public UnrealBridgeModule? Module { get; }

    public UnrealBridgeChange? Change { get; }

    public ObservableCollection<UnrealSyncSelectionTreeItem> Children { get; }

    private IReadOnlyList<UnrealSyncSelectionTreeItem> _visibleChildren;

    public IReadOnlyList<UnrealSyncSelectionTreeItem> VisibleChildren
    {
        get => _visibleChildren;
        private set => SetProperty(ref _visibleChildren, value);
    }

    public void SetVisibleChildren(IEnumerable<UnrealSyncSelectionTreeItem> items) =>
        VisibleChildren = items.ToArray();

    public bool IsLeaf => Children.Count == 0;
    public bool IsUpdatingChildren => _isUpdatingChildren;

    public void ApplyDisplay(string displayName, string detailText)
    {
        if (!string.Equals(DisplayName, displayName, StringComparison.Ordinal))
        {
            DisplayName = displayName;
            OnPropertyChanged(nameof(DisplayName));
        }

        if (!string.Equals(DetailText, detailText, StringComparison.Ordinal))
        {
            DetailText = detailText;
            OnPropertyChanged(nameof(DetailText));
        }
    }

    public bool IsGroupChecked => _isChecked == true;

    public bool IsIndeterminate => _isChecked is null;

    public bool IsGroupUnchecked => _isChecked == false;

    public bool? IsChecked
    {
        get => _isChecked;
        set
        {
            if (Children.Count > 0)
            {
                ToggleGroupSelection();
                return;
            }

            SetCheckedState(value ?? false);
        }
    }

    public void ToggleGroupSelection()
    {
        if (!IsSelectable)
        {
            return;
        }

        var selectableChildren = Children.Where(child => child.IsSelectable).ToArray();
        var shouldSelectAll = selectableChildren.Any(child => child.IsChecked != true);
        SetGroupSelection(shouldSelectAll);
    }

    private void SetGroupSelection(bool isChecked)
    {
        _isUpdatingChildren = true;
        foreach (var child in Children.Where(child => child.IsSelectable))
        {
            child.IsChecked = isChecked;
        }

        _isUpdatingChildren = false;
        UpdateCheckedStateFromChildren();
        GroupSelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Child_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_isUpdatingChildren && e.PropertyName == nameof(IsChecked))
        {
            UpdateCheckedStateFromChildren();
        }
    }

    private void UpdateCheckedStateFromChildren()
    {
        if (Children.Count == 0)
        {
            return;
        }

        var selectableChildren = Children.Where(child => child.IsSelectable).ToArray();
        if (selectableChildren.Length == 0)
        {
            SetCheckedState(false);
            return;
        }

        var checkedCount = selectableChildren.Count(child => child.IsChecked == true);
        var nextValue = checkedCount == 0
            ? false
            : checkedCount == selectableChildren.Length
                ? true
                : (bool?)null;
        SetCheckedState(nextValue);
    }

    private void SetCheckedState(bool? value)
    {
        if (SetProperty(ref _isChecked, value, nameof(IsChecked)))
        {
            OnPropertyChanged(nameof(IsGroupChecked));
            OnPropertyChanged(nameof(IsIndeterminate));
            OnPropertyChanged(nameof(IsGroupUnchecked));
        }
    }
}

internal static class UnrealSyncSelectionTreeBuilder
{
    public static IReadOnlyList<UnrealSyncSelectionTreeItem> FromChanges(
        IReadOnlyCollection<UnrealBridgeChange> changes,
        Func<UnrealBridgeChange, bool>? canExecute = null)
    {
        canExecute ??= change => !change.RequiresExplicitConfirmation;
        return changes
            .Where(change => change.Kind != UnrealBridgeChangeKind.Unchanged)
            .GroupBy(change => change.Module)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var children = group
                    .OrderBy(change => change.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .Select(change => FromChange(change, canExecute))
                    .ToArray();
                return new UnrealSyncSelectionTreeItem(
                    $"module:{group.Key}",
                    ModuleName(group.Key),
                    $"{children.Length} 项变化",
                    children.Any(child => child.StatusText == "重定向")
                        ? "重定向"
                        : children.Any(child => child.RequiresAttention)
                            ? "需要检查"
                            : "可同步",
                    children.Length > 0 && children.All(child => child.IsChecked == true),
                    children.Any(child => child.RequiresAttention),
                    children.Any(child => child.IsSelectable),
                    group.Key,
                    children: children);
            })
            .ToArray();
    }

    public static IReadOnlyList<UnrealSyncSelectionTreeItem> FromSnapshot(UnrealBridgeSnapshot snapshot)
    {
        return snapshot.Items
            .GroupBy(item => item.Module)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var children = group
                    .Where(item => !item.StableId.StartsWith("sequence-frame:", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .Select(item =>
                    {
                        var requiresExportedFile = item.Module is UnrealBridgeModule.BaseMaterials or UnrealBridgeModule.Voices;
                        var isSelectable = !requiresExportedFile ||
                            !string.IsNullOrWhiteSpace(item.AssetPath) && File.Exists(item.AssetPath);
                        return new UnrealSyncSelectionTreeItem(
                            item.StableId,
                            item.DisplayName,
                            string.IsNullOrWhiteSpace(item.SourceObjectPath) ? item.NormalizedName : item.SourceObjectPath,
                            isSelectable ? "可导入" : "缺少导出文件",
                            isSelectable,
                            !isSelectable,
                            isSelectable,
                            item.Module);
                    })
                    .ToArray();
                return new UnrealSyncSelectionTreeItem(
                    $"module:{group.Key}",
                    ModuleName(group.Key),
                    $"{children.Length} 项",
                    children.Length == 0 ? "未读取" : "已读取",
                    children.Length > 0,
                    false,
                    children.Length > 0,
                    group.Key,
                    children: children);
            })
            .ToArray();
    }

    public static IReadOnlySet<string> SelectedStableIds(IEnumerable<UnrealSyncSelectionTreeItem> roots) =>
        roots.SelectMany(root => root.Children)
            .Where(item => item.IsChecked == true)
            .Select(item => item.StableId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static UnrealSyncSelectionTreeItem FromChange(
        UnrealBridgeChange change,
        Func<UnrealBridgeChange, bool> canExecute)
    {
        var isExecutable = canExecute(change);
        var requiresRedirect = !isExecutable && change.Kind != UnrealBridgeChangeKind.Conflict;
        var requiresAttention = !isExecutable ||
            change.Kind is UnrealBridgeChangeKind.Conflict or UnrealBridgeChangeKind.DeleteCandidate;
        return new UnrealSyncSelectionTreeItem(
            change.StableId,
            change.DisplayName,
            !string.IsNullOrWhiteSpace(change.UnrealItem?.SourceObjectPath)
                ? $"Unreal 现有：{change.UnrealItem.SourceObjectPath}"
                : $"工具箱来源：{change.ToolboxItem?.ToolboxRelativePath ?? string.Empty}",
            isExecutable ? ChangeKindName(change.Kind) : requiresRedirect ? "重定向" : "需要检查",
            change.IsSelected && isExecutable,
            requiresAttention,
            isExecutable || requiresRedirect,
            change.Module,
            change);
    }

    private static string ModuleName(UnrealBridgeModule module) => module switch
    {
        UnrealBridgeModule.CharacterInfo => "角色信息",
        UnrealBridgeModule.BaseMaterials => "基础素材",
        UnrealBridgeModule.Skills => "技能",
        UnrealBridgeModule.SequenceFrames => "序列帧",
        UnrealBridgeModule.Buffs => "BUFF",
        UnrealBridgeModule.Voices => "语音",
        _ => module.ToString()
    };

    private static string ChangeKindName(UnrealBridgeChangeKind kind) => kind switch
    {
        UnrealBridgeChangeKind.Added => "新增",
        UnrealBridgeChangeKind.Updated => "修改",
        UnrealBridgeChangeKind.Renamed => "移动或改名",
        UnrealBridgeChangeKind.Conflict => "冲突",
        UnrealBridgeChangeKind.DeleteCandidate => "删除",
        _ => "无变化"
    };
}
