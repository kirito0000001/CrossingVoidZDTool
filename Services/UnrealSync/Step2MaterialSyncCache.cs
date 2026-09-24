using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 第 2 步「同步素材」自己的缓存：一步一个文件，**只装这一步的差异那一半** ——
/// 这次算出来的素材差异列表 + 用户勾了哪些。
///
/// 落点：`&lt;角色&gt;/&lt;工具目录&gt;/UnrealSync/step2-material-sync.json`。
///
/// 和旁边那份 `step2-normalization.json`（<see cref="Step2NormalizationCache"/>）**分工不同**：
/// <list type="bullet">
/// <item>`step2-normalization.json` 装的是**用户手工做的规整决策** —— 那是用户数据，长期有效；</item>
/// <item>这一份装的是**检测结果 + 勾选** —— 有版本、可丢弃、算法换代就当没有。</item>
/// </list>
/// 两者的生命周期不一样，所以不合成一个文件（合了就得为"算法换代时别把决策也丢掉"写迁移）。
///
/// `Changes` 直接存 <see cref="UnrealBridgeChange"/>：它本来就整体会话缓存
/// `PublishChanges` 的元素类型，序列化无损（ToolboxItem / UnrealItem 的哈希、路径都在里面）。
/// </summary>
internal static class Step2MaterialSyncCache
{
    /// <summary>文件格式版本：加字段就抬一版，读到不认识的版本一律当没缓存。</summary>
    public const int CurrentVersion = 1;

    public const string FileName = "step2-material-sync.json";

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

        var document = new Step2MaterialSyncCacheDocument
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
                    AppJsonSerializerContext.Default.Step2MaterialSyncCacheDocument));
            return true;
        }
        catch (Exception)
        {
            // 写不进去不是错误：下次检测会再写一遍。
            return false;
        }
    }

    /// <summary>
    /// 读缓存；没有 / 版本不认识 / 角色代号对不上 / 差异算法换代 / 坏了，
    /// 都返回 null（当作"没查过"）。
    ///
    /// 比对 <paramref name="detectionAlgorithmVersion"/> 的理由同第四步：
    /// 旧口径算出来的差异列表拿到新界面上会显示成"删除 N 项 + 新增 N 项"，宁可当没缓存重查一次。
    /// </summary>
    public static Step2MaterialSyncCacheDocument? TryLoad(
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
                AppJsonSerializerContext.Default.Step2MaterialSyncCacheDocument);
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

/// <summary>第 2 步缓存的落盘形状。</summary>
internal sealed record Step2MaterialSyncCacheDocument
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

    /// <summary>这次算出来的素材差异（含 Unchanged）。</summary>
    [JsonPropertyName("changes")]
    public UnrealBridgeChange[] Changes { get; init; } = [];

    [JsonPropertyName("selectedStableIds")]
    public string[] SelectedStableIds { get; init; } = [];

    [JsonPropertyName("selectedGroupStableIds")]
    public string[] SelectedGroupStableIds { get; init; } = [];
}
