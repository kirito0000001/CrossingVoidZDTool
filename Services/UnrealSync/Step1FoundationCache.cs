using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 第一步「底层检测」自己的缓存：**一步一个文件，只装这一步的东西**。
///
/// 落点：`&lt;角色&gt;/&lt;工具目录&gt;/UnrealSync/step1-foundation.json`
/// （和同步台的分步进度缓存同一个目录，跟着角色走、角色之间天然不串）。
///
/// 为什么不用那份整体缓存（`UnrealSyncSessionCache` / `&lt;项目键&gt;.json`）：
/// 那份是所有步骤共用的一大坨（实测每份 7 MB，装的是整棵差异树），步骤之间会互相影响 ——
/// `_loadedPublishStep` 默认写成 3、发布阶段被持久化后劫持第二步的过滤，都是这么来的。
/// 第 1 步真正需要的其实只有"这十几条检查项 + 什么时候查的"，几十 KB 就够。
/// </summary>
internal static class Step1FoundationCache
{
    /// <summary>文件格式版本：加字段就抬一版，读到不认识的版本一律当没缓存。</summary>
    public const int CurrentVersion = 1;

    public const string FileName = "step1-foundation.json";

    public static string GetFilePath(CharacterCard? character)
    {
        var folder = UnrealSyncCacheFolder.GetCacheFolderPath(character);
        return string.IsNullOrWhiteSpace(folder) ? string.Empty : Path.Combine(folder, FileName);
    }

    /// <summary>写缓存。落盘失败不该打断流程，返回 false 让调用方决定要不要提一句。</summary>
    public static bool Save(
        CharacterCard? character,
        IReadOnlyList<UnrealPublishFoundationCheckItem> items,
        bool requireAssetTypes)
    {
        var path = GetFilePath(character);
        if (string.IsNullOrWhiteSpace(path) || character is null)
        {
            return false;
        }

        var document = new Step1FoundationCacheDocument
        {
            Version = CurrentVersion,
            CharacterCode = character.Code,
            CheckedAtUtc = DateTimeOffset.UtcNow,
            RequireAssetTypes = requireAssetTypes,
            Items = items
                .Select(item => new Step1FoundationCacheItem
                {
                    DisplayName = item.DisplayName,
                    ExpectedPath = item.ExpectedPath,
                    ActualPath = item.ActualPath,
                    IsCompliant = item.IsCompliant,
                    Problem = item.Problem,
                    ExpectedType = item.ExpectedType,
                    ActualType = item.ActualType
                })
                .ToArray()
        };

        try
        {
            AtomicFileWriter.WriteAllText(
                path,
                System.Text.Json.JsonSerializer.Serialize(
                    document,
                    AppJsonSerializerContext.Default.Step1FoundationCacheDocument));
            return true;
        }
        catch (Exception)
        {
            // 缓存写不进去不是错误：下次刷新会再写一遍。
            return false;
        }
    }

    /// <summary>读缓存；没有 / 版本不认识 / 坏了，都返回 null（当作"没查过"）。</summary>
    public static Step1FoundationCacheDocument? TryLoad(CharacterCard? character, string characterCode)
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
                AppJsonSerializerContext.Default.Step1FoundationCacheDocument);
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

/// <summary>第一步缓存的落盘形状。</summary>
internal sealed record Step1FoundationCacheDocument
{
    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("characterCode")]
    public string CharacterCode { get; init; } = string.Empty;

    [JsonPropertyName("checkedAtUtc")]
    public DateTimeOffset CheckedAtUtc { get; init; }

    /// <summary>这次检查是不是带"资产类型复检"（第二步会用到更严格的那一档）。</summary>
    [JsonPropertyName("requireAssetTypes")]
    public bool RequireAssetTypes { get; init; }

    [JsonPropertyName("items")]
    public Step1FoundationCacheItem[] Items { get; init; } = [];
}

/// <summary>一条底层检查项（字段和 UnrealPublishFoundationCheckItem 一一对应）。</summary>
internal sealed record Step1FoundationCacheItem
{
    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("expectedPath")]
    public string ExpectedPath { get; init; } = string.Empty;

    [JsonPropertyName("actualPath")]
    public string ActualPath { get; init; } = string.Empty;

    [JsonPropertyName("isCompliant")]
    public bool IsCompliant { get; init; }

    [JsonPropertyName("problem")]
    public string Problem { get; init; } = string.Empty;

    [JsonPropertyName("expectedType")]
    public string ExpectedType { get; init; } = string.Empty;

    [JsonPropertyName("actualType")]
    public string ActualType { get; init; } = string.Empty;
}
