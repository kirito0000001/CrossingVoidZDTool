using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace CrossingVoidZDTool.Services;

internal sealed class CharacterToolboxDataService
{
    private const string ToolboxDataFileName = "ZDToolboxData.json";
    private const int MaxBackupCount = 60;
    private const string BackupFolderName = "ZDToolboxDataBackups";
    private static readonly ConcurrentDictionary<string, object> FileLocks = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly AppJsonSerializerContext JsonContext = new(JsonOptions);
    private static readonly JsonTypeInfo<CharacterToolboxData> ToolboxDataJsonTypeInfo = JsonContext.CharacterToolboxData;

    public CharacterToolboxData Load(CharacterCard character)
    {
        var path = GetToolboxDataFilePath(character);
        lock (GetFileLock(path))
        {
            Directory.CreateDirectory(character.ToolFolderPath);
            if (!File.Exists(path))
            {
                return new CharacterToolboxData();
            }

            return ReadToolboxData(path);
        }
    }

    public void Save(CharacterCard character, CharacterToolboxData data)
    {
        Update(character, current =>
        {
            current.Draft = data.Draft;
            current.CharacterInfo = data.CharacterInfo;
            current.Skills = data.Skills;
            current.Buffs = data.Buffs;
            current.SequenceFrames = data.SequenceFrames;
        });
    }

    public void Update(CharacterCard character, Action<CharacterToolboxData> update)
    {
        var path = GetToolboxDataFilePath(character);
        lock (GetFileLock(path))
        {
            Directory.CreateDirectory(character.ToolFolderPath);
            var data = File.Exists(path) ? ReadToolboxData(path) : new CharacterToolboxData();
            update(data);
            data.UpdatedAt = DateTime.Now;
            var nextText = JsonSerializer.Serialize(data, ToolboxDataJsonTypeInfo);
            var currentText = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : string.Empty;
            if (string.Equals(currentText, nextText, StringComparison.Ordinal))
            {
                return;
            }

            CreateBackupIfNeeded(path, currentText);
            WriteAllTextAtomic(path, nextText);
            PruneBackups(character.ToolFolderPath);
        }
    }

    private static string GetToolboxDataFilePath(CharacterCard character)
    {
        return Path.Combine(character.ToolFolderPath, ToolboxDataFileName);
    }

    private static object GetFileLock(string path)
    {
        return FileLocks.GetOrAdd(Path.GetFullPath(path), _ => new object());
    }

    private static CharacterToolboxData ReadToolboxData(string path)
    {
        try
        {
            return JsonSerializer.Deserialize(
                File.ReadAllText(path, Encoding.UTF8),
                AppJsonSerializerContext.Default.CharacterToolboxData) ?? new CharacterToolboxData();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"角色工具箱数据读取失败：{path}", ex);
        }
    }

    private static void CreateBackupIfNeeded(string path, string currentText)
    {
        if (string.IsNullOrWhiteSpace(currentText) || !File.Exists(path))
        {
            return;
        }

        var backupFolderPath = Path.Combine(Path.GetDirectoryName(path)!, BackupFolderName);
        Directory.CreateDirectory(backupFolderPath);
        var backupPath = Path.Combine(
            backupFolderPath,
            $"{Path.GetFileNameWithoutExtension(path)}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        File.WriteAllText(backupPath, currentText, Encoding.UTF8);
    }

    private static void PruneBackups(string toolFolderPath)
    {
        var backupFolderPath = Path.Combine(toolFolderPath, BackupFolderName);
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
}
