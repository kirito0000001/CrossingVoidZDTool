using System;
using System.Collections.Generic;
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
    private bool _showWorkspacePath = true;
    private bool _logEnabled = true;
    private bool _logUserOperations = true;
    private bool _logWarnings = true;
    private bool _logErrors = true;
    private bool _isLoadingSettings;
    private bool _isUndoingSetting;
    private string _settingUndoStatusText = "暂无可撤回的设置修改。";
    private readonly Stack<SettingUndoEntry> _settingUndoStack = new();

    public SettingsViewModel(
        AppSettingsService settingsService,
        ProjectRootMigrationService migrationService)
    {
        _settingsService = settingsService;
        _migrationService = migrationService;
        UndoLastSettingCommand = new RelayCommand(UndoLastSettingChange, () => CanUndoSettingChange);
    }

    public event EventHandler? AuxiliaryDisplayChanged;

    public event EventHandler? LogSettingsChanged;

    public RelayCommand UndoLastSettingCommand { get; }

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

    public bool ShowWorkspacePath
    {
        get => _showWorkspacePath;
        set => SetSettingProperty(
            ref _showWorkspacePath,
            value,
            nameof(ShowWorkspacePath),
            "显示工作区路径",
            () => _settings.ShowWorkspacePath = value,
            () => AuxiliaryDisplayChanged?.Invoke(this, EventArgs.Empty));
    }

    public bool LogEnabled
    {
        get => _logEnabled;
        set => SetSettingProperty(
            ref _logEnabled,
            value,
            nameof(LogEnabled),
            "开启 log 功能",
            () => _settings.LogEnabled = value,
            () => LogSettingsChanged?.Invoke(this, EventArgs.Empty));
    }

    public bool LogUserOperations
    {
        get => _logUserOperations;
        set => SetSettingProperty(
            ref _logUserOperations,
            value,
            nameof(LogUserOperations),
            "log 输出用户操作",
            () => _settings.LogUserOperations = value,
            () => LogSettingsChanged?.Invoke(this, EventArgs.Empty));
    }

    public bool LogWarnings
    {
        get => _logWarnings;
        set => SetSettingProperty(
            ref _logWarnings,
            value,
            nameof(LogWarnings),
            "log 输出提示",
            () => _settings.LogWarnings = value,
            () => LogSettingsChanged?.Invoke(this, EventArgs.Empty));
    }

    public bool LogErrors
    {
        get => _logErrors;
        set => SetSettingProperty(
            ref _logErrors,
            value,
            nameof(LogErrors),
            "log 输出错误提示",
            () => _settings.LogErrors = value,
            () => LogSettingsChanged?.Invoke(this, EventArgs.Empty));
    }

    public bool CanUndoSettingChange => _settingUndoStack.Count > 0;

    public string SettingUndoStatusText
    {
        get => _settingUndoStatusText;
        private set => SetProperty(ref _settingUndoStatusText, value);
    }

    public void LoadAndEnsureProjectRoot()
    {
        _settings = _settingsService.Load();
        ProjectRootPath = _settingsService.ResolveProjectRootPath(_settings);
        LoadBindableSettings(_settings);
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

    public bool ShouldWriteLog(LogKind kind)
    {
        if (!LogEnabled)
        {
            return false;
        }

        return kind switch
        {
            LogKind.User => LogUserOperations,
            LogKind.Warning => LogWarnings,
            LogKind.Error => LogErrors,
            _ => true
        };
    }

    private bool SetSettingProperty(
        ref bool field,
        bool value,
        string propertyName,
        string displayName,
        Action updateSettings,
        Action notifyChanged)
    {
        var oldValue = field;
        if (!SetProperty(ref field, value, propertyName))
        {
            return false;
        }

        updateSettings();
        if (!_isLoadingSettings)
        {
            if (!_isUndoingSetting)
            {
                _settingUndoStack.Push(new SettingUndoEntry(propertyName, displayName, oldValue, value));
                RefreshUndoState($"可撤回：{displayName} {FormatBool(value)}。");
            }

            Save();
            notifyChanged();
        }

        return true;
    }

    private void UndoLastSettingChange()
    {
        if (_settingUndoStack.Count == 0)
        {
            return;
        }

        var entry = _settingUndoStack.Pop();
        _isUndoingSetting = true;
        try
        {
            ApplyUndoEntry(entry);
            Save();
        }
        finally
        {
            _isUndoingSetting = false;
        }

        RefreshUndoState(_settingUndoStack.Count == 0
            ? $"已撤回：{entry.DisplayName}，暂无更多可撤回设置。"
            : $"已撤回：{entry.DisplayName}，还可继续撤回。");
    }

    private void ApplyUndoEntry(SettingUndoEntry entry)
    {
        switch (entry.PropertyName)
        {
            case nameof(ShowWorkspacePath):
                ShowWorkspacePath = entry.OldValue;
                AuxiliaryDisplayChanged?.Invoke(this, EventArgs.Empty);
                break;
            case nameof(LogEnabled):
                LogEnabled = entry.OldValue;
                LogSettingsChanged?.Invoke(this, EventArgs.Empty);
                AuxiliaryDisplayChanged?.Invoke(this, EventArgs.Empty);
                break;
            case nameof(LogUserOperations):
                LogUserOperations = entry.OldValue;
                LogSettingsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case nameof(LogWarnings):
                LogWarnings = entry.OldValue;
                LogSettingsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case nameof(LogErrors):
                LogErrors = entry.OldValue;
                LogSettingsChanged?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private void RefreshUndoState(string statusText)
    {
        SettingUndoStatusText = statusText;
        OnPropertyChanged(nameof(CanUndoSettingChange));
        UndoLastSettingCommand.NotifyCanExecuteChanged();
    }

    private void Save()
    {
        _settingsService.Save(_settings);
    }

    private void LoadBindableSettings(AppSettings settings)
    {
        _isLoadingSettings = true;
        try
        {
            ShowWorkspacePath = settings.ShowWorkspacePath;
            LogEnabled = settings.LogEnabled;
            LogUserOperations = settings.LogUserOperations;
            LogWarnings = settings.LogWarnings;
            LogErrors = settings.LogErrors;
        }
        finally
        {
            _isLoadingSettings = false;
        }

        RefreshUndoState("暂无可撤回的设置修改。");
    }

    private static string FormatBool(bool value)
    {
        return value ? "已开启" : "已关闭";
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

    private sealed record SettingUndoEntry(string PropertyName, string DisplayName, bool OldValue, bool NewValue);
}
