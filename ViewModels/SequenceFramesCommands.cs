using System;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;

namespace CrossingVoidZDTool.ViewModels;

/// <summary>
/// St5 命令宿主要界面提供的东西（S0 命令宿主）。
/// 一开始只有一项：打开帧管理器浮层——它是「管理」按钮要转发的那件事。
/// </summary>
internal interface ISequenceFramesCommandHost
{
    void ShowSequenceFrameManager(SequenceFrameSection section);

    void HideSequenceFrameManager();

    Task ShowSequenceFrameCollectionAsync();

    void HideSequenceFrameCollection();

    void CancelSequenceFrameCollectionSelection();

    Task DetectDuplicatesAsync();

    Task ConfirmCollectionSelectionAsync();

    Task ResolveAllDuplicatesAsync();

    /// <summary>在当前选中帧的左侧（before=true）或右侧插入一张空白帧。</summary>
    Task InsertBlankFrameAsync(bool before);

    /// <summary>复制当前选中帧。</summary>
    Task CopySelectedEditorFrameAsync();

    /// <summary>删除当前选中帧。</summary>
    Task DeleteSelectedEditorFrameAsync();

    /// <summary>批量删除时间轴上选中的帧。</summary>
    Task DeleteSelectedFramesAsync();

    /// <summary>用系统资源管理器打开这个动作的素材目录。</summary>
    void OpenActionFolder(SequenceFrameSection section);

    /// <summary>导入帧素材到某张动作卡（先弹选图框）。</summary>
    Task ImportFramesAsync(SequenceFrameSection section);

    /// <summary>按当前预览中的动作打开帧管理器；没选中动作时给一句提示。</summary>
    void OpenEditorForPreviewSection();

    void HideSequenceFrameDuplicateResolver();

    /// <summary>预览区缩略图：打开这张缩略图所属动作的帧管理器（S5 收尾）。</summary>
    void OpenManagerForFrame(SequenceFrameItem frame);

    /// <summary>「复制所选帧」：进入/退出"点击帧格选择复制位置"模式（S5 收尾）。</summary>
    void ToggleCopyTargetSelection();

    /// <summary>「复用角标」：把同一份素材用到的所有帧一起选中（S5 收尾）。</summary>
    void SelectReuseGroup(SequenceFrameItem frame);

    // S1 收尾：时间轴右键菜单那五项（数据上下文就是那一帧，所以命令挂在帧上）。
    /// <summary>「替换素材」：挑一张图替换这一帧（选图框留在壳里）。</summary>
    Task ReplaceFrameFromMenuAsync(SequenceFrameItem frame);

    /// <summary>「复制」：把这一帧复制到它自己后面。</summary>
    Task CopyFrameFromMenuAsync(SequenceFrameItem frame);

    /// <summary>「左插入 / 右插入」：这一帧的左边/右边插一张空白帧。</summary>
    Task InsertBlankFrameAtFrameAsync(SequenceFrameItem frame, bool before);

    /// <summary>「删除」：删掉这一帧。</summary>
    Task DeleteFrameFromMenuAsync(SequenceFrameItem frame);

    /// <summary>「处理重复」：帧合集里对着某个重复素材打开裁决器（S1 收尾）。</summary>
    void ResolveDuplicatesForCollectionItem(SequenceFrameCollectionItem item);

    // S5 收尾：预览/播放/导航这一圈按钮。它们的流程体都是「几行壳调用」，
    // 以前各挂一个 Click 处理器；命令化之后 XAML 只剩一行绑定。
    /// <summary>动作卡上的播放按钮：把这一组动作放进右侧预览区并开始播。</summary>
    Task PreviewSectionAsync(SequenceFrameSection section);

    /// <summary>预览/编辑器的「上一帧 / 下一帧」。</summary>
    void NavigateFrame(int direction);

    /// <summary>预览区（外层）的播放 / 暂停。</summary>
    Task TogglePreviewPlaybackAsync();

    /// <summary>编辑器里的播放 / 暂停。</summary>
    Task ToggleEditorPreviewPlaybackAsync();

    /// <summary>「替换帧素材」：给当前选中帧换一张图（选图框留在壳里）。</summary>
    Task ReplaceSelectedEditorFrameAsync();

    /// <summary>「从帧合集选择」：拿当前选中帧去帧合集里挑素材。</summary>
    Task PickEditorFrameFromCollectionAsync();

    /// <summary>「导出图集」：给当前选中动作打包图集并导出（S5 收尾）。</summary>
    Task ExportAtlasAsync();

    /// <summary>重复帧裁决器的「确认保留所选」。</summary>
    Task ConfirmDuplicateResolutionAsync();
}

/// <summary>
/// St5 的命令宿主（S0 骨架）。
///
/// **为什么需要它**：动作卡上的按钮长在 <c>DataTemplate</c> 里，`Click` 处理器靠
/// <c>sender.CommandParameter</c> 拿到当前那张卡。改成 <c>Command</c> 之后没有 sender 了，
/// 而在 WinUI 的模板里用 `ElementName`/`RelativeSource` 去够页面上的命令**不稳**——
/// 写错的症状正是「点了没反应」，和 C6b 卡住的原因同一类。
///
/// 解法：**命令随卡片一起造**。卡片是 <c>SequenceFramesViewModel</c> 建的，
/// 建的时候就把命令挂到卡片自己身上（`Command="{Binding ManageFramesCommand}"`，
/// 数据上下文天然就是这张卡），不需要跨 namescope，也能直接单测：
/// 造一个假宿主 + 一张卡，执行命令，断言宿主收到了哪张卡。
///
/// 架构卡上的按钮都走这套：命令挂**卡片/项自己**（模板的数据上下文就是它），
/// 参数用 `CommandParameter="{Binding}"` 把那一项传给命令。
/// </summary>
internal sealed class SequenceFramesCommands(ISequenceFramesCommandHost host)
{
    private readonly ISequenceFramesCommandHost _host = host;

    /// <summary>
    /// 当前这套命令。**为什么是静态的**：动作卡有两个构造点——`SequenceFramesViewModel`
    /// （帧变化后重建）和 `SequenceFrameSectionBuilder`（首次加载，静态类、由 Service 调用）。
    /// 把命令穿透进 Service 的签名改动太大，而"启动时挂一次、所有卡片都能用"
    /// 正是项目里 `ToolboxLog.SetSink` 已经在用的形状。
    ///
    /// 卡片上的 `ManageFramesCommand` 是个**只读计算属性**，读它就是读这里——
    /// 所以不存在"某个构造点忘了挂命令"这种漏路径。
    /// </summary>
    public static SequenceFramesCommands? Current { get; private set; }

    /// <summary>壳在启动时调用一次；重复调用以最后一次为准（和 SetSink 一样）。</summary>
    public static SequenceFramesCommands Attach(ISequenceFramesCommandHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        Current = new SequenceFramesCommands(host);
        return Current;
    }

    /// <summary>「管理」：打开这张卡的帧管理器。</summary>
    public RelayCommand ManageFramesCommand { get; } = new(parameter =>
    {
        if (parameter is SequenceFrameSection section)
        {
            host.ShowSequenceFrameManager(section);
        }
    });

    // S1：三个浮层的关闭。它们都是「一行壳调用」，搬过来是为了跟打开动作成对，
    // 也让 XAML 里不再剩 Click 处理器（形状统一才好批量改）。
    public RelayCommand CloseManagerCommand { get; } = new(_ => host.HideSequenceFrameManager());

    /// <summary>打开帧素材合集（S1）。原来是一行 <c>await ShowSequenceFrameCollectionAsync()</c>，
    /// 而且**两个按钮共用**同一个处理器——搬成命令的收益是双份的。</summary>
    public AsyncRelayCommand OpenCollectionCommand { get; } =
        new((object? _) => host.ShowSequenceFrameCollectionAsync());

    public RelayCommand CloseCollectionCommand { get; } = new(_ => host.HideSequenceFrameCollection());

    /// <summary>取消帧合集的多选（S2）。原来是一行 <c>CancelSequenceFrameCollectionMultiSelection()</c>。</summary>
    public RelayCommand CancelCollectionSelectionCommand { get; } =
        new(_ => host.CancelSequenceFrameCollectionSelection());

    /// <summary>检测帧合集里的重复内容（S2）。</summary>
    public AsyncRelayCommand DetectDuplicatesCommand { get; } =
        new((object? _) => host.DetectDuplicatesAsync());

    /// <summary>确认批量替换（S2）。</summary>
    public AsyncRelayCommand ConfirmCollectionSelectionCommand { get; } =
        new((object? _) => host.ConfirmCollectionSelectionAsync());

    /// <summary>一键处理重复帧（S2）。</summary>
    public AsyncRelayCommand ResolveAllDuplicatesCommand { get; } =
        new((object? _) => host.ResolveAllDuplicatesAsync());

    // S3：编辑器里三个按钮共用同一段逻辑，只是插入位置不同。
    // 命令化之后**帧从 ViewModel 的当前选中帧取**（XAML 的 IsEnabled 本来就绑它），
    // 不再需要从 sender 反推——这是这一批唯一的结构变化。
    public AsyncRelayCommand InsertBlankBeforeCommand { get; } =
        new((object? _) => host.InsertBlankFrameAsync(before: true));

    public AsyncRelayCommand InsertBlankAfterCommand { get; } =
        new((object? _) => host.InsertBlankFrameAsync(before: false));

    public AsyncRelayCommand CopyEditorFrameCommand { get; } =
        new((object? _) => host.CopySelectedEditorFrameAsync());

    public AsyncRelayCommand DeleteEditorFrameCommand { get; } =
        new((object? _) => host.DeleteSelectedEditorFrameAsync());

    public AsyncRelayCommand DeleteSelectedFramesCommand { get; } =
        new((object? _) => host.DeleteSelectedFramesAsync());

    /// <summary>打开动作素材目录（S4）。卡片当参数传进来。</summary>
    public RelayCommand OpenActionFolderCommand { get; } = new(parameter =>
    {
        if (parameter is SequenceFrameSection section)
        {
            host.OpenActionFolder(section);
        }
    });

    /// <summary>导入帧素材（S4）。卡片当参数传进来。</summary>
    public AsyncRelayCommand ImportFramesCommand { get; } = new(parameter =>
        parameter is SequenceFrameSection section
            ? host.ImportFramesAsync(section)
            : Task.CompletedTask);

    /// <summary>打开帧序列编辑器（S5 收尾）：用当前预览中的动作。</summary>
    public RelayCommand OpenEditorCommand { get; } = new(_ => host.OpenEditorForPreviewSection());

    public RelayCommand CloseDuplicateResolverCommand { get; } =
        new(_ => host.HideSequenceFrameDuplicateResolver());

    /// <summary>
    /// S5 收尾的三处接线：缩略图、复制所选帧、复用角标。
    ///
    /// 前两个按钮分别长在**卡片模板**和**时间轴模板**里（数据上下文是 `SequenceFrameItem`），
    /// 所以命令照 `ManageFramesCommand` 的先例挂在帧自己身上（只读计算属性读这里）。
    /// 「复制所选帧」不在模板里，走常规的页面级命令。
    /// </summary>
    public RelayCommand PreviewThumbnailCommand { get; } = new(parameter =>
    {
        if (parameter is SequenceFrameItem frame)
        {
            host.OpenManagerForFrame(frame);
        }
    });

    public RelayCommand ToggleCopyTargetSelectionCommand { get; } = new(_ => host.ToggleCopyTargetSelection());

    public RelayCommand SelectReuseGroupCommand { get; } = new(parameter =>
    {
        if (parameter is SequenceFrameItem frame)
        {
            host.SelectReuseGroup(frame);
        }
    });

    // S1 收尾：时间轴右键菜单 + 帧合集右键菜单。
    public AsyncRelayCommand ReplaceFrameMenuItemCommand { get; } = new(parameter =>
        parameter is SequenceFrameItem frame ? host.ReplaceFrameFromMenuAsync(frame) : Task.CompletedTask);

    public AsyncRelayCommand CopyFrameMenuItemCommand { get; } = new(parameter =>
        parameter is SequenceFrameItem frame ? host.CopyFrameFromMenuAsync(frame) : Task.CompletedTask);

    public AsyncRelayCommand InsertBlankBeforeMenuItemCommand { get; } = new(parameter =>
        parameter is SequenceFrameItem frame
            ? host.InsertBlankFrameAtFrameAsync(frame, before: true)
            : Task.CompletedTask);

    public AsyncRelayCommand InsertBlankAfterMenuItemCommand { get; } = new(parameter =>
        parameter is SequenceFrameItem frame
            ? host.InsertBlankFrameAtFrameAsync(frame, before: false)
            : Task.CompletedTask);

    public AsyncRelayCommand DeleteFrameMenuItemCommand { get; } = new(parameter =>
        parameter is SequenceFrameItem frame ? host.DeleteFrameFromMenuAsync(frame) : Task.CompletedTask);

    public RelayCommand ResolveDuplicatesMenuItemCommand { get; } = new(parameter =>
    {
        if (parameter is SequenceFrameCollectionItem item)
        {
            host.ResolveDuplicatesForCollectionItem(item);
        }
    });

    public AsyncRelayCommand PreviewSectionCommand { get; } = new(parameter =>
        parameter is SequenceFrameSection section ? host.PreviewSectionAsync(section) : Task.CompletedTask);

    public RelayCommand PreviousFrameCommand { get; } = new(_ => host.NavigateFrame(-1));

    public RelayCommand NextFrameCommand { get; } = new(_ => host.NavigateFrame(1));

    public AsyncRelayCommand TogglePreviewPlaybackCommand { get; } =
        new((object? _) => host.TogglePreviewPlaybackAsync());

    public AsyncRelayCommand ToggleEditorPreviewPlaybackCommand { get; } =
        new((object? _) => host.ToggleEditorPreviewPlaybackAsync());

    public AsyncRelayCommand ReplaceSelectedEditorFrameCommand { get; } =
        new((object? _) => host.ReplaceSelectedEditorFrameAsync());

    public AsyncRelayCommand PickEditorFrameFromCollectionCommand { get; } =
        new((object? _) => host.PickEditorFrameFromCollectionAsync());

    public AsyncRelayCommand ExportAtlasCommand { get; } = new((object? _) => host.ExportAtlasAsync());

    public AsyncRelayCommand ConfirmDuplicateResolutionCommand { get; } =
        new((object? _) => host.ConfirmDuplicateResolutionAsync());
}
