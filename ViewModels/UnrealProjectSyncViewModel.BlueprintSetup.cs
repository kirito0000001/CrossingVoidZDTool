using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CrossingVoidZDTool.Services;
using Microsoft.UI.Xaml;

namespace CrossingVoidZDTool.ViewModels;

/// <summary>
/// 第五步「蓝图置入」的界面状态。
///
/// 结构照搬第三步：一份扫描结果 + 每条字段的勾选，勾完再把选中的下发回去写。
/// 差别只在中栏按「对局设置 / 动作序列 / 一技能…」分组显示，
/// 因为第五步的字段比第三步多得多，铺成一长条会读不下去。
/// </summary>
internal sealed partial class UnrealProjectSyncViewModel
{
    private List<UnrealBlueprintSetupResultItem> _lastBlueprintSetupItems = [];
    private IReadOnlyList<UnrealBlueprintSetupGroup> _blueprintSetupGroups = [];
    public bool IsBlueprintSetupLoaded
    {
        get => _stepLoads.IsLoaded(5);
        private set
        {
            if (_stepLoads.SetLoaded(5, value))
            {
                OnPropertyChanged(nameof(IsBlueprintSetupLoaded));
                NotifyDerived(UnrealSyncDerivedNotifications.BlueprintSetupLoaded);
            }
        }
    }
    private bool _isApplyingBlueprintSetup;
    private bool _isBulkBlueprintSetupSelection;
    private string _blueprintSetupResultMessage = string.Empty;

    public ObservableCollection<UnrealBlueprintSetupViewItem> BlueprintSetupItems { get; } = [];

    /// <summary>中栏按组渲染，组内才是逐字段的卡片。</summary>
    public IReadOnlyList<UnrealBlueprintSetupGroup> BlueprintSetupGroups => _blueprintSetupGroups;

    public bool IsBlueprintSetupWorkspace => !IsEngineToToolbox && WorkflowStep == 5;

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

    public string BlueprintSetupSummaryText => !IsBlueprintSetupLoaded
        ? "尚未检测蓝图数据"
        : $"共检查 {_lastBlueprintSetupItems.Count} 项：无差异 {BlueprintSetupUnchangedCount}，待写入 {BlueprintSetupPendingCount}，错误 {BlueprintSetupErrorCount}";

    public string BlueprintSetupSelectionText =>
        $"已选择 {BlueprintSetupSelectedCount} / {BlueprintSetupPendingCount} 项";

    public string BlueprintSetupResultMessage => _blueprintSetupResultMessage;

    public bool CanApplyBlueprintSetup => IsBlueprintSetupWorkspace &&
        IsBlueprintSetupLoaded &&
        BlueprintSetupSelectedCount > 0 &&
        !_isApplyingBlueprintSetup &&
        IsWorkflowOperationIdle;

    public string WorkflowStep5StatusText => UnrealSyncWorkflowState.StepStatusText(BuildWorkflowInputs(), 5);

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

        // 无差异的字段不进中栏：第五步一个角色就有六十多条，
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
        IsBlueprintSetupLoaded = true;
        ClearWorkspaceFailure();
        _blueprintSetupResultMessage = result.Succeeded
            ? result.AppliedStableIds.Count > 0
                ? $"已写入并复查 {result.AppliedStableIds.Count} 项蓝图数据。"
                : "蓝图数据检测完成。"
            : string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? "蓝图数据存在未完成项目。"
                : result.ErrorMessage;
        NotifyBlueprintSetupChanged();
        SaveBlueprintSetupCache(SelectedSource?.DraftCharacter);
        SaveSessionCache();
    }

    // ── 自己的缓存文件 ────────────────────────────────────────────────────

    /// <summary>把当前扫描结果写进第 5 步自己的缓存文件。</summary>
    internal bool SaveBlueprintSetupCache(CharacterCard? character) =>
        character is not null &&
        Step5BlueprintSetupCache.Save(
            character,
            _lastBlueprintSetupItems,
            GetSelectedBlueprintSetupIds(),
            _blueprintSetupResultMessage);

    /// <summary>把第 5 步自己的小缓存回填到界面（内存里已经有结果时不覆盖）。</summary>
    internal bool TryApplyBlueprintSetupCache(CharacterCard? character)
    {
        if (character is null || IsBlueprintSetupLoaded)
        {
            return false;
        }

        var document = Step5BlueprintSetupCache.TryLoad(character, character.Code);
        if (document is null || document.Items.Length == 0)
        {
            return false;
        }

        SetBlueprintSetupResult(
            new UnrealBlueprintSetupResult
            {
                Succeeded = true,
                CharacterCode = character.Code,
                Items = document.Items.Select(item => item.ToResultItem()).ToList(),
                ErrorMessage = document.ResultMessage
            },
            document.SelectedStableIds.ToHashSet(StringComparer.OrdinalIgnoreCase),
            selectPendingByDefault: false);

        // SetBlueprintSetupResult 会按"刚检测完"的口径重写文案，这里换回缓存里那句。
        _blueprintSetupResultMessage = document.ResultMessage;
        OnPropertyChanged(nameof(BlueprintSetupResultMessage));
        return true;
    }

    /// <summary>
    /// 从**整体会话缓存**里兜底恢复这一步的结果。
    ///
    /// 只在"没有自己的小文件"时才用得上 —— 为的是兼容"一步一个文件"改造**之前**
    /// 留下的旧进度。原来这段构造代码在共享文件里被抄了两遍（进出这一步、冷启动恢复现场各一次），
    /// 现在收在这里；顺手带上"内存里已经有结果就别覆盖"那道闸。
    /// </summary>
    internal bool TryApplyBlueprintSetupFromSessionCache(UnrealSyncSessionCache cache)
    {
        if (IsBlueprintSetupLoaded || !cache.IsBlueprintSetupLoaded)
        {
            return false;
        }

        SetBlueprintSetupResult(
            new UnrealBlueprintSetupResult
            {
                Succeeded = true,
                CharacterCode = cache.SelectedCharacterCode,
                Items = cache.BlueprintSetupItems,
                ErrorMessage = cache.BlueprintSetupResultMessage
            },
            cache.SelectedBlueprintSetupIds,
            selectPendingByDefault: false);

        _blueprintSetupResultMessage = cache.BlueprintSetupResultMessage;
        OnPropertyChanged(nameof(BlueprintSetupResultMessage));
        return true;
    }

    /// <summary>
    /// 作废这一步的检测结果：**内存清空 + 小文件删掉 + 整体会话缓存里那份盖掉**。
    ///
    /// 由「第 2 步把素材写进工程」/「第 4 步把序列写进工程」之后调用 ——
    /// 这一步的字段里有一批问的是"工程里有没有那个资产"（技能图标、序列引用…），
    /// 那两步一跑，旧结果里的「目标资产不存在…请先完成第二步同步素材」就是假报错，
    /// 而且错误项不可勾选、用户没法弄掉（2026-09-24 体检）。
    /// </summary>
    internal void InvalidateBlueprintSetupResult()
    {
        Step5BlueprintSetupCache.Invalidate(SelectedSource?.DraftCharacter);
        ClearBlueprintSetupState();

        // 整体会话缓存里也有这一份（读不到小文件时会回退到它）。
        // 顺序不能反：FlushSessionCache 落的是**上一次**拍下的快照，先 flush 等于把旧内容写回去。
        if (!_isRestoringSession && !string.IsNullOrWhiteSpace(ProjectPath))
        {
            SaveSessionCache();
            FlushSessionCache();
        }
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
        IsBlueprintSetupLoaded = false;
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
        OnPropertyChanged(nameof(WorkflowStep5StatusText));
    }
}
