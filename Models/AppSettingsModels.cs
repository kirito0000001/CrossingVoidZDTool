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

    /// <summary>
    /// 指定的 Python 解释器路径。**留空就用工具箱内置的那个**
    /// （<c>Tools/Atlas/python/python.exe</c>），这是正常情况。
    ///
    /// 留着这个口子是为了「我想用自己的 Python」——比如内置的包坏了、
    /// 或者想用带更多库的那一个。填目录或直接填 python.exe 都认。
    /// </summary>
    public string? AtlasPythonPath { get; set; }

    /// <summary>
    /// 图的渲染方式。Unreal 的 Paper2D 走 nearest，用别的会让像素画糊掉。
    /// 跟图集无关，放这里只是因为它是「导出相关」的设置项。
    /// </summary>
    public bool AtlasUseNearestNeighbor { get; set; } = true;
}

internal sealed record MigrationResult(int FileCount, int DirectoryCount);

internal enum LogKind
{
    Info,
    User,
    Warning,
    Error
}
