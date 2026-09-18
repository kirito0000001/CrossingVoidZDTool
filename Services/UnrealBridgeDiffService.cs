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
        // 「布局已经对齐」的动作。只有这些动作的帧才谈得上按精灵名逐帧核对：
        // 布局对齐说明 Unreal 那边已经是这一套产物（自己的图集，或者借来的来源图集），
        // 精灵名才是可靠的帧身份。
        //
        // 还停在逐帧旧布局的动作（例如 frames:Click_Frame0,Click_Frame1…），
        // 精灵名是历史导入留下的、按帧序号命名的，拿它去比工具箱按素材序号算出来的名字
        // 只会把没问题的帧判成要重做；这类动作该由**动作节点**报「需要重建」，
        // 逐帧的结论没有意义。
        var atlasLayoutActions = direction == UnrealBridgeDirection.PublishToUnreal
            ? CollectLayoutAlignedActions(toolboxItems, unrealItems)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                    IsMigrationSafePair(matchedToolboxItem, matchedUnrealItem, toolbox.CharacterCode, canonicalSequenceTexturePaths, atlasLayoutActions),
                direction == UnrealBridgeDirection.PublishToUnreal &&
                    toolboxItems.TryGetValue(stableId, out var renameToolboxItem) &&
                    unrealItems.TryGetValue(stableId, out var renameUnrealItem) &&
                    NeedsCanonicalSequenceRename(renameToolboxItem, renameUnrealItem, canonicalSequenceTexturePaths, atlasLayoutActions)))
            .SelectMany(ExpandSequenceChange)
            .Where(change => !IsCanonicalOwnedSequenceAsset(change, canonicalSequenceAssetPaths))
            .Where(change => !IsAdditiveOnlyDeleteCandidate(change))
            .OrderBy(change => change.Module)
            .ThenBy(change => change.StableId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return ExpandSequenceLayoutRebuilds(changes);
    }

    /// <summary>
    /// 布局换代时，把动作级的「需要重建」摊成它下面每一帧的删除 + 新增。
    ///
    /// 非摊开不可：中栏是按帧数的增删来数「删除 N 项 / 新增 N 项」的，
    /// 只留一个动作级的 Updated 节点的话，列表里什么都看不见 ——
    /// 明明几十张旧贴图还挂在动作目录里，界面却落在「没有差异」的空态上。
    ///
    /// 帧自己的稳定 ID 不能重复出现，所以删除和新增各挂 <c>:delete</c>/<c>:add</c> 后缀 ——
    /// 与 <see cref="ExpandSequenceChange"/> 处理内容变化时用的是同一套约定。
    /// </summary>
    private static IReadOnlyList<UnrealBridgeChange> ExpandSequenceLayoutRebuilds(
        IReadOnlyList<UnrealBridgeChange> changes)
    {
        var rebuildActions = changes
            .Where(change => change.Module == UnrealBridgeModule.SequenceFrames &&
                SequenceFrameIdentity.IsActionStableId(change.StableId) &&
                change.Kind is UnrealBridgeChangeKind.Updated or UnrealBridgeChangeKind.Renamed)
            .Select(change => change.StableId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (rebuildActions.Count == 0)
        {
            return changes;
        }

        var result = new List<UnrealBridgeChange>(changes.Count);
        // 新增按**素材**去重，不按帧位：一条序列可以有 23 个帧位而素材只有 17 张，
        // 差的那些是复用位置。复用不需要新建精灵——它们共用同一个。
        // 不去重的话，界面上会写「新增 23 项」，而实际只进 17 张图，对不上。
        var addedSourceByAction = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // 图集贴图每个动作只算一次。它是这一轮真正要导进去的那张图，
        // 但它在检测阶段还不存在（打包发生在同步时），所以没有对应的帧节点，得单独补一条。
        var addedAtlasByAction = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var change in changes)
        {
            result.Add(change);
            if (change.Module != UnrealBridgeModule.SequenceFrames ||
                !SequenceFrameIdentity.IsFrameStableId(change.StableId) ||
                !rebuildActions.Contains(change.ParentStableId))
            {
                continue;
            }

            // 内容也变了的那一帧，前面已经摊成「旧/新」两行了（`:delete` + `:add`）。
            // 这里再摊一次会得到 `:add:add` —— 同一张图被数两遍，界面上的新增数虚高。
            if (change.StableId.EndsWith(":add", StringComparison.OrdinalIgnoreCase) ||
                change.StableId.EndsWith(":delete", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var groupKey = string.IsNullOrWhiteSpace(change.SequenceGroupKey)
                ? change.ParentStableId
                : change.SequenceGroupKey;
            // 删除**不在这里摊**。动作占用的资产（旧贴图、旧精灵、旧 Flipbook）本来就有
            // 自己的条目，那些条目对应的就是磁盘上的文件；再按帧位摊一遍等于把同一批文件
            // 数两次——Sk2 目录里实际 47 个文件，界面上却写「删除 52 项」，就是这么来的。
            if (change.ToolboxItem is null)
            {
                continue;
            }

            var sourceKey = change.ParentStableId + "|" +
                (change.ToolboxItem.AssetPath ?? change.ToolboxItem.ToolboxRelativePath ?? change.StableId);
            if (!addedSourceByAction.Add(sourceKey))
            {
                continue;
            }

            result.Add(change with
            {
                StableId = $"{change.StableId}:add",
                Kind = UnrealBridgeChangeKind.Added,
                UnrealItem = null,
                IsSelected = false,
                SequenceGroupKey = groupKey,
            });

            if (addedAtlasByAction.Add(change.ParentStableId))
            {
                // 整条复用别的动作的图时，这个动作**没有**自己的图集 —— 别补这一条，
                // 否则列表里会凭空多出一个永远建不出来的「图集贴图」。
                if (change.ToolboxItem is not { SourceAtlasIsOwn: true })
                {
                    continue;
                }

                result.Add(change with
                {
                    StableId = SequenceFrameIdentity.BuildAtlasStableId(change.ParentStableId),
                    DisplayName = "图集贴图",
                    Kind = UnrealBridgeChangeKind.Added,
                    UnrealItem = null,
                    IsSelected = false,
                    SequenceGroupKey = groupKey,
                });
            }
        }

        return result;
    }

    /// <summary>
    /// 只做增量的分类：其他图片、其他语音（待分配）、特效素材。
    ///
    /// 这三类在 Unreal 侧本来就可能存着工具箱不认识的东西——手工丢进工程的备用图、
    /// 还没归类的语音、早先在工程里直接做的特效。它们**不要求两侧一一对应**，
    /// 所以「Unreal 多出来」的那些不进差异列表：留着的话列表永远归不了零，
    /// 而且真正的出路只有删掉，那是删用户的素材。
    ///
    /// 新增方向不受影响——工具箱里有、Unreal 没有的，照常出现。
    /// </summary>
    private static bool IsAdditiveOnlyDeleteCandidate(UnrealBridgeChange change)
    {
        if (change.Kind != UnrealBridgeChangeKind.DeleteCandidate)
        {
            return false;
        }

        return change.Module switch
        {
            UnrealBridgeModule.BaseMaterials => IsAdditiveOnlyMaterialKind(ReadChangeKind(change)),
            UnrealBridgeModule.Voices => string.Equals(
                ReadChangeKind(change),
                nameof(VoiceMaterialKind.Other),
                StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    /// <summary>
    /// 读不出来的素材分类一律按「其他图片」算——和
    /// <see cref="UnrealBridgePublishSupportPolicy"/> 里那条注释同一个理由：
    /// 拿不准的时候宁可留着，不要判成可删、更不要凭空列一条待删除出来。
    /// </summary>
    private static bool IsAdditiveOnlyMaterialKind(string kind) =>
        string.IsNullOrWhiteSpace(kind) ||
        string.Equals(kind, nameof(BaseMaterialKind.OtherImage), StringComparison.OrdinalIgnoreCase) ||
        string.Equals(kind, nameof(BaseMaterialKind.Effect), StringComparison.OrdinalIgnoreCase);

    private static string ReadChangeKind(UnrealBridgeChange change)
    {
        // 两侧载荷格式不一样：Unreal 语义快照是 0x1F 分隔的字段串（分类在第一段），
        // 工具箱快照是 JSON（分类在 "kind" 字段）。删除候选通常只在 Unreal 那边存在，
        // 所以先读 Unreal 侧，读不出来再退到工具箱侧。
        var unrealKind = ReadPayloadKind(change.UnrealItem?.PayloadJson);
        return string.IsNullOrWhiteSpace(unrealKind)
            ? ReadPayloadKind(change.ToolboxItem?.PayloadJson)
            : unrealKind;
    }

    private static string ReadPayloadKind(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return string.Empty;
        }

        if (payload.Contains(SemanticPayloadSeparator))
        {
            return payload.Split(SemanticPayloadSeparator, 2)[0].Trim();
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.TryGetProperty("kind", out var value)
                ? value.GetString()?.Trim() ?? string.Empty
                : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
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
            // 这个动作**自己**那几只精灵（一只精灵对应一张自己的素材，复用位置共用同一只）。
            // 借来的图连精灵一起借（来源动作那只），所以这里只数 SourceAtlasIsOwn 的帧：
            // 整条都借用别人的动作名单是空的 —— 它没有自己的图集、也没有自己的精灵，
            // 只剩 Flipbook 和序列，工程里遗留的重复图集/精灵因此会被正常列成待删。
            var ownSpriteNames = frames
                .Where(frame => frame.SourceAtlasIsOwn && !string.IsNullOrWhiteSpace(frame.SpriteAssetName))
                .Select(frame => frame.SpriteAssetName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            result[group.Key] = SequenceFrameIdentity.BuildCanonicalAssetPackagePaths(
                characterCode, actionCode, ownSpriteNames);
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
        IReadOnlyDictionary<string, string> canonicalTexturePaths,
        IReadOnlySet<string> atlasLayoutActions)
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

        // 图集时代：整条动作的每一帧都指向同一张图集，贴图路径**分不出帧**。
        // 能分清的是 Flipbook 关键帧挂的精灵 —— 工具箱侧按「素材目录里第几张图」
        // 算得出应有的精灵名，Unreal 侧从关键帧上读得到实际精灵名，两边对上才算同步。
        //
        // 少了这一条，已同步的动作每次检测都会被判成「改名」，然后摊成
        // 「删除 23 项 + 新增 23 项」——素材明明是对的，列表却永远清不干净。
        if (!string.IsNullOrWhiteSpace(toolboxItem.SpriteAssetName) &&
            !string.IsNullOrWhiteSpace(unrealItem.SpriteAssetName) &&
            atlasLayoutActions.Contains(unrealItem.ParentStableId))
        {
            return string.Equals(
                toolboxItem.SpriteAssetName,
                unrealItem.SpriteAssetName,
                StringComparison.OrdinalIgnoreCase);
        }

        return canonicalTexturePaths.TryGetValue(toolboxItem.StableId, out var canonicalPath) &&
            SameObjectPath(unrealItem.SourceObjectPath, canonicalPath);
    }

    /// <summary>该帧在 Unreal 里还是历史命名，本次同步会把它改名到规范位置。</summary>
    private static bool NeedsCanonicalSequenceRename(
        UnrealBridgeSnapshotItem toolboxItem,
        UnrealBridgeSnapshotItem unrealItem,
        IReadOnlyDictionary<string, string> canonicalTexturePaths,
        IReadOnlySet<string> atlasLayoutActions)
    {
        if (toolboxItem.Module != UnrealBridgeModule.SequenceFrames ||
            unrealItem.Module != UnrealBridgeModule.SequenceFrames ||
            !SequenceFrameIdentity.IsFrameStableId(toolboxItem.StableId))
        {
            return false;
        }

        return !IsCanonicalSequenceFramePair(toolboxItem, unrealItem, canonicalTexturePaths, atlasLayoutActions);
    }

    /// <summary>
    /// 两侧**布局文字**已经一致的动作（只看 layout，不看帧率/帧位数那几项）。
    ///
    /// 布局文字是这套产物形态的指纹：自己的图集写 <c>atlas:&lt;图集名&gt;</c>，
    /// 借了别人素材的写 <c>frames:&lt;用到的图集名&gt;</c>，而逐帧时代遗留的
    /// 是 <c>frames:&lt;每帧贴图名&gt;</c>——两者不会撞上。
    /// </summary>
    private static HashSet<string> CollectLayoutAlignedActions(
        IReadOnlyDictionary<string, UnrealBridgeSnapshotItem> toolboxItems,
        IReadOnlyDictionary<string, UnrealBridgeSnapshotItem> unrealItems)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in toolboxItems)
        {
            if (pair.Value.Module != UnrealBridgeModule.SequenceFrames ||
                !SequenceFrameIdentity.IsActionStableId(pair.Key) ||
                !unrealItems.TryGetValue(pair.Key, out var unrealItem) ||
                !SequenceFrameIdentity.TryReadActionPayload(pair.Value.PayloadJson, out var toolboxLayout, out _, out _) ||
                !SequenceFrameIdentity.TryReadActionPayload(unrealItem.PayloadJson, out var unrealLayout, out _, out _) ||
                string.IsNullOrWhiteSpace(toolboxLayout))
            {
                continue;
            }

            if (string.Equals(toolboxLayout, unrealLayout, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(pair.Key);
            }

        }

        return result;
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

        // 特效素材是唯一一个「本地在 AssetMaterial 下、Unreal 落在角色根下」的基础素材分类。
        // 漏掉这一支它会掉进下面那条通用 AssetMaterial 规则，算出一个不存在的目标路径，
        // 于是每次检测都判成新增、同步完还是新增。
        if (toolboxItem.Module == UnrealBridgeModule.BaseMaterials &&
            relative.StartsWith("AssetMaterial/Effect/", StringComparison.OrdinalIgnoreCase))
        {
            return $"/Game/GameActor2D/{characterCode}/ExAsset/Effect/{fileName}.{fileName}";
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
            relative.StartsWith("AssetMaterial/Effect/", StringComparison.OrdinalIgnoreCase))
        {
            return $"/Game/GameActor2D/{characterCode}/ExAsset/Effect";
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
        IReadOnlyDictionary<string, string> canonicalSequenceTexturePaths,
        IReadOnlySet<string> atlasLayoutActions)
    {
        if (toolboxItem.Module == UnrealBridgeModule.SequenceFrames)
        {
            return IsCanonicalSequenceFramePair(toolboxItem, unrealItem, canonicalSequenceTexturePaths, atlasLayoutActions);
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
        // 序列动作以工具箱为准，直接比两侧当前载荷，不走三方比较。
        //
        // 三方比较的假设是「两边都可能被人独立编辑」，所以两边都动过才算冲突 ——
        // 这对素材成立，对序列不成立：动画由工具箱定义，Unreal 侧的布局和帧结构
        // 是从工具箱推出去的，没有独立的编辑来源。
        // 更要紧的是载荷格式一换代，旧基线两侧都对不上，三方比较会把全部动作判成冲突，
        // 于是「从逐帧换成图集」这种最该被看见的变化，反而显示成一堆冲突。
        else if (referenceItem.Module == UnrealBridgeModule.SequenceFrames &&
            SequenceFrameIdentity.IsActionStableId(stableId))
        {
            kind = string.Equals(sourceItem.ContentHash, targetItem!.ContentHash, StringComparison.OrdinalIgnoreCase)
                ? isRename || sequenceNeedsCanonicalRename
                    ? UnrealBridgeChangeKind.Renamed
                    : UnrealBridgeChangeKind.Unchanged
                : UnrealBridgeChangeKind.Updated;
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
