using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CrossingVoidZDTool.Services.Atlas;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 动作目录的布局与路径推导：一个动作在磁盘上长什么样，以及绝对路径和清单里那份
/// 相对路径之间怎么互转。
///
/// 从 <see cref="SequenceFrameService"/> 里搬出来的。留在那个类里时，这些两三行的
/// 小函数散落在一千多行中间，改一处目录规则要先把整个服务通读一遍才敢下手。
///
/// 这里最要紧的一条约定：<b>清单里的帧路径是相对动作目录的，语音路径是相对角色目录的</b>。
/// 角色目录要能在 Draft/Completed 之间整体搬家、也要能跟着工作区根目录迁移，
/// 一旦写成绝对路径，搬完之后所有帧和语音绑定就全断了。
/// 任何时候都不要把 <see cref="ResolveManifestPath"/> 的结果写回清单。
/// </summary>
internal static class SequenceActionFolderLayout
{
    /// <summary>每个动作目录下的清单文件名。</summary>
    public const string ManifestFileName = "sequence.json";

    /// <summary>动作目录下存放实际帧图的子目录名。</summary>
    public const string FramesFolderName = CharacterFolderLayout.Frames;

    private const string ZdMaterialFolderName = CharacterFolderLayout.ZdMaterial;

    /// <summary>角色名下所有动作目录的父目录。</summary>
    public static string GetMaterialRootPath(CharacterCard character)
    {
        return Path.Combine(character.FolderPath, ZdMaterialFolderName);
    }

    public static string GetActionFolderPath(CharacterCard character, SequenceFrameAction action)
    {
        return Path.Combine(character.FolderPath, ZdMaterialFolderName, action.Code);
    }

    public static string GetFramesFolderPath(CharacterCard character, SequenceFrameAction action)
    {
        return Path.Combine(GetActionFolderPath(character, action), FramesFolderName);
    }

    public static string GetManifestPath(CharacterCard character, SequenceFrameAction action)
    {
        return Path.Combine(GetActionFolderPath(character, action), ManifestFileName);
    }

    /// <summary>把清单里的相对路径还原成绝对路径。只用于读盘，不要写回清单。</summary>
    public static string ResolveManifestPath(CharacterCard character, SequenceFrameAction action, string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        return Path.GetFullPath(Path.Combine(GetActionFolderPath(character, action), normalized));
    }

    /// <summary>
    /// 这个动作的帧实际用到哪些图片（去重，按文件名升序）。
    ///
    /// **不能用「素材目录里有哪些 PNG」代替。** 帧可以复用别的动作已经收进来的那张图：
    /// <see cref="SequenceFramePool.ImportSource"/> 遇到已存在的帧就直接指过去，
    /// 清单里因此记着 <c>../Death/Frames/xxx.png</c> 这样的跨目录路径（Misaka 实测有 23 帧这样）。
    /// 只看自己的目录，这类动作要么在打包时报「素材目录里没有任何 PNG」，
    /// 要么在生成计划时报「帧素材不在本动作的图集里」—— 同一个根因的两个出口。
    ///
    /// 排序按**文件名**升序，为的是不动已经同步过的动作：以前枚举目录也是按文件名升序，
    /// 而这类动作自己目录里的图正好就是它引用的那些，序号一致、精灵名一致，不需要重建。
    /// </summary>
    public static IReadOnlyList<string> ResolveSourceImages(IEnumerable<SequenceFrameItem> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        return frames
            .Where(frame => frame is { IsBlank: false })
            .Select(frame => frame.FilePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => Path.GetFileName(path) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 一张素材图落在哪个图集里。
    ///
    /// 帧可以复用别的动作的图（<see cref="SequenceFramePool.ImportSource"/> 直接指过去），
    /// 那张图**已经在来源动作的图集里**了，不该再被塞进借用方的图集复制一份。
    /// 所以每张素材都要能回答「我的图集是谁的」：自己的图用自己的图集，
    /// 借来的图用来源动作的图集，矩形也取来源图集里那一格。
    /// </summary>
    /// <param name="OwnerActionCode">图集归属的动作代号（自己的就是自己）。</param>
    /// <param name="IsOwn">这张图是否躺在本动作自己的素材目录里。</param>
    internal sealed record SequenceSourceImageEntry(
        string FilePath,
        string AtlasName,
        string OwnerActionCode,
        bool IsOwn);

    /// <summary>
    /// 按「图集归属」把动作用到的素材分好类。顺序与 <see cref="ResolveSourceImages"/> 一致
    /// （按文件名升序）—— 精灵编号就是按这个顺序算的，两边必须同源。
    /// </summary>
    public static IReadOnlyList<SequenceSourceImageEntry> ResolveSourceImagePlan(
        CharacterCard character,
        SequenceFrameAction action,
        IEnumerable<SequenceFrameItem> frames)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(action);

        var sourceImages = ResolveSourceImages(frames);
        var result = new List<SequenceSourceImageEntry>(sourceImages.Count);
        if (sourceImages.Count == 0)
        {
            return result;
        }

        var ownCode = SequenceActionCatalog.TryResolve(action.Code, out var ownDefinition, out var ownForm)
            ? SequenceActionCatalog.GetVariantCode(ownDefinition, ownForm)
            : action.Code;
        var ownAtlas = AtlasManifestWriter.BuildAtlasName(character.Code, ownCode);
        var ownFramesFolder = Path.GetFullPath(GetFramesFolderPath(character, action));
        var materialRoot = Path.GetFullPath(GetMaterialRootPath(character));

        foreach (var file in sourceImages)
        {
            var full = Path.GetFullPath(file);
            if (full.StartsWith(ownFramesFolder, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new SequenceSourceImageEntry(full, ownAtlas, action.Code, IsOwn: true));
                continue;
            }

            var ownerCode = ResolveOwnerActionCode(materialRoot, full);
            if (ownerCode is null)
            {
                // 不在 ZDMaterial/<动作>/ 结构里的图（理论上不该有：导入时都会收进帧池）。
                // 拿不准就按自己的处理 —— 宁可多打一格，也不要引用一个不存在的图集。
                result.Add(new SequenceSourceImageEntry(full, ownAtlas, action.Code, IsOwn: true));
                continue;
            }

            var variant = SequenceActionCatalog.TryResolve(ownerCode, out var ownerDefinition, out var ownerForm)
                ? SequenceActionCatalog.GetVariantCode(ownerDefinition, ownerForm)
                : ownerCode;
            result.Add(new SequenceSourceImageEntry(
                full,
                AtlasManifestWriter.BuildAtlasName(character.Code, variant),
                ownerCode,
                IsOwn: false));
        }

        return result;
    }

    /// <summary>从完整路径反查这张图归哪个动作目录所有；不在 ZDMaterial 结构里返回 null。</summary>
    public static string? ResolveOwnerActionCode(string materialRootPath, string filePath)
    {
        var root = Path.GetFullPath(materialRootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var full = Path.GetFullPath(filePath);
        var prefix = root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rest = full[prefix.Length..];
        var separator = rest.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        return separator <= 0 ? null : rest[..separator];
    }

    /// <summary>
    /// 这个动作不需要自己的素材目录时，别把它空着留在那儿。
    ///
    /// 帧可以整条复用别的动作已经收进来的图（Misaka 的 FlyStart / FlyDown / Flying 就是这样，
    /// 它们一张自己的图都没有），于是 <c>Frames</c> 目录被建出来一次、之后再也不会有文件进去。
    /// 空目录没有任何含义，却会让人以为「这里漏导了素材」，去手工补一遍。
    ///
    /// 只在目录**确实为空**时删；有文件、读不了、或者正好被别的进程占着，就原样留着。
    /// </summary>
    public static bool RemoveFramesFolderIfEmpty(CharacterCard character, SequenceFrameAction action)
    {
        var folder = GetFramesFolderPath(character, action);
        try
        {
            if (!Directory.Exists(folder) || Directory.EnumerateFileSystemEntries(folder).Any())
            {
                return false;
            }

            Directory.Delete(folder);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>把一个帧文件按「帧池里的同名文件」推回相对路径，用于按路径反查清单条目。</summary>
    public static string ToPoolRelativePath(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        return NormalizeRelativePath(Path.Combine(FramesFolderName, fileName));
    }

    /// <summary>语音绑定存的是相对角色目录的路径。</summary>
    public static string ToCharacterRelativePath(CharacterCard character, string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return string.Empty;
        }

        return NormalizeRelativePath(Path.GetRelativePath(character.FolderPath, Path.GetFullPath(filePath)));
    }

    public static string ResolveVoicePath(CharacterCard character, string? relativePath)
    {
        return string.IsNullOrWhiteSpace(relativePath)
            ? string.Empty
            : Path.GetFullPath(Path.Combine(character.FolderPath, NormalizeRelativePath(relativePath)));
    }

    /// <summary>清单里一律用 '/' 分隔，这样同一份角色目录在不同机器上拷来拷去也不会变形。</summary>
    public static string NormalizeRelativePath(string value)
    {
        return value.Replace('\\', '/').TrimStart('/');
    }

    /// <summary>
    /// 历史上这几个动作的目录名拼写不统一（Ondm/OnDM、Defense、Flydown……）。
    /// 规范目录不存在、而某个旧拼写目录还在时，整目录改名过来——不能新建一个空目录，
    /// 那会让用户原来的帧凭空「消失」。
    /// </summary>
    public static void MigrateLegacyActionFolderPath(
        CharacterCard character,
        SequenceFrameAction action,
        string canonicalFolderPath)
    {
        if (Directory.Exists(canonicalFolderPath)) return;
        var aliases = action.Code switch
        {
            "OnDamage" => new[] { "Ondm", "OnDM" },
            "Defence" => new[] { "Defense" },
            "FlyDown" => new[] { "Flydown" },
            "FlyStart" => new[] { "Flystart" },
            "StandUP" => new[] { "Standup" },
            _ => Array.Empty<string>()
        };
        foreach (var alias in aliases)
        {
            var legacyPath = Path.Combine(character.FolderPath, ZdMaterialFolderName, alias);
            if (!Directory.Exists(legacyPath)) continue;
            Directory.Move(legacyPath, canonicalFolderPath);
            return;
        }
    }
}
