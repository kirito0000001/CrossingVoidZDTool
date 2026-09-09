using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 角色参考图的导入、改名、删除与列举。参考图放在 <c>tool/ReferenceImages/</c> 下面，
/// 只给人看，不参与任何导出或同步——所以这一摊完全可以独立于工作区存在。
///
/// 从 <see cref="CharacterWorkspaceService"/> 里搬出来的。它跟工作区唯一的牵连是
/// 「改完要把角色的最后编辑时间顶一下」，为此曾经和角色卡加载、目录搬迁一起挤在一个类里。
///
/// 「支持哪些图片格式」这份清单也归到这里：除了参考图，角色卡的封面（AssetMaterial/MorphPortrait）
/// 也按同一套后缀挑图，以前是两处各判一次，现在统一走 <see cref="IsSupportedReferenceImage"/>。
/// </summary>
internal sealed class CharacterReferenceImageService
{
    private static readonly string[] SupportedReferenceExtensions =
    [
        ".png",
        ".jpg",
        ".jpeg",
        ".webp",
        ".bmp"
    ];

    public IReadOnlyList<CharacterReferenceImage> LoadReferenceImages(CharacterCard character, CancellationToken cancellationToken = default)
    {
        CharacterWorkspaceService.EnsureCharacterLayout(character.FolderPath);
        return Directory
            .EnumerateFiles(character.ReferenceFolderPath)
            .Where(IsSupportedReferenceImage)
            .Select(path =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var info = new FileInfo(path);
                return new CharacterReferenceImage(info.Name, info.FullName, new Uri(info.FullName).AbsoluteUri, info.LastWriteTime);
            })
            .OrderByDescending(image => image.UpdatedAt)
            .ThenBy(image => image.FileName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 导入。不认识的后缀直接跳过而不是报错——用户经常一次框选一整个文件夹，
    /// 为了里面混着的 psd 中断整批导入没有意义。
    /// </summary>
    public IReadOnlyList<CharacterReferenceImage> ImportReferenceImages(CharacterCard character, IReadOnlyList<string> sourceFilePaths)
    {
        CharacterWorkspaceService.EnsureCharacterLayout(character.FolderPath);
        foreach (var sourceFilePath in sourceFilePaths.Where(IsSupportedReferenceImage))
        {
            var targetPath = CreateUniqueTargetPath(character.ReferenceFolderPath, Path.GetFileName(sourceFilePath));
            File.Copy(sourceFilePath, targetPath);
        }

        CharacterWorkspaceService.TouchMetadata(character.FolderPath);
        return LoadReferenceImages(character);
    }

    public IReadOnlyList<CharacterReferenceImage> RenameReferenceImage(CharacterCard character, string imagePath, string newFileName)
    {
        CharacterWorkspaceService.EnsureCharacterLayout(character.FolderPath);
        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException("没有找到参考图。", imagePath);
        }

        var cleanName = Path.GetFileName(newFileName.Trim());
        if (string.IsNullOrWhiteSpace(cleanName))
        {
            throw new InvalidOperationException("文件名不能为空。");
        }

        // 用户多半只想改名字不想改格式，所以不写后缀就沿用原来的；
        // 写了却是不认识的后缀才是真的错——改完这张图就从列表里消失了。
        var extension = Path.GetExtension(cleanName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            cleanName += Path.GetExtension(imagePath);
        }
        else if (!IsSupportedReferenceImage(cleanName))
        {
            throw new InvalidOperationException("只支持 png、jpg、jpeg、webp、bmp 图片。");
        }

        var targetPath = Path.Combine(character.ReferenceFolderPath, cleanName);
        if (File.Exists(targetPath) && !string.Equals(Path.GetFullPath(targetPath), Path.GetFullPath(imagePath), StringComparison.OrdinalIgnoreCase))
        {
            targetPath = CreateUniqueTargetPath(character.ReferenceFolderPath, cleanName);
        }

        File.Move(imagePath, targetPath, true);
        CharacterWorkspaceService.TouchMetadata(character.FolderPath);
        return LoadReferenceImages(character);
    }

    public IReadOnlyList<CharacterReferenceImage> DeleteReferenceImage(CharacterCard character, string imagePath)
    {
        CharacterWorkspaceService.EnsureCharacterLayout(character.FolderPath);
        if (File.Exists(imagePath))
        {
            File.Delete(imagePath);
        }

        CharacterWorkspaceService.TouchMetadata(character.FolderPath);
        return LoadReferenceImages(character);
    }

    /// <summary>
    /// 工具箱认得的图片后缀。角色卡挑封面时也按这份清单过滤，
    /// 所以是 internal——只此一份，别再各写一遍。
    /// </summary>
    internal static bool IsSupportedReferenceImage(string path)
    {
        return SupportedReferenceExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>重名不覆盖，改成 <c>名字_02</c> 往后排——用户拖进来的同名图往往是不同的稿子。</summary>
    private static string CreateUniqueTargetPath(string folderPath, string fileName)
    {
        var targetPath = Path.Combine(folderPath, fileName);
        if (!File.Exists(targetPath))
        {
            return targetPath;
        }

        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var index = 2;
        do
        {
            targetPath = Path.Combine(folderPath, $"{name}_{index:00}{extension}");
            index++;
        }
        while (File.Exists(targetPath));

        return targetPath;
    }
}
