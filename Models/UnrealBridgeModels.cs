using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json.Serialization;

namespace CrossingVoidZDTool;

internal enum UnrealBridgeDirection
{
    PublishToUnreal,
    ImportFromUnreal
}

internal enum UnrealBridgePublishStage
{
    CharacterMaterials,
    ItemData,
    ZdAnimationTracks,
    Buffs,
    CharacterBlueprint
}

internal enum UnrealAssetNormalizationDecision
{
    Pending,
    Redirect,
    NotRequired
}

internal sealed record UnrealAssetNormalizationCandidate(
    string StableId,
    string DisplayName,
    string AssetPath)
{
    public string DisplayText => $"{DisplayName}\n{AssetPath}";
    public bool HasPreview => File.Exists(AssetPath);
    public string FileUri => HasPreview ? new Uri(Path.GetFullPath(AssetPath)).AbsoluteUri : string.Empty;
    public string RelativePath => Path.GetFileName(AssetPath);
}

internal sealed class UnrealAssetNormalizationItem : ViewModels.ObservableObject
{
    private UnrealAssetNormalizationDecision _decision;
    private UnrealAssetNormalizationCandidate? _selectedCandidate;

    public UnrealAssetNormalizationItem(
        string stableId,
        UnrealBridgeModule module,
        string category,
        string unrealAssetName,
        string unrealObjectPath,
        string previewFilePath,
        int referenceCount,
        IReadOnlyList<UnrealAssetNormalizationCandidate> candidates,
        UnrealAssetNormalizationCandidate? selectedCandidate = null,
        bool isAlreadyNormalized = false)
    {
        StableId = stableId;
        Module = module;
        Category = category;
        UnrealAssetName = unrealAssetName;
        UnrealObjectPath = unrealObjectPath;
        PreviewFilePath = previewFilePath;
        ReferenceCount = referenceCount;
        Candidates = candidates;
        _selectedCandidate = selectedCandidate;
        IsAlreadyNormalized = isAlreadyNormalized;
        _decision = selectedCandidate is null ? UnrealAssetNormalizationDecision.Pending : UnrealAssetNormalizationDecision.Redirect;
    }

    public string StableId { get; }
    public UnrealBridgeModule Module { get; }
    public string Category { get; }
    public string UnrealAssetName { get; }
    public string UnrealObjectPath { get; }
    public string PreviewFilePath { get; }
    public int ReferenceCount { get; }
    public IReadOnlyList<UnrealAssetNormalizationCandidate> Candidates { get; }
    public bool IsAlreadyNormalized { get; }
    public Microsoft.UI.Xaml.Visibility ItemVisibility => IsAlreadyNormalized ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
    public bool IsAudio => Module == UnrealBridgeModule.Voices;
    public bool IsImage => !IsAudio;
    public bool HasPreview => !string.IsNullOrWhiteSpace(PreviewFilePath) && System.IO.File.Exists(PreviewFilePath);
    public string FileUri => HasPreview ? new Uri(System.IO.Path.GetFullPath(PreviewFilePath)).AbsoluteUri : string.Empty;
    public Microsoft.UI.Xaml.Visibility ImageVisibility => IsImage && HasPreview ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility AudioVisibility => IsAudio && HasPreview ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility MissingPreviewVisibility => HasPreview ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
    public bool IsResolved => Decision != UnrealAssetNormalizationDecision.Pending;

    public UnrealAssetNormalizationDecision Decision
    {
        get => _decision;
        private set
        {
            if (SetProperty(ref _decision, value))
            {
                OnPropertyChanged(nameof(IsResolved));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(DecisionText));
                OnPropertyChanged(nameof(SelectedCandidateName));
                OnPropertyChanged(nameof(SelectedCandidateFileUri));
                OnPropertyChanged(nameof(SelectedCandidateVisibility));
                OnPropertyChanged(nameof(PendingCandidateVisibility));
            }
        }
    }

    public UnrealAssetNormalizationCandidate? SelectedCandidate
    {
        get => _selectedCandidate;
        private set
        {
            if (SetProperty(ref _selectedCandidate, value))
            {
                OnPropertyChanged(nameof(DecisionText));
                OnPropertyChanged(nameof(SelectedCandidateName));
                OnPropertyChanged(nameof(SelectedCandidateFileUri));
                OnPropertyChanged(nameof(SelectedCandidateVisibility));
                OnPropertyChanged(nameof(PendingCandidateVisibility));
            }
        }
    }

    public string StatusText => Decision switch
    {
        UnrealAssetNormalizationDecision.Redirect => "已选择重定向",
        UnrealAssetNormalizationDecision.NotRequired => "不需要重定向",
        _ => "等待处理"
    };

    public string ReferenceText => $"引用 {ReferenceCount} 处";
    public string DecisionText => SelectedCandidate is null ? StatusText : $"重定向到：{SelectedCandidate.DisplayName}";
    public string SelectedCandidateName => SelectedCandidate?.DisplayName ?? "尚未选择工具箱素材";
    public string SelectedCandidateFileUri => SelectedCandidate?.FileUri ?? string.Empty;
    public Microsoft.UI.Xaml.Visibility SelectedCandidateVisibility => SelectedCandidate is null ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
    public Microsoft.UI.Xaml.Visibility PendingCandidateVisibility => SelectedCandidate is null ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public void SelectRedirect(UnrealAssetNormalizationCandidate candidate)
    {
        SelectedCandidate = candidate;
        Decision = UnrealAssetNormalizationDecision.Redirect;
    }

    public void MarkNotRequired()
    {
        SelectedCandidate = null;
        Decision = UnrealAssetNormalizationDecision.NotRequired;
    }

    public void ClearRedirect()
    {
        SelectedCandidate = null;
        Decision = UnrealAssetNormalizationDecision.Pending;
    }
}

internal enum UnrealBridgeModule
{
    CharacterInfo,
    BaseMaterials,
    Skills,
    SequenceFrames,
    Buffs,
    Voices
}

internal enum UnrealBridgeChangeKind
{
    Added,
    Updated,
    Renamed,
    Unchanged,
    Conflict,
    DeleteCandidate
}

internal sealed record UnrealBridgeSnapshot(
    string CharacterCode,
    IReadOnlyList<UnrealBridgeSnapshotItem> Items);

internal sealed record UnrealBridgeSnapshotItem(
    string StableId,
    string ParentStableId,
    UnrealBridgeModule Module,
    string DisplayName,
    string ContentHash,
    string PayloadJson,
    string AssetPath,
    string SourceObjectPath = "",
    string OriginIdentity = "",
    string ToolboxRelativePath = "",
    string NormalizedName = "",
    /// <summary>
    /// 序列帧这一格用的精灵资产名（不带路径与后缀）。
    ///
    /// 图集改造之后，同一个动作的每一帧指向的贴图都是同一张图集，贴图路径再也分不出
    /// 帧与帧的区别。真正区分它们的是 Flipbook 关键帧上的精灵：工具箱侧按「素材目录里
    /// 第几张图」算得出应有的精灵名，Unreal 侧从关键帧上读得到实际精灵名。
    /// 两侧对不上，这个动作就是没同步（或素材换过），必须重做。
    /// </summary>
    string SpriteAssetName = "",
    /// <summary>
    /// 序列帧这一格的图落在**哪张图集**里（图集资产名，例如 <c>Misaka_Death</c>）。
    ///
    /// 帧可以复用别的动作的图，那张图已经在来源动作的图集里了 —— 借用方不再重复打包，
    /// 它的精灵直接指向来源图集的那一格。所以「这条序列用到哪些图集」是判定布局是否对齐的
    /// 依据：全部是自己的图时就是自己的图集，借了别人的图就变成一组图集名。
    /// </summary>
    string SourceAtlasName = "",
    /// <summary>
    /// 这张图是不是本动作自己的（在它自己的素材目录里）。
    ///
    /// 不是自己的就说明是借来的：图已经在来源动作的图集里，这个动作**没有**自己的图集
    /// （整条都借的时候），或者图集里只放自己那几张（部分是借的）。
    /// 判定「图集算不算规范资产」就看这个标记，不需要下游再拿路径去反推归属。
    /// </summary>
    bool SourceAtlasIsOwn = false);

internal sealed record UnrealBridgeChange(
    string StableId,
    UnrealBridgeModule Module,
    string DisplayName,
    UnrealBridgeChangeKind Kind,
    UnrealBridgeSnapshotItem? ToolboxItem,
    UnrealBridgeSnapshotItem? UnrealItem,
    bool IsSelected,
    string SequenceGroupKey = "")
{
    public string ParentStableId => ToolboxItem?.ParentStableId ?? UnrealItem?.ParentStableId ?? string.Empty;

    public bool RequiresExplicitConfirmation => Kind is UnrealBridgeChangeKind.Conflict or UnrealBridgeChangeKind.DeleteCandidate;
}

internal sealed class UnrealBridgeSyncState
{
    public const string SourceFileHashScheme = "source-file-v1";

    public int ProtocolVersion { get; set; } = 2;

    public string HashScheme { get; set; } = string.Empty;

    public DateTimeOffset LastVerifiedAt { get; set; }

    public string CharacterCode { get; set; } = string.Empty;

    public string UnrealProjectPath { get; set; } = string.Empty;

    public string TemplateCharacterCode { get; set; } = string.Empty;

    /// <summary>
    /// 基线条目，键是 StableId。
    ///
    /// Populate 不能省：System.Text.Json 对有 setter 的集合属性默认是 Replace，
    /// 会自己 new 一个默认比较器的字典填完再赋值，字段初始化器里的 OrdinalIgnoreCase 保不住。
    /// 这张表是 UnrealBridgeDiffService 判「有没有变过」的唯一依据——
    /// 比较器一退化成大小写敏感，同一个素材换个大小写就查不到基线，
    /// 已经同步过的东西会被重新判成新增或冲突，第三步的差异永远归不了零。
    /// </summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Dictionary<string, UnrealBridgeSyncStateEntry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed record UnrealBridgeSyncStateEntry(
    string ToolboxHash,
    string UnrealHash,
    string UnrealObjectPath = "",
    string UnrealIdentity = "",
    string ToolboxRelativePath = "",
    string NormalizedName = "");

internal sealed record UnrealBridgeToolboxFileCandidate(
    UnrealBridgeModule Module,
    string AssetPath,
    string ContentHash);

internal sealed class UnrealBridgeToolboxIdentityMap
{
    public int ProtocolVersion { get; set; } = 1;

    /// <summary>
    /// 素材身份，键是 SyncId。同上，Populate 是为了让读回来的字典保留 OrdinalIgnoreCase——
    /// <see cref="Services.UnrealBridgeToolboxIdentityService.TryResolveAssignedPath"/> 直接拿它 TryGetValue。
    /// </summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Dictionary<string, UnrealBridgeToolboxIdentityEntry> Entries { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class UnrealBridgeToolboxIdentityEntry
{
    public string SyncId { get; set; } = string.Empty;

    public UnrealBridgeModule Module { get; set; }

    public string ToolboxRelativePath { get; set; } = string.Empty;

    public string ContentHash { get; set; } = string.Empty;
}

internal sealed class UnrealBridgeScanManifest
{
    public int ProtocolVersion { get; set; } = 1;

    public string CharacterCode { get; set; } = string.Empty;

    public string ProjectPath { get; set; } = string.Empty;

    public DateTimeOffset GeneratedAt { get; set; }

    public List<UnrealBridgeScanItem> Items { get; set; } = [];
}

internal sealed class UnrealBridgeScanItem
{
    public string SyncId { get; set; } = string.Empty;

    public string ParentStableId { get; set; } = string.Empty;

    public UnrealBridgeModule Module { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string ContentHash { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = string.Empty;

    public string ObjectPath { get; set; } = string.Empty;

    public string PackageName { get; set; } = string.Empty;

    public string OriginIdentity { get; set; } = string.Empty;

    public string AssetClass { get; set; } = string.Empty;

    public string NormalizedName { get; set; } = string.Empty;

    public List<string> Referencers { get; set; } = [];

    public List<string> Dependencies { get; set; } = [];
}

internal enum UnrealBridgeOperationKind
{
    Add,
    Update,
    Rename,
    Delete,
    Consolidate
}

internal sealed class UnrealBridgeExecutionPlan
{
    public int ProtocolVersion { get; set; } = 1;

    public UnrealBridgeDirection Direction { get; set; }

    public string CharacterCode { get; set; } = string.Empty;

    public string UnrealProjectPath { get; set; } = string.Empty;

    public string TemplateCharacterCode { get; set; } = string.Empty;

    public bool BackupRequired { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public List<UnrealBridgeOperation> Operations { get; set; } = [];
}

internal sealed class UnrealBridgeOperation
{
    public string StableId { get; set; } = string.Empty;

    public UnrealBridgeModule Module { get; set; }

    public UnrealBridgeOperationKind Kind { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string SourceFilePath { get; set; } = string.Empty;

    public string SourceObjectPath { get; set; } = string.Empty;

    public string TargetObjectPath { get; set; } = string.Empty;

    public string ToolboxRelativePath { get; set; } = string.Empty;

    public string NormalizedName { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = string.Empty;
}

internal sealed class UnrealBridgeExecutionProgress
{
    public int ProtocolVersion { get; set; } = 1;

    public int CompletedCount { get; set; }

    public int TotalCount { get; set; }

    public string StableId { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class UnrealBridgeExecutionResult
{
    public int ProtocolVersion { get; set; } = 1;

    public bool Succeeded { get; set; }

    public string ErrorMessage { get; set; } = string.Empty;

    public DateTimeOffset CompletedAt { get; set; }

    public List<UnrealBridgeExecutionItemResult> Items { get; set; } = [];

    /// <summary>
    /// 进程退出码非 0、但结果文件判定成功时的诊断信息。
    /// 只在工具箱进程内传递，不属于桥接协议。
    /// </summary>
    [JsonIgnore]
    public string ProcessExitWarning { get; set; } = string.Empty;
}

internal sealed class UnrealBridgeExecutionItemResult
{
    public string StableId { get; set; } = string.Empty;

    public bool Succeeded { get; set; }

    public string Message { get; set; } = string.Empty;

    public string ObjectPath { get; set; } = string.Empty;

    public string OriginIdentity { get; set; } = string.Empty;

    public string OutputFilePath { get; set; } = string.Empty;

    /// <summary>
    /// 条目种类。"action" 是真正执行过的动作，"diagnostic" 只是诊断信息
    /// （比如序列同步里那条孤儿序列解绑），它不对应任何动作。
    ///
    /// 加这个字段是因为诊断条目以前会被算进「已成功的动作」：于是所有真实动作
    /// 都失败时，「一个都没成功就抛」的判断失效，界面还报「已成功 1 个动作并写入基线」，
    /// 而那个假 ID 也会被拿去生成基线条目。
    ///
    /// 默认必须是 "action"：别的脚本写出的结果不带这个字段，缺省当诊断会把它们整批滤掉。
    /// </summary>
    public string ItemKind { get; set; } = "action";

    /// <summary>这条是不是诊断信息（不计入已成功动作、不进基线）。</summary>
    public bool IsDiagnostic =>
        string.Equals(ItemKind, "diagnostic", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 从执行结果里挑出「真正成功了的动作」。
    ///
    /// 单独提成函数是为了能测：以前这段挑选逻辑埋在一个 583 行的 async void
    /// 按钮处理器里，而它挑错的后果很重——序列同步的诊断条目
    /// （orphan-sequences 那条）会被算成一个成功的动作，于是
    /// 「一个动作都没成功就抛」的判断失效，界面报「已成功 1 个动作并写入基线」，
    /// 那个假 ID 还会被拿去生成基线条目。
    /// </summary>
    public static string[] SelectSucceededActionStableIds(
        IEnumerable<UnrealBridgeExecutionItemResult>? items) =>
        items?
            .Where(item => item is { Succeeded: true, IsDiagnostic: false }
                && !string.IsNullOrWhiteSpace(item.StableId))
            .Select(item => item.StableId)
            .ToArray() ?? [];
}

internal sealed record UnrealBridgeDraftImportResult(
    CharacterCard Character,
    bool CreatedNew,
    IReadOnlyList<UnrealBridgeModule> ImportedModules,
    int RemovedDuplicateCount);
