namespace CrossingVoidZDTool;

internal sealed class AppSettings
{
    public string? ProjectRootPath { get; set; }
}

internal sealed record MigrationResult(int FileCount, int DirectoryCount);
