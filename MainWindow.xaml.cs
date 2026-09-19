using System;
using System.Collections.Generic;
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
        private readonly BaseMaterialService _baseMaterialService = new();
        private readonly VoiceMaterialService _voiceMaterialService = new();
        private readonly ProductionStatusService _productionStatusService = new();
        private readonly IFilePickerService _filePickerService;
        private readonly Stopwatch _globalProgressStopwatch = new();
        private readonly DispatcherQueueTimer _globalProgressElapsedTimer;
        private readonly DispatcherQueueTimer _draftSaveTimer;
        private readonly DispatcherQueueTimer _characterInfoSaveTimer;
        private readonly DispatcherQueueTimer _skillsSaveTimer;
        private readonly DispatcherQueueTimer _buffsSaveTimer;
        private readonly DispatcherQueueTimer _sequencePreviewTimer;
        private readonly DispatcherQueueTimer _baseMaterialRefreshTimer;
        private readonly Dictionary<InfoBar, DispatcherQueueTimer> _floatingTipTimers = new();
        private readonly LogPanelBuffer _logPanel = new(MaxUiLogCount);
        private const int MaxUiLogCount = 300;
        /// <summary>当前日志批次。会跑检测/写入的动作开新批次，其余沿用。</summary>
        private string? _currentLogRunId;
        /// <summary>上一条记进日志的步骤号，用来判定「进/出某一步」。</summary>
        private int _lastLoggedWorkflowStep;
        private bool _logScrollToBottomPending;
        private readonly Queue<(DateTime Timestamp, string Text)> _recentOperations = new();
        private const int MaxRecentOperationCount = 50;
        private readonly object _runtimeLogLock = new();
        private readonly string _runtimeLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CrossingVoidZDTool",
            "Logs",
            "runtime.log");
        private readonly PageScrollPositionStore _pageScrollPositions = new();
        private FileSystemWatcher? _baseMaterialWatcher;
        private FileSystemWatcher? _voiceMaterialWatcher;
        private string? _watchedBaseMaterialRoot;
        private string? _watchedVoiceMaterialRoot;
        private string? _clipboardTextValue;
        private int? _clipboardNumberValue;
        private CharacterReferenceImage? _viewingReferenceImage;
        private SequenceFrameItem? _viewingSequenceFrame;
        private IReadOnlyList<SequenceFrameItem> _viewingSequenceFrames = [];
        private IReadOnlyList<SequenceFrameCollectionItem> _pendingDuplicateFrameItems = [];
        private SequenceFrameCollectionItem? _selectedDuplicateFrameItem;
        private bool _isResolvingDuplicateFrames;
        private readonly SequencePreviewBitmapCache _sequencePreviewBitmapCache = new();
        private TaskCompletionSource<System.Drawing.Rectangle?>? _baseMaterialCropCompletion;
        private string? _baseMaterialCropSourcePath;
        private CharacterSkillEntry? _pendingSkillIconEntry;
        private BuffEntry? _pendingBuffIconEntry;
        private BaseMaterialItem? _selectedSkillIconItem;
        private int _baseMaterialCropSourceWidth;
        private int _baseMaterialCropSourceHeight;
        private BaseMaterialSpec? _baseMaterialCropSpec;
        private double _baseMaterialCropScale = 1;
        private double _baseMaterialCropOffsetX;
        private double _baseMaterialCropOffsetY;
        private bool _isPanningBaseMaterialCrop;
        private Point _lastBaseMaterialCropPointerPosition;
        private double _referenceImageViewerScale = 1;
        private bool _isPanningReferenceImage;
        private Point _lastReferenceImagePointerPosition;
        private double _sequencePreviewScale = 1;
        private bool _isPanningSequencePreview;
        private double _sequenceEditorPreviewScale = 1;
        private bool _isPanningSequenceEditorPreview;
        private bool _isReorderingSequenceFrames;
        private bool _isSynchronizingSequenceFrameSelection;
        private bool _isSelectingSequenceFrameCopyTarget;
        private IReadOnlyList<SequenceFrameItem> _pendingSequenceFramesToDuplicate = [];
        private Point _lastSequencePreviewPointerPosition;
        private Point _lastSequenceEditorPreviewPointerPosition;
        private bool _isChangingShellSelectionInternally;
        private int _baseMaterialInternalWriteDepth;
        private CancellationTokenSource? _globalProgressCancellation;
        private TaskCompletionSource<string?>? _characterCreateDialogCompletion;

        public MainWindow()
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue));
            var settingsViewModel = new SettingsViewModel(new AppSettingsService(), new ProjectRootMigrationService());
            _applicationViewModel = new ApplicationViewModel(
                settingsViewModel,
                new GlobalProgressViewModel(),
                _baseMaterialService,
                _voiceMaterialService);
            InitializeComponent();
            RootGrid.DataContext = _applicationViewModel;
            // Services 层从这里开始能写日志。在此之前它整层没有任何日志出口，
            // 约九十处 catch 全是静默的，出问题只能靠猜。
            ToolboxLog.SetSink(new ToolboxLogBridge(this));
            // 界面层能记「用户点了什么」之后，立即把出口交给同步台 VM——
            // 右栏那几个命令已经搬进 VM，它们记的用户操作要落回同一处日志。
            _applicationViewModel.UnrealProjectSync.UserOperations = this;
            // 六步流程的编排（上一步/下一步/依次检测/重新加载）现在归 VM 的命令所有，
            // 它要的界面能力也在这里一次交给它。
            _applicationViewModel.UnrealProjectSync.WorkflowHost = this;
            _applicationViewModel.UnrealProjectSync.Notifications = this;
            _applicationViewModel.UnrealProjectSync.Clipboard = this;
            // S0/S1：St5 动作卡上的命令随卡片携带，先把壳能力交给它。
            // 漏了这一行，卡片上的 Command 就是 null——按钮看着在、点不动，而且没有任何报错。
            _applicationViewModel.SequenceFrames.AttachCommandHost(this);
            // C6b：角色详情那五个按钮的命令。流程在 CharacterDetailActionController 里，
            // 命令挂在全局持有者上，由**角色卡**读它给 XAML 绑（`{Binding ContinueEditingCommand}`）
            // ——详情面板的 DataContext 是那张卡，不是页面级 VM。
            CharacterDetailCommands.Attach(CharacterDetailAction);
            _dialogService = new WinUiDialogService(() => RootGrid.XamlRoot);
            _filePickerService = new WinUiFilePickerService(() => WindowNative.GetWindowHandle(this));
            InitializeVoicePlayback();
            RegisterSettingsShortcuts();
            RegisterSequenceFrameEditorShortcuts();
            RegisterSkillIconPickerWheelHandler();
            ApplyCustomTitleBar();
            ApplyWindowIcon();
            AppWindow.Resize(new SizeInt32(1500, 920));
            ApplyInitialWindowPlacement();
            _globalProgressElapsedTimer = DispatcherQueue.CreateTimer();
            _globalProgressElapsedTimer.Interval = TimeSpan.FromSeconds(1);
            _globalProgressElapsedTimer.Tick += GlobalProgressElapsedTimer_Tick;
            _draftSaveTimer = DispatcherQueue.CreateTimer();
            _draftSaveTimer.Interval = TimeSpan.FromMilliseconds(900);
            _draftSaveTimer.Tick += DraftSaveTimer_Tick;
            _characterInfoSaveTimer = DispatcherQueue.CreateTimer();
            _characterInfoSaveTimer.Interval = TimeSpan.FromMilliseconds(900);
            _characterInfoSaveTimer.Tick += CharacterInfoSaveTimer_Tick;
            _skillsSaveTimer = DispatcherQueue.CreateTimer();
            _skillsSaveTimer.Interval = TimeSpan.FromMilliseconds(900);
            _skillsSaveTimer.Tick += SkillsSaveTimer_Tick;
            _buffsSaveTimer = DispatcherQueue.CreateTimer();
            _buffsSaveTimer.Interval = TimeSpan.FromMilliseconds(900);
            _buffsSaveTimer.Tick += BuffsSaveTimer_Tick;
            _sequencePreviewTimer = DispatcherQueue.CreateTimer();
            _sequencePreviewTimer.Interval = TimeSpan.FromMilliseconds(1000d / 12d);
            _sequencePreviewTimer.Tick += SequencePreviewTimer_Tick;
            _baseMaterialRefreshTimer = DispatcherQueue.CreateTimer();
            _baseMaterialRefreshTimer.Interval = TimeSpan.FromMilliseconds(450);
            _baseMaterialRefreshTimer.Tick += BaseMaterialRefreshTimer_Tick;
            Closed += MainWindow_Closed;
            _applicationViewModel.CharacterDesk.DraftTextEdited += (_, _) => ScheduleDraftSave();
            _applicationViewModel.UnrealSync.CharacterInfoEdited += (_, _) => ScheduleCharacterInfoSave();
            _applicationViewModel.Skills.SkillsEdited += (_, _) => ScheduleSkillsSave();
            _applicationViewModel.SequenceFrames.SequenceFramesSaved += (_, _) =>
            {
                MarkLastEditedModule(ToolboxModuleKey.SequenceFrames);
                PersistCurrentCharacterSelection();
            };
            _applicationViewModel.Buffs.BuffsEdited += (_, _) => ScheduleBuffsSave();
            Settings.AuxiliaryDisplayChanged += (_, _) => UpdateAuxiliaryDisplayVisibility();
            Settings.ThemeSettingsChanged += (_, _) => ApplyThemeSettings();
            Settings.LogSettingsChanged += (_, _) =>
            {
                UpdateLogOptionEnabledState();
                UpdateAuxiliaryDisplayVisibility();
                AppendLog(LogKind.User, "已更新日志输出设置。");
            };

            Settings.LoadAndEnsureProjectRoot();
            _applicationViewModel.UnrealProjectSync.Load(Settings.UnrealEnginePath, Settings.UnrealProjectPath);
            _applicationViewModel.CharacterDesk.StatusText = Settings.WorkspaceStatusText;
            ApplyThemeSettings();
            UpdateLogOptionEnabledState();
            UpdateAuxiliaryDisplayVisibility();
            AppendLog(LogKind.Info, "程序启动，已检查整体项目目录。");
            // 步骤标题行从这里开始记：进/出某一步各一条 Sticky 行，
            // 中间几百条明细被面板挤掉也不影响从面板看出走完全程。
            AttachWorkflowStepLog();
            _ = LoadCharacterCardsAsync();
            ShowCharacterDeskPage();
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            FlushPendingCharacterInfoSave();
            FlushPendingSkillsSave();
            FlushPendingBuffsSave();
            // 草稿也是防抖保存的，以前唯独漏了它，关窗会丢掉最后 900 毫秒的输入
            FlushPendingDraftSave();
            _applicationViewModel.UnrealProjectSync.FlushSessionCache();
            DisposeVoicePlayback();
        }

        private SettingsViewModel Settings => _applicationViewModel.Settings;

        private GlobalProgressViewModel GlobalProgress => _applicationViewModel.GlobalProgress;

        private CharacterDeskViewModel CharacterDesk => _applicationViewModel.CharacterDesk;

        private void RunOnUiThread(Action action)
        {
            if (DispatcherQueue.HasThreadAccess)
            {
                action();
                return;
            }

            if (!DispatcherQueue.TryEnqueue(() => action()))
            {
                // The window is closing; callers are scheduling UI work that can be safely dropped.
            }
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

    }
}
