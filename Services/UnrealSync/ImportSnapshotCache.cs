using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// **导入方向（虚幻→工具箱）的现场**：那一轮从工程里扫出来的候选资产 + 用户勾了哪些。
///
/// 落点：`&lt;角色&gt;/&lt;工具目录&gt;/UnrealSync/import-snapshot.json`。
///
/// 为什么单独一个文件、而不是塞进 `session.json`：它是**数据**（一个角色几十上百条候选），
/// 和"谁的工程 / 哪个方向"那种几十字节的现场不是一回事；体量与生命周期都不同。
/// 六个步骤是"一步一个文件"，导入方向同理。
///
/// 它是原来整体缓存里**唯一没有替代品**的东西（2026-09-24 清账结论），所以搬出来单独存。
///
/// ⚠️ 快照**整份存**（`UnrealBridgeSnapshot` 本体），不逐字段映射 ——
/// 手抄字段名是最容易漂的东西（写这份文件时就把 `ModuleKey/AssetName/ExportedFilePath`
/// 当成了它的字段，实际是 `Module/DisplayName/AssetPath`）。让序列化器去对应。
/// </summary>
internal static class ImportSnapshotCache
{
    public const int CurrentVersion = 1;

    public const string FileName = "import-snapshot.json";

    public static string GetFilePath(CharacterCard? character)
    {
        var folder = UnrealSyncCacheFolder.GetCacheFolderPath(character);
        return string.IsNullOrWhiteSpace(folder) ? string.Empty : Path.Combine(folder, FileName);
    }

    public static bool Save(
        CharacterCard? character,
        string projectPath,
        UnrealBridgeSnapshot? snapshot,
        IReadOnlyCollection<string> selectedStableIds)
    {
        var path = GetFilePath(character);
        if (character is null || string.IsNullOrWhiteSpace(path) || snapshot is null)
        {
            return false;
        }

        var document = new ImportSnapshotCacheDocument
        {
            Version = CurrentVersion,
            CharacterCode = character.Code,
            ProjectPath = projectPath ?? string.Empty,
            SavedAtUtc = DateTimeOffset.UtcNow,
            SelectedStableIds = selectedStableIds.ToArray(),
            Snapshot = snapshot
        };

        try
        {
            AtomicFileWriter.WriteAllText(
                path,
                System.Text.Json.JsonSerializer.Serialize(
                    document,
                    AppJsonSerializerContext.Default.ImportSnapshotCacheDocument));
            return true;
        }
        catch (Exception error)
        {
            // 写不进去不是错误：下次检测会再写一遍。但**不能一声不吭**（同第 1 步）。
            ToolboxLog.Warn($"[UnrealSync] 导入现场写入失败：{path}", error);
            return false;
        }
    }

    /// <summary>读导入现场；没有 / 版本不认识 / 角色或工程对不上 / 坏了，都返回 null。</summary>
    public static ImportSnapshotCacheDocument? TryLoad(CharacterCard? character, string projectPath)
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
                AppJsonSerializerContext.Default.ImportSnapshotCacheDocument);
            if (document is null || document.Version != CurrentVersion || document.Snapshot is null)
            {
                return null;
            }

            if (!string.Equals(document.CharacterCode, character?.Code, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // 工程对不上就当没存过 —— 同一个角色对接两个工程时的唯一一道闸。
            return string.Equals(document.ProjectPath, projectPath ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                ? document
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>导入现场的落盘形状。</summary>
internal sealed record ImportSnapshotCacheDocument
{
    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("characterCode")]
    public string CharacterCode { get; init; } = string.Empty;

    [JsonPropertyName("projectPath")]
    public string ProjectPath { get; init; } = string.Empty;

    [JsonPropertyName("savedAtUtc")]
    public DateTimeOffset SavedAtUtc { get; init; }

    [JsonPropertyName("selectedStableIds")]
    public string[] SelectedStableIds { get; init; } = [];

    /// <summary>整份候选快照（跟原类型一一对应，交给序列化器，不手抄字段）。</summary>
    [JsonPropertyName("snapshot")]
    public UnrealBridgeSnapshot? Snapshot { get; init; }
}
