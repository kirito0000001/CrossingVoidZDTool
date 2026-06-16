using System;
using System.Collections.ObjectModel;

namespace CrossingVoidZDTool;

internal sealed class CharacterInfoData
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string ItemIconPath { get; set; } = string.Empty;

    public Collection<string> KeywordTags { get; set; } = [];

    public Collection<string> PassiveSkills { get; set; } = [];

    public int FormLimit { get; set; } = 1;

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
