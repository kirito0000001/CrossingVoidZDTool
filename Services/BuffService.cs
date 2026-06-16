using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace CrossingVoidZDTool.Services;

internal sealed class BuffService
{
    private const string BuffFolderName = "BUFF";
    private const string BuffFileName = "buff.json";
    private const string ToolFolderName = "tool";
    private const string LegacyToolboxDataFileName = "ZDToolboxData.json";
    private const string LegacyBackupsFolderName = "legacy-backups";
    private const int IconSize = 125;
    private const int MaxBackupCount = 60;
    private const string DefaultBuffIconRelativePath = "Assets\\DefaultBuffIcon.png";
    private readonly CharacterToolboxDataService _toolboxDataService = new();

    private static readonly string[] SupportedImageExtensions =
    [
        ".png",
        ".jpg",
        ".jpeg",
        ".webp",
        ".bmp"
    ];

    public BuffData Load(CharacterCard character)
    {
        using var notifications = BuffEntry.SuppressEditNotifications();

        Directory.CreateDirectory(GetBuffRootPath(character));
        MigrateLegacyBuffsIfNeeded(character);
        var data = new BuffData();
        foreach (var buffFilePath in EnumerateBuffFilePaths(character))
        {
            var fileData = ReadBuffFile(buffFilePath);
            if (fileData?.Buff is null)
            {
                continue;
            }

            data.Buffs.Add(fileData.Buff);
            if (fileData.UpdatedAt > data.UpdatedAt)
            {
                data.UpdatedAt = fileData.UpdatedAt;
            }
        }

        return NormalizeCore(character, data, syncFolders: false, resolvePaths: true);
    }

    public void Save(CharacterCard character, BuffData data)
    {
        using var notifications = BuffEntry.SuppressEditNotifications();

        Directory.CreateDirectory(GetBuffRootPath(character));
        var normalized = NormalizeCore(character, Clone(data), syncFolders: true, resolvePaths: true);
        normalized.UpdatedAt = DateTime.Now;
        var activeCodes = normalized.Buffs
            .Select(buff => buff.GeneratedCode)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var buff in normalized.Buffs)
        {
            WriteBuffFile(character, buff, normalized.UpdatedAt);
        }

        DeleteStaleBuffFolders(character, activeCodes);
    }

    public void ImportIcon(CharacterCard character, BuffEntry buff, string sourcePath)
    {
        var targetPath = ImportIconFile(character, buff.GeneratedCode, sourcePath);
        buff.IconPath = targetPath;
        buff.IconUri = new Uri(targetPath).AbsoluteUri;
    }

    public string ImportIconFile(CharacterCard character, string generatedCode, string sourcePath)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("没有找到源图片。", sourcePath);
        }

        if (!IsSupportedImage(sourcePath))
        {
            throw new InvalidOperationException("只支持 png、jpg、jpeg、webp、bmp 图片。");
        }

        var safeGeneratedCode = string.IsNullOrWhiteSpace(generatedCode)
            ? $"{character.Code}_BUFF"
            : generatedCode;
        var folderPath = Path.Combine(GetBuffRootPath(character), safeGeneratedCode);
        Directory.CreateDirectory(folderPath);
        var targetPath = Path.Combine(folderPath, $"{safeGeneratedCode}-Icon.png");
        CropCenterToPng(sourcePath, targetPath, IconSize, IconSize);
        return targetPath;
    }

    public bool EnsureDefaultIcon(CharacterCard character, BuffEntry buff)
    {
        var targetPath = EnsureDefaultIconFile(character, buff.GeneratedCode, buff.IconPath);
        if (targetPath is null)
        {
            RefreshIconUri(character, buff);
            return false;
        }

        buff.IconPath = targetPath;
        buff.IconUri = new Uri(targetPath).AbsoluteUri;
        return true;
    }

    public string? EnsureDefaultIconFile(CharacterCard character, string generatedCode, string currentIconPath)
    {
        var resolvedCurrentIconPath = ResolveCharacterPath(character, currentIconPath);
        if (!string.IsNullOrWhiteSpace(resolvedCurrentIconPath) && File.Exists(resolvedCurrentIconPath))
        {
            return null;
        }

        var defaultBuffIconPath = GetDefaultBuffIconPath();
        if (!File.Exists(defaultBuffIconPath))
        {
            return null;
        }

        var safeGeneratedCode = string.IsNullOrWhiteSpace(generatedCode)
            ? $"{character.Code}_BUFF"
            : generatedCode;
        var folderPath = Path.Combine(GetBuffRootPath(character), safeGeneratedCode);
        Directory.CreateDirectory(folderPath);
        var targetPath = Path.Combine(folderPath, $"{safeGeneratedCode}-Icon.png");
        CropCenterToPng(defaultBuffIconPath, targetPath, IconSize, IconSize);
        return targetPath;
    }

    public bool DeleteBuffFolder(CharacterCard character, BuffEntry buff)
    {
        if (string.IsNullOrWhiteSpace(buff.GeneratedCode))
        {
            return false;
        }

        var folderPath = Path.Combine(GetBuffRootPath(character), buff.GeneratedCode);
        if (!Directory.Exists(folderPath))
        {
            return false;
        }

        Directory.Delete(folderPath, recursive: true);
        return true;
    }

    private static string GetDefaultBuffIconPath()
    {
        return Path.Combine(AppContext.BaseDirectory, DefaultBuffIconRelativePath);
    }

    public string GetBuffRootPath(CharacterCard character)
    {
        return Path.Combine(character.FolderPath, BuffFolderName);
    }

    public static BuffEntry CreateBuff()
    {
        return new BuffEntry
        {
            GainType = "增益",
            DamageType = "发送方-属性",
            TaskPriority = "正常",
            Stacks = "0",
            CompleteStacks = "3",
            Strength = "1",
            CompleteStrength = "10",
            ReadStatus = "手动创建"
        };
    }

    public static void RefreshNaming(CharacterCard character, IEnumerable<BuffEntry> buffs)
    {
        using var notifications = BuffEntry.SuppressEditNotifications();
        RefreshNamingCore(character, buffs, syncFolders: true, resolvePaths: true);
    }

    private static void RefreshNamingCore(
        CharacterCard character,
        IEnumerable<BuffEntry> buffs,
        bool syncFolders = false,
        bool resolvePaths = false)
    {
        var index = 1;
        foreach (var buff in buffs)
        {
            var previousGeneratedCode = buff.GeneratedCode;
            buff.Index = index;
            var suffix = SanitizeCode(buff.UserCode);
            buff.GeneratedCode = string.IsNullOrWhiteSpace(suffix)
                ? $"{character.Code}_BUFF-{index}"
                : $"{character.Code}_BUFF-{index}-{suffix}";
            if (syncFolders &&
                !string.IsNullOrWhiteSpace(previousGeneratedCode) &&
                !string.Equals(previousGeneratedCode, buff.GeneratedCode, StringComparison.OrdinalIgnoreCase))
            {
                SyncBuffFolderName(character, buff, previousGeneratedCode);
            }

            if (resolvePaths)
            {
                buff.IconPath = ResolveCharacterPath(character, buff.IconPath);
            }

            RefreshIconUri(character, buff);
            index++;
        }
    }

    private static void SyncBuffFolderName(CharacterCard character, BuffEntry buff, string previousGeneratedCode)
    {
        var rootPath = Path.Combine(character.FolderPath, BuffFolderName);
        var previousFolderPath = Path.Combine(rootPath, previousGeneratedCode);
        var nextFolderPath = Path.Combine(rootPath, buff.GeneratedCode);
        if (Directory.Exists(previousFolderPath) &&
            !string.Equals(Path.GetFullPath(previousFolderPath), Path.GetFullPath(nextFolderPath), StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(rootPath);
            if (Directory.Exists(nextFolderPath))
            {
                foreach (var filePath in Directory.EnumerateFiles(previousFolderPath))
                {
                    var targetPath = Path.Combine(nextFolderPath, Path.GetFileName(filePath));
                    File.Move(filePath, targetPath, overwrite: true);
                }

                foreach (var directoryPath in Directory.EnumerateDirectories(previousFolderPath))
                {
                    var targetPath = Path.Combine(nextFolderPath, Path.GetFileName(directoryPath));
                    if (Directory.Exists(targetPath))
                    {
                        Directory.Delete(targetPath, recursive: true);
                    }

                    Directory.Move(directoryPath, targetPath);
                }

                Directory.Delete(previousFolderPath, recursive: true);
            }
            else
            {
                Directory.Move(previousFolderPath, nextFolderPath);
            }
        }

        var previousIconFilePath = Path.Combine(nextFolderPath, $"{previousGeneratedCode}-Icon.png");
        var nextIconFilePath = Path.Combine(nextFolderPath, $"{buff.GeneratedCode}-Icon.png");
        if (File.Exists(previousIconFilePath) &&
            !string.Equals(Path.GetFullPath(previousIconFilePath), Path.GetFullPath(nextIconFilePath), StringComparison.OrdinalIgnoreCase))
        {
            File.Move(previousIconFilePath, nextIconFilePath, overwrite: true);
        }

        var previousBuffFilePath = Path.Combine(nextFolderPath, $"{previousGeneratedCode}.json");
        if (File.Exists(previousBuffFilePath))
        {
            File.Move(previousBuffFilePath, Path.Combine(nextFolderPath, BuffFileName), overwrite: true);
        }

        var resolvedIconPath = ResolveCharacterPath(character, buff.IconPath);
        if (string.IsNullOrWhiteSpace(resolvedIconPath) ||
            string.Equals(Path.GetFileName(resolvedIconPath), $"{previousGeneratedCode}-Icon.png", StringComparison.OrdinalIgnoreCase) ||
            IsPathUnderDirectory(resolvedIconPath, previousFolderPath))
        {
            buff.IconPath = File.Exists(nextIconFilePath)
                ? nextIconFilePath
                : resolvedIconPath.Replace(previousGeneratedCode, buff.GeneratedCode, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static BuffData Normalize(CharacterCard character, BuffData? data)
    {
        using var notifications = BuffEntry.SuppressEditNotifications();
        return NormalizeCore(character, data, syncFolders: false, resolvePaths: true);
    }

    public static BuffData Clone(BuffData? data)
    {
        var clone = new BuffData
        {
            UpdatedAt = data?.UpdatedAt ?? DateTime.Now
        };

        if (data?.Buffs is null)
        {
            return clone;
        }

        using var notifications = BuffEntry.SuppressEditNotifications();
        foreach (var buff in data.Buffs)
        {
            clone.Buffs.Add(Clone(buff));
        }

        return clone;
    }

    private static BuffEntry Clone(BuffEntry buff)
    {
        return new BuffEntry
        {
            Index = buff.Index,
            UserCode = buff.UserCode,
            GeneratedCode = buff.GeneratedCode,
            Name = buff.Name,
            Description = buff.Description,
            DamageType = buff.DamageType,
            IconPath = buff.IconPath,
            IconUri = buff.IconUri,
            GainType = buff.GainType,
            Stacks = buff.Stacks,
            CompleteStacks = buff.CompleteStacks,
            Strength = buff.Strength,
            CompleteStrength = buff.CompleteStrength,
            OwnerText = buff.OwnerText,
            TaskPriority = buff.TaskPriority,
            TriggerTiming = buff.TriggerTiming,
            ConditionSummary = buff.ConditionSummary,
            ReadStatus = buff.ReadStatus,
            SourceAssetPath = buff.SourceAssetPath,
            Draft = buff.Draft
        };
    }

    private static BuffData NormalizeCore(
        CharacterCard character,
        BuffData? data,
        bool syncFolders,
        bool resolvePaths)
    {
        data ??= new BuffData();
        data.Buffs ??= [];
        foreach (var buff in data.Buffs)
        {
            buff.GainType = string.IsNullOrWhiteSpace(buff.GainType) ? "增益" : buff.GainType;
            buff.DamageType = NormalizeDamageType(buff.DamageType);
            buff.TaskPriority = NormalizeTaskPriority(buff.TaskPriority);
            buff.Stacks = NormalizeIntegerText(buff.Stacks, "0");
            buff.CompleteStacks = NormalizeIntegerText(buff.CompleteStacks, "3");
            buff.Strength = NormalizeIntegerText(buff.Strength, "1");
            buff.CompleteStrength = NormalizeIntegerText(buff.CompleteStrength, "10");
            buff.ReadStatus = string.IsNullOrWhiteSpace(buff.ReadStatus) ? "工具箱数据" : buff.ReadStatus;
            if (resolvePaths)
            {
                buff.IconPath = ResolveCharacterPath(character, buff.IconPath);
            }

            RefreshIconUri(character, buff);
        }

        RefreshNamingCore(character, data.Buffs, syncFolders, resolvePaths);
        return data;
    }

    private void MigrateLegacyBuffsIfNeeded(CharacterCard character)
    {
        if (EnumerateBuffFilePaths(character).Any())
        {
            return;
        }

        var legacyPath = GetLegacyToolboxDataPath(character);
        if (!File.Exists(legacyPath))
        {
            return;
        }

        var legacy = _toolboxDataService.Load(character);
        if (legacy.Buffs?.Buffs is null || legacy.Buffs.Buffs.Count == 0)
        {
            return;
        }

        BackupLegacyToolboxData(character, legacyPath);
        Save(character, legacy.Buffs);
    }

    private static IEnumerable<string> EnumerateBuffFilePaths(CharacterCard character)
    {
        var rootPath = Path.Combine(character.FolderPath, BuffFolderName);
        if (!Directory.Exists(rootPath))
        {
            yield break;
        }

        foreach (var directoryPath in Directory.EnumerateDirectories(rootPath))
        {
            var filePath = Path.Combine(directoryPath, BuffFileName);
            if (File.Exists(filePath))
            {
                yield return filePath;
            }
        }
    }

    private static BuffFileData? ReadBuffFile(string path)
    {
        try
        {
            return JsonSerializer.Deserialize(
                File.ReadAllText(path, Encoding.UTF8),
                AppJsonSerializerContext.Default.BuffFileData);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"BUFF 数据读取失败：{path}", ex);
        }
    }

    private static void WriteBuffFile(CharacterCard character, BuffEntry buff, DateTime updatedAt)
    {
        var folderPath = Path.Combine(character.FolderPath, BuffFolderName, buff.GeneratedCode);
        Directory.CreateDirectory(folderPath);
        var filePath = Path.Combine(folderPath, BuffFileName);
        var fileBuff = Clone(buff);
        fileBuff.IconPath = ToCharacterRelativePath(character, fileBuff.IconPath);
        fileBuff.IconUri = string.Empty;
        var fileData = new BuffFileData
        {
            Buff = fileBuff,
            UpdatedAt = updatedAt
        };
        var nextText = JsonSerializer.Serialize(fileData, AppJsonSerializerContext.Default.BuffFileData);
        var currentText = File.Exists(filePath) ? File.ReadAllText(filePath, Encoding.UTF8) : string.Empty;
        if (string.Equals(currentText, nextText, StringComparison.Ordinal))
        {
            return;
        }

        CreateBackupIfNeeded(filePath, currentText);
        WriteAllTextAtomic(filePath, nextText);
        PruneBackups(folderPath);
    }

    private static void DeleteStaleBuffFolders(CharacterCard character, ISet<string> activeCodes)
    {
        var rootPath = Path.Combine(character.FolderPath, BuffFolderName);
        if (!Directory.Exists(rootPath))
        {
            return;
        }

        foreach (var directoryPath in Directory.EnumerateDirectories(rootPath))
        {
            var folderName = Path.GetFileName(directoryPath);
            if (activeCodes.Contains(folderName))
            {
                continue;
            }

            var buffFilePath = Path.Combine(directoryPath, BuffFileName);
            if (File.Exists(buffFilePath))
            {
                Directory.Delete(directoryPath, recursive: true);
            }
        }
    }

    private static void BackupLegacyToolboxData(CharacterCard character, string legacyPath)
    {
        var backupFolderPath = Path.Combine(character.ToolFolderPath, LegacyBackupsFolderName);
        Directory.CreateDirectory(backupFolderPath);
        var backupPath = Path.Combine(
            backupFolderPath,
            $"ZDToolboxData-before-split-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        File.Copy(legacyPath, backupPath, overwrite: false);
    }

    private static string GetLegacyToolboxDataPath(CharacterCard character)
    {
        return Path.Combine(character.FolderPath, ToolFolderName, LegacyToolboxDataFileName);
    }

    private static void CreateBackupIfNeeded(string path, string currentText)
    {
        if (string.IsNullOrWhiteSpace(currentText) || !File.Exists(path))
        {
            return;
        }

        var backupFolderPath = Path.Combine(Path.GetDirectoryName(path)!, ".backups");
        Directory.CreateDirectory(backupFolderPath);
        var backupPath = Path.Combine(
            backupFolderPath,
            $"{Path.GetFileNameWithoutExtension(path)}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        File.WriteAllText(backupPath, currentText, Encoding.UTF8);
    }

    private static void PruneBackups(string ownerFolderPath)
    {
        var backupFolderPath = Path.Combine(ownerFolderPath, ".backups");
        if (!Directory.Exists(backupFolderPath))
        {
            return;
        }

        foreach (var backup in Directory
                     .EnumerateFiles(backupFolderPath, "*.json")
                     .Select(path => new FileInfo(path))
                     .OrderByDescending(file => file.LastWriteTimeUtc)
                     .Skip(MaxBackupCount))
        {
            backup.Delete();
        }
    }

    private static void WriteAllTextAtomic(string path, string text)
    {
        var tempPath = $"{path}.tmp";
        File.WriteAllText(tempPath, text, Encoding.UTF8);
        File.Move(tempPath, path, overwrite: true);
    }

    private static bool IsPathUnderDirectory(string filePath, string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(directoryPath))
        {
            return false;
        }

        var fullFilePath = Path.GetFullPath(filePath);
        var fullDirectoryPath = Path.GetFullPath(directoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullFilePath.StartsWith(fullDirectoryPath, StringComparison.OrdinalIgnoreCase);
    }

    private static void RefreshIconUri(CharacterCard character, BuffEntry buff)
    {
        var resolvedPath = ResolveCharacterPath(character, buff.IconPath);
        buff.IconUri = File.Exists(resolvedPath)
            ? new Uri(resolvedPath).AbsoluteUri
            : string.Empty;
    }

    private static string ResolveCharacterPath(CharacterCard character, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (Path.IsPathFullyQualified(value))
        {
            return value;
        }

        return Path.GetFullPath(Path.Combine(character.FolderPath, value.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string ToCharacterRelativePath(CharacterCard character, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var fullPath = ResolveCharacterPath(character, value);
        if (!IsPathUnderDirectory(fullPath, character.FolderPath))
        {
            return value;
        }

        return Path.GetRelativePath(character.FolderPath, fullPath).Replace('\\', '/');
    }

    private static string SanitizeCode(string value)
    {
        var chars = value
            .Trim()
            .Where(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-')
            .ToArray();
        return new string(chars);
    }

    private static string NormalizeDamageType(string value)
    {
        return value switch
        {
            "属性-打击方" => "发送方-属性",
            "最终伤害-打击方" => "发送方-最终",
            "属性-受伤方" => "接收方-属性",
            "最终伤害-受伤方" => "接收方-最终",
            "发送方-属性" or "发送方-最终" or "接收方-属性" or "接收方-最终" => value,
            _ => "发送方-属性"
        };
    }

    private static string NormalizeTaskPriority(string value)
    {
        return value switch
        {
            "低" or "正常" or "高" or "紧急" => value,
            "Low" => "低",
            "Normal" => "正常",
            "High" => "高",
            "Urgent" => "紧急",
            _ => "正常"
        };
    }

    private static string NormalizeIntegerText(string value, string fallback)
    {
        return int.TryParse(value, out var parsed)
            ? parsed.ToString()
            : fallback;
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

        using var output = new Bitmap(targetWidth, targetHeight);
        using var graphics = Graphics.FromImage(output);
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.DrawImage(source, new Rectangle(0, 0, targetWidth, targetHeight), crop, GraphicsUnit.Pixel);
        output.Save(targetPath, ImageFormat.Png);
    }

    private static bool IsSupportedImage(string path)
    {
        return SupportedImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }
}
