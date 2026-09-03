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
[JsonSerializable(typeof(UnrealExportProgressState))]
[JsonSerializable(typeof(UnrealProjectExportCharacterBuffSet))]
[JsonSerializable(typeof(UnrealProjectExportBuff))]
[JsonSerializable(typeof(UnrealBridgeSyncState))]
[JsonSerializable(typeof(UnrealBridgeToolboxIdentityMap))]
[JsonSerializable(typeof(UnrealBridgeScanManifest))]
[JsonSerializable(typeof(UnrealBridgeScanItem))]
[JsonSerializable(typeof(UnrealBridgeExecutionPlan))]
[JsonSerializable(typeof(UnrealBridgeOperation))]
[JsonSerializable(typeof(UnrealBridgeExecutionProgress))]
[JsonSerializable(typeof(UnrealBridgeExecutionResult))]
[JsonSerializable(typeof(UnrealBridgeExecutionItemResult))]
[JsonSerializable(typeof(UnrealSyncSessionCache))]
internal sealed partial class AppJsonSerializerContext : JsonSerializerContext
{
}
