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
    private string _statusText = "就绪：等待选择角色。";
    private string _noticeTitle = "未选择角色";
    private string _noticeMessage = "请先在零境角色台选择当前制作角色。";
    private InfoBarSeverity _noticeSeverity = InfoBarSeverity.Informational;
    private bool _isNoticeOpen = true;
    private bool _isLoading;

    public LineArtViewModel(BaseMaterialService baseMaterialService)
    {
        _baseMaterialService = baseMaterialService;
    }

    public ObservableCollection<BaseMaterialSection> Sections { get; } = [];

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
        Sections.Clear();
        if (character is null)
        {
            SetNotice(InfoBarSeverity.Informational, "未选择角色", "请先在零境角色台选择当前制作角色。");
            StatusText = "就绪：等待选择角色。";
            return;
        }

        IsLoading = true;
        try
        {
            var sections = await Task.Run(() => _baseMaterialService.LoadSections(character, cancellationToken), cancellationToken);
            foreach (var section in sections)
            {
                Sections.Add(section);
                await Task.Yield();
            }

            var missing = sections.Sum(section => System.Math.Max(0, section.Spec.MinimumCount - section.Items.Count));
            var invalid = sections.Sum(section => section.Items.Count(item => item.Status == BaseMaterialStatus.Invalid));
            if (missing > 0 || invalid > 0)
            {
                SetNotice(
                    InfoBarSeverity.Warning,
                    "基础素材需要处理",
                    BuildMaterialNoticeMessage(sections, missing, invalid));
            }
            else
            {
                SetNotice(InfoBarSeverity.Success, "基础素材已就绪", "基础素材数量和尺寸都已通过检查。");
            }

            StatusText = $"已检查基础素材：{character.Name}";
        }
        finally
        {
            IsLoading = false;
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

    public async Task RepairWithCropAsync(CharacterCard character, BaseMaterialKind kind, string sourceFilePath, int index, Rectangle crop, CancellationToken cancellationToken = default)
    {
        await Task.Run(() => _baseMaterialService.RepairWithCrop(character, kind, sourceFilePath, index, crop), cancellationToken);
        await RefreshAsync(character, cancellationToken);
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
        int missing,
        int invalid)
    {
        var missingDetails = sections
            .Select(section => new
            {
                section.Spec.DisplayName,
                MissingCount = System.Math.Max(0, section.Spec.MinimumCount - section.Items.Count)
            })
            .Where(item => item.MissingCount > 0)
            .Select(item => $"{item.DisplayName}缺 {item.MissingCount} 张")
            .ToArray();
        var invalidDetails = sections
            .Select(section => new
            {
                section.Spec.DisplayName,
                InvalidCount = section.Items.Count(item => item.Status == BaseMaterialStatus.Invalid)
            })
            .Where(item => item.InvalidCount > 0)
            .Select(item => $"{item.DisplayName} {item.InvalidCount} 个不合规")
            .ToArray();

        var detailParts = missingDetails.Concat(invalidDetails).ToArray();
        var detailsText = detailParts.Length > 0
            ? $"（{string.Join("；", detailParts)}）"
            : string.Empty;
        return $"缺少素材 {missing} 张，不合规素材 {invalid} 个{detailsText}。右键素材卡可修复。";
    }
}
