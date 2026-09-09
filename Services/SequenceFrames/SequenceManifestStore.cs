using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 动作清单（<c>sequence.json</c>）的读写、规范化与迁移，外加「这个角色到底引用了哪些帧文件」
/// 这个只有清单能回答的问题。
///
/// 从 <see cref="SequenceFrameService"/> 里搬出来的：清单是序列帧这一摊的唯一事实来源，
/// 帧率、时长、语音绑定、帧身份全在里面，而读写它的代码原本和几十个编辑操作混在一起，
/// 想单独验「清单读坏了会怎样」只能先造出一整套编辑场景。
///
/// 两处踩过坑、必须原样保留的行为：
/// 一是 <see cref="TryEnumerateReferencedFramePaths"/> 与 <see cref="SequenceManifestUnreadableException"/>
/// ——清单读不出来时必须让调用方知道「没读到」，而不是回一个残缺的引用集；
/// 二是 <see cref="JsonOptions"/> 上的 <c>PropertyNameCaseInsensitive</c>——自建 options 时
/// 源生成器上的 <c>[JsonSourceGenerationOptions]</c> 不生效，漏掉会让同一份清单
/// 在不同代码路径里读出不同结果。
/// </summary>
internal static class SequenceManifestStore
{
    /// <summary>
    /// 清单的序列化设置。<c>WriteIndented</c> 保留：这些文件躺在用户的素材目录里，
    /// 是要给人翻、给版本管理 diff 的。<c>PropertyNameCaseInsensitive</c> 必须显式给——
    /// 见类注释，这里曾经漏过一次，导致同一份 sequence.json 在这套代码里有两种读法。
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>写清单的唯一入口用的类型信息。跨域的语音绑定重写也共用这一份，排版才不会来回抖。</summary>
    internal static readonly JsonTypeInfo<SequenceFrameManifest> ManifestJsonTypeInfo =
        new AppJsonSerializerContext(JsonOptions).SequenceFrameManifest;

    public static SequenceFrameManifest Create(SequenceFrameAction action)
    {
        return new SequenceFrameManifest
        {
            ActionCode = action.Code,
            Fps = SequenceFrameSpec.DefaultFps
        };
    }

    /// <summary>
    /// 复制一条帧格。<paramref name="preserveSyncId"/> 为 false 时换一个新身份——
    /// 复制出来的帧格在 Unreal 侧是另一个资产，共用身份会让同步误判成「同一帧被移动了」。
    /// </summary>
    public static SequenceFrameManifestEntry CloneEntry(
        SequenceFrameManifestEntry entry,
        bool preserveSyncId = true)
    {
        return new SequenceFrameManifestEntry
        {
            SyncId = preserveSyncId ? entry.SyncId : Guid.NewGuid().ToString("N"),
            RelativePath = entry.RelativePath,
            IsBlank = entry.IsBlank,
            DurationFrames = entry.DurationFrames,
            VoiceRelativePath = entry.VoiceRelativePath
        };
    }

    /// <summary>读出并规范化一份已经存在的清单。规范化的结果会立刻落盘。</summary>
    public static SequenceFrameManifest LoadAndNormalize(CharacterCard character, SequenceFrameAction action)
    {
        var manifestPath = SequenceActionFolderLayout.GetManifestPath(character, action);
        try
        {
            var manifest = JsonSerializer.Deserialize(
                File.ReadAllText(manifestPath, Encoding.UTF8),
                AppJsonSerializerContext.Default.SequenceFrameManifest) ?? Create(action);
            return Normalize(character, action, manifest);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"序列帧清单读取失败：{manifestPath}", ex);
        }
    }

    public static void Save(CharacterCard character, SequenceFrameAction action, SequenceFrameManifest manifest)
    {
        manifest.SchemaVersion = 3;
        manifest.ActionCode = action.Code;
        manifest.Fps = Math.Clamp(manifest.Fps <= 0 ? SequenceFrameSpec.DefaultFps : manifest.Fps, 1, 60);
        manifest.Frames ??= [];
        Directory.CreateDirectory(SequenceActionFolderLayout.GetActionFolderPath(character, action));
        Directory.CreateDirectory(SequenceActionFolderLayout.GetFramesFolderPath(character, action));
        WriteFileIfChanged(SequenceActionFolderLayout.GetManifestPath(character, action), manifest);
    }

    /// <summary>
    /// 按路径读一份清单，读不出来回 null。
    /// 只给「读坏了跳过也不会造成破坏」的场景用；要据此删文件的路径请走
    /// <see cref="TryEnumerateReferencedFramePaths"/>。
    /// </summary>
    public static SequenceFrameManifest? ReadFileOrNull(string manifestPath)
    {
        try
        {
            return JsonSerializer.Deserialize(
                File.ReadAllText(manifestPath, Encoding.UTF8),
                ManifestJsonTypeInfo);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>内容没变就不写，免得每次打开界面都刷一遍文件时间戳、把同步误判成「工具箱改过了」。</summary>
    public static void WriteFileIfChanged(string manifestPath, SequenceFrameManifest manifest)
    {
        var text = JsonSerializer.Serialize(manifest, ManifestJsonTypeInfo);
        var current = File.Exists(manifestPath) ? File.ReadAllText(manifestPath, Encoding.UTF8) : string.Empty;
        if (string.Equals(text, current, StringComparison.Ordinal))
        {
            return;
        }

        AtomicFileWriter.WriteAllText(manifestPath, text, Encoding.UTF8);
    }

    public static void WriteFile(string manifestPath, SequenceFrameManifest manifest)
    {
        AtomicFileWriter.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(manifest, ManifestJsonTypeInfo),
            Encoding.UTF8);
    }

    /// <summary>枚举角色名下所有动作的清单文件路径。</summary>
    public static IEnumerable<string> EnumerateManifestPaths(CharacterCard character)
    {
        var materialFolderPath = SequenceActionFolderLayout.GetMaterialRootPath(character);
        if (!Directory.Exists(materialFolderPath))
        {
            return [];
        }

        return Directory.EnumerateFiles(
            materialFolderPath,
            SequenceActionFolderLayout.ManifestFileName,
            SearchOption.AllDirectories);
    }

    /// <summary>
    /// 收集这个角色所有动作里被清单引用到的帧文件。
    /// 只要有一份清单读不出来就返回 false——调用方据此放弃本轮清理。
    /// </summary>
    public static bool TryEnumerateReferencedFramePaths(CharacterCard character, out HashSet<string> referenced)
    {
        referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var path in EnumerateReferencedFramePaths(character))
            {
                referenced.Add(path);
            }

            return true;
        }
        catch (SequenceManifestUnreadableException error)
        {
            ToolboxLog.Warn($"序列清单读取失败：{error.ManifestPath}", error.InnerException);
            return false;
        }
    }

    /// <summary>清单读不出来时抛这个，让上层能区分「真的没有引用」和「没读到」。</summary>
    private sealed class SequenceManifestUnreadableException(string manifestPath, Exception inner)
        : Exception($"序列清单读取失败：{manifestPath}", inner)
    {
        public string ManifestPath { get; } = manifestPath;
    }

    public static IEnumerable<string> EnumerateReferencedFramePaths(CharacterCard character)
    {
        foreach (var manifestPath in EnumerateManifestPaths(character))
        {
            SequenceFrameManifest? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize(
                    File.ReadAllText(manifestPath, Encoding.UTF8),
                    AppJsonSerializerContext.Default.SequenceFrameManifest);
            }
            catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
            {
                // 不能 continue：那等于说「这个动作没有引用任何帧」，
                // 而调用方会照着这个结论去删文件。
                throw new SequenceManifestUnreadableException(manifestPath, error);
            }

            if (manifest?.Frames is null)
            {
                continue;
            }

            var actionFolderPath = Path.GetDirectoryName(manifestPath);
            if (string.IsNullOrWhiteSpace(actionFolderPath))
            {
                continue;
            }

            foreach (var entry in manifest.Frames)
            {
                if (entry is null || entry.IsBlank || string.IsNullOrWhiteSpace(entry.RelativePath))
                {
                    continue;
                }

                yield return Path.GetFullPath(Path.Combine(
                    actionFolderPath,
                    SequenceActionFolderLayout.NormalizeRelativePath(entry.RelativePath)));
            }
        }
    }

    private static SequenceFrameManifest Normalize(
        CharacterCard character,
        SequenceFrameAction action,
        SequenceFrameManifest manifest)
    {
        manifest.SchemaVersion = 3;
        MigrateActionCodeAliases(manifest, action);
        manifest.ActionCode = action.Code;
        manifest.Fps = Math.Clamp(manifest.Fps <= 0 ? SequenceFrameSpec.DefaultFps : manifest.Fps, 1, 60);
        manifest.Frames ??= [];
        for (var index = manifest.Frames.Count - 1; index >= 0; index--)
        {
            var entry = manifest.Frames[index];
            if (entry is null)
            {
                manifest.Frames.RemoveAt(index);
                continue;
            }

            entry.DurationFrames = Math.Clamp(entry.DurationFrames, 1, SequenceFrameSpec.MaxFrameDuration);
            entry.SyncId = string.IsNullOrWhiteSpace(entry.SyncId)
                ? Guid.NewGuid().ToString("N")
                : entry.SyncId.Trim();
            entry.VoiceRelativePath = SequenceActionFolderLayout.NormalizeRelativePath(
                entry.VoiceRelativePath ?? string.Empty);
            if (entry.IsBlank)
            {
                entry.RelativePath = string.Empty;
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.RelativePath))
            {
                manifest.Frames.RemoveAt(index);
                continue;
            }

            entry.RelativePath = SequenceActionFolderLayout.NormalizeRelativePath(entry.RelativePath);
        }

        Save(character, action, manifest);
        return manifest;
    }

    /// <summary>旧清单里可能写着历史拼写的动作代号，认得出来就就地改成规范代号。</summary>
    private static void MigrateActionCodeAliases(SequenceFrameManifest manifest, SequenceFrameAction action)
    {
        if (string.IsNullOrWhiteSpace(manifest.ActionCode) ||
            string.Equals(manifest.ActionCode, action.Code, StringComparison.OrdinalIgnoreCase))
            return;
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Ondm"] = "OnDamage", ["OnDM"] = "OnDamage", ["Defense"] = "Defence",
            ["Flydown"] = "FlyDown", ["Flystart"] = "FlyStart", ["Standup"] = "StandUP"
        };
        if (aliases.TryGetValue(manifest.ActionCode.Trim(), out var canonical) &&
            string.Equals(canonical, action.Code, StringComparison.OrdinalIgnoreCase))
            manifest.ActionCode = action.Code;
    }
}
