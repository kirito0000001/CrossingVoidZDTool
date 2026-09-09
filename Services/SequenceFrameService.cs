using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 序列帧这一摊的门面：对界面暴露「一个角色有哪些动作、每个动作有哪些帧格、
/// 以及能对帧格做的那些编辑操作」，具体活分给四个下层小类干。
///
/// 原本这是个 1400 行的大类，路径推导、清单 JSON、帧文件删改、界面用的帧格组装、
/// 快照备份全挤在一起——其中会删用户文件的那几段和纯计算的那几段没有任何界线，
/// 改一处编辑操作要先确认自己没碰到删除路径。现在按职责拆成：
/// <list type="bullet">
/// <item><see cref="SequenceActionFolderLayout"/>：动作目录布局与相对/绝对路径互转</item>
/// <item><see cref="SequenceManifestStore"/>：sequence.json 的读写、规范化、引用集枚举</item>
/// <item><see cref="SequenceFramePool"/>：帧池的文件操作（唯一会删用户文件的地方）</item>
/// <item><see cref="SequenceFrameSectionBuilder"/>：把清单摊成界面用的帧格列表（只读）</item>
/// <item><see cref="SequenceFrameSnapshotStore"/>：撤销用的动作快照</item>
/// </list>
/// 语音绑定的跨域重写在 <see cref="SequenceVoiceBindingService"/>，
/// 那是为了断开和 <see cref="VoiceMaterialService"/> 之间的循环依赖。
///
/// 留在这里的是编排：几乎每个编辑操作都是「读清单 → 改帧格 → 存清单 →（必要时）清冗余帧 → 重新组装帧格列表」。
/// </summary>
internal sealed class SequenceFrameService
{
    // 规格常量的实际定义在 SequenceFrameSpec，这里按原名转发：
    // 回归用例和四个 Unreal 侧服务都是按 SequenceFrameService.XXX 取值的。
    public const int RequiredWidth = SequenceFrameSpec.RequiredWidth;
    public const int RequiredHeight = SequenceFrameSpec.RequiredHeight;
    public const int DefaultFps = SequenceFrameSpec.DefaultFps;
    public const int MaxFrameDuration = SequenceFrameSpec.MaxFrameDuration;

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
            .Where(path => File.Exists(path) && SequenceFramePool.IsSupportedImage(path))
            .ToList());
        if (supported.Count == 0)
        {
            return [];
        }

        var folderPath = SequenceActionFolderLayout.GetActionFolderPath(character, action);
        Directory.CreateDirectory(folderPath);
        var previousFps = LoadOrCreateManifest(character, action).Fps;
        var framesFolderPath = SequenceActionFolderLayout.GetFramesFolderPath(character, action);
        Directory.CreateDirectory(framesFolderPath);
        SequenceFramePool.ClearActionFolder(folderPath);

        var manifest = SequenceManifestStore.Create(action);
        manifest.Fps = previousFps;
        for (var index = 0; index < supported.Count; index++)
        {
            var relativePath = SequenceFramePool.ImportSource(character, action, supported[index]);
            manifest.Frames.Add(new SequenceFrameManifestEntry { RelativePath = relativePath });
        }

        SequenceManifestStore.Save(character, action, manifest);
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
            .Where(frame => frame.IsBlank || File.Exists(frame.FilePath) && SequenceFramePool.IsSupportedImage(frame.FilePath))
            .ToList();
        if (validFrames.Count == 0)
        {
            return [];
        }

        var folderPath = SequenceActionFolderLayout.GetActionFolderPath(character, action);
        Directory.CreateDirectory(folderPath);
        Directory.CreateDirectory(SequenceActionFolderLayout.GetFramesFolderPath(character, action));
        SequenceFramePool.ClearActionFolder(folderPath);

        var manifest = SequenceManifestStore.Create(action);
        manifest.Fps = Math.Clamp(fps <= 0 ? DefaultFps : fps, 1, 60);
        foreach (var frame in validFrames)
        {
            manifest.Frames.Add(frame.IsBlank
                ? new SequenceFrameManifestEntry { IsBlank = true }
                : new SequenceFrameManifestEntry
                {
                    RelativePath = SequenceFramePool.ImportSource(character, action, frame.FilePath)
                });
        }

        SequenceManifestStore.Save(character, action, manifest);
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

        SequenceManifestStore.Save(character, action, manifest);
        SequenceFramePool.PruneUnreferenced(character, action);
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

        SequenceManifestStore.Save(character, action, manifest);
        SequenceFramePool.PruneUnreferenced(character, action);
        return LoadSection(character, action, CancellationToken.None).Frames;
    }

    public void RestoreActionFrames(CharacterCard character, SequenceFrameAction action, IReadOnlyList<string> snapshotFilePaths)
    {
        SequenceFrameSnapshotStore.Restore(character, action, snapshotFilePaths);
    }

    public IReadOnlyList<string> CreateActionSnapshot(CharacterCard character, SequenceFrameAction action)
    {
        return SequenceFrameSnapshotStore.Create(character, action, LoadOrCreateManifest(character, action));
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
                RelativePath = frame.IsBlank ? string.Empty : SequenceFramePool.ToManifestRelativePath(character, action, frame.FilePath),
                DurationFrames = Math.Clamp(frame.DurationFrames, 1, MaxFrameDuration),
                VoiceRelativePath = SequenceActionFolderLayout.ToCharacterRelativePath(character, frame.VoiceFilePath)
            }
            : SequenceManifestStore.CloneEntry(sourceEntry, preserveSyncId: false));
        SequenceManifestStore.Save(character, action, manifest);
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
            .Select(index => SequenceManifestStore.CloneEntry(manifest.Frames[index], preserveSyncId: false))
            .ToList();
        for (var index = 0; index < entries.Count; index++)
        {
            manifest.Frames.Insert(targetIndex + 1 + index, entries[index]);
        }

        SequenceManifestStore.Save(character, action, manifest);
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
        SequenceManifestStore.Save(character, action, manifest);
        return LoadSection(character, action, CancellationToken.None).Frames;
    }

    public IReadOnlyList<SequenceFrameItem> ReorderFrames(CharacterCard character, SequenceFrameAction action, IReadOnlyList<SequenceFrameItem> orderedFrames)
    {
        var existing = orderedFrames
            .Where(frame => frame.IsBlank || File.Exists(frame.FilePath) && SequenceFramePool.IsSupportedImage(frame.FilePath))
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
                ? SequenceManifestStore.CloneEntry(existingEntry)
                : new SequenceFrameManifestEntry
                {
                    IsBlank = frame.IsBlank,
                    RelativePath = frame.IsBlank ? string.Empty : SequenceFramePool.ToManifestRelativePath(character, action, frame.FilePath),
                    DurationFrames = Math.Clamp(frame.DurationFrames, 1, MaxFrameDuration),
                    VoiceRelativePath = SequenceActionFolderLayout.ToCharacterRelativePath(character, frame.VoiceFilePath)
                });
        }

        SequenceManifestStore.Save(character, action, manifest);
        SequenceFramePool.PruneUnreferenced(character, action);
        return LoadSection(character, action, CancellationToken.None).Frames;
    }

    public IReadOnlyList<SequenceFrameItem> ReplaceFrame(
        CharacterCard character,
        SequenceFrameAction action,
        SequenceFrameItem frame,
        string sourceFilePath)
    {
        if (!File.Exists(sourceFilePath) || !SequenceFramePool.IsSupportedImage(sourceFilePath))
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
        entry.RelativePath = SequenceFramePool.ImportSource(character, action, sourceFilePath);
        entry.IsBlank = false;
        SequenceManifestStore.Save(character, action, manifest);
        SequenceFramePool.PruneUnreferenced(character, action);
        return LoadSection(character, action, CancellationToken.None).Frames;
    }

    public IReadOnlyList<SequenceFrameItem> ReplaceFrameWithSources(
        CharacterCard character,
        SequenceFrameAction action,
        SequenceFrameItem frame,
        IReadOnlyList<string> sourceFilePaths)
    {
        if (sourceFilePaths.Count == 0 ||
            sourceFilePaths.Any(path => !File.Exists(path) || !SequenceFramePool.IsSupportedImage(path)))
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
            .Select(path => SequenceFramePool.ImportSource(character, action, path))
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

        SequenceManifestStore.Save(character, action, manifest);
        SequenceFramePool.PruneUnreferenced(character, action);
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
        SequenceManifestStore.Save(character, action, manifest);
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
            : SequenceActionFolderLayout.ToCharacterRelativePath(character, ValidateVoicePath(character, voiceFilePath));
        SequenceManifestStore.Save(character, action, manifest);
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

        SequenceManifestStore.Save(character, action, manifest);
    }

    /// <summary>
    /// 读取动作的帧率。每个动作的 sequence.json 是权威来源：
    /// ZDToolboxData 的 ActionSettings 可能缺项，也可能残留旧代号拼写（例如 Defense/Defence），
    /// 按字符串直接查会静默回落到默认帧率，发布出去的动画速度就是错的。
    /// </summary>
    public int GetActionFps(CharacterCard character, SequenceFrameAction action)
    {
        var manifest = LoadOrCreateManifest(character, action);
        return Math.Clamp(manifest.Fps <= 0 ? DefaultFps : manifest.Fps, 1, 60);
    }

    public void SetActionFps(CharacterCard character, SequenceFrameAction action, int fps)
    {
        var manifest = LoadOrCreateManifest(character, action);
        manifest.Fps = Math.Clamp(fps, 1, 60);
        SequenceManifestStore.Save(character, action, manifest);
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
            var actionFolderPath = SequenceActionFolderLayout.GetActionFolderPath(character, action);
            var keptRelativePath = SequenceActionFolderLayout.NormalizeRelativePath(Path.GetRelativePath(actionFolderPath, keptFullPath));
            var changed = false;
            foreach (var entry in manifest.Frames)
            {
                var resolvedPath = Path.GetFullPath(SequenceActionFolderLayout.ResolveManifestPath(character, action, entry.RelativePath));
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
                SequenceManifestStore.Save(character, action, manifest);
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

    /// <summary>界面上的「打开动作目录」要用。</summary>
    public string GetActionFolderPath(CharacterCard character, SequenceFrameAction action)
    {
        return SequenceActionFolderLayout.GetActionFolderPath(character, action);
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
        return SequenceFrameSectionBuilder.Build(
            character,
            action,
            LoadOrCreateManifest(character, action),
            cancellationToken);
    }

    /// <summary>
    /// 取一份可用的清单。这里把三段拼在一起：先按历史拼写把动作目录搬到规范名下，
    /// 再看有没有清单文件——没有就说明这是个还没升级过的老目录，交给帧池就地迁移。
    /// </summary>
    private SequenceFrameManifest LoadOrCreateManifest(CharacterCard character, SequenceFrameAction action)
    {
        var folderPath = SequenceActionFolderLayout.GetActionFolderPath(character, action);
        SequenceActionFolderLayout.MigrateLegacyActionFolderPath(character, action, folderPath);
        Directory.CreateDirectory(folderPath);
        Directory.CreateDirectory(SequenceActionFolderLayout.GetFramesFolderPath(character, action));
        if (!File.Exists(SequenceActionFolderLayout.GetManifestPath(character, action)))
        {
            return SequenceFramePool.MigrateLegacyActionFolder(character, action);
        }

        return SequenceManifestStore.LoadAndNormalize(character, action);
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
                SequenceActionFolderLayout.NormalizeRelativePath(item.entry.RelativePath),
                SequenceActionFolderLayout.NormalizeRelativePath(SequenceActionFolderLayout.ToPoolRelativePath(frame.FilePath)),
                StringComparison.OrdinalIgnoreCase))
            ?.index ?? -1;
    }

    private static string ValidateVoicePath(CharacterCard character, string voiceFilePath)
    {
        if (!WaveFileFormat.IsWaveFile(voiceFilePath))
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
