using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 把 Unreal 侧读出来的角色数据写回工具箱的角色 JSON：角色信息、技能与连携技、序列动作帧、
/// BUFF、基础素材。方向是单向的——这里只写工具箱，不碰 Unreal 工程，也不起引擎进程。
///
/// 从 <see cref="UnrealProjectSyncService"/> 里搬出来的。那个类同时负责「找引擎、跑
/// commandlet、读清单、投影预览、写回工具箱」，而写回这一段其实只有
/// <see cref="UnrealBridgeDraftImportService"/> 一个调用方，却因为混在里面，
/// 想验一条写回规则就得先把整条导出链路支起来。
///
/// 搬出来时顺手收敛了依赖：原先 CharacterSkillsService / BuffService 这些是在方法体里
/// 一次次 new 出来的（同一个类型在原文件里 new 了十次），现在是字段。这些服务本身无状态，
/// 共享实例不改变行为，但省掉了「同一次同步里 Load 和 Save 用的不是同一个对象」这种看着
/// 就让人心里没底的写法。
///
/// 写回是有破坏性的：它直接覆盖角色目录下的 JSON 和图标。调用方
/// （<see cref="UnrealBridgeDraftImportService"/>）会在整批导入前先做一次自动备份，
/// 所以这里的每个方法都假定「失败就整体回滚」，不再各自做局部补偿。
/// </summary>
internal sealed class UnrealToolboxWriteBackService
{
    private readonly BaseMaterialService _baseMaterialService = new();
    private readonly CharacterInfoService _characterInfoService = new();
    private readonly CharacterSkillsService _skillsService = new();
    private readonly SequenceFrameService _sequenceFrameService = new();
    private readonly CharacterFormService _formService = new();
    private readonly BuffService _buffService = new();

    public int SyncMaterialBucketToToolbox(CharacterCard character, UnrealProjectSyncMaterialBucket bucket)
    {
        if (!Enum.TryParse<BaseMaterialKind>(bucket.Kind, ignoreCase: true, out var kind))
        {
            kind = BaseMaterialKind.OtherImage;
        }

        var sourcePaths = bucket.Assets
            .Where(asset => asset.HasPreview)
            .Select(asset => asset.ExportedFilePath)
            .ToArray();
        return _baseMaterialService.ReplaceWithImages(character, kind, sourcePaths);
    }

    public void SyncCharacterInfoToToolbox(CharacterCard character, UnrealProjectSyncCharacterCandidate candidate)
    {
        if (!candidate.CharacterInfo.HasItemData)
        {
            throw new InvalidOperationException("当前角色物品蓝图还没有成功读取 ItemData.CharData，不能同步到 St3。");
        }

        var info = candidate.CharacterInfo;
        var existingData = _characterInfoService.Load(character);
        var data = new CharacterInfoData
        {
            Code = character.Code,
            Name = string.IsNullOrWhiteSpace(info.Name) ? candidate.Code : info.Name.Trim(),
            Description = info.Description,
            KeywordTagGroups = existingData.KeywordTagGroups,
            FormLimit = Math.Max(1, info.FormLimit),
            Speed = Math.Max(0, info.Speed),
            Health = Math.Max(0, info.Health),
            Attack = Math.Max(0, info.Attack),
            PhysicalDefense = Math.Max(0, info.PhysicalDefense),
            EnergyDefense = Math.Max(0, info.EnergyDefense),
            CriticalRate = Math.Max(0, info.CriticalRate),
            CriticalDamage = Math.Max(0, info.CriticalDamage),
            Synchronize = Math.Max(0, info.Synchronize),
            Anti = info.Anti,
            UpdatedAt = DateTime.Now
        };

        foreach (var tag in info.KeywordTags
                     .Select(tag => tag.Trim())
                     .Where(tag => !string.IsNullOrWhiteSpace(tag))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            data.KeywordTags.Add(tag);
        }

        foreach (var passive in info.PassiveSkills
                     .Select(passive => passive.Trim())
                     .Where(passive => !string.IsNullOrWhiteSpace(passive)))
        {
            data.PassiveSkills.Add(passive);
        }

        _characterInfoService.Save(character, data);
    }

    /// <param name="identitySuffix">
    /// 稳定身份里的槽位后缀（core:0 / support / link:…），必须和
    /// UnrealBridgeSemanticSnapshotService 用的一致，否则写回来的身份对不上快照。
    /// </param>
    public int SyncSkillStageToToolbox(
        CharacterCard character,
        UnrealProjectSyncCharacterCandidate candidate,
        UnrealProjectSyncSkillSlotPreview slot,
        int stageIndex,
        string identitySuffix = "")
    {
        if (stageIndex < 0 || stageIndex >= slot.Stages.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(stageIndex));
        }

        using var entryNotifications = CharacterSkillEntry.SuppressEditNotifications();
        using var multiplierNotifications = SkillMultiplierLevel.SuppressEditNotifications();
        var data = _skillsService.Load(character);
        var target = ResolveTargetSkillCollection(data, slot.SlotKey);
        var entry = ToCharacterSkillEntry(slot.Stages[stageIndex], candidate, linkSkill: null);
        if (target is null || entry is null)
        {
            return 0;
        }

        while (target.Count < stageIndex)
        {
            target.Add(CharacterSkillsService.CreateEntry());
        }

        entry.SyncId = UnrealBridgeSemanticSnapshotService.CreateOriginIdentity(
            $"{candidate.Code}|skill|{identitySuffix}|{slot.SlotKey}|{stageIndex}");
        if (target.Count == stageIndex)
        {
            target.Add(entry);
        }
        else
        {
            target[stageIndex] = entry;
        }

        _skillsService.Save(character, data);
        return 1;
    }

    /// <param name="identitySuffix">同 <see cref="SyncSkillStageToToolbox"/>。</param>
    public int SyncLinkSkillToToolbox(
        CharacterCard character,
        UnrealProjectSyncCharacterCandidate candidate,
        UnrealProjectSyncLinkSkillPreview linkSkill,
        string identitySuffix = "")
    {
        using var entryNotifications = CharacterSkillEntry.SuppressEditNotifications();
        using var multiplierNotifications = SkillMultiplierLevel.SuppressEditNotifications();

        var data = _skillsService.Load(character);
        var entry = ToCharacterSkillEntry(linkSkill.SkillSlot.Stages.FirstOrDefault(), candidate, linkSkill);
        if (entry is null)
        {
            return 0;
        }

        var existingIndex = data.ComboSkills
            .Select((value, index) => new { value, index })
            .FirstOrDefault(item =>
                string.Equals(item.value.ComboCharacterCode, linkSkill.SupportCharacterCode, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.value.TrueName, entry.TrueName, StringComparison.CurrentCultureIgnoreCase))
            ?.index;
        if (existingIndex is int index)
        {
            data.ComboSkills[index] = entry;
        }
        else
        {
            data.ComboSkills.Add(entry);
        }

        _skillsService.Save(character, data);
        return 1;
    }

    public int SyncSequenceActionToToolbox(
        CharacterCard character,
        UnrealProjectSyncSequenceActionPreview action)
    {
        var skills = _skillsService.Load(character);
        var actions = SequenceFrameService.BuildActions(skills, _formService.GetFormLimit(character));
        return SyncSequenceActionToToolbox(character, action, _sequenceFrameService, actions);
    }

    public int SyncBuffToToolbox(
        CharacterCard character,
        UnrealProjectSyncBuffPreview buff)
    {
        if (!buff.HasReadableData)
        {
            throw new InvalidOperationException($"BUFF 未读取成功，不能同步：{buff.AssetName}。{buff.ReadStatusText}");
        }

        var data = _buffService.Load(character);
        var entry = ToBuffEntry(buff, character, _buffService);
        var existingIndex = data.Buffs
            .Select((value, index) => new { value, index })
            .FirstOrDefault(item =>
                string.Equals(item.value.SourceAssetPath, buff.ObjectPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.value.UserCode, buff.AssetName, StringComparison.OrdinalIgnoreCase))
            ?.index;
        if (existingIndex is int index)
        {
            data.Buffs[index] = entry;
        }
        else
        {
            data.Buffs.Add(entry);
        }

        _buffService.Save(character, data);
        return 1;
    }

    private static ObservableCollection<CharacterSkillEntry>? ResolveTargetSkillCollection(
        CharacterSkillsData data,
        string slotKey)
    {
        return slotKey switch
        {
            "SkillSlot1" => data.FirstSkill,
            "SkillSlot2" => data.SecondSkill,
            "SkillSlot3" => data.UltimateSkill,
            "SkillSlot4" => data.SupportSkill,
            "SkillSlot5Data" => data.ComboSkills,
            _ => null
        };
    }

    private static CharacterSkillEntry? ToCharacterSkillEntry(
        UnrealProjectSyncSkillStagePreview? stage,
        UnrealProjectSyncCharacterCandidate candidate,
        UnrealProjectSyncLinkSkillPreview? linkSkill)
    {
        if (stage is null)
        {
            return null;
        }

        var entry = CharacterSkillsService.CreateEntry();
        entry.PositionName = string.IsNullOrWhiteSpace(stage.PositionName)
            ? stage.TrueName
            : stage.PositionName;
        entry.TrueName = stage.TrueName;
        entry.Description = stage.Description;
        entry.PtCost = stage.PtCost;
        entry.AttackCapacity = stage.AttackCapacity;
        entry.AutoPriority = stage.AutoPriority;
        entry.SkillState = string.IsNullOrWhiteSpace(stage.SkillState) ? "空" : stage.SkillState;
        entry.GuardState = string.IsNullOrWhiteSpace(stage.GuardState) ? "空" : stage.GuardState;
        entry.GuardValue = stage.GuardValue;
        entry.IconPath = File.Exists(stage.IconExportedFilePath)
            ? stage.IconExportedFilePath
            : ResolveLocalPreviewPath(candidate, stage.IconObjectPath);
        if (File.Exists(entry.IconPath))
        {
            entry.IconUri = new Uri(entry.IconPath).AbsoluteUri;
        }

        for (var index = 0; index < Math.Min(entry.LevelMultipliers.Count, stage.Multipliers.Count); index++)
        {
            entry.LevelMultipliers[index].PhysicalMultiplier = stage.Multipliers[index].PhysicalMultiplier;
            entry.LevelMultipliers[index].EnergyMultiplier = stage.Multipliers[index].EnergyMultiplier;
        }

        if (linkSkill is not null)
        {
            entry.ComboCharacterCode = linkSkill.SupportCharacterCode;
            entry.ComboCharacterName = linkSkill.SupportCharacterCode;
        }

        return entry;
    }

    private static string ResolveLocalPreviewPath(UnrealProjectSyncCharacterCandidate candidate, string iconObjectPath)
    {
        if (string.IsNullOrWhiteSpace(iconObjectPath))
        {
            return string.Empty;
        }

        var normalizedIconPath = UnrealProjectSyncService.NormalizeObjectPath(iconObjectPath);
        var allAssets = candidate.MaterialBuckets
            .SelectMany(bucket => bucket.Assets)
            .ToArray();
        var matched = allAssets.FirstOrDefault(asset =>
            string.Equals(UnrealProjectSyncService.NormalizeObjectPath(asset.ObjectPath), normalizedIconPath, StringComparison.OrdinalIgnoreCase));
        if (matched is not null && File.Exists(matched.ExportedFilePath))
        {
            return matched.ExportedFilePath;
        }

        var iconAssetName = UnrealCharacterPreviewFactory.ExtractAssetName(iconObjectPath);
        matched = allAssets.FirstOrDefault(asset =>
            string.Equals(asset.AssetName, iconAssetName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(UnrealCharacterPreviewFactory.ExtractAssetName(asset.ObjectPath), iconAssetName, StringComparison.OrdinalIgnoreCase));
        return matched is not null && File.Exists(matched.ExportedFilePath)
            ? matched.ExportedFilePath
            : string.Empty;
    }

    private static int SyncSequenceActionToToolbox(
        CharacterCard character,
        UnrealProjectSyncSequenceActionPreview action,
        SequenceFrameService service,
        IReadOnlyList<SequenceFrameAction> toolboxActions)
    {
        var targetAction = ResolveToolboxSequenceAction(action, toolboxActions);
        if (targetAction is null)
        {
            throw new InvalidOperationException($"没有找到可写入的 St5 序列项：{action.Title}。请先同步 St4 技能/连携信息。");
        }

        var sourceFrames = (action.OrderedFrames.Count > 0 ? action.OrderedFrames : action.PreviewFrames)
            .Select(frame => new ExternalSequenceFrameSource(frame.ExportedFilePath, frame.IsBlank))
            .ToArray();
        var imported = service.ImportExternalFrames(
            character,
            targetAction,
            sourceFrames,
            (int)Math.Round(action.FramesPerSecond <= 0 ? SequenceFrameService.DefaultFps : action.FramesPerSecond));
        return imported.Count;
    }

    private static BuffEntry ToBuffEntry(
        UnrealProjectSyncBuffPreview buff,
        CharacterCard character,
        BuffService buffService)
    {
        var entry = BuffService.CreateBuff();
        entry.UserCode = SanitizeBuffUserCode(buff.AssetName);
        entry.Name = buff.Title;
        entry.Description = buff.Description;
        entry.DamageType = buff.DamageType;
        entry.GainType = buff.GainType;
        entry.TaskPriority = buff.TaskPriority;
        entry.Stacks = Math.Max(0, buff.Count);
        entry.CompleteStacks = Math.Max(1, buff.CompleteCount);
        entry.Strength = Math.Max(0, buff.Power);
        entry.CompleteStrength = Math.Max(1, buff.CompletePower);
        entry.TriggerTiming = buff.TriggerTiming;
        entry.ConditionSummary = buff.ConditionSummary;
        entry.SourceAssetPath = buff.ObjectPath;
        entry.ReadStatus = buff.HasReadableData ? "Unreal 同步" : buff.ReadStatusText;
        entry.SetOwnerTextFromToolbox(character.EffectiveDisplayName);
        BuffService.RefreshNaming(character, [entry]);

        if (File.Exists(buff.IconExportedFilePath))
        {
            buffService.ImportIcon(character, entry, buff.IconExportedFilePath);
        }
        else
        {
            buffService.EnsureDefaultIcon(character, entry);
        }

        return entry;
    }

    private static string SanitizeBuffUserCode(string value)
    {
        var text = Regex.Replace(value ?? string.Empty, @"[^A-Za-z0-9_-]+", "_").Trim('_');
        if (text.Length > 40)
        {
            text = text[^40..];
        }

        return text;
    }

    private static SequenceFrameAction? ResolveToolboxSequenceAction(
        UnrealProjectSyncSequenceActionPreview action,
        IReadOnlyList<SequenceFrameAction> toolboxActions)
    {
        if (action.Category.Equals("link", StringComparison.OrdinalIgnoreCase))
        {
            var target = NormalizeSequenceToken(action.Target);
            var title = NormalizeSequenceToken(action.Title);
            var candidates = toolboxActions.Where(item => item.IsCombo).ToArray();
            if (candidates.Length == 0)
            {
                return null;
            }

            var matched = candidates.FirstOrDefault(item =>
                NormalizeSequenceToken(item.DisplayName).Contains(target, StringComparison.OrdinalIgnoreCase) ||
                NormalizeSequenceToken(item.Code).Contains(target, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(target) && title.Contains(target, StringComparison.OrdinalIgnoreCase)));
            return matched ?? candidates.FirstOrDefault();
        }

        var code = NormalizeToolboxSequenceActionCode(action.ActionCode, action.FormIndex);
        return toolboxActions.FirstOrDefault(item =>
            string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeToolboxSequenceActionCode(string unrealActionCode, int formIndex)
    {
        var code = unrealActionCode.Trim();
        code = code switch
        {
            "Defence" => "Defense",
            "OnDamage" => "Ondm",
            "KO" => "Ko",
            "FlyDown" => "Flydown",
            "FlyStart" => "Flystart",
            "StandUP" => "Standup",
            _ => code
        };

        if (formIndex > 1 && !Regex.IsMatch(code, @"\d{2}$", RegexOptions.CultureInvariant))
        {
            // 这里拼的是 Unreal 侧的资产名，绝不能跟着系统区域变。
            code += formIndex.ToString("00", CultureInfo.InvariantCulture);
        }

        return code;
    }

    private static string NormalizeSequenceToken(string value)
    {
        return Regex.Replace(value ?? string.Empty, "[^a-z0-9]", string.Empty, RegexOptions.IgnoreCase).ToLowerInvariant();
    }
}
