using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CrossingVoidZDTool.Services;
using Microsoft.UI.Xaml;

namespace CrossingVoidZDTool.ViewModels;

/// <summary>
/// 第三步「基础配置」自己的那一块（一步一个文件；前三步见各自 partial）。
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
        get => _stepLoads.IsLoaded(3);
        private set
        {
            if (!_stepLoads.SetLoaded(3, value))
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

    public bool IsLightConfigurationWorkspace => !IsEngineToToolbox && WorkflowStep == 3;

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

    /// <summary>
    /// 「这一次运行读不到它」的项数（资产在工程里，但当前实例加载不了 —— 离线实例常见）。
    /// **不算错误**：它既不进错误计数、也不参与"有错误"的判断。
    /// </summary>
    public int LightConfigurationUnavailableCount => _lastLightConfigurationItems.Count(item =>
        item.Status == UnrealLightConfigurationStatus.Unavailable);

    public int LightConfigurationSelectedCount => LightConfigurationItems.Count(item => item.IsSelected);

    public string LightConfigurationSummaryText => !IsLightConfigurationLoaded
        ? "尚未检测基础配置"
        : $"共检查 {_lastLightConfigurationItems.Count} 项：无差异 {LightConfigurationUnchangedCount}，"
          + $"待设置 {LightConfigurationPendingCount}，错误 {LightConfigurationErrorCount}"
          // 「读不到」只在真出现时才占一格，平时那行字不用变长。
          + (LightConfigurationUnavailableCount > 0
              ? $"，读不到 {LightConfigurationUnavailableCount}"
              : string.Empty);

    public string LightConfigurationEmptyTitle => LightConfigurationErrorCount > 0
        ? "基础配置存在错误"
        : LightConfigurationUnavailableCount > 0
            ? "本次读不到依赖资产"
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
                    SourceSummary = "第三步配置协议",
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

    /// <summary>把当前检测结果写进第 3 步自己的缓存文件。</summary>
    internal bool SaveLightConfigurationCache(CharacterCard? character) =>
        character is not null &&
        Step3LightConfigurationCache.Save(
            character,
            _lastLightConfigurationItems,
            GetSelectedLightConfigurationIds(),
            _lightConfigurationResultMessage);

    /// <summary>
    /// 把第 3 步自己的小缓存回填到界面（内存里已经有结果时不覆盖）。
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

        var document = Step3LightConfigurationCache.TryLoad(character, character.Code);
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

    /// <summary>
    /// 从**整体会话缓存**里兜底恢复这一步的结果。
    ///
    /// 只在"没有自己的小文件"时才用得上 —— 为的是兼容"一步一个文件"改造**之前**
    /// 留下的旧进度（那时候第 3 步的结果只存在整体缓存里）。
    ///
    /// 原来这段构造代码在共享文件里被抄了两遍（进入某一步时一处、打开页面恢复现场时一处），
    /// 现在收在它自己的文件里；那两处只管"什么时候该试"。
    /// 顺手带上"内存里已经有结果就别覆盖"这道闸：旧写法在第二处没有它，
    /// 会把刚测出来的结果会话缓存盖回去。
    /// </summary>
    internal bool TryApplyLightConfigurationFromSessionCache(UnrealSyncSessionCache cache)
    {
        if (IsLightConfigurationLoaded || !cache.IsLightConfigurationLoaded)
        {
            return false;
        }

        SetLightConfigurationResult(
            new UnrealLightConfigurationResult
            {
                Succeeded = true,
                CharacterCode = cache.SelectedCharacterCode,
                Items = cache.LightConfigurationItems,
                ErrorMessage = cache.LightConfigurationResultMessage
            },
            cache.SelectedLightConfigurationIds,
            selectPendingByDefault: false);

        // SetLightConfigurationResult 会按"刚检测完"的口径重写文案，这里换回缓存里那句。
        _lightConfigurationResultMessage = cache.LightConfigurationResultMessage;
        OnPropertyChanged(nameof(LightConfigurationResultMessage));
        return true;
    }

    /// <summary>
    /// 作废这一步的检测结果：**内存清空 + 缓存文件删掉**，界面回到「尚未检测基础配置」。
    ///
    /// 由「第 2 步把素材写进工程」之后调用 —— 理由见
    /// <see cref="Step3LightConfigurationCache.Invalidate"/>：这一步有一批项问的是
    /// 「工程里有没有那个资产」，素材一同步，旧结果里的「未找到…」就成了假报错，
    /// 而且那些项勾不动，用户自己没法清。
    ///
    /// 顺带把「同步收尾」那道基础配置预检的闸门打开：它问的正是
    /// <see cref="IsLightConfigurationLoaded"/>（原来"已加载"被当成"结果还有效"，
    /// 于是刚同步完素材反而**跳过**了重扫，把过期结果留在界面上）。
    /// 没走预检的路径（比如只同步了一部分、提前 return 的那条）下，
    /// 界面也只是显示「尚未检测」，而不是拿着一份过期结果骗人。
    /// </summary>
    internal void InvalidateLightConfigurationResult()
    {
        Step3LightConfigurationCache.Invalidate(SelectedSource?.DraftCharacter);
        ClearLightConfigurationState();

        // **整体会话缓存里也有这一份**（第 3 步读不到自己的小文件时会回退到它，
        // 见 RestoreWorkflowStepCache 的 `cache.IsLightConfigurationLoaded` 那支）——
        // 只删小文件的话，下一轮照样能从那份大缓存里把过期结果捡回来。
        // 顺序不能反：FlushSessionCache 落盘的是**上一次**拍下的快照，
        // 先 flush 等于把旧内容原样写回去。所以先重拍、再立刻写。
        if (!_isRestoringSession && !string.IsNullOrWhiteSpace(ProjectPath))
        {
            SaveSessionCache();
            FlushSessionCache();
        }
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
        OnPropertyChanged(nameof(LightConfigurationUnavailableCount));
        OnPropertyChanged(nameof(LightConfigurationUnchangedCount));
        OnPropertyChanged(nameof(LightConfigurationSelectedCount));
        OnPropertyChanged(nameof(CanApplyLightConfiguration));
        OnPropertyChanged(nameof(CanAdvanceWorkflow));
        OnPropertyChanged(nameof(WorkflowStep3StatusText));
        NotifyWorkspaceStateChanged();
    }
}
