using System;
using CommunityToolkit.Mvvm.Input;

namespace CrossingVoidZDTool.ViewModels;

/// <summary>
/// 角色详情里五个动作的命令（C6b 接线翻转）。
///
/// 流程早就搬进 <see cref="CharacterDetailActionController"/> 了，壳里只剩一行转发
/// （`await CharacterDetailAction.ContinueEditingAsync();`）。这一份把那一行也去掉——
/// 命令直接拿着控制器，XAML 写 `Command="{Binding CharacterDesk.ContinueEditingCommand}"`。
///
/// **为什么用静态持有者**：和 `SequenceFramesCommands` 同一个理由。控制器是壳
/// （`MainWindow`）造的，而命令要挂在 ViewModel 上给 XAML 绑；启动时 Attach 一次，
/// 比把控制器透传进 ViewModel 构造函数改动面小得多（那个构造函数还被一堆回归用例直接 new）。
///
/// **为什么"查看角色"也算在详情五个动作里**：它是同一个弹窗里的按钮，
/// 走的也是同一个控制器，只是文案上是"切回只读预览"。
/// </summary>
internal sealed class CharacterDetailCommands(CharacterDetailActionController controller)
{
    /// <summary>壳在启动时挂一次（和 `SequenceFramesCommands.Attach` 同时机）。</summary>
    public static CharacterDetailCommands? Current { get; private set; }

    public static CharacterDetailCommands Attach(CharacterDetailActionController value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Current = new CharacterDetailCommands(value);
        return Current;
    }

    /// <summary>「继续编辑」：恢复草稿（若已完成）→ 关弹窗 → 跳上次编辑的那一页。</summary>
    public AsyncRelayCommand ContinueEditingCommand { get; } =
        new((object? _) => controller.ContinueEditingAsync());

    /// <summary>「查看角色」：只读预览（不改工作区状态）。</summary>
    public AsyncRelayCommand ViewCharacterCommand { get; } =
        new((object? _) => controller.ViewCharacterAsync());

    /// <summary>「导出角色」：导出这条流程在 `CharacterExportController`，这里只转发。</summary>
    public AsyncRelayCommand ExportCharacterCommand { get; } =
        new((object? _) => controller.ExportCharacterAsync());

    /// <summary>「打开角色目录」：交给资源管理器（目录不存在就先建）。</summary>
    public RelayCommand OpenCharacterFolderCommand { get; } =
        new(_ => controller.OpenCharacterFolder());

    /// <summary>「前往虚幻同步台」：先把这个角色选上，再跳同步台。</summary>
    public AsyncRelayCommand GoToUnrealSyncCommand { get; } =
        new((object? _) => controller.GoToUnrealSyncAsync());
}
