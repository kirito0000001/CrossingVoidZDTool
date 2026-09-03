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
    public int ProtocolVersion { get; set; } = 3;
    public string EnginePath { get; set; } = string.Empty;
    public string ProjectPath { get; set; } = string.Empty;
    public UnrealBridgeDirection Direction { get; set; }
    public UnrealBridgePublishStage Stage { get; set; }
    public int WorkflowStep { get; set; } = 1;
    public string SelectedCharacterCode { get; set; } = string.Empty;
    public DateTimeOffset DetectedAt { get; set; }
    public bool IsPublishDetection { get; set; }
    public UnrealBridgeSnapshot? ImportSnapshot { get; set; }
    public List<UnrealBridgeChange> PublishChanges { get; set; } = [];
    public HashSet<string> SelectedStableIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> NormalizationDecisions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool HideCompletedFoundationChecks { get; set; }
    public bool HideResolvedNormalizationItems { get; set; }
}
