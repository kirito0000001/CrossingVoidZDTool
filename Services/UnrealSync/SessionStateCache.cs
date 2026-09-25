using System;
using System.IO;
using System.Text.Json.Serialization;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// **全局现场**：与"哪一步"无关的那点东西 —— 引擎路径 / 工程路径 / 方向 /
/// 当前角色 / 上次检测时间 / 两个显示开关。
///
/// 落点：`&lt;角色&gt;/&lt;工具目录&gt;/UnrealSync/session.json`。
///
/// 它取代的是原来那份 `UnrealSyncSessionCache`（所有步骤共用的一大坨）—— 那一坨里
/// **各步的结果/勾选/差异树**是 bug 的温床（第 3 步那次"假报错"就是因为同一份结果
/// 在两个地方各存一份、盖不干净）。现在各步只认自己的小文件，这里**只装全局的**：
/// 谁的工程、哪个方向、上次什么时候查的。
///
/// ⚠️ **不装"上次停在哪一步"**（晓桀 2026-09-24 明确不要）：冷启动一律从头开始。
/// </summary>
internal static class SessionStateCache
{
    public const int CurrentVersion = 1;

    public const string FileName = "session.json";

    public static string GetFilePath(CharacterCard? character)
    {
        var folder = UnrealSyncCacheFolder.GetCacheFolderPath(character);
        return string.IsNullOrWhiteSpace(folder) ? string.Empty : Path.Combine(folder, FileName);
    }

    public static bool Save(CharacterCard? character, SessionStateCacheDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var path = GetFilePath(character);
        if (character is null || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            AtomicFileWriter.WriteAllText(
                path,
                System.Text.Json.JsonSerializer.Serialize(
                    document with { Version = CurrentVersion, SavedAtUtc = DateTimeOffset.UtcNow },
                    AppJsonSerializerContext.Default.SessionStateCacheDocument));
            return true;
        }
        catch (Exception error)
        {
            // 写不进去不是错误：下次交互会再写一遍。但**不能一声不吭**（同第 1 步）。
            ToolboxLog.Warn($"[UnrealSync] 全局现场写入失败：{path}", error);
            return false;
        }
    }

    /// <summary>读现场；没有 / 版本不认识 / 坏了，都返回 null（当作"没存过"）。</summary>
    public static SessionStateCacheDocument? TryLoad(CharacterCard? character)
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
                AppJsonSerializerContext.Default.SessionStateCacheDocument);
            return document is null || document.Version != CurrentVersion ? null : document;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>
/// 冷启动恢复**现场**的结果：<paramref name="Restored"/> 为 false 表示"没有可用现场，
/// 当全新开始"，<paramref name="ErrorMessage"/> 非空表示**有现场但不能用**（引擎路径/工程/
/// 角色对不上）—— 壳侧据此只记一条日志 + 一个提示，不做别的。
/// </summary>
internal readonly record struct SessionRestoreOutcome(bool Restored, string ErrorMessage);

/// <summary>全局现场的落盘形状。加字段就抬 <see cref="SessionStateCache.CurrentVersion"/>。</summary>
internal sealed record SessionStateCacheDocument
{
    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("savedAtUtc")]
    public DateTimeOffset SavedAtUtc { get; init; }

    [JsonPropertyName("characterCode")]
    public string CharacterCode { get; init; } = string.Empty;

    /// <summary>引擎路径 —— 与当前设置不一致时这份现场整体作废（可能换机器/换装了）。</summary>
    [JsonPropertyName("enginePath")]
    public string EnginePath { get; init; } = string.Empty;

    /// <summary>目标工程路径。**同一个角色对接两个工程时靠它区分**（六个小文件里没有工程信息）。</summary>
    [JsonPropertyName("projectPath")]
    public string ProjectPath { get; init; } = string.Empty;

    /// <summary>true = 虚幻→工具箱（导入方向）。</summary>
    [JsonPropertyName("importDirection")]
    public bool ImportDirection { get; init; }

    /// <summary>上次真跑过检测的时刻，喂界面上那句"上次检测：…"。</summary>
    [JsonPropertyName("detectedAt")]
    public DateTimeOffset DetectedAt { get; init; }

    [JsonPropertyName("hideCompletedFoundationChecks")]
    public bool HideCompletedFoundationChecks { get; init; }

    [JsonPropertyName("hideResolvedNormalizationItems")]
    public bool HideResolvedNormalizationItems { get; init; }
}
