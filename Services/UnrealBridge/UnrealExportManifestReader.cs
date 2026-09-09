using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 读取 Unreal 侧导出的清单，并把它和磁盘上的实际目录合成一份「角色候选」列表。
/// 只读：解析 JSON、扫目录、读 .uasset 里的 FText，从不写任何东西，也不起引擎进程。
///
/// 从 <see cref="UnrealProjectSyncService"/> 里搬出来的。留在那个类里时，这一段是
/// <c>Check()</c> 的内部实现细节——想验「分步导出的清单能不能合并」「角色名读不出来时
/// 退回代号」这类规则，只能先备好引擎路径把整条检测链路跑起来，于是这些规则长期
/// 只靠断言源码文本来守。现在可以直接喂一个清单对象和一个临时目录跑。
///
/// 这里有两处踩过坑、必须原样保留的行为：
/// 一是清单合并（<see cref="ResolveExportManifest"/>）——第三到第五步只写各自范围的
/// 分段清单，只读 characters.json 会让 Unreal 侧整体为空，差异里删除永远是 0；
/// 二是清单读坏时那行 <c>ToolboxLog.Error</c>（<see cref="LoadExportManifest"/>）——
/// 「读坏了」和「这段本来就没写」返回的都是 null，不记一笔就完全分不出来。
///
/// 角色名是从 Item_*.uasset 的二进制里逐字节试探 FText 抠出来的
/// （<see cref="ReadFirstLocalizedText"/>）。这是离线扫描的关键：刷新角色列表不必重跑导出。
/// </summary>
internal static class UnrealExportManifestReader
{
    /// <summary>
    /// 导出清单的格式版本。低于这个版本的清单缺少详细字段，角色只能算「有目录但没最新数据」。
    /// </summary>
    private const int CurrentExportSchemaVersion = 2;

    /// <summary>
    /// 逐字节试探 FText 时必须用「解不出来就抛」的解码器：绝大多数偏移本就不是合法 UTF-16，
    /// 换成替换字符的宽容模式会让每个偏移都"成功"，抠出一堆乱码当角色名。
    /// </summary>
    private static readonly UnicodeEncoding StrictUnicodeEncoding = new(
        bigEndian: false,
        byteOrderMark: false,
        throwOnInvalidBytes: true);

    /// <summary>分步导出会各自写一个清单文件，完整导出才写 characters.json。</summary>
    private static readonly string[] ExportManifestFileNames =
    [
        UnrealProjectSyncService.ExportManifestFileName,
        UnrealProjectSyncService.SequenceExportManifestFileName,
        "characters-materials.json",
        "characters-normalization.json"
    ];

    /// <summary>
    /// 解析导出目录里可用的清单。第三到第五步只写各自范围的清单，
    /// 从来不会生成 characters.json；如果只读 characters.json，
    /// Unreal 侧就会整体为空，差异里只剩工具箱侧的新增，删除永远是 0。
    /// 这里按修改时间从新到旧合并各分段，缺哪段补哪段。
    /// </summary>
    internal static (string Path, UnrealProjectExportManifest? Manifest) ResolveExportManifest(string exportDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(exportDirectoryPath) || !Directory.Exists(exportDirectoryPath))
        {
            return (string.IsNullOrWhiteSpace(exportDirectoryPath)
                ? string.Empty
                : Path.Combine(exportDirectoryPath, UnrealProjectSyncService.ExportManifestFileName), null);
        }

        var loaded = ExportManifestFileNames
            .Select(fileName => Path.Combine(exportDirectoryPath, fileName))
            .Where(File.Exists)
            .Select(path => (Path: path, Manifest: LoadExportManifest(path), WrittenAt: File.GetLastWriteTimeUtc(path)))
            .Where(entry => entry.Manifest is not null)
            .OrderByDescending(entry => entry.WrittenAt)
            .ToArray();
        if (loaded.Length == 0)
        {
            return (Path.Combine(exportDirectoryPath, UnrealProjectSyncService.ExportManifestFileName), null);
        }

        var primary = loaded[0];
        var manifest = primary.Manifest!;
        foreach (var entry in loaded.Skip(1))
        {
            var other = entry.Manifest!;
            if (manifest.Assets.Count == 0) manifest.Assets = other.Assets;
            if (manifest.CharacterItems.Count == 0) manifest.CharacterItems = other.CharacterItems;
            if (manifest.CharacterSummaries.Count == 0) manifest.CharacterSummaries = other.CharacterSummaries;
            if (manifest.CharacterActors.Count == 0) manifest.CharacterActors = other.CharacterActors;
            if (manifest.CharacterSequences.Count == 0) manifest.CharacterSequences = other.CharacterSequences;
            if (manifest.CharacterBuffs.Count == 0) manifest.CharacterBuffs = other.CharacterBuffs;
        }

        return (primary.Path, manifest);
    }

    internal static UnrealProjectExportManifest? LoadExportManifest(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(
                File.ReadAllText(path, Encoding.UTF8),
                AppJsonSerializerContext.Default.UnrealProjectExportManifest);
        }
        catch (Exception error)
        {
            // 返回 null 是有意的：ResolveExportManifest 要能缺一段就接着合并剩下的段。
            // 但「坏掉的清单」和「这段本来就没写」在返回值上是同一个 null，于是这段资产
            // 整段消失——Unreal 侧看起来是空的，差异里只剩工具箱侧的新增、删除永远是 0，
            // 正是 ResolveExportManifest 顶上那段注释警告过的样子。区分两者只靠这行日志。
            ToolboxLog.Error($"Unreal 导出清单读不了，这一段资产会被当成不存在：{path}", error);
            return null;
        }
    }

    internal static IReadOnlyList<UnrealProjectSyncExportAssetView> BuildPreviewAssets(UnrealProjectExportManifest? manifest)
    {
        if (manifest is null)
        {
            return [];
        }

        return manifest.Assets
            .OrderBy(asset => asset.SourceRoot, StringComparer.OrdinalIgnoreCase)
            .ThenBy(asset => asset.PackagePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(asset => asset.AssetName, StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .Select(asset => new UnrealProjectSyncExportAssetView(
                asset.AssetName,
                asset.AssetClass,
                asset.PackagePath,
                asset.ObjectPath,
                asset.SourceRoot,
                asset.ExportedFilePath))
            .ToArray();
    }

    internal static IReadOnlyList<UnrealProjectSyncCharacterCandidate> BuildCharacterCandidates(
        UnrealProjectExportManifest? manifest,
        string contentPath)
    {
        var manifestGeneratedAt = ParseGeneratedAt(manifest?.GeneratedAt);
        var detailedCandidates = BuildManifestCharacterCandidates(manifest)
            .ToDictionary(candidate => candidate.Code, StringComparer.OrdinalIgnoreCase);
        var summaryNames = (manifest?.CharacterSummaries ?? [])
            .Where(summary => !string.IsNullOrWhiteSpace(summary.Code) && !string.IsNullOrWhiteSpace(summary.DisplayName))
            .GroupBy(summary => summary.Code.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().DisplayName.Trim(),
                StringComparer.OrdinalIgnoreCase);
        foreach (var item in ReadCharacterItemDisplayNames(contentPath))
        {
            summaryNames[item.Key] = item.Value;
        }
        var scannedCandidates = BuildScannedCharacterCandidates(contentPath)
            .ToDictionary(candidate => candidate.Code, StringComparer.OrdinalIgnoreCase);

        if (scannedCandidates.Count == 0)
        {
            return detailedCandidates.Values
                .OrderBy(candidate => candidate.Code, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var candidateCodes = scannedCandidates.Keys
            .Union(detailedCandidates.Keys, StringComparer.OrdinalIgnoreCase)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return candidateCodes
            .Select(code =>
            {
                if (!scannedCandidates.TryGetValue(code, out var candidate))
                {
                    return detailedCandidates[code];
                }

                if (!detailedCandidates.TryGetValue(code, out var detailed))
                {
                    return summaryNames.TryGetValue(code, out var scannedDisplayName)
                        ? CloneCharacterCandidateWithLatestData(candidate, false, scannedDisplayName)
                        : candidate;
                }

                var displayName = summaryNames.TryGetValue(code, out var summaryDisplayName)
                    ? summaryDisplayName
                    : detailed.DisplayName;

                if (manifestGeneratedAt is null || HasCharacterFolderChangedAfterExport(contentPath, code, manifestGeneratedAt.Value))
                {
                    return CloneCharacterCandidateWithLatestData(detailed, false, displayName);
                }

                return string.Equals(displayName, detailed.DisplayName, StringComparison.Ordinal)
                    ? detailed
                    : CloneCharacterCandidateWithLatestData(detailed, true, displayName);
            })
            .OrderBy(candidate => candidate.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<UnrealProjectSyncCharacterCandidate> BuildScannedCharacterCandidates(string contentPath)
    {
        if (string.IsNullOrWhiteSpace(contentPath) || !Directory.Exists(contentPath))
        {
            return [];
        }

        var baseMaterialDiskPath = UnrealProjectSyncService.CombineContentPath(contentPath, UnrealProjectSyncService.TargetBaseMaterialContentPath);
        var zdDiskPath = UnrealProjectSyncService.CombineContentPath(contentPath, UnrealProjectSyncService.TargetZdContentPath);
        if (!Directory.Exists(baseMaterialDiskPath) && !Directory.Exists(zdDiskPath))
        {
            return [];
        }

        var baseFolders = Directory.Exists(baseMaterialDiskPath)
            ? Directory.EnumerateDirectories(baseMaterialDiskPath)
                .Select(path => (Code: Path.GetFileName(path), Path: path))
                .Where(item => !string.IsNullOrWhiteSpace(item.Code))
                .ToDictionary(item => item.Code, item => item.Path, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var zdFolders = Directory.Exists(zdDiskPath)
            ? Directory.EnumerateDirectories(zdDiskPath)
                .Select(path => (Code: Path.GetFileName(path), Path: path))
                .Where(item => !string.IsNullOrWhiteSpace(item.Code))
                .ToDictionary(item => item.Code, item => item.Path, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return baseFolders.Keys
            .Union(zdFolders.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .Select(code =>
            {
                var baseAssetCount = baseFolders.TryGetValue(code, out var baseFolder)
                    ? CountUassetFiles(baseFolder)
                    : 0;
                var zdAssetCount = zdFolders.TryGetValue(code, out var zdFolder)
                    ? CountUassetFiles(Path.Combine(zdFolder, "Material"))
                    : 0;
                return new UnrealProjectSyncCharacterCandidate(
                    code,
                    code,
                    $"{UnrealProjectSyncService.TargetBaseMaterialContentPath}/{code}",
                    $"{UnrealProjectSyncService.TargetZdContentPath}/{code}",
                    baseAssetCount,
                    zdAssetCount,
                    UnrealCharacterPreviewFactory.BuildCharacterInfoPreview(null, null),
                    UnrealCharacterPreviewFactory.BuildSkillsPreview(null, null, null, null, new Dictionary<string, UnrealProjectExportAsset>(StringComparer.OrdinalIgnoreCase)),
                    UnrealCharacterPreviewFactory.BuildSequenceFramesPreview(null),
                    UnrealCharacterPreviewFactory.BuildBuffsPreview(null),
                    [],
                    hasLatestData: false);
            })
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string> ReadCharacterItemDisplayNames(string contentPath)
    {
        if (string.IsNullOrWhiteSpace(contentPath) || !Directory.Exists(contentPath))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var itemFolderPath = UnrealProjectSyncService.CombineContentPath(contentPath, UnrealProjectSyncService.TargetCharacterItemContentPath);
        if (!Directory.Exists(itemFolderPath))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var filePath in Directory.EnumerateFiles(itemFolderPath, "Item_*.uasset", SearchOption.TopDirectoryOnly))
        {
            var assetName = Path.GetFileNameWithoutExtension(filePath);
            if (assetName.Length <= "Item_".Length)
            {
                continue;
            }

            try
            {
                var displayName = ReadFirstLocalizedText(File.ReadAllBytes(filePath));
                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    names[assetName["Item_".Length..]] = displayName;
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // 单个 Item 读不了不该让整批角色名都读不出来，所以只跳过这一个。后果是
                // 这个角色退回显示代号，看起来像「名字没配」——真正的原因得靠这行日志。
                ToolboxLog.Warn($"读不了角色 Item 资产，该角色只能显示代号：{filePath}", error);
            }
        }

        return names;
    }

    private static string ReadFirstLocalizedText(ReadOnlySpan<byte> bytes)
    {
        for (var offset = 0; offset <= bytes.Length - 8; offset++)
        {
            var serializedLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset, sizeof(int)));
            if (serializedLength is > -2 or < -128)
            {
                continue;
            }

            var characterCount = -serializedLength;
            var byteCount = characterCount * sizeof(char);
            var valueOffset = offset + sizeof(int);
            if (valueOffset + byteCount > bytes.Length ||
                bytes[valueOffset + byteCount - 2] != 0 ||
                bytes[valueOffset + byteCount - 1] != 0)
            {
                continue;
            }

            try
            {
                var value = StrictUnicodeEncoding.GetString(bytes.Slice(valueOffset, byteCount - sizeof(char))).Trim();
                if (IsUsableCharacterDisplayName(value))
                {
                    return value;
                }
            }
            catch (DecoderFallbackException)
            {
                // 这里**故意不写日志**：逐字节偏移试探 FText，绝大多数偏移本就解不出
                // 合法的 UTF-16，解码失败是预期内的中间状态。一个资产能抛几千次。
            }
        }

        return string.Empty;
    }

    private static bool IsUsableCharacterDisplayName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64 || value.Any(char.IsControl))
        {
            return false;
        }

        return value.Any(character =>
            character is >= '\u3400' and <= '\u4DBF' or
            >= '\u4E00' and <= '\u9FFF' or
            >= '\uF900' and <= '\uFAFF' or
            >= '\u3040' and <= '\u30FF');
    }

    private static UnrealProjectSyncCharacterCandidate CloneCharacterCandidateWithLatestData(
        UnrealProjectSyncCharacterCandidate candidate,
        bool hasLatestData,
        string? displayName = null)
    {
        return new UnrealProjectSyncCharacterCandidate(
            candidate.Code,
            string.IsNullOrWhiteSpace(displayName) ? candidate.DisplayName : displayName,
            candidate.BaseMaterialPath,
            candidate.ZdPath,
            candidate.BaseMaterialAssetCount,
            candidate.ZdAssetCount,
            candidate.CharacterInfo,
            candidate.SkillsPreview,
            candidate.SequenceFramesPreview,
            candidate.BuffsPreview,
            candidate.MaterialBuckets,
            hasLatestData,
            candidate.VoiceBuckets);
    }

    private static bool HasCharacterFolderChangedAfterExport(string contentPath, string code, DateTime exportGeneratedAt)
    {
        if (string.IsNullOrWhiteSpace(contentPath) || string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var exportUtc = exportGeneratedAt.Kind == DateTimeKind.Utc
            ? exportGeneratedAt
            : exportGeneratedAt.ToUniversalTime();
        var roots = new[]
        {
            Path.Combine(UnrealProjectSyncService.CombineContentPath(contentPath, UnrealProjectSyncService.TargetBaseMaterialContentPath), code),
            Path.Combine(UnrealProjectSyncService.CombineContentPath(contentPath, UnrealProjectSyncService.TargetZdContentPath), code)
        };

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            if (Directory.EnumerateFiles(root, "*.uasset", SearchOption.AllDirectories)
                .Any(path => File.GetLastWriteTimeUtc(path) > exportUtc))
            {
                return true;
            }
        }

        return false;
    }

    private static int CountUassetFiles(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return 0;
        }

        return Directory.EnumerateFiles(path, "*.uasset", SearchOption.AllDirectories).Count();
    }

    private static IReadOnlyList<UnrealProjectSyncCharacterCandidate> BuildManifestCharacterCandidates(UnrealProjectExportManifest? manifest)
    {
        if (manifest is null)
        {
            return [];
        }

        var assets = manifest.Assets
            .Where(asset => !string.IsNullOrWhiteSpace(asset.PackagePath))
            .ToArray();
        var assetLookup = UnrealCharacterPreviewFactory.BuildExportAssetLookup(assets);
        var baseGroups = GroupByCharacterFolder(assets, UnrealProjectSyncService.TargetBaseMaterialContentPath);
        var zdGroups = GroupByCharacterFolder(assets, UnrealProjectSyncService.TargetZdContentPath);
        var actorMap = manifest.CharacterActors
            .GroupBy(actor => actor.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var sequenceMap = manifest.CharacterSequences
            .GroupBy(sequence => sequence.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var buffMap = manifest.CharacterBuffs
            .GroupBy(buffSet => buffSet.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var candidateCodes = baseGroups.Keys
            .Union(zdGroups.Keys, StringComparer.OrdinalIgnoreCase)
            .Union(manifest.CharacterItems.Select(item => item.Code), StringComparer.OrdinalIgnoreCase)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return candidateCodes
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .Select(code =>
            {
                var baseAssets = baseGroups.TryGetValue(code, out var matchedBaseAssets) ? matchedBaseAssets : [];
                var zdAssets = zdGroups.TryGetValue(code, out var matchedZdAssets) ? matchedZdAssets : [];
                var personalBuffRoot = $"{UnrealProjectSyncService.TargetZdContentPath}/{code}/BUFF";
                var materialAssets = baseAssets
                    .Concat(zdAssets.Where(asset =>
                        IsTextureAsset(asset) &&
                        (string.Equals(asset.PackagePath, personalBuffRoot, StringComparison.OrdinalIgnoreCase) ||
                         asset.PackagePath.StartsWith(personalBuffRoot + "/", StringComparison.OrdinalIgnoreCase))))
                    .ToArray();
                var zdMaterialTextureCount = CountZdMaterialTextures(zdAssets, code);
                var characterItem = ResolveCharacterItem(code, baseAssets, zdAssets, manifest.CharacterItems);
                actorMap.TryGetValue(code, out var characterActor);
                sequenceMap.TryGetValue(code, out var characterSequence);
                buffMap.TryGetValue(code, out var characterBuffs);
                var sequencePreview = UnrealCharacterPreviewFactory.BuildSequenceFramesPreview(characterSequence);
                return new UnrealProjectSyncCharacterCandidate(
                    code,
                    ResolveDisplayName(code, characterItem),
                    $"{UnrealProjectSyncService.TargetBaseMaterialContentPath}/{code}",
                    $"{UnrealProjectSyncService.TargetZdContentPath}/{code}",
                    baseAssets.Count,
                    zdMaterialTextureCount,
                    UnrealCharacterPreviewFactory.BuildCharacterInfoPreview(characterItem, characterActor),
                    UnrealCharacterPreviewFactory.BuildSkillsPreview(characterActor, characterItem, manifest.LinkSkillLibrary, manifest.SupportSkillLibrary, assetLookup),
                    sequencePreview,
                    UnrealCharacterPreviewFactory.BuildBuffsPreview(characterBuffs),
                    UnrealMaterialClassifier.BuildMaterialBuckets(materialAssets),
                    hasLatestData: manifest.SchemaVersion >= CurrentExportSchemaVersion &&
                        HasDetailedCharacterData(characterItem, characterActor, characterSequence, characterBuffs),
                    voiceBuckets: UnrealMaterialClassifier.BuildVoiceBuckets(zdAssets, sequencePreview));
            })
            .ToArray();
    }

    private static bool HasDetailedCharacterData(
        UnrealProjectExportCharacterItem? characterItem,
        UnrealProjectExportCharacterActor? characterActor,
        UnrealProjectExportCharacterSequence? characterSequence,
        UnrealProjectExportCharacterBuffSet? characterBuffs)
    {
        return characterItem is not null ||
            characterActor is not null ||
            characterSequence is not null ||
            characterBuffs is not null;
    }

    private static UnrealProjectExportCharacterItem? ResolveCharacterItem(
        string code,
        IReadOnlyList<UnrealProjectExportAsset> baseAssets,
        IReadOnlyList<UnrealProjectExportAsset> zdAssets,
        IReadOnlyList<UnrealProjectExportCharacterItem> items)
    {
        if (items.Count == 0)
        {
            return null;
        }

        var assetNames = baseAssets
            .Concat(zdAssets)
            .Select(asset => asset.AssetName)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return items
            .Select(item => new
            {
                Item = item,
                Score = ScoreCharacterItemMatch(code, assetNames, item)
            })
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Item.Code, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Item)
            .FirstOrDefault();
    }

    private static int ScoreCharacterItemMatch(
        string code,
        IReadOnlyList<string> assetNames,
        UnrealProjectExportCharacterItem item)
    {
        var score = 0;
        var normalizedCode = NormalizeCharacterIdentity(code);
        var itemCode = NormalizeCharacterIdentity(item.Code);
        var itemAssetName = NormalizeCharacterIdentity(item.AssetName);
        var itemNameTokens = BuildIdentityTokens(item.Code)
            .Concat(BuildIdentityTokens(item.AssetName))
            .Concat(BuildIdentityTokens(item.ItemData.Name))
            .Concat(item.ItemData.Keywords.SelectMany(BuildIdentityTokens))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!string.IsNullOrWhiteSpace(normalizedCode))
        {
            if (string.Equals(normalizedCode, itemCode, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedCode, itemAssetName, StringComparison.OrdinalIgnoreCase))
            {
                score += 1000;
            }

            var folderTokens = BuildIdentityTokens(code).ToArray();
            if (folderTokens.Length > 0 && folderTokens.All(token => itemNameTokens.Contains(token, StringComparer.OrdinalIgnoreCase)))
            {
                score += 260;
            }

            var sharedFolderTokens = folderTokens.Count(token => itemNameTokens.Contains(token, StringComparer.OrdinalIgnoreCase));
            score += sharedFolderTokens * 80;

            var distance = LevenshteinDistance(normalizedCode, itemCode);
            if (distance > 0 && distance <= 3)
            {
                score += 180 - distance * 35;
            }
        }

        foreach (var assetName in assetNames.Take(80))
        {
            var normalizedAssetName = NormalizeCharacterIdentity(assetName);
            if (string.IsNullOrWhiteSpace(normalizedAssetName))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(itemCode) &&
                normalizedAssetName.Contains(itemCode, StringComparison.OrdinalIgnoreCase))
            {
                score += 220;
            }

            var assetTokens = BuildIdentityTokens(assetName).ToArray();
            var sharedAssetTokens = assetTokens.Count(token => itemNameTokens.Contains(token, StringComparer.OrdinalIgnoreCase));
            score += sharedAssetTokens * 45;
        }

        return score;
    }

    private static IEnumerable<string> BuildIdentityTokens(string? value)
    {
        var text = value ?? string.Empty;
        foreach (Match match in Regex.Matches(text, @"[A-Za-z0-9]+|[\u4e00-\u9fff]+"))
        {
            var token = match.Value.Trim().ToLowerInvariant();
            if (token.Length >= 2 && token != "item" && token != "image")
            {
                yield return token;
            }
        }
    }

    private static string NormalizeCharacterIdentity(string? value)
    {
        var tokens = BuildIdentityTokens(value).ToArray();
        return tokens.Length == 0 ? string.Empty : string.Concat(tokens);
    }

    private static int LevenshteinDistance(string left, string right)
    {
        if (string.IsNullOrEmpty(left))
        {
            return right.Length;
        }

        if (string.IsNullOrEmpty(right))
        {
            return left.Length;
        }

        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = char.ToLowerInvariant(left[i - 1]) == char.ToLowerInvariant(right[j - 1]) ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private static string ResolveDisplayName(string code, UnrealProjectExportCharacterItem? item)
    {
        var name = item?.ItemData?.Name;
        return string.IsNullOrWhiteSpace(name) ? code : name.Trim();
    }

    private static int CountZdMaterialTextures(IReadOnlyList<UnrealProjectExportAsset> zdAssets, string code)
    {
        var materialRoot = $"{UnrealProjectSyncService.TargetZdContentPath}/{code}/Material";
        return zdAssets.Count(asset =>
            asset.PackagePath.StartsWith(materialRoot, StringComparison.OrdinalIgnoreCase) &&
            IsTextureAsset(asset));
    }

    private static bool IsTextureAsset(UnrealProjectExportAsset asset)
    {
        return asset.AssetClass.Contains("Texture", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, List<UnrealProjectExportAsset>> GroupByCharacterFolder(
        IReadOnlyList<UnrealProjectExportAsset> assets,
        string targetRoot)
    {
        var result = new Dictionary<string, List<UnrealProjectExportAsset>>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in assets)
        {
            if (!TryGetCharacterFolder(asset.PackagePath, targetRoot, out var code))
            {
                continue;
            }

            if (!result.TryGetValue(code, out var list))
            {
                list = [];
                result[code] = list;
            }

            list.Add(asset);
        }

        return result;
    }

    private static bool TryGetCharacterFolder(string packagePath, string targetRoot, out string code)
    {
        code = string.Empty;
        var normalizedPackagePath = packagePath.TrimEnd('/');
        var normalizedTargetRoot = targetRoot.TrimEnd('/');
        if (!normalizedPackagePath.StartsWith($"{normalizedTargetRoot}/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relative = normalizedPackagePath[(normalizedTargetRoot.Length + 1)..];
        var firstSlash = relative.IndexOf('/');
        code = firstSlash >= 0 ? relative[..firstSlash] : relative;
        return !string.IsNullOrWhiteSpace(code);
    }

    /// <summary>
    /// 时间戳是 Python 侧 <c>datetime.isoformat()</c> 写的 ISO-8601 文本，属于机器数据。
    /// 按当前区域解析等于让「这份清单是什么时候写的」跟着用户的区域设置走；实测 .NET 8
    /// 对 ISO-8601 够宽容、常见区域都解析得出来，但那是运行时实现细节，不该当依据。
    /// </summary>
    internal static DateTime? ParseGeneratedAt(string? generatedAt)
    {
        return DateTime.TryParse(
            generatedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
            ? value
            : null;
    }
}
