using System.Text.Json.Serialization;

namespace CrossingVoidZDTool.Services;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(CharacterMetadata))]
[JsonSerializable(typeof(CharacterDraftData))]
[JsonSerializable(typeof(CharacterInfoData))]
[JsonSerializable(typeof(CharacterKeywordTagGroups))]
[JsonSerializable(typeof(CharacterSkillsData))]
[JsonSerializable(typeof(SequenceFramesData))]
[JsonSerializable(typeof(SequenceFrameManifest))]
[JsonSerializable(typeof(BuffData))]
[JsonSerializable(typeof(BuffFileData))]
[JsonSerializable(typeof(BuffEntry))]
[JsonSerializable(typeof(BuffEffectModule))]
[JsonSerializable(typeof(CharacterToolboxData))]
[JsonSerializable(typeof(CharacterBackupMeta))]
[JsonSerializable(typeof(UnrealProjectExportManifest))]
[JsonSerializable(typeof(UnrealProjectExportTeamSelect))]
[JsonSerializable(typeof(UnrealExportProgressState))]
[JsonSerializable(typeof(UnrealProjectExportCharacterBuffSet))]
[JsonSerializable(typeof(UnrealProjectExportBuff))]
[JsonSerializable(typeof(UnrealBridgeSyncState))]
[JsonSerializable(typeof(UnrealBridgeToolboxIdentityMap))]
[JsonSerializable(typeof(UnrealBridgeScanManifest))]
[JsonSerializable(typeof(UnrealBridgeScanItem))]
  [JsonSerializable(typeof(UnrealBridgeExecutionPlan))]
  [JsonSerializable(typeof(UnrealBridgeSequenceSyncPlan))]
  [JsonSerializable(typeof(UnrealBridgeSequenceSyncAction))]
  [JsonSerializable(typeof(UnrealBridgeSequenceSyncFrame))]
  [JsonSerializable(typeof(UnrealBridgeOperation))]
[JsonSerializable(typeof(UnrealBridgeExecutionProgress))]
[JsonSerializable(typeof(UnrealBridgeExecutionResult))]
[JsonSerializable(typeof(UnrealBridgeExecutionItemResult))]
[JsonSerializable(typeof(UnrealSyncSessionCache))]
[JsonSerializable(typeof(UnrealSyncNormalizationCacheItem))]
[JsonSerializable(typeof(UnrealAssetNormalizationCandidate))]
[JsonSerializable(typeof(UnrealRemotePythonJob))]
[JsonSerializable(typeof(UnrealLightConfigurationRequest))]
[JsonSerializable(typeof(UnrealLightConfigurationResult))]
[JsonSerializable(typeof(UnrealLightConfigurationResultItem))]
[JsonSerializable(typeof(UnrealAssetBrowsePayload))]
[JsonSerializable(typeof(string[]))]
internal sealed partial class AppJsonSerializerContext : JsonSerializerContext
{
}

/// <summary>
/// 第六步「蓝图置入」的请求和结果单独用一个上下文，因为它跟桥接脚本约定的是
/// camelCase：请求由 C# 写、Python 读，结果反过来，两边字段名必须一致。
/// 其余协议沿用 <see cref="AppJsonSerializerContext"/> 的 PascalCase，不受影响。
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(UnrealBlueprintSetupRequest))]
[JsonSerializable(typeof(UnrealBlueprintSetupResult))]
[JsonSerializable(typeof(UnrealBlueprintSetupResultItem))]
internal sealed partial class UnrealBlueprintSetupJsonContext : JsonSerializerContext
{
}
