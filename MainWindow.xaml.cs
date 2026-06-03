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
        private readonly ApplicationViewModel _applicationViewModel;
        private readonly WinUiDialogService _dialogService;
        private readonly Stopwatch _globalProgressStopwatch = new();
        private readonly DispatcherQueueTimer _globalProgressElapsedTimer;
        private bool _isChangingShellSelectionInternally;
        private CancellationTokenSource? _globalProgressCancellation;

        public MainWindow()
        {
            var settingsViewModel = new SettingsViewModel(new AppSettingsService(), new ProjectRootMigrationService());
            _applicationViewModel = new ApplicationViewModel(settingsViewModel, new GlobalProgressViewModel());
            InitializeComponent();
            RootGrid.DataContext = _applicationViewModel;
            _dialogService = new WinUiDialogService(() => RootGrid.XamlRoot);
            RegisterSettingsShortcuts();
            ApplyCustomTitleBar();
            ApplyWindowIcon();
            AppWindow.Resize(new SizeInt32(1500, 920));
            ApplyInitialWindowPlacement();
            _globalProgressElapsedTimer = DispatcherQueue.CreateTimer();
            _globalProgressElapsedTimer.Interval = TimeSpan.FromSeconds(1);
            _globalProgressElapsedTimer.Tick += GlobalProgressElapsedTimer_Tick;
            Settings.AuxiliaryDisplayChanged += (_, _) => UpdateAuxiliaryDisplayVisibility();
            Settings.LogSettingsChanged += (_, _) =>
            {
                UpdateLogOptionEnabledState();
                UpdateAuxiliaryDisplayVisibility();
                AppendLog(LogKind.User, "已更新日志输出设置。");
            };

            Settings.LoadAndEnsureProjectRoot();
            _applicationViewModel.CharacterDesk.StatusText = Settings.WorkspaceStatusText;
            UpdateLogOptionEnabledState();
            UpdateAuxiliaryDisplayVisibility();
            AppendLog(LogKind.Info, "程序启动，已检查整体项目目录。");
            ShowCharacterDeskPage();
        }

        private SettingsViewModel Settings => _applicationViewModel.Settings;

        private GlobalProgressViewModel GlobalProgress => _applicationViewModel.GlobalProgress;

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

    }
}
