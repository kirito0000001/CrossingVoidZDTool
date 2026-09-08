using System.Collections.Generic;
using System.Collections.ObjectModel;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;
using Microsoft.UI.Xaml.Controls;

namespace CrossingVoidZDTool.ViewModels;

internal sealed class SequenceFramesViewModel : ObservableObject
{
    private static readonly (string ActionCode, VoiceMaterialKind Kind)[] VoiceKindMappings =
    [
        ("Click", VoiceMaterialKind.Click),
        ("Death", VoiceMaterialKind.Death),
        ("Defeat", VoiceMaterialKind.Defeat),
        // 工具箱侧的动作码是 OnDamage；写成 Ondm 时 IsActionCodeInFamily 匹配不上
        // （它只接受完全相等或「基码+纯数字」），受伤动作的语音下拉就完全不过滤了。
        ("OnDamage", VoiceMaterialKind.Hurt),
        ("Victory", VoiceMaterialKind.Victory),
        ("Sk1", VoiceMaterialKind.Skill1),
        ("Sk2", VoiceMaterialKind.Skill2),
        ("Ko", VoiceMaterialKind.Ultimate),
        ("Sub", VoiceMaterialKind.Support),
        ("Link", VoiceMaterialKind.Combo)
    ];

    private readonly SequenceFrameService _sequenceFrameService;
    private readonly CharacterSkillsService _skillsService;
    private readonly CharacterFormService _formService = new();
    private readonly VoiceMaterialService _voiceMaterialService = new();
    private readonly SequenceVoiceSyncAnalyzer _voiceSyncAnalyzer = new();
    private string _statusText = "未打开序列帧页面。";
    private string _previewTitle = "未选择动作";
    private string _currentFrameUri = string.Empty;
    private string _currentFrameFilePath = string.Empty;
    private string _currentFrameCacheKey = string.Empty;
    private string _currentFrameText = "暂无预览帧";
    private string _collectionSummaryText = "未打开帧合集。";
    private string _noticeTitle = "未打开序列帧页面";
    private string _noticeMessage = "请先选择角色。";
    private InfoBarSeverity _noticeSeverity = InfoBarSeverity.Informational;
    private bool _isNoticeOpen;
    private string _currentSequenceSignature = string.Empty;
    private int _previewFps = 12;
    private bool _isApplyingPreviewFps;
    private bool _isPreviewing;
    private bool _isPreloadingPreview;
    private bool _pauseEditorPreviewWhenVoiceEnds;
    private SequenceEditorPlaybackMode _editorPlaybackMode = SequenceEditorPlaybackMode.Loop;
    private bool _isReadOnly;
    private SequenceFrameSection? _previewSection;
    private SequenceFrameSection? _selectedSection;
    private SequenceFrameItem? _selectedEditorFrame;
    private double _editorFrameDurationInput = double.NaN;
    private SequenceFramesData _data = new();
    private int _previewIndex;
    private CharacterCard? _currentCharacter;

    public SequenceFramesViewModel(SequenceFrameService sequenceFrameService, CharacterSkillsService skillsService)
    {
        _sequenceFrameService = sequenceFrameService;
        _skillsService = skillsService;
    }

    public ObservableCollection<SequenceFrameSectionGroup> BaseSectionGroups { get; } = [];

    public ObservableCollection<SequenceFrameSectionGroup> SkillSectionGroups { get; } = [];

    public ObservableCollection<SequenceFrameSection> ComboSections { get; } = [];

    public ObservableCollection<SequenceFrameItem> PreviewFrames { get; } = [];

    public ObservableCollection<SequenceFrameItem> SelectedSectionFrames { get; } = [];

    public ObservableCollection<SequenceFrameCollectionItem> CollectionItems { get; } = [];

    public ObservableCollection<SequenceFrameVoiceOption> AvailableVoices { get; } = [];

    public event EventHandler? SequenceFramesSaved;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string PreviewTitle
    {
        get => _previewTitle;
        private set => SetProperty(ref _previewTitle, value);
    }

    public string CurrentFrameUri
    {
        get => _currentFrameUri;
        private set => SetProperty(ref _currentFrameUri, value);
    }

    public string CurrentFrameFilePath
    {
        get => _currentFrameFilePath;
        private set => SetProperty(ref _currentFrameFilePath, value);
    }

    public string CurrentFrameCacheKey
    {
        get => _currentFrameCacheKey;
        private set => SetProperty(ref _currentFrameCacheKey, value);
    }

    public string CurrentFrameText
    {
        get => _currentFrameText;
        private set => SetProperty(ref _currentFrameText, value);
    }

    public string CollectionSummaryText
    {
        get => _collectionSummaryText;
        private set => SetProperty(ref _collectionSummaryText, value);
    }

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

    public int PreviewFps
    {
        get => _previewFps;
        set
        {
            var normalized = value < 1 ? 1 : value > 60 ? 60 : value;
            var changed = SetProperty(ref _previewFps, normalized);
            if (changed && !_isApplyingPreviewFps)
            {
                SaveSelectedSectionFps(normalized);
                RefreshVoiceSyncAnalysis();
            }

            OnPropertyChanged(nameof(EditorFrameDurationText));
            OnPropertyChanged(nameof(EditorSequenceSummary));
        }
    }

    public bool IsPreviewing
    {
        get => _isPreviewing;
        set
        {
            if (SetProperty(ref _isPreviewing, value))
            {
                OnPropertyChanged(nameof(PlayPauseGlyph));
                OnPropertyChanged(nameof(PlayPauseToolTip));
            }
        }
    }

    public bool IsPreloadingPreview
    {
        get => _isPreloadingPreview;
        set => SetProperty(ref _isPreloadingPreview, value);
    }

    public bool PauseEditorPreviewWhenVoiceEnds
    {
        get => _pauseEditorPreviewWhenVoiceEnds;
        set => SetProperty(ref _pauseEditorPreviewWhenVoiceEnds, value);
    }

    public string PlayPauseGlyph => IsPreviewing ? "\uE769" : "\uE768";

    public string PlayPauseToolTip => IsPreviewing ? "暂停播放" : "播放当前序列";

    public SequenceEditorPlaybackMode EditorPlaybackMode
    {
        get => _editorPlaybackMode;
        set
        {
            if (SetProperty(ref _editorPlaybackMode, value))
            {
                OnPropertyChanged(nameof(IsEditorPlaybackLoop));
            }
        }
    }

    public bool IsEditorPlaybackLoop
    {
        get => EditorPlaybackMode == SequenceEditorPlaybackMode.Loop;
        set
        {
            EditorPlaybackMode = value
                ? SequenceEditorPlaybackMode.Loop
                : SequenceEditorPlaybackMode.Once;
        }
    }

    public SequenceFrameSection? SelectedSection
    {
        get => _selectedSection;
        private set
        {
            if (SetProperty(ref _selectedSection, value))
            {
                OnPropertyChanged(nameof(SelectedSectionTitle));
                OnPropertyChanged(nameof(HasSelectedSection));
            }
        }
    }

    public string SelectedSectionTitle => SelectedSection is null
        ? "未选择序列"
        : $"{SelectedSection.Action.DisplayName} / {SelectedSection.Action.Code}";

    public bool HasSelectedSection => SelectedSection is not null;

    public SequenceFrameSection? PreviewSection => _previewSection;

    public SequenceFrameItem? SelectedEditorFrame
    {
        get => _selectedEditorFrame;
        set
        {
            if (!SetProperty(ref _selectedEditorFrame, value))
            {
                EditorFrameDurationInput = value?.DurationFrames ?? double.NaN;
                return;
            }

            EditorFrameDurationInput = value?.DurationFrames ?? double.NaN;
            OnPropertyChanged(nameof(HasSelectedEditorFrame));
            OnPropertyChanged(nameof(EditorFrameDurationText));
            OnPropertyChanged(nameof(EditorSelectedFrameText));
            OnPropertyChanged(nameof(SelectedEditorVoicePath));
            OnPropertyChanged(nameof(SelectedEditorVoiceOption));
            if (value is not null)
            {
                SelectFrame(value);
            }
        }
    }

    public bool HasSelectedEditorFrame => SelectedEditorFrame is not null;

    public string SelectedEditorVoicePath => SelectedEditorFrame?.VoiceFilePath ?? string.Empty;

    public SequenceFrameVoiceOption? SelectedEditorVoiceOption => AvailableVoices.FirstOrDefault(option =>
        string.Equals(option.FilePath, SelectedEditorVoicePath, StringComparison.OrdinalIgnoreCase));

    public double EditorFrameDurationInput
    {
        get => _editorFrameDurationInput;
        set => SetProperty(ref _editorFrameDurationInput, value);
    }

    public string EditorFrameDurationText => SelectedEditorFrame is null
        ? "未选择帧"
        : $"{SelectedEditorFrame.DurationFrames} 格 / {SelectedEditorFrame.DurationFrames / (double)Math.Max(1, PreviewFps):0.###} 秒";

    public string EditorSelectedFrameText => SelectedEditorFrame is null
        ? "未选择帧"
        : $"第 {SelectedEditorFrame.Index} 帧 · {SelectedEditorFrame.PlainFileName}";

    public string EditorSequenceSummary
    {
        get
        {
            var totalFrames = SelectedSectionFrames.Sum(frame => frame.DurationFrames);
            return $"{SelectedSectionFrames.Count} 个素材帧 · {totalFrames} 格 · {totalFrames / (double)Math.Max(1, PreviewFps):0.###} 秒";
        }
    }

    public string EditorPlaybackPositionText => CurrentPreviewFrame is null
        ? "0 / 0"
        : $"{CurrentPreviewFrame.Index} / {PreviewFrames.Count}";

    public SequenceFrameItem? CurrentPreviewFrame => PreviewFrames.Count == 0 || _previewIndex < 0 || _previewIndex >= PreviewFrames.Count
        ? null
        : PreviewFrames[_previewIndex];

    /// <summary>
    /// 查看模式。已完成角色是「看」不是「改」，但看序列本身必须允许——
    /// 以前这层限制是在 XAML 上给整块左栏设 IsHitTestVisible=false 实现的，
    /// 而把序列送进右侧预览器的那个播放按钮也在这块里面，
    /// 于是只读模式下根本没有任何可达路径能看序列。
    /// 现在界面保持可交互，改数据的入口由下面这些守卫挡住。
    /// </summary>
    public bool IsReadOnly
    {
        get => _isReadOnly;
        set => SetProperty(ref _isReadOnly, value);
    }

    /// <summary>拒绝写操作时用：调用方据此提示用户，而不是静默什么都不做。</summary>
    public bool RejectWhenReadOnly()
    {
        if (!IsReadOnly)
        {
            return false;
        }

        StatusText = "查看模式下不能修改序列帧，请先从角色台点「继续制作」。";
        return true;
    }

    public async Task LoadAsync(CharacterCard? character, CancellationToken cancellationToken = default)
    {
        BaseSectionGroups.Clear();
        SkillSectionGroups.Clear();
        ComboSections.Clear();
        ClearPreview();
        ClearSelectedSection();
        _currentCharacter = character;
        if (character is null)
        {
            _data = new SequenceFramesData();
            StatusText = "未选择角色。";
            _currentSequenceSignature = string.Empty;
            SetNotice(InfoBarSeverity.Warning, "未选择角色", "请先在角色台选择当前制作角色。");
            return;
        }

        var sections = await Task.Run(
            () =>
            {
                _data = _sequenceFrameService.LoadData(character);
                var skills = _skillsService.Load(character);
                return _sequenceFrameService.LoadSections(character, skills, cancellationToken);
            },
            cancellationToken);
        var baseSections = sections.Where(section => !section.Action.IsSkill && !section.Action.IsCombo).ToList();
        foreach (var group in BuildSectionGroups(
                     baseSections,
                     "基础板块",
                     "目标尺寸 928x640。拖入或选择图片后会自动重命名为 角色英文代号-动作-序号.png。"))
        {
            BaseSectionGroups.Add(group);
            await Task.Yield();
        }

        var skillSections = sections.Where(section => section.Action.IsSkill && !section.Action.IsCombo).ToList();
        foreach (var group in BuildSectionGroups(
                     skillSections,
                     "技能板块",
                     "一技能、二技能、终结技、护援技会跟随 St3 形态上限生成。"))
        {
            SkillSectionGroups.Add(group);
            await Task.Yield();
        }

        foreach (var section in sections.Where(section => section.Action.IsCombo))
        {
            ComboSections.Add(section);
            await Task.Yield();
        }

        var allSections = EnumerateAllSections().ToList();
        var total = allSections.Count;
        var missingSections = allSections.Where(section => section.Frames.Count == 0).ToList();
        var invalidSections = allSections.Where(section => section.Frames.Any(frame => !frame.IsValid)).ToList();
        var warningCount = allSections.Count(section => section.HasWarning);
        _currentSequenceSignature = BuildSequenceSignature(allSections);
        StatusText = warningCount > 0
            ? $"已检查 {total} 个动作，{warningCount} 个动作缺少帧或尺寸不合规。"
            : $"已检查 {total} 个动作，序列帧尺寸均合规。";
        UpdateNotice(allSections, missingSections, invalidSections);
    }

    public async Task ImportAsync(CharacterCard character, SequenceFrameSection section, IReadOnlyList<string> sourceFilePaths, CancellationToken cancellationToken = default)
    {
        if (RejectWhenReadOnly())
        {
            return;
        }

        await Task.Run(() => _sequenceFrameService.ImportFrames(character, section.Action, sourceFilePaths), cancellationToken);
        await LoadAsync(character, cancellationToken);
    }

    public async Task DeleteFrameAsync(CharacterCard character, SequenceFrameSection section, SequenceFrameItem frame, CancellationToken cancellationToken = default)
    {
        if (RejectWhenReadOnly())
        {
            return;
        }

        var frames = await Task.Run(() => _sequenceFrameService.DeleteFrame(character, section.Action, frame), cancellationToken);
        ApplyFrameMutation(section, frames, frame.Index);
    }

    public async Task DeleteFramesAsync(
        CharacterCard character,
        SequenceFrameSection section,
        IReadOnlyList<SequenceFrameItem> selectedFrames,
        CancellationToken cancellationToken = default)
    {
        if (RejectWhenReadOnly())
        {
            return;
        }

        var selectedIndex = selectedFrames.Count == 0 ? 1 : selectedFrames.Min(frame => frame.Index);
        var frames = await Task.Run(
            () => _sequenceFrameService.DeleteFrames(character, section.Action, selectedFrames),
            cancellationToken);
        ApplyFrameMutation(section, frames, selectedIndex);
    }

    public async Task DuplicateFrameAsync(CharacterCard character, SequenceFrameSection section, SequenceFrameItem frame, CancellationToken cancellationToken = default)
    {
        var frames = await Task.Run(() => _sequenceFrameService.DuplicateFrame(character, section.Action, frame), cancellationToken);
        ApplyFrameMutation(section, frames, frame.Index + 1);
    }

    public async Task DuplicateFramesAsync(
        CharacterCard character,
        SequenceFrameSection section,
        IReadOnlyList<SequenceFrameItem> selectedFrames,
        SequenceFrameItem afterFrame,
        CancellationToken cancellationToken = default)
    {
        var frames = await Task.Run(
            () => _sequenceFrameService.DuplicateFrames(character, section.Action, selectedFrames, afterFrame),
            cancellationToken);
        ApplyFrameMutation(section, frames, afterFrame.Index + 1);
    }

    public async Task InsertBlankFrameAsync(
        CharacterCard character,
        SequenceFrameSection section,
        SequenceFrameItem? anchorFrame,
        SequenceFrameInsertPosition position,
        CancellationToken cancellationToken = default)
    {
        if (RejectWhenReadOnly())
        {
            return;
        }

        var frames = await Task.Run(
            () => _sequenceFrameService.InsertBlankFrame(character, section.Action, anchorFrame, position),
            cancellationToken);
        var selectedIndex = anchorFrame is null
            ? frames.Count
            : position == SequenceFrameInsertPosition.Before
                ? anchorFrame.Index
                : anchorFrame.Index + 1;
        ApplyFrameMutation(section, frames, selectedIndex);
    }

    public Task<IReadOnlyList<string>> CreateSectionSnapshotAsync(CharacterCard character, SequenceFrameSection section, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => _sequenceFrameService.CreateActionSnapshot(character, section.Action), cancellationToken);
    }

    public async Task RestoreSectionSnapshotAsync(CharacterCard character, SequenceFrameSection section, IReadOnlyList<string> snapshotFilePaths, CancellationToken cancellationToken = default)
    {
        await Task.Run(() => _sequenceFrameService.RestoreActionFrames(character, section.Action, snapshotFilePaths), cancellationToken);
        await ReloadAfterFrameMutationAsync(
            character,
            section.Action.Code,
            SelectedEditorFrame?.Index ?? 1,
            cancellationToken);
    }

    public async Task ReorderFramesAsync(CharacterCard character, SequenceFrameSection section, IReadOnlyList<SequenceFrameItem> orderedFrames, CancellationToken cancellationToken = default)
    {
        if (RejectWhenReadOnly())
        {
            return;
        }

        var selectedIndex = SelectedEditorFrame is null
            ? 1
            : Math.Max(
                1,
                orderedFrames
                    .Select((item, index) => new { item, index })
                    .FirstOrDefault(entry => Equals(entry.item, SelectedEditorFrame))
                    ?.index + 1 ?? 1);
        var frames = await Task.Run(() => _sequenceFrameService.ReorderFrames(character, section.Action, orderedFrames), cancellationToken);
        ApplyFrameMutation(section, frames, selectedIndex);
    }

    public async Task ReplaceFrameAsync(
        CharacterCard character,
        SequenceFrameSection section,
        SequenceFrameItem frame,
        string sourceFilePath,
        CancellationToken cancellationToken = default)
    {
        if (RejectWhenReadOnly())
        {
            return;
        }

        var frames = await Task.Run(
            () => _sequenceFrameService.ReplaceFrame(character, section.Action, frame, sourceFilePath),
            cancellationToken);
        ApplyFrameMutation(section, frames, frame.Index);
    }

    public async Task ReplaceFrameWithSourcesAsync(
        CharacterCard character,
        SequenceFrameSection section,
        SequenceFrameItem frame,
        IReadOnlyList<string> sourceFilePaths,
        CancellationToken cancellationToken = default)
    {
        if (RejectWhenReadOnly())
        {
            return;
        }

        var frames = await Task.Run(
            () => _sequenceFrameService.ReplaceFrameWithSources(character, section.Action, frame, sourceFilePaths),
            cancellationToken);
        ApplyFrameMutation(section, frames, frame.Index);
    }

    public async Task SetFrameDurationAsync(
        CharacterCard character,
        SequenceFrameSection section,
        SequenceFrameItem frame,
        int durationFrames,
        CancellationToken cancellationToken = default)
    {
        if (RejectWhenReadOnly())
        {
            return;
        }

        var frames = await Task.Run(
            () => _sequenceFrameService.SetFrameDuration(character, section.Action, frame, durationFrames),
            cancellationToken);
        ApplyFrameMutation(section, frames, frame.Index);
    }

    public async Task SetFrameVoiceAsync(
        CharacterCard character,
        SequenceFrameSection section,
        SequenceFrameItem frame,
        string? voiceFilePath,
        CancellationToken cancellationToken = default)
    {
        var frames = await Task.Run(
            () => _sequenceFrameService.SetFrameVoice(character, section.Action, frame, voiceFilePath),
            cancellationToken);
        ApplyFrameMutation(section, frames, frame.Index);
    }

    public string GetActionFolderPath(CharacterCard character, SequenceFrameSection section)
    {
        return _sequenceFrameService.GetActionFolderPath(character, section.Action);
    }

    public void SelectSection(SequenceFrameSection section)
    {
        ApplyPreviewFps(GetSectionFps(section.Action.Code));
        section = ApplyVoiceSyncAnalysis(section);
        PreviewFrames.Clear();
        foreach (var frame in section.Frames.OrderBy(frame => frame.Index).ThenBy(frame => frame.FileName))
        {
            PreviewFrames.Add(frame);
        }

        _previewIndex = 0;
        _previewSection = section;
        PreviewTitle = $"{section.Action.DisplayName} / {section.Action.Code}";
        IsPreviewing = false;
        UpdateCurrentFrame(PreviewFrames.FirstOrDefault());
    }

    public bool TrySelectSection(string actionCode)
    {
        var section = EnumerateAllSections().FirstOrDefault(item =>
            string.Equals(item.Action.Code, actionCode, StringComparison.OrdinalIgnoreCase));
        if (section is null)
        {
            return false;
        }

        SelectSection(section);
        return true;
    }

    public void SelectSectionForManagement(SequenceFrameSection section)
    {
        section = ApplyVoiceSyncAnalysis(section);
        SelectedSection = section;
        SelectedSectionFrames.Clear();
        foreach (var frame in section.Frames.OrderBy(frame => frame.Index).ThenBy(frame => frame.FileName))
        {
            SelectedSectionFrames.Add(frame);
        }

        SelectedEditorFrame = SelectedSectionFrames.FirstOrDefault();
        RefreshAvailableVoices();
        OnPropertyChanged(nameof(EditorSequenceSummary));
    }

    public void SelectEditorFrame(SequenceFrameItem frame)
    {
        SelectedEditorFrame = SelectedSectionFrames.FirstOrDefault(item => item.Index == frame.Index) ?? frame;
    }

    public async Task RefreshCollectionAsync(IProgress<ProgressUpdate>? progress = null, CancellationToken cancellationToken = default)
    {
        var sections = EnumerateAllSections().ToList();
        var materialCount = CountFrameMaterials(sections);
        var useSavedDuplicateResults = _data.DuplicateCheckMaterialCount == materialCount;
        var cachedHashes = new Dictionary<string, SequenceFrameDuplicateHashEntry>(
            _data.DuplicateContentHashes,
            StringComparer.OrdinalIgnoreCase);
        var items = await Task.Run(
            () => BuildCollectionItems(
                sections,
                detectDuplicates: false,
                useSavedDuplicateResults,
                cachedHashes,
                progress,
                cancellationToken),
            cancellationToken);
        ReplaceCollectionItems(items);
        if (!useSavedDuplicateResults)
        {
            CollectionSummaryText = $"共 {CollectionItems.Count} 张图片，尚未检测重复。";
            return;
        }

        UpdateCollectionDuplicateSummary();
        SaveDuplicateCheckResultIfChanged();
    }

    public async Task DetectCollectionDuplicatesAsync(
        IProgress<ProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sections = EnumerateAllSections().ToList();
        var items = await Task.Run(
            () => BuildCollectionItems(
                sections,
                detectDuplicates: true,
                useSavedDuplicateResults: false,
                cachedHashes: null,
                progress,
                cancellationToken),
            cancellationToken);
        ReplaceCollectionItems(items);

        var duplicateCount = CollectionItems.Count(item => item.HasDuplicate);
        UpdateCollectionDuplicateSummary();
        SaveDuplicateCheckResult();
        if (duplicateCount > 0)
        {
            SetNotice(InfoBarSeverity.Warning, "发现重复图片", $"帧合集发现 {duplicateCount} 张内容重复图片。请在重复标识卡片右键处理重复。");
        }
        else
        {
            UpdateNotice();
        }
    }

    // 这里曾经有一个 RefreshCollection()，内部 RefreshCollectionAsync().GetAwaiter().GetResult()。
    // 它全仓没有调用点，但只要有人调就会和 UI 线程互锁（和关窗那条死锁同一形状）。
    // 需要同步刷新时请直接 await RefreshCollectionAsync()，别再包一层阻塞等待。

    private IReadOnlyList<SequenceFrameCollectionItem> BuildCollectionItems(
        IReadOnlyList<SequenceFrameSection> sections,
        bool detectDuplicates,
        bool useSavedDuplicateResults,
        IReadOnlyDictionary<string, SequenceFrameDuplicateHashEntry>? cachedHashes,
        IProgress<ProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        if (sections.Count == 0)
        {
            progress?.Report(new ProgressUpdate("当前角色暂无序列帧。", 100, null));
            return [];
        }

        var groupedFrames = sections
            .SelectMany(section => section.Frames.Select(frame => new
            {
                SectionTitle = section.Action.DisplayName,
                section.Action.Code,
                Frame = frame
            }))
            .Where(item => File.Exists(item.Frame.FilePath))
            .GroupBy(item => item.Frame.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var frameRecords = new List<(SequenceFrameItem Frame, IReadOnlyList<string> Usages, string Hash)>();
        for (var index = 0; index < groupedFrames.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var group = groupedFrames[index];
            var first = group.First();
            var percent = groupedFrames.Count == 0 ? 100 : 5 + (index + 1) * 85d / groupedFrames.Count;
            progress?.Report(new ProgressUpdate(
                detectDuplicates ? "正在检测帧内容重复..." : "正在整理帧合集...",
                percent,
                first.Frame.FileName));
            frameRecords.Add((
                first.Frame,
                group
                    .Select(item => $"{item.SectionTitle} #{item.Frame.Index}")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                detectDuplicates
                    ? ComputeImageContentHash(first.Frame.FilePath)
                    : useSavedDuplicateResults
                        ? GetSavedOrUpdatedContentHash(first.Frame.FilePath, cachedHashes)
                        : string.Empty));
        }

        progress?.Report(new ProgressUpdate(
            detectDuplicates ? "正在整理重复检测结果..." : "帧合集整理完成。",
            94,
            $"{frameRecords.Count} 张图片"));
        var duplicateMap = frameRecords
            .Where(item => !string.IsNullOrWhiteSpace(item.Hash))
            .GroupBy(item => item.Hash, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .SelectMany(group =>
            {
                return group.Select(item => new
                {
                    item.Frame.FilePath,
                    Text = "存在内容重复"
                });
            })
            .ToDictionary(item => item.FilePath, item => item.Text, StringComparer.OrdinalIgnoreCase);

        var result = new List<SequenceFrameCollectionItem>();
        foreach (var record in frameRecords)
        {
            var hasDuplicate = duplicateMap.TryGetValue(record.Frame.FilePath, out var duplicateText);
            result.Add(new SequenceFrameCollectionItem(
                Path.GetFileName(record.Frame.FilePath),
                Path.GetFileName(record.Frame.FilePath),
                record.Frame.FilePath,
                record.Frame.FileUri,
                Path.GetFileName(record.Frame.FilePath),
                record.Hash,
                $"{record.Frame.ActualWidth}x{record.Frame.ActualHeight}",
                string.Join(" / ", record.Usages),
                record.Usages.Count,
                hasDuplicate,
                duplicateText ?? string.Empty));
        }

        return result;
    }

    private void ReplaceCollectionItems(IReadOnlyList<SequenceFrameCollectionItem> items)
    {
        CollectionItems.Clear();
        foreach (var item in items)
        {
            CollectionItems.Add(item);
        }
    }

    private static int CountFrameMaterials(IReadOnlyList<SequenceFrameSection> sections)
    {
        return sections
            .SelectMany(section => section.Frames)
            .Where(frame => !frame.IsBlank && File.Exists(frame.FilePath))
            .Select(frame => Path.GetFullPath(frame.FilePath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    private string GetSavedOrUpdatedContentHash(
        string filePath,
        IReadOnlyDictionary<string, SequenceFrameDuplicateHashEntry>? cachedHashes)
    {
        var file = new FileInfo(filePath);
        var cacheKey = GetDuplicateCacheKey(file.FullName);
        if (cachedHashes is not null &&
            cachedHashes.TryGetValue(cacheKey, out var cached) &&
            cached.FileLength == file.Length &&
            cached.LastWriteTimeUtcTicks == file.LastWriteTimeUtc.Ticks &&
            !string.IsNullOrWhiteSpace(cached.ContentHash))
        {
            return cached.ContentHash;
        }

        return ComputeImageContentHash(file.FullName);
    }

    private string GetDuplicateCacheKey(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        return _currentCharacter is null
            ? fullPath.Replace('\\', '/')
            : Path.GetRelativePath(_currentCharacter.FolderPath, fullPath).Replace('\\', '/');
    }

    private void UpdateCollectionDuplicateSummary()
    {
        var duplicateCount = CollectionItems.Count(item => item.HasDuplicate);
        CollectionSummaryText = duplicateCount > 0
            ? $"共 {CollectionItems.Count} 张图片，{duplicateCount} 张存在内容重复。"
            : $"共 {CollectionItems.Count} 张图片，未发现内容完全重复图片。";
    }

    private void SaveDuplicateCheckResultIfChanged()
    {
        var hashes = BuildDuplicateHashCache();
        var duplicateCount = CollectionItems.Count(item => item.HasDuplicate);
        if (_data.DuplicateCheckMaterialCount == CollectionItems.Count &&
            _data.DuplicateCheckDuplicateCount == duplicateCount &&
            DuplicateHashCachesMatch(_data.DuplicateContentHashes, hashes))
        {
            return;
        }

        SaveDuplicateCheckResult(hashes, duplicateCount);
    }

    private void SaveDuplicateCheckResult()
    {
        SaveDuplicateCheckResult(
            BuildDuplicateHashCache(),
            CollectionItems.Count(item => item.HasDuplicate));
    }

    private void SaveDuplicateCheckResult(
        Dictionary<string, SequenceFrameDuplicateHashEntry> hashes,
        int duplicateCount)
    {
        if (_currentCharacter is null)
        {
            return;
        }

        _data.DuplicateCheckSignature = _currentSequenceSignature;
        _data.DuplicateCheckMaterialCount = CollectionItems.Count;
        _data.DuplicateCheckDuplicateCount = duplicateCount;
        _data.DuplicateContentHashes = hashes;
        _sequenceFrameService.SaveData(_currentCharacter, _data);
        SequenceFramesSaved?.Invoke(this, EventArgs.Empty);
    }

    private Dictionary<string, SequenceFrameDuplicateHashEntry> BuildDuplicateHashCache()
    {
        var hashes = new Dictionary<string, SequenceFrameDuplicateHashEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in CollectionItems.Where(item => !string.IsNullOrWhiteSpace(item.ContentHash)))
        {
            var file = new FileInfo(item.FilePath);
            hashes[GetDuplicateCacheKey(file.FullName)] = new SequenceFrameDuplicateHashEntry
            {
                ContentHash = item.ContentHash,
                FileLength = file.Length,
                LastWriteTimeUtcTicks = file.LastWriteTimeUtc.Ticks
            };
        }

        return hashes;
    }

    private static bool DuplicateHashCachesMatch(
        IReadOnlyDictionary<string, SequenceFrameDuplicateHashEntry> left,
        IReadOnlyDictionary<string, SequenceFrameDuplicateHashEntry> right)
    {
        return left.Count == right.Count && left.All(pair =>
            right.TryGetValue(pair.Key, out var candidate) &&
            string.Equals(pair.Value.ContentHash, candidate.ContentHash, StringComparison.Ordinal) &&
            pair.Value.FileLength == candidate.FileLength &&
            pair.Value.LastWriteTimeUtcTicks == candidate.LastWriteTimeUtcTicks);
    }

    public IReadOnlyList<SequenceFrameCollectionItem> GetDuplicateCollectionItems(SequenceFrameCollectionItem item)
    {
        if (string.IsNullOrWhiteSpace(item.ContentHash))
        {
            return [];
        }

        return CollectionItems
            .Where(candidate => string.Equals(candidate.ContentHash, item.ContentHash, StringComparison.Ordinal))
            .OrderBy(candidate => candidate.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<int> ResolveDuplicateFramesAsync(
        CharacterCard character,
        SequenceFrameCollectionItem keptItem,
        IReadOnlyList<SequenceFrameCollectionItem> duplicateItems,
        IProgress<ProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (RejectWhenReadOnly())
        {
            return 0;
        }

        var skills = await Task.Run(() => _skillsService.Load(character), cancellationToken);
        var updatedCount = await Task.Run(
            () => _sequenceFrameService.ReplaceDuplicateFrameReferences(
                character,
                skills,
                keptItem.FilePath,
                duplicateItems.Select(item => item.FilePath).ToList(),
                progress,
                cancellationToken),
            cancellationToken);
        await LoadAsync(character, cancellationToken);
        await DetectCollectionDuplicatesAsync(cancellationToken: cancellationToken);
        return updatedCount;
    }

    public async Task<int> ResolveAllDuplicateFramesAsync(
        CharacterCard character,
        IProgress<ProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (RejectWhenReadOnly())
        {
            return 0;
        }

        var indexedItems = CollectionItems
            .Select((item, index) => new { Item = item, Index = index })
            .ToList();
        var duplicateGroups = indexedItems
            .Where(entry => entry.Item.HasDuplicate && !string.IsNullOrWhiteSpace(entry.Item.ContentHash))
            .GroupBy(entry => entry.Item.ContentHash, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.Min(entry => entry.Index))
            .ToList();
        if (duplicateGroups.Count == 0)
        {
            return 0;
        }

        var skills = await Task.Run(() => _skillsService.Load(character), cancellationToken);
        var updatedReferenceCount = 0;
        for (var groupIndex = 0; groupIndex < duplicateGroups.Count; groupIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var group = duplicateGroups[groupIndex].ToList();
            var kept = group
                .OrderByDescending(entry => entry.Item.UsageCount)
                .ThenBy(entry => entry.Index)
                .First();
            progress?.Report(new ProgressUpdate(
                "正在处理重复帧资源...",
                5 + groupIndex * 85d / duplicateGroups.Count,
                $"第 {groupIndex + 1}/{duplicateGroups.Count} 组 · 保留 {kept.Item.UsageText}"));
            updatedReferenceCount += await Task.Run(
                () => _sequenceFrameService.ReplaceDuplicateFrameReferences(
                    character,
                    skills,
                    kept.Item.FilePath,
                    group.Select(entry => entry.Item.FilePath).ToList(),
                    cancellationToken: cancellationToken),
                cancellationToken);
        }

        progress?.Report(new ProgressUpdate("正在刷新帧合集...", 94, "保存新的重复检测结果"));
        await LoadAsync(character, cancellationToken);
        await DetectCollectionDuplicatesAsync(cancellationToken: cancellationToken);
        return updatedReferenceCount;
    }

    public void SelectFrame(SequenceFrameItem frame)
    {
        var index = PreviewFrames.IndexOf(frame);
        _previewIndex = index < 0 ? 0 : index;
        IsPreviewing = false;
        UpdateCurrentFrame(frame);
    }

    public void StepPreviewFrame(int direction)
    {
        MovePreviewFrame(direction);
        IsPreviewing = false;
    }

    public void MovePreviewFrame(int direction)
    {
        if (PreviewFrames.Count == 0)
        {
            return;
        }

        _previewIndex = (_previewIndex + direction + PreviewFrames.Count) % PreviewFrames.Count;
        UpdateCurrentFrame(PreviewFrames[_previewIndex]);
    }

    public void StartPreview()
    {
        if (PreviewFrames.Count == 0)
        {
            return;
        }

        IsPreviewing = true;
    }

    public void StartEditorPreview()
    {
        if (PreviewFrames.Count == 0)
        {
            return;
        }

        if (EditorPlaybackMode == SequenceEditorPlaybackMode.Once &&
            _previewIndex == PreviewFrames.Count - 1)
        {
            _previewIndex = 0;
            UpdateCurrentFrame(PreviewFrames[0]);
        }

        StartPreview();
    }

    public void StopPreview()
    {
        IsPreviewing = false;
    }

    public void AdvancePreviewFrame()
    {
        if (!IsPreviewing || PreviewFrames.Count == 0)
        {
            return;
        }

        _previewIndex = (_previewIndex + 1) % PreviewFrames.Count;
        UpdateCurrentFrame(PreviewFrames[_previewIndex]);
    }

    public bool AdvanceEditorPreviewFrame()
    {
        if (!IsPreviewing || PreviewFrames.Count == 0)
        {
            return false;
        }

        if (EditorPlaybackMode == SequenceEditorPlaybackMode.Once &&
            _previewIndex == PreviewFrames.Count - 1)
        {
            StopPreview();
            return false;
        }

        AdvancePreviewFrame();
        return true;
    }

    private void ApplyPreviewFps(int value)
    {
        _isApplyingPreviewFps = true;
        try
        {
            PreviewFps = value;
        }
        finally
        {
            _isApplyingPreviewFps = false;
        }
    }

    private int GetSectionFps(string actionCode)
    {
        return _data.ActionSettings
            .FirstOrDefault(settings => settings.ActionCode == actionCode)
            ?.Fps ?? SequenceFrameService.DefaultFps;
    }

    private void SaveSelectedSectionFps(int fps)
    {
        if (_currentCharacter is null || _previewSection is null)
        {
            return;
        }

        var actionCode = _previewSection.Action.Code;
        if (string.IsNullOrWhiteSpace(actionCode))
        {
            return;
        }

        var settings = _data.ActionSettings.FirstOrDefault(item => item.ActionCode == actionCode);
        if (settings is null)
        {
            settings = new SequenceFrameActionSettings { ActionCode = actionCode };
            _data.ActionSettings.Add(settings);
        }

        settings.Fps = fps;
        _sequenceFrameService.SaveData(_currentCharacter, _data);
        _sequenceFrameService.SetActionFps(_currentCharacter, _previewSection.Action, fps);
        SequenceFramesSaved?.Invoke(this, EventArgs.Empty);
    }

    private void ClearPreview()
    {
        PreviewFrames.Clear();
        _previewSection = null;
        PreviewTitle = "未选择动作";
        CurrentFrameUri = string.Empty;
        CurrentFrameFilePath = string.Empty;
        CurrentFrameCacheKey = string.Empty;
        CurrentFrameText = "暂无预览帧";
        IsPreviewing = false;
        _previewIndex = 0;
        OnPropertyChanged(nameof(EditorPlaybackPositionText));
    }

    private void ClearSelectedSection()
    {
        SelectedEditorFrame = null;
        SelectedSectionFrames.Clear();
        AvailableVoices.Clear();
        SelectedSection = null;
        OnPropertyChanged(nameof(EditorSequenceSummary));
    }

    private void ApplyFrameMutation(
        SequenceFrameSection previousSection,
        IReadOnlyList<SequenceFrameItem> frames,
        int selectedIndex)
    {
        var orderedFrames = frames.OrderBy(frame => frame.Index).ToList();
        var invalidCount = orderedFrames.Count(frame => !frame.IsValid);
        var statusText = orderedFrames.Count == 0
            ? "未设置"
            : invalidCount > 0
                ? $"{orderedFrames.Count} 张，{invalidCount} 张尺寸不合规"
                : $"{orderedFrames.Count} 张，尺寸合规";
        var updatedSection = new SequenceFrameSection(
            previousSection.Action,
            orderedFrames,
            statusText,
            orderedFrames.Count == 0 || invalidCount > 0);
        updatedSection = ApplyVoiceSyncAnalysis(updatedSection);
        orderedFrames = updatedSection.Frames.ToList();

        ReplaceSection(BaseSectionGroups, updatedSection);
        ReplaceSection(SkillSectionGroups, updatedSection);
        var comboIndex = ComboSections
            .Select((section, index) => new { section, index })
            .FirstOrDefault(item => item.section.Action.Code == updatedSection.Action.Code)
            ?.index ?? -1;
        if (comboIndex >= 0)
        {
            ComboSections[comboIndex] = updatedSection;
        }

        _previewSection = updatedSection;
        SelectedSection = updatedSection;
        SynchronizeFrameCollection(PreviewFrames, orderedFrames);
        SynchronizeFrameCollection(SelectedSectionFrames, orderedFrames);
        _previewIndex = orderedFrames.Count == 0
            ? 0
            : Math.Clamp(selectedIndex - 1, 0, orderedFrames.Count - 1);
        PreviewTitle = $"{updatedSection.Action.DisplayName} / {updatedSection.Action.Code}";
        IsPreviewing = false;
        UpdateCurrentFrame(CurrentPreviewFrame);
        SelectedEditorFrame = CurrentPreviewFrame;
        RefreshAvailableVoices();
        _currentSequenceSignature = BuildSequenceSignature(EnumerateAllSections().ToList());
        OnPropertyChanged(nameof(EditorSequenceSummary));
        UpdateNotice();
        SequenceFramesSaved?.Invoke(this, EventArgs.Empty);
    }

    private static void ReplaceSection(
        ObservableCollection<SequenceFrameSectionGroup> groups,
        SequenceFrameSection updatedSection)
    {
        for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            var sectionIndex = groups[groupIndex].Sections
                .Select((section, index) => new { section, index })
                .FirstOrDefault(item => item.section.Action.Code == updatedSection.Action.Code)
                ?.index ?? -1;
            if (sectionIndex < 0)
            {
                continue;
            }

            var sections = groups[groupIndex].Sections.ToList();
            sections[sectionIndex] = updatedSection;
            groups[groupIndex] = groups[groupIndex] with { Sections = sections };
            return;
        }
    }

    private static void SynchronizeFrameCollection(
        ObservableCollection<SequenceFrameItem> target,
        IReadOnlyList<SequenceFrameItem> source)
    {
        var sharedCount = Math.Min(target.Count, source.Count);
        for (var index = 0; index < sharedCount; index++)
        {
            if (!Equals(target[index], source[index]))
            {
                target[index] = source[index];
            }
        }

        while (target.Count > source.Count)
        {
            target.RemoveAt(target.Count - 1);
        }

        for (var index = target.Count; index < source.Count; index++)
        {
            target.Add(source[index]);
        }
    }

    private async Task ReloadAfterFrameMutationAsync(
        CharacterCard character,
        string actionCode,
        int selectedIndex = 1,
        CancellationToken cancellationToken = default)
    {
        await LoadAsync(character, cancellationToken);
        var section = EnumerateAllSections()
            .FirstOrDefault(item => item.Action.Code == actionCode);
        if (section is not null)
        {
            SelectSection(section);
            SelectSectionForManagement(section);
            SelectedEditorFrame = SelectedSectionFrames.FirstOrDefault(frame => frame.Index == selectedIndex)
                ?? SelectedSectionFrames.LastOrDefault();
        }
        else
        {
            ClearSelectedSection();
        }
    }

    private void RefreshAvailableVoices()
    {
        AvailableVoices.Clear();
        AvailableVoices.Add(new SequenceFrameVoiceOption("无语音", string.Empty));
        if (_currentCharacter is null || SelectedSection is null)
        {
            OnPropertyChanged(nameof(SelectedEditorVoicePath));
            OnPropertyChanged(nameof(SelectedEditorVoiceOption));
            return;
        }

        var sections = _voiceMaterialService.LoadSections(_currentCharacter);
        var hasMappedKind = TryGetVoiceKindForAction(SelectedSection.Action.Code, out var mappedKind);
        foreach (var section in sections.Where(section => !hasMappedKind || section.Spec.Kind == mappedKind))
        {
            foreach (var item in section.Items.Where(item => item.CanPlay))
            {
                AvailableVoices.Add(new SequenceFrameVoiceOption(
                    $"{section.Spec.DisplayName} / {item.FileName}",
                    item.FilePath));
            }
        }

        var catalog = sections
            .SelectMany(section => section.Items.Select(item => new { section.Spec, Item = item }))
            .Where(entry => entry.Item.CanPlay)
            .ToDictionary(entry => entry.Item.FilePath, StringComparer.OrdinalIgnoreCase);
        foreach (var voicePath in SelectedSection.Frames
                     .Select(frame => frame.VoiceFilePath)
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (AvailableVoices.Any(option => string.Equals(
                    option.FilePath,
                    voicePath,
                    StringComparison.OrdinalIgnoreCase)) ||
                !catalog.TryGetValue(voicePath, out var currentVoice))
            {
                continue;
            }

            AvailableVoices.Add(new SequenceFrameVoiceOption(
                $"{currentVoice.Spec.DisplayName} / {currentVoice.Item.FileName}",
                currentVoice.Item.FilePath));
        }

        OnPropertyChanged(nameof(SelectedEditorVoicePath));
        OnPropertyChanged(nameof(SelectedEditorVoiceOption));
    }

    private SequenceFrameSection ApplyVoiceSyncAnalysis(SequenceFrameSection section)
    {
        var results = _voiceSyncAnalyzer.Analyze(section.Frames, PreviewFps)
            .ToDictionary(result => result.FrameIndex);
        var frames = section.Frames
            .OrderBy(frame => frame.Index)
            .ThenBy(frame => frame.FileName, StringComparer.OrdinalIgnoreCase)
            .Select(frame => frame with
            {
                VoiceSyncResult = results.GetValueOrDefault(frame.Index)
            })
            .ToList();
        return section with { Frames = frames };
    }

    private void RefreshVoiceSyncAnalysis()
    {
        if (SelectedSection is null)
        {
            return;
        }

        var selectedIndex = SelectedEditorFrame?.Index ?? 1;
        var updatedSection = ApplyVoiceSyncAnalysis(SelectedSection);
        SelectedSection = updatedSection;
        SynchronizeFrameCollection(SelectedSectionFrames, updatedSection.Frames);
        if (_previewSection?.Action.Code == updatedSection.Action.Code)
        {
            _previewSection = updatedSection;
            SynchronizeFrameCollection(PreviewFrames, updatedSection.Frames);
            _previewIndex = PreviewFrames.Count == 0
                ? 0
                : Math.Clamp(_previewIndex, 0, PreviewFrames.Count - 1);
            UpdateCurrentFrame(CurrentPreviewFrame);
        }

        SelectedEditorFrame = SelectedSectionFrames.FirstOrDefault(frame => frame.Index == selectedIndex)
            ?? SelectedSectionFrames.LastOrDefault();
        OnPropertyChanged(nameof(EditorSequenceSummary));
    }

    private static bool TryGetVoiceKindForAction(string actionCode, out VoiceMaterialKind kind)
    {
        foreach (var mapping in VoiceKindMappings)
        {
            if (IsActionCodeInFamily(actionCode, mapping.ActionCode))
            {
                kind = mapping.Kind;
                return true;
            }
        }

        kind = default;
        return false;
    }

    private static bool IsActionCodeInFamily(string actionCode, string baseCode)
    {
        if (string.Equals(actionCode, baseCode, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return actionCode.StartsWith(baseCode, StringComparison.OrdinalIgnoreCase) &&
               actionCode.Length > baseCode.Length &&
               actionCode[baseCode.Length..].All(char.IsDigit);
    }

    private void UpdateCurrentFrame(SequenceFrameItem? frame)
    {
        OnPropertyChanged(nameof(CurrentPreviewFrame));
        OnPropertyChanged(nameof(EditorPlaybackPositionText));
        CurrentFrameUri = frame?.FileUri ?? string.Empty;
        CurrentFrameFilePath = frame?.FilePath ?? string.Empty;
        CurrentFrameCacheKey = frame?.CacheKey ?? string.Empty;
        if (frame is null)
        {
            CurrentFrameText = "暂无预览帧";
            return;
        }

        var total = Math.Max(PreviewFrames.Count, frame.Index);
        var width = MaterialSequenceNaming.GetWidth(total);
        var frameIndexText = $"{frame.Index.ToString().PadLeft(width, '0')}/{total.ToString().PadLeft(width, '0')}";
        CurrentFrameText = $"{frameIndexText}  {frame.PlainFileName}  |  {frame.ActualWidth}x{frame.ActualHeight}";
        if (SelectedSection?.Action.Code == _previewSection?.Action.Code &&
            !Equals(_selectedEditorFrame, frame))
        {
            SetProperty(ref _selectedEditorFrame, frame, nameof(SelectedEditorFrame));
            EditorFrameDurationInput = frame.DurationFrames;
            OnPropertyChanged(nameof(HasSelectedEditorFrame));
            OnPropertyChanged(nameof(EditorFrameDurationText));
            OnPropertyChanged(nameof(EditorSelectedFrameText));
            OnPropertyChanged(nameof(SelectedEditorVoicePath));
            OnPropertyChanged(nameof(SelectedEditorVoiceOption));
        }
    }

    private static IEnumerable<SequenceFrameSectionGroup> BuildSectionGroups(
        IReadOnlyList<SequenceFrameSection> sections,
        string title,
        string description)
    {
        return sections
            .GroupBy(section => Math.Max(1, section.Action.FormIndex))
            .OrderBy(group => group.Key)
            .Select(group => new SequenceFrameSectionGroup(
                group.Key == 1 ? title : $"{title}{group.Key}",
                description,
                group.OrderBy(section => section.Action.Code, StringComparer.OrdinalIgnoreCase).ToList()));
    }

    private IEnumerable<SequenceFrameSection> EnumerateAllSections()
    {
        foreach (var section in BaseSectionGroups.SelectMany(group => group.Sections))
        {
            yield return section;
        }

        foreach (var section in SkillSectionGroups.SelectMany(group => group.Sections))
        {
            yield return section;
        }

        foreach (var section in ComboSections)
        {
            yield return section;
        }
    }

    private void UpdateNotice()
    {
        var allSections = EnumerateAllSections().ToList();
        UpdateNotice(
            allSections,
            allSections.Where(section => section.Frames.Count == 0).ToList(),
            allSections.Where(section => section.Frames.Any(frame => !frame.IsValid)).ToList());
    }

    private void UpdateNotice(
        IReadOnlyList<SequenceFrameSection> allSections,
        IReadOnlyList<SequenceFrameSection> missingSections,
        IReadOnlyList<SequenceFrameSection> invalidSections)
    {
        if (_currentCharacter is null)
        {
            SetNotice(InfoBarSeverity.Warning, "未选择角色", "请先在角色台选择当前制作角色。");
            return;
        }

        if (missingSections.Count > 0 || invalidSections.Count > 0)
        {
            var details = new List<string>();
            if (missingSections.Count > 0)
            {
                details.Add("未设置：" + FormatSectionNames(missingSections));
            }

            if (invalidSections.Count > 0)
            {
                details.Add("尺寸不合规：" + string.Join("、", invalidSections.Take(6).Select(section =>
                    $"{section.Action.DisplayName}{section.Frames.Count(frame => !frame.IsValid)}张")));
                if (invalidSections.Count > 6)
                {
                    details[^1] += $" 等 {invalidSections.Count} 项";
                }
            }

            SetNotice(InfoBarSeverity.Warning, "序列帧信息未完成", string.Join("；", details));
            return;
        }

        var materialCount = CountFrameMaterials(allSections);
        if (materialCount > 0 && _data.DuplicateCheckMaterialCount != materialCount)
        {
            SetNotice(InfoBarSeverity.Informational, "请确认重复帧", "帧素材数量已经变化。打开帧合集并点击“检测重复”，完成后此提示会自动消失。");
            return;
        }

        if (_data.DuplicateCheckDuplicateCount > 0)
        {
            SetNotice(InfoBarSeverity.Warning, "发现重复图片", $"当前检测结果仍有 {_data.DuplicateCheckDuplicateCount} 张重复资源，请打开帧合集处理。");
            return;
        }

        SetNotice(InfoBarSeverity.Success, "序列帧已就绪", "序列帧尺寸合规，且当前内容已确认没有重复图片。");
    }

    private void SetNotice(InfoBarSeverity severity, string title, string message)
    {
        NoticeSeverity = severity;
        NoticeTitle = title;
        NoticeMessage = message;
        IsNoticeOpen = true;
    }

    private static string FormatSectionNames(IReadOnlyList<SequenceFrameSection> sections)
    {
        var names = sections
            .Take(8)
            .Select(section => section.Action.DisplayName)
            .ToList();
        var text = string.Join("、", names);
        return sections.Count > names.Count
            ? $"{text} 等 {sections.Count} 项"
            : text;
    }

    private static string BuildSequenceSignature(IReadOnlyList<SequenceFrameSection> sections)
    {
        var builder = new StringBuilder();
        foreach (var section in sections.OrderBy(section => section.Action.Code, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(section.Action.Code).Append('|');
            foreach (var frame in section.Frames.OrderBy(frame => frame.Index))
            {
                builder
                    .Append(frame.Index)
                    .Append(':')
                    .Append(frame.IsBlank ? "blank" : frame.FilePath)
                    .Append(':')
                    .Append(frame.UpdatedAt.Ticks)
                    .Append(':')
                    .Append(frame.ActualWidth)
                    .Append('x')
                    .Append(frame.ActualHeight)
                    .Append(';');
            }

            builder.AppendLine();
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static string ComputeImageContentHash(string filePath)
    {
        try
        {
            using var source = System.Drawing.Image.FromFile(filePath);
            using var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.DrawImage(source, 0, 0, source.Width, source.Height);
            }

            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var bytes = new byte[Math.Abs(data.Stride) * data.Height];
                Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                using var sha = SHA256.Create();
                var widthBytes = BitConverter.GetBytes(bitmap.Width);
                var heightBytes = BitConverter.GetBytes(bitmap.Height);
                sha.TransformBlock(widthBytes, 0, widthBytes.Length, null, 0);
                sha.TransformBlock(heightBytes, 0, heightBytes.Length, null, 0);
                sha.TransformFinalBlock(bytes, 0, bytes.Length);
                return Convert.ToHexString(sha.Hash ?? []).ToLowerInvariant();
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }
        catch
        {
            return string.Empty;
        }
    }
}
