using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace CrossingVoidZDTool;

internal enum VoiceMaterialKind
{
    Formation,
    Click,
    Hurt,
    Death,
    Defeat,
    Victory,
    Skill1,
    Skill2,
    Ultimate,
    Support,
    Combo,
    Other
}

internal enum VoiceMaterialStatus
{
    Ready,
    Invalid
}

internal sealed record VoiceMaterialSpec(
    VoiceMaterialKind Kind,
    string DisplayName,
    string FolderName,
    string FileSuffix,
    bool IsRequired)
{
    public string RequirementText => IsRequired
        ? "支持多个，至少 1 个"
        : "支持多个，可留空";
}

internal sealed record VoiceMaterialItem(
    VoiceMaterialKind Kind,
    string DisplayName,
    string FilePath,
    string FileName,
    int Index,
    VoiceMaterialStatus Status,
    string StatusText,
    DateTime UpdatedAt) : INotifyPropertyChanged
{
    private string _duplicateStatusText = string.Empty;
    private string _usageText = string.Empty;

    public bool CanPlay => Status == VoiceMaterialStatus.Ready;

    public string DuplicateStatusText
    {
        get => _duplicateStatusText;
        private set
        {
            if (string.Equals(_duplicateStatusText, value, StringComparison.Ordinal))
            {
                return;
            }

            _duplicateStatusText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasDuplicateStatus));
        }
    }

    public bool HasDuplicateStatus => !string.IsNullOrWhiteSpace(DuplicateStatusText);

    public string UsageText
    {
        get => _usageText;
        private set
        {
            if (string.Equals(_usageText, value, StringComparison.Ordinal))
            {
                return;
            }

            _usageText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasUsageText));
        }
    }

    public bool HasUsageText => !string.IsNullOrWhiteSpace(UsageText);

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetDuplicateStatus(string? text)
    {
        DuplicateStatusText = text ?? string.Empty;
    }

    public void SetUsageText(string? text)
    {
        UsageText = text ?? string.Empty;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

internal sealed record VoiceMaterialDuplicateMatch(
    VoiceMaterialItem PendingItem,
    IReadOnlyList<VoiceMaterialItem> AssignedMatches,
    IReadOnlyList<VoiceMaterialItem> PendingMatches);

internal sealed record VoiceMaterialSection(
    VoiceMaterialSpec Spec,
    IReadOnlyList<VoiceMaterialItem> Items,
    string StatusText)
{
    public VoiceMaterialItem? PrimaryItem => Items.FirstOrDefault();

    public bool HasItems => PrimaryItem is not null;

    public int ReadyCount => Items.Count(item => item.Status == VoiceMaterialStatus.Ready);

    public int MissingCount => Spec.IsRequired && ReadyCount == 0 ? 1 : 0;

    public int InvalidCount => Items.Count(item => item.Status == VoiceMaterialStatus.Invalid);

    public int ExtraCount => 0;

    public bool HasWarning => MissingCount > 0 || InvalidCount > 0 || ExtraCount > 0;
}
