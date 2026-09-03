using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace CrossingVoidZDTool;

internal sealed record SequenceFrameAction(
    string DisplayName,
    string Code,
    bool IsSkill,
    bool IsCombo = false,
    int FormIndex = 1);

internal enum SequenceFrameInsertPosition
{
    Before,
    After
}

internal enum SequenceEditorPlaybackMode
{
    Once,
    Loop
}

internal enum SequenceVoiceSyncStatusKind
{
    Unavailable,
    Aligned,
    AnimationShorter,
    AnimationLonger,
    Interrupted
}

internal sealed record SequenceVoiceSyncResult(
    int FrameIndex,
    string VoiceFilePath,
    string VoiceFileName,
    TimeSpan? VoiceDuration,
    int RequiredFrames,
    int AvailableFrames,
    int FramesPerSecond,
    int DifferenceFrames,
    SequenceVoiceSyncStatusKind StatusKind,
    int BoundaryFrameIndex,
    int SuggestedFrameIndex,
    int SuggestedOriginalDurationFrames,
    int SuggestedDurationFrames)
{
    private double AvailableSeconds => AvailableFrames / (double)Math.Max(1, FramesPerSecond);

    private double DifferenceSeconds => Math.Abs(DifferenceFrames) / (double)Math.Max(1, FramesPerSecond);

    public string DurationText => VoiceDuration is null
        ? "有效语音：无法读取"
        : $"有效语音：{VoiceDuration.Value.TotalSeconds:0.###} 秒 / {RequiredFrames} 格";

    public string AvailableText => BoundaryFrameIndex > 0
        ? $"可用长度：{AvailableFrames} 格 / {AvailableSeconds:0.###} 秒（到第 {BoundaryFrameIndex} 帧下一条语音）"
        : $"可用长度：{AvailableFrames} 格 / {AvailableSeconds:0.###} 秒（到序列结束）";

    public string StatusText => StatusKind switch
    {
        SequenceVoiceSyncStatusKind.Unavailable => "同步结果：无法读取 WAV 时长",
        SequenceVoiceSyncStatusKind.Aligned => "同步结果：刚好对齐",
        SequenceVoiceSyncStatusKind.AnimationShorter => $"同步结果：动画少 {DifferenceFrames} 格 / {DifferenceSeconds:0.###} 秒",
        SequenceVoiceSyncStatusKind.AnimationLonger => $"同步结果：动画多 {Math.Abs(DifferenceFrames)} 格 / {DifferenceSeconds:0.###} 秒",
        SequenceVoiceSyncStatusKind.Interrupted => $"同步结果：被第 {BoundaryFrameIndex} 帧语音顶替，还差 {DifferenceFrames} 格 / {DifferenceSeconds:0.###} 秒",
        _ => string.Empty
    };

    public string BadgeText => StatusKind switch
    {
        SequenceVoiceSyncStatusKind.Unavailable => "时长未知",
        SequenceVoiceSyncStatusKind.Aligned => "对齐",
        SequenceVoiceSyncStatusKind.AnimationShorter => $"少 {DifferenceFrames} 格",
        SequenceVoiceSyncStatusKind.AnimationLonger => $"多 {Math.Abs(DifferenceFrames)} 格",
        SequenceVoiceSyncStatusKind.Interrupted => $"被顶替 · 剩 {DifferenceFrames} 格",
        _ => string.Empty
    };

    public bool HasSuggestion => DifferenceFrames > 0 && StatusKind != SequenceVoiceSyncStatusKind.Unavailable;

    public string SuggestionText => HasSuggestion
        ? $"建议：第 {SuggestedFrameIndex} 帧由 {SuggestedOriginalDurationFrames} 格调整为 {SuggestedDurationFrames} 格"
        : string.Empty;
}

internal sealed record SequenceFrameItem(
    string FilePath,
    string FileUri,
    string FileName,
    string CacheKey,
    int Index,
    int ActualWidth,
    int ActualHeight,
    bool IsValid,
    DateTime UpdatedAt,
    bool IsBlank = false,
    int DurationFrames = 1,
    string VoiceFilePath = "",
    string VoiceFileName = "",
    int ReuseCount = 1,
    int ReuseOccurrence = 1,
    int ReuseSourceIndex = 0,
    string ReusePositionsText = "",
    int ReuseColorIndex = -1,
    SequenceVoiceSyncResult? VoiceSyncResult = null,
    string SyncId = "")
{
    public string DisplayName => IsBlank ? "空白帧" : FileName;

    public string PlainFileName => IsBlank
        ? "空白帧"
        : FileName.Length > 5 && char.IsDigit(FileName[0]) && char.IsDigit(FileName[1]) && char.IsDigit(FileName[2]) && FileName[3] == ' ' && FileName[4] == ' '
            ? FileName[5..]
            : FileName;

    public Visibility BlankVisibility => IsBlank ? Visibility.Visible : Visibility.Collapsed;

    public bool HasVoice => !string.IsNullOrWhiteSpace(VoiceFilePath);

    public Visibility VoiceVisibility => HasVoice ? Visibility.Visible : Visibility.Collapsed;

    public Visibility VoiceSyncVisibility => VoiceSyncResult is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility VoiceSyncSuggestionVisibility => VoiceSyncResult?.HasSuggestion == true
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string VoiceSyncDurationText => VoiceSyncResult?.DurationText ?? string.Empty;

    public string VoiceSyncAvailableText => VoiceSyncResult?.AvailableText ?? string.Empty;

    public string VoiceSyncStatusText => VoiceSyncResult?.StatusText ?? string.Empty;

    public string VoiceSyncSuggestionText => VoiceSyncResult?.SuggestionText ?? string.Empty;

    public string VoiceSyncBadgeText => VoiceSyncResult?.BadgeText ?? string.Empty;

    public SolidColorBrush VoiceSyncBrush => new(GetVoiceSyncColor(VoiceSyncResult?.StatusKind, 255));

    public SolidColorBrush VoiceSyncTintBrush => new(GetVoiceSyncColor(VoiceSyncResult?.StatusKind, 38));

    public string DurationText => $"{DurationFrames} 格";

    public double TimelineWidth => Math.Clamp(128d + (DurationFrames - 1) * 24d, 128d, 272d);

    public bool HasReuse => ReuseCount > 1;

    public Visibility ReuseVisibility => HasReuse ? Visibility.Visible : Visibility.Collapsed;

    public string ReuseBadgeText => !HasReuse
        ? string.Empty
        : ReuseOccurrence <= 1
            ? $"共 {ReuseCount} 次"
            : $"第 {ReuseOccurrence} 次";

    public string ReuseCurrentUsageText => ReuseOccurrence <= 1
        ? "本帧：首次使用"
        : $"本帧：第 {ReuseOccurrence} 次使用";

    public string ReuseSourceText => $"首次出现：第 {ReuseSourceIndex} 帧";

    public string ReuseAllPositionsText => $"全部位置：{ReusePositionsText}";

    public SolidColorBrush ReuseBrush => new(GetReuseColor(ReuseColorIndex, 255));

    public SolidColorBrush ReuseTintBrush => new(GetReuseColor(ReuseColorIndex, 38));

    private static Color GetReuseColor(int colorIndex, byte alpha)
    {
        var normalized = colorIndex < 0 ? 0 : colorIndex % 6;
        var (red, green, blue) = normalized switch
        {
            0 => ((byte)79, (byte)140, (byte)255),
            1 => ((byte)208, (byte)138, (byte)32),
            2 => ((byte)47, (byte)158, (byte)98),
            3 => ((byte)201, (byte)92, (byte)147),
            4 => ((byte)216, (byte)92, (byte)85),
            _ => ((byte)123, (byte)115, (byte)209)
        };
        return Color.FromArgb(alpha, red, green, blue);
    }

    private static Color GetVoiceSyncColor(SequenceVoiceSyncStatusKind? statusKind, byte alpha)
    {
        var (red, green, blue) = statusKind switch
        {
            SequenceVoiceSyncStatusKind.Aligned => ((byte)47, (byte)158, (byte)98),
            SequenceVoiceSyncStatusKind.AnimationLonger => ((byte)79, (byte)140, (byte)255),
            SequenceVoiceSyncStatusKind.AnimationShorter => ((byte)208, (byte)138, (byte)32),
            SequenceVoiceSyncStatusKind.Interrupted => ((byte)216, (byte)92, (byte)85),
            _ => ((byte)128, (byte)128, (byte)128)
        };
        return Color.FromArgb(alpha, red, green, blue);
    }
}

internal sealed record SequenceFrameSection(
    SequenceFrameAction Action,
    IReadOnlyList<SequenceFrameItem> Frames,
    string StatusText,
    bool HasWarning)
{
    public IReadOnlyList<SequenceFrameItem> PreviewFrames => Frames.Take(3).ToList();

    public bool HasOverflowFrames => Frames.Count > PreviewFrames.Count;

    public string OverflowText => Frames.Count > PreviewFrames.Count
        ? $"+{Frames.Count - PreviewFrames.Count}"
        : "...";
}

internal sealed record SequenceFrameSectionGroup(
    string Title,
    string Description,
    IReadOnlyList<SequenceFrameSection> Sections);

internal sealed record SequenceFrameCollectionItem(
    string Title,
    string DisplayName,
    string FilePath,
    string FileUri,
    string FileName,
    string ContentHash,
    string StatusText,
    string UsageText,
    int UsageCount,
    bool HasDuplicate,
    string DuplicateText) : INotifyPropertyChanged
{
    private int _selectionOrder;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool HasReuse => UsageCount > 1;

    public Visibility ReuseVisibility => HasReuse ? Visibility.Visible : Visibility.Collapsed;

    public Visibility DuplicateVisibility => HasDuplicate ? Visibility.Visible : Visibility.Collapsed;

    public int SelectionOrder
    {
        get => _selectionOrder;
        set
        {
            if (_selectionOrder == value)
            {
                return;
            }

            _selectionOrder = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectionOrderText));
            OnPropertyChanged(nameof(SelectionOrderVisibility));
        }
    }

    public string SelectionOrderText => SelectionOrder > 0 ? SelectionOrder.ToString() : string.Empty;

    public Visibility SelectionOrderVisibility => SelectionOrder > 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

internal sealed record SequenceFrameVoiceOption(string DisplayName, string FilePath);

internal sealed record SequenceFrameVoiceUsage(
    string ActionCode,
    string ActionDisplayName,
    int FrameIndex)
{
    public string DisplayText => $"{ActionDisplayName} #{FrameIndex}";
}

internal sealed class SequenceFramesData
{
    public ObservableCollection<SequenceFrameActionSettings> ActionSettings { get; set; } = [];

    public string DuplicateCheckSignature { get; set; } = string.Empty;

    public int DuplicateCheckMaterialCount { get; set; } = -1;

    public int DuplicateCheckDuplicateCount { get; set; }

    public Dictionary<string, SequenceFrameDuplicateHashEntry> DuplicateContentHashes { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class SequenceFrameDuplicateHashEntry
{
    public string ContentHash { get; set; } = string.Empty;

    public long FileLength { get; set; }

    public long LastWriteTimeUtcTicks { get; set; }
}

internal sealed class SequenceFrameActionSettings
{
    public string ActionCode { get; set; } = string.Empty;

    public int Fps { get; set; } = 12;
}

internal sealed class SequenceFrameManifest
{
    public int SchemaVersion { get; set; } = 3;

    public string ActionCode { get; set; } = string.Empty;

    public int Fps { get; set; } = 12;

    public Collection<SequenceFrameManifestEntry> Frames { get; set; } = [];
}

internal sealed class SequenceFrameManifestEntry
{
    public string SyncId { get; set; } = Guid.NewGuid().ToString("N");

    public string RelativePath { get; set; } = string.Empty;

    public bool IsBlank { get; set; }

    public int DurationFrames { get; set; } = 1;

    public string VoiceRelativePath { get; set; } = string.Empty;
}

internal sealed record ExternalSequenceFrameSource(string FilePath, bool IsBlank);
