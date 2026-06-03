using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;
using Microsoft.UI.Xaml.Controls;

namespace CrossingVoidZDTool.ViewModels;

internal sealed class SettingsViewModel : ObservableObject
{
    private readonly AppSettingsService _settingsService;
    private readonly ProjectRootMigrationService _migrationService;
    private AppSettings _settings = new();
    private string _projectRootPath = AppSettingsService.DefaultProjectRootPath;
    private string _projectRootStatusTitle = "目录已就绪";
    private string _projectRootStatusMessage = string.Empty;
    private InfoBarSeverity _projectRootStatusSeverity = InfoBarSeverity.Success;

    public SettingsViewModel(
        AppSettingsService settingsService,
        ProjectRootMigrationService migrationService)
    {
        _settingsService = settingsService;
        _migrationService = migrationService;
    }

    public string ProjectRootPath
    {
        get => _projectRootPath;
        private set
        {
            if (SetProperty(ref _projectRootPath, value))
            {
                OnPropertyChanged(nameof(WorkspaceStatusText));
            }
        }
    }

    public string WorkspaceStatusText => $"就绪：整体项目位置 {ProjectRootPath}";

    public string ProjectRootStatusTitle
    {
        get => _projectRootStatusTitle;
        private set => SetProperty(ref _projectRootStatusTitle, value);
    }

    public string ProjectRootStatusMessage
    {
        get => _projectRootStatusMessage;
        private set => SetProperty(ref _projectRootStatusMessage, value);
    }

    public InfoBarSeverity ProjectRootStatusSeverity
    {
        get => _projectRootStatusSeverity;
        private set => SetProperty(ref _projectRootStatusSeverity, value);
    }

    public void LoadAndEnsureProjectRoot()
    {
        _settings = _settingsService.Load();
        ProjectRootPath = _settingsService.ResolveProjectRootPath(_settings);
        EnsureCurrentProjectRoot();
    }

    public string BuildProjectRootPathFromParent(string parentPath)
    {
        return _settingsService.BuildProjectRootPathFromParent(parentPath);
    }

    public bool IsCurrentProjectRoot(string candidateProjectRootPath)
    {
        return PathsEqual(ProjectRootPath, candidateProjectRootPath);
    }

    public bool IsCandidateInsideCurrentRoot(string candidateProjectRootPath)
    {
        return IsPathInsideDirectory(candidateProjectRootPath, ProjectRootPath);
    }

    public void EnsureCurrentProjectRoot()
    {
        _settingsService.EnsureProjectRootDirectory(ProjectRootPath);
        SetProjectRootStatus(InfoBarSeverity.Success, "目录已就绪", $"已确认目录存在：{ProjectRootPath}");
    }

    public void SetProjectRootStatus(InfoBarSeverity severity, string title, string message)
    {
        ProjectRootStatusSeverity = severity;
        ProjectRootStatusTitle = title;
        ProjectRootStatusMessage = message;
    }

    public async Task<MigrationResult> ChangeProjectRootAsync(
        string newProjectRootPath,
        IProgress<ProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var oldProjectRootPath = Path.GetFullPath(ProjectRootPath);
        var result = await Task.Run(
            () => _migrationService.Migrate(
                oldProjectRootPath,
                newProjectRootPath,
                progress,
                cancellationToken),
            cancellationToken);

        ProjectRootPath = newProjectRootPath;
        _settings.ProjectRootPath = ProjectRootPath;
        _settingsService.Save(_settings);
        EnsureCurrentProjectRoot();
        return result;
    }

    private static bool PathsEqual(string firstPath, string secondPath)
    {
        var firstFullPath = Path.GetFullPath(firstPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var secondFullPath = Path.GetFullPath(secondPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(firstFullPath, secondFullPath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPathInsideDirectory(string candidatePath, string parentDirectoryPath)
    {
        var candidateFullPath = Path.GetFullPath(candidatePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parentFullPath = Path.GetFullPath(parentDirectoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return candidateFullPath.StartsWith(
            parentFullPath + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }
}
