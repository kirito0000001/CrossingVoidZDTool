namespace CrossingVoidZDTool;

internal sealed class AppSettings
{
    public string? ProjectRootPath { get; set; }

    public bool ShowWorkspacePath { get; set; }

    public bool NightModeEnabled { get; set; }

    public bool LogEnabled { get; set; }

    public bool LogUserOperations { get; set; } = true;

    public bool LogWarnings { get; set; } = true;

    public bool LogErrors { get; set; } = true;

    public string? CurrentCharacterCode { get; set; }

    public string? LastEditedCharacterCode { get; set; }

    public string? LastEditedModuleTag { get; set; }

    public string? UnrealEnginePath { get; set; }

    public string? UnrealProjectPath { get; set; }

    public bool BackupBeforeUnrealSync { get; set; } = true;
}

internal sealed record MigrationResult(int FileCount, int DirectoryCount);

internal enum LogKind
{
    Info,
    User,
    Warning,
    Error
}
