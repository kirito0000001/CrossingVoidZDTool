using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
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
        IEnumerable<UnrealSyncSelectionTreeItem>? children = null,
        int deleteCount = 0,
        int addCount = 0,
        bool isSequenceGroup = false)
    {
        StableId = stableId;
        DisplayName = displayName;
        DetailText = detailText;
        StatusText = statusText;
        _isChecked = isChecked;
        RequiresAttention = requiresAttention;
        DeleteCount = deleteCount;
        AddCount = addCount;
        IsSequenceGroup = isSequenceGroup;
        IsSelectable = isSelectable;
        Module = module;
        Change = change;
        Children = new ObservableCollection<UnrealSyncSelectionTreeItem>(children ?? []);
        _visibleChildren = Children.ToArray();
        foreach (var child in Children)
        {
            child.PropertyChanged += Child_PropertyChanged;
        }

    }

    public string StableId { get; }

    public string DisplayName { get; private set; }

    public string DetailText { get; private set; }

    public string StatusText { get; }

    public bool RequiresAttention { get; }

    public int DeleteCount { get; }

    public int AddCount { get; }

    public string DeleteCountText => $"删除 {DeleteCount} 项";

    public string AddCountText => $"新增 {AddCount} 项";

    public bool IsSequenceGroup { get; }

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

    public void SetInitialCheckedState(bool? value)
    {
        _isChecked = value;
        OnPropertyChanged(nameof(IsChecked));
        OnPropertyChanged(nameof(IsGroupChecked));
        OnPropertyChanged(nameof(IsIndeterminate));
        OnPropertyChanged(nameof(IsGroupUnchecked));
    }

    public void RestoreCheckedState(IReadOnlySet<string> selectedStableIds)
    {
        if (Children.Count == 0)
        {
            SetInitialCheckedState(IsSelectable && selectedStableIds.Contains(StableId));
            return;
        }

        // 组节点也必须参与恢复。以前这里只恢复叶子 ID，
        // 用户只勾选“ 一技能 ”组时，刷新后会变成全未勾选。
        var groupWasSelected = selectedStableIds.Contains(StableId);
        if (groupWasSelected)
        {
            _isUpdatingChildren = true;
            foreach (var child in Children.Where(child => child.IsSelectable))
            {
                child.RestoreCheckedState(selectedStableIds);
                child.SetInitialCheckedState(true);
            }
            _isUpdatingChildren = false;
            UpdateCheckedStateFromChildren();
            return;
        }

        foreach (var child in Children)
        {
            child.RestoreCheckedState(selectedStableIds);
        }

        UpdateCheckedStateFromChildren();
    }

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
        Func<UnrealBridgeChange, bool>? canExecute = null,
        bool selectPendingByDefault = false)
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
                    .Select(change => FromChange(change, canExecute, selectPendingByDefault))
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
                    false,
                    children.Any(child => child.RequiresAttention),
                    children.Any(child => child.IsSelectable),
                    group.Key,
                    children: children);
            })
            .ToArray();
    }

    public static IReadOnlyList<UnrealSyncSelectionTreeItem> FromSequenceChanges(
        IReadOnlyCollection<UnrealBridgeChange> changes,
        Func<UnrealBridgeChange, bool>? canExecute = null,
        bool selectPendingByDefault = false)
    {
        canExecute ??= change => !change.RequiresExplicitConfirmation;
        var allChanges = changes.Where(change => change.Module == UnrealBridgeModule.SequenceFrames).ToArray();
        var parentNames = allChanges
            .Where(change => !change.StableId.StartsWith("sequence-frame:", StringComparison.OrdinalIgnoreCase))
            .GroupBy(change => change.StableId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().DisplayName, StringComparer.OrdinalIgnoreCase);
        var actionNameToKey = parentNames
            .Where(pair => pair.Key.StartsWith("sequence:", StringComparison.OrdinalIgnoreCase))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .GroupBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Key, StringComparer.OrdinalIgnoreCase);
        var actionCodeToKey = allChanges
            .Select(change => (change, actionCode: ExtractSequenceActionCode(change)))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.actionCode))
            .GroupBy(pair => NormalizeActionCode(pair.actionCode), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group =>
            {
                var toolboxParent = group.Select(pair => pair.change)
                    .FirstOrDefault(change => change.ToolboxItem is not null &&
                        !change.StableId.StartsWith("sequence-frame:", StringComparison.OrdinalIgnoreCase));
                return toolboxParent is not null
                    ? $"sequence:{NormalizeActionCode(group.Key)}"
                    : group.First().change.SequenceGroupKey;
            }, StringComparer.OrdinalIgnoreCase);
        return allChanges
            .Where(change => change.StableId.StartsWith("sequence-frame:", StringComparison.OrdinalIgnoreCase) &&
                change.Kind != UnrealBridgeChangeKind.Unchanged &&
                change.Kind is UnrealBridgeChangeKind.Added or UnrealBridgeChangeKind.DeleteCandidate)
            .GroupBy(change => ResolveSequenceGroupKey(change, actionNameToKey, actionCodeToKey))
            .Select(group =>
            {
                var deleteCount = group.Count(change => change.Kind == UnrealBridgeChangeKind.DeleteCandidate);
                var addCount = group.Count(change => change.Kind == UnrealBridgeChangeKind.Added);
                var representative = group.First();
                var parentKey = group.Key;
                var children = group
                    .OrderBy(change => change.Kind)
                    .ThenBy(change => change.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .Select(change => FromChange(change, canExecute, selectPendingByDefault))
                    .ToArray();
                var displayName = parentNames.GetValueOrDefault(parentKey, representative.DisplayName);
                return new UnrealSyncSelectionTreeItem(
                    group.Key,
                    displayName,
                    string.Empty,
                    "待同步",
                    false,
                    false,
                    children.Any(child => child.IsSelectable),
                    UnrealBridgeModule.SequenceFrames,
                    representative,
                    children: children,
                    deleteCount: deleteCount,
                    addCount: addCount,
                    isSequenceGroup: true);
            })
            .Where(item => item.DeleteCount > 0 || item.AddCount > 0)
            .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ResolveSequenceGroupKey(
        UnrealBridgeChange change,
        IReadOnlyDictionary<string, string> actionNameToKey,
        IReadOnlyDictionary<string, string> actionCodeToKey)
    {
        if (change.Module != UnrealBridgeModule.SequenceFrames)
        {
            return change.StableId;
        }

        var actionCode = ExtractSequenceActionCode(change);
        if (!string.IsNullOrWhiteSpace(actionCode) &&
            actionCodeToKey.TryGetValue(NormalizeActionCode(actionCode), out var actionKey) &&
            !string.IsNullOrWhiteSpace(actionKey))
        {
            return actionKey;
        }

        if (!string.IsNullOrWhiteSpace(change.SequenceGroupKey) &&
            change.SequenceGroupKey.StartsWith("sequence:", StringComparison.OrdinalIgnoreCase) &&
            actionNameToKey.Values.Contains(change.SequenceGroupKey, StringComparer.OrdinalIgnoreCase))
        {
            return change.SequenceGroupKey;
        }

        var match = Regex.Match(change.DisplayName ?? string.Empty, @"^(.*?)\s*第\s*\d+\s*帧\s*$");
        if (match.Success &&
            actionNameToKey.TryGetValue(match.Groups[1].Value.Trim(), out var key))
        {
            return key;
        }

        return change.StableId;
    }

    private static string ExtractSequenceActionCode(UnrealBridgeChange change)
    {
        var payload = change.ToolboxItem?.PayloadJson
            ?? change.UnrealItem?.PayloadJson
            ?? string.Empty;
        var sourcePath = change.UnrealItem?.SourceObjectPath ?? change.ToolboxItem?.SourceObjectPath ?? string.Empty;
        var package = sourcePath.Split('.', 2)[0].TrimEnd('/');
        var segments = package.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var materialIndex = Array.FindIndex(segments, segment =>
            string.Equals(segment, "Material", StringComparison.OrdinalIgnoreCase));
        if (materialIndex >= 0 && materialIndex + 1 < segments.Length)
        {
            return segments[materialIndex + 1];
        }

        if (payload.Contains("AnimSequences/", StringComparison.OrdinalIgnoreCase) ||
            sourcePath.Contains("/AnimSequences/", StringComparison.OrdinalIgnoreCase))
        {
            return segments.Length == 0 ? string.Empty : segments[^1];
        }

        if (payload.TrimStart().StartsWith("{", StringComparison.Ordinal))
        {
            try
            {
                using var document = JsonDocument.Parse(payload);
                if (document.RootElement.TryGetProperty("actionCode", out var actionCode) &&
                    actionCode.ValueKind == JsonValueKind.String)
                {
                    return actionCode.GetString() ?? string.Empty;
                }
            }
            catch (JsonException)
            {
                // Fall through to the legacy pipe-delimited payload format.
            }
        }

        var parts = payload.Split('|', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : string.Empty;
    }

    private static string NormalizeActionCode(string value) =>
        new string((value ?? string.Empty).Trim()
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());

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
        roots.SelectMany(EnumerateSelectedItems)
            .Select(item => item.StableId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlySet<string> SelectedGroupAndLeafStableIds(IEnumerable<UnrealSyncSelectionTreeItem> roots) =>
        roots.SelectMany(EnumerateSelectedGroupsAndLeaves)
            .Select(item => item.StableId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<UnrealSyncSelectionTreeItem> EnumerateSelectedGroupsAndLeaves(UnrealSyncSelectionTreeItem item)
    {
        if (item.IsChecked == true)
        {
            yield return item;
        }

        foreach (var child in item.Children.SelectMany(EnumerateSelectedGroupsAndLeaves))
        {
            yield return child;
        }
    }

    private static IEnumerable<UnrealSyncSelectionTreeItem> EnumerateSelectedItems(UnrealSyncSelectionTreeItem item)
    {
        if (item.IsChecked == true && item.IsLeaf)
        {
            yield return item;
        }

        foreach (var child in item.Children.SelectMany(EnumerateSelectedItems))
        {
            yield return child;
        }
    }

    private static UnrealSyncSelectionTreeItem FromChange(
        UnrealBridgeChange change,
        Func<UnrealBridgeChange, bool> canExecute,
        bool selectPendingByDefault = false)
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
            selectPendingByDefault && isExecutable && change.Kind is
                UnrealBridgeChangeKind.Added or UnrealBridgeChangeKind.Updated or UnrealBridgeChangeKind.Renamed,
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
