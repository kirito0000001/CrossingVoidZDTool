using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CrossingVoidZDTool.Services.Atlas;

/// <summary>
/// 把「动作素材目录里的 PNG」翻译成图集工具的清单。
///
/// **数据源是素材目录本身，不是序列清单** —— 这一点曾经做错过，记在这里免得改回去：
///
/// 序列清单（<c>sequence.json</c>）回答的是「这条动画第几格放哪张图」，
/// 它是**序列编排**，里面同一条路径可以被引用多次（复用）。
/// 图集要回答的是另一件事：**素材库里到底有哪几张图**。
///
/// 早期版本从序列清单推导帧列表，于是把复用位置也各出了一条，
/// 23 帧的序列在目录里只有 17 张图，却打出了 23 个格子 ——
/// 6 张图白占地方，还把「素材」和「序列」两件事混成了一件。
///
/// 现在的规则一句话：**目录里有几张 PNG，就打几个格子。**
/// 好处是**零判断** —— 不去猜哪个是复用、要不要合并，也就没有判断失误的余地。
/// 谁复用谁、序列怎么排，都是同步序列那一步的事，图集不掺和。
///
/// 顺序对图集**无意义**（决定播放顺序的是同步阶段的 Flipbook 索引），
/// 所以按文件名升序排一个稳定顺序即可，不追求语义。
/// </summary>
internal static class AtlasManifestWriter
{
    /// <summary>
    /// 图集只认 PNG。
    ///
    /// 素材池那边允许 jpg/webp/bmp（见 <c>SequenceFramePool.IsSupportedImage</c>），
    /// 但**图集这里是窄的**：PNG 才有确定的无损语义和 alpha，
    /// 而图集要拿去做 UE 贴图，混进有损格式会出现「本地看着没事、进引擎才发现边缘脏」。
    /// </summary>
    private const string SupportedExtension = ".png";

    /// <summary>图集名：<c>&lt;角色代号&gt;_&lt;动作变体代号&gt;</c>，例如 <c>Misaka_Sk2</c>。</summary>
    public static string BuildAtlasName(string characterCode, string variantCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(characterCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(variantCode);
        return $"{characterCode}_{variantCode}";
    }

    /// <summary>
    /// 枚举动作素材目录里的 PNG，按文件名升序。
    ///
    /// 不递归子目录：素材目录的约定是平铺的，跑进子目录会把别的东西也扫进来。
    /// </summary>
    public static IReadOnlyList<string> EnumerateSourceImages(string framesFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(framesFolder);
        if (!Directory.Exists(framesFolder))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(framesFolder, "*", SearchOption.TopDirectoryOnly)
            .Where(path => string.Equals(
                Path.GetExtension(path), SupportedExtension, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 生成清单对象。
    /// </summary>
    /// <param name="characterCode">角色代号，必须与 Unreal 角色目录名完全一致。</param>
    /// <param name="definition">动作定义（取规范代号用）。</param>
    /// <param name="formIndex">形态下标。</param>
    /// <param name="sourceImagePaths">素材目录里的 PNG 绝对路径（<see cref="EnumerateSourceImages"/> 的产出）。</param>
    /// <exception cref="InvalidOperationException">
    /// 一张图都没有时抛出。空图集是没有意义的产物，必须**当场报错**，
    /// 不能产出一张空图当成功。
    /// </exception>
    public static AtlasManifest Build(
        string characterCode,
        SequenceActionDefinition definition,
        int formIndex,
        IReadOnlyList<string> sourceImagePaths)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(sourceImagePaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(characterCode);

        if (sourceImagePaths.Count == 0)
        {
            throw new InvalidOperationException(
                $"动作「{definition.Code}」的素材目录里没有任何 PNG，不能生成图集。"
                + "请先在 St5 序列帧页导入帧素材。");
        }

        var variantCode = SequenceActionCatalog.GetVariantCode(definition, formIndex);
        var atlasName = BuildAtlasName(characterCode, variantCode);
        var total = sourceImagePaths.Count;
        var entries = new List<AtlasManifestFrame>(total);
        var missing = new List<string>();

        // index 从 1 起（清单接口的约定），等于「数组下标 + 1」。
        // 这里的下标是**素材目录里的顺序**，不是序列里的位置 —— 两者不再有关系。
        for (var ordinal = 0; ordinal < total; ordinal++)
        {
            var absolutePath = Path.GetFullPath(sourceImagePaths[ordinal]);
            if (!File.Exists(absolutePath))
            {
                // 枚举完到读之间文件被删掉了（或符号链接断了），照旧当场报错——
                // 少一格的图集比没有图集更麻烦，要在 UE 里才发现。
                missing.Add(absolutePath);
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
            throw new InvalidOperationException(
                $"动作「{definition.Code}」的素材在枚举后被移走，共有 {missing.Count} 处："
                + string.Join("；", missing));
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
