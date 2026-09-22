using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrossingVoidZDTool.Services.Atlas;

namespace CrossingVoidZDTool.ViewModels;

/// <summary>
/// 工具集要界面提供的能力（选目录 / 选文件 / 真正跑 / 打开输出目录）。
/// 流程留在 VM 里，壳只做"系统交互 + 起进程"，和项目里其它 Host 一个形状。
/// </summary>
internal interface IAtlasToolHost
{
    Task<string?> PickFolderAsync(string title);

    Task<string?> PickFileAsync(string title, string fileTypeFilter);

    Task<AtlasCreateResult?> RunCreateAsync(AtlasCreateRequest request);

    Task<AtlasExtractResult?> RunExtractAsync(AtlasExtractRequest request);

    void OpenFolder(string folderPath);
}

/// <summary>
/// 「工具集」这一页的状态与流程。
///
/// **排版是可复用的**：左边一份工具清单（<see cref="AtlasToolCatalog.Build"/>），
/// 右边是选中工具的参数面板 + 共用的运行/状态/输出区。加新工具：
/// 清单加一张卡（模型层）+ 这里加一组参数与一个 Run 分支 + XAML 加一个参数面板。
/// </summary>
internal sealed class AtlasToolViewModel : ObservableObject
{
    private IAtlasToolHost? _host;

    public AtlasToolViewModel()
    {
        PickCreateSourceCommand = new AsyncRelayCommand(() => PickCreateSourceAsync());
        PickCreateOutputCommand = new AsyncRelayCommand(() => PickCreateOutputAsync());
        PickExtractImageCommand = new AsyncRelayCommand(() => PickExtractImageAsync());
        PickExtractDataCommand = new AsyncRelayCommand(() => PickExtractDataAsync());
        PickExtractOutputCommand = new AsyncRelayCommand(() => PickExtractOutputAsync());
        RunCommand = new AsyncRelayCommand(() => RunAsync(), () => !IsBusy && CanRun);
        OpenOutputFolderCommand = new RelayCommand(
            () => _host?.OpenFolder(LastOutputFolder),
            () => HasOutput && _host is not null);
    }

    /// <summary>壳在启动时挂一次。</summary>
    public void AttachHost(IAtlasToolHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        OpenOutputFolderCommand.NotifyCanExecuteChanged();
        RunCommand.NotifyCanExecuteChanged();
    }

    // ── 工具清单与选中项 ────────────────────────────────────────────────
    public IReadOnlyList<AtlasToolCard> Tools { get; } = AtlasToolCatalog.Build();

    private AtlasToolCard _selectedTool = AtlasToolCatalog.Build()[0];

    public AtlasToolCard SelectedTool
    {
        get => _selectedTool;
        set
        {
            if (SetProperty(ref _selectedTool, value))
            {
                OnPropertyChanged(nameof(IsCreateToolSelected));
                OnPropertyChanged(nameof(IsExtractToolSelected));
                OnPropertyChanged(nameof(RunButtonText));
                RunCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsCreateToolSelected => SelectedTool.Kind == AtlasToolKind.Create;

    public bool IsExtractToolSelected => SelectedTool.Kind == AtlasToolKind.Extract;

    public string RunButtonText => IsCreateToolSelected ? "开始打包" : "开始拆分";

    // ── 创建图集的参数 ─────────────────────────────────────────────────
    private string _createSourceFolder = string.Empty;
    private string _createOutputFolder = string.Empty;
    private string _createAtlasName = string.Empty;
    private int _createModeIndex;
    private int _createColumns;
    private int _createPadding = 2;
    private bool _createTrim = true;
    private int _createMaxSize = 2048;

    public string CreateSourceFolder
    {
        get => _createSourceFolder;
        set { if (SetProperty(ref _createSourceFolder, value)) { RunCommand.NotifyCanExecuteChanged(); } }
    }

    public string CreateOutputFolder
    {
        get => _createOutputFolder;
        set { if (SetProperty(ref _createOutputFolder, value)) { RunCommand.NotifyCanExecuteChanged(); } }
    }

    public string CreateAtlasName
    {
        get => _createAtlasName;
        set => SetProperty(ref _createAtlasName, value);
    }

    /// <summary>0 = pack（紧密装箱，Paper2D 用）；1 = grid（均匀网格，Niagara SubUV 用）。</summary>
    public int CreateModeIndex
    {
        get => _createModeIndex;
        set
        {
            if (SetProperty(ref _createModeIndex, value))
            {
                OnPropertyChanged(nameof(IsCreateGridMode));
            }
        }
    }

    public bool IsCreateGridMode => CreateModeIndex == 1;

    public int CreateColumns
    {
        get => _createColumns;
        set => SetProperty(ref _createColumns, value);
    }

    public int CreatePadding
    {
        get => _createPadding;
        set => SetProperty(ref _createPadding, value);
    }

    public bool CreateTrim
    {
        get => _createTrim;
        set => SetProperty(ref _createTrim, value);
    }

    public int CreateMaxSize
    {
        get => _createMaxSize;
        set => SetProperty(ref _createMaxSize, value);
    }

    // ── 拆分图集的参数 ─────────────────────────────────────────────────
    private string _extractImagePath = string.Empty;
    private string _extractDataPath = string.Empty;
    private string _extractOutputFolder = string.Empty;
    private bool _extractPasteBack = true;

    public string ExtractImagePath
    {
        get => _extractImagePath;
        set { if (SetProperty(ref _extractImagePath, value)) { RunCommand.NotifyCanExecuteChanged(); } }
    }

    public string ExtractDataPath
    {
        get => _extractDataPath;
        set { if (SetProperty(ref _extractDataPath, value)) { RunCommand.NotifyCanExecuteChanged(); } }
    }

    public string ExtractOutputFolder
    {
        get => _extractOutputFolder;
        set { if (SetProperty(ref _extractOutputFolder, value)) { RunCommand.NotifyCanExecuteChanged(); } }
    }

    /// <summary>拆出来的图贴回原始画布（原图多大就多大）；关掉就是紧贴裁剪的那一块。</summary>
    public bool ExtractPasteBack
    {
        get => _extractPasteBack;
        set => SetProperty(ref _extractPasteBack, value);
    }

    // ── 共用的运行 / 状态 / 输出 ────────────────────────────────────────
    private string _statusText = "选一个工具，填好输入，然后点右边的按钮。";
    private bool _isBusy;
    private string _lastOutputFolder = string.Empty;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RunCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string LastOutputFolder
    {
        get => _lastOutputFolder;
        private set
        {
            if (SetProperty(ref _lastOutputFolder, value))
            {
                OnPropertyChanged(nameof(HasOutput));
                OnPropertyChanged(nameof(LastOutputFolderText));
                OpenOutputFolderCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasOutput => !string.IsNullOrWhiteSpace(LastOutputFolder);

    public string LastOutputFolderText => HasOutput ? LastOutputFolder : "（还没跑过）";

    public bool CanRun => IsCreateToolSelected
        ? !string.IsNullOrWhiteSpace(CreateSourceFolder) && !string.IsNullOrWhiteSpace(CreateOutputFolder)
        : !string.IsNullOrWhiteSpace(ExtractImagePath) &&
          !string.IsNullOrWhiteSpace(ExtractDataPath) &&
          !string.IsNullOrWhiteSpace(ExtractOutputFolder);

    public AsyncRelayCommand PickCreateSourceCommand { get; }

    public AsyncRelayCommand PickCreateOutputCommand { get; }

    public AsyncRelayCommand PickExtractImageCommand { get; }

    public AsyncRelayCommand PickExtractDataCommand { get; }

    public AsyncRelayCommand PickExtractOutputCommand { get; }

    public AsyncRelayCommand RunCommand { get; }

    public RelayCommand OpenOutputFolderCommand { get; }

    // ── 选目录 / 选文件 ────────────────────────────────────────────────
    private async Task PickCreateSourceAsync()
    {
        if (await PickFolderAsync("选择要打包成图集的 PNG 目录") is not { } folder)
        {
            return;
        }

        CreateSourceFolder = folder;
        if (string.IsNullOrWhiteSpace(CreateAtlasName))
        {
            CreateAtlasName = new DirectoryInfo(folder).Name;
        }

        if (string.IsNullOrWhiteSpace(CreateOutputFolder))
        {
            var parent = Directory.GetParent(folder)?.FullName ?? folder;
            CreateOutputFolder = Path.Combine(parent, $"{new DirectoryInfo(folder).Name}_atlas");
        }

        var count = AtlasFolderPackService.EnumerateSourceImages(folder).Count;
        StatusText = count == 0
            ? "这个目录里没有 PNG。"
            : $"已选 {count} 张 PNG，输出到：{CreateOutputFolder}";
    }

    private async Task PickCreateOutputAsync()
    {
        if (await PickFolderAsync("选择图集输出目录") is { } folder)
        {
            CreateOutputFolder = folder;
        }
    }

    private async Task PickExtractImageAsync()
    {
        if (await PickFileAsync("选择要拆的图集 PNG", ".png") is not { } file)
        {
            return;
        }

        ExtractImagePath = file;
        // 坐标文件就在旁边的话自动认出来，省一步选择。
        if (AtlasExtractService.ResolveDataFilePath(file) is { } dataFile)
        {
            ExtractDataPath = dataFile;
        }

        if (string.IsNullOrWhiteSpace(ExtractOutputFolder))
        {
            var directory = Path.GetDirectoryName(file) ?? file;
            ExtractOutputFolder = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(file)}_split");
        }

        StatusText = string.IsNullOrWhiteSpace(ExtractDataPath)
            ? "没在旁边找到同名坐标 json，请手动选一份。"
            : $"已选图集与坐标文件，输出到：{ExtractOutputFolder}";
    }

    private async Task PickExtractDataAsync()
    {
        if (await PickFileAsync("选择图集的坐标 json", ".json") is { } file)
        {
            ExtractDataPath = file;
        }
    }

    private async Task PickExtractOutputAsync()
    {
        if (await PickFolderAsync("选择拆分结果输出目录") is { } folder)
        {
            ExtractOutputFolder = folder;
        }
    }

    // ── 跑 ─────────────────────────────────────────────────────────────
    private async Task RunAsync()
    {
        if (_host is null || !CanRun)
        {
            return;
        }

        IsBusy = true;
        try
        {
            if (IsCreateToolSelected)
            {
                var request = new AtlasCreateRequest(
                    CreateSourceFolder,
                    CreateOutputFolder,
                    CreateAtlasName,
                    CreateModeIndex == 1 ? AtlasFolderPackService.GridMode : AtlasFolderPackService.PackMode,
                    CreateColumns,
                    CreatePadding,
                    CreateTrim,
                    CreateMaxSize);
                if (await _host.RunCreateAsync(request) is not { } result)
                {
                    return;
                }

                LastOutputFolder = result.OutputDirectory;
                StatusText = $"图集已生成：{Path.GetFileName(result.AtlasImagePath)}（{result.FrameCount} 张，{result.SizeText}）→ {result.OutputDirectory}";
            }
            else
            {
                var request = new AtlasExtractRequest(
                    ExtractImagePath,
                    ExtractDataPath,
                    ExtractOutputFolder,
                    ExtractPasteBack);
                if (await _host.RunExtractAsync(request) is not { } result)
                {
                    return;
                }

                LastOutputFolder = result.OutputDirectory;
                StatusText = result.PaddedToCanvasCount > 0
                    ? $"已拆出 {result.Frames.Count} 张（其中 {result.PaddedToCanvasCount} 张贴回原画布）→ {result.OutputDirectory}"
                    : $"已拆出 {result.Frames.Count} 张 → {result.OutputDirectory}";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task<string?> PickFolderAsync(string title) =>
        _host?.PickFolderAsync(title) ?? Task.FromResult<string?>(null);

    private Task<string?> PickFileAsync(string title, string filter) =>
        _host?.PickFileAsync(title, filter) ?? Task.FromResult<string?>(null);
}
