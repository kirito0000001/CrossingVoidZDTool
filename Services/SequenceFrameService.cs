using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using System.Threading;

namespace CrossingVoidZDTool.Services;

internal sealed class SequenceFrameService
{
    public const int RequiredWidth = 928;
    public const int RequiredHeight = 640;
    public const int DefaultFps = 12;
    public const int MaxFrameDuration = 600;

    private const string ZdMaterialFolderName = "ZDMaterial";
    private const string FramesFolderName = "Frames";
    private const string ManifestFileName = "sequence.json";
    private const string SnapshotManifestFileName = "sequence.snapshot.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
    private static readonly AppJsonSerializerContext JsonContext = new(JsonOptions);
    private static readonly JsonTypeInfo<SequenceFrameManifest> ManifestJsonTypeInfo = JsonContext.SequenceFrameManifest;
    private static readonly string[] SupportedImageExtensions =
    [
        ".png",
        ".jpg",
        ".jpeg",
        ".webp",
        ".bmp"
    ];

    private static readonly SequenceFrameAction[] BaseActions =
    [
        new("点击", "Click", false),
        new("死亡", "Death", false),
        new("反击", "DefAtk", false),
        new("失败", "Defeat", false),
        new("防御", "Defence", false),
        new("闪避", "Dodge", false),
        new("坠落", "FlyDown", false),
        new("飞行", "Flying", false),
        new("击飞", "FlyStart", false),
        new("站街", "Idle", false),
        new("落地", "Land", false),
        new("移动", "Move", false),
        new("受伤", "OnDamage", false),
        new("站起", "StandUP", false),
        new("胜利", "Victory", false)
    ];

    private readonly CharacterToolboxDataService _toolboxDataService = new();
    private readonly CharacterFormService _formService = new();

    public SequenceFramesData LoadData(CharacterCard character)
    {
        return NormalizeData(_toolboxDataService.Load(character).SequenceFrames);
    }

    public void SaveData(CharacterCard character, SequenceFramesData data)
    {
        _toolboxDataService.Update(character, toolboxData =>
        {
            toolboxData.SequenceFrames = NormalizeData(data);
        });
    }

    public IReadOnlyDictionary<string, IReadOnlyList<SequenceFrameVoiceUsage>> GetVoiceUsages(CharacterCard character)
    {
        var result = new Dictionary<string, List<SequenceFrameVoiceUsage>>(StringComparer.OrdinalIgnoreCase);
        var skills = new CharacterSkillsService().Load(character);
        foreach (var section in LoadSections(character, skills))
        {
            foreach (var frame in section.Frames.Where(frame => frame.HasVoice))
            {
                var path = Path.GetFullPath(frame.VoiceFilePath);
                if (!result.TryGetValue(path, out var usages))
                {
                    usages = [];
                    result[path] = usages;
                }

                usages.Add(new SequenceFrameVoiceUsage(
                    section.Action.Code,
                    section.Action.DisplayName,
                    frame.Index));
            }
        }

        return result.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<SequenceFrameVoiceUsage>)pair.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<SequenceFrameSection> LoadSections(CharacterCard character, CharacterSkillsData skillsData, CancellationToken cancellationToken = default)
    {
        var actions = BuildActions(skillsData, _formService.GetFormLimit(character));
        return actions.Select(action => LoadSection(character, action, cancellationToken)).ToList();
    }

    public IReadOnlyList<SequenceFrameItem> ImportFrames(CharacterCard character, SequenceFrameAction action, IReadOnlyList<string> sourceFilePaths)
    {
        var supported = OrderImportSourcePaths(sourceFilePaths
            .Where(path => File.Exists(path) && IsSupportedImage(path))
            .ToList());
        if (supported.Count == 0)
        {
            return [];
        }

        var folderPath = GetActionFolderPath(character, action);
        Directory.CreateDirectory(folderPath);
        var previousFps = LoadOrCreateManifest(character, action).Fps;
        var framesFolderPath = GetFramesFolderPath(character, action);
        Directory.CreateDirectory(framesFolderPath);
        ClearActionFolder(folderPath);

        var manifest = CreateManifest(action);
        manifest.Fps = previousFps;
        for (var index = 0; index < supported.Count; index++)
        {
            var relativePath = ImportSourceIntoPool(character, action, supported[index]);
            manifest.Frames.Add(new SequenceFrameManifestEntry { RelativePath = relativePath });
        }

        SaveManifest(character, action, manifest);
        return LoadSection(character, action, CancellationToken.None).Frames;
    }

    internal static IReadOnlyList<string> OrderImportSourcePaths(IReadOnlyList<string> sourceFilePaths)
    {
        if (sourceFilePaths.Count < 2)
        {
            return sourceFilePaths.ToList();
        }

        var numberedPaths = new List<(string Path, long Number, int OriginalIndex)>(sourceFilePaths.Count);
        string? sharedPrefix = null;
        var numbers = new HashSet<long>();
        for (var index = 0; index < sourceFilePaths.Count; index++)
        {
            var name = Path.GetFileNameWithoutExtension(sourceFilePaths[index]);
            var match = Regex.Match(name, "^(?<prefix>.*?)(?<number>\\d+)$", RegexOptions.CultureInvariant);
            if (!match.Success ||
                !long.TryParse(match.Groups["number"].Value, out var number) ||
                !numbers.Add(number))
            {
                return sourceFilePaths.ToList();
            }

            var prefix = match.Groups["prefix"].Value;
            sharedPrefix ??= prefix;
            if (!string.Equals(sharedPrefix, prefix, StringComparison.OrdinalIgnoreCase))
            {
                return sourceFilePaths.ToList();
            }

            numberedPaths.Add((sourceFilePaths[index], number, index));
        }

        return numberedPaths
            .OrderBy(item => item.Number)
            .ThenBy(item => item.OriginalIndex)
            .Select(item => item.Path)
            .ToList();
    }

    public IReadOnlyList<SequenceFrameItem> ImportExternalFrames(
        CharacterCard character,
        SequenceFrameAction action,
        IReadOnlyList<ExternalSequenceFrameSource> frames,
        int fps)
    {
        var validFrames = frames
            .Where(frame => frame.IsBlank || File.Exists(frame.FilePath) && IsSupportedImage(frame.FilePath))
            .ToList();
        if (validFrames.Count == 0)
        {
            return [];
        }

        var folderPath = GetActionFolderPath(character, action);
        Directory.CreateDirectory(folderPath);
        Directory.CreateDirectory(GetFramesFolderPath(character, action));
        ClearActionFolder(folderPath);

        var manifest = CreateManifest(action);
        manifest.Fps = Math.Clamp(fps <= 0 ? DefaultFps : fps, 1, 60);
        foreach (var frame in validFrames)
        {
            manifest.Frames.Add(frame.IsBlank
                ? new SequenceFrameManifestEntry { IsBlank = true }
                : new SequenceFrameManifestEntry
                {
                    RelativePath = ImportSourceIntoPool(character, action, frame.FilePath)
                });
        }

        SaveManifest(character, action, manifest);
        return LoadSection(character, action, CancellationToken.None).Frames;
    }

    public IReadOnlyList<SequenceFrameItem> DeleteFrame(CharacterCard character, SequenceFrameAction action, SequenceFrameItem frame)
    {
        var manifest = LoadOrCreateManifest(character, action);
        var index = ResolveManifestIndex(manifest, frame);
        if (index >= 0)
        {
            manifest.Frames.RemoveAt(index);
        }

        SaveManifest(character, action, manifest);
        PruneUnreferencedFrameFiles(character, action, manifest);
        return LoadSection(character, action, CancellationToken.None).Frames;
    }

    public IReadOnlyList<SequenceFrameItem> DeleteFrames(
        CharacterCard character,
        SequenceFrameAction action,
        IReadOnlyList<SequenceFrameItem> frames)
    {
        var manifest = LoadOrCreateManifest(character, action);
        var indexes = frames
            .Select(frame => ResolveManifestIndex(manifest, frame))
            .Where(index => index >= 0)
            .Distinct()
            .OrderByDescending(index => index)
            .ToList();
        foreach (var index in indexes)
        {
            manifest.Frames.RemoveAt(index);
        }

        SaveManifest(character, action, manifest);
        PruneUnreferencedFrameFiles(character, action, manifest);
        return LoadSection(character, action, CancellationToken.None).Frames;
    }

    public void RestoreActionFrames(CharacterCard character, SequenceFrameAction action, IReadOnlyList<string> snapshotFilePaths)
    {
        var snapshotManifestPath = snapshotFilePaths.FirstOrDefault(path =>
            string.Equals(Path.GetFileName(path), SnapshotManifestFileName, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(snapshotManifestPath) || !File.Exists(snapshotManifestPath))
        {
            throw new InvalidDataException("序列帧快照清单不存在，无法恢复。");
        }

        var snapshotManifest = JsonSerializer.Deserialize(
            File.ReadAllText(snapshotManifestPath, Encoding.UTF8),
            AppJsonSerializerContext.Default.SequenceFrameManifest)
            ?? throw new InvalidDataException("序列帧快照清单内容无效。");
        var folderPath = GetActionFolderPath(character, action);
        Directory.CreateDirectory(folderPath);
        ClearActionFolder(folderPath);
        var manifest = CreateManifest(action);
        manifest.Fps = snapshotManifest.Fps;
        var snapshotFolderPath = Path.GetDirectoryName(snapshotManifestPath)!;
        foreach (var entry in snapshotManifest.Frames)
        {
            if (entry.IsBlank)
            {
                manifest.Frames.Add(CloneEntry(entry));
                continue;
            }

            var sourcePath = Path.Combine(snapshotFolderPath, NormalizeRelativePath(entry.RelativePath));
            if (!File.Exists(sourcePath) || !IsSupportedImage(sourcePath))
            {
                throw new FileNotFoundException("序列帧快照中的图片不存在。", sourcePath);
            }

            manifest.Frames.Add(new SequenceFrameManifestEntry
            {
                SyncId = entry.SyncId,
                RelativePath = ImportSourceIntoPool(character, action, sourcePath),
                DurationFrames = entry.DurationFrames,
                VoiceRelativePath = entry.VoiceRelativePath
            });
        }

        SaveManifest(character, action, manifest);
    }

    public IReadOnlyList<string> CreateActionSnapshot(CharacterCard character, SequenceFrameAction action)
    {
        var manifest = LoadOrCreateManifest(character, action);
        var snapshotFolderPath = Path.Combine(
            character.ToolFolderPath,
            "OperationSnapshots",
            "SequenceFrames",
            $"{action.Code}-{DateTime.Now:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(snapshotFolderPath);
        var snapshotPaths = new List<string>();
        var snapshotIndexWidth = MaterialSequenceNaming.GetWidth(manifest.Frames.Count);
        var snapshotManifest = new SequenceFrameManifest
        {
            SchemaVersion = 3,
            ActionCode = action.Code,
            Fps = manifest.Fps
        };
        for (var index = 0; index < manifest.Frames.Count; index++)
        {
            var entry = manifest.Frames[index];
            if (entry.IsBlank)
            {
                var blankPath = Path.Combine(
                    snapshotFolderPath,
                    $"{(index + 1).ToString().PadLeft(snapshotIndexWidth, '0')}.blank");
                File.WriteAllText(blankPath, "blank", Encoding.UTF8);
                snapshotPaths.Add(blankPath);
                snapshotManifest.Frames.Add(CloneEntry(entry));
                continue;
            }

            var sourcePath = ResolveManifestPath(character, action, entry.RelativePath);
            if (!File.Exists(sourcePath) || !IsSupportedImage(sourcePath))
            {
                continue;
            }

            var extension = Path.GetExtension(sourcePath);
            var targetPath = Path.Combine(
                snapshotFolderPath,
                $"{(index + 1).ToString().PadLeft(snapshotIndexWidth, '0')}{extension}");
            File.Copy(sourcePath, targetPath, overwrite: true);
            snapshotPaths.Add(targetPath);
            snapshotManifest.Frames.Add(new SequenceFrameManifestEntry
            {
                SyncId = entry.SyncId,
                RelativePath = Path.GetFileName(targetPath),
                DurationFrames = entry.DurationFrames,
                VoiceRelativePath = entry.VoiceRelativePath
            });
        }

        var snapshotManifestPath = Path.Combine(snapshotFolderPath, SnapshotManifestFileName);
        File.WriteAllText(
            snapshotManifestPath,
            JsonSerializer.Serialize(snapshotManifest, ManifestJsonTypeInfo),
            Encoding.UTF8);
        snapshotPaths.Add(snapshotManifestPath);

        return snapshotPaths;
    }

    public IReadOnlyList<SequenceFrameItem> DuplicateFrame(CharacterCard character, SequenceFrameAction action, SequenceFrameItem frame)
    {
        if (!frame.IsBlank && !File.Exists(frame.FilePath))
        {
            throw new FileNotFoundException("没有找到要复制的序列帧。", frame.FilePath);
        }

        var manifest = LoadOrCreateManifest(character, action);
        var index = ResolveManifestIndex(manifest, frame);
        if (index < 0)
        {
            index = manifest.Frames.Count - 1;
        }

        var sourceEntry = index >= 0 && index < manifest.Frames.Count
            ? manifest.Frames[index]
            : null;
        manifest.Frames.Insert(index + 1, sourceEntry is null
            ? new SequenceFrameManifestEntry
            {
                IsBlank = frame.IsBlank,
                RelativePath = frame.IsBlank ? string.Empty : ToManifestRelativePath(character, action, frame.FilePath),
                DurationFrames = Math.Clamp(frame.DurationFrames, 1, MaxFrameDuration),
                VoiceRelativePath = ToCharacterRelativePath(character, frame.VoiceFilePath)
            }
            : CloneEntry(sourceEntry, preserveSyncId: false));
        SaveManifest(character, action, manifest);
        return LoadSection(character, action, CancellationToken.None).Frames;
    }

    public IReadOnlyList<SequenceFrameItem> DuplicateFrames(
        CharacterCard character,
        SequenceFrameAction action,
        IReadOnlyList<SequenceFrameItem> frames,
        SequenceFrameItem afterFrame)
    {
        var manifest = LoadOrCreateManifest(character, action);
        var indexes = frames
            .Select(frame => ResolveManifestIndex(manifest, frame))
            .Where(index => index >= 0)
            .Distinct()
            .OrderBy(index => index)
            .ToList();
        if (indexes.Count == 0)
        {
            return LoadSection(character, action, CancellationToken.None).Frames;
        }

        var targetIndex = ResolveManifestIndex(manifest, afterFrame);
        if (targetIndex < 0)
        {
            throw new InvalidOperationException("没有找到批量复制的插入位置。");
        }

        var entries = indexes
            .Select(index => CloneEntry(manifest.Frames[index], preserveSyncId: false))
            .ToList();
        for (var index = 0; index < entries.Count; index++)
        {
            manifest.Frames.Insert(targetIndex + 1 + index, entries[index]);
        }

        SaveManifest(character, action, manifest);
        return LoadSection(character, action, CancellationToken.None).Frames;
    }

    public IReadOnlyList<SequenceFrameItem> InsertBlankFrame(
        CharacterCard character,
        SequenceFrameAction action,
        SequenceFrameItem? anchorFrame,
        SequenceFrameInsertPosition position = SequenceFrameInsertPosition.After)
    {
        var manifest = LoadOrCreateManifest(character, action);
        var index = anchorFrame is null ? manifest.Frames.Count - 1 : ResolveManifestIndex(manifest, anchorFrame);
        if (index < -1)
        {
            index = manifest.Frames.Count - 1;
        }

        var insertIndex = position == SequenceFrameInsertPosition.Before && anchorFrame is not null
            ? Math.Max(0, index)
            : index + 1;
        manifest.Frames.Insert(insertIndex, new SequenceFrameManifestEntry { IsBlank = true });
        SaveManifest(character, action, manifest);
        return LoadSection(character, action, CancellationToken.None).Frames;
    }

    public IReadOnlyList<SequenceFrameItem> ReorderFrames(CharacterCard character, SequenceFrameAction action, IReadOnlyList<SequenceFrameItem> orderedFrames)
    {
        var existing = orderedFrames
            .Where(frame => frame.IsBlank || File.Exists(frame.FilePath) && IsSupportedImage(frame.FilePath))
            .ToList();
        if (existing.Count == 0)
        {
            return LoadSection(character, action, CancellationToken.None).Frames;
        }

        var manifest = LoadOrCreateManifest(character, action);
        var sourceEntries = manifest.Frames.ToList();
        manifest.Frames.Clear();
        foreach (var frame in existing)
        {
            var existingEntry = sourceEntries.ElementAtOrDefault(frame.Index - 1);
            manifest.Frames.Add(existingEntry is not null
                ? CloneEntry(existingEntry)
                : new SequenceFrameManifestEntry
                {
                    IsBlank = frame.IsBlank,
                    RelativePath = frame.IsBlank ? string.Empty : ToManifestRelativePath(character, action, frame.FilePath),
                    DurationFrames = Math.Clamp(frame.DurationFrames, 1, MaxFrameDuration),
                    VoiceRelativePath = ToCharacterRelativePath(character, frame.VoiceFilePath)
                });
        }

        SaveManifest(character, action, manifest);
        PruneUnreferencedFrameFiles(character, action, manifest);
        return LoadSection(character, action, CancellationToken.None).Frames;
    }

    public IReadOnlyList<SequenceFrameItem> ReplaceFrame(
        CharacterCard character,
        SequenceFrameAction action,
        SequenceFrameItem frame,
        string sourceFilePath)
    {
        if (!File.Exists(sourceFilePath) || !IsSupportedImage(sourceFilePath))
        {
            throw new InvalidDataException("请选择有效的 PNG、JPG、WEBP 或 BMP 图片。");
        }

        var manifest = LoadOrCreateManifest(character, action);
        var index = ResolveManifestIndex(manifest, frame);
        if (index < 0)
        {
            throw new InvalidOperationException("没有找到要替换的序列帧。");
        }

        var entry = manifest.Frames[index];
        entry.RelativePath = ImportSourceIntoPool(character, action, sourceFilePath);
        entry.IsBlank = false;
        SaveManifest(character, action, manifest);
        PruneUnreferencedFrameFiles(character, action, manifest);
        return LoadSection(character, action, CancellationToken.None).Frames;
    }

    public IReadOnlyList<SequenceFrameItem> ReplaceFrameWithSources(
        CharacterCard character,
        SequenceFrameAction action,
        SequenceFrameItem frame,
        IReadOnlyList<string> sourceFilePaths)
    {
        if (sourceFilePaths.Count == 0 ||
            sourceFilePaths.Any(path => !File.Exists(path) || !IsSupportedImage(path)))
        {
            throw new InvalidDataException("请选择至少一张有效的 PNG、JPG、WEBP 或 BMP 图片。");
        }

        var manifest = LoadOrCreateManifest(character, action);
        var index = ResolveManifestIndex(manifest, frame);
        if (index < 0)
        {
            throw new InvalidOperationException("没有找到要替换的序列帧。");
        }

        var relativePaths = sourceFilePaths
            .Select(path => ImportSourceIntoPool(character, action, path))
            .ToList();
        var targetEntry = manifest.Frames[index];
        targetEntry.RelativePath = relativePaths[0];
        targetEntry.IsBlank = false;
        for (var sourceIndex = 1; sourceIndex < relativePaths.Count; sourceIndex++)
        {
            manifest.Frames.Insert(index + sourceIndex, new SequenceFrameManifestEntry
            {
                RelativePath = relativePaths[sourceIndex]
            });
        }

        SaveManifest(character, action, manifest);
        PruneUnreferencedFrameFiles(character, action, manifest);
        return LoadSection(character, action, CancellationToken.None).Frames;
    }

    public IReadOnlyList<SequenceFrameItem> SetFrameDuration(
        CharacterCard character,
        SequenceFrameAction action,
        SequenceFrameItem frame,
        int durationFrames)
    {
        var manifest = LoadOrCreateManifest(character, action);
        var index = ResolveManifestIndex(manifest, frame);
        if (index < 0)
        {
            throw new InvalidOperationException("没有找到要设置时长的序列帧。");
        }

        manifest.Frames[index].DurationFrames = Math.Clamp(durationFrames, 1, MaxFrameDuration);
        SaveManifest(character, action, manifest);
        return LoadSection(character, action, CancellationToken.None).Frames;
    }

    public IReadOnlyList<SequenceFrameItem> SetFrameVoice(
        CharacterCard character,
        SequenceFrameAction action,
        SequenceFrameItem frame,
        string? voiceFilePath)
    {
        var manifest = LoadOrCreateManifest(character, action);
        var index = ResolveManifestIndex(manifest, frame);
        if (index < 0)
        {
            throw new InvalidOperationException("没有找到要绑定语音的序列帧。");
        }

        manifest.Frames[index].VoiceRelativePath = string.IsNullOrWhiteSpace(voiceFilePath)
            ? string.Empty
            : ToCharacterRelativePath(character, ValidateVoicePath(character, voiceFilePath));
        SaveManifest(character, action, manifest);
        return LoadSection(character, action, CancellationToken.None).Frames;
    }

    public void AssignFrameSyncIds(
        CharacterCard character,
        SequenceFrameAction action,
        IReadOnlyList<string> syncIds)
    {
        ArgumentNullException.ThrowIfNull(syncIds);
        var manifest = LoadOrCreateManifest(character, action);
        if (manifest.Frames.Count != syncIds.Count)
        {
            throw new InvalidOperationException(
                $"序列帧身份数量与帧格数量不一致：{action.DisplayName}，帧格 {manifest.Frames.Count}，身份 {syncIds.Count}。");
        }

        var normalized = syncIds.Select(value => value?.Trim() ?? string.Empty).ToArray();
        if (normalized.Any(string.IsNullOrWhiteSpace) ||
            normalized.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalized.Length)
        {
            throw new InvalidOperationException($"序列帧身份包含空值或重复项：{action.DisplayName}。");
        }

        for (var index = 0; index < manifest.Frames.Count; index++)
        {
            manifest.Frames[index].SyncId = normalized[index];
        }

        SaveManifest(character, action, manifest);
    }

    public void SetActionFps(CharacterCard character, SequenceFrameAction action, int fps)
    {
        var manifest = LoadOrCreateManifest(character, action);
        manifest.Fps = Math.Clamp(fps, 1, 60);
        SaveManifest(character, action, manifest);
    }

    public int ReplaceDuplicateFrameReferences(
        CharacterCard character,
        CharacterSkillsData skillsData,
        string keptFilePath,
        IReadOnlyList<string> duplicateFilePaths,
        IProgress<ProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(keptFilePath) || !File.Exists(keptFilePath))
        {
            throw new FileNotFoundException("没有找到要保留的序列帧资源。", keptFilePath);
        }

        var keptFullPath = Path.GetFullPath(keptFilePath);
        var duplicateFullPaths = duplicateFilePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Where(path => !string.Equals(path, keptFullPath, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (duplicateFullPaths.Count == 0)
        {
            return 0;
        }

        var updatedReferenceCount = 0;
        var actions = BuildActions(skillsData, _formService.GetFormLimit(character));
        for (var actionIndex = 0; actionIndex < actions.Count; actionIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var action = actions[actionIndex];
            progress?.Report(new ProgressUpdate(
                "正在重定向重复帧引用...",
                15 + (actionIndex + 1) * 55d / actions.Count,
                action.DisplayName));
            var manifest = LoadOrCreateManifest(character, action);
            var actionFolderPath = GetActionFolderPath(character, action);
            var keptRelativePath = NormalizeRelativePath(Path.GetRelativePath(actionFolderPath, keptFullPath));
            var changed = false;
            foreach (var entry in manifest.Frames)
            {
                var resolvedPath = Path.GetFullPath(ResolveManifestPath(character, action, entry.RelativePath));
                if (!duplicateFullPaths.Contains(resolvedPath))
                {
                    continue;
                }

                entry.RelativePath = keptRelativePath;
                updatedReferenceCount++;
                changed = true;
            }

            if (changed)
            {
                SaveManifest(character, action, manifest);
            }
        }

        var duplicateList = duplicateFullPaths.ToList();
        for (var index = 0; index < duplicateList.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var duplicatePath = duplicateList[index];
            progress?.Report(new ProgressUpdate(
                "正在删除重复帧资源...",
                72 + (index + 1) * 18d / duplicateList.Count,
                Path.GetFileName(duplicatePath)));
            if (File.Exists(duplicatePath))
            {
                File.Delete(duplicatePath);
            }
        }

        return updatedReferenceCount;
    }

    public string GetActionFolderPath(CharacterCard character, SequenceFrameAction action)
    {
        return Path.Combine(character.FolderPath, ZdMaterialFolderName, action.Code);
    }

    public static IReadOnlyList<SequenceFrameAction> BuildActions(CharacterSkillsData skillsData, int formLimit = 1)
    {
        var normalizedFormLimit = Math.Max(1, formLimit);
        var actions = new List<SequenceFrameAction>();
        foreach (var action in BaseActions)
        {
            AddFormActions(actions, action.DisplayName, action.Code, normalizedFormLimit, isSkill: false, isCombo: false);
        }

        AddFormActions(actions, "一技能", "Sk1", normalizedFormLimit, isSkill: true, isCombo: false);
        AddFormActions(actions, "二技能", "Sk2", normalizedFormLimit, isSkill: true, isCombo: false);
        AddFormActions(actions, "终结技", "Ko", normalizedFormLimit, isSkill: true, isCombo: false);
        AddFormActions(actions, "护援技", "Sub", normalizedFormLimit, isSkill: true, isCombo: false);
        AddFormActions(actions, "连携技", "Link", skillsData.ComboSkills.Count, isSkill: true, isCombo: true);
        return actions;
    }

    private static void AddFormActions(List<SequenceFrameAction> actions, string displayName, string code, int count, bool isSkill, bool isCombo)
    {
        var actionCount = isCombo ? Math.Max(0, count) : Math.Max(1, count);
        for (var index = 1; index <= actionCount; index++)
        {
            var suffix = actionCount > 1 && index > 1 ? index.ToString("00") : string.Empty;
            actions.Add(new SequenceFrameAction(
                index > 1 ? $"{displayName}-{index}" : displayName,
                $"{code}{suffix}",
                isSkill,
                isCombo,
                index));
        }
    }

    private SequenceFrameSection LoadSection(CharacterCard character, SequenceFrameAction action, CancellationToken cancellationToken)
    {
        var manifest = LoadOrCreateManifest(character, action);
        var frames = manifest.Frames
            .Select((entry, index) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.IsBlank)
                {
                    return CreateBlankFrameItem(character, entry, index + 1);
                }

                return CreateFrameItem(
                    ResolveManifestPath(character, action, entry.RelativePath),
                    index + 1,
                    manifest.Frames.Count,
                    entry.DurationFrames,
                    ResolveVoicePath(character, entry.VoiceRelativePath),
                    entry.SyncId);
            })
            .Where(frame => frame.IsBlank || File.Exists(frame.FilePath))
            .OrderBy(frame => frame.Index)
            .ToList();
        frames = AnnotateFrameReuse(frames);

        var invalidCount = frames.Count(frame => !frame.IsValid);
        var statusText = frames.Count == 0
            ? "未设置"
            : invalidCount > 0
                ? $"{frames.Count} 张，{invalidCount} 张尺寸不合规"
                : $"{frames.Count} 张，尺寸合规";
        return new SequenceFrameSection(action, frames, statusText, frames.Count == 0 || invalidCount > 0);
    }

    private static List<SequenceFrameItem> AnnotateFrameReuse(IReadOnlyList<SequenceFrameItem> frames)
    {
        var annotated = frames
            .Select(frame => frame with
            {
                ReuseCount = 1,
                ReuseOccurrence = 1,
                ReuseSourceIndex = 0,
                ReusePositionsText = string.Empty,
                ReuseColorIndex = -1
            })
            .ToList();
        var groups = annotated
            .Where(frame => !frame.IsBlank && !string.IsNullOrWhiteSpace(frame.FilePath))
            .GroupBy(frame => frame.FilePath, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.Min(frame => frame.Index))
            .ToList();

        for (var colorIndex = 0; colorIndex < groups.Count; colorIndex++)
        {
            var groupFrames = groups[colorIndex].OrderBy(frame => frame.Index).ToList();
            var positions = string.Join("、", groupFrames.Select(frame => frame.Index));
            for (var occurrenceIndex = 0; occurrenceIndex < groupFrames.Count; occurrenceIndex++)
            {
                var frameIndex = annotated.FindIndex(frame => frame.Index == groupFrames[occurrenceIndex].Index);
                annotated[frameIndex] = annotated[frameIndex] with
                {
                    ReuseCount = groupFrames.Count,
                    ReuseOccurrence = occurrenceIndex + 1,
                    ReuseSourceIndex = groupFrames[0].Index,
                    ReusePositionsText = positions,
                    ReuseColorIndex = colorIndex
                };
            }
        }

        return annotated;
    }

    private static SequenceFrameItem CreateFrameItem(
        string path,
        int sequenceIndex,
        int sequenceCount,
        int durationFrames,
        string voiceFilePath,
        string syncId)
    {
        var actualWidth = 0;
        var actualHeight = 0;
        try
        {
            using var image = Image.FromFile(path);
            actualWidth = image.Width;
            actualHeight = image.Height;
        }
        catch
        {
            // The item stays invalid and the UI will show its size as unreadable.
        }

        var info = new FileInfo(path);
        var version = $"{info.LastWriteTimeUtc.Ticks}-{info.Length}";
        var fileUri = $"{new Uri(info.FullName).AbsoluteUri}?v={Uri.EscapeDataString(version)}";
        return new SequenceFrameItem(
            info.FullName,
            fileUri,
            $"{MaterialSequenceNaming.FormatIndex(sequenceIndex, sequenceCount)}  {info.Name}",
            $"{info.FullName}|{version}",
            sequenceIndex,
            actualWidth,
            actualHeight,
            actualWidth == RequiredWidth && actualHeight == RequiredHeight,
            info.LastWriteTime,
            DurationFrames: Math.Clamp(durationFrames, 1, MaxFrameDuration),
            VoiceFilePath: voiceFilePath,
            VoiceFileName: Path.GetFileName(voiceFilePath),
            SyncId: syncId);
    }

    private static SequenceFrameItem CreateBlankFrameItem(
        CharacterCard character,
        SequenceFrameManifestEntry entry,
        int sequenceIndex)
    {
        var voiceFilePath = ResolveVoicePath(character, entry.VoiceRelativePath);
        return new SequenceFrameItem(
            string.Empty,
            string.Empty,
            "空白帧",
            $"blank|{sequenceIndex}",
            sequenceIndex,
            RequiredWidth,
            RequiredHeight,
            true,
            DateTime.MinValue,
            true,
            Math.Clamp(entry.DurationFrames, 1, MaxFrameDuration),
            voiceFilePath,
            Path.GetFileName(voiceFilePath),
            SyncId: entry.SyncId);
    }

    private static void ClearActionFolder(string folderPath)
    {
        foreach (var path in Directory.EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly))
        {
            File.Delete(path);
        }

        var framesFolderPath = Path.Combine(folderPath, FramesFolderName);
        if (Directory.Exists(framesFolderPath))
        {
            foreach (var path in Directory.EnumerateFiles(framesFolderPath).Where(IsSupportedImage))
            {
                File.Delete(path);
            }
        }
    }

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

    private List<string> LoadManifestFramePaths(CharacterCard character, SequenceFrameAction action)
    {
        var manifest = LoadOrCreateManifest(character, action);
        return manifest.Frames
            .Select(entry => ResolveManifestPath(character, action, entry.RelativePath))
            .Where(path => File.Exists(path) && IsSupportedImage(path))
            .ToList();
    }

    private SequenceFrameManifest LoadOrCreateManifest(CharacterCard character, SequenceFrameAction action)
    {
        var folderPath = GetActionFolderPath(character, action);
        MigrateLegacyActionFolderPath(character, action, folderPath);
        Directory.CreateDirectory(folderPath);
        Directory.CreateDirectory(GetFramesFolderPath(character, action));
        var manifestPath = GetManifestPath(character, action);
        if (!File.Exists(manifestPath))
        {
            return MigrateLegacyActionFolder(character, action);
        }

        try
        {
            var manifest = JsonSerializer.Deserialize(
                File.ReadAllText(manifestPath, Encoding.UTF8),
                AppJsonSerializerContext.Default.SequenceFrameManifest) ?? CreateManifest(action);
            return NormalizeManifest(character, action, manifest);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"序列帧清单读取失败：{manifestPath}", ex);
        }
    }

    private static void MigrateLegacyActionFolderPath(CharacterCard character, SequenceFrameAction action, string canonicalFolderPath)
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

    private SequenceFrameManifest MigrateLegacyActionFolder(CharacterCard character, SequenceFrameAction action)
    {
        var folderPath = GetActionFolderPath(character, action);
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
        var manifest = CreateManifest(action);
        foreach (var path in legacyPaths)
        {
            manifest.Frames.Add(new SequenceFrameManifestEntry
            {
                RelativePath = ImportSourceIntoPool(character, action, path)
            });
        }

        foreach (var path in legacyPaths.Where(File.Exists))
        {
            File.Delete(path);
        }

        SaveManifest(character, action, manifest);
        return manifest;
    }

    private SequenceFrameManifest NormalizeManifest(CharacterCard character, SequenceFrameAction action, SequenceFrameManifest manifest)
    {
        manifest.SchemaVersion = 3;
        MigrateActionCodeAliases(manifest, action);
        manifest.ActionCode = action.Code;
        manifest.Fps = Math.Clamp(manifest.Fps <= 0 ? DefaultFps : manifest.Fps, 1, 60);
        manifest.Frames ??= [];
        for (var index = manifest.Frames.Count - 1; index >= 0; index--)
        {
            var entry = manifest.Frames[index];
            if (entry is null)
            {
                manifest.Frames.RemoveAt(index);
                continue;
            }

            entry.DurationFrames = Math.Clamp(entry.DurationFrames, 1, MaxFrameDuration);
            entry.SyncId = string.IsNullOrWhiteSpace(entry.SyncId)
                ? Guid.NewGuid().ToString("N")
                : entry.SyncId.Trim();
            entry.VoiceRelativePath = NormalizeRelativePath(entry.VoiceRelativePath ?? string.Empty);
            if (entry.IsBlank)
            {
                entry.RelativePath = string.Empty;
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.RelativePath))
            {
                manifest.Frames.RemoveAt(index);
                continue;
            }

            entry.RelativePath = NormalizeRelativePath(entry.RelativePath);
        }

        SaveManifest(character, action, manifest);
        return manifest;
    }

    private static void MigrateActionCodeAliases(SequenceFrameManifest manifest, SequenceFrameAction action)
    {
        if (string.IsNullOrWhiteSpace(manifest.ActionCode) || string.Equals(manifest.ActionCode, action.Code, StringComparison.OrdinalIgnoreCase))
            return;
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Ondm"] = "OnDamage", ["OnDM"] = "OnDamage", ["Defense"] = "Defence",
            ["Flydown"] = "FlyDown", ["Flystart"] = "FlyStart", ["Standup"] = "StandUP"
        };
        if (aliases.TryGetValue(manifest.ActionCode.Trim(), out var canonical) && string.Equals(canonical, action.Code, StringComparison.OrdinalIgnoreCase))
            manifest.ActionCode = action.Code;
    }

    private SequenceFrameManifest CreateManifest(SequenceFrameAction action)
    {
        return new SequenceFrameManifest
        {
            ActionCode = action.Code,
            Fps = DefaultFps
        };
    }

    private void SaveManifest(CharacterCard character, SequenceFrameAction action, SequenceFrameManifest manifest)
    {
        manifest.SchemaVersion = 3;
        manifest.ActionCode = action.Code;
        manifest.Fps = Math.Clamp(manifest.Fps <= 0 ? DefaultFps : manifest.Fps, 1, 60);
        manifest.Frames ??= [];
        Directory.CreateDirectory(GetActionFolderPath(character, action));
        Directory.CreateDirectory(GetFramesFolderPath(character, action));
        var manifestPath = GetManifestPath(character, action);
        var text = JsonSerializer.Serialize(manifest, ManifestJsonTypeInfo);
        var current = File.Exists(manifestPath) ? File.ReadAllText(manifestPath, Encoding.UTF8) : string.Empty;
        if (string.Equals(text, current, StringComparison.Ordinal))
        {
            return;
        }

        var tempPath = $"{manifestPath}.tmp";
        File.WriteAllText(tempPath, text, Encoding.UTF8);
        File.Move(tempPath, manifestPath, overwrite: true);
    }

    internal static void RemapVoiceReferences(
        CharacterCard character,
        IReadOnlyDictionary<string, string?> pathMappings)
    {
        if (pathMappings.Count == 0)
        {
            return;
        }

        var normalizedMappings = pathMappings.ToDictionary(
            pair => Path.GetFullPath(pair.Key),
            pair => string.IsNullOrWhiteSpace(pair.Value) ? null : Path.GetFullPath(pair.Value),
            StringComparer.OrdinalIgnoreCase);
        var materialFolderPath = Path.Combine(character.FolderPath, ZdMaterialFolderName);
        if (!Directory.Exists(materialFolderPath))
        {
            return;
        }

        foreach (var manifestPath in Directory.EnumerateFiles(
                     materialFolderPath,
                     ManifestFileName,
                     SearchOption.AllDirectories))
        {
            SequenceFrameManifest? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize(
                    File.ReadAllText(manifestPath, Encoding.UTF8),
                    ManifestJsonTypeInfo);
            }
            catch (JsonException)
            {
                continue;
            }

            if (manifest?.Frames is null)
            {
                continue;
            }

            var changed = false;
            foreach (var entry in manifest.Frames)
            {
                if (string.IsNullOrWhiteSpace(entry.VoiceRelativePath))
                {
                    continue;
                }

                var currentPath = Path.GetFullPath(Path.Combine(
                    character.FolderPath,
                    NormalizeRelativePath(entry.VoiceRelativePath)));
                if (!normalizedMappings.TryGetValue(currentPath, out var targetPath))
                {
                    continue;
                }

                entry.VoiceRelativePath = targetPath is null
                    ? string.Empty
                    : NormalizeRelativePath(Path.GetRelativePath(character.FolderPath, targetPath));
                changed = true;
            }

            if (!changed)
            {
                continue;
            }

            var tempPath = $"{manifestPath}.tmp";
            File.WriteAllText(
                tempPath,
                JsonSerializer.Serialize(manifest, ManifestJsonTypeInfo),
                Encoding.UTF8);
            File.Move(tempPath, manifestPath, overwrite: true);
        }
    }

    private string ImportSourceIntoPool(CharacterCard character, SequenceFrameAction action, string sourcePath)
    {
        var sourceFullPath = Path.GetFullPath(sourcePath);
        var referencedCharacterFrame = EnumerateReferencedFramePaths(character)
            .FirstOrDefault(path => string.Equals(
                Path.GetFullPath(path),
                sourceFullPath,
                StringComparison.OrdinalIgnoreCase));
        if (referencedCharacterFrame is not null)
        {
            return NormalizeRelativePath(Path.GetRelativePath(
                GetActionFolderPath(character, action),
                sourceFullPath));
        }

        var framesFolderPath = GetFramesFolderPath(character, action);
        Directory.CreateDirectory(framesFolderPath);
        var hash = ComputeFileHash(sourceFullPath);
        var fileName = $"{character.Code}-{action.Code}-{hash[..16]}.png";
        var targetPath = Path.Combine(framesFolderPath, fileName);
        if (!File.Exists(targetPath))
        {
            SaveAsPng(sourceFullPath, targetPath);
        }

        return NormalizeRelativePath(Path.Combine(FramesFolderName, fileName));
    }

    private int ResolveManifestIndex(SequenceFrameManifest manifest, SequenceFrameItem frame)
    {
        var matching = manifest.Frames
            .Select((entry, index) => new { entry, index })
            .FirstOrDefault(item => item.index + 1 == frame.Index);
        if (matching is not null)
        {
            return matching.index;
        }

        return manifest.Frames
            .Select((entry, index) => new { entry, index })
            .FirstOrDefault(item => string.Equals(
                NormalizeRelativePath(item.entry.RelativePath),
                NormalizeRelativePath(ToPoolRelativePath(frame.FilePath)),
                StringComparison.OrdinalIgnoreCase))
            ?.index ?? -1;
    }

    private static SequenceFrameManifestEntry CloneEntry(
        SequenceFrameManifestEntry entry,
        bool preserveSyncId = true)
    {
        return new SequenceFrameManifestEntry
        {
            SyncId = preserveSyncId ? entry.SyncId : Guid.NewGuid().ToString("N"),
            RelativePath = entry.RelativePath,
            IsBlank = entry.IsBlank,
            DurationFrames = entry.DurationFrames,
            VoiceRelativePath = entry.VoiceRelativePath
        };
    }

    private static string ValidateVoicePath(CharacterCard character, string voiceFilePath)
    {
        if (!VoiceMaterialService.IsWaveFile(voiceFilePath))
        {
            throw new InvalidDataException("序列帧只能绑定当前角色已有的有效 WAV 语音。");
        }

        var root = Path.GetFullPath(character.FolderPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(voiceFilePath);
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("语音文件不在当前角色目录中，不能绑定到序列帧。");
        }

        return fullPath;
    }

    private static string ToCharacterRelativePath(CharacterCard character, string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return string.Empty;
        }

        return NormalizeRelativePath(Path.GetRelativePath(character.FolderPath, Path.GetFullPath(filePath)));
    }

    private static string ResolveVoicePath(CharacterCard character, string? relativePath)
    {
        return string.IsNullOrWhiteSpace(relativePath)
            ? string.Empty
            : Path.GetFullPath(Path.Combine(character.FolderPath, NormalizeRelativePath(relativePath)));
    }

    private static string ComputeFileHash(string sourcePath)
    {
        using var stream = File.OpenRead(sourcePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private void PruneUnreferencedFrameFiles(CharacterCard character, SequenceFrameAction action, SequenceFrameManifest manifest)
    {
        _ = manifest;
        var referenced = EnumerateReferencedFramePaths(character)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var framesFolderPath = GetFramesFolderPath(character, action);
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

    private IEnumerable<string> EnumerateReferencedFramePaths(CharacterCard character)
    {
        var materialFolderPath = Path.Combine(character.FolderPath, ZdMaterialFolderName);
        if (!Directory.Exists(materialFolderPath))
        {
            yield break;
        }

        foreach (var manifestPath in Directory.EnumerateFiles(materialFolderPath, ManifestFileName, SearchOption.AllDirectories))
        {
            SequenceFrameManifest? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize(
                    File.ReadAllText(manifestPath, Encoding.UTF8),
                    AppJsonSerializerContext.Default.SequenceFrameManifest);
            }
            catch
            {
                continue;
            }

            if (manifest?.Frames is null)
            {
                continue;
            }

            var actionFolderPath = Path.GetDirectoryName(manifestPath);
            if (string.IsNullOrWhiteSpace(actionFolderPath))
            {
                continue;
            }

            foreach (var entry in manifest.Frames)
            {
                if (entry is null || entry.IsBlank || string.IsNullOrWhiteSpace(entry.RelativePath))
                {
                    continue;
                }

                yield return Path.GetFullPath(Path.Combine(actionFolderPath, NormalizeRelativePath(entry.RelativePath)));
            }
        }
    }

    private string ToManifestRelativePath(CharacterCard character, SequenceFrameAction action, string filePath)
    {
        var actionFolderPath = Path.GetFullPath(GetActionFolderPath(character, action));
        var fullPath = Path.GetFullPath(filePath);
        if (fullPath.StartsWith(actionFolderPath, StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeRelativePath(Path.GetRelativePath(actionFolderPath, fullPath));
        }

        return ImportSourceIntoPool(character, action, filePath);
    }

    private static string ToPoolRelativePath(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        return NormalizeRelativePath(Path.Combine(FramesFolderName, fileName));
    }

    private string ResolveManifestPath(CharacterCard character, SequenceFrameAction action, string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        return Path.GetFullPath(Path.Combine(GetActionFolderPath(character, action), normalized));
    }

    private string GetFramesFolderPath(CharacterCard character, SequenceFrameAction action)
    {
        return Path.Combine(GetActionFolderPath(character, action), FramesFolderName);
    }

    private string GetManifestPath(CharacterCard character, SequenceFrameAction action)
    {
        return Path.Combine(GetActionFolderPath(character, action), ManifestFileName);
    }

    private static string NormalizeRelativePath(string value)
    {
        return value.Replace('\\', '/').TrimStart('/');
    }

    private static int ResolveIndex(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var tail = name.Split('-', '_').LastOrDefault();
        return int.TryParse(tail, out var index) ? index : 0;
    }

    public static bool IsSupportedImage(string path)
    {
        return SupportedImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }

    private static SequenceFramesData NormalizeData(SequenceFramesData? data)
    {
        data ??= new SequenceFramesData();
        data.ActionSettings ??= [];
        data.DuplicateCheckMaterialCount = Math.Max(-1, data.DuplicateCheckMaterialCount);
        data.DuplicateCheckDuplicateCount = Math.Max(0, data.DuplicateCheckDuplicateCount);
        data.DuplicateContentHashes = new Dictionary<string, SequenceFrameDuplicateHashEntry>(
            data.DuplicateContentHashes ?? [],
            StringComparer.OrdinalIgnoreCase);
        foreach (var settings in data.ActionSettings)
        {
            settings.Fps = Math.Clamp(settings.Fps, 1, 60);
        }

        return data;
    }
}
