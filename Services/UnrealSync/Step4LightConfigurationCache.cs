using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 第四步「基础配置」自己的缓存：一步一个文件，只装这一步的东西 ——
/// **这次检测出来的配置项（无差异 / 待设置 / 错误）+ 用户勾了哪几条**。
///
/// 落点：`&lt;角色&gt;/&lt;工具目录&gt;/UnrealSync/step4-light-configuration.json`。
///
/// 以前这一坨存在整体会话缓存（`UnrealSyncSessionCache.LightConfigurationItems`）里，
/// 和其它步骤共用一大坨；前两步已经搬出去了，这里跟着搬。
/// </summary>
internal static class Step4LightConfigurationCache
{
    /// <summary>文件格式版本：加字段就抬一版，读到不认识的版本一律当没缓存。</summary>
    public const int CurrentVersion = 1;

    public const string FileName = "step4-light-configuration.json";

    public static string GetFilePath(CharacterCard? character)
    {
        var folder = UnrealSyncSessionCacheService.GetCacheFolderPath(character);
        return string.IsNullOrWhiteSpace(folder) ? string.Empty : Path.Combine(folder, FileName);
    }

    /// <summary>写缓存。落盘失败不该打断流程，返回 false 让调用方决定要不要提一句。</summary>
    public static bool Save(
        CharacterCard? character,
        IReadOnlyList<UnrealLightConfigurationResultItem> items,
        IReadOnlyCollection<string> selectedStableIds,
        string resultMessage)
    {
        var path = GetFilePath(character);
        if (character is null || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var document = new Step4LightConfigurationCacheDocument
        {
            Version = CurrentVersion,
            CharacterCode = character.Code,
            SavedAtUtc = DateTimeOffset.UtcNow,
            ResultMessage = resultMessage ?? string.Empty,
            SelectedStableIds = selectedStableIds.ToArray(),
            Items = items.Select(Step4LightConfigurationCacheItem.From).ToArray()
        };

        try
        {
            AtomicFileWriter.WriteAllText(
                path,
                System.Text.Json.JsonSerializer.Serialize(
                    document,
                    AppJsonSerializerContext.Default.Step4LightConfigurationCacheDocument));
            return true;
        }
        catch (Exception)
        {
            // 写不进去不是错误：下次检测会再写一遍。
            return false;
        }
    }

    /// <summary>读缓存；没有 / 版本不认识 / 角色代号对不上 / 坏了，都返回 null（当作"没查过"）。</summary>
    public static Step4LightConfigurationCacheDocument? TryLoad(CharacterCard? character, string characterCode)
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
                AppJsonSerializerContext.Default.Step4LightConfigurationCacheDocument);
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

/// <summary>第四步缓存的落盘形状。</summary>
internal sealed record Step4LightConfigurationCacheDocument
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
    public Step4LightConfigurationCacheItem[] Items { get; init; } = [];
}

/// <summary>一条基础配置项（字段和 UnrealLightConfigurationResultItem 一一对应）。</summary>
internal sealed record Step4LightConfigurationCacheItem
{
    [JsonPropertyName("stableId")]
    public string StableId { get; init; } = string.Empty;

    [JsonPropertyName("groupName")]
    public string GroupName { get; init; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("targetPath")]
    public string TargetPath { get; init; } = string.Empty;

    [JsonPropertyName("targetField")]
    public string TargetField { get; init; } = string.Empty;

    [JsonPropertyName("sourceSummary")]
    public string SourceSummary { get; init; } = string.Empty;

    [JsonPropertyName("currentSummary")]
    public string CurrentSummary { get; init; } = string.Empty;

    [JsonPropertyName("targetSummary")]
    public string TargetSummary { get; init; } = string.Empty;

    [JsonPropertyName("currentValues")]
    public string[] CurrentValues { get; init; } = [];

    [JsonPropertyName("targetValues")]
    public string[] TargetValues { get; init; } = [];

    [JsonPropertyName("status")]
    public UnrealLightConfigurationStatus Status { get; init; }

    [JsonPropertyName("errorMessage")]
    public string ErrorMessage { get; init; } = string.Empty;

    public static Step4LightConfigurationCacheItem From(UnrealLightConfigurationResultItem item) => new()
    {
        StableId = item.StableId,
        GroupName = item.GroupName,
        DisplayName = item.DisplayName,
        TargetPath = item.TargetPath,
        TargetField = item.TargetField,
        SourceSummary = item.SourceSummary,
        CurrentSummary = item.CurrentSummary,
        TargetSummary = item.TargetSummary,
        CurrentValues = item.CurrentValues.ToArray(),
        TargetValues = item.TargetValues.ToArray(),
        Status = item.Status,
        ErrorMessage = item.ErrorMessage
    };

    public UnrealLightConfigurationResultItem ToResultItem() => new()
    {
        StableId = StableId,
        GroupName = GroupName,
        DisplayName = DisplayName,
        TargetPath = TargetPath,
        TargetField = TargetField,
        SourceSummary = SourceSummary,
        CurrentSummary = CurrentSummary,
        TargetSummary = TargetSummary,
        CurrentValues = CurrentValues.ToList(),
        TargetValues = TargetValues.ToList(),
        Status = Status,
        ErrorMessage = ErrorMessage
    };
}
