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

    // C6b：角色详情弹窗那五个按钮的命令。
    //
    // **为什么挂在卡片上**：详情面板的 DataContext 就是这张卡
    // （壳里 `CharacterDetailCard.DataContext = character;`），所以页面级路径
    // （`CharacterDesk.XxxCommand`）在那个作用域里根本解析不到——症状正是
    // 「按钮在、点不动、不报错」，冒烟里那条「已绑命令」的断言专门盯这个。
    // 这跟 `SequenceFrameSection.ManageFramesCommand` 是同一个形状：
    // 命令放**全局持有者**（`CharacterDetailCommands`），卡片只读它；
    // 只读计算属性不会把命令掺进 record 的相等性。

    /// <summary>「继续编辑」：恢复草稿（若已完成）→ 关弹窗 → 跳上次编辑的那一页。</summary>
    public System.Windows.Input.ICommand? ContinueEditingCommand =>
        CrossingVoidZDTool.ViewModels.CharacterDetailCommands.Current?.ContinueEditingCommand;

    /// <summary>「查看角色」：只读预览。</summary>
    public System.Windows.Input.ICommand? ViewCharacterCommand =>
        CrossingVoidZDTool.ViewModels.CharacterDetailCommands.Current?.ViewCharacterCommand;

    /// <summary>「导出角色」。</summary>
    public System.Windows.Input.ICommand? ExportCharacterCommand =>
        CrossingVoidZDTool.ViewModels.CharacterDetailCommands.Current?.ExportCharacterCommand;

    /// <summary>「打开角色目录」。</summary>
    public System.Windows.Input.ICommand? OpenCharacterFolderCommand =>
        CrossingVoidZDTool.ViewModels.CharacterDetailCommands.Current?.OpenCharacterFolderCommand;

    /// <summary>「前往虚幻同步台」。</summary>
    public System.Windows.Input.ICommand? GoToUnrealSyncCommand =>
        CrossingVoidZDTool.ViewModels.CharacterDetailCommands.Current?.GoToUnrealSyncCommand;
}

internal sealed record CharacterReferenceImage(
    string FileName,
    string FilePath,
    string FileUri,
    DateTime UpdatedAt);

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
