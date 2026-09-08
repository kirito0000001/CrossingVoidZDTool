using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CrossingVoidZDTool.Services;

internal sealed class UnrealBridgeToolboxSnapshotService
{
    public UnrealBridgeSnapshot Build(CharacterCard character)
    {
        ArgumentNullException.ThrowIfNull(character);
        if (!character.IsCompleted)
        {
            throw new InvalidOperationException("只有已完成角色可以生成发布到虚幻的工具箱快照。");
        }

        return BuildCore(character);
    }

    public UnrealBridgeSnapshot BuildForSynchronization(CharacterCard character)
    {
        ArgumentNullException.ThrowIfNull(character);
        return BuildCore(character);
    }

    private static UnrealBridgeSnapshot BuildCore(CharacterCard character)
    {
        var items = new List<UnrealBridgeSnapshotItem>();
        var baseMaterialSections = new BaseMaterialService().LoadSections(character);
        var voiceSections = new VoiceMaterialService().LoadSections(character);
        var fileIdentities = BuildFileIdentities(character, baseMaterialSections, voiceSections);
        AddCharacterInfo(items, character);
        AddBaseMaterials(items, character, baseMaterialSections, fileIdentities);
        var skills = new CharacterSkillsService().Load(character);
        AddSkills(items, character, skills);
        AddSequenceFrames(items, character, skills);
        AddBuffs(items, character);
        AddVoices(items, character, voiceSections, fileIdentities);
        return new UnrealBridgeSnapshot(character.Code, items);
    }

    private static void AddCharacterInfo(List<UnrealBridgeSnapshotItem> items, CharacterCard character)
    {
        var info = new CharacterInfoService().Load(character);
        var groups = info.KeywordTagGroups;
        var payload = BuildPayload(
            ("code", info.Code),
            ("name", info.Name),
            ("description", info.Description),
            ("tags", Join(info.KeywordTags)),
            ("works", Join(groups?.Works)),
            ("periods", Join(groups?.Periods)),
            ("abilityTypes", Join(groups?.AbilityTypes)),
            ("affiliations", Join(groups?.Affiliations)),
            ("aliases", Join(groups?.Aliases)),
            ("legacyTags", Join(groups?.LegacyKeywordTags)),
            ("passiveSkills", Join(info.PassiveSkills)),
            ("formLimit", Format(info.FormLimit)),
            ("anti", info.Anti ? "true" : "false"),
            ("speed", Format(info.Speed)),
            ("health", Format(info.Health)),
            ("attack", Format(info.Attack)),
            ("physicalDefense", Format(info.PhysicalDefense)),
            ("energyDefense", Format(info.EnergyDefense)),
            ("criticalRate", Format(info.CriticalRate)),
            ("criticalDamage", Format(info.CriticalDamage)),
            ("synchronize", Format(info.Synchronize)));
        items.Add(CreateItem(
            "character:info",
            string.Empty,
            UnrealBridgeModule.CharacterInfo,
            "角色信息",
            payload,
            string.Empty));
    }

    private static void AddBaseMaterials(
        List<UnrealBridgeSnapshotItem> items,
        CharacterCard character,
        IReadOnlyList<BaseMaterialSection> sections,
        IReadOnlyDictionary<string, string> fileIdentities)
    {
        foreach (var section in sections)
        {
            foreach (var material in section.Items.OrderBy(item => item.Index))
            {
                var kind = section.Spec.Kind.ToString().ToLowerInvariant();
                var stableId = $"material:{fileIdentities[material.FilePath]}";
                var payload = BuildPayload(
                    ("kind", section.Spec.Kind.ToString()),
                    ("index", Format(material.Index)),
                    ("width", Format(material.ActualWidth)),
                    ("height", Format(material.ActualHeight)),
                    ("fileName", material.FileName));
                items.Add(CreateItem(
                    stableId,
                    $"module:{UnrealBridgeModule.BaseMaterials}",
                    UnrealBridgeModule.BaseMaterials,
                    $"{section.Spec.DisplayName} #{material.Index}",
                    payload,
                    material.FilePath,
                    ToToolboxRelativePath(character, material.FilePath),
                    Path.GetFileNameWithoutExtension(material.FilePath)));
            }
        }
    }

    private static void AddSkills(
        List<UnrealBridgeSnapshotItem> items,
        CharacterCard character,
        CharacterSkillsData skills)
    {
        AddSkillCollection(items, character, "first", "一技能", skills.FirstSkill);
        AddSkillCollection(items, character, "second", "二技能", skills.SecondSkill);
        AddSkillCollection(items, character, "ultimate", "终结技", skills.UltimateSkill);
        AddSkillCollection(items, character, "support", "护援技", skills.SupportSkill);
        AddSkillCollection(items, character, "combo", "连携技", skills.ComboSkills);
    }

    private static void AddSkillCollection(
        List<UnrealBridgeSnapshotItem> items,
        CharacterCard character,
        string slot,
        string slotName,
        IEnumerable<CharacterSkillEntry> entries)
    {
        var index = 0;
        foreach (var entry in entries)
        {
            index++;
            var multipliers = string.Join(";", entry.LevelMultipliers.Select(level =>
                $"{level.Level}:{level.PhysicalMultiplier}:{level.EnergyMultiplier}"));
            var payload = BuildPayload(
                ("slot", slot),
                ("index", Format(index)),
                ("positionName", entry.PositionName),
                ("trueName", entry.TrueName),
                ("description", entry.Description),
                ("ptCost", entry.PtCost),
                ("attackCapacity", entry.AttackCapacity),
                ("autoPriority", entry.AutoPriority),
                ("skillState", entry.SkillState),
                ("guardState", entry.GuardState),
                ("guardValue", entry.GuardValue),
                ("comboCharacterCode", entry.ComboCharacterCode),
                ("comboCharacterName", entry.ComboCharacterName),
                ("multipliers", multipliers));
            items.Add(CreateItem(
                $"skill:{entry.SyncId}",
                $"skill:{slot}",
                UnrealBridgeModule.Skills,
                $"{slotName} #{index} {entry.TrueName}".Trim(),
                payload,
                entry.IconPath,
                ToToolboxRelativePath(character, entry.IconPath),
                entry.TrueName));
        }
    }

    private static void AddSequenceFrames(
        List<UnrealBridgeSnapshotItem> items,
        CharacterCard character,
        CharacterSkillsData skills)
    {
        var service = new SequenceFrameService();
        foreach (var section in service.LoadSections(character, skills))
        {
            // 动作与帧的稳定 ID 必须和 Unreal 语义快照用同一套规则生成，
            // 否则两侧永远配不上对，第五步只能看到“全部新增 + 全部待删除”。
            var actionId = SequenceFrameIdentity.BuildActionStableId(section.Action.Code);
            var fps = service.GetActionFps(character, section.Action);
            items.Add(CreateItem(
                actionId,
                $"module:{UnrealBridgeModule.SequenceFrames}",
                UnrealBridgeModule.SequenceFrames,
                section.Action.DisplayName,
                SequenceFrameIdentity.BuildActionPayload(section.Action.Code, fps),
                string.Empty));

            var orderedFrames = section.Frames.OrderBy(frame => frame.Index).ToArray();
            for (var ordinal = 0; ordinal < orderedFrames.Length; ordinal++)
            {
                var frame = orderedFrames[ordinal];
                var payload = BuildPayload(
                    ("actionCode", section.Action.Code),
                    ("index", Format(frame.Index)),
                    ("durationFrames", Format(frame.DurationFrames)),
                    ("isBlank", frame.IsBlank ? "true" : "false"),
                    ("voiceFileName", frame.VoiceFileName));
                items.Add(CreateItem(
                    SequenceFrameIdentity.BuildFrameStableId(section.Action.Code, ordinal),
                    actionId,
                    UnrealBridgeModule.SequenceFrames,
                    $"{section.Action.DisplayName} 第 {frame.Index} 帧",
                    payload,
                    frame.IsBlank ? string.Empty : frame.FilePath,
                    ToToolboxRelativePath(character, frame.IsBlank ? string.Empty : frame.FilePath),
                    $"{section.Action.Code}-{frame.Index}"));
            }
        }
    }

    private static void AddBuffs(List<UnrealBridgeSnapshotItem> items, CharacterCard character)
    {
        foreach (var buff in new BuffService().Load(character).Buffs.OrderBy(buff => buff.Index))
        {
            var effects = string.Join(";", buff.Effects.Select(effect =>
                $"{effect.EffectType}:{effect.Target}:{effect.Attribute}:{effect.Operation}:{effect.Value}:{effect.ImplementationNotes}"));
            var payload = BuildPayload(
                ("userCode", buff.UserCode),
                ("generatedCode", buff.GeneratedCode),
                ("name", buff.Name),
                ("description", buff.Description),
                ("damageType", buff.DamageType),
                ("gainType", buff.GainType),
                ("stacks", Format(buff.Stacks)),
                ("completeStacks", Format(buff.CompleteStacks)),
                ("strength", Format(buff.Strength)),
                ("completeStrength", Format(buff.CompleteStrength)),
                ("endsWhenStacksReachZero", buff.EndsWhenStacksReachZero ? "true" : "false"),
                ("taskPriority", buff.TaskPriority),
                ("triggerTiming", buff.TriggerTiming),
                ("conditionSummary", buff.ConditionSummary),
                ("initializationNotes", buff.InitializationNotes),
                ("conditionUpdateNotes", buff.ConditionUpdateNotes),
                ("completionNotes", buff.CompletionNotes),
                ("removalNotes", buff.RemovalNotes),
                ("effects", effects));
            items.Add(CreateItem(
                $"buff:{buff.SyncId}",
                $"module:{UnrealBridgeModule.Buffs}",
                UnrealBridgeModule.Buffs,
                buff.DisplayName,
                payload,
                buff.IconPath,
                ToToolboxRelativePath(character, buff.IconPath),
                buff.GeneratedCode));
        }
    }

    private static void AddVoices(
        List<UnrealBridgeSnapshotItem> items,
        CharacterCard character,
        IReadOnlyList<VoiceMaterialSection> sections,
        IReadOnlyDictionary<string, string> fileIdentities)
    {
        foreach (var section in sections)
        {
            foreach (var voice in section.Items.OrderBy(item => item.Index))
            {
                var kind = section.Spec.Kind.ToString().ToLowerInvariant();
                var payload = BuildPayload(
                    ("kind", section.Spec.Kind.ToString()),
                    ("index", Format(voice.Index)),
                    ("fileName", voice.FileName));
                items.Add(CreateItem(
                    $"voice:{fileIdentities[voice.FilePath]}",
                    $"module:{UnrealBridgeModule.Voices}",
                    UnrealBridgeModule.Voices,
                    $"{section.Spec.DisplayName} #{voice.Index}",
                    payload,
                    voice.FilePath,
                    ToToolboxRelativePath(character, voice.FilePath),
                    Path.GetFileNameWithoutExtension(voice.FilePath)));
            }
        }
    }

    private static IReadOnlyDictionary<string, string> BuildFileIdentities(
        CharacterCard character,
        IReadOnlyList<BaseMaterialSection> baseMaterialSections,
        IReadOnlyList<VoiceMaterialSection> voiceSections)
    {
        var candidates = baseMaterialSections
            .SelectMany(section => section.Items)
            .Where(item => File.Exists(item.FilePath))
            .Select(item => new UnrealBridgeToolboxFileCandidate(
                UnrealBridgeModule.BaseMaterials,
                item.FilePath,
                ComputeFileContentHash(item.FilePath)))
            .Concat(voiceSections
                .SelectMany(section => section.Items)
                .Where(item => File.Exists(item.FilePath))
                .Select(item => new UnrealBridgeToolboxFileCandidate(
                    UnrealBridgeModule.Voices,
                    item.FilePath,
                    ComputeFileContentHash(item.FilePath))))
            .ToArray();
        return new UnrealBridgeToolboxIdentityService().Reconcile(character, candidates);
    }

    private static UnrealBridgeSnapshotItem CreateItem(
        string stableId,
        string parentStableId,
        UnrealBridgeModule module,
        string displayName,
        string payload,
        string assetPath,
        string toolboxRelativePath = "",
        string normalizedName = "")
    {
        if ((module is UnrealBridgeModule.BaseMaterials or UnrealBridgeModule.Voices) &&
            !string.IsNullOrWhiteSpace(assetPath) && File.Exists(assetPath))
        {
            return new UnrealBridgeSnapshotItem(
                stableId,
                parentStableId,
                module,
                displayName,
                ComputeFileContentHash(assetPath),
                payload,
                assetPath,
                ToolboxRelativePath: toolboxRelativePath,
                NormalizedName: normalizedName);
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(payload));
        if (!string.IsNullOrWhiteSpace(assetPath) && File.Exists(assetPath))
        {
            using var stream = new FileStream(assetPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[81920];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                hash.AppendData(buffer.AsSpan(0, read));
            }
        }

        return new UnrealBridgeSnapshotItem(
            stableId,
            parentStableId,
            module,
            displayName,
            Convert.ToHexString(hash.GetHashAndReset()),
            payload,
            assetPath,
            ToolboxRelativePath: toolboxRelativePath,
            NormalizedName: normalizedName);
    }

    private static string ComputeFileContentHash(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string ToToolboxRelativePath(CharacterCard character, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var rootPath = Path.GetFullPath(character.FolderPath);
        var fullPath = Path.GetFullPath(path);
        var relativePath = Path.GetRelativePath(rootPath, fullPath);
        return relativePath == ".." ||
               relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
               Path.IsPathRooted(relativePath)
            ? string.Empty
            : relativePath.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string BuildPayload(params (string Key, string? Value)[] values)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var (key, value) in values.OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                writer.WriteString(key, value ?? string.Empty);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string Join(IEnumerable<string>? values) =>
        values is null ? string.Empty : string.Join("\u001F", values.Select(value => value.Trim()));

    private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string NormalizeId(string value)
    {
        var normalized = new string(value.Trim().ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-')
            .ToArray());
        return string.IsNullOrWhiteSpace(normalized) ? "unnamed" : normalized;
    }
}
