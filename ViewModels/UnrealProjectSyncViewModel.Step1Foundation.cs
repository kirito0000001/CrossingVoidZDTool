using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CrossingVoidZDTool.Services;
using Microsoft.UI.Xaml;

namespace CrossingVoidZDTool.ViewModels;

/// <summary>
/// 第一步「底层检测」自己的那一块（一步一个文件，从这个 partial 开始拆）。
///
/// 这一块只关心两件事：**检查项**和**它自己的缓存文件**。
/// 以前第 1 步的状态是从整体会话缓存（所有步骤共用的一大坨）恢复的，
/// 那份缓存串台过好几次（`_loadedPublishStep` 默认写 3、发布阶段劫持第三步的过滤……），
/// 所以这里改成读 `<角色>\<工具目录>\UnrealSync\step1-foundation.json`。
/// </summary>
internal sealed partial class UnrealProjectSyncViewModel
{
    // ── 检查项状态（第 1 步自己的）──────────────────────────────────────────

    /// <summary>
    /// 这份检查属于**第 1 步**，所以口径固定按 1 问 —— 不能拿当前步号去问：
    /// 切角色时可能正停在第 5 步（那边"不查类型"），算出来的档位会把这一步的结果写歪。
    /// </summary>
    private const int FoundationCheckStep = 1;

    public ObservableCollection<UnrealPublishFoundationCheckItem> FoundationChecks { get; } = [];

    private IReadOnlyList<UnrealPublishFoundationCheckItem> _visibleFoundationChecks = [];

    public IReadOnlyList<UnrealPublishFoundationCheckItem> VisibleFoundationChecks
    {
        get => _visibleFoundationChecks;
        private set => SetProperty(ref _visibleFoundationChecks, value);
    }

    private bool _hideCompletedFoundationChecks;

    public bool HideCompletedFoundationChecks
    {
        get => _hideCompletedFoundationChecks;
        set
        {
            if (SetProperty(ref _hideCompletedFoundationChecks, value))
            {
                RefreshVisibleFoundationChecks();
                SaveSessionCache();
            }
        }
    }

    public string FoundationSummaryText =>
        $"共 {FoundationChecks.Count} 项：合规 {FoundationChecks.Count(item => item.IsCompliant)}，"
        + $"待处理 {FoundationChecks.Count(item => !item.IsCompliant)}";

    // ── 工作区可见性（只有第 1 步是"底层检测"那一步时才显出来）───────────────

    public bool IsFoundationWorkspace => !IsEngineToToolbox && WorkflowStep == 1;

    public Visibility FoundationWorkspaceVisibility =>
        IsFoundationWorkspace && WorkspaceState == UnrealSyncWorkspaceState.HasContent
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility FoundationDetailsVisibility =>
        IsFoundationWorkspace ? Visibility.Visible : Visibility.Collapsed;

    // ── 刷新 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 重跑这一层的检查（壳在进入/重新加载第 1 步时调用）。
    ///
    /// **这一步的检查一律带资产类型**（晓桀 2026-09-24：「改成第一步就查蓝图类型」）。
    /// 类型来自磁盘上那份导出清单，**不跑 Unreal**，所以没有额外代价。
    ///
    /// 刻意**不收这个开关**：收着就会有人传 false，界面又退回那句占位串
    /// 「等待 Unreal 类型复检」——以前就是被几处默认值漏成这样。
    /// 校验强度（会不会因此抛错拦住流程）是另一个问题，由调用方各自决定，
    /// 见 <see cref="ValidatePublishCharacterFolders"/>。
    /// </summary>
    public void RefreshFoundationChecks(string characterCode)
    {
        FoundationChecks.Clear();
        foreach (var item in _syncService.CheckPublishCharacterFolders(
                     ProjectPath,
                     characterCode,
                     RequiresAssetTypesFor(FoundationCheckStep)))
        {
            FoundationChecks.Add(item);
        }

        RefreshVisibleFoundationChecks();
        OnPropertyChanged(nameof(FoundationSummaryText));
        OnPropertyChanged(nameof(CanAdvanceWorkflow));
        OnPropertyChanged(nameof(WorkflowNextButtonEnabled));
    }

    private void RefreshVisibleFoundationChecks()
    {
        VisibleFoundationChecks = FoundationChecks
            .Where(item => !HideCompletedFoundationChecks || !item.IsCompliant)
            .ToArray();
    }

    // ── 别处往这一步挂错误（第 4 步的基础配置检测用）─────────────────────────

    /// <summary>
    /// 第 4 步的基础配置检测发现某条依赖不对时，把这条**挂到第 1 步的检查列表**上
    /// （同名的那条先删掉再插），这样回到第 1 步就能看到"配置错误"。
    ///
    /// 它住在这一步的文件里，是因为它增删的就是**这一步的检查项** ——
    /// 虽然入参是第 4 步的结果类型，但按"一步一个文件"，改这一步状态的入口该在这一步；
    /// 调用方（壳里第 4 步那条路）照旧调用，不用知道它住哪。
    /// </summary>
    public void SetFoundationConfigurationError(UnrealLightConfigurationResultItem error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var previous = FoundationChecks.FirstOrDefault(item => item.DisplayName == error.DisplayName);
        if (previous is not null)
        {
            FoundationChecks.Remove(previous);
        }

        FoundationChecks.Add(new UnrealPublishFoundationCheckItem(
            error.DisplayName,
            string.IsNullOrWhiteSpace(error.TargetPath) ? "基础配置依赖" : error.TargetPath,
            string.Empty,
            false,
            "配置错误",
            ActualType: error.ErrorMessage));
        RefreshVisibleFoundationChecks();
        OnPropertyChanged(nameof(FoundationSummaryText));
        OnPropertyChanged(nameof(CanAdvanceWorkflow));
        OnPropertyChanged(nameof(WorkflowNextButtonEnabled));
    }

    // ── 自己的缓存文件 ────────────────────────────────────────────────────

    /// <summary>把第 1 步自己的小缓存回填到界面（内存里已经有检查项时不覆盖）。</summary>
    internal void TryApplyFoundationCache(CharacterCard? character)
    {
        if (character is null || FoundationChecks.Count > 0)
        {
            return;
        }

        var document = Step1FoundationCache.TryLoad(character, character.Code);
        if (document is null)
        {
            return;
        }

        // 这份缓存是"**不查资产类型**"那一档存的（改动前的默认值），而现在第 1 步要查
        // 蓝图类型（晓桀 2026-09-24：「改成第一步就查蓝图类型」）。
        // 直接当成"没查过"：进这一步时会重查一遍，否则界面会一直显示改动前那句
        // 占位串「等待 Unreal 类型复检」，看着像没改。
        if (document.RequireAssetTypes != RequiresAssetTypesFor(FoundationCheckStep))
        {
            return;
        }

        FoundationChecks.Clear();
        foreach (var item in document.Items)
        {
            FoundationChecks.Add(new UnrealPublishFoundationCheckItem(
                item.DisplayName,
                item.ExpectedPath,
                item.ActualPath,
                item.IsCompliant,
                item.Problem,
                item.ExpectedType,
                item.ActualType));
        }

        RefreshVisibleFoundationChecks();
        NotifyWorkflowStateChanged();
    }

    /// <summary>
    /// 把当前检查项写进第 1 步自己的缓存文件。
    ///
    /// 那个"要不要查类型"的开关**跟检查本身一起存**：存的时候是"不查"、读的时候要"查"，
    /// 就会被当成"查过了"，再也不补查（反过来也一样）。它必须和
    /// <see cref="RefreshFoundationChecks"/> 用的是同一个口径。
    /// </summary>
    internal bool SaveFoundationCache(CharacterCard? character) =>
        character is not null && Step1FoundationCache.Save(
            character,
            FoundationChecks.ToArray(),
            RequiresAssetTypesFor(FoundationCheckStep));
}
