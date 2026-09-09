using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CrossingVoidZDTool;

internal enum UnrealSyncSessionCacheLoadStatus
{
    Missing,
    Loaded,
    Invalid
}

internal sealed record UnrealSyncSessionCacheLoadResult(
    UnrealSyncSessionCacheLoadStatus Status,
    UnrealSyncSessionCache? Cache = null,
    string ErrorMessage = "");

/// <summary>
/// 虚幻同步台的流程步数。加新步骤时只改这一处。
///
/// 步号在好几个地方会被夹到合法区间，之前这个上限散落着写死成 5：
/// 第六步「蓝图置入」刚接上时，点「下一步」会被静默夹回第五步，
/// 界面停在原地却已经开始跑虚幻检测，看着就像按钮直接执行了操作。
/// </summary>
internal static class UnrealSyncWorkflow
{
    public const int MinStep = 1;

    /// <summary>1 底层检测、2 规整素材、3 同步素材、4 基础配置、5 序列同步、6 蓝图置入。</summary>
    public const int MaxStep = 6;
}

internal sealed class UnrealSyncSessionCache
{
    public int DetectionAlgorithmVersion { get; set; }
    public int ProtocolVersion { get; set; } = 3;
    public string EnginePath { get; set; } = string.Empty;
    public string ProjectPath { get; set; } = string.Empty;
    public UnrealBridgeDirection Direction { get; set; }
    public UnrealBridgePublishStage Stage { get; set; }
    public int WorkflowStep { get; set; } = 1;
    public string SelectedCharacterCode { get; set; } = string.Empty;
    public DateTimeOffset DetectedAt { get; set; }
    public bool IsPublishDetection { get; set; }
    public int DetectionTotalCount { get; set; }
    public int DetectionUnchangedCount { get; set; }
    public int DetectionAddedCount { get; set; }
    public int DetectionUpdatedCount { get; set; }
    public int DetectionRenamedCount { get; set; }
    public int DetectionConflictCount { get; set; }
    public int DetectionDeletedCount { get; set; }
    public UnrealBridgeSnapshot? ImportSnapshot { get; set; }
    public List<UnrealBridgeChange> PublishChanges { get; set; } = [];

    // 下面这几个集合按 StableId 查表，必须大小写不敏感。
    //
    // 但字段初始化器里的 OrdinalIgnoreCase 只在「new 出来的对象」上成立：
    // System.Text.Json 对有 setter 的集合属性默认 JsonObjectCreationHandling.Replace——
    // 它自己 new 一个默认比较器的实例填完再赋值，初始化器给的比较器直接被丢掉。
    // 于是同一份缓存写进去时 OrdinalIgnoreCase、读回来就变回大小写敏感，
    // 第二步的重定向决策重启后查不到、回到「等待处理」。
    //
    // Populate 让它往初始化器建好的那个实例里填，比较器就保住了。
    // 之所以标在属性上而不是在每条读取路径上手工重建：读取路径有三处
    // （UnrealSyncSessionCacheService / UnrealBridgeStateService / UnrealBridgeToolboxIdentityService），
    // 将来加第四处时没人会记得这件事。
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public HashSet<string> SelectedStableIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public HashSet<string> SelectedGroupStableIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Dictionary<string, string> NormalizationDecisions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<UnrealSyncNormalizationCacheItem> NormalizationItems { get; set; } = [];
    public bool IsNormalizationStepLoaded { get; set; }
    public bool HideCompletedFoundationChecks { get; set; }
    public bool HideResolvedNormalizationItems { get; set; }
    public bool IsLightConfigurationLoaded { get; set; }
    public List<UnrealLightConfigurationResultItem> LightConfigurationItems { get; set; } = [];

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public HashSet<string> SelectedLightConfigurationIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string LightConfigurationResultMessage { get; set; } = string.Empty;

    // 第六步「蓝图置入」。存下来是为了重进这一步时不必再跑一次虚幻检测——
    // 离线检测一次十几秒，来回切步骤全是白等。
    public bool IsBlueprintSetupLoaded { get; set; }
    public List<UnrealBlueprintSetupResultItem> BlueprintSetupItems { get; set; } = [];

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public HashSet<string> SelectedBlueprintSetupIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string BlueprintSetupResultMessage { get; set; } = string.Empty;
}

internal sealed class UnrealSyncNormalizationCacheItem
{
    public string StableId { get; set; } = string.Empty;
    public UnrealBridgeModule Module { get; set; }
    public string Category { get; set; } = string.Empty;
    public string UnrealAssetName { get; set; } = string.Empty;
    public string UnrealObjectPath { get; set; } = string.Empty;
    public string PreviewFilePath { get; set; } = string.Empty;
    public int ReferenceCount { get; set; }
    public List<UnrealAssetNormalizationCandidate> Candidates { get; set; } = [];
    public string SelectedCandidateStableId { get; set; } = string.Empty;
    public UnrealAssetNormalizationDecision Decision { get; set; }
    public bool IsAlreadyNormalized { get; set; }
}
