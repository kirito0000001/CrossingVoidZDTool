namespace CrossingVoidZDTool;

internal sealed class AppSettings
{
    public string? ProjectRootPath { get; set; }

    public bool ShowWorkspacePath { get; set; } = true;

    public bool LogEnabled { get; set; } = true;

    public bool LogUserOperations { get; set; } = true;

    public bool LogWarnings { get; set; } = true;

    public bool LogErrors { get; set; } = true;
}

internal sealed record MigrationResult(int FileCount, int DirectoryCount);

internal enum LogKind
{
    Info,
    User,
    Warning,
    Error
}
