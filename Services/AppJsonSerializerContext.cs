using System.Text.Json.Serialization;

namespace CrossingVoidZDTool.Services;

[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(CharacterMetadata))]
[JsonSerializable(typeof(CharacterDraftData))]
[JsonSerializable(typeof(CharacterInfoData))]
[JsonSerializable(typeof(CharacterSkillsData))]
[JsonSerializable(typeof(SequenceFramesData))]
[JsonSerializable(typeof(SequenceFrameManifest))]
[JsonSerializable(typeof(BuffData))]
[JsonSerializable(typeof(BuffFileData))]
[JsonSerializable(typeof(BuffEntry))]
[JsonSerializable(typeof(CharacterToolboxData))]
[JsonSerializable(typeof(CharacterBackupMeta))]
[JsonSerializable(typeof(UnrealProjectExportManifest))]
[JsonSerializable(typeof(UnrealExportProgressState))]
[JsonSerializable(typeof(UnrealProjectExportCharacterBuffSet))]
[JsonSerializable(typeof(UnrealProjectExportBuff))]
internal sealed partial class AppJsonSerializerContext : JsonSerializerContext
{
}
