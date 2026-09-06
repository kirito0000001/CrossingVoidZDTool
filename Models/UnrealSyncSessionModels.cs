using System;
using System.Collections.Generic;

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
    public HashSet<string> SelectedStableIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> SelectedGroupStableIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> NormalizationDecisions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<UnrealSyncNormalizationCacheItem> NormalizationItems { get; set; } = [];
    public bool IsNormalizationStepLoaded { get; set; }
    public bool HideCompletedFoundationChecks { get; set; }
    public bool HideResolvedNormalizationItems { get; set; }
    public bool IsLightConfigurationLoaded { get; set; }
    public List<UnrealLightConfigurationResultItem> LightConfigurationItems { get; set; } = [];
    public HashSet<string> SelectedLightConfigurationIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string LightConfigurationResultMessage { get; set; } = string.Empty;
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
