using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 第二步「规整素材」自己的缓存：一步一个文件，只装这一步的东西 ——
/// **用户做过的规整决策**（哪个资产重定向到哪儿 / 哪个不用管）。
///
/// 落点：`&lt;角色&gt;/&lt;工具目录&gt;/UnrealSync/step2-normalization.json`。
///
/// 以前这份决策存在整体会话缓存（`UnrealSyncSessionCache.NormalizationDecisions`）里，
/// 和其它步骤共用一大坨；第 1 步已经搬出去了，这里跟着搬。
/// </summary>
internal static class Step2NormalizationCache
{
    public const int CurrentVersion = 1;

    public const string FileName = "step2-normalization.json";

    public static string GetFilePath(CharacterCard? character)
    {
        var folder = UnrealSyncCacheFolder.GetCacheFolderPath(character);
        return string.IsNullOrWhiteSpace(folder) ? string.Empty : Path.Combine(folder, FileName);
    }

    public static bool Save(CharacterCard? character, IReadOnlyDictionary<string, string> decisions)
    {
        var path = GetFilePath(character);
        if (character is null || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var document = new Step2NormalizationCacheDocument
        {
            Version = CurrentVersion,
            CharacterCode = character.Code,
            SavedAtUtc = DateTimeOffset.UtcNow,
            Decisions = new Dictionary<string, string>(decisions, StringComparer.OrdinalIgnoreCase)
        };

        try
        {
            AtomicFileWriter.WriteAllText(
                path,
                System.Text.Json.JsonSerializer.Serialize(
                    document,
                    AppJsonSerializerContext.Default.Step2NormalizationCacheDocument));
            return true;
        }
        catch (Exception)
        {
            // 写不进去不是错误：下次做决策时会再写一遍。
            return false;
        }
    }

    /// <summary>读缓存；没有 / 版本不认识 / 角色代号对不上 / 坏了，都返回 null（当作"没做过规整"）。</summary>
    public static Step2NormalizationCacheDocument? TryLoad(CharacterCard? character, string characterCode)
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
                AppJsonSerializerContext.Default.Step2NormalizationCacheDocument);
            if (document is null || document.Version != CurrentVersion)
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

/// <summary>第二步缓存的落盘形状。</summary>
internal sealed record Step2NormalizationCacheDocument
{
    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("characterCode")]
    public string CharacterCode { get; init; } = string.Empty;

    [JsonPropertyName("savedAtUtc")]
    public DateTimeOffset SavedAtUtc { get; init; }

    /// <summary>稳定 ID → 决策（重定向目标路径；"__not_required__" 表示不用管）。</summary>
    [JsonPropertyName("decisions")]
    public Dictionary<string, string> Decisions { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
