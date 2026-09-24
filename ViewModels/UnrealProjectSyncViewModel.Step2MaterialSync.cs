using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;
using Microsoft.UI.Xaml;

namespace CrossingVoidZDTool.ViewModels;

/// <summary>
/// 第二步「同步素材」自己的那一块：**发布阶段 + 差异树 + 勾选计数**。
///
/// 同前两步的规矩（一步一个文件）：这里的成员只服务第二步，不再替别的步骤保管状态。
/// 第五/七步只是**复用同一块工作区界面**，它们的计划由各自入口生成，不从这里取。
/// </summary>
internal sealed partial class UnrealProjectSyncViewModel
{
    // ── 发布阶段（第二步要挑"这次同步哪一类"）──────────────────────────────

    public ObservableCollection<UnrealSyncPublishStageItem> PublishStages { get; } =
    [
        new(UnrealBridgePublishStage.CharacterMaterials, "1. 导入素材", "角色目录内的图片和声音", true),
        new(UnrealBridgePublishStage.ItemData, "2. 同步 Item 数据", "更新 Item 蓝图中的工具箱管理字段", false),
        new(UnrealBridgePublishStage.ZdAnimationTracks, "3. 同步 ZD 动画轨道", "合并基础序列、AnimMaps 引用和保留通知", false),
        new(UnrealBridgePublishStage.Buffs, "4. 同步 BUFF", "更新 BUFF 数据和个人 BUFF 素材", false),
        new(UnrealBridgePublishStage.CharacterBlueprint, "5. 同步 Character 蓝图数据", "更新 Character 蓝图白名单字段", false)
    ];

    private UnrealSyncPublishStageItem? _selectedPublishStage;

    public UnrealSyncPublishStageItem? SelectedPublishStage
    {
        get => _selectedPublishStage;
        set
        {
            if (SetProperty(ref _selectedPublishStage, value))
            {
                HasImportDetection = false;
                _loadedPublishStep = 0;
                OnPropertyChanged(nameof(HasContentDetection));
                OnPropertyChanged(nameof(WorkflowStep4StatusText));
                OnPropertyChanged(nameof(WorkflowStep5StatusText));
                SetSelectionTree([]);
                ResetImportOperation();
                OnPropertyChanged(nameof(PublishStageDescription));
                SaveSessionCache();
            }
        }
    }

    public string PublishStageDescription => SelectedPublishStage?.DetailText ?? "请选择要执行的同步阶段。";

    // ── 差异树 + 勾选计数 ────────────────────────────────────────────────

    public ObservableCollection<UnrealSyncSelectionTreeItem> SelectionTreeRoots { get; } = [];

    public int PendingRedirectCount => SelectionTreeRoots.SelectMany(root => root.Children)
        .Count(item => item.IsChecked == true && item.Change is not null &&
            item.Change.Kind != UnrealBridgeChangeKind.Conflict && !CanExecutePublishChange(item.Change));

    public int PublishConflictCount => SelectionTreeRoots.SelectMany(root => root.Children)
        .Count(item => item.IsChecked == true && item.Change?.Kind == UnrealBridgeChangeKind.Conflict);

    public int ReadyPublishCount => SelectionTreeRoots.SelectMany(root => root.Children)
        .Count(item => item.IsChecked == true && item.Change is not null && CanExecutePublishChange(item.Change));

    public string ReadyPublishText => $"可同步：{ReadyPublishCount}";
    public string PendingRedirectText => $"待重定向：{PendingRedirectCount}";
    public string PublishConflictText => $"冲突：{PublishConflictCount}";

    // ── 过滤器（"全部 / 待重定向 / 可同步 / 冲突"）──────────────────────────

    private string _publishFilter = "全部";

    public ObservableCollection<string> PublishFilterOptions { get; } = ["全部", "待重定向", "可同步", "冲突"];

    public string PublishFilter
    {
        get => _publishFilter;
        set
        {
            if (SetProperty(ref _publishFilter, value))
            {
                ApplyPublishFilter();
            }
        }
    }

    private void ApplyPublishFilter()
    {
        foreach (var root in SelectionTreeRoots)
        {
            root.SetVisibleChildren(root.Children.Where(ShouldShowPublishChild));
        }
    }

    private bool ShouldShowPublishChild(UnrealSyncSelectionTreeItem item)
    {
        if (IsEngineToToolbox || PublishFilter == "全部") return true;
        if (PublishFilter == "待重定向") return item.Change is not null && item.Change.Kind != UnrealBridgeChangeKind.Conflict && !CanExecutePublishChange(item.Change);
        if (PublishFilter == "冲突") return item.Change?.Kind == UnrealBridgeChangeKind.Conflict;
        return item.Change is not null && CanExecutePublishChange(item.Change);
    }

    // ── 可用性判定 ────────────────────────────────────────────────────────

    public bool IsPublishSelectionReady
    {
        get
        {
            if (IsEngineToToolbox || !HasImportDetection)
            {
                return CanImportSelection;
            }

            var selected = SelectionTreeRoots.SelectMany(root => root.Children)
                .Where(item => item.IsChecked == true && item.Change is not null)
                .Select(item => item.Change!)
                .ToArray();
            if (selected.Length == 0)
            {
                return false;
            }

            return selected.All(change =>
                UnrealBridgePublishSupportPolicy.CanExecute(change) ||
                change.Kind == UnrealBridgeChangeKind.DeleteCandidate &&
                NormalizationItems.Any(item =>
                    item.Decision == UnrealAssetNormalizationDecision.Redirect &&
                    string.Equals(item.UnrealObjectPath, change.UnrealItem?.SourceObjectPath, StringComparison.OrdinalIgnoreCase)));
        }
    }

    public bool CanExecutePublishChange(UnrealBridgeChange change)
    {
        if (UnrealBridgePublishSupportPolicy.CanExecute(change))
        {
            return true;
        }

        return change.Kind == UnrealBridgeChangeKind.DeleteCandidate &&
            NormalizationItems.Any(item =>
                item.Decision == UnrealAssetNormalizationDecision.Redirect &&
                string.Equals(item.UnrealObjectPath, change.UnrealItem?.SourceObjectPath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>第二步/第四步"没有任何差异可同步"时，界面要给出"下一步"而不是"同步"。</summary>
    public bool HasNoPublishChanges => !IsEngineToToolbox && WorkflowStep is 2 or 4 &&
        HasImportDetection &&
        _lastPublishChanges.All(change => change.Kind == UnrealBridgeChangeKind.Unchanged);

    public string PublishActionText => WorkflowStep switch
    {
        4 => "同步序列到虚幻",
        6 => "同步特效到虚幻",
        _ => "同步到虚幻"
    };

    // ── 可见性 ────────────────────────────────────────────────────────────

    public Visibility WorkflowConfirmationVisibility => WorkflowStep is 2 or 4 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SelectionContentVisibility =>
        !IsFoundationWorkspace && !IsLightConfigurationWorkspace &&
        !IsBlueprintSetupWorkspace && WorkspaceState == UnrealSyncWorkspaceState.HasContent
            ? Visibility.Visible
            : Visibility.Collapsed;

    // ── 差异树：建 / 过滤 / 取勾选 / 恢复 ────────────────────────────────

    /// <summary>
    /// 换一棵差异树。订阅/退订都在这里做——导入方向和发布方向共用它，
    /// 所以这个成员不归某一步独占，但仍和树一起放，免得"谁订了事件"散在两处。
    /// </summary>
    public void SetSelectionTree(IEnumerable<UnrealSyncSelectionTreeItem> roots)
    {
        foreach (var root in SelectionTreeRoots)
        {
            root.GroupSelectionChanged -= SelectionGroup_GroupSelectionChanged;
        }
        foreach (var child in SelectionTreeRoots.SelectMany(root => root.Children))
        {
            child.PropertyChanged -= ImportSelectionItem_PropertyChanged;
        }

        SelectionTreeRoots.Clear();
        _selectionParents.Clear();
        foreach (var root in roots)
        {
            SelectionTreeRoots.Add(root);
            root.GroupSelectionChanged += SelectionGroup_GroupSelectionChanged;
            foreach (var child in root.Children)
            {
                _selectionParents[child] = root;
                child.PropertyChanged += ImportSelectionItem_PropertyChanged;
            }
        }

        OnPropertyChanged(nameof(SelectionContentVisibility));
        UpdateImportSelectionSummary();
        ApplyPublishFilter();
    }

    public void SetPublishSelectionTree(
        IEnumerable<UnrealSyncSelectionTreeItem> roots,
        IReadOnlyCollection<UnrealBridgeChange>? changes = null)
    {
        _existingImportStableIds.Clear();
        HasImportDetection = true;
        // 默认按当前步骤认领这棵树。检测流程会在建完树、切到目标步骤之前
        // 用 SetLoadedPublishStep 覆盖成真正的目标步骤；这里只是保证
        // 视图模型单独使用时也是自洽的，不会出现「有树但没人认领」。
        if (WorkflowStep is 2 or 4)
        {
            _loadedPublishStep = WorkflowStep;
        }
        ClearWorkspaceFailure();
        OnPropertyChanged(nameof(HasContentDetection));
        OnPropertyChanged(nameof(WorkflowStep4StatusText));
        OnPropertyChanged(nameof(WorkflowStep5StatusText));
        ImportOperationTitle = "差异检测完成";
        ImportOperationMessage = "展开中间分类并确认本次需要同步的内容。";
        ImportDetailVisibility = Visibility.Visible;
        ImportResultVisibility = Visibility.Collapsed;
        ImportResultMessage = string.Empty;
        var rootList = roots.ToArray();
        if (changes is not null)
        {
            SetPublishDetectionSummary(changes);
        }
        ApplyPublishDisplay(rootList);
        SetSelectionTree(rootList);
        _lastPublishChanges = changes?.ToList() ?? SelectionTreeRoots.SelectMany(root => root.Children)
            .Where(item => item.Change is not null)
            .Select(item => item.Change!)
            .ToList();
        OnPropertyChanged(nameof(HasNoPublishChanges));
        OnPropertyChanged(nameof(WorkflowNextButtonEnabled));
        OnPropertyChanged(nameof(CanStartPublish));
        OnPropertyChanged(nameof(PublishActionText));
        _lastContentDetectionAt = DateTimeOffset.Now;
        OnPropertyChanged(nameof(ContentDetectionStatusText));
        SaveSessionCache();
    }

    public async Task SetPublishSelectionTreeAsync(
        IReadOnlyCollection<UnrealBridgeChange> changes,
        Func<UnrealBridgeChange, bool>? canExecute = null,
        CancellationToken cancellationToken = default,
        bool selectPendingByDefault = true)
    {
        var roots = await Task.Run(
            () => WorkflowStep == 4 || SelectedPublishStage?.Stage == UnrealBridgePublishStage.ZdAnimationTracks
                ? UnrealSyncSelectionTreeBuilder.FromSequenceChanges(changes, canExecute, selectPendingByDefault)
                : UnrealSyncSelectionTreeBuilder.FromChanges(changes, canExecute, selectPendingByDefault),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        SetPublishSelectionTree(roots, changes);
    }

    public IReadOnlyList<UnrealBridgeChange> FilterPublishChanges(IReadOnlyList<UnrealBridgeChange> changes)
    {
        // **先按当前步骤判，再按（从会话缓存恢复的）发布阶段判。**
        //
        // 顺序颠倒过一次，后果是：第四步跑过之后 SelectedPublishStage 停在 ZdAnimationTracks 并被持久化，
        // 回到第二步时上面那个 `||` 立刻成立 —— 素材变更被当成序列变更过滤，整批丢掉，
        // 界面显示"共检查 0 项 · 无差异 0 项"，真该报的新增（幻形立绘 #2 / 失败语音 #1）全被吞掉。
        // （2026-09-24 实测，用户看到的正是这个。）
        // 序列那一路：第五/七步，**或者**阶段明确停在"序列动画轨道"（会话缓存恢复时步骤可能还没落定，只有阶段可信）。
        // 但**第二步除外**：那一步要的是素材，残留的序列阶段不能在这里生效。
        if (WorkflowStep is 4 or 6 ||
            (WorkflowStep != 2 &&
             SelectedPublishStage?.Stage == UnrealBridgePublishStage.ZdAnimationTracks))
        {
            return changes.Where(change => change.Module == UnrealBridgeModule.SequenceFrames).ToArray();
        }

        if (WorkflowStep == 2 || SelectedPublishStage?.Stage == UnrealBridgePublishStage.CharacterMaterials)
        {
            return changes
                .Where(change => change.Module is UnrealBridgeModule.BaseMaterials or UnrealBridgeModule.Voices)
                .ToArray();
        }

        // 其它阶段（ItemData / Buffs / 角色蓝图…）各有自己的界面，这棵素材树不由它们过滤。
        return [];
    }

    public IReadOnlySet<string> GetSelectedStableIds() =>
        UnrealSyncSelectionTreeBuilder.SelectedStableIds(SelectionTreeRoots);

    public IReadOnlySet<string> GetSelectedGroupAndLeafStableIds() =>
        UnrealSyncSelectionTreeBuilder.SelectedGroupAndLeafStableIds(SelectionTreeRoots);

    /// <summary>按上一次的勾选恢复整棵树，全程只做一次汇总与写盘。</summary>
    public void RestoreSelectionState(IReadOnlySet<string> selectedStableIds)
    {
        using var scope = BeginBulkSelectionUpdate();
        foreach (var root in SelectionTreeRoots)
        {
            root.RestoreCheckedState(selectedStableIds);
        }

        _bulkSelectionUpdatePending = true;
    }

    /// <summary>
    /// 勾选状态变化后重新写一次会话缓存。SetPublishSelectionTree 保存的是重建后的默认态，
    /// 调用方恢复用户勾选之后需要再存一次，缓存里才是真实勾选。
    /// </summary>
    public void SaveSelectionStateToSessionCache()
    {
        SaveSessionCache();
        FlushSessionCache();
    }

    // ── 自己的缓存文件（`step2-material-sync.json`）────────────────────────
    //
    // 和第四步那三个方法一一对应（那边落 `step4-sequence-sync.json`）。
    // 本步的**规整决策**另有一份 `step2-normalization.json`（那是用户数据，不随算法换代失效）；
    // 这里装的是**检测结果 + 勾选**，所以带差异算法版本、对不上就当没缓存。

    /// <summary>把当前的素材差异 + 勾选写进第 2 步自己的缓存文件。</summary>
    /// ⚠️ **树不归这一步就别写**：`WriteAllStepCaches` 每次防抖都会调它，
    /// 而此时内存里可能是空的检测结果 —— 写下去就把上一份有效文件盖没了（2026-09-24）。
    internal bool SaveMaterialSyncCache(CharacterCard? character) =>
        character is not null &&
        _loadedPublishStep == 2 &&
        Step2MaterialSyncCache.Save(
            character,
            _lastPublishChanges,
            GetSelectedStableIds(),
            GetSelectedGroupAndLeafStableIds(),
            CurrentDetectionAlgorithmVersion);


    /// <summary>
    /// 把第 2 步自己的小缓存回填成差异树（内存里已经有一棵树时不覆盖）。
    /// 返回 true 表示确实用文件里的结果建好了树。
    /// </summary>
    internal bool TryApplyMaterialSyncCache(CharacterCard? character)
    {
        if (character is null || HasImportDetection)
        {
            return false;
        }

        var document = Step2MaterialSyncCache.TryLoad(
            character,
            character.Code,
            CurrentDetectionAlgorithmVersion);
        if (document is null || document.Changes.Length == 0)
        {
            return false;
        }

        // 过滤口径要和检测时一致，否则会拿别步的变更去建第 2 步的树。
        var changes = FilterPublishChanges(document.Changes).ToArray();
        if (changes.Length == 0)
        {
            return false;
        }

        // 恢复期间必须屏蔽写盘：`SetLoadedPublishStep(2)` 会顺手把这一步的小缓存存一次，
        // 而那一刻 `_lastPublishChanges` 还是上一次的（树还没建），存下去就是把好缓存覆盖成旧的。
        var wasRestoring = _isRestoringSession;
        _isRestoringSession = true;
        try
        {
            SetLoadedPublishStep(2);
            var roots = UnrealSyncSelectionTreeBuilder.FromChanges(
                changes,
                UnrealBridgePublishSupportPolicy.CanExecute,
                selectPendingByDefault: SelectsPendingChangesByDefault(2));
            var restoreIds = document.SelectedStableIds
                .Union(document.SelectedGroupStableIds, StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            ApplySelection(roots, restoreIds);
            SetPublishSelectionTree(roots, changes);
        }
        finally
        {
            _isRestoringSession = wasRestoring;
        }

        return true;
    }
}
