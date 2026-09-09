using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 动作目录下 <c>Frames/</c> 帧池的文件操作：把外部图片收进池子、清空动作目录、
/// 清理没人引用的冗余帧，以及把还没有清单的老目录就地升级成帧池 + 清单。
///
/// 从 <see cref="SequenceFrameService"/> 里搬出来的。帧池是唯一会<b>删用户文件</b>的地方，
/// 之前它和几十个纯改清单的操作混在一起，谁在什么前提下删了什么很难一眼看清。
///
/// 池子里的文件按内容哈希命名，所以同一张图被多个帧格复用时只存一份；
/// 也正因为如此，删文件必须以「全角色的清单引用集」为准，不能只看当前这个动作
/// ——见 <see cref="PruneUnreferenced"/>。
/// </summary>
internal static class SequenceFramePool
{
    private static readonly string[] SupportedImageExtensions =
    [
        ".png",
        ".jpg",
        ".jpeg",
        ".webp",
        ".bmp"
    ];

    public static bool IsSupportedImage(string path)
    {
        return SupportedImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 把一张外部图片收进这个动作的帧池，返回可以写进清单的相对路径。
    ///
    /// 如果这张图本来就是该角色某个动作清单引用着的帧，就直接指过去，不再复制一份——
    /// 「复用同一张帧」是这套工具的常规做法，每次都拷贝会让素材目录成倍膨胀。
    /// </summary>
    public static string ImportSource(CharacterCard character, SequenceFrameAction action, string sourcePath)
    {
        var sourceFullPath = Path.GetFullPath(sourcePath);
        var referencedCharacterFrame = SequenceManifestStore.EnumerateReferencedFramePaths(character)
            .FirstOrDefault(path => string.Equals(
                Path.GetFullPath(path),
                sourceFullPath,
                StringComparison.OrdinalIgnoreCase));
        if (referencedCharacterFrame is not null)
        {
            return SequenceActionFolderLayout.NormalizeRelativePath(Path.GetRelativePath(
                SequenceActionFolderLayout.GetActionFolderPath(character, action),
                sourceFullPath));
        }

        var framesFolderPath = SequenceActionFolderLayout.GetFramesFolderPath(character, action);
        Directory.CreateDirectory(framesFolderPath);
        var hash = ComputeFileHash(sourceFullPath);
        var fileName = $"{character.Code}-{action.Code}-{hash[..16]}.png";
        var targetPath = Path.Combine(framesFolderPath, fileName);
        if (!File.Exists(targetPath))
        {
            SaveAsPng(sourceFullPath, targetPath);
        }

        return SequenceActionFolderLayout.NormalizeRelativePath(Path.Combine(
            SequenceActionFolderLayout.FramesFolderName,
            fileName));
    }

    /// <summary>已经在动作目录里的文件按原样引用，否则收进帧池。</summary>
    public static string ToManifestRelativePath(
        CharacterCard character,
        SequenceFrameAction action,
        string filePath)
    {
        var actionFolderPath = Path.GetFullPath(SequenceActionFolderLayout.GetActionFolderPath(character, action));
        var fullPath = Path.GetFullPath(filePath);
        if (fullPath.StartsWith(actionFolderPath, StringComparison.OrdinalIgnoreCase))
        {
            return SequenceActionFolderLayout.NormalizeRelativePath(
                Path.GetRelativePath(actionFolderPath, fullPath));
        }

        return ImportSource(character, action, filePath);
    }

    /// <summary>清空一个动作目录：顶层文件全删，帧池里只删图片（清单由调用方随后重写）。</summary>
    public static void ClearActionFolder(string folderPath)
    {
        foreach (var path in Directory.EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly))
        {
            File.Delete(path);
        }

        var framesFolderPath = Path.Combine(folderPath, SequenceActionFolderLayout.FramesFolderName);
        if (Directory.Exists(framesFolderPath))
        {
            foreach (var path in Directory.EnumerateFiles(framesFolderPath).Where(IsSupportedImage))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>删掉这个动作帧池里没有任何清单引用的图片。</summary>
    public static void PruneUnreferenced(CharacterCard character, SequenceFrameAction action)
    {
        // 引用集必须是完整的。以前某个动作的 sequence.json 读不出来时（杀软刚扫完、
        // 文件被占用、上游非原子写留下的坏文件）会被当作「这个动作零引用」，
        // 于是它名下的帧图全都不在引用集里，紧接着被这里删光——用户的原始帧
        // 就这么没了，界面上还没有任何提示。宁可这一轮不清理，也不能删错。
        if (!SequenceManifestStore.TryEnumerateReferencedFramePaths(character, out var referenced))
        {
            ToolboxLog.Warn(
                $"有动作的序列清单读不出来，本次跳过 {action.Code} 的冗余帧清理，避免误删。");
            return;
        }

        var framesFolderPath = SequenceActionFolderLayout.GetFramesFolderPath(character, action);
        if (!Directory.Exists(framesFolderPath))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(framesFolderPath).Where(IsSupportedImage))
        {
            if (!referenced.Contains(Path.GetFullPath(file)))
            {
                File.Delete(file);
            }
        }
    }

    /// <summary>
    /// 还没有清单的老动作目录：把散在顶层的图片按文件名里的序号排好收进帧池，
    /// 生成一份清单，再删掉顶层的原件。
    /// </summary>
    public static SequenceFrameManifest MigrateLegacyActionFolder(
        CharacterCard character,
        SequenceFrameAction action)
    {
        var folderPath = SequenceActionFolderLayout.GetActionFolderPath(character, action);
        var legacyPaths = Directory
            .EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly)
            .Where(IsSupportedImage)
            .Select(path => new
            {
                Path = path,
                Index = ResolveIndex(Path.GetFileName(path))
            })
            .OrderBy(item => item.Index)
            .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Path)
            .ToList();
        var manifest = SequenceManifestStore.Create(action);
        foreach (var path in legacyPaths)
        {
            manifest.Frames.Add(new SequenceFrameManifestEntry
            {
                RelativePath = ImportSource(character, action, path)
            });
        }

        foreach (var path in legacyPaths.Where(File.Exists))
        {
            File.Delete(path);
        }

        SequenceManifestStore.Save(character, action, manifest);
        return manifest;
    }

    /// <summary>帧池里一律存 PNG：webp/bmp 这些格式在预览控件和引擎侧的支持不一致。</summary>
    private static void SaveAsPng(string sourcePath, string targetPath)
    {
        using var source = Image.FromFile(sourcePath);
        using var output = new Bitmap(source.Width, source.Height);
        using (var graphics = Graphics.FromImage(output))
        {
            graphics.DrawImage(source, 0, 0, source.Width, source.Height);
        }

        output.Save(targetPath, ImageFormat.Png);
    }

    private static string ComputeFileHash(string sourcePath)
    {
        using var stream = File.OpenRead(sourcePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>老命名里的序号在最后一段（<c>xxx-12.png</c> / <c>xxx_12.png</c>），认不出来算 0。</summary>
    private static int ResolveIndex(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var tail = name.Split('-', '_').LastOrDefault();
        return int.TryParse(tail, out var index) ? index : 0;
    }
}
