using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;

namespace CrossingVoidZDTool.ViewModels;

internal sealed class SkillsViewModel : ObservableObject
{
    private readonly CharacterSkillsService _skillsService;
    private readonly DispatcherQueue? _dispatcherQueue;
    private bool _isLoading;
    private bool _isDirty;
    private DateTime _ignoreEditsUntil = DateTime.MinValue;
    private string _statusText = "未打开技能页。";
    private bool _isNoticeOpen;
    private InfoBarSeverity _noticeSeverity = InfoBarSeverity.Informational;
    private string _noticeTitle = "技能未打开";
    private string _noticeMessage = "请选择角色后继续编辑技能。";
    private CharacterSkillsData _data = new();
    private readonly Stack<IReadOnlyList<SkillStageUndoEntry>> _stageUndoStack = new();

    public SkillsViewModel(CharacterSkillsService skillsService)
    {
        _skillsService = skillsService;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        // 订阅的是静态事件，而且从不退订。这在应用里是安全的：本视图模型由
        // ApplicationViewModel 在启动时构造一次，活到进程结束。
        // 但它意味着**每 new 一个实例就多一份永久订阅**——测试里反复构造会让
        // 一次编辑通知到所有历史实例。真要在测试里大量构造，请复用同一个实例。
        CharacterSkillEntry.AnyEntryEdited += (_, _) => NotifyEdited();
        SkillMultiplierLevel.AnyMultiplierEdited += (_, _) => NotifyEdited();
    }

    public event EventHandler? SkillsEdited;

    public string[] SkillStateOptions { get; } = ["", "常态", "禁用", "舍弃"];

    public string[] GuardStateOptions { get; } = ["", "防御", "反击", "闪避"];

    public ObservableCollection<CharacterSkillEntry> FirstSkill => _data.FirstSkill;

    public ObservableCollection<CharacterSkillEntry> SecondSkill => _data.SecondSkill;

    public ObservableCollection<CharacterSkillEntry> UltimateSkill => _data.UltimateSkill;

    public ObservableCollection<CharacterSkillEntry> SupportSkill => _data.SupportSkill;

    public ObservableCollection<CharacterSkillEntry> ComboSkills => _data.ComboSkills;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool CanUndoStageChange => _stageUndoStack.Count > 0;

    public bool IsNoticeOpen
    {
        get => _isNoticeOpen;
        private set => SetProperty(ref _isNoticeOpen, value);
    }

    public InfoBarSeverity NoticeSeverity
    {
        get => _noticeSeverity;
        private set => SetProperty(ref _noticeSeverity, value);
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

    public async Task LoadAsync(CharacterCard? character, CancellationToken cancellationToken = default)
    {
        _isLoading = true;
        try
        {
            if (character is null)
            {
                _data = new CharacterSkillsData();
                RefreshCollectionBindings();
                StatusText = "未选择角色。";
                SetNotice(InfoBarSeverity.Warning, "未选择角色", "请先在角色台选择当前制作角色。");
                return;
            }

            _data = await Task.Run(() => _skillsService.Load(character), cancellationToken);
            RefreshCollectionBindings();
            _stageUndoStack.Clear();
            OnPropertyChanged(nameof(CanUndoStageChange));
            _isDirty = false;
            _ignoreEditsUntil = DateTime.Now.AddMilliseconds(1200);
            StatusText = $"技能已打开：{character.Name}";
        }
        finally
        {
            _isLoading = false;
        }

        RefreshNotice();
    }

    public async Task<bool> SaveAsync(CharacterCard? character, CancellationToken cancellationToken = default)
    {
        if (character is null || !_isDirty)
        {
            return false;
        }

        await Task.Run(() => _skillsService.Save(character, _data), cancellationToken);
        _isDirty = false;
        StatusText = $"技能已保存：{DateTime.Now:HH:mm:ss}";
        return true;
    }

    public bool SaveNow(CharacterCard? character)
    {
        if (character is null || !_isDirty)
        {
            return false;
        }

        _skillsService.Save(character, _data);
        _isDirty = false;
        StatusText = $"技能已保存：{DateTime.Now:HH:mm:ss}";
        return true;
    }

    public void AddStage(ObservableCollection<CharacterSkillEntry> entries)
    {
        if (IsCoreSkillCollection(entries))
        {
            StatusText = "核心技能形态数量由 St3 形态上限控制。";
            return;
        }

        var undoEntries = new List<SkillStageUndoEntry>();
        var entry = CharacterSkillsService.CreateEntry();
        entries.Add(entry);
        undoEntries.Add(new SkillStageUndoEntry(SkillStageUndoKind.Added, entries, entry, entries.Count - 1));

        _stageUndoStack.Push(undoEntries);
        RefreshStageDeleteState(entries);
        OnPropertyChanged(nameof(CanUndoStageChange));
        MarkEdited();
    }

    public void AddComboStage(CharacterCard character)
    {
        var entry = CharacterSkillsService.CreateEntry();
        entry.ComboCharacterCode = character.Code;
        entry.ComboCharacterName = character.EffectiveDisplayName;
        ComboSkills.Add(entry);
        _stageUndoStack.Push(
        [
            new SkillStageUndoEntry(SkillStageUndoKind.Added, ComboSkills, entry, ComboSkills.Count - 1)
        ]);
        RefreshStageDeleteState(ComboSkills);
        OnPropertyChanged(nameof(CanUndoStageChange));
        MarkEdited();
    }

    public bool RemoveStage(CharacterSkillEntry entry)
    {
        var target = FindCollection(entry);
        if (target is null)
        {
            return false;
        }

        var (entries, allowEmpty) = target.Value;
        if (IsCoreSkillCollection(entries))
        {
            StatusText = "核心技能形态数量由 St3 形态上限控制，不能单独删除。";
            return false;
        }

        if (!allowEmpty && entries.Count <= 1)
        {
            StatusText = "这个技能至少要保留一张阶段卡。";
            return false;
        }

        var index = entries.IndexOf(entry);
        if (index < 0)
        {
            return false;
        }

        var undoEntries = new List<SkillStageUndoEntry>();
        entries.RemoveAt(index);
        undoEntries.Add(new SkillStageUndoEntry(SkillStageUndoKind.Removed, entries, entry, index));

        _stageUndoStack.Push(undoEntries);
        RefreshStageDeleteState(entries);
        OnPropertyChanged(nameof(CanUndoStageChange));
        MarkEdited();
        return true;
    }

    public bool UndoLastStageChange()
    {
        if (_stageUndoStack.Count == 0)
        {
            return false;
        }

        var entries = _stageUndoStack.Pop();
        foreach (var entry in entries.Reverse())
        {
            if (entry.Kind == SkillStageUndoKind.Added)
            {
                entry.Entries.Remove(entry.Entry);
            }
            else
            {
                var insertIndex = Math.Clamp(entry.Index, 0, entry.Entries.Count);
                entry.Entries.Insert(insertIndex, entry.Entry);
            }
        }

        RefreshAllStageDeleteStates();
        OnPropertyChanged(nameof(CanUndoStageChange));
        MarkEdited();
        StatusText = "已撤回上一次技能阶段增删。";
        return true;
    }

    public void NotifyEdited()
    {
        if (_dispatcherQueue is not null && !_dispatcherQueue.HasThreadAccess)
        {
            return;
        }

        MarkEdited();
    }

    private void MarkEdited()
    {
        if (_isLoading || DateTime.Now < _ignoreEditsUntil)
        {
            return;
        }

        _isDirty = true;
        StatusText = "技能有修改，等待自动保存...";
        RefreshNotice();
        SkillsEdited?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshCollectionBindings()
    {
        NormalizeVisibleSkillEntries();
        OnPropertyChanged(nameof(FirstSkill));
        OnPropertyChanged(nameof(SecondSkill));
        OnPropertyChanged(nameof(UltimateSkill));
        OnPropertyChanged(nameof(SupportSkill));
        OnPropertyChanged(nameof(ComboSkills));
    }

    private void NormalizeVisibleSkillEntries()
    {
        NormalizeVisibleSkillEntries(FirstSkill);
        NormalizeVisibleSkillEntries(SecondSkill);
        NormalizeVisibleSkillEntries(UltimateSkill);
        NormalizeVisibleSkillEntries(SupportSkill);
        NormalizeVisibleSkillEntries(ComboSkills);
    }

    private static void NormalizeVisibleSkillEntries(ObservableCollection<CharacterSkillEntry> entries)
    {
        foreach (var entry in entries)
        {
            CharacterSkillsService.NormalizeEntryForEditing(entry);
        }
    }

    private (ObservableCollection<CharacterSkillEntry> Entries, bool AllowEmpty)? FindCollection(CharacterSkillEntry entry)
    {
        if (FirstSkill.Contains(entry))
        {
            return (FirstSkill, false);
        }

        if (SecondSkill.Contains(entry))
        {
            return (SecondSkill, false);
        }

        if (UltimateSkill.Contains(entry))
        {
            return (UltimateSkill, false);
        }

        if (SupportSkill.Contains(entry))
        {
            return (SupportSkill, false);
        }

        if (ComboSkills.Contains(entry))
        {
            return (ComboSkills, true);
        }

        return null;
    }

    private bool IsCoreSkillCollection(ObservableCollection<CharacterSkillEntry> entries)
    {
        return ReferenceEquals(entries, FirstSkill) ||
            ReferenceEquals(entries, SecondSkill) ||
            ReferenceEquals(entries, UltimateSkill);
    }

    private void RefreshAllStageDeleteStates()
    {
        RefreshStageDeleteState(FirstSkill);
        RefreshStageDeleteState(SecondSkill);
        RefreshStageDeleteState(UltimateSkill);
        RefreshStageDeleteState(SupportSkill);
        RefreshStageDeleteState(ComboSkills);
    }

    private void RefreshStageDeleteState(ObservableCollection<CharacterSkillEntry> entries)
    {
        var canDelete = !IsCoreSkillCollection(entries) &&
            (!ReferenceEquals(entries, SupportSkill) || entries.Count > 1);
        foreach (var entry in entries)
        {
            entry.CanDelete = canDelete;
        }
    }

    private void RefreshNotice()
    {
        if (_isLoading)
        {
            return;
        }

        NormalizeVisibleSkillEntries();
        var allMissing = BuildMissingSkillMessages().ToList();
        var missing = allMissing.Take(6).ToList();
        var totalMissing = allMissing.Count;
        if (totalMissing == 0)
        {
            SetNotice(InfoBarSeverity.Success, "技能信息已完整", "一技能、二技能、终结技、护援技和连携技都已填写完成。");
            return;
        }

        var suffix = totalMissing > missing.Count ? $" 等 {totalMissing} 项" : string.Empty;
        SetNotice(
            InfoBarSeverity.Warning,
            "技能信息未完整",
            string.Join("；", missing) + suffix);
    }

    private IEnumerable<string> BuildMissingSkillMessages()
    {
        foreach (var message in BuildMissingSkillMessages("一技能", FirstSkill))
        {
            yield return message;
        }

        foreach (var message in BuildMissingSkillMessages("二技能", SecondSkill))
        {
            yield return message;
        }

        foreach (var message in BuildMissingSkillMessages("终结技", UltimateSkill))
        {
            yield return message;
        }

        foreach (var message in BuildMissingSkillMessages("护援技", SupportSkill))
        {
            yield return message;
        }

        foreach (var message in BuildMissingSkillMessages("连携技", ComboSkills, requireComboCharacter: true))
        {
            yield return message;
        }
    }

    private static IEnumerable<string> BuildMissingSkillMessages(
        string skillName,
        ObservableCollection<CharacterSkillEntry> entries,
        bool requireComboCharacter = false)
    {
        for (var index = 0; index < entries.Count; index++)
        {
            var missingFields = GetMissingFields(entries[index], requireComboCharacter);
            if (missingFields.Count == 0)
            {
                continue;
            }

            yield return $"{skillName}形态{index + 1}缺少{string.Join("、", missingFields)}";
        }
    }

    private static List<string> GetMissingFields(CharacterSkillEntry entry, bool requireComboCharacter)
    {
        var missing = new List<string>();
        if (requireComboCharacter && !entry.HasComboCharacter)
        {
            missing.Add("连携角色");
        }

        if (string.IsNullOrWhiteSpace(entry.IconPath))
        {
            missing.Add("图标");
        }

        if (string.IsNullOrWhiteSpace(entry.PositionName))
        {
            missing.Add("定位名称");
        }

        if (string.IsNullOrWhiteSpace(entry.TrueName))
        {
            missing.Add("技能真名");
        }

        if (string.IsNullOrWhiteSpace(entry.PtCost))
        {
            missing.Add("Pt消耗");
        }

        if (string.IsNullOrWhiteSpace(entry.AttackCapacity))
        {
            missing.Add("攻击容量");
        }

        if (string.IsNullOrWhiteSpace(entry.Description))
        {
            missing.Add("介绍");
        }

        if (entry.LevelMultipliers.Any(level =>
                string.IsNullOrWhiteSpace(level.PhysicalMultiplier) &&
                string.IsNullOrWhiteSpace(level.EnergyMultiplier)))
        {
            missing.Add("倍率");
        }

        return missing;
    }

    private void SetNotice(InfoBarSeverity severity, string title, string message)
    {
        NoticeSeverity = severity;
        NoticeTitle = title;
        NoticeMessage = message;
        IsNoticeOpen = true;
    }

    private sealed record SkillStageUndoEntry(
        SkillStageUndoKind Kind,
        ObservableCollection<CharacterSkillEntry> Entries,
        CharacterSkillEntry Entry,
        int Index);

    private enum SkillStageUndoKind
    {
        Added,
        Removed
    }
}
