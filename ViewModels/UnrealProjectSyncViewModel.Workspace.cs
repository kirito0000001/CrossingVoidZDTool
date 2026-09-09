using System;
using System.Collections.Generic;
using System.Collections.Specialized;
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
    private (UnrealSyncWorkspaceState State, string StepName, string Title, string Description)?
        _lastNotifiedWorkspaceSnapshot;

    /// <summary>
    /// 本类自己广播出去的属性名。监听器要跳过它们，否则会自己触发自己。
    /// </summary>
    private static readonly HashSet<string> SelfNotifiedProperties = new(StringComparer.Ordinal)
    {
        nameof(WorkspaceState),
        nameof(WorkflowStepName),
        nameof(WorkspaceContentVisibility),
        nameof(WorkspacePlaceholderVisibility),
        nameof(WorkspaceBusyVisibility),
        nameof(FoundationWorkspaceVisibility),
        nameof(LightConfigurationWorkspaceVisibility),
        nameof(BlueprintSetupWorkspaceVisibility),
        nameof(SelectionContentVisibility),
        nameof(IsNormalizationWorkspace),
        nameof(WorkspacePlaceholderGlyph),
        nameof(WorkspacePlaceholderTitle),
        nameof(WorkspacePlaceholderDescription),
        nameof(IsWorkspacePlaceholderError),
    };

    /// <summary>
    /// 让中栏自己盯着它依赖的数据，而不是指望每个改数据的地方都记得通知。
    ///
    /// 中栏空白反复出现就是因为这个：状态由六步的集合和几个标志一起算出来，
    /// 只要有一条改数据的路径忘了通知，面板就停在上一刻的可见性上——
    /// 占位和内容同时收起，中栏一片空白。挂上监听之后，
    /// 以后再多加一步、多一条清空路径，都不用记得补通知。
    /// </summary>
    private void AttachWorkspaceWatchers()
    {
        foreach (var collection in new INotifyCollectionChanged[]
                 {
                     FoundationChecks,
                     NormalizationItems,
                     SelectionTreeRoots,
                     LightConfigurationItems,
                     BlueprintSetupItems,
                 })
        {
            collection.CollectionChanged += (_, _) => NotifyWorkspaceStateIfChanged();
        }

        PropertyChanged += (_, e) =>
        {
            // 这里原本是白名单，只认三个属性名。但中栏状态实际读了十一个输入
            // （失败原因、是否正在跑、导入检测标志、四个步骤加载标志、
            // 当前来源的角色……），白名单外的那些只能继续靠手工通知——
            // 「重置导入操作后中栏不刷新」就是这么漏的。
            //
            // 改成黑名单：除了本函数自己广播出去的那些派生属性，其余一律重算。
            // 重算本身很便宜（就是读几个字段），而且下面有值相等守卫兜着，
            // 状态没变就不会惊动界面。宁可多算，也不要再漏。
            if (e.PropertyName is not null && !SelfNotifiedProperties.Contains(e.PropertyName))
            {
                NotifyWorkspaceStateIfChanged();
            }
        };
    }

    /// <summary>状态真的变了才惊动界面，批量填充列表时不至于刷成百上千次。</summary>
    private void NotifyWorkspaceStateIfChanged()
    {
        // 只比枚举是不够的：同样是「无差异」态，占位面板的说明文字会随各步摘要变，
        // 步骤名也会随步号变。只比状态会把这些变化一起吞掉。
        var snapshot = (WorkspaceState, WorkflowStepName, WorkspacePlaceholderTitle, WorkspacePlaceholderDescription);
        if (_lastNotifiedWorkspaceSnapshot == snapshot)
        {
            return;
        }

        NotifyWorkspaceStateChanged();
    }

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
        _lastNotifiedWorkspaceSnapshot = (WorkspaceState, WorkflowStepName, WorkspacePlaceholderTitle, WorkspacePlaceholderDescription);
        OnPropertyChanged(nameof(WorkspaceState));
        OnPropertyChanged(nameof(WorkflowStepName));
        OnPropertyChanged(nameof(WorkspaceContentVisibility));
        OnPropertyChanged(nameof(WorkspacePlaceholderVisibility));
        OnPropertyChanged(nameof(WorkspaceBusyVisibility));
        // 各步自己的内容面板现在也由 WorkspaceState 决定显不显示，必须一起通知。
        //
        // 漏掉它们会漏出一个中栏全白的状态：检测结束时的顺序是
        // 「先写入结果（此时操作还没结束 -> 忙碌态 -> 内容面板收起）」，
        // 再「结束操作 -> 变成内容态 -> 占位面板收起」。如果这一步没有重新
        // 通知内容面板，它就停在忙碌态那一刻的 Collapsed 上——占位和内容
        // 双双隐藏，中栏一片空白，而右栏的计数看着一切正常。
        OnPropertyChanged(nameof(FoundationWorkspaceVisibility));
        OnPropertyChanged(nameof(LightConfigurationWorkspaceVisibility));
        OnPropertyChanged(nameof(BlueprintSetupWorkspaceVisibility));
        OnPropertyChanged(nameof(SelectionContentVisibility));
        OnPropertyChanged(nameof(IsNormalizationWorkspace));
        OnPropertyChanged(nameof(WorkspacePlaceholderGlyph));
        OnPropertyChanged(nameof(WorkspacePlaceholderTitle));
        OnPropertyChanged(nameof(WorkspacePlaceholderDescription));
        OnPropertyChanged(nameof(IsWorkspacePlaceholderError));
        // 勾选统计跟着步骤和内容一起变，两边总是同时失效。
        NotifyStepSelectionChanged();
    }
}
