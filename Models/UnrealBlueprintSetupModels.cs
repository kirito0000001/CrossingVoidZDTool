using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CrossingVoidZDTool.ViewModels;

namespace CrossingVoidZDTool;

internal enum UnrealBlueprintSetupStatus
{
    Unchanged,
    Pending,
    Error
}

/// <summary>
/// 第六步「蓝图置入」下发给 Unreal 的目标值。
///
/// 和第四步一样走「请求 -> 扫描/应用」而不是「导出清单 -> C# 比对」：
/// 写入侧本来就必须拿到工具箱这份目标值（FText 要保留命名空间和键、
/// 图标是 TSoftObjectPtr、状态是枚举），所以扫描和应用共用同一份载荷，
/// 比对也就留在离真实对象最近的地方，不必再维护一套「规范文本」。
/// </summary>
internal sealed class UnrealBlueprintSetupRequest
{
    public int ProtocolVersion { get; set; } = 1;

    /// <summary>Scan 只读并比对，Apply 才写盘。</summary>
    public string Mode { get; set; } = "Scan";

    public string CharacterCode { get; set; } = string.Empty;

    /// <summary>角色中文名，同时是三张数据表的行键。</summary>
    public string CharacterName { get; set; } = string.Empty;

    /// <summary>形态数量，决定各并列数组的长度。</summary>
    public int FormCount { get; set; } = 1;

    public bool Anti { get; set; }

    /// <summary>对局内头像槽 1，写入角色蓝图的 Icon1P。</summary>
    public string Icon1PObjectPath { get; set; } = string.Empty;

    /// <summary>对局内头像槽 3，写入角色蓝图的 Icon2P。</summary>
    public string Icon2PObjectPath { get; set; } = string.Empty;

    /// <summary>对局内头像槽 2，写入 SupImage 表的 Sub1P。</summary>
    public string Support1PObjectPath { get; set; } = string.Empty;

    /// <summary>对局内头像槽 4，写入 SupImage 表的 Sub2P。</summary>
    public string Support2PObjectPath { get; set; } = string.Empty;

    public string AnimInstanceClassObjectPath { get; set; } = string.Empty;

    public string IdleFlipbookObjectPath { get; set; } = string.Empty;

    /// <summary>角色蓝图上的序列数组，按形态下标存放。</summary>
    public List<UnrealBlueprintSequenceBinding> Sequences { get; set; } = [];

    /// <summary>一技能、二技能、终结技、护援技、连携技。</summary>
    public List<UnrealBlueprintSkillPayload> Skills { get; set; } = [];

    /// <summary>Apply 时只处理这些条目；Scan 时忽略。</summary>
    public HashSet<string> SelectedStableIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>一个序列数组属性的目标值。</summary>
internal sealed class UnrealBlueprintSequenceBinding
{
    /// <summary>角色蓝图上的属性名，取自 SequenceActionCatalog。</summary>
    public string PropertyName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>按形态顺序排列的 AnimSequence 对象路径，缺形态时为空串。</summary>
    public List<string> ObjectPaths { get; set; } = [];
}

/// <summary>
/// 一个技能槽的目标值。所有列表按形态并列，长度等于 <see cref="UnrealBlueprintSetupRequest.FormCount"/>。
/// </summary>
internal sealed class UnrealBlueprintSkillPayload
{
    /// <summary>
    /// SkillSlot1 / SkillSlot2 / SkillSlot3 写角色蓝图；
    /// SubSkill 写 2DSubSkill 表；Combo 写 12SkInfor 表。
    /// </summary>
    public string SlotKey { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>连携技的搭档中文名，即 SubCharName 映射的键；其它槽为空。</summary>
    public string PartnerName { get; set; } = string.Empty;

    public int Skill12Index { get; set; }

    /// <summary>技能名字（定位名）。</summary>
    public List<string> Names { get; set; } = [];

    /// <summary>技能真名。</summary>
    public List<string> SkillNames { get; set; } = [];

    public List<string> Descriptions { get; set; } = [];

    public List<string> IconObjectPaths { get; set; } = [];

    public List<int> PointCosts { get; set; } = [];

    public List<int> AttackCapacities { get; set; } = [];

    public List<int> AutoPriorities { get; set; } = [];

    /// <summary>Unreal 的 E2DSkillType 名称：Air / Normal / Disable / Abandon。</summary>
    public List<string> SkillStates { get; set; } = [];

    /// <summary>Unreal 的 EPreformType 名称：Air / Defense / Attack / Dodge。</summary>
    public List<string> PreformTypes { get; set; } = [];

    public List<double> PreSkillValues { get; set; } = [];

    public List<UnrealBlueprintSkillRate> SkillRates { get; set; } = [];
}

/// <summary>一个形态的等级倍率，键是等级字符串 "1"-"5"。</summary>
internal sealed class UnrealBlueprintSkillRate
{
    public Dictionary<string, double> Physical { get; set; } = [];

    public Dictionary<string, double> Energy { get; set; } = [];
}

internal sealed class UnrealBlueprintSetupResult
{
    public int ProtocolVersion { get; set; } = 1;
    public bool Succeeded { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public string CharacterCode { get; set; } = string.Empty;
    public DateTimeOffset CompletedAt { get; set; }
    public List<UnrealBlueprintSetupResultItem> Items { get; set; } = [];
    public List<string> AppliedStableIds { get; set; } = [];
    public List<string> SavedAssets { get; set; } = [];
}

internal sealed class UnrealBlueprintSetupResultItem
{
    public string StableId { get; set; } = string.Empty;

    /// <summary>界面分组键：onset / sequence / skill1 / skill2 / skill3 / support / combo / component。</summary>
    public string GroupKey { get; set; } = string.Empty;

    public string GroupName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>写入目标，例如角色蓝图路径或数据表路径。</summary>
    public string TargetPath { get; set; } = string.Empty;

    public string TargetField { get; set; } = string.Empty;

    public string CurrentSummary { get; set; } = string.Empty;

    public string TargetSummary { get; set; } = string.Empty;

    /// <summary>按形态并列的当前值，单值字段留空。</summary>
    public List<string> CurrentValues { get; set; } = [];

    public List<string> TargetValues { get; set; } = [];

    public UnrealBlueprintSetupStatus Status { get; set; }

    public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>中栏一张字段卡片。展示规则沿用第四步：单值直接显示，多形态铺成标签。</summary>
internal sealed class UnrealBlueprintSetupViewItem : ObservableObject
{
    private static readonly Regex UnrealObjectPathRegex = new(
        @"/Game/[^\s,;]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private bool _isSelected;

    public UnrealBlueprintSetupViewItem(UnrealBlueprintSetupResultItem source, bool isSelected)
    {
        Source = source;
        _isSelected = isSelected && IsSelectable;
    }

    public event EventHandler? SelectionChanged;

    public UnrealBlueprintSetupResultItem Source { get; }
    public string StableId => Source.StableId;
    public string GroupKey => Source.GroupKey;
    public string GroupName => Source.GroupName;
    public string DisplayName => Source.DisplayName;
    public string TargetField => Source.TargetField;
    public bool IsError => Source.Status == UnrealBlueprintSetupStatus.Error;

    /// <summary>多形态字段铺成一行标签，单形态照旧一行文本。</summary>
    public bool IsTagList => Source.CurrentValues.Count > 1 || Source.TargetValues.Count > 1;

    public string CurrentValueText => IsTagList ? string.Empty : FormatValue(Source.CurrentSummary);
    public string TargetValueText => IsTagList ? string.Empty : FormatValue(Source.TargetSummary);
    public IReadOnlyList<string> CurrentValueItems => IsTagList ? FormatValues(Source.CurrentValues) : [];
    public IReadOnlyList<string> TargetValueItems => IsTagList ? FormatValues(Source.TargetValues) : [];

    public string ErrorText => string.IsNullOrWhiteSpace(Source.ErrorMessage)
        ? "蓝图数据读取失败"
        : FormatValue(Source.ErrorMessage);

    public string StatusText => Source.Status switch
    {
        UnrealBlueprintSetupStatus.Pending => "待写入",
        UnrealBlueprintSetupStatus.Error => "读取失败",
        _ => "无变化"
    };

    public bool IsSelectable => Source.Status == UnrealBlueprintSetupStatus.Pending;

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

    private static IReadOnlyList<string> FormatValues(IReadOnlyList<string> values) =>
        values.Count == 0 ? ["空"] : values.Select(FormatValue).ToArray();

    /// <summary>对象路径在界面上只留资产名，整条 /Game/... 又长又挤不下。</summary>
    private static string FormatValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "空";
        }

        return UnrealObjectPathRegex.Replace(value, match =>
        {
            var path = match.Value.TrimEnd(',', ';', ')', ']', '}', '"', '\'');
            var assetName = path[(path.LastIndexOf('/') + 1)..];
            var dotIndex = assetName.IndexOf('.');
            return dotIndex > 0 ? assetName[..dotIndex] : assetName;
        });
    }
}

/// <summary>中栏的一个分组，例如「一技能」。</summary>
internal sealed class UnrealBlueprintSetupGroup(string groupKey, string groupName, IReadOnlyList<UnrealBlueprintSetupViewItem> items)
{
    public string GroupKey { get; } = groupKey;
    public string GroupName { get; } = groupName;
    public IReadOnlyList<UnrealBlueprintSetupViewItem> Items { get; } = items;
    public int PendingCount => Items.Count(item => item.IsSelectable);
    public string SummaryText => PendingCount > 0 ? $"待写入 {PendingCount} 项" : "无变化";
}
