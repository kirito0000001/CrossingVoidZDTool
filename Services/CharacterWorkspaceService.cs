using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 角色工作区：扫描 Draft/Completed、加载角色卡、增删改名角色、草稿读写、元数据持久化。
///
/// 这个类原本还兼管备份、参考图和导出，一共六件事一千五百行。那三件事各自只认
/// 「一个角色目录」这一个输入，跟工作区怎么组织角色没有关系，已经分别搬到
/// <see cref="CharacterBackupService"/>、<see cref="CharacterReferenceImageService"/>、
/// <see cref="CharacterExportService"/>。留在这里的是真正回答「什么是一个角色」的那部分。
/// </summary>
internal sealed class CharacterWorkspaceService
{
    private const string ToolFolderName = CharacterFolderLayout.Tool;
    private const string AssetMaterialFolderName = CharacterFolderLayout.AssetMaterial;
    private const string ZDMaterialFolderName = CharacterFolderLayout.ZdMaterial;
    private const string SoundFolderName = CharacterFolderLayout.Sound;
    private const string ExAssetFolderName = CharacterFolderLayout.ExAsset;
    private const string BuffFolderName = CharacterFolderLayout.Buff;
    private const string ReferenceFolderName = CharacterFolderLayout.ReferenceImages;
    private const string MetadataFileName = "character.json";
    private const string ToolboxDataFileName = "ZDToolboxData.json";
    private const string LegacyDraftFileName = "St1-设计理念.txt";
    private const string DraftFolderName = CharacterFolderLayout.Draft;
    private const string CompletedFolderName = CharacterFolderLayout.Completed;
    private const string ExportFolderName = CharacterFolderLayout.Export;

    /// <summary>
    /// 备份已经搬到 <see cref="CharacterBackupService"/>。这里留一个实例，
    /// 只是因为 <see cref="UnrealBridgeDraftImportService"/> 的回滚路径还是拿着
    /// 工作区服务在调备份——新的调用点请直接用 <see cref="CharacterBackupService"/>。
    /// </summary>
    private readonly CharacterBackupService _backupService = new();

    /// <summary>导出同理：实现在 <see cref="CharacterExportService"/>，这里只剩回归用例还在用的两个转发。</summary>
    private readonly CharacterExportService _exportService = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly AppJsonSerializerContext JsonContext = new(JsonOptions);
    private static readonly JsonTypeInfo<CharacterMetadata> CharacterMetadataJsonTypeInfo = JsonContext.CharacterMetadata;

    public IReadOnlyList<CharacterCard> LoadCharacters(string projectRootPath, CancellationToken cancellationToken = default)
    {
        EnsureWorkspaceFolders(projectRootPath);
        var cards = new List<CharacterCard>();
        foreach (var stateFolder in new[]
        {
            (Path: GetDraftFolderPath(projectRootPath), IsCompleted: false),
            (Path: GetCompletedFolderPath(projectRootPath), IsCompleted: true)
        })
        {
            foreach (var directoryPath in Directory.EnumerateDirectories(stateFolder.Path)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var card = TryLoadCharacterCard(directoryPath)
                    ?? TryBuildFallbackCharacterCard(directoryPath, stateFolder.IsCompleted);
                if (card is null)
                {
                    continue;
                }

                if (card.IsCompleted != stateFolder.IsCompleted)
                {
                    throw new InvalidDataException(
                        $"角色 {card.Code} 的完成状态与所在目录不一致：{directoryPath}");
                }

                cards.Add(card);
            }
        }

        var duplicate = cards
            .GroupBy(card => card.Code, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new IOException($"Draft 和 Completed 中同时存在角色 {duplicate.Key}，请先保留其中一份。");
        }

        return cards
            .OrderByDescending(card => card.LastEditedAt)
            .ThenBy(card => card.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public CharacterCreationResult CreateCharacter(string projectRootPath, string characterName)
    {
        if (string.IsNullOrWhiteSpace(characterName))
        {
            throw new ArgumentException("角色名字不能为空。", nameof(characterName));
        }

        EnsureWorkspaceFolders(projectRootPath);
        var code = CreateUniqueCharacterCode(projectRootPath, characterName);
        var characterFolderPath = Path.Combine(GetDraftFolderPath(projectRootPath), code);
        var createdNewFolder = !Directory.Exists(characterFolderPath);
        Directory.CreateDirectory(characterFolderPath);
        EnsureCharacterFolders(characterFolderPath);

        var metadata = new CharacterMetadata
        {
            Code = code,
            Name = characterName.Trim(),
            LastEditedAt = DateTime.Now
        };
        SaveMetadata(characterFolderPath, metadata);

        EnsureDraftStoredInJson(characterFolderPath);

        return new CharacterCreationResult(BuildCharacterCard(characterFolderPath, metadata), createdNewFolder);
    }

    public CharacterCreationResult EnsureCharacterByCode(string projectRootPath, string code, string displayName)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("角色英文代号不能为空。", nameof(code));
        }

        EnsureWorkspaceFolders(projectRootPath);
        var normalizedCode = NormalizeManualCharacterCode(code);
        var completedFolderPath = Path.Combine(GetCompletedFolderPath(projectRootPath), normalizedCode);
        var draftFolderPath = Path.Combine(GetDraftFolderPath(projectRootPath), normalizedCode);
        if (Directory.Exists(completedFolderPath) && Directory.Exists(draftFolderPath))
        {
            throw new IOException($"Draft 和 Completed 中同时存在角色 {normalizedCode}，请先保留其中一份：{completedFolderPath}；{draftFolderPath}");
        }

        var characterFolderPath = Directory.Exists(completedFolderPath)
            ? completedFolderPath
            : draftFolderPath;
        var createdNewFolder = !Directory.Exists(characterFolderPath);
        Directory.CreateDirectory(characterFolderPath);
        EnsureCharacterFolders(characterFolderPath);

        var normalizedName = string.IsNullOrWhiteSpace(displayName)
            ? normalizedCode
            : displayName.Trim();
        var metadata = ReadMetadata(characterFolderPath) ?? new CharacterMetadata
        {
            Code = normalizedCode,
            Name = normalizedName,
            IsCompleted = false
        };
        metadata.Code = normalizedCode;
        if (string.IsNullOrWhiteSpace(metadata.Name))
        {
            metadata.Name = normalizedName;
        }

        SaveMetadata(characterFolderPath, metadata);
        EnsureDraftStoredInJson(characterFolderPath);

        return new CharacterCreationResult(
            TryLoadCharacterCard(characterFolderPath) ?? BuildCharacterCard(characterFolderPath, metadata),
            createdNewFolder);
    }

    public CharacterCard EnsureCharacterStructure(CharacterCard character)
    {
        EnsureCharacterLayout(character.FolderPath);

        return TryLoadCharacterCard(character.FolderPath) ?? character;
    }

    /// <summary>
    /// 把一个角色目录补成完整形状：素材子目录齐全，草稿已经在 JSON 里。
    ///
    /// 「什么算一个完整的角色目录」是工作区的定义，所以留在这里；
    /// 抽出去的备份、参考图、导出三个服务动手之前都得先调它一次——
    /// 它们各自都会往角色目录里读写，碰上半成品目录（用户手建的、
    /// 从旧版本升上来的）会直接抛 DirectoryNotFound。
    /// </summary>
    internal static void EnsureCharacterLayout(string characterFolderPath)
    {
        EnsureCharacterFolders(characterFolderPath);
        EnsureDraftStoredInJson(characterFolderPath);
    }

    public CharacterCard RefreshCharacterCard(CharacterCard character)
    {
        return TryLoadCharacterCard(character.FolderPath) ?? character;
    }

    public CharacterCard SynchronizeCharacterDisplayName(CharacterCard character, string displayName)
    {
        EnsureCharacterStructure(character);
        var normalizedName = string.IsNullOrWhiteSpace(displayName)
            ? character.Name
            : displayName.Trim();
        var metadata = ReadMetadata(character.FolderPath) ?? new CharacterMetadata
        {
            Code = character.Code,
            Name = normalizedName,
            IsCompleted = character.IsCompleted
        };

        metadata.Code = character.Code;
        metadata.Name = normalizedName;
        metadata.IsCompleted = character.IsCompleted;
        SaveMetadata(character.FolderPath, metadata);
        return TryLoadCharacterCard(character.FolderPath) ?? character with
        {
            Name = normalizedName,
            DisplayName = normalizedName
        };
    }

    public string LoadDraft(CharacterCard character)
    {
        EnsureCharacterStructure(character);
        return LoadToolboxData(character.FolderPath).Draft?.Text ?? string.Empty;
    }

    public void SaveDraft(CharacterCard character, string text)
    {
        EnsureCharacterStructure(character);
        var data = LoadToolboxData(character.FolderPath);
        data.Draft ??= new CharacterDraftData();
        data.Draft.Text = text;
        data.Draft.UpdatedAt = DateTime.Now;
        SaveToolboxData(character.FolderPath, data);
        TouchMetadata(character.FolderPath);
    }
    public void DeleteCharacter(CharacterCard character)
    {
        if (Directory.Exists(character.FolderPath))
        {
            Directory.Delete(character.FolderPath, recursive: true);
        }
    }

    public CharacterCard RenameCharacterCode(CharacterCard character, string newCode)
    {
        EnsureCharacterStructure(character);
        var normalizedCode = NormalizeManualCharacterCode(newCode);
        var oldCode = character.Code;
        if (string.Equals(oldCode, normalizedCode, StringComparison.Ordinal))
        {
            return TryLoadCharacterCard(character.FolderPath) ?? character;
        }

        var oldFolderPath = Path.GetFullPath(character.FolderPath);
        var currentParentPath = Path.GetDirectoryName(oldFolderPath)
            ?? throw new InvalidOperationException("角色目录无效。");
        var projectRootPath = GetProjectRootPath(oldFolderPath);
        var newFolderPath = Path.Combine(currentParentPath, normalizedCode);
        var sameFolderIgnoringCase = string.Equals(oldFolderPath, newFolderPath, StringComparison.OrdinalIgnoreCase);
        var conflictingPath = new[]
            {
                Path.Combine(GetCompletedFolderPath(projectRootPath), normalizedCode),
                Path.Combine(GetDraftFolderPath(projectRootPath), normalizedCode)
            }
            .FirstOrDefault(path =>
                Directory.Exists(path) &&
                !string.Equals(Path.GetFullPath(path), oldFolderPath, StringComparison.OrdinalIgnoreCase));
        if (conflictingPath is not null)
        {
            throw new InvalidOperationException($"已存在同名角色目录：{conflictingPath}");
        }

        var metadata = ReadMetadata(oldFolderPath) ?? new CharacterMetadata
        {
            Code = oldCode,
            Name = character.Name,
            IsCompleted = character.IsCompleted
        };

        var workingFolderPath = oldFolderPath;
        if (!sameFolderIgnoringCase)
        {
            workingFolderPath = MoveOrCopyCharacterFolder(oldFolderPath, newFolderPath);
        }
        else if (!string.Equals(oldFolderPath, newFolderPath, StringComparison.Ordinal))
        {
            try
            {
                MoveDirectoryCaseOnly(oldFolderPath, newFolderPath);
            }
            catch (UnauthorizedAccessException)
            {
                MoveDirectoryCaseOnlyByCopy(oldFolderPath, newFolderPath);
            }

            workingFolderPath = newFolderPath;
        }

        RenamePrefixedPaths(workingFolderPath, oldCode, normalizedCode);
        metadata.Code = normalizedCode;
        SaveMetadata(workingFolderPath, metadata);
        UpdateToolboxCharacterCode(workingFolderPath, oldCode, normalizedCode);
        EnsureCharacterFolders(workingFolderPath);
        return TryLoadCharacterCard(workingFolderPath)
            ?? BuildCharacterCard(workingFolderPath, metadata);
    }

    /// <summary>
    /// 元数据读不出来时的兜底卡片。
    ///
    /// 以前这里是直接 continue：character.json 只要坏了（写一半崩溃、
    /// 被外部工具改坏），这个角色就从角色台上凭空消失，用户会以为素材全丢了，
    /// 而磁盘上的图片语音其实都还在。改成用目录名兜一张卡出来，
    /// 角色仍然看得见、能进去，坏掉的那份元数据留在原地等修。
    ///
    /// 元数据文件本来就不存在的目录（比如用户随手建的空文件夹）仍然返回 null，
    /// 那才是「这不是一个角色」。
    /// </summary>
    private static CharacterCard? TryBuildFallbackCharacterCard(string characterFolderPath, bool isCompleted)
    {
        var metadataFilePath = Path.Combine(characterFolderPath, ToolFolderName, MetadataFileName);
        if (!File.Exists(metadataFilePath))
        {
            return null;
        }

        var code = Path.GetFileName(characterFolderPath.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        return BuildCharacterCard(characterFolderPath, new CharacterMetadata
        {
            Code = code,
            Name = code,
            IsCompleted = isCompleted,
        });
    }

    internal static CharacterCard? TryLoadCharacterCard(string characterFolderPath)
    {
        var metadataFilePath = Path.Combine(characterFolderPath, ToolFolderName, MetadataFileName);
        if (!File.Exists(metadataFilePath))
        {
            return null;
        }

        try
        {
            var metadata = JsonSerializer.Deserialize(
                File.ReadAllText(metadataFilePath, Encoding.UTF8),
                AppJsonSerializerContext.Default.CharacterMetadata);
            if (metadata is null ||
                string.IsNullOrWhiteSpace(metadata.Code) ||
                string.IsNullOrWhiteSpace(metadata.Name))
            {
                return null;
            }

            return BuildCharacterCard(characterFolderPath, metadata);
        }
        catch
        {
            return null;
        }
    }

    private static CharacterCard BuildCharacterCard(string characterFolderPath, CharacterMetadata metadata)
    {
        var toolFolderPath = Path.Combine(characterFolderPath, ToolFolderName);
        var displayName = ReadCharacterDisplayName(characterFolderPath, metadata);
        var coverUri = ResolveCharacterCoverUri(characterFolderPath);
        return new CharacterCard(
            metadata.Code,
            metadata.Name,
            characterFolderPath,
            toolFolderPath,
            Path.Combine(toolFolderPath, ReferenceFolderName),
            metadata.LastEditedAt == default ? Directory.GetLastWriteTime(characterFolderPath) : metadata.LastEditedAt,
            metadata.IsCompleted,
            displayName,
            coverUri);
    }

    private static string ReadCharacterDisplayName(string characterFolderPath, CharacterMetadata metadata)
    {
        var toolboxPath = GetToolboxDataFilePath(characterFolderPath);
        if (File.Exists(toolboxPath))
        {
            try
            {
                var toolboxData = JsonSerializer.Deserialize(
                    File.ReadAllText(toolboxPath, Encoding.UTF8),
                    AppJsonSerializerContext.Default.CharacterToolboxData);
                if (!string.IsNullOrWhiteSpace(toolboxData?.CharacterInfo?.Name))
                {
                    return toolboxData.CharacterInfo.Name.Trim();
                }
            }
            catch
            {
            }
        }

        return string.IsNullOrWhiteSpace(metadata.Name) ? metadata.Code : metadata.Name.Trim();
    }

    private static string ResolveCharacterCoverUri(string characterFolderPath)
    {
        var folderPath = Path.Combine(characterFolderPath, AssetMaterialFolderName, "MorphPortrait");
        if (!Directory.Exists(folderPath))
        {
            return string.Empty;
        }

        var filePath = Directory
            .EnumerateFiles(folderPath)
            .Where(CharacterReferenceImageService.IsSupportedReferenceImage)
            .Select(path => new
            {
                Path = path,
                Index = ResolveIndexedFileOrder(path),
                Name = Path.GetFileName(path)
            })
            .OrderBy(item => item.Index)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Path)
            .FirstOrDefault();
        if (filePath is null)
        {
            return string.Empty;
        }

        var info = new FileInfo(filePath);
        var version = Math.Max(info.LastWriteTimeUtc.Ticks, info.Length);
        return $"{new Uri(info.FullName).AbsoluteUri}?v={version}";
    }

    private static int ResolveIndexedFileOrder(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var lastDash = name.LastIndexOf('-');
        if (lastDash < 0)
        {
            return 1;
        }

        return int.TryParse(name[(lastDash + 1)..], out var index)
            ? Math.Max(1, index)
            : 1;
    }

    internal static void EnsureCharacterFolders(string characterFolderPath)
    {
        Directory.CreateDirectory(Path.Combine(characterFolderPath, ToolFolderName));
        Directory.CreateDirectory(Path.Combine(characterFolderPath, AssetMaterialFolderName));
        Directory.CreateDirectory(Path.Combine(characterFolderPath, ZDMaterialFolderName));
        Directory.CreateDirectory(Path.Combine(characterFolderPath, SoundFolderName));
        Directory.CreateDirectory(Path.Combine(characterFolderPath, ExAssetFolderName));
        Directory.CreateDirectory(Path.Combine(characterFolderPath, BuffFolderName));
        Directory.CreateDirectory(Path.Combine(characterFolderPath, ToolFolderName, ReferenceFolderName));
    }

    private static void SaveMetadata(string characterFolderPath, CharacterMetadata metadata)
    {
        metadata.LastEditedAt = DateTime.Now;
        var metadataFilePath = Path.Combine(characterFolderPath, ToolFolderName, MetadataFileName);
        // character.json 决定角色卡能不能被认出来，写一半崩溃这个角色就从角色台上消失了，
        // 所以和其他角色数据一样走原子替换。
        WriteAllTextAtomic(
            metadataFilePath,
            JsonSerializer.Serialize(metadata, CharacterMetadataJsonTypeInfo));
    }

    private static CharacterMetadata? ReadMetadata(string characterFolderPath)
    {
        var metadataFilePath = Path.Combine(characterFolderPath, ToolFolderName, MetadataFileName);
        if (!File.Exists(metadataFilePath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(
                File.ReadAllText(metadataFilePath, Encoding.UTF8),
                AppJsonSerializerContext.Default.CharacterMetadata);
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            // 读不出来不能当作「没有这个角色」——那样角色会从角色台上凭空消失，
            // 用户还以为数据丢了。把坏文件挪到一边留证据，让调用方按「缺元数据」
            // 走既有的兜底路径（用目录名当代号），角色卡仍然看得见。
            TryQuarantineCorruptFile(metadataFilePath);
            return null;
        }
    }

    /// <summary>把读不出来的文件改名留档，避免下一次保存直接覆盖掉证据。</summary>
    private static void TryQuarantineCorruptFile(string path)
    {
        try
        {
            var quarantinePath = $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            if (!File.Exists(quarantinePath))
            {
                File.Move(path, quarantinePath);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void EnsureDraftStoredInJson(string characterFolderPath)
    {
        EnsureCharacterFolders(characterFolderPath);
        var data = LoadToolboxData(characterFolderPath);
        var legacyDraftPath = GetLegacyDraftFilePath(characterFolderPath);
        var legacyText = File.Exists(legacyDraftPath)
            ? File.ReadAllText(legacyDraftPath, Encoding.UTF8)
            : string.Empty;
        if (data.Draft is not null &&
            (!string.IsNullOrWhiteSpace(data.Draft.Text) || string.IsNullOrWhiteSpace(legacyText)))
        {
            return;
        }

        data.Draft = new CharacterDraftData
        {
            Text = legacyText,
            UpdatedAt = File.Exists(legacyDraftPath)
                ? File.GetLastWriteTime(legacyDraftPath)
                : DateTime.Now
        };
        SaveToolboxData(characterFolderPath, data);
    }

    private static CharacterToolboxData LoadToolboxData(string characterFolderPath)
    {
        var toolboxPath = GetToolboxDataFilePath(characterFolderPath);
        if (!File.Exists(toolboxPath))
        {
            return new CharacterToolboxData();
        }

        try
        {
            return JsonSerializer.Deserialize(
                File.ReadAllText(toolboxPath, Encoding.UTF8),
                AppJsonSerializerContext.Default.CharacterToolboxData) ?? new CharacterToolboxData();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"角色工具箱数据读取失败：{toolboxPath}", ex);
        }
    }

    private static void SaveToolboxData(string characterFolderPath, CharacterToolboxData data)
    {
        Directory.CreateDirectory(Path.Combine(characterFolderPath, ToolFolderName));
        data.UpdatedAt = DateTime.Now;
        WriteAllTextAtomic(
            GetToolboxDataFilePath(characterFolderPath),
            JsonSerializer.Serialize(data, JsonContext.CharacterToolboxData));
    }

    private static string GetToolboxDataFilePath(string characterFolderPath)
    {
        return Path.Combine(characterFolderPath, ToolFolderName, ToolboxDataFileName);
    }

    private static void UpdateToolboxCharacterCode(string characterFolderPath, string oldCode, string newCode)
    {
        var toolboxPath = GetToolboxDataFilePath(characterFolderPath);
        if (!File.Exists(toolboxPath))
        {
            return;
        }

        CharacterToolboxData? data;
        try
        {
            data = JsonSerializer.Deserialize(
                File.ReadAllText(toolboxPath, Encoding.UTF8),
                AppJsonSerializerContext.Default.CharacterToolboxData);
        }
        catch
        {
            return;
        }

        if (data?.CharacterInfo is null)
        {
            return;
        }

        data.CharacterInfo.Code = newCode;
        data.CharacterInfo.ItemIconPath = ReplaceCodeToken(data.CharacterInfo.ItemIconPath, oldCode, newCode);
        if (data.Buffs?.Buffs is not null)
        {
            foreach (var buff in data.Buffs.Buffs)
            {
                buff.GeneratedCode = ReplaceCodeToken(buff.GeneratedCode, oldCode, newCode);
                buff.IconPath = ReplaceCodeToken(buff.IconPath, oldCode, newCode);
                buff.IconUri = ReplaceCodeToken(buff.IconUri, oldCode, newCode);
            }
        }

        if (data.Skills is not null)
        {
            foreach (var skill in EnumerateSkillEntries(data.Skills))
            {
                skill.IconPath = ReplaceCodeToken(skill.IconPath, oldCode, newCode);
            }
        }

        data.UpdatedAt = DateTime.Now;
        WriteAllTextAtomic(
            toolboxPath,
            JsonSerializer.Serialize(data, JsonContext.CharacterToolboxData));
    }

    /// <summary>
    /// 原子替换。以前这里是「写临时文件 -> 删掉目标 -> 移过去」，
    /// 删和移之间崩溃就等于角色的技能、BUFF、信息整份消失；
    /// 而且临时名是固定的 .tmp，两处并发保存会互相踩。
    /// 同仓其他八处走的都是 File.Move(overwrite: true)，唯独这里没跟上。
    /// </summary>
    private static void WriteAllTextAtomic(string path, string text) =>
        AtomicFileWriter.WriteAllText(path, text, Encoding.UTF8);

    private static IEnumerable<CharacterSkillEntry> EnumerateSkillEntries(CharacterSkillsData skills)
    {
        foreach (var entry in skills.FirstSkill)
        {
            yield return entry;
        }

        foreach (var entry in skills.SecondSkill)
        {
            yield return entry;
        }

        foreach (var entry in skills.UltimateSkill)
        {
            yield return entry;
        }

        foreach (var entry in skills.SupportSkill)
        {
            yield return entry;
        }

        foreach (var entry in skills.ComboSkills)
        {
            yield return entry;
        }
    }

    private static string ReplaceCodeToken(string value, string oldCode, string newCode)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (string.Equals(value, oldCode, StringComparison.OrdinalIgnoreCase))
        {
            return newCode;
        }

        return value
            .Replace($"{oldCode}-", $"{newCode}-", StringComparison.OrdinalIgnoreCase)
            .Replace($"\\{oldCode}\\", $"\\{newCode}\\", StringComparison.OrdinalIgnoreCase)
            .Replace($"/{oldCode}/", $"/{newCode}/", StringComparison.OrdinalIgnoreCase);
    }

    private static void RenamePrefixedPaths(string rootPath, string oldCode, string newCode)
    {
        var prefix = $"{oldCode}-";
        foreach (var filePath in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories).ToList())
        {
            var fileName = Path.GetFileName(filePath);
            if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var targetPath = Path.Combine(
                Path.GetDirectoryName(filePath)!,
                $"{newCode}-{fileName[prefix.Length..]}");
            MoveFileSafely(filePath, targetPath);
        }

        var directories = Directory
            .EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories)
            .OrderByDescending(path => path.Length)
            .ToList();
        foreach (var directoryPath in directories)
        {
            var directoryName = Path.GetFileName(directoryPath);
            if (!directoryName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var targetPath = Path.Combine(
                Path.GetDirectoryName(directoryPath)!,
                $"{newCode}-{directoryName[prefix.Length..]}");
            MoveDirectorySafely(directoryPath, targetPath);
        }
    }

    private static string MoveOrCopyCharacterFolder(string sourcePath, string targetPath)
    {
        try
        {
            Directory.Move(sourcePath, targetPath);
            return targetPath;
        }
        catch (UnauthorizedAccessException)
        {
            CopyDirectory(sourcePath, targetPath);
            TryDeleteDirectory(sourcePath);
            return targetPath;
        }
        catch (IOException)
        {
            CopyDirectory(sourcePath, targetPath);
            TryDeleteDirectory(sourcePath);
            return targetPath;
        }
    }

    private static void CopyDirectory(string sourcePath, string targetPath)
    {
        if (Directory.Exists(targetPath))
        {
            throw new IOException($"目标角色目录已存在：{targetPath}");
        }

        Directory.CreateDirectory(targetPath);
        foreach (var directoryPath in Directory.EnumerateDirectories(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourcePath, directoryPath);
            Directory.CreateDirectory(Path.Combine(targetPath, relativePath));
        }

        foreach (var filePath in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourcePath, filePath);
            var targetFilePath = Path.Combine(targetPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFilePath)!);
            File.Copy(filePath, targetFilePath, overwrite: false);
        }
    }

    internal static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static void MoveFileSafely(string sourcePath, string targetPath)
    {
        var sourceFullPath = Path.GetFullPath(sourcePath);
        var targetFullPath = Path.GetFullPath(targetPath);
        if (string.Equals(sourceFullPath, targetFullPath, StringComparison.Ordinal))
        {
            return;
        }

        if (string.Equals(sourceFullPath, targetFullPath, StringComparison.OrdinalIgnoreCase))
        {
            var tempPath = Path.Combine(
                Path.GetDirectoryName(sourceFullPath)!,
                $".rename_{Guid.NewGuid():N}.tmp");
            File.Move(sourceFullPath, tempPath);
            File.Move(tempPath, targetFullPath);
            return;
        }

        if (File.Exists(targetFullPath))
        {
            throw new IOException($"重命名目标文件已存在：{targetFullPath}");
        }

        File.Move(sourceFullPath, targetFullPath);
    }

    private static void MoveDirectorySafely(string sourcePath, string targetPath)
    {
        var sourceFullPath = Path.GetFullPath(sourcePath);
        var targetFullPath = Path.GetFullPath(targetPath);
        if (string.Equals(sourceFullPath, targetFullPath, StringComparison.Ordinal))
        {
            return;
        }

        if (string.Equals(sourceFullPath, targetFullPath, StringComparison.OrdinalIgnoreCase))
        {
            MoveDirectoryCaseOnly(sourceFullPath, targetFullPath);
            return;
        }

        if (Directory.Exists(targetFullPath))
        {
            throw new IOException($"重命名目标文件夹已存在：{targetFullPath}");
        }

        Directory.Move(sourceFullPath, targetFullPath);
    }

    private static void MoveDirectoryCaseOnly(string sourcePath, string targetPath)
    {
        var tempPath = Path.Combine(
            Path.GetDirectoryName(sourcePath)!,
            $".rename_{Guid.NewGuid():N}.tmp");
        Directory.Move(sourcePath, tempPath);
        Directory.Move(tempPath, targetPath);
    }

    private static void MoveDirectoryCaseOnlyByCopy(string sourcePath, string targetPath)
    {
        var tempPath = Path.Combine(
            Path.GetDirectoryName(sourcePath)!,
            $".rename_{Guid.NewGuid():N}.tmp");
        CopyDirectory(sourcePath, tempPath);
        TryDeleteDirectory(sourcePath);
        Directory.Move(tempPath, targetPath);
    }

    internal static void TouchMetadata(string characterFolderPath)
    {
        var metadataFilePath = Path.Combine(characterFolderPath, ToolFolderName, MetadataFileName);
        if (!File.Exists(metadataFilePath))
        {
            return;
        }

        var metadata = JsonSerializer.Deserialize(
            File.ReadAllText(metadataFilePath, Encoding.UTF8),
            AppJsonSerializerContext.Default.CharacterMetadata);
        if (metadata is null)
        {
            return;
        }

        SaveMetadata(characterFolderPath, metadata);
    }

    public CharacterCard SetCompleted(CharacterCard character, bool isCompleted)
    {
        EnsureCharacterStructure(character);
        var projectRootPath = GetProjectRootPath(character.FolderPath);
        var targetParentPath = isCompleted ? GetCompletedFolderPath(projectRootPath) : GetDraftFolderPath(projectRootPath);
        var targetPath = Path.Combine(targetParentPath, character.Code);
        if (!string.Equals(
                Path.GetFullPath(character.FolderPath),
                Path.GetFullPath(targetPath),
                StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(targetPath))
        {
            throw new IOException($"移动角色失败，目标目录已存在：{targetPath}");
        }

        var metadataFilePath = Path.Combine(character.FolderPath, ToolFolderName, MetadataFileName);
        var metadata = File.Exists(metadataFilePath)
            ? JsonSerializer.Deserialize(
                File.ReadAllText(metadataFilePath, Encoding.UTF8),
                AppJsonSerializerContext.Default.CharacterMetadata)
            : null;
        metadata ??= new CharacterMetadata
        {
            Code = character.Code,
            Name = character.Name
        };
        metadata.IsCompleted = isCompleted;
        SaveMetadata(character.FolderPath, metadata);
        var updated = TryLoadCharacterCard(character.FolderPath) ?? character with { IsCompleted = isCompleted };
        return MoveCharacterToStateFolder(projectRootPath, updated, isCompleted);
    }
    /// <summary>
    /// 「某个路径是不是落在这个目录里面」。备份要靠它把 CharacterBackups 排除在打包范围外，
    /// 导出要靠它拦住「把导出目录设在角色目录里面」。两边都要用，所以留在工作区这一份，
    /// 别在各自的服务里再抄一遍。
    /// </summary>
    internal static bool IsPathInsideDirectory(string path, string directoryPath)
    {
        var fullPath = EnsureTrailingDirectorySeparator(Path.GetFullPath(path));
        var fullDirectory = EnsureTrailingDirectorySeparator(Path.GetFullPath(directoryPath));
        return fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureTrailingDirectorySeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static string GetLegacyDraftFilePath(string characterFolderPath)
    {
        return Path.Combine(characterFolderPath, ToolFolderName, LegacyDraftFileName);
    }

    private static string CreateUniqueCharacterCode(string projectRootPath, string characterName)
    {
        var baseCode = NormalizeCharacterCode(characterName);
        var code = baseCode;
        var index = 2;
        while (Directory.Exists(Path.Combine(GetCompletedFolderPath(projectRootPath), code)) ||
               Directory.Exists(Path.Combine(GetDraftFolderPath(projectRootPath), code)))
        {
            code = $"{baseCode}_{index:00}";
            index++;
        }

        return code;
    }

    private static void EnsureWorkspaceFolders(string projectRootPath)
    {
        Directory.CreateDirectory(projectRootPath);
        Directory.CreateDirectory(GetDraftFolderPath(projectRootPath));
        Directory.CreateDirectory(GetCompletedFolderPath(projectRootPath));
        Directory.CreateDirectory(Path.Combine(projectRootPath, ExportFolderName));
    }

    private static string GetDraftFolderPath(string projectRootPath)
    {
        return Path.Combine(Path.GetFullPath(projectRootPath), DraftFolderName);
    }

    private static string GetCompletedFolderPath(string projectRootPath)
    {
        return Path.Combine(Path.GetFullPath(projectRootPath), CompletedFolderName);
    }

    private static string GetProjectRootPath(string characterFolderPath)
    {
        var parent = Directory.GetParent(Path.GetFullPath(characterFolderPath))
            ?? throw new InvalidOperationException("角色目录无效。");
        if (!string.Equals(parent.Name, DraftFolderName, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(parent.Name, CompletedFolderName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"角色目录必须位于 Draft 或 Completed：{characterFolderPath}");
        }

        return parent.Parent?.FullName
            ?? throw new InvalidOperationException($"{parent.Name} 目录缺少项目根目录。");
    }

    private static CharacterCard MoveCharacterToStateFolder(
        string projectRootPath,
        CharacterCard character,
        bool isCompleted)
    {
        var targetParentPath = isCompleted ? GetCompletedFolderPath(projectRootPath) : GetDraftFolderPath(projectRootPath);
        Directory.CreateDirectory(targetParentPath);
        var sourcePath = Path.GetFullPath(character.FolderPath);
        var targetPath = Path.Combine(targetParentPath, character.Code);
        if (string.Equals(sourcePath, Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
        {
            return character;
        }

        if (Directory.Exists(targetPath))
        {
            throw new IOException($"移动角色失败，目标目录已存在：{targetPath}");
        }

        var movedPath = MoveOrCopyCharacterFolder(sourcePath, targetPath);
        return TryLoadCharacterCard(movedPath) ?? BuildCharacterCard(
            movedPath,
            new CharacterMetadata
            {
                Code = character.Code,
                Name = character.Name,
                IsCompleted = isCompleted,
                LastEditedAt = character.LastEditedAt
            });
    }

    private static string NormalizeCharacterCode(string characterName)
    {
        var normalized = characterName.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
            else if (ch is ' ' or '-' or '_')
            {
                builder.Append('_');
            }
        }

        var code = builder
            .ToString()
            .Trim('_');
        while (code.Contains("__", StringComparison.Ordinal))
        {
            code = code.Replace("__", "_", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(code)
            ? $"character_{DateTime.Now:yyyyMMddHHmmss}"
            : code;
    }

    private static string NormalizeManualCharacterCode(string value)
    {
        var code = value.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("英文代号不能为空。", nameof(value));
        }

        if (code.Any(ch => !char.IsLetterOrDigit(ch) && ch is not '_' and not '-'))
        {
            throw new ArgumentException("英文代号只能包含英文、数字、下划线或连字符。", nameof(value));
        }

        return code;
    }
}
