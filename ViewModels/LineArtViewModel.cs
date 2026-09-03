using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;
using Microsoft.UI.Xaml.Controls;

namespace CrossingVoidZDTool.ViewModels;

internal sealed class LineArtViewModel : ObservableObject
{
    private readonly BaseMaterialService _baseMaterialService;
    private readonly VoiceMaterialService _voiceMaterialService;
    private string _statusText = "就绪：等待选择角色。";
    private string _noticeTitle = "未选择角色";
    private string _noticeMessage = "请先在零境角色台选择当前制作角色。";
    private InfoBarSeverity _noticeSeverity = InfoBarSeverity.Informational;
    private bool _isNoticeOpen = true;
    private bool _isLoading;

    public LineArtViewModel(BaseMaterialService baseMaterialService)
        : this(baseMaterialService, new VoiceMaterialService())
    {
    }

    public LineArtViewModel(
        BaseMaterialService baseMaterialService,
        VoiceMaterialService voiceMaterialService)
    {
        _baseMaterialService = baseMaterialService;
        _voiceMaterialService = voiceMaterialService;
    }

    public ObservableCollection<BaseMaterialSection> Sections { get; } = [];

    public ObservableCollection<VoiceMaterialSection> VoiceSections { get; } = [];

    public ObservableCollection<VoiceMaterialSection> RequiredVoiceSections { get; } = [];

    public ObservableCollection<VoiceMaterialSection> OptionalVoiceSections { get; } = [];

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

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public async Task RefreshAsync(CharacterCard? character, CancellationToken cancellationToken = default)
    {
        if (character is null)
        {
            Sections.Clear();
            VoiceSections.Clear();
            RequiredVoiceSections.Clear();
            OptionalVoiceSections.Clear();
            SetNotice(InfoBarSeverity.Informational, "未选择角色", "请先在零境角色台选择当前制作角色。");
            StatusText = "就绪：等待选择角色。";
            return;
        }

        IsLoading = true;
        try
        {
            var (sections, voiceSections) = await Task.Run(
                () => (
                    _baseMaterialService.LoadSections(character, cancellationToken),
                    _voiceMaterialService.LoadSections(character)),
                cancellationToken);
            ApplySections(sections);
            ApplyVoiceSections(voiceSections);

            var missing = sections.Sum(section => section.MissingCount) +
                          voiceSections.Sum(section => section.MissingCount);
            var invalid = sections.Sum(section => section.InvalidCount) +
                          voiceSections.Sum(section => section.InvalidCount);
            var extra = sections.Sum(section => section.ExtraCount) +
                        voiceSections.Sum(section => section.ExtraCount);
            if (missing > 0 || invalid > 0 || extra > 0)
            {
                SetNotice(
                    InfoBarSeverity.Warning,
                    "基础素材需要处理",
                    BuildMaterialNoticeMessage(sections, voiceSections, missing, invalid, extra));
            }
            else
            {
                SetNotice(InfoBarSeverity.Success, "基础素材已就绪", "图片和语音素材均已通过检查。");
            }

            StatusText = $"已检查基础素材：{character.Name}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplySections(IReadOnlyList<BaseMaterialSection> sections)
    {
        var sharedCount = System.Math.Min(Sections.Count, sections.Count);
        for (var index = 0; index < sharedCount; index++)
        {
            Sections[index] = sections[index];
        }

        while (Sections.Count > sections.Count)
        {
            Sections.RemoveAt(Sections.Count - 1);
        }

        for (var index = Sections.Count; index < sections.Count; index++)
        {
            Sections.Add(sections[index]);
        }
    }

    private void ApplyVoiceSections(IReadOnlyList<VoiceMaterialSection> sections)
    {
        var sharedCount = System.Math.Min(VoiceSections.Count, sections.Count);
        for (var index = 0; index < sharedCount; index++)
        {
            VoiceSections[index] = sections[index];
        }

        while (VoiceSections.Count > sections.Count)
        {
            VoiceSections.RemoveAt(VoiceSections.Count - 1);
        }

        for (var index = VoiceSections.Count; index < sections.Count; index++)
        {
            VoiceSections.Add(sections[index]);
        }

        ApplyVoiceGroup(RequiredVoiceSections, sections.Where(section => section.Spec.IsRequired).ToArray());
        ApplyVoiceGroup(OptionalVoiceSections, sections.Where(section => !section.Spec.IsRequired).ToArray());
    }

    private static void ApplyVoiceGroup(
        ObservableCollection<VoiceMaterialSection> target,
        IReadOnlyList<VoiceMaterialSection> sections)
    {
        var sharedCount = System.Math.Min(target.Count, sections.Count);
        for (var index = 0; index < sharedCount; index++)
        {
            target[index] = sections[index];
        }

        while (target.Count > sections.Count)
        {
            target.RemoveAt(target.Count - 1);
        }

        for (var index = target.Count; index < sections.Count; index++)
        {
            target.Add(sections[index]);
        }
    }

    public async Task ImportAsync(CharacterCard character, BaseMaterialKind kind, string sourceFilePath, CancellationToken cancellationToken = default)
    {
        await Task.Run(() => _baseMaterialService.ImportAndCrop(character, kind, sourceFilePath), cancellationToken);
        await RefreshAsync(character, cancellationToken);
    }

    public async Task RepairAsync(CharacterCard character, BaseMaterialKind kind, string sourceFilePath, int index, CancellationToken cancellationToken = default)
    {
        await Task.Run(() => _baseMaterialService.Repair(character, kind, sourceFilePath, index), cancellationToken);
        await RefreshAsync(character, cancellationToken);
    }

    public async Task ImportWithCropAsync(CharacterCard character, BaseMaterialKind kind, string sourceFilePath, Rectangle crop, CancellationToken cancellationToken = default)
    {
        await Task.Run(() => _baseMaterialService.ImportWithCrop(character, kind, sourceFilePath, crop), cancellationToken);
        await RefreshAsync(character, cancellationToken);
    }

    public async Task ImportAtIndexAsync(
        CharacterCard character,
        BaseMaterialKind kind,
        string sourceFilePath,
        int index,
        CancellationToken cancellationToken = default)
    {
        await Task.Run(() => _baseMaterialService.ImportAtIndex(character, kind, sourceFilePath, index), cancellationToken);
        await RefreshAsync(character, cancellationToken);
    }

    public async Task ImportWithCropAtIndexAsync(
        CharacterCard character,
        BaseMaterialKind kind,
        string sourceFilePath,
        Rectangle crop,
        int index,
        CancellationToken cancellationToken = default)
    {
        await Task.Run(() => _baseMaterialService.ImportWithCropAtIndex(character, kind, sourceFilePath, crop, index), cancellationToken);
        await RefreshAsync(character, cancellationToken);
    }

    public async Task RepairWithCropAsync(CharacterCard character, BaseMaterialKind kind, string sourceFilePath, int index, Rectangle crop, CancellationToken cancellationToken = default)
    {
        await Task.Run(() => _baseMaterialService.RepairWithCrop(character, kind, sourceFilePath, index, crop), cancellationToken);
        await RefreshAsync(character, cancellationToken);
    }

    public async Task ImportVoiceAsync(
        CharacterCard character,
        VoiceMaterialKind kind,
        string sourceFilePath,
        CancellationToken cancellationToken = default)
    {
        await Task.Run(() => _voiceMaterialService.Import(character, kind, sourceFilePath), cancellationToken);
        await RefreshAsync(character, cancellationToken);
    }

    public async Task ImportVoicesAsync(
        CharacterCard character,
        VoiceMaterialKind kind,
        IReadOnlyList<string> sourceFilePaths,
        CancellationToken cancellationToken = default)
    {
        await Task.Run(() => _voiceMaterialService.ImportMany(character, kind, sourceFilePaths), cancellationToken);
        await RefreshAsync(character, cancellationToken);
    }

    public async Task ReplaceVoiceAsync(
        CharacterCard character,
        VoiceMaterialItem item,
        string sourceFilePath,
        CancellationToken cancellationToken = default)
    {
        await Task.Run(
            () => _voiceMaterialService.Replace(character, item.Kind, item.Index, sourceFilePath),
            cancellationToken);
        await RefreshAsync(character, cancellationToken);
    }

    public async Task DeleteVoiceAsync(
        CharacterCard character,
        VoiceMaterialItem item,
        CancellationToken cancellationToken = default)
    {
        await Task.Run(
            () => _voiceMaterialService.Delete(character, item),
            cancellationToken);
        await RefreshAsync(character, cancellationToken);
    }

    public async Task DeletePendingVoicesAsync(
        CharacterCard character,
        IReadOnlyList<VoiceMaterialItem> items,
        CancellationToken cancellationToken = default)
    {
        await Task.Run(
            () => _voiceMaterialService.DeletePendingVoices(character, items),
            cancellationToken);
        await RefreshAsync(character, cancellationToken);
    }

    public async Task MovePendingVoicesAsync(
        CharacterCard character,
        IReadOnlyList<VoiceMaterialItem> items,
        VoiceMaterialKind targetKind,
        CancellationToken cancellationToken = default)
    {
        await Task.Run(
            () => _voiceMaterialService.MovePendingVoices(character, items, targetKind),
            cancellationToken);
        await RefreshAsync(character, cancellationToken);
    }

    public async Task<IReadOnlyList<VoiceMaterialDuplicateMatch>> FindPendingVoiceDuplicatesAsync(
        CharacterCard character,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(
            () => _voiceMaterialService.FindPendingVoiceDuplicates(character),
            cancellationToken);
    }

    private void SetNotice(InfoBarSeverity severity, string title, string message)
    {
        NoticeSeverity = severity;
        NoticeTitle = title;
        NoticeMessage = message;
        IsNoticeOpen = true;
    }

    private static string BuildMaterialNoticeMessage(
        IReadOnlyList<BaseMaterialSection> sections,
        IReadOnlyList<VoiceMaterialSection> voiceSections,
        int missing,
        int invalid,
        int extra)
    {
        var missingDetails = sections
            .Select(section => new
            {
                section.Spec.DisplayName,
                section.MissingCount
            })
            .Where(item => item.MissingCount > 0)
            .Select(item => $"{item.DisplayName}缺 {item.MissingCount} 张")
            .ToArray();
        var invalidDetails = sections
            .Select(section => new
            {
                section.Spec.DisplayName,
                section.InvalidCount
            })
            .Where(item => item.InvalidCount > 0)
            .Select(item => $"{item.DisplayName} {item.InvalidCount} 个不合规")
            .ToArray();

        var extraDetails = sections
            .Where(section => section.ExtraCount > 0)
            .Select(section => $"{section.Spec.DisplayName}额外 {section.ExtraCount} 张")
            .ToArray();
        var missingVoiceDetails = voiceSections
            .Where(section => section.MissingCount > 0)
            .Select(section => $"{section.Spec.DisplayName}缺 {section.MissingCount} 个")
            .ToArray();
        var invalidVoiceDetails = voiceSections
            .Where(section => section.InvalidCount > 0)
            .Select(section => $"{section.Spec.DisplayName} {section.InvalidCount} 个不合规")
            .ToArray();
        var detailParts = missingDetails
            .Concat(invalidDetails)
            .Concat(extraDetails)
            .Concat(missingVoiceDetails)
            .Concat(invalidVoiceDetails)
            .ToArray();
        var detailsText = detailParts.Length > 0
            ? $"（{string.Join("；", detailParts)}）"
            : string.Empty;
        var summaryParts = new List<string>();
        if (missing > 0)
        {
            summaryParts.Add($"缺少素材 {missing} 个");
        }

        if (invalid > 0)
        {
            summaryParts.Add($"不合规素材 {invalid} 个");
        }

        if (extra > 0)
        {
            summaryParts.Add($"额外文件 {extra} 个");
        }

        return $"{string.Join("，", summaryParts)}{detailsText}。";
    }
}
