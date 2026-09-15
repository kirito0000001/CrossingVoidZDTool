using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CrossingVoidZDTool.Services.Atlas;

/// <summary>
/// 把工具箱的序列帧清单（<see cref="SequenceFrameManifest"/>）翻译成图集工具的清单。
///
/// **这一层的唯一难点是「序号」**，而序号错了会静默建重复资产，所以规则写在这里、
/// 只写一遍：
///
/// 1. **序号 = 该帧在 <c>Frames</c> 数组里的原始下标 + 1**，从 1 起（清单接口的约定）。
///    Unreal 侧从 0 起，这里是 1 起，差一个 1 —— 这是唯一需要记住的偏移。
/// 2. **空白帧不产出像素，但绝不能压缩别人的序号**。它占位、跳过，后面的帧号照原样。
///    所以不能「先过滤空白帧再 enumerate」——那样第 5 帧会被叫成第 4 帧。
/// 3. **复用帧（同一张图出现在多个位置）每个位置都要出一条**，因为每个位置有**自己的精灵名**：
///    <c>Sk2_Frame05_Sprite</c> 与 <c>Sk2_Frame17_Sprite</c> 是两张独立资产。
///    真正要防的是**同名**，不是同图。
/// 4. 精灵名一律走 <see cref="SequenceActionCatalog.GetFrameSpriteName"/>，
///    **不自己拼字符串** —— 那是「和 Unreal 侧漂移」的唯一入口。
///
/// 纯函数，不碰磁盘（路径拼装也在这里做完，出去就是可落盘的 JSON），所以好测。
/// </summary>
internal static class AtlasManifestWriter
{
    /// <summary>图集名：<c>&lt;角色代号&gt;_&lt;动作变体代号&gt;</c>，例如 <c>Misaka_Sk2</c>。</summary>
    public static string BuildAtlasName(string characterCode, string variantCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(characterCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(variantCode);
        return $"{characterCode}_{variantCode}";
    }

    /// <summary>
    /// 生成清单对象。
    /// </summary>
    /// <param name="characterCode">角色代号，必须与 Unreal 角色目录名完全一致。</param>
    /// <param name="actionFolder">动作素材目录的绝对路径（<c>RelativePath</c> 的解析基准）。</param>
    /// <param name="definition">动作定义（取规范代号用）。</param>
    /// <param name="formIndex">形态下标。</param>
    /// <param name="manifest">工具箱的序列帧清单。</param>
    /// <exception cref="InvalidOperationException">
    /// 清单为空、或全是空白帧、或某帧的图片不存在时抛出。
    /// 空图集是没有意义的产物，必须**当场报错**，不能产出一张空图当成功。
    /// </exception>
    public static AtlasManifest Build(
        string characterCode,
        string actionFolder,
        SequenceActionDefinition definition,
        int formIndex,
        SequenceFrameManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(characterCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionFolder);

        var frames = manifest.Frames;
        var total = frames.Count;
        if (total == 0)
        {
            throw new InvalidOperationException(
                $"动作「{definition.Code}」没有任何帧，不能生成图集。");
        }

        var variantCode = SequenceActionCatalog.GetVariantCode(definition, formIndex);
        var atlasName = BuildAtlasName(characterCode, variantCode);
        var entries = new List<AtlasManifestFrame>(total);
        var missing = new List<string>();

        // 注意这里用的是**原始下标** ordinal，不是 items.Count——
        // 空白帧被跳过了，但它的序号位必须留给它后面的帧。
        for (var ordinal = 0; ordinal < total; ordinal++)
        {
            var frame = frames[ordinal];
            if (frame.IsBlank)
            {
                // 空白帧占位不出图：它有 DurationFrames 语义但没有像素。
                continue;
            }

            var relativePath = (frame.RelativePath ?? string.Empty).Trim();
            if (relativePath.Length == 0)
            {
                missing.Add($"第 {ordinal + 1} 帧没有记录文件路径");
                continue;
            }

            var absolutePath = Path.GetFullPath(
                Path.Combine(actionFolder, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(absolutePath))
            {
                missing.Add($"第 {ordinal + 1} 帧的图片不存在：{absolutePath}");
                continue;
            }

            entries.Add(new AtlasManifestFrame
            {
                File = absolutePath,
                Name = SequenceActionCatalog.GetFrameSpriteName(definition, formIndex, ordinal, total),
                Index = ordinal + 1,
            });
        }

        if (missing.Count > 0)
        {
            // 缺图直接报错，不做「跳过继续」：少一帧的图集和 Flipbook 对不上序号，
            // 而那个错误要到 Unreal 里才看得出来，那时候已经很难倒推是哪一步出的问题。
            throw new InvalidOperationException(
                $"动作「{definition.Code}」的素材不完整，共有 {missing.Count} 处问题："
                + string.Join("；", missing));
        }

        if (entries.Count == 0)
        {
            throw new InvalidOperationException(
                $"动作「{definition.Code}」的 {total} 帧全是空白帧，不能生成图集。");
        }

        return new AtlasManifest
        {
            Atlas = atlasName,
            Mode = "pack",
            Frames = entries,
        };
    }

    /// <summary>把清单序列化成 JSON 文本（缩进，用户可能直接打开看）。</summary>
    public static string Serialize(AtlasManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return JsonSerializer.Serialize(manifest, AtlasJsonContext.Indented.AtlasManifest);
    }

    /// <summary>清单落盘。走原子写，避免留下半截 JSON。</summary>
    public static void Write(string path, AtlasManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        AtomicFileWriter.WriteAllText(path, Serialize(manifest));
    }
}
