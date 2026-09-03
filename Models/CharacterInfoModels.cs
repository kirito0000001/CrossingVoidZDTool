using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace CrossingVoidZDTool;

internal sealed class CharacterInfoData
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string ItemIconPath { get; set; } = string.Empty;

    public Collection<string> KeywordTags { get; set; } = [];

    public CharacterKeywordTagGroups? KeywordTagGroups { get; set; }

    public Collection<string> PassiveSkills { get; set; } = [];

    public int FormLimit { get; set; } = 1;

    public bool Anti { get; set; }

    public int Speed { get; set; } = 0;

    public int Health { get; set; } = 2000;

    public int Attack { get; set; } = 50;

    public int PhysicalDefense { get; set; } = 50;

    public int EnergyDefense { get; set; } = 50;

    public int CriticalRate { get; set; } = 10;

    public int CriticalDamage { get; set; } = 10;

    public int Synchronize { get; set; } = 0;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

internal enum CharacterInfoStatKind
{
    FormLimit,
    Speed,
    Health,
    Attack,
    PhysicalDefense,
    EnergyDefense,
    CriticalRate,
    CriticalDamage,
    Synchronize
}

internal sealed class CharacterInfoStat : CrossingVoidZDTool.ViewModels.ObservableObject
{
    private int _value;

    public CharacterInfoStat(CharacterInfoStatKind kind, string displayName, int value)
    {
        Kind = kind;
        DisplayName = displayName;
        _value = value;
    }

    public CharacterInfoStatKind Kind { get; }

    public string DisplayName { get; }

    public int Value
    {
        get => _value;
        set => SetProperty(ref _value, Math.Max(0, value));
    }
}

internal sealed class CharacterInfoTextEntry : CrossingVoidZDTool.ViewModels.ObservableObject
{
    private string _value;

    public CharacterInfoTextEntry(string value)
    {
        _value = value;
    }

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }
}

internal enum CharacterKeywordTagCategoryKind
{
    CharacterName,
    Work,
    Period,
    AbilityType,
    Affiliation,
    Alias
}

internal sealed class CharacterKeywordTagGroups
{
    public Collection<string> Works { get; set; } = [];

    public Collection<string> Periods { get; set; } = [];

    public Collection<string> AbilityTypes { get; set; } = [];

    public Collection<string> Affiliations { get; set; } = [];

    public Collection<string> Aliases { get; set; } = [];

    public Collection<string> LegacyKeywordTags { get; set; } = [];
}

internal sealed class CharacterKeywordTagEntry : CrossingVoidZDTool.ViewModels.ObservableObject
{
    private string _value;
    private bool _canRemove;

    public CharacterKeywordTagEntry(CharacterKeywordTagCategoryKind categoryKind, string value, bool isReadOnly)
    {
        CategoryKind = categoryKind;
        _value = value;
        IsReadOnly = isReadOnly;
    }

    public CharacterKeywordTagCategoryKind CategoryKind { get; }

    public bool IsReadOnly { get; }

    public bool CanRemove
    {
        get => _canRemove;
        internal set => SetProperty(ref _canRemove, value);
    }

    public string Value
    {
        get => _value;
        set
        {
            if (!IsReadOnly)
            {
                SetProperty(ref _value, value);
            }
        }
    }
}

internal sealed class CharacterKeywordTagCategory
{
    private CharacterKeywordTagCategory(
        CharacterKeywordTagCategoryKind kind,
        string displayName,
        bool isReadOnly,
        IEnumerable<string> values)
    {
        Kind = kind;
        DisplayName = displayName;
        IsReadOnly = isReadOnly;
        foreach (var value in values)
        {
            Entries.Add(new CharacterKeywordTagEntry(kind, value, isReadOnly));
        }

        if (Entries.Count == 0)
        {
            Entries.Add(new CharacterKeywordTagEntry(kind, string.Empty, isReadOnly));
        }

        RefreshRemovalState();
    }

    public CharacterKeywordTagCategoryKind Kind { get; }

    public string DisplayName { get; }

    public bool IsReadOnly { get; }

    public bool CanAdd => !IsReadOnly;

    public ObservableCollection<CharacterKeywordTagEntry> Entries { get; } = [];

    public static CharacterKeywordTagCategory CreateCharacterNames(string englishCode, string chineseName)
    {
        return new CharacterKeywordTagCategory(
            CharacterKeywordTagCategoryKind.CharacterName,
            "角色名字",
            true,
            [(englishCode ?? string.Empty).Trim(), (chineseName ?? string.Empty).Trim()]);
    }

    public static CharacterKeywordTagCategory CreateEditable(
        CharacterKeywordTagCategoryKind kind,
        string displayName,
        IEnumerable<string> values)
    {
        return new CharacterKeywordTagCategory(kind, displayName, false, values);
    }

    public bool TryAddEntry()
    {
        if (IsReadOnly)
        {
            return false;
        }

        Entries.Add(new CharacterKeywordTagEntry(Kind, string.Empty, false));
        RefreshRemovalState();
        return true;
    }

    public bool TryRemoveEntry(CharacterKeywordTagEntry entry)
    {
        if (IsReadOnly || Entries.Count <= 1 || !Entries.Remove(entry))
        {
            return false;
        }

        RefreshRemovalState();
        return true;
    }

    public IEnumerable<string> GetValues()
    {
        return Entries.Select(entry => entry.Value);
    }

    private void RefreshRemovalState()
    {
        var canRemove = !IsReadOnly && Entries.Count > 1;
        foreach (var entry in Entries)
        {
            entry.CanRemove = canRemove;
        }
    }
}

internal static class CharacterKeywordTagRules
{
    public static IReadOnlyList<string> GetMissingCategoryNames(
        string englishCode,
        string chineseName,
        CharacterKeywordTagGroups groups)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(englishCode) || string.IsNullOrWhiteSpace(chineseName))
        {
            missing.Add("角色名字");
        }

        AddMissing(missing, "所属作品", groups.Works);
        AddMissing(missing, "角色时期", groups.Periods);
        AddMissing(missing, "能力类型", groups.AbilityTypes);
        AddMissing(missing, "阵营/组织归属", groups.Affiliations);
        AddMissing(missing, "外号", groups.Aliases);
        return missing;
    }

    public static IReadOnlyList<string> Flatten(
        string englishCode,
        string chineseName,
        CharacterKeywordTagGroups groups)
    {
        return new[] { englishCode, chineseName }
            .Concat(groups.Works)
            .Concat(groups.Periods)
            .Concat(groups.AbilityTypes)
            .Concat(groups.Affiliations)
            .Concat(groups.Aliases)
            .Concat(groups.LegacyKeywordTags)
            .Select(value => (value ?? string.Empty).Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddMissing(List<string> missing, string displayName, IEnumerable<string> values)
    {
        if (!values.Any(value => !string.IsNullOrWhiteSpace(value)))
        {
            missing.Add(displayName);
        }
    }
}
