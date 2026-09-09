using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 角色的 Zip 备份、还原与保留策略。
///
/// 从 <see cref="CharacterWorkspaceService"/> 里搬出来的。工作区那个类原本同时管六件事，
/// 备份是其中最独立的一块：输入只有「一个角色目录」，既不参与工作区扫描，也不决定
/// 角色卡怎么解释，却自带压缩、进度上报、保留份数、备注元数据一整套自己的规矩。
/// 混在一起的时候，想看清「到底留几份、什么时候删」得先翻过八百行不相干的代码。
///
/// 三条踩过坑的规矩留在这里，免得下次有人「顺手简化」：
/// 打包时必须跳过 CharacterBackups 目录本身，否则每备份一次体积翻一倍；
/// 还原前必须先自动备份当前状态，否则用户选错一份就再也回不去了；
/// 清理旧备份时手动和自动分开数，不然还原前产生的那串自动备份会把用户
/// 特意打的手动标记点挤掉。
/// </summary>
internal sealed class CharacterBackupService
{
    /// <summary>备份放在角色自己的 tool/ 下面，跟着角色目录一起搬家、一起导出。</summary>
    private const string BackupsFolderName = CharacterFolderLayout.CharacterBackups;

    private const int MaxManualBackupCount = 3;
    private const int MaxAutomaticBackupCount = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly AppJsonSerializerContext JsonContext = new(JsonOptions);

    public CharacterBackupEntry BackupCharacter(
        CharacterCard character,
        string note,
        IProgress<CharacterBackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return BackupCharacter(character, note, CharacterBackupKinds.Manual, progress, cancellationToken);
    }

    public CharacterBackupEntry BackupCharacter(
        CharacterCard character,
        string note,
        string kind,
        IProgress<CharacterBackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        CharacterWorkspaceService.EnsureCharacterLayout(character.FolderPath);
        if (!Directory.Exists(character.FolderPath))
        {
            throw new DirectoryNotFoundException($"角色文件夹不存在：{character.FolderPath}");
        }

        var backupsPath = GetCharacterBackupsPath(character);
        Directory.CreateDirectory(backupsPath);

        var createdAt = DateTime.Now;
        var normalizedKind = NormalizeBackupKind(kind);
        var safeName = SanitizeFileName($"{character.Code}-{character.Name}");
        var safeNote = SanitizeFileName(NormalizeSingleLine(note));
        var noteSuffix = string.IsNullOrWhiteSpace(safeNote) ? string.Empty : $"_{safeNote}";
        var kindToken = string.Equals(normalizedKind, CharacterBackupKinds.Automatic, StringComparison.OrdinalIgnoreCase) ? "auto" : "manual";
        var backupPath = Path.Combine(backupsPath, $"{safeName}_{kindToken}_{createdAt:yyyyMMdd_HHmmss}{noteSuffix}.zip");
        var duplicateIndex = 1;
        while (File.Exists(backupPath))
        {
            backupPath = Path.Combine(backupsPath, $"{safeName}_{kindToken}_{createdAt:yyyyMMdd_HHmmss}{noteSuffix}_{duplicateIndex}.zip");
            duplicateIndex++;
        }

        progress?.Report(new CharacterBackupProgress("正在扫描角色文件...", 0, 0, 0, 0, 0, null));
        cancellationToken.ThrowIfCancellationRequested();
        var files = EnumerateCharacterBackupFiles(character.FolderPath, backupsPath).ToList();
        var totalBytes = files.Sum(filePath => new FileInfo(filePath).Length);

        using (var archive = ZipFile.Open(backupPath, ZipArchiveMode.Create))
        {
            long completedBytes = 0;
            for (var index = 0; index < files.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var filePath = files[index];
                var fileLength = new FileInfo(filePath).Length;
                var relativePath = Path.GetRelativePath(character.FolderPath, filePath).Replace('\\', '/');
                var percent = files.Count == 0
                    ? 90
                    : Math.Min(90, Math.Max(1, completedBytes * 90d / Math.Max(1, totalBytes)));
                progress?.Report(new CharacterBackupProgress(
                    $"正在备份 {index + 1}/{files.Count}：{relativePath}",
                    percent,
                    index,
                    files.Count,
                    completedBytes,
                    totalBytes,
                    relativePath));
                archive.CreateEntryFromFile(filePath, relativePath, CompressionLevel.Optimal);
                completedBytes += fileLength;
            }
        }

        progress?.Report(new CharacterBackupProgress("正在写入备份备注...", 94, files.Count, files.Count, totalBytes, totalBytes, null));
        cancellationToken.ThrowIfCancellationRequested();
        var meta = new CharacterBackupMeta
        {
            CreatedAt = createdAt,
            Note = NormalizeSingleLine(note),
            Kind = normalizedKind
        };
        File.WriteAllText(GetBackupMetaPath(backupPath), JsonSerializer.Serialize(meta, JsonContext.CharacterBackupMeta), Encoding.UTF8);

        progress?.Report(new CharacterBackupProgress("正在清理旧备份，自动和手动各最多保留 3 份...", 97, files.Count, files.Count, totalBytes, totalBytes, null));
        cancellationToken.ThrowIfCancellationRequested();
        PruneCharacterBackups(character, normalizedKind);
        progress?.Report(new CharacterBackupProgress("备份完成。", 100, files.Count, files.Count, totalBytes, totalBytes, null));
        return BuildBackupEntry(backupPath);
    }

    public CharacterCard RestoreCharacterBackup(
        CharacterCard character,
        CharacterBackupEntry backup,
        IProgress<CharacterBackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        CharacterWorkspaceService.EnsureCharacterLayout(character.FolderPath);
        if (!File.Exists(backup.Path))
        {
            throw new FileNotFoundException("备份文件不存在。", backup.Path);
        }

        progress?.Report(new CharacterBackupProgress("还原前正在自动备份当前状态...", 0, 0, 0, 0, 0, null));
        BackupCharacter(character, "还原前自动保护", CharacterBackupKinds.Automatic, progress, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var backupsPath = GetCharacterBackupsPath(character);
        progress?.Report(new CharacterBackupProgress("正在清理当前角色文件...", 35, 0, 0, 0, 0, null));
        foreach (var filePath in Directory.EnumerateFiles(character.FolderPath, "*", SearchOption.AllDirectories)
                     .Where(filePath => !CharacterWorkspaceService.IsPathInsideDirectory(filePath, backupsPath))
                     .ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(filePath);
        }

        foreach (var directoryPath in Directory.EnumerateDirectories(character.FolderPath, "*", SearchOption.AllDirectories)
                     .Where(directoryPath => !CharacterWorkspaceService.IsPathInsideDirectory(directoryPath, backupsPath))
                     .OrderByDescending(path => path.Length)
                     .ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(directoryPath) && !Directory.EnumerateFileSystemEntries(directoryPath).Any())
            {
                Directory.Delete(directoryPath);
            }
        }

        progress?.Report(new CharacterBackupProgress("正在解压备份...", 65, 0, 0, 0, 0, null));
        ZipFile.ExtractToDirectory(backup.Path, character.FolderPath, overwriteFiles: true);
        CharacterWorkspaceService.EnsureCharacterFolders(character.FolderPath);
        progress?.Report(new CharacterBackupProgress("还原完成。", 100, 0, 0, 0, 0, null));
        return CharacterWorkspaceService.TryLoadCharacterCard(character.FolderPath) ?? character;
    }

    public IReadOnlyList<CharacterBackupEntry> LoadCharacterBackups(CharacterCard character)
    {
        var backupsPath = GetCharacterBackupsPath(character);
        if (!Directory.Exists(backupsPath))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(backupsPath, "*.zip", SearchOption.TopDirectoryOnly)
            .Select(BuildBackupEntry)
            .OrderByDescending(backup => backup.CreatedAt)
            .ToList();
    }

    private static string GetCharacterBackupsPath(CharacterCard character)
    {
        return Path.Combine(character.ToolFolderPath, BackupsFolderName);
    }

    private static string GetBackupMetaPath(string backupPath)
    {
        return $"{backupPath}.meta.json";
    }

    /// <summary>
    /// 打包范围。刻意把 CharacterBackups 目录整个排除掉——
    /// 备份里再套备份的话，每按一次备份体积就翻一倍。
    /// </summary>
    private static IEnumerable<string> EnumerateCharacterBackupFiles(string characterFolderPath, string backupsPath)
    {
        return Directory
            .EnumerateFiles(characterFolderPath, "*", SearchOption.AllDirectories)
            .Where(filePath => !CharacterWorkspaceService.IsPathInsideDirectory(filePath, backupsPath));
    }

    private void PruneCharacterBackups(CharacterCard character, string kind)
    {
        var maxCount = string.Equals(kind, CharacterBackupKinds.Automatic, StringComparison.OrdinalIgnoreCase)
            ? MaxAutomaticBackupCount
            : MaxManualBackupCount;
        foreach (var backup in LoadCharacterBackups(character)
                     .Where(backup => string.Equals(backup.Kind, kind, StringComparison.OrdinalIgnoreCase))
                     .Skip(maxCount))
        {
            if (File.Exists(backup.Path))
            {
                File.Delete(backup.Path);
            }

            var metaPath = GetBackupMetaPath(backup.Path);
            if (File.Exists(metaPath))
            {
                File.Delete(metaPath);
            }
        }
    }

    private static CharacterBackupEntry BuildBackupEntry(string backupPath)
    {
        var fileInfo = new FileInfo(backupPath);
        var meta = ReadBackupMeta(GetBackupMetaPath(backupPath));
        var createdAt = meta?.CreatedAt is { } metaCreatedAt && metaCreatedAt != default
            ? metaCreatedAt
            : fileInfo.LastWriteTime;
        var note = NormalizeSingleLine(meta?.Note);
        var noteText = string.IsNullOrWhiteSpace(note) ? "无备注" : note;
        var kind = NormalizeBackupKind(meta?.Kind);
        var kindText = string.Equals(kind, CharacterBackupKinds.Automatic, StringComparison.OrdinalIgnoreCase) ? "自动备份" : "手动备份";
        var displayName = $"{createdAt:yyyy-MM-dd HH:mm:ss} · {kindText} · {noteText} · {FormatFileSize(fileInfo.Length)}";
        return new CharacterBackupEntry(backupPath, createdAt, fileInfo.Length, note, displayName, kind);
    }

    /// <summary>
    /// 备注元数据读不出来不算错：备份文件本身还在，用文件时间和「无备注」把这一条撑出来，
    /// 用户至少还能还原。丢一行备注，总好过整份备份从列表里消失。
    /// </summary>
    private static CharacterBackupMeta? ReadBackupMeta(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path, Encoding.UTF8), AppJsonSerializerContext.Default.CharacterBackupMeta);
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeBackupKind(string? kind)
    {
        return string.Equals(kind, CharacterBackupKinds.Automatic, StringComparison.OrdinalIgnoreCase)
            ? CharacterBackupKinds.Automatic
            : CharacterBackupKinds.Manual;
    }

    private static string NormalizeSingleLine(string? text)
    {
        return string.Join(" ", (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    /// <summary>备注要进文件名，非法字符和长度都得先削掉，否则备份直接写不出来。</summary>
    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(NormalizeSingleLine(value).Where(ch => !invalidChars.Contains(ch)).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return "Backup";
        }

        return sanitized.Length > 40 ? sanitized[..40] : sanitized;
    }

    private static string FormatFileSize(long byteCount)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var size = (double)byteCount;
        var unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return unitIndex == 0 ? $"{byteCount} {units[unitIndex]}" : $"{size:0.##} {units[unitIndex]}";
    }
}
