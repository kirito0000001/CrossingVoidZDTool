using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;

namespace CrossingVoidZDTool.Services;

internal sealed class BaseMaterialService
{
    private const string AssetMaterialFolderName = "AssetMaterial";
    private static readonly string[] SupportedImageExtensions =
    [
        ".png",
        ".jpg",
        ".jpeg",
        ".webp",
        ".bmp"
    ];

    public static readonly BaseMaterialSpec[] Specs =
    [
        new(BaseMaterialKind.ItemIcon, "图标-道具", "ItemIcon", "ItemIcon", 196, 196, 1, true),
        new(BaseMaterialKind.Icon, "头像", "Icon", "Icon", 200, 209, 2),
        new(BaseMaterialKind.BattleAvatar, "对局内头像", "BattleAvatar", "BattleAvatar", 566, 325, 4),
        new(BaseMaterialKind.SkillIcon, "技能图标", "SkillIcon", "SkillIcon", 200, 235),
        new(BaseMaterialKind.BuffIcon, "BUFF图标", "BuffIcon", "BuffIcon", 125, 125),
        new(BaseMaterialKind.MorphPortrait, "幻形立绘", "MorphPortrait", "MorphPortrait", 558, 1200, 2),
        new(BaseMaterialKind.FullMorphPortrait, "幻形完整立绘", "FullMorphPortrait", "FullMorphPortrait", 1200, 1023, 2),
        new(BaseMaterialKind.Background, "背景图", "Background", "Background", 1920, 1080, 1, true),
        new(BaseMaterialKind.SupportCutIn, "护援特写", "SupportCutIn", "SupportCutIn", 382, 80),
        new(BaseMaterialKind.OtherImage, "其他图片", "OtherImage", "OtherImage", 0, 0, 0)
    ];

    private static readonly Dictionary<BaseMaterialKind, string[]> LegacyFileSuffixes = new()
    {
        [BaseMaterialKind.MorphPortrait] = ["Morph"],
        [BaseMaterialKind.FullMorphPortrait] = ["FullMorph"]
    };

    public IReadOnlyList<BaseMaterialSection> LoadSections(CharacterCard character, CancellationToken cancellationToken = default)
    {
        EnsureFolders(character);
        return Specs.Select(spec => LoadSection(character, spec, cancellationToken)).ToList();
    }

    public BaseMaterialItem ImportAndCrop(CharacterCard character, BaseMaterialKind kind, string sourceFilePath)
    {
        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException("没有找到源图片。", sourceFilePath);
        }

        if (!IsSupportedImage(sourceFilePath))
        {
            throw new InvalidOperationException("只支持 png、jpg、jpeg、webp、bmp 图片。");
        }

        var spec = GetSpec(kind);
        var folderPath = GetMaterialFolderPath(character, spec);
        Directory.CreateDirectory(folderPath);
        var index = GetNextIndex(folderPath, character.Code, spec);
        var targetPath = Path.Combine(folderPath, BuildFileName(character.Code, spec, index));
        if (spec.HasFixedSize)
        {
            CropCenterToPng(sourceFilePath, targetPath, spec.Width, spec.Height);
        }
        else
        {
            File.Copy(sourceFilePath, targetPath, true);
        }

        return CreateItem(spec, targetPath, index);
    }

    public BaseMaterialItem ImportWithCrop(CharacterCard character, BaseMaterialKind kind, string sourceFilePath, Rectangle crop)
    {
        var spec = GetSpec(kind);
        var folderPath = GetMaterialFolderPath(character, spec);
        Directory.CreateDirectory(folderPath);
        var index = GetNextIndex(folderPath, character.Code, spec);
        var targetPath = Path.Combine(folderPath, BuildFileName(character.Code, spec, index));
        SaveCropToPng(sourceFilePath, targetPath, crop, spec.Width, spec.Height);
        return CreateItem(spec, targetPath, index);
    }

    public BaseMaterialItem Repair(CharacterCard character, BaseMaterialKind kind, string sourceFilePath, int index)
    {
        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException("没有找到源图片。", sourceFilePath);
        }

        var spec = GetSpec(kind);
        var folderPath = GetMaterialFolderPath(character, spec);
        Directory.CreateDirectory(folderPath);
        var targetPath = Path.Combine(folderPath, BuildFileName(character.Code, spec, index));
        if (spec.HasFixedSize)
        {
            CropCenterToPng(sourceFilePath, targetPath, spec.Width, spec.Height);
        }
        else
        {
            File.Copy(sourceFilePath, targetPath, true);
        }

        return CreateItem(spec, targetPath, index);
    }

    public BaseMaterialItem RepairWithCrop(CharacterCard character, BaseMaterialKind kind, string sourceFilePath, int index, Rectangle crop)
    {
        var spec = GetSpec(kind);
        var folderPath = GetMaterialFolderPath(character, spec);
        Directory.CreateDirectory(folderPath);
        var targetPath = Path.Combine(folderPath, BuildFileName(character.Code, spec, index));
        SaveCropToPng(sourceFilePath, targetPath, crop, spec.Width, spec.Height);
        return CreateItem(spec, targetPath, index);
    }

    public int ReplaceWithImages(CharacterCard character, BaseMaterialKind kind, IReadOnlyList<string> sourceFilePaths)
    {
        var validSourcePaths = sourceFilePaths
            .Where(path => File.Exists(path) && IsSupportedImage(path))
            .ToArray();
        if (validSourcePaths.Length == 0)
        {
            return 0;
        }

        var spec = GetSpec(kind);
        var folderPath = GetMaterialFolderPath(character, spec);
        Directory.CreateDirectory(folderPath);
        foreach (var filePath in Directory.EnumerateFiles(folderPath).Where(IsSupportedImage).ToList())
        {
            TryDeleteFile(filePath);
        }

        var importedCount = 0;
        foreach (var sourceFilePath in validSourcePaths)
        {
            ImportAndCrop(character, kind, sourceFilePath);
            importedCount++;
        }

        return importedCount;
    }

    private static bool TryDeleteFile(string filePath)
    {
        try
        {
            File.Delete(filePath);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public string GetMaterialFolderPath(CharacterCard character, BaseMaterialKind kind)
    {
        return GetMaterialFolderPath(character, GetSpec(kind));
    }

    public static BaseMaterialSpec GetSpec(BaseMaterialKind kind)
    {
        return Specs.First(spec => spec.Kind == kind);
    }

    public static (int Width, int Height) GetImageSize(string path)
    {
        using var image = Image.FromFile(path);
        return (image.Width, image.Height);
    }

    private static BaseMaterialSection LoadSection(CharacterCard character, BaseMaterialSpec spec, CancellationToken cancellationToken)
    {
        var folderPath = GetMaterialFolderPath(character, spec);
        Directory.CreateDirectory(folderPath);
        var items = Directory
            .EnumerateFiles(folderPath)
            .Where(IsSupportedImage)
            .Select(path =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return CreateItem(spec, path, ResolveIndex(character.Code, spec, path));
            })
            .OrderBy(item => item.Index)
            .ThenBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (items.Count == 0)
        {
            return new BaseMaterialSection(spec, items, true, $"未设置，至少需要 {spec.MinimumCount} 张");
        }

        var invalidCount = items.Count(item => item.Status == BaseMaterialStatus.Invalid);
        var missingCount = Math.Max(0, spec.MinimumCount - items.Count);
        var statusText = missingCount > 0
            ? $"还差 {missingCount} 张，已设置 {items.Count} 个"
            : invalidCount > 0
            ? $"{invalidCount} 个素材不合规"
            : $"已设置 {items.Count} 个";
        return new BaseMaterialSection(spec, items, missingCount > 0 || invalidCount > 0, statusText);
    }

    private static BaseMaterialItem CreateItem(BaseMaterialSpec spec, string path, int index)
    {
        var actualWidth = 0;
        var actualHeight = 0;
        var status = BaseMaterialStatus.Invalid;
        var statusText = "无法读取图片";
        try
        {
            using var image = Image.FromFile(path);
            actualWidth = image.Width;
            actualHeight = image.Height;
            status = !spec.HasFixedSize || actualWidth == spec.Width && actualHeight == spec.Height
                ? BaseMaterialStatus.Ready
                : BaseMaterialStatus.Invalid;
            statusText = spec.HasFixedSize
                ? status == BaseMaterialStatus.Ready
                    ? "合规"
                    : $"尺寸 {actualWidth}x{actualHeight}，需要 {spec.Width}x{spec.Height}"
                : $"已设置，尺寸 {actualWidth}x{actualHeight}";
        }
        catch
        {
            statusText = "图片损坏或格式不支持";
        }

        var info = new FileInfo(path);
        var version = Math.Max(info.LastWriteTimeUtc.Ticks, info.Length);
        return new BaseMaterialItem(
            spec.Kind,
            spec.DisplayName,
            info.FullName,
            $"{new Uri(info.FullName).AbsoluteUri}?v={version}",
            info.Name,
            Math.Max(1, index),
            spec.Width,
            spec.Height,
            actualWidth,
            actualHeight,
            status,
            statusText,
            info.LastWriteTime);
    }

    private static void EnsureFolders(CharacterCard character)
    {
        Directory.CreateDirectory(Path.Combine(character.FolderPath, AssetMaterialFolderName));
        foreach (var spec in Specs)
        {
            Directory.CreateDirectory(GetMaterialFolderPath(character, spec));
        }
    }

    private static string GetMaterialFolderPath(CharacterCard character, BaseMaterialSpec spec)
    {
        return Path.Combine(character.FolderPath, AssetMaterialFolderName, spec.FolderName);
    }

    private static int GetNextIndex(string folderPath, string characterCode, BaseMaterialSpec spec)
    {
        if (spec.IsSingle)
        {
            return 1;
        }

        var indexes = Directory
            .EnumerateFiles(folderPath)
            .Where(IsSupportedImage)
            .Select(path => ResolveIndex(characterCode, spec, path))
            .Where(index => index > 0)
            .DefaultIfEmpty(0);
        return indexes.Max() + 1;
    }

    private static int ResolveIndex(string characterCode, BaseMaterialSpec spec, string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var matchedSuffix = ResolveMatchedFileSuffix(characterCode, spec, name);
        if (matchedSuffix is null)
        {
            return 1;
        }

        var prefix = $"{characterCode}-{matchedSuffix}";
        var indexText = name[prefix.Length..].TrimStart('-');
        return int.TryParse(indexText, out var index)
            ? Math.Max(1, index)
            : 1;
    }

    private static string? ResolveMatchedFileSuffix(string characterCode, BaseMaterialSpec spec, string fileNameWithoutExtension)
    {
        var suffixes = LegacyFileSuffixes.TryGetValue(spec.Kind, out var legacySuffixes)
            ? new[] { spec.FileSuffix }.Concat(legacySuffixes)
            : [spec.FileSuffix];
        foreach (var suffix in suffixes)
        {
            if (fileNameWithoutExtension.StartsWith($"{characterCode}-{suffix}", StringComparison.OrdinalIgnoreCase))
            {
                return suffix;
            }
        }

        return null;
    }

    private static string BuildFileName(string characterCode, BaseMaterialSpec spec, int index)
    {
        var suffix = spec.IsSingle ? string.Empty : $"-{index:00}";
        return $"{characterCode}-{spec.FileSuffix}{suffix}.png";
    }

    private static void CropCenterToPng(string sourcePath, string targetPath, int targetWidth, int targetHeight)
    {
        using var source = Image.FromFile(sourcePath);
        var targetRatio = targetWidth / (double)targetHeight;
        var sourceRatio = source.Width / (double)source.Height;

        Rectangle crop;
        if (sourceRatio > targetRatio)
        {
            var cropWidth = (int)Math.Round(source.Height * targetRatio);
            crop = new Rectangle((source.Width - cropWidth) / 2, 0, cropWidth, source.Height);
        }
        else
        {
            var cropHeight = (int)Math.Round(source.Width / targetRatio);
            crop = new Rectangle(0, (source.Height - cropHeight) / 2, source.Width, cropHeight);
        }

        SaveCropToPng(sourcePath, targetPath, crop, targetWidth, targetHeight);
    }

    public static void SaveCropToPng(string sourcePath, string targetPath, Rectangle crop, int targetWidth, int targetHeight)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var tempPath = Path.Combine(
            Path.GetDirectoryName(targetPath)!,
            $".{Path.GetFileNameWithoutExtension(targetPath)}.{Guid.NewGuid():N}.tmp.png");

        try
        {
            using (var source = Image.FromFile(sourcePath))
            {
                crop = Rectangle.Intersect(crop, new Rectangle(0, 0, source.Width, source.Height));
                if (crop.Width <= 0 || crop.Height <= 0)
                {
                    throw new InvalidOperationException("裁剪范围无效。");
                }

                using var output = new Bitmap(targetWidth, targetHeight);
                using var graphics = Graphics.FromImage(output);
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.DrawImage(source, new Rectangle(0, 0, targetWidth, targetHeight), crop, GraphicsUnit.Pixel);
                output.Save(tempPath, ImageFormat.Png);
            }

            File.Copy(tempPath, targetPath, true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static bool IsSupportedImage(string path)
    {
        return SupportedImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }
}
