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
using System.Threading;

namespace CrossingVoidZDTool.Services;

internal sealed class SequenceFrameService
{
    public const int RequiredWidth = 928;
    public const int RequiredHeight = 640;
    public const int DefaultFps = 12;

    private const string ZdMaterialFolderName = "ZDMaterial";
    private const string FramesFolderName = "Frames";
    private const string ManifestFileName = "sequence.json";
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
        new("防御", "Defense", false),
        new("闪避", "Dodge", false),
        new("坠落", "Flydown", false),
        new("飞行", "Flying", false),
        new("击飞", "Flystart", false),
        new("站街", "Idle", false),
        new("落地", "Land", false),
        new("移动", "Move", false),
        new("受伤", "Ondm", false),
        new("站起", "Standup", false),
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

    public IReadOnlyList<SequenceFrameSection> LoadSections(CharacterCard character, CharacterSkillsData skillsData, CancellationToken cancellationToken = default)
    {
        var actions = BuildActions(skillsData, _formService.GetFormLimit(character));
        return actions.Select(action => LoadSection(character, action, cancellationToken)).ToList();
    }

    public IReadOnlyList<SequenceFrameItem> ImportFrames(CharacterCard character, SequenceFrameAction action, IReadOnlyList<string> sourceFilePaths)
    {
        var supported = sourceFilePaths
            .Where(path => File.Exists(path) && IsSupportedImage(path))
            .ToList();
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

    public void RestoreActionFrames(CharacterCard character, SequenceFrameAction action, IReadOnlyList<string> snapshotFilePaths)
    {
        var folderPath = GetActionFolderPath(character, action);
        Directory.CreateDirectory(folderPath);
        ClearActionFolder(folderPath);
        var manifest = CreateManifest(action);
        foreach (var path in snapshotFilePaths.Where(File.Exists))
        {
            if (string.Equals(Path.GetExtension(path), ".blank", StringComparison.OrdinalIgnoreCase))
            {
                manifest.Frames.Add(new SequenceFrameManifestEntry { IsBlank = true });
                continue;
            }

            if (!IsSupportedImage(path))
            {
                continue;
            }

            manifest.Frames.Add(new SequenceFrameManifestEntry
            {
                RelativePath = ImportSourceIntoPool(character, action, path)
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
        for (var index = 0; index < manifest.Frames.Count; index++)
        {
            var entry = manifest.Frames[index];
            if (entry.IsBlank)
            {
                var blankPath = Path.Combine(snapshotFolderPath, $"{(index + 1).ToString().PadLeft(3, '0')}.blank");
                File.WriteAllText(blankPath, "blank", Encoding.UTF8);
                snapshotPaths.Add(blankPath);
                continue;
            }

            var sourcePath = ResolveManifestPath(character, action, entry.RelativePath);
            if (!File.Exists(sourcePath) || !IsSupportedImage(sourcePath))
            {
                continue;
            }

            var extension = Path.GetExtension(sourcePath);
            var targetPath = Path.Combine(snapshotFolderPath, $"{(index + 1).ToString().PadLeft(3, '0')}{extension}");
            File.Copy(sourcePath, targetPath, overwrite: true);
            snapshotPaths.Add(targetPath);
        }

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

        manifest.Frames.Insert(index + 1, frame.IsBlank
            ? new SequenceFrameManifestEntry { IsBlank = true }
            : new SequenceFrameManifestEntry { RelativePath = ToManifestRelativePath(character, action, frame.FilePath) });
        SaveManifest(character, action, manifest);
        return LoadSection(character, action, CancellationToken.None).Frames;
    }

    public IReadOnlyList<SequenceFrameItem> InsertBlankFrame(CharacterCard character, SequenceFrameAction action, SequenceFrameItem? afterFrame)
    {
        var manifest = LoadOrCreateManifest(character, action);
        var index = afterFrame is null ? manifest.Frames.Count - 1 : ResolveManifestIndex(manifest, afterFrame);
        if (index < -1)
        {
            index = manifest.Frames.Count - 1;
        }

        manifest.Frames.Insert(index + 1, new SequenceFrameManifestEntry { IsBlank = true });
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
            if (frame.IsBlank)
            {
                manifest.Frames.Add(new SequenceFrameManifestEntry { IsBlank = true });
                continue;
            }

            var existingEntry = sourceEntries.ElementAtOrDefault(frame.Index - 1);
            manifest.Frames.Add(new SequenceFrameManifestEntry
            {
                RelativePath = existingEntry is not null
                    ? existingEntry.RelativePath
                    : ToManifestRelativePath(character, action, frame.FilePath)
            });
        }

        SaveManifest(character, action, manifest);
        PruneUnreferencedFrameFiles(character, action, manifest);
        return LoadSection(character, action, CancellationToken.None).Frames;
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
                    return CreateBlankFrameItem(index + 1);
                }

                return CreateFrameItem(
                    ResolveManifestPath(character, action, entry.RelativePath),
                    index + 1,
                    manifest.Frames.Count);
            })
            .Where(frame => frame.IsBlank || File.Exists(frame.FilePath))
            .OrderBy(frame => frame.Index)
            .ToList();

        var invalidCount = frames.Count(frame => !frame.IsValid);
        var statusText = frames.Count == 0
            ? "未设置"
            : invalidCount > 0
                ? $"{frames.Count} 张，{invalidCount} 张尺寸不合规"
                : $"{frames.Count} 张，尺寸合规";
        return new SequenceFrameSection(action, frames, statusText, frames.Count == 0 || invalidCount > 0);
    }

    private static SequenceFrameItem CreateFrameItem(string path, int sequenceIndex, int sequenceCount)
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
            $"{sequenceIndex.ToString().PadLeft(Math.Max(3, sequenceCount.ToString().Length), '0')}  {info.Name}",
            $"{info.FullName}|{version}",
            sequenceIndex,
            actualWidth,
            actualHeight,
            actualWidth == RequiredWidth && actualHeight == RequiredHeight,
            info.LastWriteTime);
    }

    private static SequenceFrameItem CreateBlankFrameItem(int sequenceIndex)
    {
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
            true);
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
        manifest.ActionCode = string.IsNullOrWhiteSpace(manifest.ActionCode) ? action.Code : manifest.ActionCode;
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

    private string ImportSourceIntoPool(CharacterCard character, SequenceFrameAction action, string sourcePath)
    {
        var framesFolderPath = GetFramesFolderPath(character, action);
        Directory.CreateDirectory(framesFolderPath);
        var hash = ComputeFileHash(sourcePath);
        var fileName = $"{character.Code}-{action.Code}-{hash[..16]}.png";
        var targetPath = Path.Combine(framesFolderPath, fileName);
        if (!File.Exists(targetPath))
        {
            SaveAsPng(sourcePath, targetPath);
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
        foreach (var settings in data.ActionSettings)
        {
            settings.Fps = Math.Clamp(settings.Fps, 1, 60);
        }

        return data;
    }
}
