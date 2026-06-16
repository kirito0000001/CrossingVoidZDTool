using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;
using Microsoft.UI.Xaml.Controls;

namespace CrossingVoidZDTool.ViewModels;

internal sealed class UnrealSyncViewModel : ObservableObject
{
    private readonly CharacterInfoService _characterInfoService;
    private CharacterInfoData _data = new();
    private bool _isLoading;
    private bool _isDirty;
    private string _statusText = "就绪：等待选择角色。";
    private string _noticeTitle = "未选择角色";
    private string _noticeMessage = "请先在零境角色台选择当前制作角色。";
    private InfoBarSeverity _noticeSeverity = InfoBarSeverity.Informational;
    private bool _isNoticeOpen = true;
    private string _code = string.Empty;
    private string _characterName = string.Empty;
    private string _description = string.Empty;
    private string _saveStatusText = "未打开角色信息。";
    private CharacterInfoStat _formLimitStat = new(CharacterInfoStatKind.FormLimit, "形态上限", 1);

    public UnrealSyncViewModel(CharacterInfoService characterInfoService)
    {
        _characterInfoService = characterInfoService;
    }

    public event EventHandler? CharacterInfoEdited;

    public ObservableCollection<BaseMaterialSection> MaterialSections { get; } = [];

    public ObservableCollection<CharacterInfoTextEntry> KeywordTags { get; } = [];

    public ObservableCollection<CharacterInfoTextEntry> PassiveSkills { get; } = [];

    public ObservableCollection<CharacterInfoStat> Stats { get; } = [];

    public CharacterInfoStat FormLimitStat
    {
        get => _formLimitStat;
        private set => SetProperty(ref _formLimitStat, value);
    }

    public string Code
    {
        get => _code;
        set
        {
            if (SetProperty(ref _code, value) && !_isLoading)
            {
                MarkEdited();
            }
        }
    }

    public string CharacterName
    {
        get => _characterName;
        set
        {
            if (SetProperty(ref _characterName, value) && !_isLoading)
            {
                MarkEdited();
            }
        }
    }

    public string Description
    {
        get => _description;
        set
        {
            if (SetProperty(ref _description, value) && !_isLoading)
            {
                MarkEdited();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string SaveStatusText
    {
        get => _saveStatusText;
        private set => SetProperty(ref _saveStatusText, value);
    }

    public string NoticeTitle
    {
        get => _noticeTitle;
        private set => SetProperty(ref _noticeTitle, value);
    }

    public string NoticeMessage
    {
        get => _noticeMessage;
        private set => SetProperty(ref _noticeMessage, value);
    }

    public InfoBarSeverity NoticeSeverity
    {
        get => _noticeSeverity;
        private set => SetProperty(ref _noticeSeverity, value);
    }

    public bool IsNoticeOpen
    {
        get => _isNoticeOpen;
        private set => SetProperty(ref _isNoticeOpen, value);
    }

    public async Task LoadAsync(CharacterCard? character, CancellationToken cancellationToken = default)
    {
        _isLoading = true;
        try
        {
            MaterialSections.Clear();
            KeywordTags.Clear();
            PassiveSkills.Clear();
            Stats.Clear();

            if (character is null)
            {
                _data = new CharacterInfoData();
                Code = string.Empty;
                CharacterName = string.Empty;
                Description = string.Empty;
                FormLimitStat = new CharacterInfoStat(CharacterInfoStatKind.FormLimit, "形态上限", 1);
                SetNotice(InfoBarSeverity.Informational, "未选择角色", "请先在零境角色台选择当前制作角色。");
                StatusText = "就绪：等待选择角色。";
                SaveStatusText = "未打开角色信息。";
                return;
            }

            var loadResult = await Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return (
                        Info: _characterInfoService.Load(character),
                        Materials: _characterInfoService.LoadMaterialSections(character));
                },
                cancellationToken);

            _data = loadResult.Info;
            Code = _data.Code;
            CharacterName = _data.Name;
            Description = _data.Description;
            FormLimitStat = new CharacterInfoStat(CharacterInfoStatKind.FormLimit, "形态上限", Math.Max(1, _data.FormLimit));
            foreach (var tag in _data.KeywordTags.DefaultIfEmpty(string.Empty))
            {
                KeywordTags.Add(new CharacterInfoTextEntry(tag));
            }

            foreach (var passive in _data.PassiveSkills.DefaultIfEmpty(string.Empty))
            {
                PassiveSkills.Add(new CharacterInfoTextEntry(passive));
            }

            foreach (var stat in CreateStats(_data))
            {
                Stats.Add(stat);
            }

            foreach (var section in loadResult.Materials)
            {
                MaterialSections.Add(section);
                await Task.Yield();
            }

            UpdateNotice();

            _isDirty = false;
            StatusText = $"角色信息已打开：{character.Name}";
            SaveStatusText = "修改会自动保存。";
        }
        finally
        {
            _isLoading = false;
        }
    }

    public async Task<bool> SaveAsync(CharacterCard? character, CancellationToken cancellationToken = default)
    {
        if (character is null || !_isDirty)
        {
            return false;
        }

        var snapshot = BuildDataSnapshot();
        await Task.Run(() => _characterInfoService.Save(character, snapshot), cancellationToken);
        _data = snapshot;
        _isDirty = false;
        SaveStatusText = $"已保存：{DateTime.Now:HH:mm:ss}";
        return true;
    }

    public bool SaveNow(CharacterCard? character)
    {
        if (character is null || !_isDirty)
        {
            return false;
        }

        var snapshot = BuildDataSnapshot();
        _characterInfoService.Save(character, snapshot);
        _data = snapshot;
        _isDirty = false;
        SaveStatusText = $"已保存：{DateTime.Now:HH:mm:ss}";
        return true;
    }

    public void AddPassiveSkill()
    {
        PassiveSkills.Add(new CharacterInfoTextEntry(string.Empty));
        MarkEdited();
    }

    public void AddKeywordTag()
    {
        KeywordTags.Add(new CharacterInfoTextEntry(string.Empty));
        MarkEdited();
    }

    public void ChangeStat(CharacterInfoStat stat, int delta)
    {
        SetStat(stat, stat.Value + delta);
    }

    public void SetStat(CharacterInfoStat stat, int value)
    {
        if (stat.Kind == CharacterInfoStatKind.FormLimit)
        {
            value = Math.Max(1, value);
        }

        stat.Value = value;
        UpdateNotice();
        MarkEdited();
    }

    private CharacterInfoData BuildDataSnapshot()
    {
        var data = new CharacterInfoData
        {
            Code = Code.Trim(),
            Name = CharacterName.Trim(),
            Description = Description,
            UpdatedAt = DateTime.Now
        };

        foreach (var tag in KeywordTags
                     .Select(tag => tag.Value.Trim())
                     .Where(tag => !string.IsNullOrWhiteSpace(tag))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            data.KeywordTags.Add(tag);
        }

        foreach (var passive in PassiveSkills
                     .Select(passive => passive.Value)
                     .Where(passive => !string.IsNullOrWhiteSpace(passive)))
        {
            data.PassiveSkills.Add(passive);
        }

        ApplyStatValue(data, FormLimitStat);

        foreach (var stat in Stats)
        {
            ApplyStatValue(data, stat);
        }

        return data;
    }

    private void MarkEdited()
    {
        _isDirty = true;
        SaveStatusText = "有修改，等待自动保存...";
        CharacterInfoEdited?.Invoke(this, EventArgs.Empty);
    }

    private void SetNotice(InfoBarSeverity severity, string title, string message)
    {
        NoticeSeverity = severity;
        NoticeTitle = title;
        NoticeMessage = message;
        IsNoticeOpen = true;
    }

    private void UpdateNotice()
    {
        var zeroStats = Stats
            .Where(stat => stat.Kind != CharacterInfoStatKind.Synchronize && stat.Value == 0)
            .Select(stat => stat.DisplayName)
            .ToArray();
        if (zeroStats.Length > 0)
        {
            SetNotice(
                InfoBarSeverity.Warning,
                "角色数值存在 0",
                $"请确认这些数值是否需要填写：{string.Join("、", zeroStats)}。");
            return;
        }

        var requiredSections = MaterialSections
            .Where(section => section.Spec.MinimumCount > 0)
            .ToArray();
        var missingSections = requiredSections
            .Select(section => new
                MaterialMissingNotice(section.Spec.DisplayName, Math.Max(0, section.Spec.MinimumCount - section.Items.Count)))
            .Where(section => section.MissingCount > 0)
            .ToArray();
        var invalidItems = requiredSections
            .SelectMany(section => section.Items
                .Where(item => item.Status == BaseMaterialStatus.Invalid)
                .Select(item => $"{section.Spec.DisplayName}/{item.FileName}：{item.StatusText}"))
            .ToArray();
        var missing = missingSections.Sum(section => section.MissingCount);
        var invalid = invalidItems.Length;
        var detail = BuildMaterialNoticeDetail(missingSections, invalidItems);
        SetNotice(
            missing > 0 || invalid > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success,
            missing > 0 || invalid > 0 ? "角色信息素材未完整" : "角色信息已同步",
            missing > 0 || invalid > 0
                ? $"{detail}素材修改请回到 St2。"
                : "图标-道具、头像、幻形立绘、幻形完整立绘已从 St2 同步显示。");
    }

    private static string BuildMaterialNoticeDetail(
        IReadOnlyList<MaterialMissingNotice> missingSections,
        IReadOnlyList<string> invalidItems)
    {
        var details = new System.Collections.Generic.List<string>();
        foreach (var section in missingSections)
        {
            details.Add($"{section.DisplayName}缺 {section.MissingCount} 张");
        }

        details.AddRange(invalidItems.Take(3));
        if (invalidItems.Count > 3)
        {
            details.Add($"另有 {invalidItems.Count - 3} 个尺寸问题");
        }

        return details.Count == 0
            ? string.Empty
            : $"{string.Join("；", details)}。";
    }

    private sealed record MaterialMissingNotice(string DisplayName, int MissingCount);

    private static CharacterInfoStat[] CreateStats(CharacterInfoData data)
    {
        return
        [
            new(CharacterInfoStatKind.Speed, "速度", data.Speed),
            new(CharacterInfoStatKind.Health, "生命值", data.Health),
            new(CharacterInfoStatKind.Attack, "攻击力", data.Attack),
            new(CharacterInfoStatKind.PhysicalDefense, "物理防御", data.PhysicalDefense),
            new(CharacterInfoStatKind.EnergyDefense, "异能防御", data.EnergyDefense),
            new(CharacterInfoStatKind.CriticalRate, "暴击率", data.CriticalRate),
            new(CharacterInfoStatKind.CriticalDamage, "暴击伤害", data.CriticalDamage),
            new(CharacterInfoStatKind.Synchronize, "同步率", data.Synchronize)
        ];
    }

    private static void ApplyStatValue(CharacterInfoData data, CharacterInfoStat stat)
    {
        switch (stat.Kind)
        {
            case CharacterInfoStatKind.Speed:
                data.Speed = stat.Value;
                break;
            case CharacterInfoStatKind.FormLimit:
                data.FormLimit = Math.Max(1, stat.Value);
                break;
            case CharacterInfoStatKind.Health:
                data.Health = stat.Value;
                break;
            case CharacterInfoStatKind.Attack:
                data.Attack = stat.Value;
                break;
            case CharacterInfoStatKind.PhysicalDefense:
                data.PhysicalDefense = stat.Value;
                break;
            case CharacterInfoStatKind.EnergyDefense:
                data.EnergyDefense = stat.Value;
                break;
            case CharacterInfoStatKind.CriticalRate:
                data.CriticalRate = stat.Value;
                break;
            case CharacterInfoStatKind.CriticalDamage:
                data.CriticalDamage = stat.Value;
                break;
            case CharacterInfoStatKind.Synchronize:
                data.Synchronize = stat.Value;
                break;
        }
    }

    public void NotifyTextEntryEdited()
    {
        if (!_isLoading)
        {
            MarkEdited();
        }
    }
}
