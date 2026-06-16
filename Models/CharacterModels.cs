using System;

namespace CrossingVoidZDTool;

internal sealed class CharacterMetadata
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsCompleted { get; set; }

    public DateTime LastEditedAt { get; set; } = DateTime.Now;
}

internal sealed record CharacterCard(
    string Code,
    string Name,
    string FolderPath,
    string ToolFolderPath,
    string ReferenceFolderPath,
    DateTime LastEditedAt,
    bool IsCompleted,
    string DisplayName,
    string CoverUri)
{
    public string EffectiveDisplayName => string.IsNullOrWhiteSpace(DisplayName) ? Code : DisplayName;

    public string StatusDisplayText => $"{Code} / {EffectiveDisplayName}";

    public string CompletionStateText => IsCompleted ? "已完成" : "草稿";

    public string EffectiveCoverUri => string.IsNullOrWhiteSpace(CoverUri)
        ? "ms-appx:///Assets/DefaultPortrait.png"
        : CoverUri;
}

internal sealed record CharacterReferenceImage(
    string FileName,
    string FilePath,
    string FileUri,
    DateTime UpdatedAt);

internal sealed record CharacterPortraitEntry(string Name, string Code, string CoverUri)
{
    public string EffectiveDisplayName => string.IsNullOrWhiteSpace(Name) ? Code : Name;

    public string EffectiveCoverUri => string.IsNullOrWhiteSpace(CoverUri)
        ? "ms-appx:///Assets/DefaultPortrait.png"
        : CoverUri;
}

internal sealed record CharacterCreationResult(CharacterCard Character, bool CreatedNewFolder);

internal sealed record CharacterBackupEntry(
    string Path,
    DateTime CreatedAt,
    long SizeBytes,
    string Note,
    string DisplayName,
    string Kind)
{
    public bool IsAutomatic => string.Equals(Kind, CharacterBackupKinds.Automatic, StringComparison.OrdinalIgnoreCase);

    public string KindDisplayText => IsAutomatic ? "自动备份" : "手动备份";
}

internal sealed record CharacterBackupProgress(
    string Message,
    double Percent,
    int CompletedFiles,
    int TotalFiles,
    long CompletedBytes,
    long TotalBytes,
    string? CurrentRelativePath);

internal sealed class CharacterBackupMeta
{
    public DateTime CreatedAt { get; set; }

    public string? Note { get; set; }

    public string? Kind { get; set; }
}

internal static class CharacterBackupKinds
{
    public const string Manual = "Manual";

    public const string Automatic = "Automatic";
}
