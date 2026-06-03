using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;
using CrossingVoidZDTool.Views;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CrossingVoidZDTool
{
    public sealed partial class MainWindow : Window
    {
        private const double PageEntranceOffsetX = -96;
        private static readonly TimeSpan PageEntranceDuration = TimeSpan.FromMilliseconds(280);
        private readonly AppSettingsService _appSettingsService = new();
        private readonly ProjectRootMigrationService _projectRootMigrationService = new();
        private readonly WinUiDialogService _dialogService;
        private AppSettings _appSettings = new();
        private string _projectRootPath = AppSettingsService.DefaultProjectRootPath;
        private bool _isChangingShellSelectionInternally;

        public MainWindow()
        {
            InitializeComponent();
            _dialogService = new WinUiDialogService(() => Content.XamlRoot);
            ApplyCustomTitleBar();
            ApplyWindowIcon();
            AppWindow.Resize(new SizeInt32(1500, 920));
            ApplyInitialWindowPlacement();

            _appSettings = _appSettingsService.Load();
            _projectRootPath = _appSettingsService.ResolveProjectRootPath(_appSettings);
            EnsureProjectRootDirectory(_projectRootPath);
            ShowCharacterDeskPage();
        }

        private void ApplyWindowIcon()
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath))
            {
                AppWindow.SetIcon(iconPath);
            }
        }

        private void ApplyCustomTitleBar()
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        }

        private void ApplyInitialWindowPlacement()
        {
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }
        }

        private void EnsureProjectRootDirectory(string projectRootPath)
        {
            _appSettingsService.EnsureProjectRootDirectory(projectRootPath);
            ProjectRootPathTextBox.Text = projectRootPath;
            ProjectRootStatusInfoBar.Message = $"已确认目录存在：{projectRootPath}";
            WorkspaceStatusText.Text = $"就绪：整体项目位置 {projectRootPath}";
        }

        private void SaveAppSettings()
        {
            _appSettingsService.Save(_appSettings);
        }

        private void ShellNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag)
            {
                return;
            }

            if (_isChangingShellSelectionInternally)
            {
                return;
            }

            if (string.Equals(tag, "Settings", StringComparison.Ordinal))
            {
                ShowSettingsPage();
                return;
            }

            if (string.Equals(tag, "ActionFrames", StringComparison.Ordinal))
            {
                ShowPlaceholderPage(ActionFramesPage);
                return;
            }

            if (string.Equals(tag, "LineArt", StringComparison.Ordinal))
            {
                ShowPlaceholderPage(LineArtPage);
                return;
            }

            if (string.Equals(tag, "UnrealSync", StringComparison.Ordinal))
            {
                ShowPlaceholderPage(UnrealSyncPage);
                return;
            }

            ShowCharacterDeskPage();
        }

        private void ShowCharacterDeskPage()
        {
            ShowOnlyPage(CharacterDeskPage);
            SelectShellNavigationItem(CharacterDeskNavItem);
        }

        private void ShowSettingsPage()
        {
            ShowOnlyPage(SettingsPage);
            SelectShellNavigationItem(GlobalSettingsNavItem);
        }

        private void ShowPlaceholderPage(FrameworkElement page)
        {
            ShowOnlyPage(page);
        }

        private void ShowOnlyPage(FrameworkElement visiblePage)
        {
            FrameworkElement[] pages =
            [
                CharacterDeskPage,
                ActionFramesPage,
                LineArtPage,
                UnrealSyncPage,
                SettingsPage
            ];

            foreach (var page in pages)
            {
                page.Visibility = ReferenceEquals(page, visiblePage)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            PlayPageEntrance(visiblePage);
        }

        private void SelectShellNavigationItem(NavigationViewItem item)
        {
            _isChangingShellSelectionInternally = true;
            try
            {
                ShellNavigation.SelectedItem = item;
                item.IsSelected = true;
            }
            finally
            {
                _isChangingShellSelectionInternally = false;
            }
        }

        private static void PlayPageEntrance(FrameworkElement page)
        {
            if (page.Visibility != Visibility.Visible)
            {
                return;
            }

            page.Transitions = null;
            page.Resources["PageEntranceStoryboard"] = null;

            if (page.RenderTransform is not TranslateTransform transform)
            {
                transform = new TranslateTransform();
                page.RenderTransform = transform;
            }

            transform.X = PageEntranceOffsetX;
            transform.Y = 0;
            page.Opacity = 0;

            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            var slideAnimation = new DoubleAnimation
            {
                From = PageEntranceOffsetX,
                To = 0,
                Duration = PageEntranceDuration,
                EasingFunction = easing
            };
            Storyboard.SetTarget(slideAnimation, transform);
            Storyboard.SetTargetProperty(slideAnimation, nameof(TranslateTransform.X));

            var fadeAnimation = new DoubleAnimation
            {
                From = 0.82,
                To = 1,
                Duration = PageEntranceDuration,
                EasingFunction = easing
            };
            Storyboard.SetTarget(fadeAnimation, page);
            Storyboard.SetTargetProperty(fadeAnimation, nameof(UIElement.Opacity));

            var storyboard = new Storyboard();
            storyboard.Children.Add(slideAnimation);
            storyboard.Children.Add(fadeAnimation);
            storyboard.Completed += (_, _) =>
            {
                transform.X = 0;
                transform.Y = 0;
                page.Opacity = 1;
                page.Resources.Remove("PageEntranceStoryboard");
            };
            page.Resources["PageEntranceStoryboard"] = storyboard;
            storyboard.Begin();
        }

        private async void ChooseProjectRootButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.ComputerFolder
            };
            picker.FileTypeFilter.Add("*");

            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            var selectedFolder = await picker.PickSingleFolderAsync();
            if (selectedFolder is null)
            {
                return;
            }

            var newProjectRootPath = _appSettingsService.BuildProjectRootPathFromParent(selectedFolder.Path);
            var oldProjectRootPath = Path.GetFullPath(_projectRootPath);

            if (PathsEqual(oldProjectRootPath, newProjectRootPath))
            {
                SetProjectRootStatus(InfoBarSeverity.Informational, "目录未变化", $"当前已经在使用：{newProjectRootPath}");
                return;
            }

            if (IsPathInsideDirectory(newProjectRootPath, oldProjectRootPath))
            {
                SetProjectRootStatus(InfoBarSeverity.Error, "无法迁移目录", "新位置不能放在旧项目总目录里面，否则迁移完成后删除旧目录时会连新目录一起删除。");
                return;
            }

            try
            {
                SetProjectRootStatus(InfoBarSeverity.Informational, "正在迁移目录", $"{oldProjectRootPath} -> {newProjectRootPath}");
                var result = await Task.Run(() => _projectRootMigrationService.Migrate(oldProjectRootPath, newProjectRootPath, CancellationToken.None));

                _projectRootPath = newProjectRootPath;
                _appSettings.ProjectRootPath = _projectRootPath;
                SaveAppSettings();
                EnsureProjectRootDirectory(_projectRootPath);
                SetProjectRootStatus(InfoBarSeverity.Success, "目录迁移完成", $"已迁移并校验 {result.FileCount} 个文件、{result.DirectoryCount} 个文件夹。旧目录已删除：{oldProjectRootPath}");
            }
            catch (Exception ex)
            {
                EnsureProjectRootDirectory(_projectRootPath);
                SetProjectRootStatus(InfoBarSeverity.Error, "目录迁移失败", $"已保留原目录和设置，未删除旧目录。错误：{ex.Message}");
            }
        }

        private async void ShowProjectRootHelpButton_Click(object sender, RoutedEventArgs e)
        {
            await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "整体项目位置说明",
                DialogContentFactory.CreateProjectRootHelpContent(),
                PrimaryButtonText: "关闭",
                CloseButtonText: string.Empty,
                DefaultButton: ContentDialogButton.Primary,
                ConfigureDialog: dialog =>
                {
                    dialog.MinWidth = 610;
                    dialog.MaxWidth = 610;
                }));
        }

        private void SetProjectRootStatus(InfoBarSeverity severity, string title, string message)
        {
            ProjectRootStatusInfoBar.Severity = severity;
            ProjectRootStatusInfoBar.Title = title;
            ProjectRootStatusInfoBar.Message = message;
            ProjectRootStatusInfoBar.IsOpen = true;
        }

        private static bool PathsEqual(string firstPath, string secondPath)
        {
            var first = Path.GetFullPath(firstPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var second = Path.GetFullPath(secondPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPathInsideDirectory(string path, string directoryPath)
        {
            var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var fullDirectory = Path.GetFullPath(directoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
        }
    }
}
