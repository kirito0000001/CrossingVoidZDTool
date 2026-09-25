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
    public UnrealSyncWorkspaceState WorkspaceState =>
        UnrealSyncWorkflowState.ResolveWorkspaceState(BuildWorkflowInputs());

    /// <summary>
    /// 把 ViewModel 里散着的字段和集合折成投影的输入。
    ///
    /// 这是 P5「只读投影」那一半：规则搬进 <see cref="UnrealSyncWorkflowState"/> 之后，
    /// 这里只剩「读现状」。字段本身还没搬进状态对象（那是下一批），
    /// 但改规则从此只改那一个纯函数，而且能被单测钉住——
    /// 以前这段六十行嵌套三元只能靠跑起界面去看。
    /// </summary>
    internal UnrealSyncWorkflowInputs BuildWorkflowInputs() => new(
        IsImportDirection: IsEngineToToolbox,
        HasSelectedSource: SelectedSource is not null,
        HasSelectedCharacter: SelectedSource?.DraftCharacter is not null,
        CurrentStep: WorkflowStep,
        HasFailure: _workspaceFailure.Length > 0,
        IsOperationRunning: IsWorkflowOperationRunning,
        Step1Loaded: FoundationChecks.Count > 0,
        Step1ItemCount: FoundationChecks.Count,
        // 「这一步加载过没有」一律问**步加载表**（和第三/五步同一个口径）。
        // ⚠️ 第 2 步以前挂的是旧的规整标志 `IsNormalizationStepLoaded`，而它会被
        // `RestoreWorkflowStepCache` 拿**会话缓存**重设 —— 缓存里没有那几项时就变 false，
        // 于是"退回第 2 步"显示成「尚未检测同步素材」（2026-09-24 实测踩到）。
        Step2Loaded: _stepLoads.IsLoaded(2),
        // 喂给状态机的是**还有待处理的**规整项数量，不是列表总数。
        // ⚠️ 用总数会出事：规整项都处理完了（`IsAlreadyNormalized`）时列表仍非空，
        // 状态会被判成「有内容」→ 去渲染差异树 → 而树里一条都没有 → **中栏一片空白**。
        // （2026-09-24 实测踩到：素材无差异时中栏什么都不显示。）
        // 用 `!IsAlreadyNormalized` 而不是 `VisibleNormalizationItems`：后者受"隐藏已处理"
        // 开关影响，一勾就会把状态翻成"无差异"，那是界面开关不该有的副作用。
        Step2ItemCount: NormalizationItems.Count(item => !item.IsAlreadyNormalized),
        Step3Loaded: _stepLoads.IsLoaded(3),
        Step4Loaded: _stepLoads.IsLoaded(4),
        SharedTreeItemCount: SelectionTreeRoots.Count,
        // 第四步的徽标要看"还剩几条差异"：全是 Unchanged 才算完成。
        // 用 `_lastPublishChanges`（检测出来的那份完整差异列表），
        // 和 `HasNoPublishChanges`、和发布前那条"没差异就别同步"同一个口径。
        Step4PendingCount: _lastPublishChanges.Count(change =>
            change.Kind != UnrealBridgeChangeKind.Unchanged),
        Step3ItemCount: LightConfigurationItems.Count,
        Step3ErrorCount: LightConfigurationErrorCount,
        Step3PendingCount: LightConfigurationPendingCount,
        Step5Loaded: IsBlueprintSetupLoaded,
        Step5ItemCount: BlueprintSetupItems.Count,
        Step5ErrorCount: BlueprintSetupErrorCount,
        Step5PendingCount: BlueprintSetupPendingCount,
        // 第 6 步「特效同步」：自己的加载标志 + 自己的动作清单条数。
        Step6Loaded: IsEffectSyncLoaded,
        Step6ItemCount: EffectSyncItems.Count,
        HasDetectionRun: HasImportDetection);

    /// <summary>这一步在界面上的名字，占位文案里用。</summary>
    public string WorkflowStepName => IsEngineToToolbox ? "导入差异" : WorkflowStepNameFor(WorkflowStep);

    /// <summary>
    /// 任意一步的名字。日志的步骤标题行要按步号取（写「离开第二步」时，
    /// 当前步已经是第三步了），所以不能只看 <see cref="WorkflowStepName"/>。
    /// </summary>
    public string WorkflowStepNameFor(int step) => step switch
    {
        1 => "底层检测",
        // 第 2 步 = 合并后的「同步素材」（规整 + 素材同步）。
        2 => "同步素材",
        3 => "基础配置",
        4 => "序列同步",
        5 => "蓝图置入",
        6 => "特效同步",
        _ => "同步结果",
    };

    /// <summary>
    /// 某一步的结论，日志里「■ 第 N 步 · 结束：…」直接用这一句。
    /// 复用各步**已经存在**的摘要文案，不另造一套说法。
    /// </summary>
    public string WorkflowStepConclusionText(int step) => step switch
    {
        1 => FoundationSummaryText,
        // 第 2 步是合并后的「同步素材」：**规整和素材差异都属于它**。
        // 以前 `2 =>` 挡在 `2 or 4 =>` 前面，第 2 步的结束行永远只报规整那半，
        // 差异摘要被静默吞掉（2026-09-24 体检）。两段都要，空的那段跳过、中间用「；」。
        2 => JoinConclusion(NormalizationSummaryText, DetectionResultSummaryText),
        4 => DetectionResultSummaryText,
        3 => LightConfigurationSummaryText,
        5 => BlueprintSetupSummaryText,
        _ => string.Empty,
    };

    /// <summary>
    /// 两段摘要拼一句（空的跳过、中间用「；」）。第 2 步是**合并步**，
    /// 规整摘要和素材差异摘要都属于它，所以它的结论行得给两段。
    /// </summary>
    private static string JoinConclusion(string first, string second) =>
        string.IsNullOrWhiteSpace(first) ? second
            : string.IsNullOrWhiteSpace(second) ? first
                : $"{first}；{second}";

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
                // 第 2 步是合并后的「同步素材」：**规整摘要和差异摘要两样都属于它**。
                // 有要规整的项时两样都报，没有就只报差异（否则会多出一句
                // "没有需要规整的 Unreal 素材"，看着像故障）。
                2 => HasVisibleNormalizationItems
                    ? $"{NormalizationSummaryText}　{DetectionResultSummaryText}"
                    : DetectionResultSummaryText,
                4 => DetectionResultSummaryText,
                3 => LightConfigurationSummaryText,
                5 => BlueprintSetupSummaryText,
                // 第六步「特效同步」：它没有差异树，摘要就是自己的动作清单。
                6 => EffectSyncSummaryText,
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
        // 第六步「特效同步」自己那块也必须在这里补一句 —— 漏掉它就是下面注释说的那个
        // 状态：检测时先写结果（那一刻还在忙碌态 → 面板收起），收尾才变成内容态；
        // 面板若没被重新通知，就停在收起上，而占位面板按内容态又是收起的 → **中栏一片空白**。
        // （2026-09-25 实测踩到：第六步进去什么都没有。）
        OnPropertyChanged(nameof(EffectSyncWorkspaceVisibility));
        OnPropertyChanged(nameof(EffectSyncDetailsVisibility));
        OnPropertyChanged(nameof(WorkspacePlaceholderGlyph));
        OnPropertyChanged(nameof(WorkspacePlaceholderTitle));
        OnPropertyChanged(nameof(WorkspacePlaceholderDescription));
        OnPropertyChanged(nameof(IsWorkspacePlaceholderError));
        // 勾选统计跟着步骤和内容一起变，两边总是同时失效。
        NotifyStepSelectionChanged();
    }
}
