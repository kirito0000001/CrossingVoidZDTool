using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CrossingVoidZDTool.Services;

internal sealed class VoiceMaterialService
{
    private const string SoundFolderName = "Sound";
    private static readonly string PlaybackCacheFolderPath = Path.Combine(
        Path.GetTempPath(),
        "CrossingVoidZDTool",
        "VoicePreview");

    public static IReadOnlyList<VoiceMaterialSpec> Specs { get; } =
    [
        new(VoiceMaterialKind.Formation, "编队语音", "Formation", "Formation", true),
        new(VoiceMaterialKind.Click, "点击语音", "Click", "Click", true),
        new(VoiceMaterialKind.Hurt, "受伤语音", "Hurt", "Hurt", true),
        new(VoiceMaterialKind.Death, "死亡语音", "Death", "Death", true),
        new(VoiceMaterialKind.Defeat, "失败语音", "Defeat", "Defeat", true),
        new(VoiceMaterialKind.Victory, "胜利语音", "Victory", "Victory", true),
        new(VoiceMaterialKind.Skill1, "一技能语音", "Skill1", "Skill1", false),
        new(VoiceMaterialKind.Skill2, "二技能语音", "Skill2", "Skill2", false),
        new(VoiceMaterialKind.Ultimate, "终结技语音", "Ultimate", "Ultimate", false),
        new(VoiceMaterialKind.Support, "护援技语音", "Support", "Support", false),
        new(VoiceMaterialKind.Combo, "连携技语音", "Combo", "Combo", false),
        new(VoiceMaterialKind.Other, "待分配语音", "Other", "OtherVoice", false),
        new(VoiceMaterialKind.SoundEffect, "音效", "SoundEffect", "SE", false)
    ];

    public IReadOnlyList<VoiceMaterialSection> LoadSections(CharacterCard character)
    {
        Directory.CreateDirectory(GetSoundFolderPath(character));
        return Specs.Select(spec => LoadSection(character, spec)).ToList();
    }

    public VoiceMaterialItem Import(
        CharacterCard character,
        VoiceMaterialKind kind,
        string sourceFilePath)
    {
        return ImportMany(character, kind, [sourceFilePath]).Single();
    }

    public IReadOnlyList<VoiceMaterialItem> ImportMany(
        CharacterCard character,
        VoiceMaterialKind kind,
        IReadOnlyList<string> sourceFilePaths)
    {
        var spec = GetSpec(kind);
        foreach (var sourceFilePath in sourceFilePaths)
        {
            ValidateWaveFile(sourceFilePath);
        }

        var folderPath = GetCategoryFolderPath(character, kind);
        Directory.CreateDirectory(folderPath);
        NormalizeCategoryFileNames(character, spec);
        var nextIndex = GetNextIndex(character, spec);
        var totalCount = nextIndex - 1 + sourceFilePaths.Count;
        NormalizeCategoryFileNames(character, spec, totalCount);
        var imported = new List<VoiceMaterialItem>(sourceFilePaths.Count);
        foreach (var sourceFilePath in sourceFilePaths)
        {
            var targetPath = Path.Combine(
                folderPath,
                BuildFileName(
                    character.Code,
                    spec,
                    nextIndex++,
                    totalCount,
                    Path.GetFileNameWithoutExtension(sourceFilePath)));
            CopyFile(sourceFilePath, targetPath);
            imported.Add(CreateItem(character.Code, spec, targetPath, totalCount));
        }

        return imported;
    }

    public IReadOnlyList<VoiceMaterialItem> ReplaceWithVoices(
        CharacterCard character,
        VoiceMaterialKind kind,
        IReadOnlyList<string> sourceFilePaths)
    {
        foreach (var sourceFilePath in sourceFilePaths)
        {
            ValidateWaveFile(sourceFilePath);
        }

        var spec = GetSpec(kind);
        var previousPaths = NormalizeCategoryFileNames(character, spec).ToArray();
        var previousIndexes = previousPaths.ToDictionary(
            path => path,
            path => ResolveIndex(character.Code, spec, Path.GetFileName(path)),
            StringComparer.OrdinalIgnoreCase);
        foreach (var previousPath in previousPaths)
        {
            File.Delete(previousPath);
        }

        var imported = ImportMany(character, kind, sourceFilePaths);
        var importedByIndex = imported.ToDictionary(item => item.Index);
        var remap = previousIndexes.ToDictionary(
            item => Path.GetFullPath(item.Key),
            item => importedByIndex.TryGetValue(item.Value, out var replacement)
                ? Path.GetFullPath(replacement.FilePath)
                : null,
            StringComparer.OrdinalIgnoreCase);
        if (remap.Count > 0)
        {
            SequenceFrameService.RemapVoiceReferences(character, remap);
        }

        return imported;
    }

    public VoiceMaterialItem Replace(
        CharacterCard character,
        VoiceMaterialKind kind,
        int index,
        string sourceFilePath)
    {
        ValidateWaveFile(sourceFilePath);
        var spec = GetSpec(kind);
        if (index < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var folderPath = GetCategoryFolderPath(character, kind);
        Directory.CreateDirectory(folderPath);
        var normalizedPaths = NormalizeCategoryFileNames(character, spec);
        var targetPath = normalizedPaths
            .FirstOrDefault(path => ResolveIndex(character.Code, spec, Path.GetFileName(path)) == index)
            ?? throw new FileNotFoundException("没有找到要替换的语音素材。");
        CopyFile(sourceFilePath, targetPath);
        return CreateItem(character.Code, spec, targetPath, normalizedPaths.Count);
    }

    public void Delete(CharacterCard character, VoiceMaterialKind kind, int index)
    {
        var spec = GetSpec(kind);
        var normalizedPaths = NormalizeCategoryFileNames(character, spec);
        var targetPath = normalizedPaths
            .FirstOrDefault(path => ResolveIndex(character.Code, spec, Path.GetFileName(path)) == index);
        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
            SequenceFrameService.RemapVoiceReferences(character, new Dictionary<string, string?>
            {
                [Path.GetFullPath(targetPath)] = null
            });
            NormalizeCategoryFileNames(character, spec);
        }
    }

    public void Delete(CharacterCard character, VoiceMaterialItem item)
    {
        var spec = GetSpec(item.Kind);
        var categoryFolder = Path.GetFullPath(GetCategoryFolderPath(character, item.Kind));
        var normalizedPaths = NormalizeCategoryFileNames(character, spec);
        var filePath = normalizedPaths
            .FirstOrDefault(path => ResolveIndex(character.Code, spec, Path.GetFileName(path)) == item.Index)
            ?? Path.GetFullPath(item.FilePath);
        filePath = Path.GetFullPath(filePath);
        var folderPrefix = categoryFolder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!filePath.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("语音文件不在当前角色的分类目录中，拒绝删除。");
        }

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            SequenceFrameService.RemapVoiceReferences(character, new Dictionary<string, string?>
            {
                [filePath] = null
            });
            NormalizeCategoryFileNames(character, spec);
        }
    }

    public void DeletePendingVoices(
        CharacterCard character,
        IReadOnlyList<VoiceMaterialItem> items)
    {
        var selectedItems = items
            .Where(item => item.Kind == VoiceMaterialKind.Other)
            .GroupBy(item => item.Index)
            .Select(group => group.First())
            .ToArray();
        if (selectedItems.Length != items.Count)
        {
            throw new InvalidOperationException("只能批量删除待分配语音中的素材。");
        }

        if (selectedItems.Length == 0)
        {
            return;
        }

        var spec = GetSpec(VoiceMaterialKind.Other);
        var normalizedPaths = NormalizeCategoryFileNames(character, spec);
        var selectedPaths = selectedItems
            .Select(item => normalizedPaths.FirstOrDefault(path =>
                ResolveIndex(character.Code, spec, Path.GetFileName(path)) == item.Index))
            .ToArray();
        if (selectedPaths.Any(string.IsNullOrWhiteSpace))
        {
            throw new FileNotFoundException("部分待分配语音已经不存在，请刷新后重试。");
        }

        var deletedPaths = selectedPaths.Select(path => Path.GetFullPath(path!)).ToArray();
        foreach (var path in deletedPaths)
        {
            File.Delete(path);
        }

        SequenceFrameService.RemapVoiceReferences(
            character,
            deletedPaths.ToDictionary(
                path => path,
                _ => (string?)null,
                StringComparer.OrdinalIgnoreCase));
        NormalizeCategoryFileNames(character, spec);
    }

    public IReadOnlyList<VoiceMaterialItem> MovePendingVoices(
        CharacterCard character,
        IReadOnlyList<VoiceMaterialItem> items,
        VoiceMaterialKind targetKind)
    {
        if (targetKind == VoiceMaterialKind.Other)
        {
            throw new InvalidOperationException("待分配语音不能移动到自身。");
        }

        var selectedItems = items
            .Where(item => item.Kind == VoiceMaterialKind.Other)
            .GroupBy(item => item.Index)
            .Select(group => group.First())
            .ToArray();
        if (selectedItems.Length != items.Count)
        {
            throw new InvalidOperationException("只能分配待分配语音中的素材。");
        }

        if (selectedItems.Length == 0)
        {
            return [];
        }

        var sourceSpec = GetSpec(VoiceMaterialKind.Other);
        var targetSpec = GetSpec(targetKind);
        var sourcePaths = NormalizeCategoryFileNames(character, sourceSpec);
        var selectedPaths = selectedItems
            .Select(item => sourcePaths.FirstOrDefault(path =>
                ResolveIndex(character.Code, sourceSpec, Path.GetFileName(path)) == item.Index))
            .ToArray();
        if (selectedPaths.Any(string.IsNullOrWhiteSpace))
        {
            throw new FileNotFoundException("部分待分配语音已经不存在，请刷新后重试。");
        }

        var targetPaths = NormalizeCategoryFileNames(character, targetSpec);
        var targetTotalCount = targetPaths.Count + selectedPaths.Length;
        NormalizeCategoryFileNames(character, targetSpec, targetTotalCount);
        var targetFolderPath = GetCategoryFolderPath(character, targetKind);
        var moves = selectedPaths
            .Select((sourcePath, position) => new MaterialPathRename(
                sourcePath!,
                Path.Combine(
                    targetFolderPath,
                    BuildFileName(
                        character.Code,
                        targetSpec,
                        targetPaths.Count + position + 1,
                        targetTotalCount))))
            .ToArray();
        var appliedMoves = MaterialSequenceNaming.RenameFilesAtomically(moves);
        RemapToolboxIdentities(character, appliedMoves);
        SequenceFrameService.RemapVoiceReferences(
            character,
            appliedMoves.ToDictionary(
                move => move.SourcePath,
                move => (string?)move.TargetPath,
                StringComparer.OrdinalIgnoreCase));
        NormalizeCategoryFileNames(character, sourceSpec);

        return appliedMoves
            .Select(move => CreateItem(
                character.Code,
                targetSpec,
                move.TargetPath,
                targetTotalCount))
            .ToArray();
    }

    public IReadOnlyList<VoiceMaterialDuplicateMatch> FindPendingVoiceDuplicates(CharacterCard character)
    {
        var sections = LoadSections(character);
        var pendingItems = sections
            .Single(section => section.Spec.Kind == VoiceMaterialKind.Other)
            .Items
            .Where(item => item.CanPlay)
            .ToArray();
        var allItems = sections
            .SelectMany(section => section.Items)
            .Where(item => item.CanPlay)
            .ToArray();
        var groups = new Dictionary<string, List<VoiceMaterialItem>>(StringComparer.Ordinal);
        foreach (var item in allItems)
        {
            var fingerprint = TryCreateWaveAudioFingerprint(item.FilePath);
            if (fingerprint is null)
            {
                continue;
            }

            if (!groups.TryGetValue(fingerprint, out var group))
            {
                group = [];
                groups.Add(fingerprint, group);
            }

            group.Add(item);
        }

        var matches = new List<VoiceMaterialDuplicateMatch>();
        foreach (var pendingItem in pendingItems)
        {
            var fingerprint = TryCreateWaveAudioFingerprint(pendingItem.FilePath);
            if (fingerprint is null || !groups.TryGetValue(fingerprint, out var group))
            {
                continue;
            }

            var assignedMatches = group
                .Where(item => item.Kind != VoiceMaterialKind.Other)
                .ToArray();
            var pendingMatches = group
                .Where(item =>
                    item.Kind == VoiceMaterialKind.Other &&
                    !string.Equals(item.FilePath, pendingItem.FilePath, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (assignedMatches.Length > 0 || pendingMatches.Length > 0)
            {
                matches.Add(new VoiceMaterialDuplicateMatch(
                    pendingItem,
                    assignedMatches,
                    pendingMatches));
            }
        }

        return matches;
    }

    public string GetCategoryFolderPath(CharacterCard character, VoiceMaterialKind kind)
    {
        return Path.Combine(GetSoundFolderPath(character), GetSpec(kind).FolderName);
    }

    public static VoiceMaterialSpec GetSpec(VoiceMaterialKind kind)
    {
        return Specs.First(spec => spec.Kind == kind);
    }

    internal static string CreatePlaybackCopy(string sourcePath)
    {
        ValidateWaveFile(sourcePath);
        Directory.CreateDirectory(PlaybackCacheFolderPath);
        var previewPath = Path.Combine(PlaybackCacheFolderPath, $"{Guid.NewGuid():N}.wav");
        File.Copy(sourcePath, previewPath, overwrite: false);
        return previewPath;
    }

    internal static void TryDeletePlaybackCopy(string? previewPath)
    {
        if (string.IsNullOrWhiteSpace(previewPath))
        {
            return;
        }

        var fullCachePath = Path.GetFullPath(PlaybackCacheFolderPath)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPreviewPath = Path.GetFullPath(previewPath);
        if (!fullPreviewPath.StartsWith(fullCachePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            File.Delete(fullPreviewPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    internal static void CleanupPlaybackCopies()
    {
        if (!Directory.Exists(PlaybackCacheFolderPath))
        {
            return;
        }

        foreach (var previewPath in Directory.EnumerateFiles(PlaybackCacheFolderPath, "*.wav"))
        {
            TryDeletePlaybackCopy(previewPath);
        }
    }

    public static bool IsWaveFile(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(path))
        {
            return false;
        }

        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length < 12)
            {
                return false;
            }

            Span<byte> header = stackalloc byte[12];
            return stream.Read(header) == header.Length &&
                   header[..4].SequenceEqual(Encoding.ASCII.GetBytes("RIFF")) &&
                   header[8..12].SequenceEqual(Encoding.ASCII.GetBytes("WAVE"));
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

    private static VoiceMaterialSection LoadSection(CharacterCard character, VoiceMaterialSpec spec)
    {
        var folderPath = Path.Combine(GetSoundFolderPath(character), spec.FolderName);
        Directory.CreateDirectory(folderPath);
        var normalizedPaths = NormalizeCategoryFileNames(character, spec);
        var itemPaths = Directory
            .EnumerateFiles(folderPath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var items = itemPaths
            .Select(path => CreateItem(character.Code, spec, path, normalizedPaths.Count))
            .OrderBy(item => item.Index)
            .ThenBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var readyCount = items.Count(item => item.Status == VoiceMaterialStatus.Ready);
        var invalidCount = items.Count(item => item.Status == VoiceMaterialStatus.Invalid);
        var statusText = readyCount == 0
            ? spec.IsRequired ? "未设置，必须导入 1 个 WAV" : "未设置，可留空"
            : $"已设置 {readyCount} 个";
        if (invalidCount > 0)
        {
            statusText += $"，{invalidCount} 个文件不合规";
        }

        return new VoiceMaterialSection(spec, items, statusText);
    }

    private static VoiceMaterialItem CreateItem(
        string characterCode,
        VoiceMaterialSpec spec,
        string path,
        int totalCount)
    {
        var fileName = Path.GetFileName(path);
        var index = ResolveIndex(characterCode, spec, fileName);
        var validName = index > 0 && (spec.Kind == VoiceMaterialKind.Other
            ? UnclassifiedMaterialNaming.TryParse(
                characterCode,
                GetFileMarkers(spec),
                fileName,
                out _,
                out _)
            : fileName.Equals(
                BuildFileName(characterCode, spec, index, totalCount),
                StringComparison.OrdinalIgnoreCase));
        var isReady = validName && IsWaveFile(path);
        return new VoiceMaterialItem(
            spec.Kind,
            spec.DisplayName,
            path,
            fileName,
            index,
            isReady ? VoiceMaterialStatus.Ready : VoiceMaterialStatus.Invalid,
            isReady ? "WAV 格式正常" : "文件名或 WAV 格式不合规",
            File.GetLastWriteTime(path));
    }

    private static int GetNextIndex(CharacterCard character, VoiceMaterialSpec spec)
    {
        var folderPath = Path.Combine(GetSoundFolderPath(character), spec.FolderName);
        if (!Directory.Exists(folderPath))
        {
            return 1;
        }

        return Directory
            .EnumerateFiles(folderPath)
            .Select(path => ResolveIndex(character.Code, spec, Path.GetFileName(path)))
            .Where(index => index > 0)
            .DefaultIfEmpty(0)
            .Max() + 1;
    }

    private static int ResolveIndex(string characterCode, VoiceMaterialSpec spec, string fileName)
    {
        if (spec.Kind == VoiceMaterialKind.Other &&
            UnclassifiedMaterialNaming.TryParse(
                characterCode,
                GetFileMarkers(spec),
                fileName,
                out var unclassifiedIndex,
                out _))
        {
            return unclassifiedIndex;
        }

        var expectedLegacyFirstName = $"{characterCode}-{spec.FileSuffix}.wav";
        if (fileName.Equals(expectedLegacyFirstName, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        var prefix = $"{characterCode}-{spec.FileSuffix}-";
        if (!string.Equals(Path.GetExtension(fileName), ".wav", StringComparison.OrdinalIgnoreCase) ||
            !fileNameWithoutExtension.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(fileNameWithoutExtension[prefix.Length..], out var index))
        {
            return 0;
        }

        return index;
    }

    private static IReadOnlyList<string> NormalizeCategoryFileNames(
        CharacterCard character,
        VoiceMaterialSpec spec,
        int? targetTotalCount = null)
    {
        var folderPath = Path.Combine(GetSoundFolderPath(character), spec.FolderName);
        Directory.CreateDirectory(folderPath);
        var files = Directory
            .EnumerateFiles(folderPath)
            .Where(path => string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase))
            .Select(path => new
            {
                Path = Path.GetFullPath(path),
                ParsedIndex = ResolveIndex(character.Code, spec, Path.GetFileName(path))
            })
            .OrderBy(file => file.ParsedIndex <= 0 ? int.MaxValue : file.ParsedIndex)
            .ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var totalCount = Math.Max(files.Count, targetTotalCount ?? files.Count);
        var renames = files
            .Select((file, position) =>
            {
                var originalName = spec.Kind == VoiceMaterialKind.Other
                    ? UnclassifiedMaterialNaming.ResolveOriginalName(
                        character.Code,
                        GetFileMarkers(spec),
                        file.Path)
                    : null;
                return new MaterialPathRename(
                    file.Path,
                    Path.Combine(
                        folderPath,
                        BuildFileName(character.Code, spec, position + 1, totalCount, originalName)));
            })
            .ToList();
        var appliedRenames = MaterialSequenceNaming.RenameFilesAtomically(renames);
        if (appliedRenames.Count > 0)
        {
            RemapToolboxIdentities(character, appliedRenames);
            SequenceFrameService.RemapVoiceReferences(
                character,
                appliedRenames.ToDictionary(
                    rename => rename.SourcePath,
                    rename => (string?)rename.TargetPath,
                    StringComparer.OrdinalIgnoreCase));
        }

        return renames.Select(rename => rename.TargetPath).ToList();
    }

    private static void RemapToolboxIdentities(
        CharacterCard character,
        IReadOnlyList<MaterialPathRename> renames)
    {
        var identityService = new UnrealBridgeToolboxIdentityService();
        foreach (var rename in renames)
        {
            identityService.RemapPath(character, rename.SourcePath, rename.TargetPath);
        }
    }

    private static string BuildFileName(
        string characterCode,
        VoiceMaterialSpec spec,
        int index,
        int totalCount,
        string? originalName = null)
    {
        return spec.Kind == VoiceMaterialKind.Other
            ? UnclassifiedMaterialNaming.BuildFileName(
                characterCode,
                spec.FileSuffix,
                index,
                totalCount,
                originalName,
                ".wav")
            : $"{characterCode}-{spec.FileSuffix}-{MaterialSequenceNaming.FormatIndex(index, totalCount)}.wav";
    }

    private static IReadOnlyList<string> GetFileMarkers(VoiceMaterialSpec spec)
    {
        return spec.Kind == VoiceMaterialKind.Other
            ? [spec.FileSuffix, "Other"]
            : [spec.FileSuffix];
    }

    private static string? TryCreateWaveAudioFingerprint(string path)
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
            if (stream.Length < 12 ||
                Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF")
            {
                return null;
            }

            reader.ReadUInt32();
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE")
            {
                return null;
            }

            byte[]? formatData = null;
            using var audioHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var hasAudioData = false;
            var buffer = new byte[81920];
            while (stream.Position + 8 <= stream.Length)
            {
                var chunkId = Encoding.ASCII.GetString(reader.ReadBytes(4));
                var chunkSize = reader.ReadUInt32();
                if (chunkSize > stream.Length - stream.Position)
                {
                    return null;
                }

                if (chunkId == "fmt ")
                {
                    if (chunkSize > 1024 * 1024)
                    {
                        return null;
                    }

                    if (formatData is null)
                    {
                        formatData = reader.ReadBytes((int)chunkSize);
                        if (formatData.Length != chunkSize)
                        {
                            return null;
                        }
                    }
                    else
                    {
                        stream.Seek(chunkSize, SeekOrigin.Current);
                    }
                }
                else if (chunkId == "data")
                {
                    hasAudioData = true;
                    var remaining = (long)chunkSize;
                    while (remaining > 0)
                    {
                        var read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                        if (read == 0)
                        {
                            return null;
                        }

                        audioHash.AppendData(buffer, 0, read);
                        remaining -= read;
                    }
                }
                else
                {
                    stream.Seek(chunkSize, SeekOrigin.Current);
                }

                if ((chunkSize & 1) != 0 && stream.Position < stream.Length)
                {
                    stream.Seek(1, SeekOrigin.Current);
                }
            }

            if (formatData is null || !hasAudioData)
            {
                return null;
            }

            var formatHash = SHA256.HashData(formatData);
            var dataHash = audioHash.GetHashAndReset();
            return $"{Convert.ToHexString(formatHash)}:{Convert.ToHexString(dataHash)}";
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string GetSoundFolderPath(CharacterCard character)
    {
        return Path.Combine(character.FolderPath, SoundFolderName);
    }

    private static void ValidateWaveFile(string path)
    {
        if (!IsWaveFile(path))
        {
            throw new InvalidDataException("仅支持具有有效 RIFF/WAVE 文件头的 .wav 文件。");
        }
    }

    private static void CopyFile(string sourcePath, string targetPath)
    {
        if (string.Equals(
            Path.GetFullPath(sourcePath),
            Path.GetFullPath(targetPath),
            StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        File.Copy(sourcePath, targetPath, overwrite: true);
    }
}
