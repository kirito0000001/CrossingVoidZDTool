using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;

namespace CrossingVoidZDTool.ViewModels;

/// <summary>
/// 第六步「蓝图置入」的界面状态。
///
/// 结构照搬第四步：一份扫描结果 + 每条字段的勾选，勾完再把选中的下发回去写。
/// 差别只在中栏按「对局设置 / 动作序列 / 一技能…」分组显示，
/// 因为第六步的字段比第四步多得多，铺成一长条会读不下去。
/// </summary>
internal sealed partial class UnrealProjectSyncViewModel
{
    private List<UnrealBlueprintSetupResultItem> _lastBlueprintSetupItems = [];
    private IReadOnlyList<UnrealBlueprintSetupGroup> _blueprintSetupGroups = [];
    private bool _isBlueprintSetupLoaded;
    private bool _isApplyingBlueprintSetup;
    private bool _isBulkBlueprintSetupSelection;
    private string _blueprintSetupResultMessage = string.Empty;

    public ObservableCollection<UnrealBlueprintSetupViewItem> BlueprintSetupItems { get; } = [];

    /// <summary>中栏按组渲染，组内才是逐字段的卡片。</summary>
    public IReadOnlyList<UnrealBlueprintSetupGroup> BlueprintSetupGroups => _blueprintSetupGroups;

    public bool IsBlueprintSetupWorkspace => !IsEngineToToolbox && WorkflowStep == 6;

    public Visibility BlueprintSetupWorkspaceVisibility =>
        IsBlueprintSetupWorkspace && WorkspaceState == UnrealSyncWorkspaceState.HasContent
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility BlueprintSetupDetailsVisibility =>
        IsBlueprintSetupWorkspace ? Visibility.Visible : Visibility.Collapsed;

    public int BlueprintSetupPendingCount =>
        _lastBlueprintSetupItems.Count(item => item.Status == UnrealBlueprintSetupStatus.Pending);
    public int BlueprintSetupErrorCount =>
        _lastBlueprintSetupItems.Count(item => item.Status == UnrealBlueprintSetupStatus.Error);
    public int BlueprintSetupUnchangedCount =>
        _lastBlueprintSetupItems.Count(item => item.Status == UnrealBlueprintSetupStatus.Unchanged);
    public int BlueprintSetupSelectedCount => BlueprintSetupItems.Count(item => item.IsSelected);

    public string BlueprintSetupSummaryText => !_isBlueprintSetupLoaded
        ? "尚未检测蓝图数据"
        : $"共检查 {_lastBlueprintSetupItems.Count} 项：无差异 {BlueprintSetupUnchangedCount}，待写入 {BlueprintSetupPendingCount}，错误 {BlueprintSetupErrorCount}";

    public string BlueprintSetupSelectionText =>
        $"已选择 {BlueprintSetupSelectedCount} / {BlueprintSetupPendingCount} 项";

    public string BlueprintSetupResultMessage => _blueprintSetupResultMessage;

    public bool IsBlueprintSetupLoaded => _isBlueprintSetupLoaded;

    public bool CanApplyBlueprintSetup => IsBlueprintSetupWorkspace &&
        _isBlueprintSetupLoaded &&
        BlueprintSetupSelectedCount > 0 &&
        !_isApplyingBlueprintSetup &&
        IsWorkflowOperationIdle;

    public string WorkflowStep6StatusText => WorkflowStep < 6
        ? "待处理"
        : !_isBlueprintSetupLoaded
            ? "进行中"
            : BlueprintSetupErrorCount > 0
                ? "存在错误"
                : BlueprintSetupPendingCount > 0
                    ? "进行中"
                    : "已完成";

    public void SetBlueprintSetupResult(
        UnrealBlueprintSetupResult result,
        IReadOnlySet<string>? selectedStableIds = null,
        bool selectPendingByDefault = true)
    {
        ArgumentNullException.ThrowIfNull(result);
        foreach (var item in BlueprintSetupItems)
        {
            item.SelectionChanged -= BlueprintSetupItem_SelectionChanged;
        }

        BlueprintSetupItems.Clear();
        _lastBlueprintSetupItems = result.Items.Count == 0 && !result.Succeeded
            ?
            [
                new UnrealBlueprintSetupResultItem
                {
                    StableId = "blueprint.execution",
                    GroupKey = "onset",
                    GroupName = "蓝图置入",
                    DisplayName = "无法读取蓝图数据",
                    TargetField = "Unreal Python 执行结果",
                    Status = UnrealBlueprintSetupStatus.Error,
                    ErrorMessage = result.ErrorMessage
                }
            ]
            : result.Items.ToList();

        // 无差异的字段不进中栏：第六步一个角色就有六十多条，
        // 全铺出来会把真正待处理的几条埋掉。统计数字仍然按全部算。
        foreach (var source in _lastBlueprintSetupItems.Where(item => item.Status != UnrealBlueprintSetupStatus.Unchanged))
        {
            var isSelected = source.Status == UnrealBlueprintSetupStatus.Pending &&
                (selectedStableIds?.Contains(source.StableId) ?? selectPendingByDefault);
            var item = new UnrealBlueprintSetupViewItem(source, isSelected);
            item.SelectionChanged += BlueprintSetupItem_SelectionChanged;
            BlueprintSetupItems.Add(item);
        }

        RebuildBlueprintSetupGroups();
        _isBlueprintSetupLoaded = true;
        ClearWorkspaceFailure();
        _blueprintSetupResultMessage = result.Succeeded
            ? result.AppliedStableIds.Count > 0
                ? $"已写入并复查 {result.AppliedStableIds.Count} 项蓝图数据。"
                : "蓝图数据检测完成。"
            : string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? "蓝图数据存在未完成项目。"
                : result.ErrorMessage;
        NotifyBlueprintSetupChanged();
        SaveSessionCache();
    }

    /// <summary>分组顺序跟着扫描结果走，桥接脚本那边是按写入顺序产出的。</summary>
    private void RebuildBlueprintSetupGroups()
    {
        var groups = new List<UnrealBlueprintSetupGroup>();
        foreach (var item in BlueprintSetupItems)
        {
            var last = groups.Count > 0 ? groups[^1] : null;
            if (last is not null && last.GroupKey == item.GroupKey)
            {
                ((List<UnrealBlueprintSetupViewItem>)last.Items).Add(item);
                continue;
            }

            groups.Add(new UnrealBlueprintSetupGroup(
                item.GroupKey, item.GroupName, new List<UnrealBlueprintSetupViewItem> { item }));
        }

        _blueprintSetupGroups = groups;
        OnPropertyChanged(nameof(BlueprintSetupGroups));
    }

    public IReadOnlySet<string> GetSelectedBlueprintSetupIds() =>
        BlueprintSetupItems
            .Where(item => item.IsSelected && item.IsSelectable)
            .Select(item => item.StableId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public void SetApplyingBlueprintSetup(bool value)
    {
        if (_isApplyingBlueprintSetup == value)
        {
            return;
        }

        _isApplyingBlueprintSetup = value;
        OnPropertyChanged(nameof(CanApplyBlueprintSetup));
    }

    public void FailBlueprintSetup(string message)
    {
        _blueprintSetupResultMessage = message;
        OnPropertyChanged(nameof(BlueprintSetupResultMessage));
        SetWorkspaceFailure(message);
    }

    private void BlueprintSetupItem_SelectionChanged(object? sender, EventArgs e)
    {
        if (_isBulkBlueprintSetupSelection)
        {
            return;
        }

        NotifyStepSelectionChanged();
        NotifyBlueprintSetupChanged();
        SaveSessionCache();
    }

    private void ClearBlueprintSetupState()
    {
        foreach (var item in BlueprintSetupItems)
        {
            item.SelectionChanged -= BlueprintSetupItem_SelectionChanged;
        }

        BlueprintSetupItems.Clear();
        _lastBlueprintSetupItems.Clear();
        _blueprintSetupGroups = [];
        _isBlueprintSetupLoaded = false;
        _isApplyingBlueprintSetup = false;
        _isBulkBlueprintSetupSelection = false;
        _blueprintSetupResultMessage = string.Empty;
        OnPropertyChanged(nameof(BlueprintSetupGroups));
        NotifyBlueprintSetupChanged();
    }

    private void NotifyBlueprintSetupChanged()
    {
        OnPropertyChanged(nameof(IsBlueprintSetupLoaded));
        OnPropertyChanged(nameof(BlueprintSetupWorkspaceVisibility));
        OnPropertyChanged(nameof(BlueprintSetupSummaryText));
        NotifyWorkspaceStateChanged();
        OnPropertyChanged(nameof(BlueprintSetupSelectionText));
        OnPropertyChanged(nameof(BlueprintSetupResultMessage));
        OnPropertyChanged(nameof(BlueprintSetupPendingCount));
        OnPropertyChanged(nameof(BlueprintSetupErrorCount));
        OnPropertyChanged(nameof(BlueprintSetupUnchangedCount));
        OnPropertyChanged(nameof(BlueprintSetupSelectedCount));
        OnPropertyChanged(nameof(CanApplyBlueprintSetup));
        OnPropertyChanged(nameof(CanAdvanceWorkflow));
        OnPropertyChanged(nameof(WorkflowStep6StatusText));
    }
}
