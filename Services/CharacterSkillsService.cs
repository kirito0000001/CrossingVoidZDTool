using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace CrossingVoidZDTool.Services;

internal sealed class CharacterSkillsService
{
    private readonly CharacterToolboxDataService _toolboxDataService = new();
    private readonly CharacterFormService _formService = new();

    public CharacterSkillsData Load(CharacterCard character)
    {
        using var entryNotifications = CharacterSkillEntry.SuppressEditNotifications();
        using var multiplierNotifications = SkillMultiplierLevel.SuppressEditNotifications();

        var toolboxData = _toolboxDataService.Load(character);
        var requiresSyncIdentityMigration = toolboxData.Skills is null || toolboxData.Skills.SchemaVersion < 2;
        var skills = NormalizeCore(toolboxData.Skills, _formService.GetFormLimit(character));
        if (RepairMissingIconPaths(character, skills) || requiresSyncIdentityMigration)
        {
            _toolboxDataService.Update(character, current => current.Skills = skills);
        }

        return skills;
    }

    public void Save(CharacterCard character, CharacterSkillsData data)
    {
        using var entryNotifications = CharacterSkillEntry.SuppressEditNotifications();
        using var multiplierNotifications = SkillMultiplierLevel.SuppressEditNotifications();

        _toolboxDataService.Update(character, toolboxData =>
        {
            var formLimit = _formService.GetFormLimit(character);
            var existingSkills = NormalizeCore(toolboxData.Skills, formLimit);
            var nextSkills = NormalizeCore(data, formLimit);
            var mergedSkills = MergeWithExisting(existingSkills, nextSkills);
            if (HasMeaningfulSkillData(existingSkills) && !HasMeaningfulSkillData(mergedSkills))
            {
                return;
            }

            toolboxData.Skills = mergedSkills;
        });
    }

    public static CharacterSkillEntry CreateEntry()
    {
        var entry = new CharacterSkillEntry();
        for (var level = 1; level <= 5; level++)
        {
            entry.LevelMultipliers.Add(new SkillMultiplierLevel { Level = level });
        }

        return entry;
    }

    public static void NormalizeEntryForEditing(CharacterSkillEntry entry)
    {
        using var entryNotifications = CharacterSkillEntry.SuppressEditNotifications();
        using var multiplierNotifications = SkillMultiplierLevel.SuppressEditNotifications();

        NormalizeEntry(entry);
    }

    private static CharacterSkillsData Normalize(CharacterSkillsData? data, int formLimit)
    {
        using var entryNotifications = CharacterSkillEntry.SuppressEditNotifications();
        using var multiplierNotifications = SkillMultiplierLevel.SuppressEditNotifications();

        return NormalizeCore(data, formLimit);
    }

    private static CharacterSkillsData NormalizeCore(CharacterSkillsData? data, int formLimit)
    {
            data ??= CreateDefault();
            data.FirstSkill ??= [];
            data.SecondSkill ??= [];
            data.UltimateSkill ??= [];
            data.SupportSkill ??= [];
            data.ComboSkills ??= [];
            data.SchemaVersion = 2;
            var normalizedFormLimit = Math.Max(1, formLimit);
            SyncCollectionCount(data.FirstSkill, normalizedFormLimit);
            SyncCollectionCount(data.SecondSkill, normalizedFormLimit);
            SyncCollectionCount(data.UltimateSkill, normalizedFormLimit);
            EnsureMinimumCount(data.SupportSkill, 1);
            foreach (var entry in data.FirstSkill)
            {
                entry.CanDelete = false;
                NormalizeEntry(entry);
            }

            foreach (var entry in data.SecondSkill)
            {
                entry.CanDelete = false;
                NormalizeEntry(entry);
            }

            foreach (var entry in data.UltimateSkill)
            {
                entry.CanDelete = false;
                NormalizeEntry(entry);
            }

            foreach (var entry in data.SupportSkill)
            {
                entry.CanDelete = data.SupportSkill.Count > 1;
                NormalizeEntry(entry);
            }

            foreach (var entry in data.ComboSkills)
            {
                entry.CanDelete = true;
                NormalizeEntry(entry);
            }

            return data;
    }

    private static CharacterSkillsData CreateDefault()
    {
        var data = new CharacterSkillsData();
        data.FirstSkill.Add(CreateEntry());
        data.SecondSkill.Add(CreateEntry());
        data.UltimateSkill.Add(CreateEntry());
        data.SupportSkill.Add(CreateEntry());
        return data;
    }

    private static void SyncCollectionCount(ObservableCollection<CharacterSkillEntry> entries, int targetCount)
    {
        while (entries.Count < targetCount)
        {
            entries.Add(CreateEntry());
        }

        while (entries.Count > targetCount)
        {
            entries.RemoveAt(entries.Count - 1);
        }
    }

    private static void EnsureMinimumCount(ObservableCollection<CharacterSkillEntry> entries, int minimumCount)
    {
        while (entries.Count < minimumCount)
        {
            entries.Add(CreateEntry());
        }
    }

    private static void NormalizeEntry(CharacterSkillEntry entry)
    {
        entry.SyncId = string.IsNullOrWhiteSpace(entry.SyncId)
            ? Guid.NewGuid().ToString("N")
            : entry.SyncId.Trim();
        RefreshIconUri(entry);
        entry.SkillState = NormalizeSkillState(entry.SkillState);
        entry.GuardState = NormalizeGuardState(entry.GuardState);

        entry.LevelMultipliers ??= [];
        while (entry.LevelMultipliers.Count < 5)
        {
            entry.LevelMultipliers.Add(new SkillMultiplierLevel { Level = entry.LevelMultipliers.Count + 1 });
        }

        for (var index = 0; index < entry.LevelMultipliers.Count; index++)
        {
            entry.LevelMultipliers[index].Level = index + 1;
        }
    }

    private static bool HasMeaningfulSkillData(CharacterSkillsData data)
    {
        return HasMeaningfulSkillData(data.FirstSkill) ||
            HasMeaningfulSkillData(data.SecondSkill) ||
            HasMeaningfulSkillData(data.UltimateSkill) ||
            HasMeaningfulSkillData(data.SupportSkill) ||
            HasMeaningfulSkillData(data.ComboSkills);
    }

    private static CharacterSkillsData MergeWithExisting(CharacterSkillsData existing, CharacterSkillsData next)
    {
        MergeEntries(existing.FirstSkill, next.FirstSkill);
        MergeEntries(existing.SecondSkill, next.SecondSkill);
        MergeEntries(existing.UltimateSkill, next.UltimateSkill);
        MergeEntries(existing.SupportSkill, next.SupportSkill);
        MergeEntries(existing.ComboSkills, next.ComboSkills);
        return next;
    }

    private static void MergeEntries(
        ObservableCollection<CharacterSkillEntry> existingEntries,
        ObservableCollection<CharacterSkillEntry> nextEntries)
    {
        var count = Math.Min(existingEntries.Count, nextEntries.Count);
        for (var index = 0; index < count; index++)
        {
            MergeEntry(existingEntries[index], nextEntries[index]);
        }
    }

    private static void MergeEntry(CharacterSkillEntry existing, CharacterSkillEntry next)
    {
        next.SyncId = string.IsNullOrWhiteSpace(next.SyncId) ? existing.SyncId : next.SyncId;
        next.PositionName = KeepExistingWhenBlank(existing.PositionName, next.PositionName);
        next.Description = KeepExistingWhenBlank(existing.Description, next.Description);
        next.PtCost = KeepExistingWhenBlank(existing.PtCost, next.PtCost);
        next.AutoPriority = KeepExistingWhenBlank(existing.AutoPriority, next.AutoPriority);
        next.GuardValue = KeepExistingWhenBlank(existing.GuardValue, next.GuardValue);
        next.TrueName = KeepExistingWhenBlank(existing.TrueName, next.TrueName);
        next.AttackCapacity = KeepExistingWhenBlank(existing.AttackCapacity, next.AttackCapacity);
        next.IconPath = KeepExistingWhenBlank(existing.IconPath, next.IconPath);
        next.ComboCharacterCode = KeepExistingWhenBlank(existing.ComboCharacterCode, next.ComboCharacterCode);
        next.ComboCharacterName = KeepExistingWhenBlank(existing.ComboCharacterName, next.ComboCharacterName);
        next.SkillState = KeepExistingWhenBlankOrDefault(existing.SkillState, next.SkillState, "空");
        next.GuardState = KeepExistingWhenBlankOrDefault(existing.GuardState, next.GuardState, "空");

        var multiplierCount = Math.Min(existing.LevelMultipliers.Count, next.LevelMultipliers.Count);
        for (var index = 0; index < multiplierCount; index++)
        {
            next.LevelMultipliers[index].PhysicalMultiplier = KeepExistingWhenBlank(
                existing.LevelMultipliers[index].PhysicalMultiplier,
                next.LevelMultipliers[index].PhysicalMultiplier);
            next.LevelMultipliers[index].EnergyMultiplier = KeepExistingWhenBlank(
                existing.LevelMultipliers[index].EnergyMultiplier,
                next.LevelMultipliers[index].EnergyMultiplier);
        }
    }

    private static string KeepExistingWhenBlank(string existing, string next)
    {
        return string.IsNullOrWhiteSpace(next) && !string.IsNullOrWhiteSpace(existing)
            ? existing
            : next;
    }

    private static string KeepExistingWhenBlankOrDefault(string existing, string next, string defaultValue)
    {
        if (!string.IsNullOrWhiteSpace(existing) &&
            !string.Equals(existing, defaultValue, StringComparison.Ordinal) &&
            (string.IsNullOrWhiteSpace(next) || string.Equals(next, defaultValue, StringComparison.Ordinal)))
        {
            return existing;
        }

        return string.IsNullOrWhiteSpace(next) ? defaultValue : next;
    }

    private static bool HasMeaningfulSkillData(ObservableCollection<CharacterSkillEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (!string.IsNullOrWhiteSpace(entry.PositionName) ||
                !string.IsNullOrWhiteSpace(entry.TrueName) ||
                !string.IsNullOrWhiteSpace(entry.PtCost) ||
                !string.IsNullOrWhiteSpace(entry.AttackCapacity) ||
                !string.IsNullOrWhiteSpace(entry.Description) ||
                !string.IsNullOrWhiteSpace(entry.AutoPriority) ||
                HasNonDefaultOption(entry.SkillState, "空") ||
                HasNonDefaultOption(entry.GuardState, "空") ||
                !string.IsNullOrWhiteSpace(entry.GuardValue) ||
                !string.IsNullOrWhiteSpace(entry.IconPath) ||
                !string.IsNullOrWhiteSpace(entry.ComboCharacterCode) ||
                !string.IsNullOrWhiteSpace(entry.ComboCharacterName) ||
                entry.LevelMultipliers.Any(level =>
                    !string.IsNullOrWhiteSpace(level.PhysicalMultiplier) ||
                    !string.IsNullOrWhiteSpace(level.EnergyMultiplier)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasNonDefaultOption(string value, string defaultValue)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            !string.Equals(value, defaultValue, StringComparison.Ordinal);
    }

    private static void RefreshIconUri(CharacterSkillEntry entry)
    {
        entry.IconUri = File.Exists(entry.IconPath)
            ? new Uri(entry.IconPath).AbsoluteUri
            : string.Empty;
    }

    private static bool RepairMissingIconPaths(CharacterCard character, CharacterSkillsData skills)
    {
        var repaired = false;
        foreach (var entry in EnumerateEntries(skills))
        {
            var resolvedPath = ResolveRenumberedIconPath(character, entry.IconPath);
            if (string.IsNullOrWhiteSpace(resolvedPath) ||
                string.Equals(entry.IconPath, resolvedPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            entry.IconPath = resolvedPath;
            RefreshIconUri(entry);
            repaired = true;
        }

        return repaired;
    }

    private static IEnumerable<CharacterSkillEntry> EnumerateEntries(CharacterSkillsData skills)
    {
        return skills.FirstSkill
            .Concat(skills.SecondSkill)
            .Concat(skills.UltimateSkill)
            .Concat(skills.SupportSkill)
            .Concat(skills.ComboSkills);
    }

    private static string? ResolveRenumberedIconPath(CharacterCard character, string iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath) || File.Exists(iconPath))
        {
            return null;
        }

        var fileName = Path.GetFileName(iconPath);
        if (!TryResolveSkillIconIndex(character.Code, fileName, out var expectedIndex))
        {
            return null;
        }

        var iconFolderPath = Path.Combine(character.FolderPath, "AssetMaterial", "SkillIcon");
        if (!Directory.Exists(iconFolderPath))
        {
            return null;
        }

        var matches = Directory
            .EnumerateFiles(iconFolderPath)
            .Where(path => TryResolveSkillIconIndex(character.Code, Path.GetFileName(path), out var index) &&
                           index == expectedIndex)
            .Take(2)
            .Select(Path.GetFullPath)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static bool TryResolveSkillIconIndex(string characterCode, string fileName, out int index)
    {
        index = 0;
        if (!string.Equals(Path.GetExtension(fileName), ".png", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var name = Path.GetFileNameWithoutExtension(fileName);
        var prefix = $"{characterCode}-SkillIcon-";
        return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(name[prefix.Length..], out index) &&
               index > 0;
    }

    private static string NormalizeSkillState(string value)
    {
        return NormalizeOption(value) switch
        {
            "normal" => "常态",
            "disable" => "禁用",
            "abandon" => "舍弃",
            "air" or "" => "空",
            "常态" or "禁用" or "舍弃" or "空" => value.Trim(),
            _ => string.IsNullOrWhiteSpace(value) ? "空" : value.Trim()
        };
    }

    private static string NormalizeGuardState(string value)
    {
        return NormalizeOption(value) switch
        {
            "defense" => "防御",
            "attack" => "反击",
            "dodge" => "闪避",
            "air" or "" => "空",
            "防御" or "反击" or "闪避" or "空" => value.Trim(),
            _ => string.IsNullOrWhiteSpace(value) ? "空" : value.Trim()
        };
    }

    private static string NormalizeOption(string value)
    {
        var text = value?.Trim() ?? string.Empty;
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

        return text.Trim().Trim('<', '>', ' ').ToLowerInvariant();
    }
}
