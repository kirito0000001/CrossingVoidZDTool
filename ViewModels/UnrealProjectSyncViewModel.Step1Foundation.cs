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

    /// <summary>重跑这一层的检查（壳在进入/重新加载第 1 步时调用）。</summary>
    public void RefreshFoundationChecks(string characterCode, bool requireAssetTypes = false)
    {
        FoundationChecks.Clear();
        foreach (var item in _syncService.CheckPublishCharacterFolders(ProjectPath, characterCode, requireAssetTypes))
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

    /// <summary>把当前检查项写进第 1 步自己的缓存文件。</summary>
    internal bool SaveFoundationCache(CharacterCard? character, bool requireAssetTypes = false) =>
        character is not null && Step1FoundationCache.Save(character, FoundationChecks.ToArray(), requireAssetTypes);
}
