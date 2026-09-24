using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CrossingVoidZDTool.Services;
using Microsoft.UI.Xaml;

namespace CrossingVoidZDTool.ViewModels;

/// <summary>
/// 第四步「基础配置」自己的那一块（一步一个文件；前三步见各自 partial）。
///
/// 装的是这一步的**状态**：检测结果（待设置 / 无差异 / 错误）、选择情况、汇总文案、可见性。
/// 跑检测、应用配置那些动作仍在 <c>MainWindow.UnrealSync.LightConfiguration.cs</c>（壳侧），
/// 这里只保管"这一步现在是什么样"。
/// </summary>
internal sealed partial class UnrealProjectSyncViewModel
{
    // ── 检测结果与状态 ────────────────────────────────────────────────────

    private List<UnrealLightConfigurationResultItem> _lastLightConfigurationItems = [];

    public bool IsLightConfigurationLoaded
    {
        get => _stepLoads.IsLoaded(4);
        private set
        {
            if (!_stepLoads.SetLoaded(4, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsLightConfigurationLoaded));
            NotifyDerived(UnrealSyncDerivedNotifications.LightConfigurationLoaded);
            NotifyWorkspaceStateChanged();
        }
    }

    private bool _isApplyingLightConfiguration;
    private string _lightConfigurationResultMessage = string.Empty;

    public ObservableCollection<UnrealLightConfigurationViewItem> LightConfigurationItems { get; } = [];

    // ── 工作区可见性 ──────────────────────────────────────────────────────

    public bool IsLightConfigurationWorkspace => !IsEngineToToolbox && WorkflowStep == 4;

    public Visibility LightConfigurationWorkspaceVisibility =>
        IsLightConfigurationWorkspace && WorkspaceState == UnrealSyncWorkspaceState.HasContent
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility LightConfigurationDetailsVisibility =>
        IsLightConfigurationWorkspace ? Visibility.Visible : Visibility.Collapsed;

    // ── 计数与文案 ────────────────────────────────────────────────────────

    public int LightConfigurationPendingCount => _lastLightConfigurationItems.Count(item =>
        item.Status == UnrealLightConfigurationStatus.Pending);

    public int LightConfigurationErrorCount => _lastLightConfigurationItems.Count(item =>
        item.Status == UnrealLightConfigurationStatus.Error);

    public int LightConfigurationUnchangedCount => _lastLightConfigurationItems.Count(item =>
        item.Status == UnrealLightConfigurationStatus.Unchanged);

    public int LightConfigurationSelectedCount => LightConfigurationItems.Count(item => item.IsSelected);

    public string LightConfigurationSummaryText => !IsLightConfigurationLoaded
        ? "尚未检测基础配置"
        : $"共检查 {_lastLightConfigurationItems.Count} 项：无差异 {LightConfigurationUnchangedCount}，"
          + $"待设置 {LightConfigurationPendingCount}，错误 {LightConfigurationErrorCount}";

    public string LightConfigurationEmptyTitle => LightConfigurationErrorCount > 0
        ? "基础配置存在错误"
        : "基础配置没有改动";

    public string LightConfigurationSelectionText =>
        $"已选择 {LightConfigurationSelectedCount} / {LightConfigurationPendingCount} 项";

    public string LightConfigurationResultMessage => _lightConfigurationResultMessage;

    public bool CanApplyLightConfiguration => IsLightConfigurationWorkspace &&
        IsLightConfigurationLoaded &&
        LightConfigurationSelectedCount > 0 &&
        !_isApplyingLightConfiguration &&
        IsWorkflowOperationIdle;

    // ── 检测结果落库 ──────────────────────────────────────────────────────

    public void SetLightConfigurationResult(
        UnrealLightConfigurationResult result,
        IReadOnlySet<string>? selectedStableIds = null,
        bool selectPendingByDefault = true)
    {
        ArgumentNullException.ThrowIfNull(result);
        foreach (var item in LightConfigurationItems)
        {
            item.SelectionChanged -= LightConfigurationItem_SelectionChanged;
        }

        LightConfigurationItems.Clear();
        _lastLightConfigurationItems = result.Items.Count == 0 && !result.Succeeded
            ?
            [
                new UnrealLightConfigurationResultItem
                {
                    StableId = "configuration.execution",
                    GroupName = "基础配置",
                    DisplayName = "无法读取基础配置",
                    TargetField = "Unreal Python 执行结果",
                    SourceSummary = "第四步配置协议",
                    Status = UnrealLightConfigurationStatus.Error,
                    ErrorMessage = result.ErrorMessage
                }
            ]
            : result.Items.ToList();
        foreach (var source in _lastLightConfigurationItems.Where(item => item.Status != UnrealLightConfigurationStatus.Unchanged))
        {
            var isSelected = source.Status == UnrealLightConfigurationStatus.Pending &&
                (selectedStableIds?.Contains(source.StableId) ?? selectPendingByDefault);
            var item = new UnrealLightConfigurationViewItem(source, isSelected);
            item.SelectionChanged += LightConfigurationItem_SelectionChanged;
            LightConfigurationItems.Add(item);
        }

        IsLightConfigurationLoaded = true;
        _lightConfigurationResultMessage = result.Succeeded
            ? result.AppliedStableIds.Count > 0
                ? $"已应用并验证 {result.AppliedStableIds.Count} 项配置。"
                : "基础配置检测完成。"
            : string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? "基础配置存在未完成项目。"
                : result.ErrorMessage;
        ClearWorkspaceFailure();
        NotifyLightConfigurationChanged();
        SaveLightConfigurationCache(SelectedSource?.DraftCharacter);
        SaveSessionCache();
    }

    // ── 自己的缓存文件 ────────────────────────────────────────────────────

    /// <summary>把当前检测结果写进第 4 步自己的缓存文件。</summary>
    internal bool SaveLightConfigurationCache(CharacterCard? character) =>
        character is not null &&
        Step4LightConfigurationCache.Save(
            character,
            _lastLightConfigurationItems,
            GetSelectedLightConfigurationIds(),
            _lightConfigurationResultMessage);

    /// <summary>
    /// 把第 4 步自己的小缓存回填到界面（内存里已经有结果时不覆盖）。
    ///
    /// 和整体缓存（`UnrealSyncSessionCache`）的区别：那份是所有步骤共用的一大坨，
    /// 步骤之间会互相影响；这份只装这一步的检测项与勾选，坏掉也只坏这一步。
    /// </summary>
    internal bool TryApplyLightConfigurationCache(CharacterCard? character)
    {
        if (character is null || IsLightConfigurationLoaded)
        {
            return false;
        }

        var document = Step4LightConfigurationCache.TryLoad(character, character.Code);
        if (document is null || document.Items.Length == 0)
        {
            return false;
        }

        SetLightConfigurationResult(
            new UnrealLightConfigurationResult
            {
                Succeeded = true,
                CharacterCode = character.Code,
                Items = document.Items.Select(item => item.ToResultItem()).ToList(),
                ErrorMessage = document.ResultMessage
            },
            document.SelectedStableIds.ToHashSet(StringComparer.OrdinalIgnoreCase),
            selectPendingByDefault: false);

        // SetLightConfigurationResult 会按"刚检测完"的口径重写文案，这里换回缓存里那句。
        _lightConfigurationResultMessage = document.ResultMessage;
        OnPropertyChanged(nameof(LightConfigurationResultMessage));
        return true;
    }

    // ── 勾选 ──────────────────────────────────────────────────────────────

    public IReadOnlySet<string> GetSelectedLightConfigurationIds() =>
        LightConfigurationItems
            .Where(item => item.IsSelected && item.IsSelectable)
            .Select(item => item.StableId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private void LightConfigurationItem_SelectionChanged(object? sender, EventArgs e)
    {
        NotifyStepSelectionChanged();
        NotifyLightConfigurationChanged();
        SaveSessionCache();
    }

    // ── 应用配置的运行态 ──────────────────────────────────────────────────

    public void SetApplyingLightConfiguration(bool value)
    {
        if (_isApplyingLightConfiguration == value)
        {
            return;
        }

        _isApplyingLightConfiguration = value;
        OnPropertyChanged(nameof(CanApplyLightConfiguration));
    }

    public void FailLightConfiguration(string message)
    {
        _lightConfigurationResultMessage = message;
        OnPropertyChanged(nameof(LightConfigurationResultMessage));
        SetWorkspaceFailure(message);
    }

    // ── 换步清理与通知 ────────────────────────────────────────────────────

    private void ClearLightConfigurationState()
    {
        foreach (var item in LightConfigurationItems)
        {
            item.SelectionChanged -= LightConfigurationItem_SelectionChanged;
        }

        LightConfigurationItems.Clear();
        _lastLightConfigurationItems.Clear();
        IsLightConfigurationLoaded = false;
        _isApplyingLightConfiguration = false;
        _lightConfigurationResultMessage = string.Empty;
        NotifyLightConfigurationChanged();
    }

    private void NotifyLightConfigurationChanged()
    {
        OnPropertyChanged(nameof(IsLightConfigurationLoaded));
        OnPropertyChanged(nameof(LightConfigurationWorkspaceVisibility));
        OnPropertyChanged(nameof(LightConfigurationSummaryText));
        OnPropertyChanged(nameof(LightConfigurationEmptyTitle));
        OnPropertyChanged(nameof(LightConfigurationSelectionText));
        OnPropertyChanged(nameof(LightConfigurationResultMessage));
        OnPropertyChanged(nameof(LightConfigurationPendingCount));
        OnPropertyChanged(nameof(LightConfigurationErrorCount));
        OnPropertyChanged(nameof(LightConfigurationUnchangedCount));
        OnPropertyChanged(nameof(LightConfigurationSelectedCount));
        OnPropertyChanged(nameof(CanApplyLightConfiguration));
        OnPropertyChanged(nameof(CanAdvanceWorkflow));
        OnPropertyChanged(nameof(WorkflowStep4StatusText));
        NotifyWorkspaceStateChanged();
    }
}
