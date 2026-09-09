using System.Text.Json;
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
[JsonSerializable(typeof(ProjectSharedMaterialIndex))]
[JsonSerializable(typeof(ProjectSharedMaterialItem))]
[JsonSerializable(typeof(string[]))]
internal sealed partial class AppJsonSerializerContext : JsonSerializerContext
{
    private static AppJsonSerializerContext? s_indented;

    /// <summary>
    /// 落盘要缩进（用户会直接打开看的文件）时用这一份，别自己 new 一个 JsonSerializerOptions 再喂给构造函数。
    ///
    /// <see cref="JsonSourceGenerationOptionsAttribute"/> 上写的 PropertyNameCaseInsensitive 只会烘进
    /// <see cref="Default"/> 自带的那份 options；<c>new AppJsonSerializerContext(自己造的 options)</c>
    /// 拿到的是一份干净默认值，大小写不敏感就悄悄没了——读一个属性名大小写不同的文件时字段被静默丢弃，
    /// 既不报错也没有痕迹。
    ///
    /// 这里从 <c>Default.Options</c> 复制一份再只改 WriteIndented，所以将来往
    /// <see cref="JsonSourceGenerationOptionsAttribute"/> 上加别的开关，这条路径也会自动跟上。
    /// </summary>
    public static AppJsonSerializerContext Indented =>
        s_indented ??= new AppJsonSerializerContext(new JsonSerializerOptions(Default.Options)
        {
            WriteIndented = true
        });
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
