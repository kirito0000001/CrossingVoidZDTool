using System.Collections.ObjectModel;
using System.Collections.Generic;
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using CrossingVoidZDTool.Services;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;

namespace CrossingVoidZDTool.ViewModels;

internal sealed partial class UnrealProjectSyncViewModel : ObservableObject
{
    // 5：资产类名不再把 TopLevelAssetPath 的对象内存地址带进内容哈希。
    // 旧缓存里 ownedAssets 的哈希掺了指针，和新导出的永远对不上，
    // 必须整份作废重新检测，不能拿来做同步前比对。
    private const int CurrentDetectionAlgorithmVersion = 5;
    private readonly UnrealProjectSyncService _syncService;
    private string _enginePath = string.Empty;
    private string _projectPath = string.Empty;
    private string _contentPath = string.Empty;
    private string _targetBaseMaterialContentPath = UnrealProjectSyncService.TargetBaseMaterialContentPath;
    private string _targetZdContentPath = UnrealProjectSyncService.TargetZdContentPath;
    private string _targetCharacterItemContentPath = UnrealProjectSyncService.TargetCharacterItemContentPath;
    private string _linkSkillLibraryObjectPath = UnrealProjectSyncService.LinkSkillLibraryObjectPath;
    private string _targetBaseMaterialDiskPath = string.Empty;
    private string _targetZdDiskPath = string.Empty;
    private string _targetCharacterItemDiskPath = string.Empty;
    private string _linkSkillLibraryDiskPath = string.Empty;
    private string _exportDirectoryPath = string.Empty;
    private string _exportScriptPath = string.Empty;
    private string _exportManifestPath = string.Empty;
    private int _exportedAssetCount;
    private string _exportGeneratedAtText = "尚未导出";
    private bool _hasExportManifest;
    private bool _isEngineToToolbox = true;
    private InfoBarSeverity _statusSeverity = InfoBarSeverity.Informational;
    private string _statusTitle = "尚未检测";
    private string _statusMessage = "请选择虚幻引擎和目标项目后进行关联检测。";
    private bool _canSync;
    private string _sourceSearchText = string.Empty;
    private UnrealSyncSourceItem? _selectedSource;
    /// <summary>
    /// 第二步有没有可用数据。
    ///
    /// 从这里往下这几个「这一步加载了没」的标志都是**私有 setter + 集中清单**：
    /// 写入口只有一个，派生属性由 <see cref="UnrealSyncDerivedNotifications"/> 统一通知，
    /// 漏不掉（P3a）。以前是「谁改这个字段，谁记得补 OnPropertyChanged」——
    /// 改的路径有十几条，漏一条界面就停在上一刻。
    /// </summary>
    private readonly UnrealSyncStepLoadStore _stepLoads = new();

    public bool IsNormalizationStepLoaded
    {
        get => _stepLoads.IsLoaded(2);
        private set
        {
            if (!_stepLoads.SetLoaded(2, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsNormalizationStepLoaded));
            NotifyDerived(UnrealSyncDerivedNotifications.NormalizationStepLoaded);
            NotifyWorkspaceStateChanged();
            SaveSessionCache();
        }
    }
    private readonly List<CharacterCard> _draftSources = [];
    private readonly HashSet<string> _existingImportStableIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<UnrealSyncSelectionTreeItem, UnrealSyncSelectionTreeItem> _selectionParents = [];
    public bool HasImportDetection
    {
        get => _stepLoads.HasPublishTree;
        private set
        {
            if (value ? _stepLoads.MarkPublishTree() : _stepLoads.ClearPublishTree())
            {
                OnPropertyChanged(nameof(HasImportDetection));
                NotifyDerived(UnrealSyncDerivedNotifications.ImportDetection);
            }
        }
    }
    /// <summary>当前差异树属于哪一步（第三步或第五步）；0 表示还没有已加载的差异树。</summary>
    private int _loadedPublishStep
    {
        get => _stepLoads.PublishTreeOwnerStep;
        // 旧名字保留成门面：读写都落到状态对象，历史调用点一行不用改。
        set
        {
            if (value is 2 or 5)
            {
                _stepLoads.ClaimPublishTree(value);
            }
            else
            {
                _stepLoads.ClearPublishTree();
            }
        }
    }
    private int _importSelectedCount;
    private int _importAddedCount;
    private int _importUpdatedCount;
    private int _importDeletedCount;
    private int _importAttentionCount;
    private int _importSkippedCount;
    private string _importOperationTitle = "等待内容检测";
    private string _importOperationMessage = "先选择角色，再检测可导入内容。";
    private string _importSelectionCountText = "尚未检测";
    private string _importAddedCountText = "新增 0 项";
    private string _importUpdatedCountText = "更新 0 项";
    private string _importDeletedCountText = "删除旧资产 0 项";
    private string _importAttentionCountText = "需检查 0 项";
    private string _importSkippedCountText = "未选 0 项";
    private string _importPrimaryActionText = "导入所选到草稿";
    private string _importResultMessage = string.Empty;
    private Visibility _importDetailVisibility = Visibility.Collapsed;
    private Visibility _importResultVisibility = Visibility.Collapsed;
    private bool _canImportSelection;
    private bool _isPublishRunning;
    private bool _isWorkflowOperationRunning;

    public bool IsWorkflowOperationRunning
    {
        get => _isWorkflowOperationRunning;
        private set
        {
            if (!SetProperty(ref _isWorkflowOperationRunning, value))
            {
                return;
            }

            NotifyDerived(UnrealSyncDerivedNotifications.WorkflowOperationRunning);
            // 单条派生属性的通知归清单管；中栏要单独重算——
            // 检测结果是在操作还没结束时写进来的，那一刻算出来的可用性必然是假，
            // 操作收尾时不重算一次，写入按钮就会一直停在灰色。
            NotifyWorkspaceStateChanged();
        }
    }
    private readonly UnrealSyncSessionCacheService _sessionCacheService = new();
    private readonly SemaphoreSlim _sessionSaveSemaphore = new(1, 1);
    private int _sessionSaveVersion;
    private UnrealSyncSessionCache? _loadedSessionCache;
    private UnrealSyncSessionCache? _pendingSessionCache;
    private string _pendingSessionProjectPath = string.Empty;
    private CharacterCard? _pendingSessionCharacter;
    private bool _sessionRestored;
    private bool _isRestoringSession;
    private int _bulkSelectionUpdateDepth;
    private bool _bulkSelectionUpdatePending;
    private DateTimeOffset? _lastContentDetectionAt;
    private UnrealBridgeSnapshot? _lastImportSnapshot;
    private List<UnrealBridgeChange> _lastPublishChanges = [];
    private int _detectionTotalCount;
    private int _detectionUnchangedCount;
    private int _detectionAddedCount;
    private int _detectionUpdatedCount;
    private int _detectionRenamedCount;
    private int _detectionConflictCount;
    private int _detectionDeletedCount;
    // ── 第四步「基础配置」的状态搬到了 UnrealProjectSyncViewModel.Step4LightConfiguration.cs ──
    // ── 第三步「同步素材」的发布过滤器（PublishFilter / ApplyPublishFilter）搬到了 Step2MaterialSync.cs ──
    /// <summary>
    /// 当前步号。
    ///
    /// 这一个字段喂着三十多条派生属性（六条状态文案、下一步文案、各处可见性、
    /// 可用性判断…）。以前这些通知是**手写**在 setter 里的：改一个属性要顺着
    /// 三十多行对齐着补，历史上漏掉一条就是一次真实的界面 bug。
    /// 现在同一份清单只有 <see cref="UnrealSyncDerivedNotifications"/> 一处，
    /// 加派生属性时漏不掉。
    /// </summary>
    private int _workflowStep = 1;

    public int WorkflowStep
    {
        get => _workflowStep;
        private set
        {
            // 越界的步号在这里夹住：入口有六七条，钳位留在唯一的写入口最省心。
            if (!SetProperty(
                    ref _workflowStep,
                    Math.Clamp(value, UnrealSyncWorkflow.MinStep, UnrealSyncWorkflow.MaxStep)))
            {
                return;
            }

            NotifyDerived(UnrealSyncDerivedNotifications.WorkflowStep);
            // 另外两件清单管不了的事：落盘，以及重算中栏
            // （中栏状态是从十来个数一起算出来的，不是单个属性）。
            SaveSessionCache();
            NotifyWorkspaceStateChanged();
        }
    }

    public UnrealProjectSyncViewModel(UnrealProjectSyncService syncService)
    {
        _syncService = syncService;
        SharedMaterialSources.Add(new(UnrealSyncSourceKind.SharedMaterial, "通用音效", "项目共享素材", "通用音效 音效 BattleEffects", IsAvailable: false));
        SharedMaterialSources.Add(new(UnrealSyncSourceKind.SharedMaterial, "战斗 BGM", "项目共享素材", "战斗 BGM 音乐 Music", IsAvailable: false));
        SharedMaterialSources.Add(new(UnrealSyncSourceKind.SharedMaterial, "通用 BUFF 图标", "项目共享素材", "通用 BUFF 图标 BuffIcon", IsAvailable: false));
        SharedMaterialSources.Add(new(UnrealSyncSourceKind.SharedMaterial, "活动图片", "项目共享素材", "活动图片 EventImage", IsAvailable: false));
        SharedMaterialSources.Add(new(UnrealSyncSourceKind.SharedMaterial, "其他项目素材", "项目共享素材", "其他项目素材 Shared", IsAvailable: false));
        SelectedPublishStage = PublishStages[0];
        AttachWorkspaceWatchers();
    }

    public ObservableCollection<UnrealProjectSyncCheckItem> CheckItems { get; } = [];

    public ObservableCollection<UnrealProjectSyncCharacterCandidate> CharacterCandidates { get; } = [];

    public ObservableCollection<UnrealSyncSourceItem> SharedMaterialSources { get; } = [];

    public ObservableCollection<UnrealSyncSourceItem> FilteredSharedMaterialSources { get; } = [];

    public ObservableCollection<UnrealSyncSourceItem> CharacterSources { get; } = [];

    // ── 第四步的 LightConfigurationItems 也搬到了 Step4LightConfiguration.cs ──
    // ── 第一步「底层检测」的状态与逻辑都搬到了 UnrealProjectSyncViewModel.Step1Foundation.cs ──
    // ── 第三步「同步素材」的发布阶段 / 差异树 / 计数搬到了 Step2MaterialSync.cs ──

    // ── 第二步「规整素材」的状态搬到了 UnrealProjectSyncViewModel.Step2Normalization.cs ──
    // ── 第五步「序列同步」的工作区开关 / 口径 / 缓存都搬到了 Step5SequenceSync.cs ──
    public string WorkspaceTitle => IsEngineToToolbox ? "检测与选择" : WorkflowStep switch
    {
        1 => "底层检测",
        // 第 2 步 = 合并后的「同步素材」（规整 + 素材同步）。**第 3 步的号放空**，所以这里没有 3。
        2 => "同步素材",
        4 => "基础配置",
        5 => "序列同步",
        6 => "蓝图置入",
        7 => "特效同步",
        _ => "同步结果"
    };
    public string WorkspaceDescription => IsEngineToToolbox
        ? "展开模块，勾选本次需要处理的具体内容。"
        : WorkflowStep switch
        {
            1 => "检查 Unreal 目录和角色 Item 是否符合规范。",
            2 => "先确认 Unreal 旧素材与工具箱规范素材的对应关系，再勾选本次要同步的素材。",
            4 => "检查并应用角色入队语音、Item、MetaSound 和语音并发设置。",
            5 => "同步当前角色的序列、帧素材、AnimMaps 映射和语音轨道。",
            6 => "把角色数据写入角色蓝图和 2DInfor 数据表：对局设置、动作序列、技能与护援连携。",
            7 => "把特效网格与材质实例同步到 Unreal。",
            _ => "查看最近一次同步执行结果。"
        };

    // ── 第四步「基础配置」的计数 / 文案 / 可用性也都搬到了 Step4LightConfiguration.cs ──

    /// <summary>
    /// 这一步**有没有数据**（不是"完成了没有"）。
    ///
    /// ③「每一步都不阻断同步」之后，「下一步」的判据统一成这一条 ——
    /// 不再要求"全合规 / 规整全处理完 / 没有待同步"（那等于给每一步都装一道门）。
    /// 名字保留成 `CanAdvanceWorkflow` 是为了不动那八处 `OnPropertyChanged`。
    /// </summary>
    public bool CanAdvanceWorkflow => WorkflowStep switch
    {
        // 第 1 步：跑过底层检测就有数据（不要求 16 项全绿）
        1 => FoundationChecks.Count > 0,
        // 第 2 步（合并后的「同步素材」）和第 5 步：检测过就有数据（不要求规整全处理完）
        2 or 5 => HasImportDetection,
        // 第 4 步：检测过就有数据（不要求 0 待设置 / 0 错误）
        4 => IsLightConfigurationLoaded,
        _ => true
    };

    public bool HasPublishSelection => !IsEngineToToolbox && _importSelectedCount > 0;
    // ── HasNoPublishChanges / PublishActionText 搬到了 Step2MaterialSync.cs ──
    public bool CanStartPublish => HasPublishSelection &&
        !IsPublishRunning && IsWorkflowOperationIdle;

    public bool IsWorkflowOperationIdle => !IsWorkflowOperationRunning;

    public bool IsPublishRunning
    {
        get => _isPublishRunning;
        private set
        {
            if (SetProperty(ref _isPublishRunning, value))
            {
                OnPropertyChanged(nameof(CanStartPublish));
            }
        }
    }

    public void SetPublishRunning(bool value) => IsPublishRunning = value;

    public void SetWorkflowOperationRunning(bool value) => IsWorkflowOperationRunning = value;

    public string ContentDetectionStatusText => _lastContentDetectionAt is DateTimeOffset detected
        ? $"上次检测：{detected.LocalDateTime:yyyy-MM-dd HH:mm:ss}（打开页面不会自动重检，同步前会强制刷新）"
        : "尚未检测内容";


    public string DetectionResultSummaryText
    {
        get
        {
            if (IsEngineToToolbox)
            {
                return $"共读取 {_detectionTotalCount} 项内容。";
            }

            var details = new List<string> { $"共检查 {_detectionTotalCount} 项", $"无差异 {_detectionUnchangedCount} 项" };
            AddDetectionCount(details, "新增", _detectionAddedCount);
            AddDetectionCount(details, "更新", _detectionUpdatedCount);
            AddDetectionCount(details, "改名", _detectionRenamedCount);
            AddDetectionCount(details, "冲突", _detectionConflictCount);
            AddDetectionCount(details, "删除候选", _detectionDeletedCount);
            return string.Join(" · ", details);
        }
    }

    private int DetectionChangedCount =>
        _detectionAddedCount + _detectionUpdatedCount + _detectionRenamedCount + _detectionConflictCount + _detectionDeletedCount;

    private static void AddDetectionCount(ICollection<string> details, string label, int count)
    {
        if (count > 0)
        {
            details.Add($"{label} {count} 项");
        }
    }

    private void SetPublishDetectionSummary(IReadOnlyCollection<UnrealBridgeChange> changes)
    {
        _detectionTotalCount = changes.Count;
        _detectionUnchangedCount = changes.Count(change => change.Kind == UnrealBridgeChangeKind.Unchanged);
        _detectionAddedCount = changes.Count(change => change.Kind == UnrealBridgeChangeKind.Added);
        _detectionUpdatedCount = changes.Count(change => change.Kind == UnrealBridgeChangeKind.Updated);
        _detectionRenamedCount = changes.Count(change => change.Kind == UnrealBridgeChangeKind.Renamed);
        _detectionConflictCount = changes.Count(change => change.Kind == UnrealBridgeChangeKind.Conflict);
        _detectionDeletedCount = changes.Count(change => change.Kind == UnrealBridgeChangeKind.DeleteCandidate);
        NotifyDetectionSummaryChanged();
    }

    private void SetImportDetectionSummary(UnrealBridgeSnapshot? snapshot, IEnumerable<UnrealSyncSelectionTreeItem> roots)
    {
        _detectionTotalCount = snapshot?.Items.Count ?? roots.SelectMany(root => root.Children).Count();
        _detectionUnchangedCount = 0;
        _detectionAddedCount = _detectionTotalCount;
        _detectionUpdatedCount = 0;
        _detectionRenamedCount = 0;
        _detectionConflictCount = 0;
        _detectionDeletedCount = 0;
        NotifyDetectionSummaryChanged();
    }

    private void ResetDetectionSummary()
    {
        _detectionTotalCount = 0;
        _detectionUnchangedCount = 0;
        _detectionAddedCount = 0;
        _detectionUpdatedCount = 0;
        _detectionRenamedCount = 0;
        _detectionConflictCount = 0;
        _detectionDeletedCount = 0;
        NotifyDetectionSummaryChanged();
    }

    private void NotifyDetectionSummaryChanged()
    {
        OnPropertyChanged(nameof(DetectionResultSummaryText));
        // 这段摘要还会经 WorkspacePlaceholderDetail 喂给中栏的占位文案。
        // 只通知自己的话，重新检测后仍是「无差异」时，
        // 占位面板的说明文字会停留在上一次的计数。
        NotifyWorkspaceStateChanged();
    }

    public bool HasContentDetection => HasImportDetection;

    /// <summary>
    /// 这一步是否已经有可用数据。有就不必再跑一次虚幻检测——
    /// 六步来回切，每次都重检测是纯粹的等待（离线一次十几秒）。
    ///
    /// 第三步和第五步共用同一棵差异树，只是范围不同，
    /// 所以要靠 <see cref="_loadedPublishStep"/> 区分树里装的是谁的数据，
    /// 不能只看 <see cref="HasContentDetection"/>。
    /// </summary>
    public bool IsWorkflowStepLoaded(int step) =>
        UnrealSyncWorkflowState.IsStepLoaded(BuildWorkflowInputs(), step);

    /// <summary>差异检测完成或从缓存恢复后，记下这棵树属于哪一步。</summary>
    public void SetLoadedPublishStep(int step)
    {
        // 这个字段经 IsWorkflowStepLoaded 直接决定中栏状态。以前它是个纯赋值，
        // 三个调用点都恰好跟在 ReturnToWorkflowStep 后面才没出事——那是运气不是保障。
        // 第五步的树在这里才真正"定归属"（检测流程是先建树、后认领），
        // 所以这就是把序列差异写进它自己那份小缓存的最好时机。
        //
        // 写在**早退之前**：刷新时这棵树本来就归第 5 步，早退会把它漏掉。
        // 从缓存恢复时不写：那是读回来的东西，原样写回去只是白一次磁盘。
        //
        // 第 2 步「同步素材」同理：检测是先建树、后认领，所以"认领"就是把它那份差异
        // 写进自己的小缓存（`step2-material-sync.json`）的最好时机。
        if (!_isRestoringSession)
        {
            if (step == 5)
            {
                SaveSequenceSyncCache(SelectedSource?.DraftCharacter);
            }
            else if (step == 2)
            {
                SaveMaterialSyncCache(SelectedSource?.DraftCharacter);
            }
        }

        if (_loadedPublishStep == step)
        {
            return;
        }

        _loadedPublishStep = step;
        NotifyWorkspaceStateChanged();
    }

    /// <summary>
    /// 六步流程的可用性与文案是从步号、各步加载标志、检测结果一起算出来的，
    /// 派生属性有三十多条。
    ///
    /// P3a 之后，主要输入（<c>_workflowStep</c>、四个加载标志、操作闸门）的依赖
    /// 都声明在字段上了，走属性赋值的路径不会再漏。这个方法留给
    /// **声明覆盖不到的路径**：集合内容变了、重置了整块状态这类，
    /// 它们没有对应的字段可以挂声明。
    /// </summary>
    private void NotifyWorkflowStateChanged()
    {
        NotifyDerived(UnrealSyncDerivedNotifications.WorkflowStep);
        NotifyWorkspaceStateChanged();
    }

    /// <summary>
    /// 按 <see cref="UnrealSyncDerivedNotifications"/> 的清单广播派生属性。
    /// 清单是「哪个输入喂着哪些派生属性」的唯一真相，加属性时改那一个文件。
    /// </summary>
    private void NotifyDerived(IReadOnlyList<string> propertyNames)
    {
        for (var index = 0; index < propertyNames.Count; index++)
        {
            OnPropertyChanged(propertyNames[index]);
        }
    }

    public string WorkflowStep1StatusText => UnrealSyncWorkflowState.StepStatusText(BuildWorkflowInputs(), 1);
    public string WorkflowStep2StatusText => UnrealSyncWorkflowState.StepStatusText(BuildWorkflowInputs(), 2);
    public string WorkflowStep4StatusText => UnrealSyncWorkflowState.StepStatusText(BuildWorkflowInputs(), 4);
    public string WorkflowStep5StatusText => UnrealSyncWorkflowState.StepStatusText(BuildWorkflowInputs(), 5);
    /// <summary>
    /// 第 7 步「特效同步」的状态文字。
    ///
    /// 步骤计数住在 <see cref="UnrealSyncWorkflow.MaxStep"/>（已经是 7），这里按同一套写法补上；
    /// 第七步自己的"检测/同步"输入还没接（见 Docs/特效Niagara-面片与序列同步-设计.md 第十一节），
    /// 所以这一步现在只会照规则显示"未开始"，不会谎报完成。
    /// </summary>
    public string WorkflowStep7StatusText => UnrealSyncWorkflowState.StepStatusText(BuildWorkflowInputs(), 7);
    /// <summary>「下一步」按钮上的文案：要写的是**下一步**叫什么，不是当前步。</summary>
    public string WorkflowNextText => WorkflowStep switch
    {
        // 第 1 步的下一步 = 合并后的第 2 步「同步素材」
        1 => "同步素材",
        // 第 2 步的下一步要**跨过放空的第 3 步**，直接进基础配置
        2 => "基础配置",
        4 => "序列同步",
        5 => "蓝图置入",
        6 => "特效同步",
        _ => "已完成"
    };

    public string WorkflowReloadText => WorkflowStep switch
    {
        1 => "重新加载底层检测",
        // 第 2 步是合并后的「同步素材」（规整 + 素材同步），不再是"重新加载规整素材"
        2 => "重新加载同步素材",
        4 => "重新加载基础配置",
        5 => "重新加载序列同步",
        6 => "重新加载蓝图置入",
        7 => "重新加载特效同步",
        _ => "重新加载同步结果"
    };

    // ── 发布确认栏 / 选择栏的可见性见 Step2MaterialSync.cs（WorkflowNextButtonVisibility 由流程表决定，仍留这里）──
    public Visibility WorkflowNextButtonVisibility => Visibility.Visible;
    /// <summary>
    /// 「下一步」按钮能不能点。
    ///
    /// ③ 之后判据收成两句：**这一步有数据**（`CanAdvanceWorkflow`）+ **别正在跑**。
    /// 不再按步写一堆分支 —— 原来那张表里还留着**已放空的第 3 步**的一支，
    /// 而第 6 步那支读的 `CanAdvanceWorkflow` 又恒为 false（等于按钮永远是灰的）。
    /// 第 7 步是最后一步，恒灰。
    /// </summary>
    public bool WorkflowNextButtonEnabled =>
        WorkflowStep != UnrealSyncWorkflow.MaxStep && CanAdvanceWorkflow && IsWorkflowOperationIdle;

    // ── IsPublishSelectionReady / CanExecutePublishChange 搬到了 Step2MaterialSync.cs ──

    public string EnginePath
    {
        get => _enginePath;
        set
        {
            if (SetProperty(ref _enginePath, value))
            {
                Detect();
            }
        }
    }

    public string ProjectPath
    {
        get => _projectPath;
        set
        {
            if (SetProperty(ref _projectPath, value))
            {
                Detect();
            }
        }
    }

    public string ContentPath
    {
        get => _contentPath;
        private set => SetProperty(ref _contentPath, value);
    }

    public string TargetBaseMaterialContentPath
    {
        get => _targetBaseMaterialContentPath;
        private set => SetProperty(ref _targetBaseMaterialContentPath, value);
    }

    public string TargetZdContentPath
    {
        get => _targetZdContentPath;
        private set => SetProperty(ref _targetZdContentPath, value);
    }

    public string TargetCharacterItemContentPath
    {
        get => _targetCharacterItemContentPath;
        private set => SetProperty(ref _targetCharacterItemContentPath, value);
    }

    public string LinkSkillLibraryObjectPath
    {
        get => _linkSkillLibraryObjectPath;
        private set => SetProperty(ref _linkSkillLibraryObjectPath, value);
    }

    public string TargetBaseMaterialDiskPath
    {
        get => _targetBaseMaterialDiskPath;
        private set => SetProperty(ref _targetBaseMaterialDiskPath, value);
    }

    public string TargetZdDiskPath
    {
        get => _targetZdDiskPath;
        private set => SetProperty(ref _targetZdDiskPath, value);
    }

    public string TargetCharacterItemDiskPath
    {
        get => _targetCharacterItemDiskPath;
        private set => SetProperty(ref _targetCharacterItemDiskPath, value);
    }

    public string LinkSkillLibraryDiskPath
    {
        get => _linkSkillLibraryDiskPath;
        private set => SetProperty(ref _linkSkillLibraryDiskPath, value);
    }

    public string ExportDirectoryPath
    {
        get => _exportDirectoryPath;
        private set => SetProperty(ref _exportDirectoryPath, value);
    }

    public string ExportScriptPath
    {
        get => _exportScriptPath;
        private set => SetProperty(ref _exportScriptPath, value);
    }

    public string ExportManifestPath
    {
        get => _exportManifestPath;
        private set => SetProperty(ref _exportManifestPath, value);
    }

    public int ExportedAssetCount
    {
        get => _exportedAssetCount;
        private set => SetProperty(ref _exportedAssetCount, value);
    }

    public string ExportGeneratedAtText
    {
        get => _exportGeneratedAtText;
        private set => SetProperty(ref _exportGeneratedAtText, value);
    }

    public bool HasExportManifest
    {
        get => _hasExportManifest;
        private set => SetProperty(ref _hasExportManifest, value);
    }

    public bool IsEngineToToolbox
    {
        get => _isEngineToToolbox;
        set
        {
            if (SetProperty(ref _isEngineToToolbox, value))
            {
                OnPropertyChanged(nameof(DirectionTitle));
                OnPropertyChanged(nameof(DirectionDescription));
                OnPropertyChanged(nameof(ImportWorkspaceVisibility));
                OnPropertyChanged(nameof(PublishWorkspaceVisibility));
                OnPropertyChanged(nameof(ExecuteActionText));
                OnPropertyChanged(nameof(IsFoundationWorkspace));
                OnPropertyChanged(nameof(FoundationWorkspaceVisibility));
                OnPropertyChanged(nameof(FoundationDetailsVisibility));
                OnPropertyChanged(nameof(WorkspaceTitle));
                NotifyWorkspaceStateChanged();
                OnPropertyChanged(nameof(WorkspaceDescription));
                NotifyDetectionSummaryChanged();
                ClearLightConfigurationState();
            ClearBlueprintSetupState();
                ClearBlueprintSetupState();
                SetSelectionTree([]);
                SelectedSource = null;
                ResetImportOperation();
                RebuildSourceLists();
                SaveSessionCache();
            }
        }
    }

    public string DirectionTitle => IsEngineToToolbox ? "工具箱 ← 虚幻引擎" : "工具箱 → 虚幻引擎";

    public string DirectionDescription => IsEngineToToolbox
        ? "检测 Unreal 角色，勾选需要写入草稿的内容。"
        : "检测草稿与 Unreal 的差异，勾选需要同步的内容。";

    public Visibility ImportWorkspaceVisibility => IsEngineToToolbox ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PublishWorkspaceVisibility => IsEngineToToolbox ? Visibility.Collapsed : Visibility.Visible;

    public string ExecuteActionText => IsEngineToToolbox ? "导入所选到草稿" : "同步所选到虚幻";

    public string ImportOperationTitle
    {
        get => _importOperationTitle;
        private set => SetProperty(ref _importOperationTitle, value);
    }

    public string ImportOperationMessage
    {
        get => _importOperationMessage;
        private set => SetProperty(ref _importOperationMessage, value);
    }

    public string ImportSelectionCountText
    {
        get => _importSelectionCountText;
        private set => SetProperty(ref _importSelectionCountText, value);
    }

    public string ImportAddedCountText
    {
        get => _importAddedCountText;
        private set => SetProperty(ref _importAddedCountText, value);
    }

    public string ImportUpdatedCountText
    {
        get => _importUpdatedCountText;
        private set => SetProperty(ref _importUpdatedCountText, value);
    }

    public string ImportDeletedCountText
    {
        get => _importDeletedCountText;
        private set => SetProperty(ref _importDeletedCountText, value);
    }

    public string ImportAttentionCountText
    {
        get => _importAttentionCountText;
        private set => SetProperty(ref _importAttentionCountText, value);
    }

    public string ImportSkippedCountText
    {
        get => _importSkippedCountText;
        private set => SetProperty(ref _importSkippedCountText, value);
    }

    public string ImportPrimaryActionText
    {
        get => _importPrimaryActionText;
        private set => SetProperty(ref _importPrimaryActionText, value);
    }

    public string ImportResultMessage
    {
        get => _importResultMessage;
        private set => SetProperty(ref _importResultMessage, value);
    }

    public Visibility ImportDetailVisibility
    {
        get => _importDetailVisibility;
        private set => SetProperty(ref _importDetailVisibility, value);
    }

    public Visibility ImportResultVisibility
    {
        get => _importResultVisibility;
        private set => SetProperty(ref _importResultVisibility, value);
    }

    public bool CanImportSelection
    {
        get => _canImportSelection;
        private set => SetProperty(ref _canImportSelection, value);
    }

    // ── SelectionContentVisibility 搬到了 Step2MaterialSync.cs ──

    public string SourceGroupTitle => IsEngineToToolbox ? "Unreal 角色" : "已完成角色";

    public string SourceSearchText
    {
        get => _sourceSearchText;
        set
        {
            if (SetProperty(ref _sourceSearchText, value))
            {
                RebuildSourceLists();
            }
        }
    }

    public UnrealSyncSourceItem? SelectedSource
    {
        get => _selectedSource;
        private set
        {
            if (SetProperty(ref _selectedSource, value))
            {
                OnPropertyChanged(nameof(CanDetectSelectedSource));
                OnPropertyChanged(nameof(CanAdvanceWorkflow));
                OnPropertyChanged(nameof(WorkflowNextButtonEnabled));
            }
        }
    }

    public bool CanDetectSelectedSource => IsWorkflowOperationIdle && CanSync &&
        (IsEngineToToolbox || SelectedPublishStage?.IsAvailable == true) &&
        SelectedSource is { IsSharedMaterial: false };

    public InfoBarSeverity StatusSeverity
    {
        get => _statusSeverity;
        private set => SetProperty(ref _statusSeverity, value);
    }

    public string StatusTitle
    {
        get => _statusTitle;
        private set => SetProperty(ref _statusTitle, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool CanSync
    {
        get => _canSync;
        private set => SetProperty(ref _canSync, value);
    }

    public void Load(string enginePath, string projectPath)
    {
        _loadedSessionCache = null;
        _isRestoringSession = true;
        try
        {
            SelectedSource = null;
            SetSelectionTree([]);
            ResetImportOperation();
            _lastImportSnapshot = null;
            _lastPublishChanges.Clear();
            ResetDetectionSummary();
            ClearLightConfigurationState();
            ClearBlueprintSetupState();
            IsNormalizationStepLoaded = false;
            NormalizationItems.Clear();
            VisibleNormalizationItems = [];
            FoundationChecks.Clear();
            VisibleFoundationChecks = [];
            WorkflowStep = 1;
        }
        finally
        {
            _isRestoringSession = false;
        }

        _enginePath = enginePath;
        _projectPath = projectPath;
        OnPropertyChanged(nameof(EnginePath));
        OnPropertyChanged(nameof(ProjectPath));
        _sessionRestored = false;
        _lastContentDetectionAt = null;
        OnPropertyChanged(nameof(ContentDetectionStatusText));
    }

    public void Detect()
    {
        var selectedCodes = CharacterCandidates
            .Where(candidate => candidate.IsSelected)
            .Select(candidate => candidate.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedSourceKind = SelectedSource?.Kind;
        var selectedSourceCode = SelectedSource?.UnrealCandidate?.Code ?? SelectedSource?.DraftCharacter?.Code;
        var result = _syncService.Check(EnginePath, ProjectPath);
        StatusSeverity = result.Severity;
        StatusTitle = result.Title;
        StatusMessage = result.Message;
        CanSync = result.CanSync;
        OnPropertyChanged(nameof(CanDetectSelectedSource));
        UpdateImportSelectionSummary();
        ContentPath = result.UnrealContentPath;
        TargetBaseMaterialContentPath = result.TargetBaseMaterialContentPath;
        TargetZdContentPath = result.TargetZdContentPath;
        TargetCharacterItemContentPath = result.TargetCharacterItemContentPath;
        LinkSkillLibraryObjectPath = result.LinkSkillLibraryObjectPath;
        TargetBaseMaterialDiskPath = result.TargetBaseMaterialDiskPath;
        TargetZdDiskPath = result.TargetZdDiskPath;
        TargetCharacterItemDiskPath = result.TargetCharacterItemDiskPath;
        LinkSkillLibraryDiskPath = result.LinkSkillLibraryDiskPath;
        ExportDirectoryPath = result.ExportDirectoryPath;
        ExportScriptPath = result.ExportScriptPath;
        ExportManifestPath = result.ExportManifestPath;
        ExportedAssetCount = result.ExportedAssetCount;
        HasExportManifest = result.ExportManifestExists;
        ExportGeneratedAtText = result.ExportGeneratedAt is DateTime generatedAt
            ? generatedAt.ToString("yyyy-MM-dd HH:mm:ss")
            : "尚未导出";
        CheckItems.Clear();
        foreach (var item in result.Items)
        {
            CheckItems.Add(item);
        }

        CharacterCandidates.Clear();
        foreach (var candidate in result.CharacterCandidates)
        {
            if (selectedCodes.Contains(candidate.Code))
            {
                candidate.IsSelected = true;
            }

            CharacterCandidates.Add(candidate);
        }

        RebuildSourceLists();
        if (!string.IsNullOrWhiteSpace(selectedSourceCode))
        {
            SelectedSource = CharacterSources.FirstOrDefault(source =>
                source.Kind == selectedSourceKind &&
                string.Equals(
                    source.UnrealCandidate?.Code ?? source.DraftCharacter?.Code,
                    selectedSourceCode,
                    StringComparison.OrdinalIgnoreCase));
        }

    }

    public string GetExportScriptPath()
    {
        var scriptPath = _syncService.GetExportScriptPath();
        Detect();
        return scriptPath;
    }

    public async Task<UnrealProjectSyncExportRunResult> ExportProjectCharactersAsync(
        IReadOnlyCollection<string>? selectedCharacterCodes = null,
        IProgress<ProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default,
        UnrealProjectSyncExportScope scope = UnrealProjectSyncExportScope.Full)
    {
        var result = await _syncService.ExportProjectCharactersAsync(
            EnginePath,
            ProjectPath,
            selectedCharacterCodes,
            progress,
            cancellationToken,
            scope);
        Detect();
        return result;
    }

    public string[] GetSelectedCharacterCodes()
    {
        return CharacterCandidates
            .Where(candidate => candidate.IsSelected)
            .Select(candidate => candidate.Code)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// 校验发布用的角色目录（第 3/4 步的进入前检查）。
    ///
    /// **这里有两件不同的事，别混**：
    /// ① 往上刷的那份检查项（界面显示）**一律带资产类型** —— 它和第 1 步共用同一个集合，
    ///    不带就会让第 1 步显示成那句占位串「等待 Unreal 类型复检」；
    /// ② "校验强度"（要不要因为读不到类型就抛错、拦住流程）仍按调用方给的
    ///    <paramref name="requireAssetTypes"/> —— 各步维持原样，不因为第 1 步要查类型
    ///    就把第 4 步也变得更容易抛错。
    /// </summary>
    public void ValidatePublishCharacterFolders(string characterCode, bool requireAssetTypes = false)
    {
        RefreshFoundationChecks(characterCode);
        _syncService.ValidatePublishCharacterFolders(ProjectPath, characterCode, requireAssetTypes);
    }

    // ── ValidateSequenceCharacterFolders 搬到了 Step5SequenceSync.cs ──
    // ── SetFoundationConfigurationError 搬到了 Step1Foundation.cs ──
    //    （它增删的是**第 1 步的检查项**，按"一步一个文件"该住那边；第 4 步那边照旧调用它。）

    public UnrealSyncSessionCacheLoadResult RefreshDraftSources(IEnumerable<CharacterCard> characters, string? preferredCharacterCode = null)
    {
        _draftSources.Clear();
        _draftSources.AddRange(characters.Where(character => character.IsCompleted));
        RebuildSourceLists();
        var result = RestoreSessionCache(preferredCharacterCode);
        if (result.Status != UnrealSyncSessionCacheLoadStatus.Loaded)
        {
            SelectDefaultPublishSource(preferredCharacterCode);
        }

        return result;
    }

    public void SelectSource(UnrealSyncSourceItem? source)
    {
        var previousCode = SelectedSource?.UnrealCandidate?.Code ?? SelectedSource?.DraftCharacter?.Code;
        var nextCode = source?.UnrealCandidate?.Code ?? source?.DraftCharacter?.Code;
        var sameDetectedSource = (HasImportDetection || IsLightConfigurationLoaded) &&
            string.Equals(previousCode, nextCode, StringComparison.OrdinalIgnoreCase);
        sameDetectedSource |= IsNormalizationStepLoaded &&
            string.Equals(previousCode, nextCode, StringComparison.OrdinalIgnoreCase);
        var sameSource = string.Equals(previousCode, nextCode, StringComparison.OrdinalIgnoreCase);
        SelectedSource = source;
        if (!sameDetectedSource)
        {
            SetSelectionTree([]);
            ResetImportOperation();
            ClearLightConfigurationState();
            ClearBlueprintSetupState();
            SetNormalizationStepLoaded(false);
            NormalizationItems.Clear();
            VisibleNormalizationItems = [];
        }
        if (!sameSource)
        {
            FoundationChecks.Clear();
            VisibleFoundationChecks = [];
            OnPropertyChanged(nameof(FoundationSummaryText));
            OnPropertyChanged(nameof(CanAdvanceWorkflow));
            OnPropertyChanged(nameof(WorkflowNextButtonEnabled));
        }
        if (!IsEngineToToolbox && !string.IsNullOrWhiteSpace(nextCode))
        {
            // ② 去掉自动检测（晓桀 2026-09-24：「只由我进行检测操作」）：
            // 切角色**不再顺手查一遍**第 1 步的检查项，只把上一位的清掉、
            // 再读**这一位自己的检查项缓存**。读缓存不是检测；没有缓存就空着，
            // 要不要查由用户点「重新加载」。
            ClearFoundationChecks();
            TryApplyFoundationCache(SelectedSource?.DraftCharacter);
        }
        ClearWorkspaceFailure();
        if (!sameSource)
        {
            RestoreCharacterWorkflowStep();
        }

        NotifyWorkspaceStateChanged();
        SaveSessionCache();
    }

    /// <summary>
    /// 切到另一个角色时，回到这个角色自己上次停的步骤。
    ///
    /// 以前切换只换数据、不换步号，于是「在御坂的第六步切到桐人」会停在
    /// 桐人的第六步上——而桐人可能连第一步都没做完。更糟的是紧接着那次
    /// 保存会在桐人目录里写一份空的第六步缓存，把他真实的进度盖出一个假象。
    /// </summary>
    private void RestoreCharacterWorkflowStep()
    {
        var character = SelectedSource?.DraftCharacter;
        if (IsEngineToToolbox || character is null || string.IsNullOrWhiteSpace(ProjectPath))
        {
            return;
        }

        var cached = _sessionCacheService.LoadLatest(character, ProjectPath, character.Code).Cache;
        var step = cached?.WorkflowStep is int value &&
            value >= UnrealSyncWorkflow.MinStep && value <= UnrealSyncWorkflow.MaxStep
                ? value
                : UnrealSyncWorkflow.MinStep;
        if (WorkflowStep != step)
        {
            ReturnToWorkflowStep(step);
            return;
        }

        // 步号没变也要把这一步的缓存读回来，否则会显示上一个角色的内容。
        RestoreWorkflowStepCache(step);
    }

    public bool OpenNormalizationWorkspace()
    {
        var character = SelectedSource?.DraftCharacter;
        var candidate = SelectedSource?.UnrealCandidate ??
            CharacterCandidates.FirstOrDefault(item =>
                string.Equals(item.Code, character?.Code, StringComparison.OrdinalIgnoreCase));
        if (character is null || candidate is null)
        {
            return false;
        }

        var inMemoryCache = _loadedSessionCache;
        var stepCache = _sessionCacheService.LoadStep(character, ProjectPath, character.Code, 2).Cache;
        // 先读第 2 步自己的缓存文件，没有再回退到整体缓存（兼容搬之前留下的旧决策）。
        var cachedDecisions = LoadNormalizationDecisions(character, () =>
            stepCache?.NormalizationDecisions ??
            (inMemoryCache is not null && string.Equals(
                inMemoryCache.SelectedCharacterCode,
                character.Code,
                StringComparison.OrdinalIgnoreCase)
                ? inMemoryCache.NormalizationDecisions
                : _sessionCacheService.LoadLatest(character, ProjectPath, character.Code).Cache?.NormalizationDecisions ?? []));
        var rebuiltItems = BuildNormalizationItems(character, candidate, cachedDecisions);
        ApplyNormalizationItems(rebuiltItems);
        return true;
    }

    /// <summary>
    /// 用**当前 Unreal 候选**重建规整项，并把缓存里的旧决策回填上去。
    ///
    /// 合并后这一步和素材差异树吃的是**同一次导出的同一份候选** ——
    /// 所以它只是"再算一遍规整"，不再切工作区、也不再单独跑导出。
    /// </summary>
    public async Task<bool> RebuildNormalizationItemsAsync()
    {
        var character = SelectedSource?.DraftCharacter;
        var candidate = SelectedSource?.UnrealCandidate ??
            CharacterCandidates.FirstOrDefault(item =>
                string.Equals(item.Code, character?.Code, StringComparison.OrdinalIgnoreCase));
        if (character is null || candidate is null)
        {
            return false;
        }

        var inMemoryCache = _loadedSessionCache;
        // 同上：先读自己的文件。
        var cachedDecisions = LoadNormalizationDecisions(character, () =>
            inMemoryCache is not null && string.Equals(
                inMemoryCache.SelectedCharacterCode,
                character.Code,
                StringComparison.OrdinalIgnoreCase)
                ? inMemoryCache.NormalizationDecisions
                : _sessionCacheService.LoadLatest(character, ProjectPath, character.Code).Cache?.NormalizationDecisions ?? []);
        var rebuiltItems = await Task.Run(() => BuildNormalizationItems(character, candidate, cachedDecisions));
        ApplyNormalizationItems(rebuiltItems);
        return true;
    }

    // ── 第二步「规整素材」的构建 / 应用 / 交互逻辑也搬到了 Step2Normalization.cs ──
    // （BuildNormalizationItems、ApplyNormalizationItems、SelectRedirect / MarkNotRequired /
    //   ClearRedirect、BeginNormalizationStepLoad、SetNormalizationStepLoaded、
    //   RefreshVisibleNormalizationItems、CloseNormalizationWorkspace）

    public void ReturnToWorkflowStep(int step)
    {
        step = Math.Clamp(step, UnrealSyncWorkflow.MinStep, UnrealSyncWorkflow.MaxStep);
        // 切换步骤会触发 WorkflowStep 的保存。恢复前先抑制这次保存，
        // 避免用当前步骤的空显示树覆盖目标步骤已有缓存。
        _isRestoringSession = true;
        try
        {
            if (step == 5)
            {
                SelectedPublishStage = PublishStages.FirstOrDefault(item => item.Stage == UnrealBridgePublishStage.ZdAnimationTracks);
                WorkflowStep = 5;
                RestoreWorkflowStepCache(5);
                return;
            }

            // 第 2 步不需要特判了：合并后它没有"规整工作区"要开，
            // 落步 + 读自己的缓存就是它该做的全部。
            WorkflowStep = step;
            RestoreWorkflowStepCache(step);
            // ② 去掉自动检测：进第 1 步**不再自动查一遍**。
            //    检查项要么是刚才从这位角色自己的缓存读回来的，要么就等用户点「重新加载」。
        }
        finally
        {
            _isRestoringSession = false;
        }
    }

    private void RestoreWorkflowStepCache(int step)
    {
        var characterCode = SelectedSource?.DraftCharacter?.Code ?? SelectedSource?.UnrealCandidate?.Code;
        if (string.IsNullOrWhiteSpace(ProjectPath) || string.IsNullOrWhiteSpace(characterCode))
        {
            return;
        }

        // 勾选是 180ms 防抖写盘的。这里只读磁盘，所以必须先把挂起的那份落盘，
        // 否则「勾选后立刻点同步」会读到勾选之前的旧缓存，把刚做的勾选整个抹掉。
        FlushSessionCache();
        var result = _sessionCacheService.LoadStep(
            SelectedSource?.DraftCharacter, ProjectPath, characterCode, step);

        // 第 5 步先读自己的小缓存（`step5-sequence-sync.json`）：序列差异 + 勾选都在里面，
        // 不依赖那份所有步骤共用、越来越大的会话缓存。
        //
        // 会话缓存仍然读进来放进 _loadedSessionCache —— 它是写盘时的"兜底快照"，
        // 别的步骤没被这次改动的数据（规整决策、基础配置…）要靠它带过去，
        // 不能因为这一步换了来源就把它们丢掉。
        if (step == 5)
        {
            if (result.Status == UnrealSyncSessionCacheLoadStatus.Loaded && result.Cache is not null)
            {
                _loadedSessionCache = result.Cache;
            }

            if (TryApplySequenceSyncCache(SelectedSource?.DraftCharacter))
            {
                return;
            }
        }

        // 第 2 步「同步素材」（合并后）同理：先读自己的小缓存 `step2-material-sync.json`。
        if (step == 2)
        {
            if (result.Status == UnrealSyncSessionCacheLoadStatus.Loaded && result.Cache is not null)
            {
                _loadedSessionCache = result.Cache;
            }

            if (TryApplyMaterialSyncCache(SelectedSource?.DraftCharacter))
            {
                return;
            }
        }

        if (result.Status != UnrealSyncSessionCacheLoadStatus.Loaded || result.Cache is null)
        {
            return;
        }

        var cache = result.Cache;
        _isRestoringSession = true;
        try
        {
            _loadedSessionCache = cache;
            if (step == 2)
            {
                // 第 2 步（合并后的「同步素材」）：
                // ① **内存里有东西就别拿会话缓存盖回去** —— 刚检测出来的结果比缓存新；
                // ② **不再动"加载过"的标志** —— 那个现在归步加载表（`_stepLoads.IsLoaded(2)`）。
                //    原来这里会从会话缓存重设 `IsNormalizationStepLoaded`，缓存里没有那几项时
                //    就把它设成 false，于是"退回第 2 步"显示成「尚未检测」（2026-09-24 实测踩到）。
                if (NormalizationItems.Count == 0)
                {
                    RestoreNormalizationItems(cache.NormalizationItems);
                }

                return;
            }

            if (step == 6)
            {
                if (cache.IsBlueprintSetupLoaded)
                {
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
                }
                return;
            }

            if (step == 4)
            {
                // 第 4 步先读自己的小缓存（`step4-light-configuration.json`），
                // 整体缓存只在没有小缓存时兜底（兼容搬之前留下的旧进度）。
                if (TryApplyLightConfigurationCache(SelectedSource?.DraftCharacter))
                {
                    return;
                }

                if (cache.IsLightConfigurationLoaded)
                {
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
                    return;
                }
            }

            if (step is 2 or 5 && cache.IsPublishDetection)
            {
                // 这一步的树已经在内存里了，就别再拿缓存盖回去。
                //
                // 「重新加载序列同步」的流程是：检测 → 建树 → SetLoadedPublishStep(5)
                // → ReturnToWorkflowStep(5)。最后这一步如果老老实实读缓存，
                // 刚检测出来的结果马上会被上一次写下的那份覆盖掉 —— 用户按了刷新，
                // 屏幕上却还是刷新前的内容，日志里却能同时看到两份不同的数字。
                //
                // 判定用 _loadedPublishStep：它记的就是「内存里这棵树属于哪一步」。
                // 第三步和第五步共用同一棵树的槽位、范围不同，只有它能把两者分开。
                // 换角色时 SelectSource 会 ResetImportOperation 把它清零，冷启动是 0，
                // 这两种情况照常从缓存恢复。
                if (HasImportDetection &&
                    _loadedPublishStep == step &&
                    SelectionTreeRoots.Count > 0 &&
                    string.Equals(
                        SelectedSource?.DraftCharacter?.Code ?? SelectedSource?.UnrealCandidate?.Code,
                        cache.SelectedCharacterCode,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                SetLoadedPublishStep(step);
                var changes = FilterCachedPublishChanges(cache, SelectedSource?.DraftCharacter).ToArray();
                var roots = step == 5
                    ? UnrealSyncSelectionTreeBuilder.FromSequenceChanges(changes, UnrealBridgePublishSupportPolicy.CanExecute, selectPendingByDefault: false)
                    : UnrealSyncSelectionTreeBuilder.FromChanges(changes, UnrealBridgePublishSupportPolicy.CanExecute, selectPendingByDefault: true);
                var selectedIds = cache.SelectedStableIds.Count > 0
                    ? cache.SelectedStableIds
                    : cache.PublishChanges.Where(change => change.IsSelected)
                        .Select(change => change.StableId)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var selectedGroupIds = cache.SelectedGroupStableIds.Count > 0
                    ? cache.SelectedGroupStableIds
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var restoreIds = selectedIds.Union(selectedGroupIds, StringComparer.OrdinalIgnoreCase)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                ApplySelection(roots, restoreIds);
                SetPublishSelectionTree(roots, changes);
            }
        }
        finally
        {
            _isRestoringSession = false;
        }
    }

    /// <summary>
    /// 合并后这里**不再切工作区、也不再改步号**。
    ///
    /// 旧时它干两件事：第 1 步 → 打开"规整工作区"，第 2 步 → 切到第 3 步。
    /// 现在第 2 步（同步素材）本身就是规整 + 素材差异**同屏**，没有"进入规整工作区"这一说；
    /// 往前走统一由流程控制器 <c>GoToNextStepAsync</c> 负责。
    /// 方法留着是因为壳里同步成功后还会调它一次（那时候它本来也只是把界面推一下）。
    /// </summary>
    public bool AdvanceWorkflowStep() => true;

    public bool ReloadPublishResult()
    {
        var character = SelectedSource?.DraftCharacter;
        if (character is null) return false;
        var state = new UnrealBridgeStateService().Load(character, ProjectPath);
        if (state is null) return false;
        ImportOperationTitle = "同步结果";
        ImportOperationMessage = $"最近验证：{state.LastVerifiedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}";
        ImportResultMessage = $"已记录 {state.Entries.Count} 项经过验证的同步状态。";
        ImportResultVisibility = Visibility.Visible;
        return true;
    }

    // ── SetSelectionTree 搬到了 Step2MaterialSync.cs（导入方向也走它）──

    public void SetImportSelectionTree(
        IEnumerable<UnrealSyncSelectionTreeItem> roots,
        IReadOnlySet<string> existingStableIds,
        UnrealBridgeSnapshot? snapshot = null)
    {
        var rootList = roots.ToArray();
        _existingImportStableIds.Clear();
        _existingImportStableIds.UnionWith(existingStableIds);
        HasImportDetection = true;
        OnPropertyChanged(nameof(HasContentDetection));
        OnPropertyChanged(nameof(WorkflowStep5StatusText));
        OnPropertyChanged(nameof(WorkflowStep6StatusText));
        SetImportDetectionSummary(snapshot, rootList);
        ImportOperationTitle = "内容检测完成";
        ImportOperationMessage = "展开中间分类并勾选内容，下面会实时显示本次导入影响。";
        ImportDetailVisibility = Visibility.Visible;
        ImportResultVisibility = Visibility.Collapsed;
        ImportResultMessage = string.Empty;
        SetSelectionTree(rootList);
        _lastImportSnapshot = snapshot;
        _lastContentDetectionAt = DateTimeOffset.Now;
        OnPropertyChanged(nameof(ContentDetectionStatusText));
        SaveSessionCache();
    }

    public async Task SetImportSelectionTreeAsync(
        UnrealBridgeSnapshot snapshot,
        IReadOnlySet<string> existingStableIds,
        CancellationToken cancellationToken = default)
    {
        var roots = await Task.Run(
            () => UnrealSyncSelectionTreeBuilder.FromSnapshot(snapshot),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        SetImportSelectionTree(roots, existingStableIds, snapshot);
    }

    public void CompleteImportOperation(string draftPath, int removedDuplicateCount)
    {
        ImportOperationTitle = "导入完成";
        ImportOperationMessage = "本次结果已保留，可以继续调整勾选后再次导入。";
        ImportResultMessage =
            $"新增 {_importAddedCount} 项，更新 {_importUpdatedCount} 项，未选 {_importSkippedCount} 项。" +
            (removedDuplicateCount > 0 ? $" 已清理旧重复素材 {removedDuplicateCount} 个。" : string.Empty) +
            $"\n草稿：{draftPath}";
        ImportResultVisibility = Visibility.Visible;
    }

    // ── SetPublishSelectionTree / SetPublishSelectionTreeAsync / FilterPublishChanges 搬到了 Step2MaterialSync.cs ──

    public bool MatchesCurrentPublishChanges(IReadOnlyList<UnrealBridgeChange> latestChanges)
    {
        // 两侧必须同口径。_lastPublishChanges 的来源不固定：刚检测完是完整差异集，
        // 而从会话缓存恢复（ReturnToWorkflowStep → RestoreWorkflowStepCache）之后
        // 会变成去掉 Unchanged 和已验证项的子集。直接比会因为元素个数不同恒判"内容已变化"，
        // 同步永远走不下去。所以比较前把两侧都归一到"本次真正需要处理的差异"。
        var current = NormalizePublishChangesForComparison(_lastPublishChanges);
        var latest = NormalizePublishChangesForComparison(latestChanges);
        return current.SequenceEqual(latest, StringComparer.OrdinalIgnoreCase);
    }

    private string[] NormalizePublishChangesForComparison(IReadOnlyList<UnrealBridgeChange> changes)
    {
        var actionable = FilterPublishChanges(changes)
            .Where(change => change.Kind != UnrealBridgeChangeKind.Unchanged);
        var state = SelectedSource?.DraftCharacter is { } character && !string.IsNullOrWhiteSpace(ProjectPath)
            ? new UnrealBridgeStateService().Load(character, ProjectPath)
            : null;
        if (state is not null)
        {
            actionable = actionable.Where(change => !IsAlreadyVerified(change, state));
        }

        return actionable
            .Select(GetPublishChangeFingerprint)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string GetPublishChangeFingerprint(UnrealBridgeChange change) =>
        string.Join("|",
            change.StableId,
            change.Kind,
            change.ToolboxItem?.ContentHash ?? string.Empty,
            change.UnrealItem?.ContentHash ?? string.Empty,
            change.ToolboxItem?.ToolboxRelativePath ?? string.Empty,
            change.UnrealItem?.SourceObjectPath ?? string.Empty);

    public bool RefreshPublishTreeDisplay()
    {
        return ApplyPublishDisplay(SelectionTreeRoots);
    }

    private bool ApplyPublishDisplay(IEnumerable<UnrealSyncSelectionTreeItem> roots)
    {
        var allRedirectNamesResolved = true;
        foreach (var item in roots.SelectMany(root => root.Children))
        {
            if (item.Change is not UnrealBridgeChange change)
            {
                continue;
            }

            // 序列行不走素材重定向：删除项显示 Unreal 资产名，新增项显示帧名，
            // 否则待清理的旧 Sprite 会被标成“待选择工具箱素材”。
            if (change.Module == UnrealBridgeModule.SequenceFrames)
            {
                var sequenceDetail = change.UnrealItem is not null
                    ? $"Unreal 现有：{TrimGamePrefix(change.UnrealItem.SourceObjectPath)}"
                    : $"目标：{BuildPublishTargetPreview(change.ToolboxItem)}";
                item.ApplyDisplay(change.DisplayName, sequenceDetail);
                continue;
            }

            var normalization = change.UnrealItem is null
                ? null
                : NormalizationItems.FirstOrDefault(candidate =>
                    string.Equals(candidate.UnrealObjectPath, change.UnrealItem.SourceObjectPath, StringComparison.OrdinalIgnoreCase));
            if (normalization is null && change.UnrealItem is not null)
            {
                var unrealAssetName = GetUnrealAssetName(change.UnrealItem.SourceObjectPath);
                normalization = NormalizationItems.FirstOrDefault(candidate =>
                    candidate.Module == change.Module &&
                    string.Equals(GetUnrealAssetName(candidate.UnrealObjectPath), unrealAssetName, StringComparison.OrdinalIgnoreCase));
            }
            var redirectedDisplayName = change.UnrealItem is null
                ? null
                : normalization?.SelectedCandidate?.DisplayName ?? ResolveCachedRedirectDisplayName(change);
            var displayName = change.UnrealItem is not null
                ? redirectedDisplayName ?? "待选择工具箱素材"
                : change.ToolboxItem is { AssetPath: var assetPath }
                    ? Path.GetFileNameWithoutExtension(assetPath)
                    : change.DisplayName;
            var detail = change.UnrealItem is not null
                ? $"Unreal 现有：{TrimGamePrefix(change.UnrealItem.SourceObjectPath)}"
                : $"目标：{BuildPublishTargetPreview(change.ToolboxItem)}";
            item.ApplyDisplay(displayName, detail);
            if (change.UnrealItem is not null && string.IsNullOrWhiteSpace(redirectedDisplayName))
            {
                allRedirectNamesResolved = false;
            }
        }
        return allRedirectNamesResolved;
    }

    private string? ResolveCachedRedirectDisplayName(UnrealBridgeChange change)
    {
        if (change.UnrealItem is null || SelectedSource?.DraftCharacter is not { } character)
        {
            return null;
        }

        var decisions = _loadedSessionCache?.NormalizationDecisions;
        if (decisions is null || decisions.Count == 0)
        {
            return null;
        }

        var sourcePath = change.UnrealItem.SourceObjectPath;
        var decision = decisions.GetValueOrDefault(sourcePath);
        if (string.IsNullOrWhiteSpace(decision))
        {
            var assetName = GetUnrealAssetName(sourcePath);
            decision = decisions.FirstOrDefault(pair =>
                string.Equals(GetUnrealAssetName(pair.Key), assetName, StringComparison.OrdinalIgnoreCase)).Value;
        }

        if (string.IsNullOrWhiteSpace(decision) || string.Equals(decision, "__not_required__", StringComparison.Ordinal))
        {
            return null;
        }

        var identityId = decision.StartsWith("material:", StringComparison.OrdinalIgnoreCase)
            ? decision["material:".Length..]
            : decision.StartsWith("voice:", StringComparison.OrdinalIgnoreCase)
                ? decision["voice:".Length..]
                : decision;
        return new UnrealBridgeToolboxIdentityService().TryResolveAssignedPath(
            character,
            change.Module,
            identityId,
            out var assignedPath)
                ? Path.GetFileNameWithoutExtension(assignedPath)
                : null;
    }

    private static string BuildPublishTargetPreview(UnrealBridgeSnapshotItem? toolboxItem)
    {
        if (toolboxItem is null || string.IsNullOrWhiteSpace(toolboxItem.AssetPath)) return string.Empty;
        var relative = toolboxItem.ToolboxRelativePath.Replace('\\', '/').Trim('/');
        var fileName = Path.GetFileNameWithoutExtension(toolboxItem.AssetPath);
        var codeMarker = "/Completed/";
        var normalizedPath = toolboxItem.AssetPath.Replace('\\', '/');
        var markerIndex = normalizedPath.IndexOf(codeMarker, StringComparison.OrdinalIgnoreCase);
        var code = markerIndex >= 0 ? normalizedPath[(markerIndex + codeMarker.Length)..].Split('/')[0] : "";
        if (relative.StartsWith("AssetMaterial/BuffIcon/", StringComparison.OrdinalIgnoreCase))
            return $"GameActor2D/{code}/BUFF/{fileName}";
        if (relative.StartsWith("AssetMaterial/", StringComparison.OrdinalIgnoreCase))
            return $"AssetMaterial/ImageS/CharaterS/{code}/{fileName}";
        if (relative.StartsWith("Sound/", StringComparison.OrdinalIgnoreCase))
        {
            var separatorIndex = relative.LastIndexOf('/');
            var categoryPath = separatorIndex >= 0 ? relative[..(separatorIndex + 1)] : string.Empty;
            return $"GameActor2D/{code}/{categoryPath}{fileName}";
        }
        return relative;
    }

    private static string TrimGamePrefix(string path) =>
        path.StartsWith("/Game/", StringComparison.OrdinalIgnoreCase) ? path[6..] : path;

    private static string GetUnrealAssetName(string objectPath)
    {
        var leaf = objectPath.Replace('\\', '/').Split('/').LastOrDefault() ?? string.Empty;
        return leaf.Split('.', 2)[0];
    }

    public void CompletePublishOperation(int executedCount, int deferredCount)
    {
        SetSelectionTree([]);
        _lastPublishChanges.Clear();
        // 同步完成后必须留下反馈。以前这里把 _hasImportDetection 置假、又清空检测计数：
        // DetectionResultVisibility 要求 HasContentDetection 为真，于是整块结果面板直接折叠，
        // 中栏什么都不显示——刚跑完一次成功的同步，界面却像什么都没发生过。
        // 复扫的统计是真实且有意义的（检查了多少项、还剩多少差异），保留它。
        HasImportDetection = true;
        OnPropertyChanged(nameof(HasContentDetection));
        OnPropertyChanged(nameof(WorkflowStep5StatusText));
        OnPropertyChanged(nameof(WorkflowStep6StatusText));
        OnPropertyChanged(nameof(IsPublishSelectionReady));
        OnPropertyChanged(nameof(HasPublishSelection));
        OnPropertyChanged(nameof(HasNoPublishChanges));
        OnPropertyChanged(nameof(WorkflowNextButtonEnabled));
        OnPropertyChanged(nameof(CanStartPublish));
        OnPropertyChanged(nameof(PublishActionText));
        // **不再改步号**：完成一次同步之后该停在哪，由**调用方**决定
        // （发布链路自己会 `ReturnToWorkflowStep(...)`；第七步回 7、第五步回 5、第 2 步回 2）。
        // 这里原来无条件跳到第 4 步 —— 第 2 步（素材）同步完也被弹到基础配置，
        // 而导航那条路以前还会顺手调它一次，等于"离开第 2 步"就把它的检测结果扔了
        // （2026-09-24 实测：从基础配置往回切，第 2 步变成"尚未检测"）。
        var wasSequenceStep = WorkflowStep == 5;

        ImportOperationTitle = wasSequenceStep ? "序列同步完成" : "素材同步完成";
        ImportOperationMessage = wasSequenceStep
            ? "当前角色的序列已全部同步。"
            : "正在进入第四步基础配置。";
        ImportResultMessage = deferredCount > 0
            ? $"本次已执行 {executedCount} 项，保留未执行 {deferredCount} 项。"
            : $"本次已执行 {executedCount} 项，复扫未发现剩余差异。";
        ImportResultVisibility = Visibility.Visible;
    }

    // ── 第四步「基础配置」的检测结果落库 / 勾选 / 换步清理也都搬到了 Step4LightConfiguration.cs ──

    public void FailPublishOperation(string message)
    {
        ImportOperationTitle = "同步失败";
        ImportOperationMessage = "本次没有完成，当前差异选择仍保留。";
        ImportResultMessage = message;
        ImportResultVisibility = Visibility.Visible;
    }

    public void FailImportOperation(string message)
    {
        ImportOperationTitle = "导入失败";
        ImportOperationMessage = "本次没有完成，当前勾选仍保留。";
        ImportResultMessage = message;
        ImportResultVisibility = Visibility.Visible;
    }

    public void FailImportDetection(string message)
    {
        SetWorkspaceFailure(message);
        if (IsEngineToToolbox || SelectionTreeRoots.Count == 0)
        {
            ResetImportOperation();
        }
        ImportOperationTitle = IsEngineToToolbox ? "内容检测失败" : "差异检测失败";
        ImportOperationMessage = message;
    }

    // ── GetSelectedStableIds / GetSelectedGroupAndLeafStableIds 搬到了 Step2MaterialSync.cs ──

    private void ImportSelectionItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UnrealSyncSelectionTreeItem.IsChecked))
        {
            if (sender is UnrealSyncSelectionTreeItem child &&
                _selectionParents.TryGetValue(child, out var parent) &&
                parent.IsUpdatingChildren)
            {
                return;
            }

            if (DeferSelectionRecompute())
            {
                return;
            }

            RecomputeSelectionState();
        }
    }

    /// <summary>
    /// 批量改勾选时，把「汇总 + 过滤 + 写缓存」压成结尾的一次。
    /// 这三件事每次都要走一遍整棵树，SaveSessionCache 还会把全部变更记录克隆一份去建缓存对象；
    /// 恢复上千个叶子勾选时逐个触发，就是上百万次克隆，实测能把进程顶到 5GB 并让 UI 线程一直满载空转。
    /// </summary>
    public IDisposable BeginBulkSelectionUpdate()
    {
        _bulkSelectionUpdateDepth++;
        return new BulkSelectionUpdateScope(this);
    }

    /// <summary>批量期间返回 true，表示这次重算推迟到批量结束时统一做。</summary>
    private bool DeferSelectionRecompute()
    {
        if (_bulkSelectionUpdateDepth <= 0)
        {
            return false;
        }

        _bulkSelectionUpdatePending = true;
        return true;
    }

    private void EndBulkSelectionUpdate()
    {
        if (_bulkSelectionUpdateDepth > 0)
        {
            _bulkSelectionUpdateDepth--;
        }

        if (_bulkSelectionUpdateDepth > 0 || !_bulkSelectionUpdatePending)
        {
            return;
        }

        _bulkSelectionUpdatePending = false;
        RecomputeSelectionState();
    }

    private void RecomputeSelectionState()
    {
        UpdateImportSelectionSummary();
        ApplyPublishFilter();
        // 右栏那组「已选择 N / M 项」是按步骤算的，和这里的导入摘要不是一回事。
        // 第四、六步的单项勾选都记得通知它，唯独第三、五步这条路径漏了，
        // 于是逐个勾选素材时计数一直卡在旧值，要点全选或切步骤才跳回来。
        NotifyStepSelectionChanged();
        SaveSessionCache();
    }

    private sealed class BulkSelectionUpdateScope(UnrealProjectSyncViewModel owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            owner.EndBulkSelectionUpdate();
        }
    }

    // ── RestoreSelectionState 搬到了 Step2MaterialSync.cs ──

    private void SelectionGroup_GroupSelectionChanged(object? sender, EventArgs e)
    {
        if (DeferSelectionRecompute())
        {
            return;
        }

        RecomputeSelectionState();
    }

    private void NormalizationResolutionChanged()
    {
        OnPropertyChanged(nameof(NormalizationSummaryText));
        OnPropertyChanged(nameof(CanAdvanceWorkflow));
        OnPropertyChanged(nameof(IsPublishSelectionReady));
            OnPropertyChanged(nameof(HasPublishSelection));
            OnPropertyChanged(nameof(CanStartPublish));
            OnPropertyChanged(nameof(WorkflowNextButtonEnabled));
        OnPropertyChanged(nameof(PendingRedirectCount));
        OnPropertyChanged(nameof(ReadyPublishCount));
        OnPropertyChanged(nameof(ReadyPublishText));
        OnPropertyChanged(nameof(PendingRedirectText));
        if (HideResolvedNormalizationItems)
        {
            RefreshVisibleNormalizationItems();
        }
        SaveSessionCache();
    }

    private UnrealSyncSessionCacheLoadResult RestoreSessionCache(string? preferredCharacterCode)
    {
        if (_sessionRestored || string.IsNullOrWhiteSpace(ProjectPath))
        {
            return new(UnrealSyncSessionCacheLoadStatus.Missing);
        }

        _sessionRestored = true;
        // 缓存现在按角色存在各自的工具目录下，所以要拿着角色卡去找，
        // 没有指定角色时就在所有已完成角色里挑最近写过的那一份。
        var preferred = _draftSources.FirstOrDefault(item =>
            string.Equals(item.Code, preferredCharacterCode, StringComparison.OrdinalIgnoreCase));
        var loadResult = preferred is not null
            ? _sessionCacheService.LoadLatest(preferred, ProjectPath, preferred.Code)
            : _sessionCacheService.LoadLatest(_draftSources, ProjectPath);
        var cache = loadResult.Cache;
        if (loadResult.Status != UnrealSyncSessionCacheLoadStatus.Loaded || cache is null)
        {
            return loadResult;
        }
        _loadedSessionCache = cache;

        // 第 1 步「底层检测」**不再从这份整体缓存恢复**，它读自己的小缓存文件
        // （`tool\UnrealSync\step1-foundation.json`）；整体缓存对它只写不读。
        TryApplyFoundationCache(preferred);

        if (!string.Equals(cache.EnginePath, EnginePath, StringComparison.OrdinalIgnoreCase))
        {
            return new(UnrealSyncSessionCacheLoadStatus.Invalid, ErrorMessage: "同步进度使用的 Unreal 引擎路径与当前设置不一致。");
        }

        // 第四步会在进入时清理第三步的差异树标记，因此不能只靠
        // IsPublishDetection 判断是否存在可恢复的同步进度。
        var hasProgress = cache.IsPublishDetection ||
            cache.ImportSnapshot is not null ||
            cache.IsLightConfigurationLoaded ||
            cache.IsBlueprintSetupLoaded ||
            cache.WorkflowStep >= 4;
        if (string.IsNullOrWhiteSpace(cache.SelectedCharacterCode) || !hasProgress)
        {
            return new(UnrealSyncSessionCacheLoadStatus.Missing);
        }

        _isRestoringSession = true;
        try
        {
            HideCompletedFoundationChecks = cache.HideCompletedFoundationChecks;
            HideResolvedNormalizationItems = cache.HideResolvedNormalizationItems;
            IsEngineToToolbox = cache.Direction == UnrealBridgeDirection.ImportFromUnreal;
            var stage = PublishStages.FirstOrDefault(item => item.Stage == cache.Stage);
            if (stage is not null) SelectedPublishStage = stage;
            var source = CharacterSources.FirstOrDefault(item =>
                string.Equals(item.UnrealCandidate?.Code ?? item.DraftCharacter?.Code, cache.SelectedCharacterCode, StringComparison.OrdinalIgnoreCase));
            if (source is null)
            {
                return new(UnrealSyncSessionCacheLoadStatus.Invalid, ErrorMessage: $"同步进度中的角色 {cache.SelectedCharacterCode} 已不在当前来源列表中。");
            }

            SelectedSource = source;
            _lastContentDetectionAt = cache.DetectedAt == default ? null : cache.DetectedAt;
            WorkflowStep = cache.WorkflowStep >= UnrealSyncWorkflow.MinStep &&
                cache.WorkflowStep <= UnrealSyncWorkflow.MaxStep
                    ? cache.WorkflowStep
                    : UnrealSyncWorkflow.MinStep;
            if (WorkflowStep == 5 && !cache.IsPublishDetection)
            {
                WorkflowStep = 4;
            }
            OnPropertyChanged(nameof(ContentDetectionStatusText));

            RestoreNormalizationItems(cache.NormalizationItems);
            _detectionTotalCount = cache.DetectionTotalCount;
            _detectionUnchangedCount = cache.DetectionUnchangedCount;
            _detectionAddedCount = cache.DetectionAddedCount;
            _detectionUpdatedCount = cache.DetectionUpdatedCount;
            _detectionRenamedCount = cache.DetectionRenamedCount;
            _detectionConflictCount = cache.DetectionConflictCount;
            _detectionDeletedCount = cache.DetectionDeletedCount;
            NotifyDetectionSummaryChanged();
            // 这里原来会从会话缓存重设 `IsNormalizationStepLoaded`（下面那两行）。
            // 它已经**没人读了** —— "这一步加载过没有"现在一律问步加载表（`_stepLoads.IsLoaded(2)`），
            // 理由见 `UnrealProjectSyncViewModel.Workspace.cs` 的 `BuildWorkflowInputs`。
            // 删掉是为了不留一个"看起来还在起作用"的假标志。
            if (cache.IsBlueprintSetupLoaded)
            {
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
            }

            // 第 4 步先读自己的小缓存，整体缓存只在没有小缓存时兜底。
            if (!TryApplyLightConfigurationCache(SelectedSource?.DraftCharacter) &&
                cache.IsLightConfigurationLoaded)
            {
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
                _lightConfigurationResultMessage = cache.LightConfigurationResultMessage;
                OnPropertyChanged(nameof(LightConfigurationResultMessage));
            }

            if (cache.IsPublishDetection)
            {
                if (cache.DetectionAlgorithmVersion != CurrentDetectionAlgorithmVersion)
                {
                    cache.DetectionAlgorithmVersion = CurrentDetectionAlgorithmVersion;
                    cache.PublishChanges.Clear();
                    cache.SelectedStableIds.Clear();
                    _lastPublishChanges.Clear();
                    HasImportDetection = false;
                    _loadedPublishStep = 0;
                    ResetDetectionSummary();
                    OnPropertyChanged(nameof(HasContentDetection));
                    SetSelectionTree([]);
                    ImportOperationTitle = "等待差异检测";
                    ImportOperationMessage = "检测缓存已过期，请重新加载同步素材。";
                    ImportDetailVisibility = Visibility.Collapsed;
                    ImportResultVisibility = Visibility.Collapsed;
                    ImportResultMessage = string.Empty;
                }
                else
                {
                    // 这一步的差异树**已经用刚检测出来的数据建好了**，就别再拿缓存盖回去。
                    //
                    // 缓存里存的是一份「待办清单」——两侧已经一致的项（Unchanged）按设计
                    // 被剔掉了。盖回去之后，界面上会重新冒出一批早就同步完的动作，
                    // 看着就像「刷新了却什么都没变」。用户点重新加载、看到列表照旧，
                    // 就是这么来的。
                    //
                    // 只在这一步已经加载过、且还是同一个角色时才跳过；冷启动时
                    // _loadedPublishStep 是 0，照常从缓存恢复。
                    if (_loadedPublishStep == cache.WorkflowStep &&
                        _loadedPublishStep is 2 or 5 &&
                        SelectionTreeRoots.Count > 0 &&
                        string.Equals(
                            SelectedSource?.DraftCharacter?.Code,
                            cache.SelectedCharacterCode,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return loadResult;
                    }

                    var cachedComparison = FilterPublishChanges(cache.PublishChanges).ToArray();
                    var cachedChanges = FilterCachedPublishChanges(cache, source.DraftCharacter).ToArray();
                    var roots = cache.WorkflowStep == 5
                        ? UnrealSyncSelectionTreeBuilder.FromSequenceChanges(cachedChanges, UnrealBridgePublishSupportPolicy.CanExecute, selectPendingByDefault: false)
                        : UnrealSyncSelectionTreeBuilder.FromChanges(cachedChanges, UnrealBridgePublishSupportPolicy.CanExecute, selectPendingByDefault: true);
                    var selectedIds = cache.SelectedStableIds.Count > 0
                        ? cache.SelectedStableIds
                        : cache.PublishChanges.Where(change => change.IsSelected).Select(change => change.StableId).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    ApplySelection(roots, selectedIds);
                    if (cache.DetectionTotalCount == 0 && cachedChanges.Length > 0)
                    {
                        SetPublishDetectionSummary(cachedChanges);
                    }
                    SetPublishSelectionTree(roots, cachedComparison);
                    // 只有第三步和第五步自己的缓存才算「这一步已加载」。
                    // 从第六步的缓存恢复时，树里装的是上一步顺带留下的差异，
                    // 认成第三步已加载会让人拿着旧范围的数据继续往下走。
                    SetLoadedPublishStep(cache.WorkflowStep is 2 or 5 ? cache.WorkflowStep : 0);
                }
            }
            else
            {
                var roots = UnrealSyncSelectionTreeBuilder.FromSnapshot(cache.ImportSnapshot!);
                ApplySelection(roots, cache.SelectedStableIds);
                SetImportSelectionTree(roots, new HashSet<string>(StringComparer.OrdinalIgnoreCase), cache.ImportSnapshot);
            }

            _lastContentDetectionAt = cache.DetectedAt == default ? null : cache.DetectedAt;
            OnPropertyChanged(nameof(ContentDetectionStatusText));

            return loadResult;
        }
        finally
        {
            _isRestoringSession = false;
        }
    }

    private void RestoreNormalizationItems(IEnumerable<UnrealSyncNormalizationCacheItem> cachedItems)
    {
        NormalizationItems.Clear();
        foreach (var cached in cachedItems)
        {
            var candidates = cached.Candidates.ToArray();
            var selectedCandidate = candidates.FirstOrDefault(candidate =>
                string.Equals(candidate.StableId, cached.SelectedCandidateStableId, StringComparison.OrdinalIgnoreCase));
            var item = new UnrealAssetNormalizationItem(
                cached.StableId,
                cached.Module,
                cached.Category,
                cached.UnrealAssetName,
                cached.UnrealObjectPath,
                cached.PreviewFilePath,
                cached.ReferenceCount,
                candidates,
                cached.Decision == UnrealAssetNormalizationDecision.Redirect ? selectedCandidate : null,
                cached.IsAlreadyNormalized);
            if (cached.Decision == UnrealAssetNormalizationDecision.NotRequired)
            {
                item.MarkNotRequired();
            }
            else if (cached.Decision == UnrealAssetNormalizationDecision.Pending || selectedCandidate is null)
            {
                item.ClearRedirect();
            }

            NormalizationItems.Add(item);
        }

        RefreshVisibleNormalizationItems();
        OnPropertyChanged(nameof(CanAdvanceWorkflow));
    }

    private IReadOnlyList<UnrealBridgeChange> FilterCachedPublishChanges(
        UnrealSyncSessionCache cache,
        CharacterCard? character)
    {
        var sequenceView = WorkflowStep == 5 ||
            SelectedPublishStage?.Stage == UnrealBridgePublishStage.ZdAnimationTracks;
        // 序列这一侧**不能**剔掉 Unchanged。第五步的树是按帧配对数出来的：
        // 动作节点（sequence:<动作>）本身往往就是 Unchanged，剔掉之后
        // 「Unreal 现有多少个帧位」无从得知，帧与帧的配对也全断了，
        // 于是已经同步好的动作会重新显示成「删除 23 项 + 新增 23 项」。
        // 第三步（素材）没有这个问题，那边只要待办清单。
        var changes = FilterPublishChanges(cache.PublishChanges)
            .Where(change => sequenceView || change.Kind != UnrealBridgeChangeKind.Unchanged)
            .ToArray();
        if (sequenceView)
        {
            return changes;
        }

        if (character is null)
        {
            return changes;
        }

        var state = new UnrealBridgeStateService().Load(character, ProjectPath);
        if (state is null)
        {
            return changes;
        }

        return changes
            .Where(change => !IsAlreadyVerified(change, state))
            .ToArray();
    }

    private static bool IsAlreadyVerified(
        UnrealBridgeChange change,
        UnrealBridgeSyncState state)
    {
        if (!state.Entries.TryGetValue(change.StableId, out var entry))
        {
            return false;
        }

        var toolboxMatches = change.ToolboxItem is not null &&
            string.Equals(change.ToolboxItem.ContentHash, entry.ToolboxHash, StringComparison.OrdinalIgnoreCase) &&
            BaselinePathMatches(change.ToolboxItem.ToolboxRelativePath, entry.ToolboxRelativePath);
        var unrealMatches = change.UnrealItem is not null &&
            string.Equals(change.UnrealItem.ContentHash, entry.UnrealHash, StringComparison.OrdinalIgnoreCase) &&
            BaselinePathMatches(change.UnrealItem.SourceObjectPath, entry.UnrealObjectPath);
        return toolboxMatches && (unrealMatches || change.UnrealItem is null);
    }

    /// <summary>
    /// 基线记的路径要和这次差异里的路径对得上，才算「这条已经同步过了」。
    ///
    /// 只比内容哈希是不够的：把一条语音从「待分配」挪到「失败语音」时，
    /// 文件字节没变、Unreal 那侧的资产也没被动过，两个哈希都和基线一致，
    /// 于是这条 Renamed 被当成早就做完了、从第三步的列表里整条抹掉——
    /// 界面还会因此报「全部素材无差异」，而 Unreal 里那条语音一直躺在 Other。
    ///
    /// 基线里没记路径（老版本写的）时一律判为「确认不了」。宁可多显示一条
    /// 让用户自己看，也不要再悄悄跳过该做的事。
    /// </summary>
    private static bool BaselinePathMatches(string current, string recorded) =>
        !string.IsNullOrWhiteSpace(recorded) &&
        string.Equals(
            current.Replace('\\', '/').Trim('/'),
            recorded.Replace('\\', '/').Trim('/'),
            // 资产路径在 Unreal 里大小写不敏感，这里也不能按大小写判成两条不同的路径
            StringComparison.OrdinalIgnoreCase);

    private void ApplySelection(IEnumerable<UnrealSyncSelectionTreeItem> roots, IReadOnlySet<string> selectedIds)
    {
        using var scope = BeginBulkSelectionUpdate();
        foreach (var root in roots)
        {
            root.RestoreCheckedState(selectedIds);
        }
    }

    private void SelectDefaultPublishSource(string? preferredCharacterCode)
    {
        if (SelectedSource is not null || string.IsNullOrWhiteSpace(preferredCharacterCode))
        {
            return;
        }

        _isRestoringSession = true;
        try
        {
            if (_draftSources.Any(character => string.Equals(character.Code, preferredCharacterCode, StringComparison.OrdinalIgnoreCase)))
            {
                IsEngineToToolbox = false;
            }

            SelectedSource = CharacterSources.FirstOrDefault(source =>
                string.Equals(source.DraftCharacter?.Code, preferredCharacterCode, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _isRestoringSession = false;
        }
    }

    private void SaveSessionCache()
    {
        if (_isRestoringSession || string.IsNullOrWhiteSpace(ProjectPath)) return;
        var existing = _loadedSessionCache;
        var selectedCode = SelectedSource?.UnrealCandidate?.Code ?? SelectedSource?.DraftCharacter?.Code ?? existing?.SelectedCharacterCode ?? string.Empty;
        var existingForSelectedCharacter = existing is not null && string.Equals(
            existing.SelectedCharacterCode,
            selectedCode,
            StringComparison.OrdinalIgnoreCase)
                ? existing
                : null;
        var selectedIds = GetSelectedStableIds().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedByStableId = SelectionTreeRoots.SelectMany(root => root.Children)
            .ToDictionary(item => item.StableId, item => item.IsChecked == true, StringComparer.OrdinalIgnoreCase);
        var changes = _lastPublishChanges.Select(change => change with
        {
            IsSelected = selectedByStableId.GetValueOrDefault(change.StableId)
        }).ToList();
        var cache = new UnrealSyncSessionCache
        {
            EnginePath = EnginePath,
            ProjectPath = ProjectPath,
            DetectionAlgorithmVersion = CurrentDetectionAlgorithmVersion,
            Direction = IsEngineToToolbox ? UnrealBridgeDirection.ImportFromUnreal : UnrealBridgeDirection.PublishToUnreal,
            Stage = SelectedPublishStage?.Stage ?? UnrealBridgePublishStage.CharacterMaterials,
            WorkflowStep = WorkflowStep,
            SelectedCharacterCode = selectedCode,
            DetectedAt = _lastContentDetectionAt ?? DateTimeOffset.Now,
            IsPublishDetection = !IsEngineToToolbox,
            DetectionTotalCount = _detectionTotalCount,
            DetectionUnchangedCount = _detectionUnchangedCount,
            DetectionAddedCount = _detectionAddedCount,
            DetectionUpdatedCount = _detectionUpdatedCount,
            DetectionRenamedCount = _detectionRenamedCount,
            DetectionConflictCount = _detectionConflictCount,
            DetectionDeletedCount = _detectionDeletedCount,
            ImportSnapshot = _lastImportSnapshot ?? existingForSelectedCharacter?.ImportSnapshot,
            // 第四步保存时也必须保留第三步差异；当前步骤文件由 WorkflowStep 隔离，
            // 不能用空列表覆盖尚未执行完的第三步缓存。
            PublishChanges = changes.Count > 0 ? changes : existingForSelectedCharacter?.PublishChanges ?? [],
            SelectedStableIds = selectedIds,
            SelectedGroupStableIds = UnrealSyncSelectionTreeBuilder.SelectedGroupAndLeafStableIds(SelectionTreeRoots)
                .Where(id => SelectionTreeRoots.SelectMany(root => root.Children).All(item => !string.Equals(item.StableId, id, StringComparison.OrdinalIgnoreCase)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            NormalizationDecisions = NormalizationItems.Count == 0
                ? existingForSelectedCharacter?.NormalizationDecisions ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : NormalizationItems
                    .Where(item => item.IsResolved)
                     .ToDictionary(
                         item => item.StableId,
                         item => item.Decision == UnrealAssetNormalizationDecision.NotRequired
                             ? "__not_required__"
                             : item.SelectedCandidate?.StableId ?? string.Empty,
                         StringComparer.OrdinalIgnoreCase),
            NormalizationItems = NormalizationItems
                .Select(item => new UnrealSyncNormalizationCacheItem
                {
                    StableId = item.StableId,
                    Module = item.Module,
                    Category = item.Category,
                    UnrealAssetName = item.UnrealAssetName,
                    UnrealObjectPath = item.UnrealObjectPath,
                    PreviewFilePath = item.PreviewFilePath,
                    ReferenceCount = item.ReferenceCount,
                    Candidates = item.Candidates.ToList(),
                    SelectedCandidateStableId = item.SelectedCandidate?.StableId ?? string.Empty,
                    Decision = item.Decision,
                    IsAlreadyNormalized = item.IsAlreadyNormalized
                })
                .ToList(),
            IsNormalizationStepLoaded = IsNormalizationStepLoaded,
            HideCompletedFoundationChecks = HideCompletedFoundationChecks,
            HideResolvedNormalizationItems = HideResolvedNormalizationItems,
            IsLightConfigurationLoaded = IsLightConfigurationLoaded,
            LightConfigurationItems = _lastLightConfigurationItems.ToList(),
            SelectedLightConfigurationIds = GetSelectedLightConfigurationIds().ToHashSet(StringComparer.OrdinalIgnoreCase),
            LightConfigurationResultMessage = _lightConfigurationResultMessage,
            IsBlueprintSetupLoaded = IsBlueprintSetupLoaded,
            BlueprintSetupItems = _lastBlueprintSetupItems.ToList(),
            SelectedBlueprintSetupIds = GetSelectedBlueprintSetupIds().ToHashSet(StringComparer.OrdinalIgnoreCase),
            BlueprintSetupResultMessage = _blueprintSetupResultMessage
        };
        _loadedSessionCache = cache;
        _pendingSessionCache = cache;
        _pendingSessionProjectPath = ProjectPath;
        // 缓存写在角色目录下，落盘时需要角色卡；这里连同快照一起捕获，
        // 免得延迟落盘执行时选中的角色已经换掉了。
        _pendingSessionCharacter = SelectedSource?.DraftCharacter;
        var version = Interlocked.Increment(ref _sessionSaveVersion);
        _ = PersistSessionCacheAfterDelayAsync(version, ProjectPath, _pendingSessionCharacter, cache);
    }

    private async Task PersistSessionCacheAfterDelayAsync(
        int version,
        string projectPath,
        CharacterCard? character,
        UnrealSyncSessionCache cache)
    {
        try
        {
            // 全程不回 UI 线程：FlushSessionCache 会在 UI 线程上同步等这个信号量，
            // 续体一旦需要 UI 线程，两边就会互相等死。
            await Task.Delay(180).ConfigureAwait(false);
            if (version != Volatile.Read(ref _sessionSaveVersion)) return;
            await _sessionSaveSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                if (version != Volatile.Read(ref _sessionSaveVersion)) return;
                await Task.Run(() => _sessionCacheService.Write(character, projectPath, cache)).ConfigureAwait(false);
                // 第 2 步「规整素材」另外写一份**自己的**缓存文件（一步一个文件）：
                // 决策是这一步的私有数据，不该挤在所有步骤共用的大缓存里。
                // 整体缓存照旧写（兼容旧版本），但它对第 2 步只是备份，读的时候优先读下面这份。
                if (cache.NormalizationDecisions.Count > 0)
                {
                    await Task.Run(() => Step2NormalizationCache.Save(character, cache.NormalizationDecisions))
                        .ConfigureAwait(false);
                }
                // 第 5 步「序列同步」同样另写一份自己的缓存文件（一步一个文件）。
                // 用刚写的这份快照，两个文件里第五步的内容不会各说各话。
                await Task.Run(() => SaveSequenceSyncCacheFromSnapshot(character, cache)).ConfigureAwait(false);
                // 第 2 步「同步素材」也抄一份（同一份快照，所以两个地方不会各说各话）。
                await Task.Run(() => SaveMaterialSyncCacheFromSnapshot(character, cache)).ConfigureAwait(false);
                if (version == Volatile.Read(ref _sessionSaveVersion)) _pendingSessionCache = null;
            }
            finally
            {
                _sessionSaveSemaphore.Release();
            }
        }
        catch
        {
            // A later interaction or window-close flush will retry the latest snapshot.
        }
    }

    // ── SaveSelectionStateToSessionCache 搬到了 Step2MaterialSync.cs（FlushSessionCache 仍在这里）──

    public void FlushSessionCache()
    {
        var cache = _pendingSessionCache;
        var projectPath = _pendingSessionProjectPath;
        var character = _pendingSessionCharacter;
        if (cache is null || string.IsNullOrWhiteSpace(projectPath)) return;
        Interlocked.Increment(ref _sessionSaveVersion);
        // 这个方法会在 UI 线程上被调用，绝不能无限期阻塞：
        // 拿不到信号量就直接放弃这次落盘，交给防抖保存完成，界面不能因此卡死。
        if (!_sessionSaveSemaphore.Wait(TimeSpan.FromSeconds(2)))
        {
            return;
        }

        try
        {
            _sessionCacheService.Write(character, projectPath, cache);
            // 防抖那条路会被这次 flush 顶掉（版本号一变它就放弃），
            // 所以这里也得补写一次第五步自己的小文件，否则刚改的勾选只进了会话缓存。
            SaveSequenceSyncCacheFromSnapshot(character, cache);
            // 第 2 步同理。
            SaveMaterialSyncCacheFromSnapshot(character, cache);
            _pendingSessionCache = null;
        }
        finally
        {
            _sessionSaveSemaphore.Release();
        }
    }

    private void UpdateImportSelectionSummary()
    {
        if (!HasImportDetection)
        {
            return;
        }

        var leaves = SelectionTreeRoots.SelectMany(root => root.Children).ToArray();
        var selected = leaves.Where(item => item.IsChecked == true).ToArray();
        _importSelectedCount = selected.Length;
        if (IsEngineToToolbox)
        {
            _importUpdatedCount = selected.Count(item => _existingImportStableIds.Contains(item.StableId));
            _importAddedCount = selected.Length - _importUpdatedCount;
            _importAttentionCount = 0;
        }
        else
        {
            _importAddedCount = selected.Count(item => item.Change?.Kind == UnrealBridgeChangeKind.Added);
            _importUpdatedCount = selected.Count(item => item.Change?.Kind is
                UnrealBridgeChangeKind.Updated or UnrealBridgeChangeKind.Renamed);
            _importDeletedCount = selected.Count(item => item.Change?.Kind == UnrealBridgeChangeKind.DeleteCandidate);
            _importAttentionCount = leaves.Count(item => item.RequiresAttention ||
                item.Change is not null && !UnrealBridgePublishSupportPolicy.CanExecute(item.Change));
        }

        _importSkippedCount = leaves.Length - selected.Length;
        ImportSelectionCountText = $"已选择 {_importSelectedCount} / {leaves.Length} 项";
        ImportAddedCountText = $"新增 {_importAddedCount} 项";
        ImportUpdatedCountText = $"更新 {_importUpdatedCount} 项";
        ImportDeletedCountText = $"删除旧资产 {_importDeletedCount} 项";
        ImportAttentionCountText = $"需检查 {_importAttentionCount} 项";
        ImportSkippedCountText = $"{(IsEngineToToolbox ? "未选" : "未执行")} {_importSkippedCount} 项";
        ImportPrimaryActionText = _importSelectedCount > 0
            ? IsEngineToToolbox
                ? $"导入所选 {_importSelectedCount} 项"
                : $"同步所选 {_importSelectedCount} 项"
            : IsEngineToToolbox
                ? "请先选择导入内容"
                : "请先选择同步内容";
        var hasUnsupportedSelection = !IsEngineToToolbox && selected.Any(item =>
            item.Change is not null && !UnrealBridgePublishSupportPolicy.CanExecute(item.Change));
        CanImportSelection = CanSync && _importSelectedCount > 0 && !hasUnsupportedSelection;
        OnPropertyChanged(nameof(CanAdvanceWorkflow));
        OnPropertyChanged(nameof(IsPublishSelectionReady));
        OnPropertyChanged(nameof(HasPublishSelection));
        OnPropertyChanged(nameof(HasNoPublishChanges));
        OnPropertyChanged(nameof(WorkflowNextButtonEnabled));
        OnPropertyChanged(nameof(CanStartPublish));
        OnPropertyChanged(nameof(PublishActionText));
        OnPropertyChanged(nameof(PendingRedirectCount));
        OnPropertyChanged(nameof(PublishConflictCount));
        OnPropertyChanged(nameof(ReadyPublishCount));
        OnPropertyChanged(nameof(ReadyPublishText));
        OnPropertyChanged(nameof(PendingRedirectText));
        OnPropertyChanged(nameof(PublishConflictText));
    }

    // ── ApplyPublishFilter / ShouldShowPublishChild 搬到了 Step2MaterialSync.cs ──

    private void ResetImportOperation()
    {
        HasImportDetection = false;
        _loadedPublishStep = 0;
        OnPropertyChanged(nameof(HasContentDetection));
        ResetDetectionSummary();
        _existingImportStableIds.Clear();
        _importSelectedCount = 0;
        _importAddedCount = 0;
        _importUpdatedCount = 0;
        _importDeletedCount = 0;
        _importAttentionCount = 0;
        _importSkippedCount = 0;
        ImportOperationTitle = IsEngineToToolbox ? "等待内容检测" : "等待差异检测";
        ImportOperationMessage = IsEngineToToolbox
            ? "先选择角色，再检测可导入内容。"
            : "先选择已完成角色，再检测与 Unreal 的差异。";
        ImportSelectionCountText = "尚未检测";
        ImportAddedCountText = "新增 0 项";
        ImportUpdatedCountText = "更新 0 项";
        ImportDeletedCountText = "删除旧资产 0 项";
        ImportAttentionCountText = "需检查 0 项";
        ImportSkippedCountText = IsEngineToToolbox ? "未选 0 项" : "未执行 0 项";
        ImportPrimaryActionText = IsEngineToToolbox ? "导入所选到草稿" : "同步所选到虚幻";
        ImportResultMessage = string.Empty;
        ImportDetailVisibility = Visibility.Collapsed;
        ImportResultVisibility = Visibility.Collapsed;
        CanImportSelection = false;
        // 这里清掉了 _hasImportDetection 和 _loadedPublishStep，中栏状态、
        // 能否进下一步、发布勾选是否就绪全都跟着变。而本方法不动任何
        // ObservableCollection，所以中栏的集合监听在这条路径上也不会触发——
        // 不显式广播的话，中栏会停在上一刻的可见性上。
        NotifyWorkflowStateChanged();
    }

    private void RebuildSourceLists()
    {
        var query = SourceSearchText.Trim();
        FilteredSharedMaterialSources.Clear();
        foreach (var source in SharedMaterialSources.Where(source => MatchesSearch(source, query)))
        {
            FilteredSharedMaterialSources.Add(source);
        }

        CharacterSources.Clear();
        var sources = IsEngineToToolbox
            ? CharacterCandidates.Select(candidate => new UnrealSyncSourceItem(
                UnrealSyncSourceKind.UnrealCharacter,
                candidate.DisplayName,
                candidate.Code,
                $"{candidate.DisplayName} {candidate.Code}",
                UnrealCandidate: candidate))
            : _draftSources.Select(character => new UnrealSyncSourceItem(
                UnrealSyncSourceKind.DraftCharacter,
                character.DisplayName,
                character.Code,
                $"{character.DisplayName} {character.Code}",
                DraftCharacter: character));
        foreach (var source in sources.Where(source => MatchesSearch(source, query)))
        {
            CharacterSources.Add(source);
        }

        OnPropertyChanged(nameof(SourceGroupTitle));
    }

    private static bool MatchesSearch(UnrealSyncSourceItem source, string query) =>
        string.IsNullOrWhiteSpace(query) || source.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase);

}
