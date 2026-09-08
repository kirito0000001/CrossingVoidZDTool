using System;
using System.Linq;
using Microsoft.UI.Xaml;

namespace CrossingVoidZDTool.ViewModels;

/// <summary>
/// 中栏的统一状态。
///
/// 六步各自的列表还是各自的模板，但「什么时候显示列表、什么时候显示占位」
/// 由这里一处决定。以前每步各写一组可见性条件，
/// 「已加载」和「有内容」两个条件之间漏掉的那块就是空白面板。
/// </summary>
internal sealed partial class UnrealProjectSyncViewModel
{
    private string _workspaceFailure = string.Empty;

    /// <summary>当前步骤的中栏状态。</summary>
    public UnrealSyncWorkspaceState WorkspaceState
    {
        get
        {
            if (IsEngineToToolbox)
            {
                // 导入方向没有分步流程，只有「检测出的差异树」一种内容。
                return SelectedSource is null
                    ? UnrealSyncWorkspaceState.NoCharacter
                    : !string.IsNullOrEmpty(_workspaceFailure)
                        ? UnrealSyncWorkspaceState.Failed
                        : _isWorkflowOperationRunning
                            ? UnrealSyncWorkspaceState.Busy
                            : !_hasImportDetection
                                ? UnrealSyncWorkspaceState.NotDetected
                                : SelectionTreeRoots.Count == 0
                                    ? UnrealSyncWorkspaceState.NoChanges
                                    : UnrealSyncWorkspaceState.HasContent;
            }

            if (!string.IsNullOrEmpty(_workspaceFailure))
            {
                return UnrealSyncWorkspaceState.Failed;
            }
            // 正在跑的时候一律是忙碌态：这一步的旧数据可能已经被清掉了，
            // 继续按旧数据显示会让人以为检测没开始。
            if (_isWorkflowOperationRunning)
            {
                return UnrealSyncWorkspaceState.Busy;
            }
            // 这一步已经有数据就按数据说话。放在选角色判断之前是刻意的：
            // 手里明明有检测结果却显示「尚未选择角色」，比显示结果更让人困惑。
            if (!IsWorkflowStepLoaded(WorkflowStep))
            {
                return SelectedSource?.DraftCharacter is null
                    ? UnrealSyncWorkspaceState.NoCharacter
                    : UnrealSyncWorkspaceState.NotDetected;
            }

            return WorkflowStep switch
            {
                // 第一、二步的列表本身就是内容，加载过就有东西看。
                1 => FoundationChecks.Count == 0
                    ? UnrealSyncWorkspaceState.NoChanges
                    : UnrealSyncWorkspaceState.HasContent,
                2 => NormalizationItems.Count == 0
                    ? UnrealSyncWorkspaceState.NoChanges
                    : UnrealSyncWorkspaceState.HasContent,
                3 or 5 => SelectionTreeRoots.Count == 0
                    ? UnrealSyncWorkspaceState.NoChanges
                    : UnrealSyncWorkspaceState.HasContent,
                4 => LightConfigurationItems.Count == 0
                    ? UnrealSyncWorkspaceState.NoChanges
                    : UnrealSyncWorkspaceState.HasContent,
                6 => BlueprintSetupItems.Count == 0
                    ? UnrealSyncWorkspaceState.NoChanges
                    : UnrealSyncWorkspaceState.HasContent,
                _ => UnrealSyncWorkspaceState.NoChanges,
            };
        }
    }

    /// <summary>这一步在界面上的名字，占位文案里用。</summary>
    public string WorkflowStepName => IsEngineToToolbox ? "导入差异" : WorkflowStep switch
    {
        1 => "底层检测",
        2 => "规整素材",
        3 => "同步素材",
        4 => "基础配置",
        5 => "序列同步",
        6 => "蓝图置入",
        _ => "同步结果",
    };

    /// <summary>各步自己的列表；只有内容态才显示。</summary>
    public Visibility WorkspaceContentVisibility =>
        WorkspaceState == UnrealSyncWorkspaceState.HasContent ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>其余状态统一走占位面板，不会再出现没人认领的空白。</summary>
    public Visibility WorkspacePlaceholderVisibility =>
        WorkspaceState == UnrealSyncWorkspaceState.HasContent ? Visibility.Collapsed : Visibility.Visible;

    public Visibility WorkspaceBusyVisibility =>
        WorkspaceState == UnrealSyncWorkspaceState.Busy ? Visibility.Visible : Visibility.Collapsed;

    private UnrealSyncWorkspacePlaceholder Placeholder => UnrealSyncWorkspacePlaceholder.For(
        WorkspaceState, WorkflowStep, WorkflowStepName, WorkspacePlaceholderDetail);

    public string WorkspacePlaceholderGlyph => Placeholder.Glyph;
    public string WorkspacePlaceholderTitle => Placeholder.Title;
    public string WorkspacePlaceholderDescription => Placeholder.Description;
    public bool IsWorkspacePlaceholderError => Placeholder.IsError;

    /// <summary>占位面板的补充说明，优先用这一步自己的摘要。</summary>
    private string WorkspacePlaceholderDetail
    {
        get
        {
            if (WorkspaceState == UnrealSyncWorkspaceState.Failed)
            {
                return _workspaceFailure;
            }
            if (WorkspaceState != UnrealSyncWorkspaceState.NoChanges)
            {
                return string.Empty;
            }

            return WorkflowStep switch
            {
                1 => FoundationSummaryText,
                2 => NormalizationSummaryText,
                3 or 5 => DetectionResultSummaryText,
                4 => LightConfigurationSummaryText,
                6 => BlueprintSetupSummaryText,
                _ => string.Empty,
            };
        }
    }

    /// <summary>记下这一步的失败原因，中栏据此显示错误态。</summary>
    public void SetWorkspaceFailure(string message)
    {
        _workspaceFailure = message ?? string.Empty;
        NotifyWorkspaceStateChanged();
    }

    /// <summary>这一步重新开始检测或加载成功时清掉失败态。</summary>
    public void ClearWorkspaceFailure()
    {
        if (_workspaceFailure.Length == 0)
        {
            return;
        }

        _workspaceFailure = string.Empty;
        NotifyWorkspaceStateChanged();
    }

    public void NotifyWorkspaceStateChanged()
    {
        OnPropertyChanged(nameof(WorkspaceState));
        OnPropertyChanged(nameof(WorkflowStepName));
        OnPropertyChanged(nameof(WorkspaceContentVisibility));
        OnPropertyChanged(nameof(WorkspacePlaceholderVisibility));
        OnPropertyChanged(nameof(WorkspaceBusyVisibility));
        OnPropertyChanged(nameof(WorkspacePlaceholderGlyph));
        OnPropertyChanged(nameof(WorkspacePlaceholderTitle));
        OnPropertyChanged(nameof(WorkspacePlaceholderDescription));
        OnPropertyChanged(nameof(IsWorkspacePlaceholderError));
    }
}
