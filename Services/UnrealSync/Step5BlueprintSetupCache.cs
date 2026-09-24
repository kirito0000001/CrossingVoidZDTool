using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 第 5 步「蓝图置入」自己的缓存：一步一个文件，只装这一步的东西 ——
/// **这次扫描出来的字段（无差异 / 待写入 / 错误）+ 用户勾了哪几条**。
///
/// 落点：`&lt;角色&gt;/&lt;工具目录&gt;/UnrealSync/step5-blueprint-setup.json`。
///
/// 以前这一步的结果只住在整体会话缓存（`UnrealSyncSessionCache.IsBlueprintSetupLoaded` /
/// `BlueprintSetupItems` / `SelectedBlueprintSetupIds` / `BlueprintSetupResultMessage`）——
/// 那是所有步骤共用的一大坨。前四步都搬成了各自的小文件，这一步跟着搬（2026-09-24 体检）。
/// </summary>
internal static class Step5BlueprintSetupCache
{
    /// <summary>文件格式版本：加字段就抬一版，读到不认识的版本一律当没缓存。</summary>
    public const int CurrentVersion = 1;

    public const string FileName = "step5-blueprint-setup.json";

    public static string GetFilePath(CharacterCard? character)
    {
        var folder = UnrealSyncSessionCacheService.GetCacheFolderPath(character);
        return string.IsNullOrWhiteSpace(folder) ? string.Empty : Path.Combine(folder, FileName);
    }

    /// <summary>写缓存。落盘失败不该打断流程，返回 false 让调用方决定要不要提一句。</summary>
    public static bool Save(
        CharacterCard? character,
        IReadOnlyList<UnrealBlueprintSetupResultItem> items,
        IReadOnlyCollection<string> selectedStableIds,
        string resultMessage)
    {
        var path = GetFilePath(character);
        if (character is null || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var document = new Step5BlueprintSetupCacheDocument
        {
            Version = CurrentVersion,
            CharacterCode = character.Code,
            SavedAtUtc = DateTimeOffset.UtcNow,
            ResultMessage = resultMessage ?? string.Empty,
            SelectedStableIds = selectedStableIds.ToArray(),
            Items = items.Select(Step5BlueprintSetupCacheItem.From).ToArray()
        };

        try
        {
            AtomicFileWriter.WriteAllText(
                path,
                System.Text.Json.JsonSerializer.Serialize(
                    document,
                    AppJsonSerializerContext.Default.Step5BlueprintSetupCacheDocument));
            return true;
        }
        catch (Exception)
        {
            // 写不进去不是错误：下次检测会再写一遍。
            return false;
        }
    }

    /// <summary>
    /// 作废这一份缓存（删文件），下次读就是"没查过"。
    ///
    /// **什么时候必须作废**：这一步的字段里有一批是「工程里有没有那个资产」——
    /// 技能图标、序列引用…，它们由第 2 步同步素材 / 第 4 步同步序列写进工程。
    /// 所以那两步一跑，这份结果的前提就变了：里面那些「目标资产不存在…请先完成第二步同步素材」
    /// 从"当时的事实"变成**假报错**，而且错误项不可勾选，用户在界面上没法弄掉
    /// （2026-09-24 体检：第 3 步已经修过同一族问题，第 5 步当时漏了）。
    ///
    /// 只删文件不够 —— 调用方还要清内存那层（见
    /// `UnrealProjectSyncViewModel.InvalidateBlueprintSetupResult`），
    /// 并把整体会话缓存里那一份一起盖掉（读不到小文件时它会兜底回退）。
    /// </summary>
    public static bool Invalidate(CharacterCard? character)
    {
        var path = GetFilePath(character);
        if (character is null || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        // 删除走 AtomicFileWriter 里那份共享实现（"删不掉就算了"的那一套别再写一份）。
        if (!AtomicFileWriter.TryDelete(path))
        {
            ToolboxLog.Warn($"第 5 步的缓存文件没删掉，下次可能还会读回旧结果：{path}");
            return false;
        }

        return true;
    }

    /// <summary>读缓存；没有 / 版本不认识 / 角色代号对不上 / 坏了，都返回 null（当作"没查过"）。</summary>
    public static Step5BlueprintSetupCacheDocument? TryLoad(CharacterCard? character, string characterCode)
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
                AppJsonSerializerContext.Default.Step5BlueprintSetupCacheDocument);
            if (document is null || document.Version != CurrentVersion)
            {
                return null;
            }

            // 角色代号对不上就当没缓存 —— 目录被换过 / 复制过别人的工具目录时会发生。
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

/// <summary>第 5 步缓存的落盘形状。</summary>
internal sealed record Step5BlueprintSetupCacheDocument
{
    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("characterCode")]
    public string CharacterCode { get; init; } = string.Empty;

    [JsonPropertyName("savedAtUtc")]
    public DateTimeOffset SavedAtUtc { get; init; }

    [JsonPropertyName("resultMessage")]
    public string ResultMessage { get; init; } = string.Empty;

    [JsonPropertyName("selectedStableIds")]
    public string[] SelectedStableIds { get; init; } = [];

    [JsonPropertyName("items")]
    public Step5BlueprintSetupCacheItem[] Items { get; init; } = [];
}

/// <summary>一条蓝图字段（字段集与 UnrealBlueprintSetupResultItem 一一对应）。</summary>
internal sealed record Step5BlueprintSetupCacheItem
{
    [JsonPropertyName("stableId")]
    public string StableId { get; init; } = string.Empty;

    [JsonPropertyName("groupKey")]
    public string GroupKey { get; init; } = string.Empty;

    [JsonPropertyName("groupName")]
    public string GroupName { get; init; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("targetPath")]
    public string TargetPath { get; init; } = string.Empty;

    [JsonPropertyName("targetField")]
    public string TargetField { get; init; } = string.Empty;

    [JsonPropertyName("currentSummary")]
    public string CurrentSummary { get; init; } = string.Empty;

    [JsonPropertyName("targetSummary")]
    public string TargetSummary { get; init; } = string.Empty;

    [JsonPropertyName("currentValues")]
    public string[] CurrentValues { get; init; } = [];

    [JsonPropertyName("targetValues")]
    public string[] TargetValues { get; init; } = [];

    [JsonPropertyName("status")]
    public UnrealBlueprintSetupStatus Status { get; init; }

    [JsonPropertyName("errorMessage")]
    public string ErrorMessage { get; init; } = string.Empty;

    public static Step5BlueprintSetupCacheItem From(UnrealBlueprintSetupResultItem item) => new()
    {
        StableId = item.StableId,
        GroupKey = item.GroupKey,
        GroupName = item.GroupName,
        DisplayName = item.DisplayName,
        TargetPath = item.TargetPath,
        TargetField = item.TargetField,
        CurrentSummary = item.CurrentSummary,
        TargetSummary = item.TargetSummary,
        CurrentValues = item.CurrentValues.ToArray(),
        TargetValues = item.TargetValues.ToArray(),
        Status = item.Status,
        ErrorMessage = item.ErrorMessage
    };

    public UnrealBlueprintSetupResultItem ToResultItem() => new()
    {
        StableId = StableId,
        GroupKey = GroupKey,
        GroupName = GroupName,
        DisplayName = DisplayName,
        TargetPath = TargetPath,
        TargetField = TargetField,
        CurrentSummary = CurrentSummary,
        TargetSummary = TargetSummary,
        CurrentValues = CurrentValues.ToList(),
        TargetValues = TargetValues.ToList(),
        Status = Status,
        ErrorMessage = ErrorMessage
    };
}
