using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;
using Microsoft.UI.Dispatching;

namespace CrossingVoidZDTool.ViewModels;

internal sealed class BuffsViewModel : ObservableObject
{
    private readonly BuffService _buffService;
    private readonly DispatcherQueue? _dispatcherQueue;
    private BuffData _data = new();
    private bool _isLoading;
    private bool _isDirty;
    private CharacterCard? _currentCharacter;
    private string _statusText = "未打开 BUFF 页。";
    private string _noticeTitle = "未选择角色";
    private string _noticeMessage = "请先在零境角色台选择当前制作角色。";
    private Microsoft.UI.Xaml.Controls.InfoBarSeverity _noticeSeverity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational;
    private bool _isNoticeOpen = true;
    private BuffEntry? _selectedBuff;
    private bool _isEditorOpen;

    public BuffsViewModel(BuffService buffService)
        : this(buffService, DispatcherQueue.GetForCurrentThread())
    {
    }

    internal BuffsViewModel(BuffService buffService, DispatcherQueue? dispatcherQueue)
    {
        _buffService = buffService;
        _dispatcherQueue = dispatcherQueue;
        // 同 SkillsViewModel：订阅静态事件且从不退订。单例使用下安全，
        // 但每 new 一个实例就多一份永久订阅，测试里反复构造会互相串台。
        BuffEntry.AnyBuffEdited += (_, _) => NotifyEdited();
    }

    public event EventHandler? BuffsEdited;

    public ObservableCollection<BuffEntry> Buffs => _data.Buffs;

    public string[] DamageTypeOptions { get; } =
    [
        string.Empty,
        "发送方-属性",
        "发送方-最终",
        "接收方-属性",
        "接收方-最终"
    ];

    public string[] GainTypeOptions { get; } = ["增益", "削弱"];

    public string[] TaskPriorityOptions { get; } = ["低", "正常", "高", "紧急"];

    public string[] EffectTypeOptions { get; } = ["属性修改", "最终伤害", "速度", "技能锁定", "防御", "生命", "自定义效果"];

    public string[] EffectTargetOptions { get; } = ["自身", "自身主战", "自身护援", "敌方主战", "敌方护援", "发送方", "接收方"];

    public string[] EffectOperationOptions { get; } = ["加算", "乘算", "覆盖", "锁定", "蓝图逻辑"];

    public BuffEntry? SelectedBuff
    {
        get => _selectedBuff;
        private set => SetProperty(ref _selectedBuff, value);
    }

    public bool IsEditorOpen
    {
        get => _isEditorOpen;
        private set => SetProperty(ref _isEditorOpen, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
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

    public Microsoft.UI.Xaml.Controls.InfoBarSeverity NoticeSeverity
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
            if (_currentCharacter is not null && _isDirty)
            {
                await SaveCurrentCharacterAsync(cancellationToken);
            }

            if (character is null)
            {
                _currentCharacter = null;
                _data = new BuffData();
                RefreshBindings();
                SetNotice(Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational, "未选择角色", "请先在零境角色台选择当前制作角色。");
                StatusText = "未选择角色。";
                return;
            }

            _currentCharacter = character;
            _data = await Task.Run(() => _buffService.Load(character), cancellationToken);
            RefreshOwnerText(character);
            RefreshBindings();
            _isDirty = false;
            UpdateNotice();
            StatusText = $"BUFF 已打开：{character.Name}";
        }
        finally
        {
            _isLoading = false;
        }
    }

    public async Task<bool> SaveAsync(CharacterCard? character, CancellationToken cancellationToken = default)
    {
        if (!IsCurrentCharacter(character) || !_isDirty)
        {
            return false;
        }

        return await SaveCurrentCharacterAsync(cancellationToken);
    }

    public bool SaveNow(CharacterCard? character)
    {
        if (!IsCurrentCharacter(character) || !_isDirty || _currentCharacter is null)
        {
            return false;
        }

        RefreshOwnerText(_currentCharacter);
        var snapshot = BuffService.Clone(_data);
        _buffService.Save(_currentCharacter, snapshot);
        _isDirty = false;
        UpdateNotice();
        StatusText = $"BUFF 已保存：{DateTime.Now:HH:mm:ss}";
        return true;
    }

    public async Task AddBuffAsync(CharacterCard? character, CancellationToken cancellationToken = default)
    {
        var buff = BuffService.CreateBuff();
        Buffs.Add(buff);
        RefreshOwnerText(character);
        RefreshNaming(character);
        if (character is not null)
        {
            var targetPath = await Task.Run(
                () => _buffService.EnsureDefaultIconFile(character, buff.GeneratedCode, buff.IconPath),
                cancellationToken);
            if (targetPath is not null)
            {
                buff.IconPath = targetPath;
                buff.IconUri = BuffService.CreateIconUri(targetPath);
            }
        }

        OpenEditor(buff);
        MarkEdited();
    }

    public bool RemoveBuff(CharacterCard? character, BuffEntry buff)
    {
        var removed = Buffs.Remove(buff);
        if (removed)
        {
            if (character is not null)
            {
                _buffService.DeleteBuffFolder(character, buff);
            }

            RefreshNaming(character);
            MarkEdited();
        }

        return removed;
    }

    public void OpenEditor(BuffEntry buff)
    {
        SelectedBuff = buff;
        IsEditorOpen = true;
    }

    public void CloseEditor()
    {
        IsEditorOpen = false;
        SelectedBuff = null;
    }

    public void AddEffect(BuffEntry buff)
    {
        buff.Effects.Add(new BuffEffectModule());
    }

    public bool RemoveEffect(BuffEntry buff, BuffEffectModule effect)
    {
        return buff.Effects.Remove(effect);
    }

    public async Task ImportIconAsync(CharacterCard character, BuffEntry buff, string sourcePath, CancellationToken cancellationToken = default)
    {
        RefreshNaming(character);
        var generatedCode = buff.GeneratedCode;
        var targetPath = await Task.Run(() => _buffService.ImportIconFile(character, generatedCode, sourcePath), cancellationToken);
        buff.IconPath = targetPath;
        buff.IconUri = BuffService.CreateIconUri(targetPath);
        MarkEdited();
    }

    public async Task<bool> EnsureDefaultIconAsync(CharacterCard character, BuffEntry buff, CancellationToken cancellationToken = default)
    {
        RefreshNaming(character);
        var generatedCode = buff.GeneratedCode;
        var currentIconPath = buff.IconPath;
        var targetPath = await Task.Run(() => _buffService.EnsureDefaultIconFile(character, generatedCode, currentIconPath), cancellationToken);
        if (targetPath is not null)
        {
            buff.IconPath = targetPath;
            buff.IconUri = BuffService.CreateIconUri(targetPath);
            MarkEdited();
            return true;
        }

        return false;
    }

    public string GetBuffRootPath(CharacterCard character)
    {
        return _buffService.GetBuffRootPath(character);
    }

    public void NotifyEdited()
    {
        if (_dispatcherQueue is not null && !_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(NotifyEdited);
            return;
        }

        RefreshNaming(null);
        MarkEdited();
    }

    private void RefreshNaming(CharacterCard? character)
    {
        character ??= _currentCharacter;
        if (character is not null)
        {
            RefreshOwnerText(character);
            BuffService.RefreshNaming(character, Buffs);
        }
    }

    private void RefreshOwnerText(CharacterCard? character)
    {
        if (character is null)
        {
            return;
        }

        foreach (var buff in Buffs)
        {
            buff.SetOwnerTextFromToolbox(character.EffectiveDisplayName);
        }
    }

    private void MarkEdited()
    {
        if (_isLoading)
        {
            return;
        }

        _isDirty = true;
        UpdateNotice();
        StatusText = "BUFF 有修改，等待自动保存...";
        BuffsEdited?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateNotice()
    {
        var missingIcon = Buffs.Count(buff => string.IsNullOrWhiteSpace(buff.IconPath));
        var missingName = Buffs.Count(buff => string.IsNullOrWhiteSpace(buff.Name));
        var missingOwner = Buffs.Count(buff => string.IsNullOrWhiteSpace(buff.OwnerText));
        var missingDescription = Buffs.Count(buff => string.IsNullOrWhiteSpace(buff.Description));

        if (Buffs.Count == 0)
        {
            SetNotice(Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational, "BUFF 尚未创建", "需要特殊效果时，请先新增 BUFF 卡，或从虚幻同步台读取。");
            return;
        }

        var problems = new[]
            {
                missingIcon > 0 ? $"缺图标 {missingIcon}" : string.Empty,
                missingName > 0 ? $"缺名称 {missingName}" : string.Empty,
                missingOwner > 0 ? $"缺归属 {missingOwner}" : string.Empty,
                missingDescription > 0 ? $"缺说明 {missingDescription}" : string.Empty
            }
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();

        if (problems.Length > 0)
        {
            SetNotice(Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning, "BUFF 信息未完整", string.Join("；", problems));
            return;
        }

        SetNotice(Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success, "BUFF 已就绪", "所有 BUFF 卡的图标、名称、归属、说明、层数和强度都已填写。");
    }

    private void SetNotice(Microsoft.UI.Xaml.Controls.InfoBarSeverity severity, string title, string message)
    {
        NoticeSeverity = severity;
        NoticeTitle = title;
        NoticeMessage = message;
        IsNoticeOpen = true;
    }

    private void RefreshBindings()
    {
        OnPropertyChanged(nameof(Buffs));
    }

    private async Task<bool> SaveCurrentCharacterAsync(CancellationToken cancellationToken)
    {
        if (_currentCharacter is null || !_isDirty)
        {
            return false;
        }

        var owner = _currentCharacter;
        RefreshOwnerText(owner);
        var snapshot = BuffService.Clone(_data);
        await Task.Run(() => _buffService.Save(owner, snapshot), cancellationToken);
        _isDirty = false;
        UpdateNotice();
        StatusText = $"BUFF 已保存：{DateTime.Now:HH:mm:ss}";
        return true;
    }

    private bool IsCurrentCharacter(CharacterCard? character)
    {
        if (character is null || _currentCharacter is null)
        {
            return false;
        }

        return string.Equals(character.Code, _currentCharacter.Code, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(
                   Path.GetFullPath(character.FolderPath),
                   Path.GetFullPath(_currentCharacter.FolderPath),
                   StringComparison.OrdinalIgnoreCase);
    }
}
