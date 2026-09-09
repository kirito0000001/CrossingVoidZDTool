using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 把导出清单里的原始数据（角色道具、角色蓝图技能槽、序列动作、BUFF）投影成界面直接绑定的
/// 预览模型。纯投影：数据进、预览出，不找引擎、不起进程、不读设置。
///
/// 这段代码原本长在 <see cref="UnrealProjectSyncService"/> 里，和「定位工程、跑 commandlet、
/// 读清单、回写工具箱」共处一个 3000 行的类。它其实一个外部调用方都没有，却因为身处那个类中间，
/// 想验一条映射规则就得先把整条导出链路支起来——于是这些规则长期只能靠断言源码文本来守。
/// 搬出来之后它可以脱离 Unreal 单独构造输入直接跑。
///
/// 唯一的 IO 是 <see cref="ResolveExportedAssetPath"/> 里那次 File.Exists：图标到底导出成功没有，
/// 只有磁盘知道，清单本身答不上来。
///
/// 区域敏感的格式化（<see cref="FormatDouble"/> 及其邻居）尤其属于这里：它写出的文本会存进角色
/// JSON，再由第六步按不变区域解析回来，两头区域不一致就静默归零。放在这个类里，回归可以切到
/// 逗号小数点的区域直接调它验往返，不必再去数源码里有没有写 InvariantCulture。
/// </summary>
internal static class UnrealCharacterPreviewFactory
{
    private static readonly UnrealSequenceActionDefinition[] StandardSequenceActions =
    [
        new("Click", "点击", "base", 0),
        new("Death", "死亡", "base", 1),
        new("DefAtk", "守备反击", "base", 2),
        new("Defeat", "失败", "base", 3),
        new("Defence", "守备防御", "base", 4),
        new("Dodge", "守备闪避", "base", 5),
        new("FlyDown", "坠落", "base", 6),
        new("Flying", "飞行", "base", 7),
        new("FlyStart", "击飞", "base", 8),
        new("Idle", "站街", "base", 9),
        new("Land", "落地", "base", 10),
        new("Move", "移动", "base", 11),
        new("OnDamage", "受击", "base", 12),
        new("StandUP", "站起", "base", 13),
        new("Victory", "胜利", "base", 14),
        new("Sk1", "一技能", "skill", 100),
        new("Sk2", "二技能", "skill", 101),
        new("KO", "终结技", "skill", 102),
        new("Sub", "护援技", "skill", 103)
    ];

    internal static UnrealProjectSyncCharacterInfoPreview BuildCharacterInfoPreview(
        UnrealProjectExportCharacterItem? item,
        UnrealProjectExportCharacterActor? actor)
    {
        if (item is null)
        {
            return new UnrealProjectSyncCharacterInfoPreview(
                string.Empty,
                string.Empty,
                false,
                string.Empty,
                string.Empty,
                string.Empty,
                [],
                [],
                1,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0);
        }

        var data = item.ItemData;
        var charData = data.CharData;
        var formLimit = ResolveFormLimitFromSkillSlots(actor);
        if (formLimit <= 0)
        {
            formLimit = charData.CharShapeHas.Count > 0
                ? Math.Max(1, charData.CharShapeHas.Count(value => value))
                : charData.CharShapeNow + 1;
        }

        formLimit = Math.Max(1, formLimit);
        var skillCount = Math.Max(charData.SkillDataCount, charData.SkillHave.Count);
        return new UnrealProjectSyncCharacterInfoPreview(
            item.AssetName,
            item.ObjectPath,
            item.HasItemData,
            item.ReadMessage,
            data.Name,
            data.Description,
            data.Keywords,
            charData.SkillDescription,
            formLimit,
            skillCount,
            charData.Speed,
            charData.Health,
            charData.Attack,
            charData.PhyDefense,
            charData.MagDefense,
            charData.Critical,
            charData.CriticalC,
            charData.Synchronize,
            charData.Anti);
    }

    internal static int ResolveFormLimitFromSkillSlots(UnrealProjectExportCharacterActor? actor)
    {
        if (actor is null)
        {
            return 0;
        }

        return new[] { "SkillSlot1", "SkillSlot2", "SkillSlot3" }
            .Select(slotKey => actor.SkillSlots.TryGetValue(slotKey, out var slot) ? GetSkillSlotStageCount(slot) : 0)
            .DefaultIfEmpty(0)
            .Max();
    }

    internal static int GetSkillSlotStageCount(UnrealProjectExportSkillSlot slot)
    {
        return new[]
        {
            slot.Icons.Count,
            slot.Names.Count,
            slot.Descriptions.Count,
            slot.PointCosts.Count,
            slot.SkillRates.Count,
            slot.AutoPriorities.Count,
            slot.SkillStates.Count,
            slot.PreformTypes.Count,
            slot.PreSkillValues.Count,
            slot.SkillNames.Count,
            slot.AttackCapacities.Count
        }.Max();
    }

    internal static UnrealProjectSyncSkillsPreview BuildSkillsPreview(
        UnrealProjectExportCharacterActor? actor,
        UnrealProjectExportCharacterItem? item,
        UnrealProjectExportLinkSkillLibrary? linkLibrary,
        UnrealProjectExportSupportSkillLibrary? supportLibrary,
        IReadOnlyDictionary<string, UnrealProjectExportAsset> assetLookup)
    {
        var coreSlots = new[]
        {
            BuildSkillSlotPreview(actor, "SkillSlot1", "一技能", assetLookup),
            BuildSkillSlotPreview(actor, "SkillSlot2", "二技能", assetLookup),
            BuildSkillSlotPreview(actor, "SkillSlot3", "终结技", assetLookup)
        };

        var characterName = item?.ItemData?.Name?.Trim() ?? string.Empty;
        var supportEntry = ResolveSupportSkillEntry(supportLibrary, item);
        var supportSkillSlot = supportEntry is null
            ? new UnrealProjectSyncSkillSlotPreview("SkillSlot4", "护援技", true, false, ResolveSupportSkillStatus(supportLibrary, item), [])
            : BuildSkillSlotPreview(supportEntry.SkillSlot4Data, "SkillSlot4", "护援技", assetLookup, isDynamic: true);

        var linkEntries = linkLibrary?.Entries.AsEnumerable() ?? [];
        if (!string.IsNullOrWhiteSpace(characterName))
        {
            linkEntries = linkEntries.Where(pair =>
                string.IsNullOrWhiteSpace(pair.Value.MainCharacterName) ||
                string.Equals(pair.Value.MainCharacterName, characterName, StringComparison.CurrentCultureIgnoreCase));
        }

        var linkSkills = linkEntries
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new UnrealProjectSyncLinkSkillPreview(
                pair.Value.MainCharacterName,
                pair.Value.SupportCharacterCode,
                pair.Value.Skill12Index,
                BuildSkillSlotPreview(pair.Value.SkillSlot5Data, "SkillSlot5Data", "连携技", assetLookup, isDynamic: true)))
            .ToArray() ?? [];

        var sourceText = actor is null
            ? "未找到角色蓝图"
            : $"{actor.AssetName}  {actor.ObjectPath}";
        var actorStatus = actor is null
            ? "未找到角色蓝图技能数据"
            : actor.HasActorData
                ? $"已读取角色蓝图技能槽，目标形态 {Math.Max(1, actor.TargetShape)}"
                : $"已找到角色蓝图，但未读出 SkillSlot1-4：{actor.ReadMessage}";
        var linkStatus = linkLibrary is null || string.IsNullOrWhiteSpace(linkLibrary.ObjectPath)
            ? "未找到连携/护援函数库导出数据"
            : linkLibrary.HasData || supportLibrary?.HasData == true
                ? $"已读取函数库：{linkLibrary.ObjectPath}，护援技 {supportSkillSlot.Stages.Count} 个阶段，连携技 {linkSkills.Length} 个"
                : $"函数库未读出护援/连携数据：{CombineStatusMessages(supportLibrary?.ReadMessage, linkLibrary.ReadMessage)}";

        return new UnrealProjectSyncSkillsPreview(
            actor?.HasActorData == true,
            sourceText,
            actorStatus,
            linkStatus,
            coreSlots,
            supportSkillSlot,
            linkSkills);
    }

    internal static UnrealProjectExportSupportSkillEntry? ResolveSupportSkillEntry(
        UnrealProjectExportSupportSkillLibrary? supportLibrary,
        UnrealProjectExportCharacterItem? item)
    {
        if (supportLibrary is null ||
            !supportLibrary.HasData)
        {
            return null;
        }

        var matchKeys = BuildSupportMatchKeys(item);
        if (matchKeys.Count == 0)
        {
            return null;
        }

        return supportLibrary.Entries.Values
            .Where(entry => SupportEntryMatches(entry, matchKeys))
            .OrderBy(entry => entry.SourceIndex)
            .FirstOrDefault();
    }

    internal static bool SupportEntryMatches(UnrealProjectExportSupportSkillEntry entry, ISet<string> matchKeys)
    {
        foreach (var value in EnumerateSupportEntryKeys(entry))
        {
            if (matchKeys.Contains(NormalizeSupportMatchKey(value)))
            {
                return true;
            }
        }

        return false;
    }

    internal static IEnumerable<string> EnumerateSupportEntryKeys(UnrealProjectExportSupportSkillEntry entry)
    {
        yield return entry.SupportCharacterName;
        yield return entry.SupportCharacterCode;
        yield return StripBracketSuffix(entry.SupportCharacterName);
    }

    internal static HashSet<string> BuildSupportMatchKeys(UnrealProjectExportCharacterItem? item)
    {
        var values = new[]
        {
            item?.ItemData?.Name,
            StripBracketSuffix(item?.ItemData?.Name),
            item?.Code,
            item?.AssetName,
            item?.AssetName?.StartsWith("Item_", StringComparison.OrdinalIgnoreCase) == true
                ? item.AssetName[5..]
                : string.Empty
        };

        return values
            .Select(NormalizeSupportMatchKey)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    internal static string NormalizeSupportMatchKey(string? value)
    {
        return value?.Trim().Replace("？", "?", StringComparison.OrdinalIgnoreCase) ?? string.Empty;
    }

    internal static string StripBracketSuffix(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var bracketIndex = text.LastIndexOf('[');
        return bracketIndex > 0 && text.EndsWith(']')
            ? text[..bracketIndex].Trim()
            : text;
    }

    internal static string ResolveSupportSkillStatus(UnrealProjectExportSupportSkillLibrary? supportLibrary, UnrealProjectExportCharacterItem? item)
    {
        if (supportLibrary is null || string.IsNullOrWhiteSpace(supportLibrary.ObjectPath))
        {
            return "未找到护援函数库导出数据。";
        }

        if (!supportLibrary.HasData)
        {
            return $"函数库未读出护援技：{supportLibrary.ReadMessage}";
        }

        var characterName = item?.ItemData?.Name?.Trim() ?? string.Empty;
        var characterCode = item?.Code?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(characterName)
            ? "当前角色未读取到中文名，无法匹配护援技。"
            : $"函数库未找到 {characterName}（{characterCode}）的护援技。";
    }

    internal static string CombineStatusMessages(params string?[] messages)
    {
        var values = messages
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Select(message => message!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return values.Length == 0 ? "无详细信息" : string.Join("；", values);
    }

    internal static UnrealProjectSyncSkillSlotPreview BuildSkillSlotPreview(
        UnrealProjectExportCharacterActor? actor,
        string slotKey,
        string displayName,
        IReadOnlyDictionary<string, UnrealProjectExportAsset> assetLookup,
        bool isDynamic = false)
    {
        if (actor is null || !actor.SkillSlots.TryGetValue(slotKey, out var slot))
        {
            return new UnrealProjectSyncSkillSlotPreview(slotKey, displayName, isDynamic, false, "未读取到该技能槽。", []);
        }

        return BuildSkillSlotPreview(slot, slotKey, displayName, assetLookup, isDynamic);
    }

    internal static UnrealProjectSyncSkillSlotPreview BuildSkillSlotPreview(
        UnrealProjectExportSkillSlot slot,
        string slotKey,
        string displayName,
        IReadOnlyDictionary<string, UnrealProjectExportAsset> assetLookup,
        bool isDynamic = false)
    {
        var stageCount = GetSkillSlotStageCount(slot);

        var stages = Enumerable.Range(0, stageCount)
            .Select(index => BuildSkillStagePreview(slot, index, assetLookup))
            .ToArray();
        var hasData = stages.Length > 0;
        return new UnrealProjectSyncSkillSlotPreview(
            slotKey,
            string.IsNullOrWhiteSpace(slot.DisplayName) ? displayName : slot.DisplayName,
            isDynamic,
            hasData,
            hasData ? "已读取" : "该技能槽没有形态数据。",
            stages);
    }

    internal static UnrealProjectSyncSkillStagePreview BuildSkillStagePreview(
        UnrealProjectExportSkillSlot slot,
        int index,
        IReadOnlyDictionary<string, UnrealProjectExportAsset> assetLookup)
    {
        var iconObjectPath = GetAt(slot.Icons, index);
        var iconExportedFilePath = ResolveExportedAssetPath(assetLookup, iconObjectPath ?? string.Empty);
        return new UnrealProjectSyncSkillStagePreview(
            index + 1,
            GetTextAt(slot.Names, index),
            GetTextAt(slot.SkillNames, index),
            GetTextAt(slot.Descriptions, index),
            // 这三个整数和 FormatDouble 走的是同一条往返路径（回填文本框 -> 第六步按
            // 不变区域解析回来），所以格式化侧同样要钉死区域，不能跟着系统区域走。
            GetAt(slot.PointCosts, index).ToString(CultureInfo.InvariantCulture),
            GetAt(slot.AttackCapacities, index).ToString(CultureInfo.InvariantCulture),
            GetAt(slot.AutoPriorities, index).ToString(CultureInfo.InvariantCulture),
            MapSkillState(GetAt(slot.SkillStates, index)),
            MapGuardState(GetAt(slot.PreformTypes, index)),
            FormatDouble(GetAt(slot.PreSkillValues, index)),
            iconObjectPath ?? string.Empty,
            ExtractAssetName(iconObjectPath ?? string.Empty),
            iconExportedFilePath,
            BuildMultiplierPreview(GetAt(slot.SkillRates, index)));
    }

    internal static IReadOnlyList<UnrealProjectSyncSkillMultiplierPreview> BuildMultiplierPreview(UnrealProjectExportSkillRate? rate)
    {
        if (rate is null)
        {
            return [];
        }

        return Enumerable.Range(1, 5)
            .Select(level => new UnrealProjectSyncSkillMultiplierPreview(
                level,
                FormatDouble(GetRate(rate.Physical, level)),
                FormatDouble(GetRate(rate.Energy, level))))
            .ToArray();
    }

    internal static UnrealProjectSyncSequenceFramesPreview BuildSequenceFramesPreview(UnrealProjectExportCharacterSequence? sequence)
    {
        if (sequence is null)
        {
            return new UnrealProjectSyncSequenceFramesPreview(
                false,
                false,
                string.Empty,
                "未找到 AnimMaps/AnimSequences 导出数据；请先重新获取项目角色。",
                [],
                [],
                [],
                [],
                []);
        }

        var actions = BuildSequenceActionPreviews(sequence.Actions);
        var baseActions = actions
            .Where(action => IsSequenceCategory(action, "base"))
            .ToArray();
        var skillActions = actions
            .Where(action => IsSequenceCategory(action, "skill"))
            .ToArray();
        var linkActions = actions
            .Where(action => IsSequenceCategory(action, "link"))
            .ToArray();
        var otherActions = actions
            .Where(action => IsSequenceCategory(action, "other"))
            .ToArray();
        var status = sequence.HasData
            ? sequence.HasAnimMaps
                ? $"已找到 AnimMaps：{sequence.AnimMapsObjectPath}"
                : $"序列素材已读取，但 AnimMaps 未找到：{sequence.ReadMessage}"
            : string.IsNullOrWhiteSpace(sequence.ReadMessage)
                ? "未读取到 AnimMaps、AnimSequences 或 Material 帧素材。"
                : sequence.ReadMessage;
        return new UnrealProjectSyncSequenceFramesPreview(
            sequence.HasData,
            sequence.HasAnimMaps,
            sequence.AnimMapsObjectPath,
            status,
            baseActions,
            skillActions,
            linkActions,
            otherActions,
            sequence.OrphanSequences.Select(BuildExportAssetView).ToArray());
    }

    internal static UnrealProjectSyncBuffsPreview BuildBuffsPreview(UnrealProjectExportCharacterBuffSet? buffSet)
    {
        if (buffSet is null)
        {
            return new UnrealProjectSyncBuffsPreview(
                false,
                "未找到 BUFF 导出数据；请先重新获取项目角色。",
                []);
        }

        var buffs = buffSet.Buffs
            .OrderBy(buff => ExtractTrailingNumber(buff.AssetName))
            .ThenBy(buff => buff.AssetName, StringComparer.OrdinalIgnoreCase)
            .Select(buff => new UnrealProjectSyncBuffPreview(
                buff.AssetName,
                buff.ObjectPath,
                buff.HasReadableData,
                string.IsNullOrWhiteSpace(buff.ReadMessage)
                    ? buff.HasReadableData ? "已读取 DreamTask BUFF" : "未读取到 DreamTask 数据"
                    : buff.ReadMessage,
                buff.TaskName,
                buff.DisplayName,
                buff.Description,
                MapBuffDamageType(buff.DamageType),
                MapBuffGainType(buff.GainType),
                MapBuffTaskPriority(buff.TaskPriority),
                buff.Count,
                buff.CompleteCount,
                buff.Power,
                buff.CompletePower,
                buff.TriggerTiming,
                buff.ConditionSummary,
                buff.IconObjectPath,
                string.IsNullOrWhiteSpace(buff.IconAssetName) ? ExtractAssetName(buff.IconObjectPath) : buff.IconAssetName,
                buff.IconExportedFilePath))
            .ToArray();
        var status = buffSet.HasData
            ? $"已读取 BUFF 文件夹：{buffSet.BuffFolderObjectPath}"
            : string.IsNullOrWhiteSpace(buffSet.ReadMessage)
                ? "BUFF 文件夹存在，但未读取到 DreamTask BUFF。"
                : buffSet.ReadMessage;
        return new UnrealProjectSyncBuffsPreview(buffSet.HasData, status, buffs);
    }

    internal static IReadOnlyList<UnrealProjectSyncSequenceActionPreview> BuildSequenceActionPreviews(
        IReadOnlyList<UnrealProjectExportSequenceAction> exportActions)
    {
        var actions = exportActions
            .SelectMany(SplitSequenceActionByForm)
            .ToList();
        var formLimit = Math.Max(
            1,
            actions
                .Where(action => !action.Category.Equals("link", StringComparison.OrdinalIgnoreCase) &&
                    !action.Category.Equals("other", StringComparison.OrdinalIgnoreCase))
                .Select(action => action.FormIndex)
                .DefaultIfEmpty(1)
                .Max());
        foreach (var definition in StandardSequenceActions)
        {
            for (var formIndex = 1; formIndex <= formLimit; formIndex++)
            {
                if (actions.Any(action =>
                        string.Equals(action.ActionCode, definition.ActionCode, StringComparison.OrdinalIgnoreCase) &&
                        action.FormIndex == formIndex))
                {
                    continue;
                }

                actions.Add(CreateMissingSequenceActionPreview(definition, formIndex));
            }
        }

        return actions
            .OrderBy(GetSequenceActionSortOrder)
            .ThenBy(action => action.FormIndex)
            .ThenBy(action => action.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static IEnumerable<UnrealProjectSyncSequenceActionPreview> SplitSequenceActionByForm(UnrealProjectExportSequenceAction action)
    {
        var formIndexes = action.FormIndexes.Count == 0
            ? new[] { 1 }
            : action.FormIndexes.Distinct().Order().ToArray();
        if (formIndexes.Length <= 1)
        {
            yield return BuildSequenceActionPreview(action);
            yield break;
        }

        foreach (var formIndex in formIndexes)
        {
            yield return BuildSequenceActionPreview(CloneSequenceActionForForm(action, formIndex));
        }
    }

    internal static UnrealProjectExportSequenceAction CloneSequenceActionForForm(UnrealProjectExportSequenceAction action, int formIndex)
    {
        var animSequences = FilterSequenceAssetsByForm(action.AnimSequences, action.ActionCode, formIndex).ToList();
        var sequencePaths = animSequences
            .Select(asset => UnrealProjectSyncService.NormalizeObjectPath(asset.ObjectPath))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new UnrealProjectExportSequenceAction
        {
            ActionCode = action.ActionCode,
            DisplayName = action.DisplayName,
            SourceProperty = action.SourceProperty,
            Category = action.Category,
            Target = action.Target,
            HasData = action.HasData,
            FormIndexes = [formIndex],
            ReferencedSequences = action.ReferencedSequences,
            AnimSequences = animSequences,
            TextureCount = action.TextureCount,
            SpriteCount = action.SpriteCount,
            FlipbookCount = action.FlipbookCount,
            FramesPerSecond = action.FramesPerSecond,
            OrderedFrames = FilterSequenceAssetsByForm(action.OrderedFrames, action.ActionCode, formIndex, fallbackToAllWhenSingleForm: true).ToList(),
            PreviewFrames = FilterSequenceAssetsByForm(action.PreviewFrames, action.ActionCode, formIndex, fallbackToAllWhenSingleForm: true).ToList(),
            SoundNotifies = sequencePaths.Count == 0
                ? action.SoundNotifies
                : action.SoundNotifies
                    .Where(notify => sequencePaths.Contains(UnrealProjectSyncService.NormalizeObjectPath(notify.SequenceObjectPath)))
                    .ToList()
        };
    }

    internal static IEnumerable<UnrealProjectExportSequenceAsset> FilterSequenceAssetsByForm(
        IReadOnlyList<UnrealProjectExportSequenceAsset> assets,
        string actionCode,
        int formIndex,
        bool fallbackToAllWhenSingleForm = false)
    {
        var matched = assets
            .Where(asset => GetSequenceAssetFormIndex(asset.AssetName, actionCode) == formIndex)
            .ToList();
        if (matched.Count == 0 && fallbackToAllWhenSingleForm && formIndex == 1)
        {
            return assets;
        }

        return matched;
    }

    internal static int GetSequenceAssetFormIndex(string assetName, string actionCode)
    {
        var name = assetName ?? string.Empty;
        if (Regex.IsMatch(name, @"(?:^|[_-])Shape0*([2-9]\d*)", RegexOptions.IgnoreCase) is true)
        {
            var match = Regex.Match(name, @"(?:^|[_-])Shape0*([2-9]\d*)", RegexOptions.IgnoreCase);
            // 形态号是从资产名里抠出来的机器标识，解析区域必须固定，不能跟着系统设置飘。
            return int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var shapeIndex)
                ? shapeIndex
                : 1;
        }

        var normalizedCode = Regex.Replace(actionCode ?? string.Empty, "[^a-z0-9]", string.Empty).ToLowerInvariant();
        var normalizedName = Regex.Replace(name, "[^a-z0-9]", string.Empty).ToLowerInvariant();
        if (!string.IsNullOrEmpty(normalizedCode) &&
            normalizedName.Contains(normalizedCode + "2", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 1;
    }

    internal static UnrealProjectSyncSequenceActionPreview CreateMissingSequenceActionPreview(UnrealSequenceActionDefinition definition, int formIndex)
    {
        return new UnrealProjectSyncSequenceActionPreview(
            definition.ActionCode,
            definition.DisplayName,
            string.Empty,
            definition.Category,
            string.Empty,
            false,
            [formIndex],
            0,
            0,
            0,
            0,
            0,
            0,
            [],
            []);
    }

    internal static int GetSequenceActionSortOrder(UnrealProjectSyncSequenceActionPreview action)
    {
        var definition = StandardSequenceActions.FirstOrDefault(item =>
            string.Equals(item.ActionCode, action.ActionCode, StringComparison.OrdinalIgnoreCase));
        if (definition is not null)
        {
            return definition.SortOrder;
        }

        return action.Category.Equals("link", StringComparison.OrdinalIgnoreCase)
            ? 200
            : 300;
    }

    internal static UnrealProjectSyncSequenceActionPreview BuildSequenceActionPreview(UnrealProjectExportSequenceAction action)
    {
        var orderedFrames = action.OrderedFrames
            .Select(BuildExportAssetView)
            .ToArray();
        var previewFrames = action.PreviewFrames
            .Select(BuildExportAssetView)
            .ToArray();
        return new UnrealProjectSyncSequenceActionPreview(
            action.ActionCode,
            string.IsNullOrWhiteSpace(action.DisplayName) ? action.ActionCode : action.DisplayName,
            action.SourceProperty,
            action.Category,
            action.Target,
            action.HasData,
            action.FormIndexes,
            action.ReferencedSequences.Count,
            action.AnimSequences.Count,
            action.TextureCount,
            action.SpriteCount,
            action.FlipbookCount,
            action.FramesPerSecond,
            orderedFrames,
            previewFrames,
            action.SoundNotifies.Select(notify => new UnrealProjectSyncSequenceSoundNotifyPreview(
                notify.FrameIndex,
                notify.TimeSeconds,
                notify.TrackIndex,
                notify.SoundObjectPath,
                notify.SoundAssetName,
                notify.SoundAssetClass,
                notify.ExportedFilePath,
                notify.IsCharacterVoice,
                notify.SequenceObjectPath)).ToArray(),
            action.OwnedAssets.Select(BuildExportAssetView).ToArray());
    }

    internal static UnrealProjectSyncExportAssetView BuildExportAssetView(UnrealProjectExportSequenceAsset asset)
    {
        return new UnrealProjectSyncExportAssetView(
            asset.AssetName,
            asset.AssetClass,
            asset.PackagePath,
            asset.ObjectPath,
            string.Empty,
            asset.ExportedFilePath);
    }

    internal sealed record UnrealSequenceActionDefinition(
        string ActionCode,
        string DisplayName,
        string Category,
        int SortOrder);

    internal static bool IsSequenceCategory(UnrealProjectSyncSequenceActionPreview action, string category)
    {
        return string.Equals(action.Category, category, StringComparison.OrdinalIgnoreCase);
    }

    internal static T? GetAt<T>(IReadOnlyList<T> values, int index)
    {
        return index >= 0 && index < values.Count ? values[index] : default;
    }

    internal static string GetTextAt(IReadOnlyList<string> values, int index)
    {
        return index >= 0 && index < values.Count ? values[index] : string.Empty;
    }

    internal static double GetRate(Dictionary<string, double> values, int level)
    {
        // 键是写入侧按不变区域生成的（见 UnrealBlueprintSetupService.BuildSkillRate），
        // 取键也必须同区域：两头区域不一致时数字文本对不上，整档倍率查不到，
        // 又会悄悄退回 0。
        return values.TryGetValue(level.ToString(CultureInfo.InvariantCulture), out var value) ? value : 0;
    }

    /// <summary>
    /// 这些文本会回填进技能编辑器，第六步再按不变区域解析回 double，所以格式化侧
    /// 必须是同一个区域。以前跟着当前区域走：de/fr/ru 写出来的是「1,5」，第六步解析不回来，
    /// 而它当时是静默归 0 的——技能倍率、守备数值被无声清零，界面却一路显示成功。
    /// </summary>
    /// <summary>
    /// 数值回填文本框时的格式化。必须用不变区域：这个字符串会存进角色 JSON，
    /// 再由第六步按不变区域解析回来。以前这里用当前区域，于是在小数点是逗号的
    /// 区域（de/fr/ru）写出 "1,5"，解析读不了，旧代码把它静默当成 0。
    ///
    /// 开成 internal 只为了能测这条往返——回归里那条「逗号小数点区域仍能往返」
    /// 直接切区域跑一遍，比逐个断言源码里有没有写 InvariantCulture 靠谱得多。
    /// </summary>
    internal static string FormatDouble(double value)
    {
        return Math.Abs(value) < 0.000001 ? string.Empty : value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    internal static string FormatDouble(double? value)
    {
        return value.HasValue ? FormatDouble(value.Value) : string.Empty;
    }

    internal static string MapSkillState(string? value)
    {
        return NormalizeUnrealEnumValue(value) switch
        {
            "normal" => "常态",
            "disable" => "禁用",
            "abandon" => "舍弃",
            "air" => "空",
            "" or null => "空",
            var other => value?.Trim() ?? other
        };
    }

    internal static string MapGuardState(string? value)
    {
        return NormalizeUnrealEnumValue(value) switch
        {
            "defense" => "防御",
            "attack" => "反击",
            "dodge" => "闪避",
            "air" => "空",
            "" or null => "空",
            var other => value?.Trim() ?? other
        };
    }

    internal static string MapBuffDamageType(string? value)
    {
        return NormalizeUnrealEnumValue(value) switch
        {
            "sendattr" or "senderattr" or "ownerattr" or "attribute" or "attr" or "property" or "battackerattr" => "发送方-属性",
            "sendfinal" or "senderfinal" or "ownerfinal" or "final" or "battackerfinal" => "发送方-最终",
            "receiveattr" or "receiverattr" or "targetattr" or "injuredattr" => "接收方-属性",
            "receivefinal" or "receiverfinal" or "targetfinal" or "injuredfinal" => "接收方-最终",
            "发送方-属性" => "发送方-属性",
            "发送方-最终" => "发送方-最终",
            "接收方-属性" => "接收方-属性",
            "接收方-最终" => "接收方-最终",
            _ => string.IsNullOrWhiteSpace(value) ? "发送方-属性" : value!.Trim()
        };
    }

    internal static string MapBuffGainType(string? value)
    {
        var normalized = NormalizeUnrealEnumValue(value);
        if (normalized.Contains("debuff", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("weak", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("削弱", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("decrease", StringComparison.OrdinalIgnoreCase))
        {
            return "削弱";
        }

        return "增益";
    }

    internal static string MapBuffTaskPriority(string? value)
    {
        return NormalizeUnrealEnumValue(value) switch
        {
            "low" or "低" => "低",
            "high" or "高" => "高",
            "urgent" or "紧急" => "紧急",
            _ => "正常"
        };
    }

    internal static int ExtractTrailingNumber(string value)
    {
        // 同上：资产名尾巴上的序号是机器标识，解析区域必须固定。
        var match = Regex.Match(value ?? string.Empty, @"(\d+)(?!.*\d)");
        return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : int.MaxValue;
    }

    internal static string NormalizeUnrealEnumValue(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var colonIndex = text.IndexOf(':');
        if (colonIndex > 0)
        {
            text = text[..colonIndex];
        }

        var dotIndex = text.LastIndexOf('.');
        if (dotIndex >= 0 && dotIndex < text.Length - 1)
        {
            text = text[(dotIndex + 1)..];
        }

        text = text.Trim().Trim('<', '>', ' ');
        return text.ToLowerInvariant();
    }

    internal static string ExtractAssetName(string objectPath)
    {
        if (string.IsNullOrWhiteSpace(objectPath))
        {
            return string.Empty;
        }

        var dotIndex = objectPath.LastIndexOf('.');
        if (dotIndex >= 0 && dotIndex < objectPath.Length - 1)
        {
            return objectPath[(dotIndex + 1)..];
        }

        var slashIndex = objectPath.LastIndexOf('/');
        return slashIndex >= 0 && slashIndex < objectPath.Length - 1
            ? objectPath[(slashIndex + 1)..]
            : objectPath;
    }

    internal static IReadOnlyDictionary<string, UnrealProjectExportAsset> BuildExportAssetLookup(
        IReadOnlyList<UnrealProjectExportAsset> assets)
    {
        var lookup = new Dictionary<string, UnrealProjectExportAsset>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in assets)
        {
            AddExportAssetLookupValue(lookup, asset.ObjectPath, asset);
            AddExportAssetLookupValue(lookup, asset.AssetName, asset);
            AddExportAssetLookupValue(lookup, ExtractAssetName(asset.ObjectPath), asset);
        }

        return lookup;
    }

    internal static void AddExportAssetLookupValue(
        IDictionary<string, UnrealProjectExportAsset> lookup,
        string key,
        UnrealProjectExportAsset asset)
    {
        var normalized = UnrealProjectSyncService.NormalizeObjectPath(key);
        if (string.IsNullOrWhiteSpace(normalized) ||
            lookup.ContainsKey(normalized))
        {
            return;
        }

        lookup[normalized] = asset;
    }

    internal static string ResolveExportedAssetPath(
        IReadOnlyDictionary<string, UnrealProjectExportAsset> assetLookup,
        string objectPath)
    {
        if (string.IsNullOrWhiteSpace(objectPath))
        {
            return string.Empty;
        }

        var lookupKeys = new[]
        {
            objectPath,
            ExtractAssetName(objectPath)
        };

        foreach (var key in lookupKeys)
        {
            if (assetLookup.TryGetValue(UnrealProjectSyncService.NormalizeObjectPath(key), out var asset) &&
                File.Exists(asset.ExportedFilePath))
            {
                return asset.ExportedFilePath;
            }
        }

        return string.Empty;
    }
}
