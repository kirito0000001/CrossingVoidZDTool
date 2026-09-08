using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;

namespace CrossingVoidZDTool.ViewModels;

internal sealed class CharacterDeskViewModel : ObservableObject
{
    private readonly CharacterWorkspaceService _characterWorkspaceService;
    private string _projectRootPath = AppSettingsService.DefaultProjectRootPath;
    private CharacterCard? _currentCharacter;
    private CharacterCard? _lastEditedCharacter;
    private string _draftText = string.Empty;
    private string _statusText = "就绪：等待创建或选择角色。";
    private string _draftSaveStatusText = "草稿未打开。";
    private bool _isLoadingCharacters;
    private bool _isDraftOpen;
    private bool _isReferencePanelExpanded;
    private bool _isLoadingDraft;
    private bool _isViewOnly;

    public CharacterDeskViewModel(CharacterWorkspaceService characterWorkspaceService)
    {
        _characterWorkspaceService = characterWorkspaceService;
    }

    public ObservableCollection<CharacterCard> Characters { get; } = [];

    public ObservableCollection<CharacterCard> CompletedCharacters { get; } = [];

    public ObservableCollection<CharacterCard> DraftCharacters { get; } = [];

    public ObservableCollection<CharacterReferenceImage> ReferenceImages { get; } = [];

    public event EventHandler? DraftTextEdited;

    public CharacterCard? CurrentCharacter
    {
        get => _currentCharacter;
        private set
        {
            if (SetProperty(ref _currentCharacter, value))
            {
                OnPropertyChanged(nameof(CurrentCharacterName));
                OnPropertyChanged(nameof(CurrentCharacterCode));
                OnPropertyChanged(nameof(CurrentCharacterStatusText));
                OnPropertyChanged(nameof(HasCurrentCharacter));
                OnPropertyChanged(nameof(CanOpenCurrentDraft));
                OnPropertyChanged(nameof(IsCurrentCharacterCompleted));
                OnPropertyChanged(nameof(CanEditCurrentCharacter));
            }
        }
    }

    public CharacterCard? LastEditedCharacter
    {
        get => _lastEditedCharacter;
        private set
        {
            if (SetProperty(ref _lastEditedCharacter, value))
            {
                OnPropertyChanged(nameof(LastEditedCharacterText));
                OnPropertyChanged(nameof(HasLastEditedCharacter));
            }
        }
    }

    public string CurrentCharacterName => CurrentCharacter?.EffectiveDisplayName ?? "未选择角色";

    public string CurrentCharacterCode => CurrentCharacter?.Code ?? "--";

    public string CurrentCharacterStatusText => CurrentCharacter is null
        ? "当前制作角色：未选择"
        : IsViewOnly
            ? $"当前查看角色：{CurrentCharacter.StatusDisplayText}（只读）"
            : $"当前制作角色：{CurrentCharacter.StatusDisplayText}";

    public bool HasCurrentCharacter => CurrentCharacter is not null;

    public bool CanOpenCurrentDraft => CurrentCharacter is not null && !CurrentCharacter.IsCompleted;

    public bool IsCurrentCharacterCompleted => CurrentCharacter?.IsCompleted == true;

    public bool IsViewOnly
    {
        get => _isViewOnly;
        private set
        {
            if (SetProperty(ref _isViewOnly, value))
            {
                OnPropertyChanged(nameof(CanEditCurrentCharacter));
                OnPropertyChanged(nameof(CurrentCharacterStatusText));
            }
        }
    }

    public bool CanEditCurrentCharacter => HasCurrentCharacter && !IsViewOnly;

    public bool HasLastEditedCharacter => LastEditedCharacter is not null;

    public string LastEditedCharacterText => LastEditedCharacter is null
        ? "暂无上次编辑角色"
        : $"继续：{LastEditedCharacter.EffectiveDisplayName}";

    public string DraftText
    {
        get => _draftText;
        set
        {
            if (SetProperty(ref _draftText, value) && !_isLoadingDraft)
            {
                DraftSaveStatusText = "草稿有修改，等待自动保存...";
                DraftTextEdited?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string DraftSaveStatusText
    {
        get => _draftSaveStatusText;
        private set => SetProperty(ref _draftSaveStatusText, value);
    }

    public bool IsLoadingCharacters
    {
        get => _isLoadingCharacters;
        private set => SetProperty(ref _isLoadingCharacters, value);
    }

    public bool IsDraftOpen
    {
        get => _isDraftOpen;
        private set => SetProperty(ref _isDraftOpen, value);
    }

    public bool IsReferencePanelExpanded
    {
        get => _isReferencePanelExpanded;
        set => SetProperty(ref _isReferencePanelExpanded, value);
    }

    public async Task LoadCharactersAsync(
        string projectRootPath,
        string? currentCharacterCode,
        string? lastEditedCharacterCode,
        CancellationToken cancellationToken = default)
    {
        _projectRootPath = projectRootPath;
        IsLoadingCharacters = true;
        StatusText = "正在加载角色卡...";
        try
        {
            var cards = await Task.Run(
                () => _characterWorkspaceService.LoadCharacters(projectRootPath, cancellationToken),
                cancellationToken);
            Characters.Clear();
            foreach (var card in cards)
            {
                Characters.Add(card);
            }
            RefreshCharacterBuckets();

            LastEditedCharacter = FindCharacter(lastEditedCharacterCode) ?? Characters.FirstOrDefault();
            var current = FindCharacter(currentCharacterCode) ?? LastEditedCharacter;
            if (current is not null)
            {
                await SetCurrentCharacterAsync(current, cancellationToken);
            }
            else
            {
                CurrentCharacter = null;
                IsDraftOpen = false;
                SetDraftTextSilently(string.Empty);
                ReferenceImages.Clear();
                StatusText = Characters.Count == 0
                    ? "就绪：还没有角色卡。"
                    : $"就绪：已加载 {Characters.Count} 张角色卡。";
                DraftSaveStatusText = "草稿未打开。";
            }
        }
        finally
        {
            IsLoadingCharacters = false;
        }
    }

    public async Task<CharacterCard> CreateCharacterAsync(string characterName, CancellationToken cancellationToken = default)
    {
        var result = await Task.Run(
            () => _characterWorkspaceService.CreateCharacter(_projectRootPath, characterName),
            cancellationToken);
        Characters.Insert(0, result.Character);
        RefreshCharacterBuckets();
        await SetCurrentCharacterAsync(result.Character, cancellationToken);
        return result.Character;
    }

    public async Task<(CharacterCard Character, bool CreatedNew)> EnsureCharacterByCodeAsync(
        string code,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        var result = await Task.Run(
            () => _characterWorkspaceService.EnsureCharacterByCode(_projectRootPath, code, displayName),
            cancellationToken);
        var existing = Characters.FirstOrDefault(character => string.Equals(character.Code, result.Character.Code, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            Characters.Insert(0, result.Character);
        }
        else
        {
            var index = Characters.IndexOf(existing);
            Characters[index] = result.Character;
        }

        RefreshCharacterBuckets();
        await SetCurrentCharacterAsync(result.Character, cancellationToken);
        return (result.Character, result.CreatedNewFolder);
    }

    public async Task SelectCharacterAsync(CharacterCard character, CancellationToken cancellationToken = default)
    {
        await SetCurrentCharacterAsync(character, cancellationToken);
        await OpenCurrentCharacterDraftAsync(cancellationToken);
    }

    public async Task SetCurrentCharacterAsync(CharacterCard character, CancellationToken cancellationToken = default)
    {
        IsViewOnly = false;
        var ensuredCharacter = await Task.Run(
            () => _characterWorkspaceService.EnsureCharacterStructure(character),
            cancellationToken);
        CurrentCharacter = ensuredCharacter;
        LastEditedCharacter = ensuredCharacter;
        IsDraftOpen = false;
        SetDraftTextSilently(string.Empty);
        ReferenceImages.Clear();
        DraftSaveStatusText = "已选择角色，进入 St1 后点击立绘卡打开草稿。";
        StatusText = CurrentCharacterStatusText;
    }

    public async Task OpenCompletedCharacterViewAsync(
        CharacterCard character,
        CancellationToken cancellationToken = default)
    {
        if (!character.IsCompleted)
        {
            throw new InvalidOperationException("只有已完成角色可以使用只读查看。");
        }

        IsViewOnly = true;
        CurrentCharacter = character;
        LastEditedCharacter = character;
        ReferenceImages.Clear();
        _isLoadingDraft = true;
        try
        {
            DraftText = await Task.Run(() => _characterWorkspaceService.LoadDraft(character), cancellationToken);
        }
        finally
        {
            _isLoadingDraft = false;
        }

        IsDraftOpen = true;
        DraftSaveStatusText = "只读查看模式。";
        StatusText = CurrentCharacterStatusText;
        await RefreshReferenceImagesAsync(cancellationToken);
    }

    public void SetViewOnly(bool value) => IsViewOnly = value;

    public async Task RefreshCharacterCardAsync(CharacterCard character, CancellationToken cancellationToken = default)
    {
        var refreshedCharacter = await Task.Run(
            () => _characterWorkspaceService.RefreshCharacterCard(character),
            cancellationToken);
        ReplaceCharacter(refreshedCharacter, character.Code);
    }

    public async Task SynchronizeCurrentCharacterDisplayNameAsync(string displayName, CancellationToken cancellationToken = default)
    {
        if (CurrentCharacter is null || IsViewOnly)
        {
            return;
        }

        var previousCode = CurrentCharacter.Code;
        var refreshedCharacter = await Task.Run(
            () => _characterWorkspaceService.SynchronizeCharacterDisplayName(CurrentCharacter, displayName),
            cancellationToken);
        ReplaceCharacter(refreshedCharacter, previousCode);
    }

    /// <summary>
    /// 同步版，只给关窗这类「已经在阻塞等待」的收尾路径用。
    ///
    /// 上面那个 async 版在关窗时会死锁：调用方 .GetAwaiter().GetResult() 占着 UI 线程，
    /// 而 await Task.Run(...) 的续体要 post 回同一个 DispatcherQueue，
    /// 于是续体永远排不上、程序卡死。底层本来就是同步方法，直接调即可。
    /// </summary>
    public void SynchronizeCurrentCharacterDisplayName(string displayName)
    {
        if (CurrentCharacter is null || IsViewOnly)
        {
            return;
        }

        var previousCode = CurrentCharacter.Code;
        var refreshedCharacter = _characterWorkspaceService.SynchronizeCharacterDisplayName(CurrentCharacter, displayName);
        ReplaceCharacter(refreshedCharacter, previousCode);
    }

    public async Task OpenCurrentCharacterDraftAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentCharacter is null || IsViewOnly)
        {
            return;
        }

        var ensuredCharacter = await Task.Run(
            () => _characterWorkspaceService.EnsureCharacterStructure(CurrentCharacter),
            cancellationToken);
        CurrentCharacter = ensuredCharacter;
        LastEditedCharacter = ensuredCharacter;
        _isLoadingDraft = true;
        try
        {
            DraftText = await Task.Run(() => _characterWorkspaceService.LoadDraft(ensuredCharacter), cancellationToken);
        }
        finally
        {
            _isLoadingDraft = false;
        }

        IsDraftOpen = true;
        DraftSaveStatusText = "草稿已打开，修改会自动保存。";
        StatusText = CurrentCharacterStatusText;
        await RefreshReferenceImagesAsync(cancellationToken);
    }

    public async Task<string> LoadCurrentCharacterDraftTextAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentCharacter is null)
        {
            return string.Empty;
        }

        var ensuredCharacter = await Task.Run(
            () => _characterWorkspaceService.EnsureCharacterStructure(CurrentCharacter),
            cancellationToken);
        CurrentCharacter = ensuredCharacter;
        LastEditedCharacter = ensuredCharacter;
        return await Task.Run(() => _characterWorkspaceService.LoadDraft(ensuredCharacter), cancellationToken);
    }

    public void CloseDraftView()
    {
        IsDraftOpen = false;
        DraftSaveStatusText = HasCurrentCharacter
            ? "已回到立绘入口。"
            : "草稿未打开。";
    }

    public Task SelectLastEditedCharacterAsync(CancellationToken cancellationToken = default)
    {
        return LastEditedCharacter is null
            ? Task.CompletedTask
            : SelectCharacterAsync(LastEditedCharacter, cancellationToken);
    }

    public async Task SaveDraftNowAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentCharacter is null || IsViewOnly)
        {
            return;
        }

        var character = CurrentCharacter;
        var text = DraftText;
        await Task.Run(() => _characterWorkspaceService.SaveDraft(character, text), cancellationToken);
        DraftSaveStatusText = $"已保存：{DateTime.Now:HH:mm:ss}";
        StatusText = CurrentCharacterStatusText;
    }

    /// <summary>
    /// 同步版，给关窗收尾用。草稿是 900 毫秒防抖保存的，
    /// 关窗时不 flush 就会丢掉最后这段输入；而在关窗路径上等 async
    /// 会和 UI 线程互锁，所以直接同步写。
    /// </summary>
    public void SaveDraftNow()
    {
        if (CurrentCharacter is null || IsViewOnly)
        {
            return;
        }

        _characterWorkspaceService.SaveDraft(CurrentCharacter, DraftText);
        DraftSaveStatusText = $"已保存：{DateTime.Now:HH:mm:ss}";
        StatusText = CurrentCharacterStatusText;
    }

    public async Task ImportReferenceImagesAsync(IReadOnlyList<string> sourceFilePaths, CancellationToken cancellationToken = default)
    {
        if (CurrentCharacter is null || IsViewOnly || sourceFilePaths.Count == 0)
        {
            return;
        }

        var character = CurrentCharacter;
        var images = await Task.Run(
            () => _characterWorkspaceService.ImportReferenceImages(character, sourceFilePaths),
            cancellationToken);
        ReplaceReferenceImages(images);
        IsReferencePanelExpanded = true;
        DraftSaveStatusText = $"已导入参考图：{sourceFilePaths.Count} 个文件。";
    }

    public async Task RefreshReferenceImagesAsync(CancellationToken cancellationToken = default)
    {
        ReferenceImages.Clear();
        if (CurrentCharacter is null)
        {
            return;
        }

        var character = CurrentCharacter;
        var images = await Task.Run(() => _characterWorkspaceService.LoadReferenceImages(character, cancellationToken), cancellationToken);
        ReplaceReferenceImages(images);
    }

    public async Task RenameReferenceImageAsync(CharacterReferenceImage image, string newFileName, CancellationToken cancellationToken = default)
    {
        if (CurrentCharacter is null || IsViewOnly)
        {
            return;
        }

        var character = CurrentCharacter;
        var images = await Task.Run(() => _characterWorkspaceService.RenameReferenceImage(character, image.FilePath, newFileName), cancellationToken);
        ReplaceReferenceImages(images);
        DraftSaveStatusText = "参考图已重命名。";
    }

    public async Task DeleteReferenceImageAsync(CharacterReferenceImage image, CancellationToken cancellationToken = default)
    {
        if (CurrentCharacter is null || IsViewOnly)
        {
            return;
        }

        var character = CurrentCharacter;
        var images = await Task.Run(() => _characterWorkspaceService.DeleteReferenceImage(character, image.FilePath), cancellationToken);
        ReplaceReferenceImages(images);
        DraftSaveStatusText = "参考图已删除。";
    }

    public Task<CharacterBackupEntry> BackupCharacterAsync(
        CharacterCard character,
        string note,
        IProgress<CharacterBackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => _characterWorkspaceService.BackupCharacter(character, note, progress, cancellationToken), cancellationToken);
    }

    public string GetDefaultExportRootPath(string projectRootPath)
    {
        return _characterWorkspaceService.GetDefaultExportRootPath(projectRootPath);
    }

    public Task<string> ExportCharacterFolderAsync(
        CharacterCard character,
        string exportRootPath,
        bool overwrite,
        IProgress<CharacterBackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => _characterWorkspaceService.ExportCharacterFolder(
                character,
                exportRootPath,
                overwrite,
                progress,
                cancellationToken),
            cancellationToken);
    }

    public Task<IReadOnlyList<CharacterBackupEntry>> LoadCharacterBackupsAsync(
        CharacterCard character,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => _characterWorkspaceService.LoadCharacterBackups(character), cancellationToken);
    }

    public async Task<CharacterCard> RestoreCharacterBackupAsync(
        CharacterCard character,
        CharacterBackupEntry backup,
        IProgress<CharacterBackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var restoredCharacter = await Task.Run(
            () => _characterWorkspaceService.RestoreCharacterBackup(character, backup, progress, cancellationToken),
            cancellationToken);
        ReplaceCharacter(restoredCharacter, character.Code);
        return restoredCharacter;
    }

    public async Task DeleteCharacterAsync(CharacterCard character, CancellationToken cancellationToken = default)
    {
        await Task.Run(() => _characterWorkspaceService.DeleteCharacter(character), cancellationToken);
        RemoveCharacter(character);

        if (CurrentCharacter is not null &&
            string.Equals(CurrentCharacter.Code, character.Code, StringComparison.OrdinalIgnoreCase))
        {
            CurrentCharacter = null;
            IsDraftOpen = false;
            SetDraftTextSilently(string.Empty);
            ReferenceImages.Clear();
            DraftSaveStatusText = "草稿未打开。";
        }

        if (LastEditedCharacter is not null &&
            string.Equals(LastEditedCharacter.Code, character.Code, StringComparison.OrdinalIgnoreCase))
        {
            LastEditedCharacter = Characters.FirstOrDefault();
        }

        StatusText = Characters.Count == 0
            ? "就绪：还没有角色卡。"
            : $"就绪：已加载 {Characters.Count} 张角色卡。";
    }

    public async Task<CharacterCard> ReopenCompletedCharacterAsync(
        CharacterCard character,
        CancellationToken cancellationToken = default)
    {
        if (!character.IsCompleted)
        {
            return character;
        }

        var reopened = await Task.Run(
            () => _characterWorkspaceService.SetCompleted(character, isCompleted: false),
            cancellationToken);
        ReplaceCharacter(reopened, character.Code);
        return reopened;
    }

    private void ReplaceReferenceImages(IReadOnlyList<CharacterReferenceImage> images)
    {
        ReferenceImages.Clear();
        foreach (var image in images)
        {
            ReferenceImages.Add(image);
        }
    }

    private CharacterCard? FindCharacter(string? code)
    {
        return string.IsNullOrWhiteSpace(code)
            ? null
            : Characters.FirstOrDefault(character => string.Equals(character.Code, code, StringComparison.OrdinalIgnoreCase));
    }

    private void RefreshCharacterBuckets()
    {
        CompletedCharacters.Clear();
        foreach (var character in Characters.Where(character => character.IsCompleted))
        {
            CompletedCharacters.Add(character);
        }

        DraftCharacters.Clear();
        foreach (var character in Characters.Where(character => !character.IsCompleted))
        {
            DraftCharacters.Add(character);
        }
    }

    private void RemoveCharacter(CharacterCard character)
    {
        var old = Characters.FirstOrDefault(card => string.Equals(card.Code, character.Code, StringComparison.OrdinalIgnoreCase));
        if (old is not null)
        {
            Characters.Remove(old);
        }

        var completed = CompletedCharacters.FirstOrDefault(card => string.Equals(card.Code, character.Code, StringComparison.OrdinalIgnoreCase));
        if (completed is not null)
        {
            CompletedCharacters.Remove(completed);
        }

        var draft = DraftCharacters.FirstOrDefault(card => string.Equals(card.Code, character.Code, StringComparison.OrdinalIgnoreCase));
        if (draft is not null)
        {
            DraftCharacters.Remove(draft);
        }
    }

    public void ReplaceCharacter(CharacterCard character)
    {
        ReplaceCharacter(character, character.Code);
    }

    public void ReplaceCharacter(CharacterCard character, string previousCode)
    {
        var isReplacingCurrentCharacter = CurrentCharacter is not null &&
            string.Equals(CurrentCharacter.Code, previousCode, StringComparison.OrdinalIgnoreCase);
        var old = Characters.FirstOrDefault(card => string.Equals(card.Code, previousCode, StringComparison.OrdinalIgnoreCase));
        var index = old is null ? -1 : Characters.IndexOf(old);
        if (index >= 0)
        {
            Characters[index] = character;
        }
        RefreshCharacterBuckets();

        CurrentCharacter = character;
        LastEditedCharacter = character;
        if (character.IsCompleted)
        {
            IsDraftOpen = false;
            DraftSaveStatusText = "角色已完成。后续请从对应步骤页面继续编辑。";
        }
        else if (!isReplacingCurrentCharacter)
        {
            IsDraftOpen = false;
            SetDraftTextSilently(string.Empty);
            DraftSaveStatusText = "已选择角色，进入 St1 后点击立绘卡打开草稿。";
        }

        StatusText = CurrentCharacterStatusText;
    }

    private void SetDraftTextSilently(string text)
    {
        var wasLoadingDraft = _isLoadingDraft;
        _isLoadingDraft = true;
        try
        {
            DraftText = text;
        }
        finally
        {
            _isLoadingDraft = wasLoadingDraft;
        }
    }
}
