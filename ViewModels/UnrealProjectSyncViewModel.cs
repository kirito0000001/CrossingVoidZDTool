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

internal sealed class UnrealProjectSyncViewModel : ObservableObject
{
    private const int CurrentDetectionAlgorithmVersion = 4;
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
    private UnrealSyncPublishStageItem? _selectedPublishStage;
    private bool _isNormalizationWorkspace;
    private bool _isNormalizationStepLoaded;
    private readonly List<CharacterCard> _draftSources = [];
    private readonly HashSet<string> _existingImportStableIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<UnrealSyncSelectionTreeItem, UnrealSyncSelectionTreeItem> _selectionParents = [];
    private bool _hasImportDetection;
    private int _importSelectedCount;
    private int _importAddedCount;
    private int _importUpdatedCount;
    private int _importAttentionCount;
    private int _importSkippedCount;
    private string _importOperationTitle = "等待内容检测";
    private string _importOperationMessage = "先选择角色，再检测可导入内容。";
    private string _importSelectionCountText = "尚未检测";
    private string _importAddedCountText = "新增 0 项";
    private string _importUpdatedCountText = "更新 0 项";
    private string _importAttentionCountText = "需检查 0 项";
    private string _importSkippedCountText = "未选 0 项";
    private string _importPrimaryActionText = "导入所选到草稿";
    private string _importResultMessage = string.Empty;
    private Visibility _importDetailVisibility = Visibility.Collapsed;
    private Visibility _importResultVisibility = Visibility.Collapsed;
    private bool _canImportSelection;
    private bool _isPublishRunning;
    private bool _isWorkflowOperationRunning;
    private readonly UnrealSyncSessionCacheService _sessionCacheService = new();
    private readonly SemaphoreSlim _sessionSaveSemaphore = new(1, 1);
    private int _sessionSaveVersion;
    private UnrealSyncSessionCache? _loadedSessionCache;
    private UnrealSyncSessionCache? _pendingSessionCache;
    private string _pendingSessionProjectPath = string.Empty;
    private bool _sessionRestored;
    private bool _isRestoringSession;
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
    private List<UnrealLightConfigurationResultItem> _lastLightConfigurationItems = [];
    private bool _isLightConfigurationLoaded;
    private bool _isApplyingLightConfiguration;
    private string _lightConfigurationResultMessage = string.Empty;
    private string _publishFilter = "全部";
    private int _workflowStep = 1;

    public UnrealProjectSyncViewModel(UnrealProjectSyncService syncService)
    {
        _syncService = syncService;
        SharedMaterialSources.Add(new(UnrealSyncSourceKind.SharedMaterial, "通用音效", "项目共享素材", "通用音效 音效 BattleEffects", IsAvailable: false));
        SharedMaterialSources.Add(new(UnrealSyncSourceKind.SharedMaterial, "战斗 BGM", "项目共享素材", "战斗 BGM 音乐 Music", IsAvailable: false));
        SharedMaterialSources.Add(new(UnrealSyncSourceKind.SharedMaterial, "通用 BUFF 图标", "项目共享素材", "通用 BUFF 图标 BuffIcon", IsAvailable: false));
        SharedMaterialSources.Add(new(UnrealSyncSourceKind.SharedMaterial, "活动图片", "项目共享素材", "活动图片 EventImage", IsAvailable: false));
        SharedMaterialSources.Add(new(UnrealSyncSourceKind.SharedMaterial, "其他项目素材", "项目共享素材", "其他项目素材 Shared", IsAvailable: false));
        SelectedPublishStage = PublishStages[0];
    }

    public ObservableCollection<UnrealProjectSyncCheckItem> CheckItems { get; } = [];

    public ObservableCollection<UnrealProjectSyncCharacterCandidate> CharacterCandidates { get; } = [];

    public ObservableCollection<UnrealSyncSourceItem> SharedMaterialSources { get; } = [];

    public ObservableCollection<UnrealSyncSourceItem> FilteredSharedMaterialSources { get; } = [];

    public ObservableCollection<UnrealSyncSourceItem> CharacterSources { get; } = [];

    public ObservableCollection<UnrealSyncPublishStageItem> PublishStages { get; } =
    [
        new(UnrealBridgePublishStage.CharacterMaterials, "1. 导入素材", "角色目录内的图片和声音", true),
        new(UnrealBridgePublishStage.ItemData, "2. 同步 Item 数据", "更新 Item 蓝图中的工具箱管理字段", false),
        new(UnrealBridgePublishStage.ZdAnimationTracks, "3. 同步 ZD 动画轨道", "合并基础序列、AnimMaps 引用和保留通知", false),
        new(UnrealBridgePublishStage.Buffs, "4. 同步 BUFF", "更新 BUFF 数据和个人 BUFF 素材", false),
        new(UnrealBridgePublishStage.CharacterBlueprint, "5. 同步 Character 蓝图数据", "更新 Character 蓝图白名单字段", false)
    ];

    public ObservableCollection<UnrealSyncSelectionTreeItem> SelectionTreeRoots { get; } = [];
    public ObservableCollection<UnrealLightConfigurationViewItem> LightConfigurationItems { get; } = [];
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

    public string FoundationSummaryText => $"共 {FoundationChecks.Count} 项：合规 {FoundationChecks.Count(item => item.IsCompliant)}，待处理 {FoundationChecks.Count(item => !item.IsCompliant)}";
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

    public ObservableCollection<UnrealAssetNormalizationItem> NormalizationItems { get; } = [];
    private IReadOnlyList<UnrealAssetNormalizationItem> _visibleNormalizationItems = [];
    public IReadOnlyList<UnrealAssetNormalizationItem> VisibleNormalizationItems
    {
        get => _visibleNormalizationItems;
        private set => SetProperty(ref _visibleNormalizationItems, value);
    }
    private bool _hideResolvedNormalizationItems;

    public bool HideResolvedNormalizationItems
    {
        get => _hideResolvedNormalizationItems;
        set
        {
            if (SetProperty(ref _hideResolvedNormalizationItems, value))
            {
                RefreshVisibleNormalizationItems();
                SaveSessionCache();
            }
        }
    }

    public bool IsNormalizationWorkspace
    {
        get => _isNormalizationWorkspace;
        private set
        {
            if (SetProperty(ref _isNormalizationWorkspace, value))
            {
                OnPropertyChanged(nameof(IsDetectionWorkspace));
                OnPropertyChanged(nameof(NormalizationEmptyVisibility));
                OnPropertyChanged(nameof(SelectionEmptyVisibility));
                OnPropertyChanged(nameof(SelectionContentVisibility));
                OnPropertyChanged(nameof(DetectionResultVisibility));
            }
        }
    }

    public bool IsDetectionWorkspace => !IsNormalizationWorkspace;
    public Visibility NormalizationEmptyVisibility => IsNormalizationWorkspace &&
        _isNormalizationStepLoaded && VisibleNormalizationItems.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;
    public bool IsFoundationWorkspace => !IsEngineToToolbox && WorkflowStep == 1;
    public bool IsLightConfigurationWorkspace => !IsEngineToToolbox && WorkflowStep == 4;
    public bool IsSequenceSynchronizationWorkspace => !IsEngineToToolbox && WorkflowStep == 5;
    public Visibility FoundationWorkspaceVisibility => IsFoundationWorkspace ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FoundationDetailsVisibility => IsFoundationWorkspace ? Visibility.Visible : Visibility.Collapsed;
    public Visibility LightConfigurationWorkspaceVisibility => IsLightConfigurationWorkspace && LightConfigurationItems.Count > 0
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility LightConfigurationEmptyVisibility => IsLightConfigurationWorkspace &&
        _isLightConfigurationLoaded && LightConfigurationItems.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility LightConfigurationDetailsVisibility => IsLightConfigurationWorkspace
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility SequenceSynchronizationDetailsVisibility => IsSequenceSynchronizationWorkspace
        ? Visibility.Visible
        : Visibility.Collapsed;
    public string WorkspaceTitle => IsEngineToToolbox ? "检测与选择" : WorkflowStep switch
    {
        1 => "底层检测",
        2 => "素材规整",
        3 => "同步素材",
        4 => "基础配置",
        5 => "序列同步",
        _ => "同步结果"
    };
    public string WorkspaceDescription => IsEngineToToolbox
        ? "展开模块，勾选本次需要处理的具体内容。"
        : WorkflowStep switch
        {
            1 => "检查 Unreal 目录和角色 Item 是否符合规范。",
            2 => "确认 Unreal 旧素材与工具箱规范素材的对应关系。",
            3 => "勾选本次需要同步到 Unreal 的素材。",
            4 => "检查并应用角色入队语音、Item、MetaSound 和语音并发设置。",
            5 => "同步当前角色的序列、帧素材、AnimMaps 映射和语音轨道。",
            _ => "查看最近一次同步执行结果。"
        };

    public int LightConfigurationPendingCount => _lastLightConfigurationItems.Count(item =>
        item.Status == UnrealLightConfigurationStatus.Pending);
    public int LightConfigurationErrorCount => _lastLightConfigurationItems.Count(item =>
        item.Status == UnrealLightConfigurationStatus.Error);
    public int LightConfigurationUnchangedCount => _lastLightConfigurationItems.Count(item =>
        item.Status == UnrealLightConfigurationStatus.Unchanged);
    public int LightConfigurationSelectedCount => LightConfigurationItems.Count(item => item.IsSelected);
    public string LightConfigurationSummaryText => !_isLightConfigurationLoaded
        ? "尚未检测基础配置"
        : $"共检查 {_lastLightConfigurationItems.Count} 项：无差异 {LightConfigurationUnchangedCount}，待设置 {LightConfigurationPendingCount}，错误 {LightConfigurationErrorCount}";
    public string LightConfigurationEmptyTitle => LightConfigurationErrorCount > 0
        ? "基础配置存在错误"
        : "基础配置没有改动";
    public string LightConfigurationSelectionText => $"已选择 {LightConfigurationSelectedCount} / {LightConfigurationPendingCount} 项";
    public string LightConfigurationResultMessage => _lightConfigurationResultMessage;
    public bool IsLightConfigurationLoaded => _isLightConfigurationLoaded;
    public bool CanApplyLightConfiguration => IsLightConfigurationWorkspace &&
        _isLightConfigurationLoaded &&
        LightConfigurationSelectedCount > 0 &&
        !_isApplyingLightConfiguration &&
        IsWorkflowOperationIdle;

    public string NormalizationSummaryText
    {
        get
        {
            var actionableItems = NormalizationItems.Where(item => !item.IsAlreadyNormalized).ToArray();
            return actionableItems.Length == 0
                ? "没有需要规整的 Unreal 素材"
                : $"共 {actionableItems.Length} 项：已处理 {actionableItems.Count(item => item.IsResolved)}，待处理 {actionableItems.Count(item => !item.IsResolved)}";
        }
    }

    public bool CanAdvanceWorkflow => WorkflowStep switch
    {
        1 => SelectedSource?.DraftCharacter is not null &&
            FoundationChecks.Count > 0 && FoundationChecks.All(item => item.IsCompliant),
        2 => SelectedSource?.DraftCharacter is not null &&
            _isNormalizationStepLoaded && NormalizationItems.All(item => item.IsResolved),
        3 => IsPublishSelectionReady,
        4 => _isLightConfigurationLoaded &&
            LightConfigurationPendingCount == 0 &&
            LightConfigurationErrorCount == 0,
        5 => false,
        _ => false
    };

    public bool IsNormalizationStepLoaded => _isNormalizationStepLoaded;

    public bool HasPublishSelection => !IsEngineToToolbox && _importSelectedCount > 0;
    public bool HasNoPublishChanges => !IsEngineToToolbox && WorkflowStep is 3 or 5 &&
        _hasImportDetection &&
        _lastPublishChanges.All(change => change.Kind == UnrealBridgeChangeKind.Unchanged);
    public bool CanStartPublish => HasPublishSelection &&
        !IsPublishRunning && IsWorkflowOperationIdle;
    public string PublishActionText => WorkflowStep == 5 ? "同步序列到虚幻" : "同步到虚幻";

    public bool IsWorkflowOperationIdle => !_isWorkflowOperationRunning;

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

    public void SetWorkflowOperationRunning(bool value)
    {
        if (SetProperty(ref _isWorkflowOperationRunning, value))
        {
            OnPropertyChanged(nameof(IsWorkflowOperationIdle));
            OnPropertyChanged(nameof(CanStartPublish));
            OnPropertyChanged(nameof(CanDetectSelectedSource));
            OnPropertyChanged(nameof(WorkflowNextButtonEnabled));
            OnPropertyChanged(nameof(CanApplyLightConfiguration));
        }
    }

    public string ContentDetectionStatusText => _lastContentDetectionAt is DateTimeOffset detected
        ? $"上次检测：{detected.LocalDateTime:yyyy-MM-dd HH:mm:ss}（打开页面不会自动重检，同步前会强制刷新）"
        : "尚未检测内容";

    public Visibility DetectionResultVisibility =>
        !IsNormalizationWorkspace && !IsFoundationWorkspace && !IsLightConfigurationWorkspace &&
        HasContentDetection && SelectionTreeRoots.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string DetectionResultTitle => IsEngineToToolbox
        ? "内容检测完成"
        : DetectionChangedCount == 0
            ? "本次没有改动"
            : $"检测到 {DetectionChangedCount} 项改动";

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
        OnPropertyChanged(nameof(DetectionResultTitle));
        OnPropertyChanged(nameof(DetectionResultSummaryText));
    }

    public bool HasContentDetection => _hasImportDetection;

    public int WorkflowStep
    {
        get => _workflowStep;
        private set
        {
            if (SetProperty(ref _workflowStep, Math.Clamp(value, 1, 5)))
            {
                OnPropertyChanged(nameof(WorkflowStep1StatusText));
                OnPropertyChanged(nameof(WorkflowStep2StatusText));
                OnPropertyChanged(nameof(WorkflowStep3StatusText));
                OnPropertyChanged(nameof(WorkflowStep4StatusText));
                OnPropertyChanged(nameof(WorkflowStep5StatusText));
                OnPropertyChanged(nameof(WorkflowNextText));
                OnPropertyChanged(nameof(WorkflowReloadText));
                OnPropertyChanged(nameof(WorkflowConfirmationVisibility));
                OnPropertyChanged(nameof(NormalizationDetailsVisibility));
                OnPropertyChanged(nameof(WorkflowNextButtonVisibility));
                OnPropertyChanged(nameof(WorkflowNextButtonEnabled));
                OnPropertyChanged(nameof(IsFoundationWorkspace));
                OnPropertyChanged(nameof(IsLightConfigurationWorkspace));
                OnPropertyChanged(nameof(IsSequenceSynchronizationWorkspace));
                OnPropertyChanged(nameof(FoundationWorkspaceVisibility));
                OnPropertyChanged(nameof(FoundationDetailsVisibility));
                OnPropertyChanged(nameof(LightConfigurationWorkspaceVisibility));
                OnPropertyChanged(nameof(LightConfigurationEmptyVisibility));
                OnPropertyChanged(nameof(LightConfigurationDetailsVisibility));
                OnPropertyChanged(nameof(SequenceSynchronizationDetailsVisibility));
                OnPropertyChanged(nameof(WorkspaceTitle));
                OnPropertyChanged(nameof(WorkspaceDescription));
                OnPropertyChanged(nameof(SelectionEmptyVisibility));
                OnPropertyChanged(nameof(SelectionContentVisibility));
                OnPropertyChanged(nameof(DetectionResultVisibility));
                OnPropertyChanged(nameof(CanAdvanceWorkflow));
                OnPropertyChanged(nameof(CanApplyLightConfiguration));
                OnPropertyChanged(nameof(HasNoPublishChanges));
                OnPropertyChanged(nameof(CanStartPublish));
                OnPropertyChanged(nameof(PublishActionText));
                SaveSessionCache();
            }
        }
    }

    public string WorkflowStep1StatusText => WorkflowStep > 1 ? "已完成" : WorkflowStep == 1 ? "进行中" : "待处理";
    public string WorkflowStep2StatusText => WorkflowStep > 2 ? "已完成" : WorkflowStep == 2 ? "进行中" : "待处理";
    public string WorkflowStep3StatusText => WorkflowStep > 3 ? "已完成" : WorkflowStep == 3 ? "进行中" : "待处理";
    public string WorkflowStep4StatusText => WorkflowStep < 4
        ? "待处理"
        : !_isLightConfigurationLoaded
            ? "进行中"
            : LightConfigurationErrorCount > 0
                ? "有错误"
                : LightConfigurationPendingCount > 0
                    ? "待设置"
                    : "已完成";
    public string WorkflowStep5StatusText => WorkflowStep < 5
        ? "待处理"
        : !HasContentDetection
            ? "待检测"
            : "进行中";
    public string WorkflowNextText => WorkflowStep switch
    {
        1 => "规整素材",
        2 => "同步素材",
        3 => "基础配置",
        4 => "序列同步",
        _ => "已完成"
    };
    public string WorkflowReloadText => WorkflowStep switch
    {
        1 => "重新加载底层检测",
        2 => "重新加载规整素材",
        3 => "重新加载同步素材",
        4 => "重新加载基础配置",
        5 => "重新加载序列同步",
        _ => "重新加载同步结果"
    };

    public Visibility WorkflowConfirmationVisibility => WorkflowStep is 3 or 5 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NormalizationDetailsVisibility => WorkflowStep == 2 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility WorkflowNextButtonVisibility => Visibility.Visible;
    public bool WorkflowNextButtonEnabled => WorkflowStep switch
    {
        3 => HasNoPublishChanges && IsWorkflowOperationIdle,
        4 => CanAdvanceWorkflow && IsWorkflowOperationIdle,
        5 => false,
        _ => WorkflowStep < 3 && CanAdvanceWorkflow && IsWorkflowOperationIdle
    };

    public bool IsPublishSelectionReady
    {
        get
        {
            if (IsEngineToToolbox || !_hasImportDetection)
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
                OnPropertyChanged(nameof(WorkspaceDescription));
                OnPropertyChanged(nameof(DetectionResultVisibility));
                NotifyDetectionSummaryChanged();
                ClearLightConfigurationState();
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

    public Visibility SelectionEmptyVisibility => !IsNormalizationWorkspace && !IsFoundationWorkspace && !IsLightConfigurationWorkspace &&
        SelectionTreeRoots.Count == 0 && !HasContentDetection
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility SelectionContentVisibility => !IsNormalizationWorkspace && !IsFoundationWorkspace && !IsLightConfigurationWorkspace &&
        SelectionTreeRoots.Count > 0
        ? Visibility.Visible
        : Visibility.Collapsed;

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

    public UnrealSyncPublishStageItem? SelectedPublishStage
    {
        get => _selectedPublishStage;
        set
        {
            if (SetProperty(ref _selectedPublishStage, value))
            {
                _hasImportDetection = false;
                OnPropertyChanged(nameof(HasContentDetection));
                OnPropertyChanged(nameof(WorkflowStep5StatusText));
                SetSelectionTree([]);
                ResetImportOperation();
                OnPropertyChanged(nameof(PublishStageDescription));
                SaveSessionCache();
            }
        }
    }

    public string PublishStageDescription => SelectedPublishStage?.DetailText ?? "请选择要执行的同步阶段。";

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
            _isNormalizationStepLoaded = false;
            NormalizationItems.Clear();
            VisibleNormalizationItems = [];
            FoundationChecks.Clear();
            VisibleFoundationChecks = [];
            IsNormalizationWorkspace = false;
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

    public void ValidatePublishCharacterFolders(string characterCode, bool requireAssetTypes = false)
    {
        RefreshFoundationChecks(characterCode, requireAssetTypes);
        _syncService.ValidatePublishCharacterFolders(ProjectPath, characterCode, requireAssetTypes);
    }

    public void ValidateSequenceCharacterFolders(string projectPath, string characterCode) =>
        _syncService.ValidateSequenceCharacterFolders(projectPath, characterCode);

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
        var sameDetectedSource = (_hasImportDetection || _isLightConfigurationLoaded) &&
            string.Equals(previousCode, nextCode, StringComparison.OrdinalIgnoreCase);
        sameDetectedSource |= _isNormalizationStepLoaded &&
            string.Equals(previousCode, nextCode, StringComparison.OrdinalIgnoreCase);
        var sameSource = string.Equals(previousCode, nextCode, StringComparison.OrdinalIgnoreCase);
        SelectedSource = source;
        if (!sameDetectedSource)
        {
            SetSelectionTree([]);
            ResetImportOperation();
            ClearLightConfigurationState();
            CloseNormalizationWorkspace();
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
            if (!sameSource || FoundationChecks.Count == 0)
            {
                Detect();
                RefreshFoundationChecks(nextCode);
            }
        }
        SaveSessionCache();
    }

    public bool OpenNormalizationWorkspace(bool activateWorkspace = true)
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
        var stepCache = _sessionCacheService.LoadStep(ProjectPath, character.Code, 2).Cache;
        var cachedDecisions = stepCache?.NormalizationDecisions ??
            (inMemoryCache is not null && string.Equals(
                inMemoryCache.SelectedCharacterCode,
                character.Code,
                StringComparison.OrdinalIgnoreCase)
                ? inMemoryCache.NormalizationDecisions
                : _sessionCacheService.Load(ProjectPath, character.Code).Cache?.NormalizationDecisions ?? []);
        var rebuiltItems = BuildNormalizationItems(character, candidate, cachedDecisions);
        ApplyNormalizationItems(rebuiltItems, activateWorkspace);
        return true;
    }

    public async Task<bool> OpenNormalizationWorkspaceAsync(bool activateWorkspace = true)
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
        var cachedDecisions = inMemoryCache is not null && string.Equals(
                inMemoryCache.SelectedCharacterCode,
                character.Code,
                StringComparison.OrdinalIgnoreCase)
            ? inMemoryCache.NormalizationDecisions
            : _sessionCacheService.Load(ProjectPath, character.Code).Cache?.NormalizationDecisions ?? [];
        var rebuiltItems = await Task.Run(() => BuildNormalizationItems(character, candidate, cachedDecisions));
        ApplyNormalizationItems(rebuiltItems, activateWorkspace);
        return true;
    }

    private static IReadOnlyList<UnrealAssetNormalizationItem> BuildNormalizationItems(
        CharacterCard character,
        UnrealProjectSyncCharacterCandidate candidate,
        IReadOnlyDictionary<string, string> cachedDecisions)
    {
        var rebuiltItems = new UnrealAssetNormalizationService().Build(character, candidate).ToArray();
        foreach (var item in rebuiltItems)
        {
            var decisionFound = cachedDecisions.TryGetValue(item.StableId, out var decision);
            if (!decisionFound)
            {
                var assetName = item.UnrealObjectPath.Split('/').LastOrDefault()?.Split('.', 2)[0];
                if (!string.IsNullOrWhiteSpace(assetName))
                {
                    var legacyDecision = cachedDecisions.FirstOrDefault(pair =>
                        pair.Key.EndsWith($"/{assetName}.{assetName}", StringComparison.OrdinalIgnoreCase));
                    decisionFound = !string.IsNullOrWhiteSpace(legacyDecision.Key);
                    decision = legacyDecision.Value;
                }
            }

            if (decisionFound && !string.IsNullOrWhiteSpace(decision))
            {
                if (string.Equals(decision, "__not_required__", StringComparison.Ordinal))
                {
                    item.MarkNotRequired();
                }
                else
                {
                    var selected = item.Candidates.FirstOrDefault(candidateItem => string.Equals(candidateItem.StableId, decision, StringComparison.OrdinalIgnoreCase));
                    var identityId = decision.StartsWith("material:", StringComparison.OrdinalIgnoreCase)
                        ? decision["material:".Length..]
                        : decision.StartsWith("voice:", StringComparison.OrdinalIgnoreCase)
                            ? decision["voice:".Length..]
                            : decision;
                    if (selected is null &&
                        new UnrealBridgeToolboxIdentityService().TryResolveAssignedPath(
                            character,
                            item.Module,
                            identityId,
                            out var assignedPath))
                    {
                        selected = item.Candidates.FirstOrDefault(candidateItem =>
                            string.Equals(Path.GetFullPath(candidateItem.AssetPath), Path.GetFullPath(assignedPath), StringComparison.OrdinalIgnoreCase));
                    }

                    if (selected is not null) item.SelectRedirect(selected);
                }
            }

        }

        return rebuiltItems;
    }

    private void ApplyNormalizationItems(
        IReadOnlyList<UnrealAssetNormalizationItem> rebuiltItems,
        bool activateWorkspace)
    {
        NormalizationItems.Clear();
        foreach (var item in rebuiltItems)
        {
            NormalizationItems.Add(item);
        }

        RefreshVisibleNormalizationItems();
        SetNormalizationStepLoaded(true);

        if (activateWorkspace)
        {
            IsNormalizationWorkspace = true;
            WorkflowStep = 2;
        }
        OnPropertyChanged(nameof(NormalizationSummaryText));
        OnPropertyChanged(nameof(CanAdvanceWorkflow));
    }

    public void SelectNormalizationRedirect(UnrealAssetNormalizationItem item, UnrealAssetNormalizationCandidate candidate)
    {
        item.SelectRedirect(candidate);
        NormalizationResolutionChanged();
    }

    public void MarkNormalizationNotRequired(UnrealAssetNormalizationItem item)
    {
        item.MarkNotRequired();
        NormalizationResolutionChanged();
    }

    public void ClearNormalizationRedirect(UnrealAssetNormalizationItem item)
    {
        item.ClearRedirect();
        NormalizationResolutionChanged();
    }

    public void BeginNormalizationStepLoad() => SetNormalizationStepLoaded(false);

    private void SetNormalizationStepLoaded(bool value)
    {
        if (_isNormalizationStepLoaded == value)
        {
            return;
        }

        _isNormalizationStepLoaded = value;
        OnPropertyChanged(nameof(IsNormalizationStepLoaded));
        OnPropertyChanged(nameof(NormalizationEmptyVisibility));
        OnPropertyChanged(nameof(CanAdvanceWorkflow));
        OnPropertyChanged(nameof(WorkflowNextButtonEnabled));
        SaveSessionCache();
    }

    private void RefreshVisibleNormalizationItems()
    {
        VisibleNormalizationItems = NormalizationItems.Where(item =>
                !item.IsAlreadyNormalized &&
                (!HideResolvedNormalizationItems || !item.IsResolved))
            .ToArray();
        OnPropertyChanged(nameof(NormalizationEmptyVisibility));
        OnPropertyChanged(nameof(NormalizationSummaryText));
    }

    private void RefreshVisibleFoundationChecks()
    {
        VisibleFoundationChecks = FoundationChecks
            .Where(item => !HideCompletedFoundationChecks || !item.IsCompliant)
            .ToArray();
    }

    public void CloseNormalizationWorkspace()
    {
        IsNormalizationWorkspace = false;
        if (WorkflowStep == 2)
        {
            WorkflowStep = 1;
        }
    }

    public void ReturnToWorkflowStep(int step)
    {
        step = Math.Clamp(step, 1, 5);
        // 切换步骤会触发 WorkflowStep 的保存。恢复前先抑制这次保存，
        // 避免用当前步骤的空显示树覆盖目标步骤已有缓存。
        _isRestoringSession = true;
        try
        {
            if (step == 5)
            {
                SelectedPublishStage = PublishStages.FirstOrDefault(item => item.Stage == UnrealBridgePublishStage.ZdAnimationTracks);
                IsNormalizationWorkspace = false;
                WorkflowStep = 5;
                RestoreWorkflowStepCache(5);
                return;
            }

            if (step == 2)
            {
                IsNormalizationWorkspace = true;
                WorkflowStep = 2;
                RestoreWorkflowStepCache(2);
                return;
            }

            WorkflowStep = step;
            IsNormalizationWorkspace = false;
            RestoreWorkflowStepCache(step);
            if (step == 1 && FoundationChecks.Count == 0 &&
                SelectedSource?.DraftCharacter is { Code: var characterCode })
            {
                Detect();
                RefreshFoundationChecks(characterCode);
            }
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

        var result = _sessionCacheService.LoadStep(ProjectPath, characterCode, step);
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
                RestoreNormalizationItems(cache.NormalizationItems);
                _isNormalizationStepLoaded = cache.IsNormalizationStepLoaded || cache.NormalizationItems.Count > 0;
                OnPropertyChanged(nameof(IsNormalizationStepLoaded));
                return;
            }

            if (step == 4 && cache.IsLightConfigurationLoaded)
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

            if (step is 3 or 5 && cache.IsPublishDetection)
            {
                var changes = FilterCachedPublishChanges(cache, SelectedSource?.DraftCharacter).ToArray();
                var roots = step == 5
                    ? UnrealSyncSelectionTreeBuilder.FromSequenceChanges(changes, UnrealBridgePublishSupportPolicy.CanExecute)
                    : UnrealSyncSelectionTreeBuilder.FromChanges(changes, UnrealBridgePublishSupportPolicy.CanExecute);
                if (step != 5)
                {
                    var selectedIds = cache.SelectedStableIds.Count > 0
                        ? cache.SelectedStableIds
                        : cache.PublishChanges.Where(change => change.IsSelected)
                            .Select(change => change.StableId)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    ApplySelection(roots, selectedIds);
                }
                SetPublishSelectionTree(roots, changes);
            }
        }
        finally
        {
            _isRestoringSession = false;
        }
    }

    public bool AdvanceWorkflowStep()
    {
        if (WorkflowStep == 1)
        {
            if (!HasContentDetection)
            {
                return false;
            }

            return OpenNormalizationWorkspace();
        }

        if (WorkflowStep == 2)
        {
            if (NormalizationItems.Any(item => !item.IsResolved))
            {
                return false;
            }

            IsNormalizationWorkspace = false;
            if (!RefreshPublishTreeDisplay())
            {
                return false;
            }
            WorkflowStep = 3;
            return true;
        }

        return WorkflowStep == 3;
    }

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

        OnPropertyChanged(nameof(SelectionEmptyVisibility));
        OnPropertyChanged(nameof(SelectionContentVisibility));
        OnPropertyChanged(nameof(DetectionResultVisibility));
        UpdateImportSelectionSummary();
        ApplyPublishFilter();
    }

    public void SetImportSelectionTree(
        IEnumerable<UnrealSyncSelectionTreeItem> roots,
        IReadOnlySet<string> existingStableIds,
        UnrealBridgeSnapshot? snapshot = null)
    {
        var rootList = roots.ToArray();
        _existingImportStableIds.Clear();
        _existingImportStableIds.UnionWith(existingStableIds);
        _hasImportDetection = true;
        OnPropertyChanged(nameof(HasContentDetection));
        OnPropertyChanged(nameof(WorkflowStep5StatusText));
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
        OnPropertyChanged(nameof(DetectionResultVisibility));
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

    public void SetPublishSelectionTree(
        IEnumerable<UnrealSyncSelectionTreeItem> roots,
        IReadOnlyCollection<UnrealBridgeChange>? changes = null)
    {
        _existingImportStableIds.Clear();
        _hasImportDetection = true;
        OnPropertyChanged(nameof(HasContentDetection));
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
        OnPropertyChanged(nameof(CanStartPublish));
        OnPropertyChanged(nameof(PublishActionText));
        _lastContentDetectionAt = DateTimeOffset.Now;
        OnPropertyChanged(nameof(ContentDetectionStatusText));
        OnPropertyChanged(nameof(DetectionResultVisibility));
        SaveSessionCache();
    }

    public async Task SetPublishSelectionTreeAsync(
        IReadOnlyCollection<UnrealBridgeChange> changes,
        Func<UnrealBridgeChange, bool>? canExecute = null,
        CancellationToken cancellationToken = default)
    {
        var roots = await Task.Run(
            () => WorkflowStep == 5 || SelectedPublishStage?.Stage == UnrealBridgePublishStage.ZdAnimationTracks
                ? UnrealSyncSelectionTreeBuilder.FromSequenceChanges(changes, canExecute)
                : UnrealSyncSelectionTreeBuilder.FromChanges(changes, canExecute),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        SetPublishSelectionTree(roots, changes);
    }

    public IReadOnlyList<UnrealBridgeChange> FilterPublishChanges(IReadOnlyList<UnrealBridgeChange> changes)
    {
        if (WorkflowStep == 5 || SelectedPublishStage?.Stage == UnrealBridgePublishStage.ZdAnimationTracks)
        {
            return changes.Where(change => change.Module == UnrealBridgeModule.SequenceFrames).ToArray();
        }

        if (SelectedPublishStage?.Stage != UnrealBridgePublishStage.CharacterMaterials)
        {
            return [];
        }

        return changes
            .Where(change => change.Module is UnrealBridgeModule.BaseMaterials or UnrealBridgeModule.Voices)
            .ToArray();
    }

    public bool MatchesCurrentPublishChanges(IReadOnlyList<UnrealBridgeChange> latestChanges)
    {
        var current = _lastPublishChanges
            .Select(GetPublishChangeFingerprint)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var latest = latestChanges
            .Select(GetPublishChangeFingerprint)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return current.SequenceEqual(latest, StringComparer.OrdinalIgnoreCase);
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
        _hasImportDetection = false;
        OnPropertyChanged(nameof(HasContentDetection));
        OnPropertyChanged(nameof(WorkflowStep5StatusText));
        OnPropertyChanged(nameof(DetectionResultVisibility));
        ResetDetectionSummary();
        OnPropertyChanged(nameof(IsPublishSelectionReady));
        OnPropertyChanged(nameof(HasPublishSelection));
        OnPropertyChanged(nameof(HasNoPublishChanges));
        OnPropertyChanged(nameof(CanStartPublish));
        OnPropertyChanged(nameof(PublishActionText));
        WorkflowStep = 4;
        ImportOperationTitle = "素材同步完成";
        ImportOperationMessage = "正在进入第四步基础配置。";
        ImportResultMessage = $"已验证 {executedCount} 项，保留未执行 {deferredCount} 项。";
        ImportResultVisibility = Visibility.Collapsed;
    }

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

        _isLightConfigurationLoaded = true;
        _lightConfigurationResultMessage = result.Succeeded
            ? result.AppliedStableIds.Count > 0
                ? $"已应用并验证 {result.AppliedStableIds.Count} 项配置。"
                : "基础配置检测完成。"
            : string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? "基础配置存在未完成项目。"
                : result.ErrorMessage;
        NotifyLightConfigurationChanged();
        SaveSessionCache();
    }

    public IReadOnlySet<string> GetSelectedLightConfigurationIds() =>
        LightConfigurationItems
            .Where(item => item.IsSelected && item.IsSelectable)
            .Select(item => item.StableId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

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
    }

    private void LightConfigurationItem_SelectionChanged(object? sender, EventArgs e)
    {
        NotifyLightConfigurationChanged();
        SaveSessionCache();
    }

    private void ClearLightConfigurationState()
    {
        foreach (var item in LightConfigurationItems)
        {
            item.SelectionChanged -= LightConfigurationItem_SelectionChanged;
        }

        LightConfigurationItems.Clear();
        _lastLightConfigurationItems.Clear();
        _isLightConfigurationLoaded = false;
        _isApplyingLightConfiguration = false;
        _lightConfigurationResultMessage = string.Empty;
        NotifyLightConfigurationChanged();
    }

    private void NotifyLightConfigurationChanged()
    {
        OnPropertyChanged(nameof(IsLightConfigurationLoaded));
        OnPropertyChanged(nameof(LightConfigurationWorkspaceVisibility));
        OnPropertyChanged(nameof(LightConfigurationEmptyVisibility));
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
    }

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
        if (IsEngineToToolbox || SelectionTreeRoots.Count == 0)
        {
            ResetImportOperation();
        }
        ImportOperationTitle = IsEngineToToolbox ? "内容检测失败" : "差异检测失败";
        ImportOperationMessage = message;
    }

    public IReadOnlySet<string> GetSelectedStableIds() =>
        UnrealSyncSelectionTreeBuilder.SelectedStableIds(SelectionTreeRoots);

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

            UpdateImportSelectionSummary();
            ApplyPublishFilter();
            SaveSessionCache();
        }
    }

    private void SelectionGroup_GroupSelectionChanged(object? sender, EventArgs e)
    {
        UpdateImportSelectionSummary();
        ApplyPublishFilter();
        SaveSessionCache();
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
        var loadResult = string.IsNullOrWhiteSpace(preferredCharacterCode)
            ? _sessionCacheService.Load(ProjectPath)
            : _sessionCacheService.Load(ProjectPath, preferredCharacterCode);
        var cache = loadResult.Cache;
        if (loadResult.Status != UnrealSyncSessionCacheLoadStatus.Loaded || cache is null)
        {
            return loadResult;
        }
        _loadedSessionCache = cache;

        if (!string.Equals(cache.EnginePath, EnginePath, StringComparison.OrdinalIgnoreCase))
        {
            return new(UnrealSyncSessionCacheLoadStatus.Invalid, ErrorMessage: "同步进度使用的 Unreal 引擎路径与当前设置不一致。");
        }

        // 第四步会在进入时清理第三步的差异树标记，因此不能只靠
        // IsPublishDetection 判断是否存在可恢复的同步进度。
        var hasProgress = cache.IsPublishDetection ||
            cache.ImportSnapshot is not null ||
            cache.IsLightConfigurationLoaded ||
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
            WorkflowStep = cache.WorkflowStep is >= 1 and <= 5 ? cache.WorkflowStep : 1;
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
            _isNormalizationStepLoaded = cache.IsNormalizationStepLoaded ||
                cache.WorkflowStep >= 3 || cache.NormalizationItems.Count > 0;
            OnPropertyChanged(nameof(IsNormalizationStepLoaded));
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
                    _hasImportDetection = false;
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
                    var cachedComparison = FilterPublishChanges(cache.PublishChanges).ToArray();
                    var cachedChanges = FilterCachedPublishChanges(cache, source.DraftCharacter).ToArray();
                    var roots = cache.WorkflowStep == 5
                        ? UnrealSyncSelectionTreeBuilder.FromSequenceChanges(cachedChanges, UnrealBridgePublishSupportPolicy.CanExecute)
                        : UnrealSyncSelectionTreeBuilder.FromChanges(cachedChanges, UnrealBridgePublishSupportPolicy.CanExecute);
                    if (cache.WorkflowStep != 5)
                    {
                        var selectedIds = cache.SelectedStableIds.Count > 0
                            ? cache.SelectedStableIds
                            : cache.PublishChanges.Where(change => change.IsSelected).Select(change => change.StableId).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        ApplySelection(roots, selectedIds);
                    }
                    if (cache.DetectionTotalCount == 0 && cachedChanges.Length > 0)
                    {
                        SetPublishDetectionSummary(cachedChanges);
                    }
                    SetPublishSelectionTree(roots, cachedComparison);
                }
            }
            else
            {
                var roots = UnrealSyncSelectionTreeBuilder.FromSnapshot(cache.ImportSnapshot!);
                ApplySelection(roots, cache.SelectedStableIds);
                SetImportSelectionTree(roots, new HashSet<string>(StringComparer.OrdinalIgnoreCase), cache.ImportSnapshot);
            }

            _lastContentDetectionAt = cache.DetectedAt == default ? null : cache.DetectedAt;
            IsNormalizationWorkspace = cache.IsPublishDetection && WorkflowStep == 2;
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
        var changes = FilterPublishChanges(cache.PublishChanges)
            .Where(change => change.Kind != UnrealBridgeChangeKind.Unchanged)
            .ToArray();
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
            string.Equals(change.ToolboxItem.ContentHash, entry.ToolboxHash, StringComparison.OrdinalIgnoreCase);
        var unrealMatches = change.UnrealItem is not null &&
            string.Equals(change.UnrealItem.ContentHash, entry.UnrealHash, StringComparison.OrdinalIgnoreCase);
        return toolboxMatches && (unrealMatches || change.UnrealItem is null);
    }

    private static void ApplySelection(IEnumerable<UnrealSyncSelectionTreeItem> roots, IReadOnlySet<string> selectedIds)
    {
        foreach (var leaf in roots.SelectMany(root => root.Children))
        {
            leaf.IsChecked = selectedIds.Contains(leaf.StableId);
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
            IsNormalizationStepLoaded = _isNormalizationStepLoaded,
            HideCompletedFoundationChecks = HideCompletedFoundationChecks,
            HideResolvedNormalizationItems = HideResolvedNormalizationItems,
            IsLightConfigurationLoaded = _isLightConfigurationLoaded,
            LightConfigurationItems = _lastLightConfigurationItems.ToList(),
            SelectedLightConfigurationIds = GetSelectedLightConfigurationIds().ToHashSet(StringComparer.OrdinalIgnoreCase),
            LightConfigurationResultMessage = _lightConfigurationResultMessage
        };
        _loadedSessionCache = cache;
        _pendingSessionCache = cache;
        _pendingSessionProjectPath = ProjectPath;
        var version = Interlocked.Increment(ref _sessionSaveVersion);
        _ = PersistSessionCacheAfterDelayAsync(version, ProjectPath, cache);
    }

    private async Task PersistSessionCacheAfterDelayAsync(
        int version,
        string projectPath,
        UnrealSyncSessionCache cache)
    {
        try
        {
            await Task.Delay(180);
            if (version != Volatile.Read(ref _sessionSaveVersion)) return;
            await _sessionSaveSemaphore.WaitAsync();
            try
            {
                if (version != Volatile.Read(ref _sessionSaveVersion)) return;
                await Task.Run(() => _sessionCacheService.Write(projectPath, cache));
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

    public void FlushSessionCache()
    {
        var cache = _pendingSessionCache;
        var projectPath = _pendingSessionProjectPath;
        if (cache is null || string.IsNullOrWhiteSpace(projectPath)) return;
        Interlocked.Increment(ref _sessionSaveVersion);
        _sessionSaveSemaphore.Wait();
        try
        {
            _sessionCacheService.Write(projectPath, cache);
            _pendingSessionCache = null;
        }
        finally
        {
            _sessionSaveSemaphore.Release();
        }
    }

    private void UpdateImportSelectionSummary()
    {
        if (!_hasImportDetection)
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
            _importAttentionCount = leaves.Count(item => item.RequiresAttention ||
                item.Change is not null && !UnrealBridgePublishSupportPolicy.CanExecute(item.Change));
        }

        _importSkippedCount = leaves.Length - selected.Length;
        ImportSelectionCountText = $"已选择 {_importSelectedCount} / {leaves.Length} 项";
        ImportAddedCountText = $"新增 {_importAddedCount} 项";
        ImportUpdatedCountText = $"更新 {_importUpdatedCount} 项";
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
        OnPropertyChanged(nameof(CanStartPublish));
        OnPropertyChanged(nameof(PublishActionText));
        OnPropertyChanged(nameof(WorkflowNextButtonEnabled));
        OnPropertyChanged(nameof(PendingRedirectCount));
        OnPropertyChanged(nameof(PublishConflictCount));
        OnPropertyChanged(nameof(ReadyPublishCount));
        OnPropertyChanged(nameof(ReadyPublishText));
        OnPropertyChanged(nameof(PendingRedirectText));
        OnPropertyChanged(nameof(PublishConflictText));
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

    private void ResetImportOperation()
    {
        _hasImportDetection = false;
        OnPropertyChanged(nameof(HasContentDetection));
        OnPropertyChanged(nameof(DetectionResultVisibility));
        ResetDetectionSummary();
        _existingImportStableIds.Clear();
        _importSelectedCount = 0;
        _importAddedCount = 0;
        _importUpdatedCount = 0;
        _importAttentionCount = 0;
        _importSkippedCount = 0;
        ImportOperationTitle = IsEngineToToolbox ? "等待内容检测" : "等待差异检测";
        ImportOperationMessage = IsEngineToToolbox
            ? "先选择角色，再检测可导入内容。"
            : "先选择已完成角色，再检测与 Unreal 的差异。";
        ImportSelectionCountText = "尚未检测";
        ImportAddedCountText = "新增 0 项";
        ImportUpdatedCountText = "更新 0 项";
        ImportAttentionCountText = "需检查 0 项";
        ImportSkippedCountText = IsEngineToToolbox ? "未选 0 项" : "未执行 0 项";
        ImportPrimaryActionText = IsEngineToToolbox ? "导入所选到草稿" : "同步所选到虚幻";
        ImportResultMessage = string.Empty;
        ImportDetailVisibility = Visibility.Collapsed;
        ImportResultVisibility = Visibility.Collapsed;
        CanImportSelection = false;
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
