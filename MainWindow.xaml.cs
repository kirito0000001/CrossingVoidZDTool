using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;
using CrossingVoidZDTool.ViewModels;
using CrossingVoidZDTool.Views;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Microsoft.UI.Dispatching;
using WinRT.Interop;
using PathFigure = Microsoft.UI.Xaml.Media.PathFigure;

namespace CrossingVoidZDTool
{
    public sealed partial class MainWindow : Window
    {
        private const double PageEntranceOffsetX = -96;
        private static readonly TimeSpan PageEntranceDuration = TimeSpan.FromMilliseconds(280);
        private readonly SettingsViewModel _settingsViewModel = new(new AppSettingsService(), new ProjectRootMigrationService());
        private readonly WinUiDialogService _dialogService;
        private readonly Stopwatch _globalProgressStopwatch = new();
        private readonly DispatcherQueueTimer _globalProgressElapsedTimer;
        private bool _isChangingShellSelectionInternally;
        private bool _isGlobalProgressVisible;
        private string _globalProgressOperationTitle = string.Empty;
        private double _globalProgressLastPercent;
        private CancellationTokenSource? _globalProgressCancellation;

        public MainWindow()
        {
            InitializeComponent();
            RootGrid.DataContext = _settingsViewModel;
            _dialogService = new WinUiDialogService(() => Content.XamlRoot);
            ApplyCustomTitleBar();
            ApplyWindowIcon();
            AppWindow.Resize(new SizeInt32(1500, 920));
            ApplyInitialWindowPlacement();
            _globalProgressElapsedTimer = DispatcherQueue.CreateTimer();
            _globalProgressElapsedTimer.Interval = TimeSpan.FromSeconds(1);
            _globalProgressElapsedTimer.Tick += GlobalProgressElapsedTimer_Tick;

            _settingsViewModel.LoadAndEnsureProjectRoot();
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

            var newProjectRootPath = _settingsViewModel.BuildProjectRootPathFromParent(selectedFolder.Path);
            var oldProjectRootPath = Path.GetFullPath(_settingsViewModel.ProjectRootPath);

            if (_settingsViewModel.IsCurrentProjectRoot(newProjectRootPath))
            {
                _settingsViewModel.SetProjectRootStatus(InfoBarSeverity.Informational, "目录未变化", $"当前已经在使用：{newProjectRootPath}");
                return;
            }

            if (_settingsViewModel.IsCandidateInsideCurrentRoot(newProjectRootPath))
            {
                _settingsViewModel.SetProjectRootStatus(InfoBarSeverity.Error, "无法迁移目录", "新位置不能放在旧项目总目录里面，否则迁移完成后删除旧目录时会连新目录一起删除。");
                return;
            }

            try
            {
                _settingsViewModel.SetProjectRootStatus(InfoBarSeverity.Informational, "正在迁移目录", $"{oldProjectRootPath} -> {newProjectRootPath}");
                ShowGlobalProgress("迁移整体项目目录", newProjectRootPath);
                UpdateGlobalProgress("正在复制和校验项目文件...", 5, $"{oldProjectRootPath} -> {newProjectRootPath}");
                var progress = new Progress<ProgressUpdate>(update =>
                    UpdateGlobalProgress(update.Message, update.Percent, update.Detail, update.IsIndeterminate));
                var result = await _settingsViewModel.ChangeProjectRootAsync(
                    newProjectRootPath,
                    progress,
                    GetGlobalProgressCancellationToken());

                CompleteGlobalProgress("目录迁移完成", $"已迁移 {result.FileCount} 个文件、{result.DirectoryCount} 个文件夹");
                await HideGlobalProgressAfterDelayAsync();
                _settingsViewModel.SetProjectRootStatus(InfoBarSeverity.Success, "目录迁移完成", $"已迁移并校验 {result.FileCount} 个文件、{result.DirectoryCount} 个文件夹。旧目录已删除：{oldProjectRootPath}");
            }
            catch (Exception ex)
            {
                CompleteGlobalProgress(ex is OperationCanceledException ? "目录迁移已取消" : "目录迁移失败", ex.Message);
                await HideGlobalProgressAfterDelayAsync();
                _settingsViewModel.EnsureCurrentProjectRoot();
                _settingsViewModel.SetProjectRootStatus(InfoBarSeverity.Error, "目录迁移失败", $"已保留原目录和设置，未删除旧目录。错误：{ex.Message}");
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

        private void ShowGlobalProgress(string title, string detail)
        {
            _globalProgressCancellation?.Dispose();
            _globalProgressCancellation = new CancellationTokenSource();
            _globalProgressOperationTitle = title;
            _globalProgressStopwatch.Restart();
            _globalProgressElapsedTimer.Start();
            _isGlobalProgressVisible = true;

            GlobalProgressHost.Visibility = Visibility.Visible;
            GlobalProgressTitleText.Text = detail;
            GlobalProgressDetailText.Text = title;
            GlobalProgressElapsedText.Text = FormatElapsedTime(TimeSpan.Zero);
            GlobalProgressPercentText.Text = "0%";
            GlobalProgressBar.IsIndeterminate = true;
            GlobalProgressBar.Value = 0;
            _globalProgressLastPercent = 0;
            UpdateGlobalProgressRing(0);
            AnimateGlobalProgressHost(show: true);
        }

        private void UpdateGlobalProgress(string message, double percent, string? detail = null, bool isIndeterminate = false)
        {
            if (!_isGlobalProgressVisible)
            {
                ShowGlobalProgress(_globalProgressOperationTitle.Length == 0 ? "正在处理" : _globalProgressOperationTitle, message);
            }

            var clampedPercent = Math.Clamp(percent, 0, 100);
            GlobalProgressTitleText.Text = message;
            GlobalProgressDetailText.Text = string.IsNullOrWhiteSpace(detail)
                ? _globalProgressOperationTitle
                : detail.Replace('\n', ' ');
            GlobalProgressBar.IsIndeterminate = isIndeterminate;
            if (!isIndeterminate)
            {
                GlobalProgressBar.Value = clampedPercent;
            }

            GlobalProgressPercentText.Text = $"{clampedPercent:0}%";
            _globalProgressLastPercent = clampedPercent;
            UpdateGlobalProgressRing(clampedPercent);
            UpdateGlobalProgressElapsedText();
        }

        private void CompleteGlobalProgress(string message, string? detail = null)
        {
            _globalProgressStopwatch.Stop();
            _globalProgressElapsedTimer.Stop();
            GlobalProgressBar.IsIndeterminate = false;
            GlobalProgressBar.Value = 100;
            GlobalProgressPercentText.Text = "100%";
            _globalProgressLastPercent = 100;
            UpdateGlobalProgressRing(100);
            GlobalProgressTitleText.Text = message;
            GlobalProgressDetailText.Text = string.IsNullOrWhiteSpace(detail) ? _globalProgressOperationTitle : detail;
            UpdateGlobalProgressElapsedText();
        }

        private async Task HideGlobalProgressAfterDelayAsync(int delayMilliseconds = 1400)
        {
            await Task.Delay(delayMilliseconds);
            HideGlobalProgress();
        }

        private void HideGlobalProgress()
        {
            _globalProgressStopwatch.Reset();
            _globalProgressElapsedTimer.Stop();
            _isGlobalProgressVisible = false;
            _globalProgressCancellation?.Dispose();
            _globalProgressCancellation = null;
            AnimateGlobalProgressHost(show: false);
        }

        private CancellationToken GetGlobalProgressCancellationToken()
        {
            return _globalProgressCancellation?.Token ?? CancellationToken.None;
        }

        private async void GlobalProgressRing_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
            if (!_isGlobalProgressVisible || _globalProgressCancellation is null || _globalProgressCancellation.IsCancellationRequested)
            {
                return;
            }

            var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "取消当前操作？",
                new TextBlock
                {
                    Text = $"正在进行：{_globalProgressOperationTitle}\n取消后，已经写入的文件可能会保留，未完成的部分会停止。",
                    TextWrapping = TextWrapping.Wrap,
                    Width = 360
                },
                PrimaryButtonText: "取消操作",
                CloseButtonText: "继续等待"));
            if (result != DialogResultKind.Primary)
            {
                return;
            }

            _globalProgressCancellation.Cancel();
            UpdateGlobalProgress("正在取消...", _globalProgressLastPercent, "等待当前步骤安全停止。");
        }

        private void GlobalProgressElapsedTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            UpdateGlobalProgressElapsedText();
        }

        private void UpdateGlobalProgressElapsedText()
        {
            GlobalProgressElapsedText.Text = FormatElapsedTime(_globalProgressStopwatch.Elapsed);
        }

        private void UpdateGlobalProgressRing(double percent)
        {
            var clampedPercent = Math.Clamp(percent, 0, 100);
            const double size = 86;
            const double stroke = 7;
            var radius = (size - stroke) / 2;
            var center = size / 2;

            if (clampedPercent <= 0)
            {
                GlobalProgressRingPath.Data = null;
                return;
            }

            if (clampedPercent >= 99.9)
            {
                var geometryGroup = new GeometryGroup();
                geometryGroup.Children.Add(CreateProgressRingArc(center, radius, 359.9));
                GlobalProgressRingPath.Data = geometryGroup;
                return;
            }

            GlobalProgressRingPath.Data = CreateProgressRingArc(center, radius, clampedPercent / 100d * 360d);
        }

        private static Geometry CreateProgressRingArc(double center, double radius, double angleDegrees)
        {
            var startPoint = new Point(center, center - radius);
            var radians = (angleDegrees - 90) * Math.PI / 180d;
            var endPoint = new Point(
                center + radius * Math.Cos(radians),
                center + radius * Math.Sin(radians));
            var figure = new PathFigure
            {
                StartPoint = startPoint,
                IsClosed = false
            };
            figure.Segments.Add(new ArcSegment
            {
                Point = endPoint,
                Size = new Size(radius, radius),
                SweepDirection = SweepDirection.Clockwise,
                IsLargeArc = angleDegrees > 180
            });

            return new PathGeometry
            {
                Figures = { figure }
            };
        }

        private void AnimateGlobalProgressHost(bool show)
        {
            var transform = GlobalProgressHostTransform;
            var fromY = show ? 130 : 0;
            var toY = show ? 0 : 130;
            var fromOpacity = show ? 0 : 1;
            var toOpacity = show ? 1 : 0;
            GlobalProgressHost.Visibility = Visibility.Visible;
            transform.Y = fromY;
            GlobalProgressHost.Opacity = fromOpacity;

            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            var slideAnimation = new DoubleAnimation
            {
                From = fromY,
                To = toY,
                Duration = TimeSpan.FromMilliseconds(show ? 240 : 180),
                EasingFunction = easing
            };
            Storyboard.SetTarget(slideAnimation, transform);
            Storyboard.SetTargetProperty(slideAnimation, nameof(TranslateTransform.Y));

            var fadeAnimation = new DoubleAnimation
            {
                From = fromOpacity,
                To = toOpacity,
                Duration = TimeSpan.FromMilliseconds(show ? 220 : 160),
                EasingFunction = easing
            };
            Storyboard.SetTarget(fadeAnimation, GlobalProgressHost);
            Storyboard.SetTargetProperty(fadeAnimation, nameof(UIElement.Opacity));

            var storyboard = new Storyboard();
            storyboard.Children.Add(slideAnimation);
            storyboard.Children.Add(fadeAnimation);
            storyboard.Completed += (_, _) =>
            {
                transform.Y = toY;
                GlobalProgressHost.Opacity = toOpacity;
                if (!show)
                {
                    GlobalProgressHost.Visibility = Visibility.Collapsed;
                }
            };
            storyboard.Begin();
        }

        private static string FormatElapsedTime(TimeSpan elapsed)
        {
            return elapsed.TotalHours >= 1
                ? elapsed.ToString(@"h\:mm\:ss")
                : elapsed.ToString(@"mm\:ss");
        }

    }
}
