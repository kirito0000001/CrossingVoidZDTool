using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;

namespace CrossingVoidZDTool;

internal sealed record SequenceFrameAction(
    string DisplayName,
    string Code,
    bool IsSkill,
    bool IsCombo = false,
    int FormIndex = 1);

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
    bool IsBlank = false)
{
    public string DisplayName => IsBlank ? "空白帧" : FileName;

    public string PlainFileName => IsBlank
        ? "空白帧"
        : FileName.Length > 5 && char.IsDigit(FileName[0]) && char.IsDigit(FileName[1]) && char.IsDigit(FileName[2]) && FileName[3] == ' ' && FileName[4] == ' '
            ? FileName[5..]
            : FileName;

    public Visibility BlankVisibility => IsBlank ? Visibility.Visible : Visibility.Collapsed;
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
    bool HasDuplicate,
    string DuplicateText)
{
    public Visibility DuplicateVisibility => HasDuplicate ? Visibility.Visible : Visibility.Collapsed;
}

internal sealed class SequenceFramesData
{
    public ObservableCollection<SequenceFrameActionSettings> ActionSettings { get; set; } = [];

    public string DuplicateCheckSignature { get; set; } = string.Empty;
}

internal sealed class SequenceFrameActionSettings
{
    public string ActionCode { get; set; } = string.Empty;

    public int Fps { get; set; } = 12;
}

internal sealed class SequenceFrameManifest
{
    public int SchemaVersion { get; set; } = 1;

    public string ActionCode { get; set; } = string.Empty;

    public int Fps { get; set; } = 12;

    public Collection<SequenceFrameManifestEntry> Frames { get; set; } = [];
}

internal sealed class SequenceFrameManifestEntry
{
    public string RelativePath { get; set; } = string.Empty;

    public bool IsBlank { get; set; }
}

internal sealed record ExternalSequenceFrameSource(string FilePath, bool IsBlank);
