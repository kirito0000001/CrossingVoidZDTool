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
    private readonly SequenceFrameService _sequenceFrameService;
    private readonly CharacterSkillsService _skillsService;
    private readonly CharacterFormService _formService = new();
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
    private SequenceFrameSection? _previewSection;
    private SequenceFrameSection? _selectedSection;
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
            if (SetProperty(ref _previewFps, normalized) && !_isApplyingPreviewFps)
            {
                SaveSelectedSectionFps(normalized);
            }
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

    public string PlayPauseGlyph => IsPreviewing ? "\uE769" : "\uE768";

    public string PlayPauseToolTip => IsPreviewing ? "暂停播放" : "播放当前序列";

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
        await Task.Run(() => _sequenceFrameService.ImportFrames(character, section.Action, sourceFilePaths), cancellationToken);
        await LoadAsync(character, cancellationToken);
    }

    public async Task DeleteFrameAsync(CharacterCard character, SequenceFrameSection section, SequenceFrameItem frame, CancellationToken cancellationToken = default)
    {
        await Task.Run(() => _sequenceFrameService.DeleteFrame(character, section.Action, frame), cancellationToken);
        await ReloadAfterFrameMutationAsync(character, section.Action.Code, cancellationToken);
    }

    public async Task DuplicateFrameAsync(CharacterCard character, SequenceFrameSection section, SequenceFrameItem frame, CancellationToken cancellationToken = default)
    {
        await Task.Run(() => _sequenceFrameService.DuplicateFrame(character, section.Action, frame), cancellationToken);
        await ReloadAfterFrameMutationAsync(character, section.Action.Code, cancellationToken);
    }

    public async Task InsertBlankFrameAsync(CharacterCard character, SequenceFrameSection section, SequenceFrameItem? afterFrame, CancellationToken cancellationToken = default)
    {
        await Task.Run(() => _sequenceFrameService.InsertBlankFrame(character, section.Action, afterFrame), cancellationToken);
        await ReloadAfterFrameMutationAsync(character, section.Action.Code, cancellationToken);
    }

    public Task<IReadOnlyList<string>> CreateSectionSnapshotAsync(CharacterCard character, SequenceFrameSection section, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => _sequenceFrameService.CreateActionSnapshot(character, section.Action), cancellationToken);
    }

    public async Task RestoreSectionSnapshotAsync(CharacterCard character, SequenceFrameSection section, IReadOnlyList<string> snapshotFilePaths, CancellationToken cancellationToken = default)
    {
        await Task.Run(() => _sequenceFrameService.RestoreActionFrames(character, section.Action, snapshotFilePaths), cancellationToken);
        await ReloadAfterFrameMutationAsync(character, section.Action.Code, cancellationToken);
    }

    public async Task ReorderFramesAsync(CharacterCard character, SequenceFrameSection section, IReadOnlyList<SequenceFrameItem> orderedFrames, CancellationToken cancellationToken = default)
    {
        await Task.Run(() => _sequenceFrameService.ReorderFrames(character, section.Action, orderedFrames), cancellationToken);
        await ReloadAfterFrameMutationAsync(character, section.Action.Code, cancellationToken);
    }

    public string GetActionFolderPath(CharacterCard character, SequenceFrameSection section)
    {
        return _sequenceFrameService.GetActionFolderPath(character, section.Action);
    }

    public void SelectSection(SequenceFrameSection section)
    {
        PreviewFrames.Clear();
        foreach (var frame in section.Frames.OrderBy(frame => frame.Index).ThenBy(frame => frame.FileName))
        {
            PreviewFrames.Add(frame);
        }

        _previewIndex = 0;
        _previewSection = section;
        PreviewTitle = $"{section.Action.DisplayName} / {section.Action.Code}";
        IsPreviewing = false;
        ApplyPreviewFps(GetSectionFps(section.Action.Code));
        UpdateCurrentFrame(PreviewFrames.FirstOrDefault());
    }

    public void SelectSectionForManagement(SequenceFrameSection section)
    {
        SelectedSection = section;
        SelectedSectionFrames.Clear();
        foreach (var frame in section.Frames.OrderBy(frame => frame.Index).ThenBy(frame => frame.FileName))
        {
            SelectedSectionFrames.Add(frame);
        }
    }

    public async Task RefreshCollectionAsync(IProgress<ProgressUpdate>? progress = null, CancellationToken cancellationToken = default)
    {
        var sections = EnumerateAllSections().ToList();
        var items = await Task.Run(
            () => BuildCollectionItems(sections, progress, cancellationToken),
            cancellationToken);
        CollectionItems.Clear();
        foreach (var item in items)
        {
            CollectionItems.Add(item);
        }

        var duplicateCount = CollectionItems.Count(item => item.HasDuplicate);
        CollectionSummaryText = duplicateCount > 0
            ? $"共 {CollectionItems.Count} 张图片，{duplicateCount} 张存在内容重复。"
            : $"共 {CollectionItems.Count} 张图片，未发现内容完全重复图片。";
        if (duplicateCount > 0)
        {
            SetNotice(InfoBarSeverity.Warning, "发现重复图片", $"帧合集发现 {duplicateCount} 张内容重复图片。请在重复标识卡片右键处理重复。");
        }
        else if (_currentCharacter is not null)
        {
            _data.DuplicateCheckSignature = _currentSequenceSignature;
            _sequenceFrameService.SaveData(_currentCharacter, _data);
            UpdateNotice();
            SequenceFramesSaved?.Invoke(this, EventArgs.Empty);
        }
    }

    public void RefreshCollection()
    {
        RefreshCollectionAsync().GetAwaiter().GetResult();
    }

    private IReadOnlyList<SequenceFrameCollectionItem> BuildCollectionItems(
        IReadOnlyList<SequenceFrameSection> sections,
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
                "正在检测帧内容重复...",
                percent,
                first.Frame.FileName));
            frameRecords.Add((
                first.Frame,
                group
                    .Select(item => $"{item.SectionTitle} #{item.Frame.Index}")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                ComputeImageContentHash(first.Frame.FilePath)));
        }

        progress?.Report(new ProgressUpdate("正在整理重复检测结果...", 94, $"{frameRecords.Count} 张图片"));
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
        foreach (var record in frameRecords.OrderBy(item => item.Frame.FileName, StringComparer.OrdinalIgnoreCase))
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
                $"使用位置：{string.Join("、", record.Usages)}",
                hasDuplicate,
                duplicateText ?? string.Empty));
        }

        return result;
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
        await RefreshCollectionAsync(cancellationToken: cancellationToken);
        return updatedCount;
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
    }

    private void ClearSelectedSection()
    {
        SelectedSectionFrames.Clear();
        SelectedSection = null;
    }

    private async Task ReloadAfterFrameMutationAsync(CharacterCard character, string actionCode, CancellationToken cancellationToken)
    {
        await LoadAsync(character, cancellationToken);
        var section = EnumerateAllSections()
            .FirstOrDefault(item => item.Action.Code == actionCode);
        if (section is not null)
        {
            SelectSectionForManagement(section);
            SelectSection(section);
        }
        else
        {
            ClearSelectedSection();
        }
    }

    private void UpdateCurrentFrame(SequenceFrameItem? frame)
    {
        CurrentFrameUri = frame?.FileUri ?? string.Empty;
        CurrentFrameFilePath = frame?.FilePath ?? string.Empty;
        CurrentFrameCacheKey = frame?.CacheKey ?? string.Empty;
        if (frame is null)
        {
            CurrentFrameText = "暂无预览帧";
            return;
        }

        var total = Math.Max(PreviewFrames.Count, frame.Index);
        var width = Math.Max(3, total.ToString().Length);
        var frameIndexText = $"{frame.Index.ToString().PadLeft(width, '0')}/{total.ToString().PadLeft(width, '0')}";
        CurrentFrameText = $"{frameIndexText}  {frame.PlainFileName}  |  {frame.ActualWidth}x{frame.ActualHeight}";
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

        if (allSections.Any(section => section.Frames.Count > 0) &&
            !string.Equals(_data.DuplicateCheckSignature, _currentSequenceSignature, StringComparison.Ordinal))
        {
            SetNotice(InfoBarSeverity.Informational, "请确认重复帧", "当前序列内容尚未打开帧合集确认重复内容。点击右侧“打开帧合集”，无重复后此提示会自动消失；序列变更后会重新提示。");
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
