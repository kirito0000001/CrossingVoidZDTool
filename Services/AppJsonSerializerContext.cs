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
  [JsonSerializable(typeof(UnrealBridgeSequenceContentFingerprints))]
  [JsonSerializable(typeof(UnrealBridgeSequenceActionFingerprint))]
  [JsonSerializable(typeof(UnrealBridgeOperation))]
[JsonSerializable(typeof(UnrealBridgeExecutionProgress))]
[JsonSerializable(typeof(UnrealBridgeExecutionResult))]
[JsonSerializable(typeof(UnrealBridgeExecutionItemResult))]
// 第一步「底层检测」自己的小缓存（一步一个文件，只装这一步的东西）。
[JsonSerializable(typeof(Step1FoundationCacheDocument))]
// 第二步「规整素材」自己的小缓存（用户做过的规整决策）。
[JsonSerializable(typeof(Step2NormalizationCacheDocument))]
// 第二步「同步素材」（合并后）自己的另一半缓存：素材差异 + 勾选。
[JsonSerializable(typeof(Step2MaterialSyncCacheDocument))]
// 第三步「基础配置」自己的小缓存（检测项 + 勾选）。
[JsonSerializable(typeof(Step3LightConfigurationCacheDocument))]
// 第四步「序列同步」自己的小缓存（序列差异 + 勾选）。
[JsonSerializable(typeof(Step4SequenceSyncCacheDocument))]
// 第五步「蓝图置入」自己的小缓存（扫描出来的字段 + 勾选）。
[JsonSerializable(typeof(Step5BlueprintSetupCacheDocument))]
// 第六步「特效同步」自己的小缓存（特效动作清单 + 上次同步时间）。
[JsonSerializable(typeof(Step6EffectSyncCacheDocument))]

// 全局现场（引擎/工程路径、方向、角色、上次检测时间、显示开关）。
[JsonSerializable(typeof(SessionStateCacheDocument))]
// 导入方向（虚幻→工具箱）的候选快照 + 勾选。
[JsonSerializable(typeof(ImportSnapshotCacheDocument))]
[JsonSerializable(typeof(UnrealBridgeSnapshot))]
[JsonSerializable(typeof(UnrealBridgeSnapshotItem))]
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
/// 第五步「蓝图置入」的请求和结果单独用一个上下文，因为它跟桥接脚本约定的是
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
