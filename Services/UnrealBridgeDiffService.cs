using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CrossingVoidZDTool.Services;

internal sealed class UnrealBridgeDiffService
{
    /// <summary>Unreal 语义快照 payload 的字段分隔符，与 UnrealBridgeSemanticSnapshotService.Join 一致。</summary>
    private const char SemanticPayloadSeparator = (char)0x1F;

    public IReadOnlyList<UnrealBridgeChange> Compare(
        UnrealBridgeSnapshot toolbox,
        UnrealBridgeSnapshot unreal,
        UnrealBridgeDirection direction,
        UnrealBridgeSyncState? baseline)
    {
        ArgumentNullException.ThrowIfNull(toolbox);
        ArgumentNullException.ThrowIfNull(unreal);

        var toolboxItems = ToItemMap(toolbox.Items, "工具箱");
        var unrealItems = ToItemMap(unreal.Items, "Unreal");
        if (direction == UnrealBridgeDirection.PublishToUnreal)
        {
            AlignCanonicalMaterialItems(toolboxItems, unrealItems, toolbox.CharacterCode);
        }

        // 序列帧两侧现在共用「动作 + 帧位置」稳定 ID，不再需要事后对齐。
        // 这里预先算出每一帧同步后应有的贴图路径，用来区分「已经是规范命名」和「本次要改名过去」。
        var canonicalSequenceTexturePaths = direction == UnrealBridgeDirection.PublishToUnreal
            ? BuildCanonicalSequenceTexturePaths(toolboxItems, toolbox.CharacterCode)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // 每个动作同步之后应有的资产名；Unreal 侧多出来的资产就是要清理的历史素材。
        var canonicalSequenceAssetPaths = BuildCanonicalSequenceAssetPaths(toolboxItems, toolbox.CharacterCode);
        var changes = toolboxItems.Keys
            .Union(unrealItems.Keys, StringComparer.OrdinalIgnoreCase)
            .Select(stableId => BuildChange(
                stableId,
                toolboxItems.GetValueOrDefault(stableId),
                unrealItems.GetValueOrDefault(stableId),
                direction,
                baseline,
                direction == UnrealBridgeDirection.PublishToUnreal &&
                    toolboxItems.TryGetValue(stableId, out var matchedToolboxItem) &&
                    unrealItems.TryGetValue(stableId, out var matchedUnrealItem) &&
                    IsMigrationSafePair(matchedToolboxItem, matchedUnrealItem, toolbox.CharacterCode, canonicalSequenceTexturePaths),
                direction == UnrealBridgeDirection.PublishToUnreal &&
                    toolboxItems.TryGetValue(stableId, out var renameToolboxItem) &&
                    unrealItems.TryGetValue(stableId, out var renameUnrealItem) &&
                    NeedsCanonicalSequenceRename(renameToolboxItem, renameUnrealItem, canonicalSequenceTexturePaths)))
            .SelectMany(ExpandSequenceChange)
            .Where(change => !IsCanonicalOwnedSequenceAsset(change, canonicalSequenceAssetPaths))
            .OrderBy(change => change.Module)
            .ThenBy(change => change.StableId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return changes;
    }

    /// <summary>
    /// 为每个工具箱序列帧算出「同步之后应有的贴图对象路径」。
    /// 编号位宽跟随该动作的总帧数，所以必须按动作分组之后再算。
    /// </summary>
    private static Dictionary<string, string> BuildCanonicalSequenceTexturePaths(
        IDictionary<string, UnrealBridgeSnapshotItem> toolboxItems,
        string characterCode)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(characterCode))
        {
            return result;
        }

        var groups = toolboxItems.Values
            .Where(item => item.Module == UnrealBridgeModule.SequenceFrames &&
                SequenceFrameIdentity.IsFrameStableId(item.StableId))
            .GroupBy(item => item.ParentStableId, StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            var frames = group.ToArray();
            foreach (var item in frames)
            {
                var actionCode = ExtractActionCode(item.PayloadJson);
                var ordinal = ParseFrameOrdinal(item.StableId);
                if (ordinal < 0 || ordinal >= frames.Length)
                {
                    continue;
                }

                var path = SequenceFrameIdentity.BuildCanonicalTextureObjectPath(
                    characterCode, actionCode, ordinal, frames.Length);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    result[item.StableId] = path;
                }
            }
        }

        return result;
    }

    /// <summary>按动作汇总「同步后应当存在的资产包路径」。</summary>
    private static Dictionary<string, IReadOnlyCollection<string>> BuildCanonicalSequenceAssetPaths(
        IDictionary<string, UnrealBridgeSnapshotItem> toolboxItems,
        string characterCode)
    {
        var result = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);
        var groups = toolboxItems.Values
            .Where(item => item.Module == UnrealBridgeModule.SequenceFrames &&
                SequenceFrameIdentity.IsFrameStableId(item.StableId))
            .GroupBy(item => item.ParentStableId, StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            var frames = group.ToArray();
            var actionCode = ExtractActionCode(frames[0].PayloadJson);
            result[group.Key] = SequenceFrameIdentity.BuildCanonicalAssetPackagePaths(
                characterCode, actionCode, frames.Length);
        }

        return result;
    }

    /// <summary>规范位置上的资产由帧节点负责，不再单独列成删除项。</summary>
    private static bool IsCanonicalOwnedSequenceAsset(
        UnrealBridgeChange change,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> canonicalAssetPaths)
    {
        if (!SequenceFrameIdentity.IsOwnedAssetStableId(change.StableId))
        {
            return false;
        }

        var objectPath = change.UnrealItem?.SourceObjectPath ?? string.Empty;
        return canonicalAssetPaths.TryGetValue(change.SequenceGroupKey, out var paths) &&
            paths.Contains(SequenceFrameIdentity.NormalizePackagePath(objectPath));
    }

    private static int ParseFrameOrdinal(string stableId)
    {
        var separator = (stableId ?? string.Empty).LastIndexOf(':');
        return separator >= 0 && int.TryParse(stableId![(separator + 1)..], out var ordinal) ? ordinal : -1;
    }

    /// <summary>
    /// 两侧 payload 格式不同：工具箱是 JSON，Unreal 语义快照是分隔符连接的字段串，
    /// 其中 isBlank 是最后一个字段。不能整串找 "True"，否则任何含该词的字段都会误判。
    /// </summary>
    private static bool IsBlankSequenceFrame(UnrealBridgeSnapshotItem item) =>
        SequenceFrameIdentity.IsBlankFramePayload(item.PayloadJson);

    /// <summary>该帧的 Unreal 资产已经落在规范命名上，可以直接作为同步基线。</summary>
    private static bool IsCanonicalSequenceFramePair(
        UnrealBridgeSnapshotItem toolboxItem,
        UnrealBridgeSnapshotItem unrealItem,
        IReadOnlyDictionary<string, string> canonicalTexturePaths)
    {
        if (toolboxItem.Module != UnrealBridgeModule.SequenceFrames ||
            unrealItem.Module != UnrealBridgeModule.SequenceFrames ||
            !SequenceFrameIdentity.IsFrameStableId(toolboxItem.StableId))
        {
            return false;
        }

        // 空白帧两侧都没有贴图资产，只要都是空白就算一致。
        if (IsBlankSequenceFrame(toolboxItem) && IsBlankSequenceFrame(unrealItem))
        {
            return true;
        }

        return canonicalTexturePaths.TryGetValue(toolboxItem.StableId, out var canonicalPath) &&
            SameObjectPath(unrealItem.SourceObjectPath, canonicalPath);
    }

    /// <summary>该帧在 Unreal 里还是历史命名，本次同步会把它改名到规范位置。</summary>
    private static bool NeedsCanonicalSequenceRename(
        UnrealBridgeSnapshotItem toolboxItem,
        UnrealBridgeSnapshotItem unrealItem,
        IReadOnlyDictionary<string, string> canonicalTexturePaths)
    {
        if (toolboxItem.Module != UnrealBridgeModule.SequenceFrames ||
            unrealItem.Module != UnrealBridgeModule.SequenceFrames ||
            !SequenceFrameIdentity.IsFrameStableId(toolboxItem.StableId))
        {
            return false;
        }

        return !IsCanonicalSequenceFramePair(toolboxItem, unrealItem, canonicalTexturePaths);
    }
    private static IEnumerable<UnrealBridgeChange> ExpandSequenceChange(UnrealBridgeChange change)
    {
        var sequenceGroupKey = ComputeSequenceGroupKey(change);

        if (change.Module != UnrealBridgeModule.SequenceFrames ||
            !change.StableId.StartsWith("sequence-frame:", StringComparison.OrdinalIgnoreCase) ||
            change.ToolboxItem is null || change.UnrealItem is null ||
            change.Kind is not (UnrealBridgeChangeKind.Updated or UnrealBridgeChangeKind.Conflict or UnrealBridgeChangeKind.Renamed))
        {
            yield return change with { SequenceGroupKey = sequenceGroupKey };
            yield break;
        }

        yield return change with
        {
            StableId = $"{change.StableId}:delete",
            DisplayName = $"{change.DisplayName}（旧）",
            Kind = UnrealBridgeChangeKind.DeleteCandidate,
            ToolboxItem = null,
            IsSelected = change.IsSelected,
            SequenceGroupKey = sequenceGroupKey
        };
        yield return change with
        {
            StableId = $"{change.StableId}:add",
            DisplayName = $"{change.DisplayName}（新）",
            Kind = UnrealBridgeChangeKind.Added,
            UnrealItem = null,
            IsSelected = change.IsSelected,
            SequenceGroupKey = sequenceGroupKey
        };
    }

    private static void AlignCanonicalMaterialItems(
        IDictionary<string, UnrealBridgeSnapshotItem> toolboxItems,
        IDictionary<string, UnrealBridgeSnapshotItem> unrealItems,
        string characterCode)
    {
        if (string.IsNullOrWhiteSpace(characterCode))
        {
            return;
        }

        var claimedUnrealIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var toolboxPair in toolboxItems.ToArray())
        {
            var toolboxItem = toolboxPair.Value;
            if (unrealItems.ContainsKey(toolboxPair.Key) ||
                toolboxItem.Module is not (UnrealBridgeModule.BaseMaterials or UnrealBridgeModule.Voices))
            {
                continue;
            }

            var canonicalPath = BuildCanonicalObjectPath(characterCode, toolboxItem);
            if (string.IsNullOrWhiteSpace(canonicalPath))
            {
                continue;
            }

            var matchingPair = unrealItems.FirstOrDefault(pair =>
                !claimedUnrealIds.Contains(pair.Key) &&
                pair.Value.Module == toolboxItem.Module &&
                SameObjectPath(pair.Value.SourceObjectPath, canonicalPath));
            if (string.IsNullOrWhiteSpace(matchingPair.Key))
            {
                var expectedFolder = GetCanonicalFolder(characterCode, toolboxItem);
                var expectedName = NormalizeAssetName(GetToolboxAssetName(toolboxItem));
                var fallbackMatches = unrealItems
                    .Where(pair =>
                        !claimedUnrealIds.Contains(pair.Key) &&
                        pair.Value.Module == toolboxItem.Module &&
                        string.Equals(GetObjectFolder(pair.Value.SourceObjectPath), expectedFolder, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(NormalizeAssetName(GetObjectAssetName(pair.Value.SourceObjectPath)), expectedName, StringComparison.Ordinal))
                    .ToArray();
                matchingPair = fallbackMatches.Length == 1 ? fallbackMatches[0] : default;
            }
            if (string.IsNullOrWhiteSpace(matchingPair.Key))
            {
                continue;
            }

            unrealItems.Remove(matchingPair.Key);
            unrealItems[toolboxPair.Key] = matchingPair.Value with { StableId = toolboxPair.Key };
            claimedUnrealIds.Add(toolboxPair.Key);
        }
    }

    private static string BuildCanonicalObjectPath(string characterCode, UnrealBridgeSnapshotItem toolboxItem)
    {
        var relative = toolboxItem.ToolboxRelativePath.Replace('\\', '/').Trim('/');
        var fileName = Path.GetFileNameWithoutExtension(
            string.IsNullOrWhiteSpace(relative) ? toolboxItem.AssetPath : relative);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = toolboxItem.NormalizedName;
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        if (toolboxItem.Module == UnrealBridgeModule.Voices &&
            relative.StartsWith("Sound/", StringComparison.OrdinalIgnoreCase))
        {
            var relativeFolder = relative[..relative.LastIndexOf('/')];
            return $"/Game/GameActor2D/{characterCode}/{relativeFolder}/{fileName}.{fileName}";
        }

        if (toolboxItem.Module == UnrealBridgeModule.BaseMaterials &&
            relative.StartsWith("AssetMaterial/BuffIcon/", StringComparison.OrdinalIgnoreCase))
        {
            return $"/Game/GameActor2D/{characterCode}/BUFF/{fileName}.{fileName}";
        }

        if (toolboxItem.Module == UnrealBridgeModule.BaseMaterials &&
            relative.StartsWith("AssetMaterial/", StringComparison.OrdinalIgnoreCase))
        {
            return $"/Game/AssetMaterial/ImageS/CharaterS/{characterCode}/{fileName}.{fileName}";
        }

        return string.Empty;
    }

    private static bool SameObjectPath(string left, string right) =>
        string.Equals(NormalizeObjectPath(left), NormalizeObjectPath(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeObjectPath(string value)
    {
        var path = (value ?? string.Empty).Trim().Replace('\\', '/');
        var dotIndex = path.IndexOf('.', StringComparison.Ordinal);
        return dotIndex >= 0 ? path[..dotIndex] : path;
    }

    private static string GetCanonicalFolder(string characterCode, UnrealBridgeSnapshotItem toolboxItem)
    {
        var relative = toolboxItem.ToolboxRelativePath.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(characterCode) || string.IsNullOrWhiteSpace(relative))
        {
            return string.Empty;
        }

        var separator = relative.LastIndexOf('/');
        if (separator < 0)
        {
            return string.Empty;
        }

        var folder = relative[..separator];
        if (toolboxItem.Module == UnrealBridgeModule.Voices &&
            relative.StartsWith("Sound/", StringComparison.OrdinalIgnoreCase))
        {
            return $"/Game/GameActor2D/{characterCode}/{folder}";
        }

        if (toolboxItem.Module == UnrealBridgeModule.BaseMaterials &&
            relative.StartsWith("AssetMaterial/BuffIcon/", StringComparison.OrdinalIgnoreCase))
        {
            return $"/Game/GameActor2D/{characterCode}/BUFF";
        }

        if (toolboxItem.Module == UnrealBridgeModule.BaseMaterials &&
            relative.StartsWith("AssetMaterial/", StringComparison.OrdinalIgnoreCase))
        {
            return $"/Game/AssetMaterial/ImageS/CharaterS/{characterCode}";
        }

        return string.Empty;
    }

    private static bool IsMigrationSafePair(
        UnrealBridgeSnapshotItem toolboxItem,
        UnrealBridgeSnapshotItem unrealItem,
        string characterCode,
        IReadOnlyDictionary<string, string> canonicalSequenceTexturePaths)
    {
        if (toolboxItem.Module == UnrealBridgeModule.SequenceFrames)
        {
            return IsCanonicalSequenceFramePair(toolboxItem, unrealItem, canonicalSequenceTexturePaths);
        }

        if (toolboxItem.Module is not (UnrealBridgeModule.BaseMaterials or UnrealBridgeModule.Voices) ||
            unrealItem.Module != toolboxItem.Module ||
            string.IsNullOrWhiteSpace(unrealItem.SourceObjectPath))
        {
            return false;
        }

        var expectedFolder = GetCanonicalFolder(characterCode, toolboxItem);
        var expectedName = NormalizeAssetName(GetToolboxAssetName(toolboxItem));
        return !string.IsNullOrWhiteSpace(expectedFolder) &&
            string.Equals(GetObjectFolder(unrealItem.SourceObjectPath), expectedFolder, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                NormalizeAssetName(GetObjectAssetName(unrealItem.SourceObjectPath)),
                expectedName,
                StringComparison.Ordinal);
    }

    private static string GetToolboxAssetName(UnrealBridgeSnapshotItem item) =>
        Path.GetFileNameWithoutExtension(
            string.IsNullOrWhiteSpace(item.ToolboxRelativePath)
                ? item.AssetPath
                : item.ToolboxRelativePath);

    private static string GetObjectFolder(string objectPath)
    {
        var package = NormalizeObjectPath(objectPath);
        var separator = package.LastIndexOf('/');
        return separator > 0 ? package[..separator] : string.Empty;
    }

    private static string GetObjectAssetName(string objectPath)
    {
        var package = NormalizeObjectPath(objectPath);
        var separator = package.LastIndexOf('/');
        return separator >= 0 ? package[(separator + 1)..] : package;
    }

    private static string NormalizeAssetName(string value) =>
        new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static Dictionary<string, UnrealBridgeSnapshotItem> ToItemMap(
        IReadOnlyList<UnrealBridgeSnapshotItem> items,
        string sourceName)
    {
        var result = new Dictionary<string, UnrealBridgeSnapshotItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.StableId))
            {
                throw new InvalidOperationException($"{sourceName}快照包含空的稳定 ID。");
            }

            if (result.ContainsKey(item.StableId))
            {
                throw new InvalidOperationException(
                    $"{sourceName}快照包含重复稳定 ID：{item.StableId}。请检查快照生成和稳定 ID 规范化逻辑。");
            }

            result[item.StableId] = item;
        }

        return result;
    }

    private static UnrealBridgeChange BuildChange(
        string stableId,
        UnrealBridgeSnapshotItem? toolboxItem,
        UnrealBridgeSnapshotItem? unrealItem,
        UnrealBridgeDirection direction,
        UnrealBridgeSyncState? baseline,
        bool isMigrationSafePair,
        bool sequenceNeedsCanonicalRename)
    {
        var sourceItem = direction == UnrealBridgeDirection.PublishToUnreal ? toolboxItem : unrealItem;
        var targetItem = direction == UnrealBridgeDirection.PublishToUnreal ? unrealItem : toolboxItem;
        var referenceItem = sourceItem ?? targetItem
            ?? throw new InvalidOperationException($"无法解析差异项：{stableId}。");
        UnrealBridgeSyncStateEntry? stateEntry = null;
        baseline?.Entries.TryGetValue(stableId, out stateEntry);
        var isRename = IsRename(direction, toolboxItem, unrealItem, stateEntry);

        UnrealBridgeChangeKind kind;
        if (sourceItem is not null && targetItem is null)
        {
            kind = UnrealBridgeChangeKind.Added;
        }
        else if (sourceItem is null)
        {
            kind = UnrealBridgeChangeKind.DeleteCandidate;
        }
        else if (stateEntry is not null)
        {
            var baselineSourceHash = direction == UnrealBridgeDirection.PublishToUnreal
                ? stateEntry.ToolboxHash
                : stateEntry.UnrealHash;
            var baselineTargetHash = direction == UnrealBridgeDirection.PublishToUnreal
                ? stateEntry.UnrealHash
                : stateEntry.ToolboxHash;
            var sourceChanged = !string.Equals(sourceItem.ContentHash, baselineSourceHash, StringComparison.OrdinalIgnoreCase);
            var targetChanged = !string.Equals(targetItem!.ContentHash, baselineTargetHash, StringComparison.OrdinalIgnoreCase);
            // 两侧当前就是一致的：没有东西可同步，也谈不上冲突。
            // 基线可能停在更早的状态——比如某些动作的 fps 曾经两边不一样，那时记下的
            // 两个哈希互不相同；同步把两边弄一致之后，只凭「相对基线都变了」会把这些
            // 已经同步好的动作永久判成冲突，差异列表再也归不了零。
            kind = string.Equals(sourceItem.ContentHash, targetItem.ContentHash, StringComparison.OrdinalIgnoreCase)
                ? isRename || sequenceNeedsCanonicalRename
                    ? UnrealBridgeChangeKind.Renamed
                    : UnrealBridgeChangeKind.Unchanged
                : targetChanged
                    ? UnrealBridgeChangeKind.Conflict
                    : isRename
                        ? UnrealBridgeChangeKind.Renamed
                        : sourceChanged
                            ? UnrealBridgeChangeKind.Updated
                            // 有基线条目、两侧哈希也都没变，但资产还停在历史命名上：
                            // 仍然要判成改名，否则该改名的帧永远不会出现在第五步列表里。
                            : sequenceNeedsCanonicalRename
                                ? UnrealBridgeChangeKind.Renamed
                                : UnrealBridgeChangeKind.Unchanged;
        }
        // 走到这里说明这一项在基线里没有记录。两侧的哈希本来就不可直接比较
        // （工具箱是源文件，Unreal 是导出的资产），所以只要资产已经在规范位置、
        // 用的就是工具箱会给的规范名，就当作已同步，并在这一轮补记基线。
        //
        // 这里以前还要求「整个基线文件为空」，只覆盖得了「第一次同步」。
        // 于是把 Unreal 工程换个盘符之后，旧基线按项目路径散列存在另一个文件里，
        // 新路径的基线只有序列帧那几十条；素材项因为查不到记录又不满足这个条件，
        // 全部被判成冲突——而冲突不能自动执行，第三步就此卡死，列表再也归不了零。
        else if (isMigrationSafePair)
        {
            kind = UnrealBridgeChangeKind.Unchanged;
        }
        else if (string.Equals(sourceItem.ContentHash, targetItem!.ContentHash, StringComparison.OrdinalIgnoreCase))
        {
            kind = isRename ? UnrealBridgeChangeKind.Renamed : UnrealBridgeChangeKind.Unchanged;
        }
        else if (sequenceNeedsCanonicalRename)
        {
            // 还是历史命名，本次同步会改名过去；这不是冲突，冲突要留给「基线之后 Unreal 侧被改动」。
            kind = UnrealBridgeChangeKind.Renamed;
        }
        else
        {
            kind = UnrealBridgeChangeKind.Conflict;
        }

        return new UnrealBridgeChange(
            stableId,
            referenceItem.Module,
            referenceItem.DisplayName,
            kind,
            toolboxItem,
            unrealItem,
            false,
            stableId);
    }

    private static string ComputeSequenceGroupKey(UnrealBridgeChange change)
    {
        if (change.Module != UnrealBridgeModule.SequenceFrames)
        {
            return change.StableId;
        }

        // 孤儿序列不属于任何动作，全部归到同一个分组下。
        if (SequenceFrameIdentity.IsOrphanSequenceStableId(change.StableId))
        {
            return SequenceFrameIdentity.OrphanGroupStableId;
        }

        // 帧是「动作键 + 位置」，占用资产是「动作键 + 资产名」；两者都取最后一个冒号之前的部分。
        var prefix = SequenceFrameIdentity.IsFrameStableId(change.StableId)
            ? SequenceFrameIdentity.FramePrefix
            : SequenceFrameIdentity.IsOwnedAssetStableId(change.StableId)
                ? SequenceFrameIdentity.OwnedAssetPrefix
                : string.Empty;
        if (prefix.Length > 0)
        {
            var separator = change.StableId.LastIndexOf(':');
            if (separator > prefix.Length)
            {
                var actionKey = change.StableId[prefix.Length..separator];
                if (!string.IsNullOrWhiteSpace(actionKey))
                {
                    return SequenceFrameIdentity.ActionPrefix + actionKey;
                }
            }
        }

        if (SequenceFrameIdentity.IsActionStableId(change.StableId))
        {
            return change.StableId;
        }

        var payloadJson = change.ToolboxItem?.PayloadJson
            ?? change.UnrealItem?.PayloadJson
            ?? string.Empty;
        var actionCode = ExtractActionCode(payloadJson);
        return string.IsNullOrWhiteSpace(actionCode)
            ? change.StableId
            : SequenceFrameIdentity.BuildActionStableId(actionCode);
    }

    private static string ExtractActionCode(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return string.Empty;
        }

        var trimmed = payloadJson.Trim();
        if (trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                if (doc.RootElement.TryGetProperty("actionCode", out var element) &&
                    element.ValueKind == JsonValueKind.String)
                {
                    return element.GetString() ?? string.Empty;
                }
            }
            catch (JsonException)
            {
                // Fall through to the pipe-delimited format.
            }
        }

        var parts = trimmed.Split('|', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : string.Empty;
    }

    private static bool IsRename(
        UnrealBridgeDirection direction,
        UnrealBridgeSnapshotItem? toolboxItem,
        UnrealBridgeSnapshotItem? unrealItem,
        UnrealBridgeSyncStateEntry? stateEntry)
    {
        if (direction == UnrealBridgeDirection.PublishToUnreal)
        {
            var toolboxPathChanged = stateEntry is not null &&
                toolboxItem is not null &&
                !string.IsNullOrWhiteSpace(stateEntry.ToolboxRelativePath) &&
                !string.IsNullOrWhiteSpace(toolboxItem.ToolboxRelativePath) &&
                !string.Equals(
                    stateEntry.ToolboxRelativePath,
                    toolboxItem.ToolboxRelativePath,
                    StringComparison.OrdinalIgnoreCase);
            if (toolboxPathChanged)
            {
                return true;
            }

            return toolboxItem is not null &&
                unrealItem is not null &&
                toolboxItem.Module == UnrealBridgeModule.Voices &&
                UnrealBridgeVoicePathPolicy.TryBuildCanonicalObjectPath(
                    unrealItem.SourceObjectPath,
                    toolboxItem.PayloadJson,
                    toolboxItem.NormalizedName,
                    out var canonicalPath) &&
                !string.Equals(canonicalPath, unrealItem.SourceObjectPath, StringComparison.OrdinalIgnoreCase);
        }

        return stateEntry is not null &&
            unrealItem is not null &&
            !string.IsNullOrWhiteSpace(stateEntry.UnrealObjectPath) &&
            !string.IsNullOrWhiteSpace(unrealItem.SourceObjectPath) &&
            !string.Equals(
                stateEntry.UnrealObjectPath,
                unrealItem.SourceObjectPath,
                StringComparison.OrdinalIgnoreCase);
    }
}
