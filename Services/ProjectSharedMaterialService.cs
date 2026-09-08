using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace CrossingVoidZDTool.Services;

internal sealed class ProjectSharedMaterialService
{
    private const string SharedFolderName = "Shared";
    private const string IndexRelativePath = "tool/shared-materials.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly IReadOnlyDictionary<ProjectSharedMaterialCategory, string> CategoryFolders =
        new Dictionary<ProjectSharedMaterialCategory, string>
        {
            [ProjectSharedMaterialCategory.BuffIcons] = "Images/BuffIcons",
            [ProjectSharedMaterialCategory.EventImages] = "Images/EventImages",
            [ProjectSharedMaterialCategory.BattleUiImages] = "Images/BattleUI",
            [ProjectSharedMaterialCategory.UnclassifiedImages] = "Images/Unclassified",
            [ProjectSharedMaterialCategory.BattleEffects] = "Audio/BattleEffects",
            [ProjectSharedMaterialCategory.BattleMusic] = "Audio/BattleMusic",
            [ProjectSharedMaterialCategory.UiEffects] = "Audio/UIEffects",
            [ProjectSharedMaterialCategory.UnclassifiedAudio] = "Audio/Unclassified"
        };

    public ProjectSharedMaterialIndex Load(string projectRootPath)
    {
        var root = GetSharedRoot(projectRootPath);
        var indexPath = Path.Combine(root, IndexRelativePath.Replace('/', Path.DirectorySeparatorChar));
        ProjectSharedMaterialIndex index;
        if (!File.Exists(indexPath))
        {
            index = new ProjectSharedMaterialIndex();
        }
        else
        {
            index = JsonSerializer.Deserialize<ProjectSharedMaterialIndex>(File.ReadAllText(indexPath), JsonOptions)
                ?? new ProjectSharedMaterialIndex();
        }

        foreach (var item in index.Items)
        {
            item.RelativePath = NormalizeRelativePath(item.RelativePath);
            item.FilePath = string.IsNullOrWhiteSpace(item.RelativePath)
                ? string.Empty
                : Path.GetFullPath(Path.Combine(root, item.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            item.SourceObjectPaths = item.SourceObjectPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            item.Usages = item.Usages.Distinct().ToList();
        }

        return index;
    }

    public ProjectSharedMaterialItem Import(
        string projectRootPath,
        ProjectSharedMaterialCategory category,
        string sourceFilePath,
        string sourceObjectPath,
        ProjectSharedMaterialUsage usage)
    {
        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException("共享素材源文件不存在。", sourceFilePath);
        }

        var root = GetSharedRoot(projectRootPath);
        var index = Load(projectRootPath);
        var fullSourcePath = Path.GetFullPath(sourceFilePath);
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fullSourcePath)));
        var normalizedObjectPath = NormalizeObjectPath(sourceObjectPath);
        var item = index.Items.FirstOrDefault(value =>
                value.SourceObjectPaths.Contains(normalizedObjectPath, StringComparer.OrdinalIgnoreCase))
            ?? index.Items.FirstOrDefault(value =>
                value.Category == category && string.Equals(value.ContentHash, hash, StringComparison.OrdinalIgnoreCase));

        if (item is null || item.IsReferenceOnly)
        {
            var relativeFolder = CategoryFolders[category];
            var fileName = SanitizeFileName(Path.GetFileName(fullSourcePath));
            var relativePath = NormalizeRelativePath(Path.Combine(relativeFolder, fileName));
            var targetPath = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (File.Exists(targetPath))
            {
                var baseName = Path.GetFileNameWithoutExtension(fileName);
                var extension = Path.GetExtension(fileName);
                relativePath = NormalizeRelativePath(Path.Combine(relativeFolder, $"{baseName}-{hash[..8]}{extension}"));
                targetPath = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            }

            if (item is null)
            {
                item = new ProjectSharedMaterialItem
                {
                    Id = $"shared:{hash.ToLowerInvariant()}"
                };
                index.Items.Add(item);
            }

            item.Category = category;
            item.DisplayName = Path.GetFileNameWithoutExtension(fullSourcePath);
            item.AssetClass = Path.GetExtension(fullSourcePath).Equals(".wav", StringComparison.OrdinalIgnoreCase)
                ? "SoundWave"
                : item.AssetClass;
            item.RelativePath = relativePath;
            item.ContentHash = hash;
            item.FilePath = targetPath;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(item.FilePath)!);
        if (!string.Equals(fullSourcePath, item.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(fullSourcePath, item.FilePath, overwrite: true);
        }

        item.Category = category;
        item.ContentHash = hash;
        if (!string.IsNullOrWhiteSpace(normalizedObjectPath) &&
            !item.SourceObjectPaths.Contains(normalizedObjectPath, StringComparer.OrdinalIgnoreCase))
        {
            item.SourceObjectPaths.Add(normalizedObjectPath);
        }
        if (!item.Usages.Contains(usage))
        {
            item.Usages.Add(usage);
        }

        Save(projectRootPath, index);
        return item;
    }

    public ProjectSharedMaterialItem RegisterReference(
        string projectRootPath,
        ProjectSharedMaterialCategory category,
        string displayName,
        string assetClass,
        string sourceObjectPath,
        ProjectSharedMaterialUsage usage)
    {
        var index = Load(projectRootPath);
        var normalizedObjectPath = NormalizeObjectPath(sourceObjectPath);
        var item = index.Items.FirstOrDefault(value =>
            value.SourceObjectPaths.Contains(normalizedObjectPath, StringComparer.OrdinalIgnoreCase));
        if (item is null)
        {
            var identityHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalizedObjectPath)));
            item = new ProjectSharedMaterialItem
            {
                Id = $"shared-ref:{identityHash.ToLowerInvariant()}",
                Category = category,
                DisplayName = displayName,
                AssetClass = assetClass
            };
            index.Items.Add(item);
        }

        item.Category = category;
        item.DisplayName = string.IsNullOrWhiteSpace(displayName) ? item.DisplayName : displayName;
        item.AssetClass = string.IsNullOrWhiteSpace(assetClass) ? item.AssetClass : assetClass;
        if (!string.IsNullOrWhiteSpace(normalizedObjectPath) &&
            !item.SourceObjectPaths.Contains(normalizedObjectPath, StringComparer.OrdinalIgnoreCase))
        {
            item.SourceObjectPaths.Add(normalizedObjectPath);
        }
        if (!item.Usages.Contains(usage))
        {
            item.Usages.Add(usage);
        }

        Save(projectRootPath, index);
        return item;
    }

    private static void Save(string projectRootPath, ProjectSharedMaterialIndex index)
    {
        var root = GetSharedRoot(projectRootPath);
        var indexPath = Path.Combine(root, IndexRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
        AtomicFileWriter.WriteAllText(indexPath, JsonSerializer.Serialize(index, JsonOptions));
    }

    private static string GetSharedRoot(string projectRootPath)
    {
        return Path.Combine(Path.GetFullPath(projectRootPath), SharedFolderName);
    }

    private static string NormalizeRelativePath(string value)
    {
        return (value ?? string.Empty).Replace('\\', '/').TrimStart('/');
    }

    private static string NormalizeObjectPath(string value)
    {
        return (value ?? string.Empty).Trim().Replace('\\', '/');
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "SharedMaterial" : sanitized;
    }
}
