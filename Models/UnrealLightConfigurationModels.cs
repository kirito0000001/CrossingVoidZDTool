using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Linq;
using System.Text.RegularExpressions;
using CrossingVoidZDTool.ViewModels;

namespace CrossingVoidZDTool;

internal enum UnrealLightConfigurationStatus
{
    Unchanged,
    Pending,
    Error
}

internal sealed class UnrealLightConfigurationRequest
{
    public int ProtocolVersion { get; set; } = 1;
    public string Mode { get; set; } = "Scan";
    public string CharacterCode { get; set; } = string.Empty;
    public string CharacterName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Keywords { get; set; } = [];
    public string TeamSelectObjectPath { get; set; } = string.Empty;
    public string TeamVoiceObjectPath { get; set; } = string.Empty;
    public string ItemObjectPath { get; set; } = string.Empty;
    public string ItemTypeObjectPath { get; set; } = string.Empty;
    public string ItemIconObjectPath { get; set; } = string.Empty;
    public List<string> ShapeIconObjectPaths { get; set; } = [];
    public List<string> ShapePortraitObjectPaths { get; set; } = [];
    public List<string> ShapeCompleteObjectPaths { get; set; } = [];
    public List<string> PassiveSkills { get; set; } = [];
    public int Speed { get; set; }
    public int Health { get; set; }
    public int Attack { get; set; }
    public int PhysicalDefense { get; set; }
    public int EnergyDefense { get; set; }
    public int CriticalRate { get; set; }
    public int CriticalDamage { get; set; }
    public string MetaSoundObjectPath { get; set; } = string.Empty;
    public string HurtConcurrencyObjectPath { get; set; } = string.Empty;
    public string TalkConcurrencyObjectPath { get; set; } = string.Empty;
    public List<string> HurtVoiceObjectPaths { get; set; } = [];
    public List<string> TalkVoiceObjectPaths { get; set; } = [];
    // 目前只序列化给 Python 读，没有 C# 读回路径。标上 Populate 是预防：
    // 哪天加了读回，System.Text.Json 会新建一个默认比较器的集合再赋值，
    // 声明处的 OrdinalIgnoreCase 就丢了，而这类丢失是静默的。
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public HashSet<string> SelectedStableIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class UnrealLightConfigurationResult
{
    public int ProtocolVersion { get; set; } = 1;
    public bool Succeeded { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public string CharacterCode { get; set; } = string.Empty;
    public DateTimeOffset CompletedAt { get; set; }
    public List<UnrealLightConfigurationResultItem> Items { get; set; } = [];
    public List<string> AppliedStableIds { get; set; } = [];
    public List<string> SavedAssets { get; set; } = [];
}

internal sealed class UnrealLightConfigurationResultItem
{
    public string StableId { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public string TargetField { get; set; } = string.Empty;
    public string SourceSummary { get; set; } = string.Empty;
    public string CurrentSummary { get; set; } = string.Empty;
    public string TargetSummary { get; set; } = string.Empty;
    public List<string> CurrentValues { get; set; } = [];
    public List<string> TargetValues { get; set; } = [];
    public UnrealLightConfigurationStatus Status { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

internal sealed class UnrealLightConfigurationViewItem : ObservableObject
{
    private static readonly Regex UnrealObjectPathRegex = new(
        @"/Game/[^\s]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private bool _isSelected;

    public UnrealLightConfigurationViewItem(UnrealLightConfigurationResultItem source, bool isSelected)
    {
        Source = source;
        _isSelected = isSelected && IsSelectable;
    }

    public event EventHandler? SelectionChanged;

    public UnrealLightConfigurationResultItem Source { get; }
    public string StableId => Source.StableId;
    public string GroupName => Source.GroupName;
    public string DisplayName => Source.DisplayName;
    public bool IsError => Source.Status == UnrealLightConfigurationStatus.Error;
    public bool IsListValue => Source.CurrentValues.Count > 0 ||
        Source.TargetValues.Count > 0 ||
        Source.StableId is "item.keywords" or "item.passive";
    public bool IsCountOnly => Source.StableId is "meta.waves" or "meta.weights";
    public bool IsTagList => IsListValue && !IsCountOnly &&
        (Source.CurrentValues.Count > 1 || Source.TargetValues.Count > 1 || Source.StableId is "item.keywords" or "item.passive");
    public string CurrentValueText => IsTagList ? string.Empty : IsCountOnly
        ? FormatCount(Source.CurrentValues, Source.CurrentSummary)
        : FormatValue(Source.CurrentSummary);
    public string TargetValueText => IsTagList ? string.Empty : IsCountOnly
        ? FormatCount(Source.TargetValues, Source.TargetSummary)
        : FormatValue(Source.TargetSummary);
    public IReadOnlyList<string> CurrentValueItems => IsTagList
        ? GetValues(Source.CurrentValues, Source.CurrentSummary)
        : Array.Empty<string>();
    public IReadOnlyList<string> TargetValueItems => IsTagList
        ? GetValues(Source.TargetValues, Source.TargetSummary)
        : Array.Empty<string>();
    public string ErrorText => string.IsNullOrWhiteSpace(Source.ErrorMessage)
        ? "基础配置读取失败"
        : FormatValue(Source.ErrorMessage);

    private static IReadOnlyList<string> GetValues(IReadOnlyList<string> values, string summary)
    {
        if (values.Count > 0)
        {
            return values.Select(FormatValue).Where(item => !string.IsNullOrWhiteSpace(item)).ToArray();
        }

        if (string.IsNullOrWhiteSpace(summary))
        {
            return ["空"];
        }

        return summary
            .Split('、', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(FormatValue)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
    }

    private static string FormatCount(IReadOnlyList<string> values, string summary)
    {
        if (values.Count > 0)
        {
            return $"共 {values.Count} 项";
        }

        var match = Regex.Match(summary ?? string.Empty, @"^\s*(\d+)\s*项");
        return match.Success ? $"共 {match.Groups[1].Value} 项" : "共 0 项";
    }

    private static string FormatValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "空";
        }

        return UnrealObjectPathRegex.Replace(value, match =>
        {
            var path = match.Value.TrimEnd(',', ';', ')', ']', '}', '"');
            var assetName = path[(path.LastIndexOf('/') + 1)..];
            var dotIndex = assetName.IndexOf('.');
            return dotIndex > 0 ? assetName[..dotIndex] : assetName;
        });
    }
    public string StatusText => Source.Status switch
    {
        UnrealLightConfigurationStatus.Pending => "待设置",
        UnrealLightConfigurationStatus.Error => "配置错误",
        _ => "无变化"
    };
    public bool IsSelectable => Source.Status == UnrealLightConfigurationStatus.Pending;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            var next = IsSelectable && value;
            if (SetProperty(ref _isSelected, next))
            {
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
