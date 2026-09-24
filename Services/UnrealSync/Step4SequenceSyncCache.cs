using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 第四步「序列同步」自己的缓存：一步一个文件，只装这一步的东西 ——
/// **这次算出来的序列差异列表 + 用户勾了哪些**。
///
/// 落点：`&lt;角色&gt;/&lt;工具目录&gt;/UnrealSync/step4-sequence-sync.json`。
///
/// 和第二步那份（`step3` 的差异树）是两回事，虽然它们共用界面：
/// 第二步看的是"素材"（图 / 声音），第四步看的是"序列帧"，
/// 同名的文件放一起下次就会有人拿错。
///
/// `Changes` 直接存 <see cref="UnrealBridgeChange"/>：它已经是
/// 整体会话缓存 `PublishChanges` 的元素类型，序列化是现成且无损的
/// （ToolboxItem / UnrealItem 的哈希、路径都在里面），不需要再手搓一份镜像结构。
/// </summary>
internal static class Step4SequenceSyncCache
{
    /// <summary>文件格式版本：加字段就抬一版，读到不认识的版本一律当没缓存。</summary>
    public const int CurrentVersion = 1;

    public const string FileName = "step4-sequence-sync.json";

    public static string GetFilePath(CharacterCard? character)
    {
        var folder = UnrealSyncSessionCacheService.GetCacheFolderPath(character);
        return string.IsNullOrWhiteSpace(folder) ? string.Empty : Path.Combine(folder, FileName);
    }

    /// <summary>写缓存。落盘失败不该打断流程，返回 false 让调用方决定要不要提一句。</summary>
    public static bool Save(
        CharacterCard? character,
        IReadOnlyCollection<UnrealBridgeChange> changes,
        IReadOnlyCollection<string> selectedStableIds,
        IReadOnlyCollection<string> selectedGroupStableIds,
        int detectionAlgorithmVersion)
    {
        var path = GetFilePath(character);
        if (character is null || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var document = new Step4SequenceSyncCacheDocument
        {
            Version = CurrentVersion,
            CharacterCode = character.Code,
            SavedAtUtc = DateTimeOffset.UtcNow,
            DetectionAlgorithmVersion = detectionAlgorithmVersion,
            Changes = changes.ToArray(),
            SelectedStableIds = selectedStableIds.ToArray(),
            SelectedGroupStableIds = selectedGroupStableIds.ToArray()
        };

        try
        {
            AtomicFileWriter.WriteAllText(
                path,
                System.Text.Json.JsonSerializer.Serialize(
                    document,
                    AppJsonSerializerContext.Default.Step4SequenceSyncCacheDocument));
            return true;
        }
        catch (Exception)
        {
            // 写不进去不是错误：下次检测会再写一遍。
            return false;
        }
    }

    /// <summary>
    /// 读缓存；没有 / 版本不认识 / 角色代号对不上 / 检测算法换代 / 坏了，
    /// 都返回 null（当作"没查过"）。
    ///
    /// 比对 <paramref name="detectionAlgorithmVersion"/> 是因为差异口径改过版
    /// （见 ViewModel 的 <c>CurrentDetectionAlgorithmVersion</c>）：
    /// 旧版算出来的差异列表拿到新版界面上会显示成"删除 N 项 + 新增 N 项"，
    /// 宁可当没缓存重查一次。
    /// </summary>
    public static Step4SequenceSyncCacheDocument? TryLoad(
        CharacterCard? character,
        string characterCode,
        int detectionAlgorithmVersion)
    {
        var path = GetFilePath(character);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var document = System.Text.Json.JsonSerializer.Deserialize(
                File.ReadAllText(path),
                AppJsonSerializerContext.Default.Step4SequenceSyncCacheDocument);
            if (document is null ||
                document.Version != CurrentVersion ||
                document.DetectionAlgorithmVersion != detectionAlgorithmVersion)
            {
                return null;
            }

            return string.Equals(document.CharacterCode, characterCode, StringComparison.OrdinalIgnoreCase)
                ? document
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>第四步缓存的落盘形状。</summary>
internal sealed record Step4SequenceSyncCacheDocument
{
    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("characterCode")]
    public string CharacterCode { get; init; } = string.Empty;

    [JsonPropertyName("savedAtUtc")]
    public DateTimeOffset SavedAtUtc { get; init; }

    /// <summary>写这份缓存时的差异算法版本（对不上就当没缓存）。</summary>
    [JsonPropertyName("detectionAlgorithmVersion")]
    public int DetectionAlgorithmVersion { get; init; }

    /// <summary>这次算出来的序列差异（含 Unchanged：序列树要靠它们配帧）。</summary>
    [JsonPropertyName("changes")]
    public UnrealBridgeChange[] Changes { get; init; } = [];

    [JsonPropertyName("selectedStableIds")]
    public string[] SelectedStableIds { get; init; } = [];

    [JsonPropertyName("selectedGroupStableIds")]
    public string[] SelectedGroupStableIds { get; init; } = [];
}
