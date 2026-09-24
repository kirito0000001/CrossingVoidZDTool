using System.Collections.Generic;

namespace CrossingVoidZDTool.ViewModels;

/// <summary>特效帧从哪来。</summary>
internal enum SequenceEffectImportSource
{
    /// <summary>默认那条：读「导出底板」那份多图层 PSD，按图层顺序当特效帧。</summary>
    BasePlatePsd,

    /// <summary>老那条：自己挑一个装满 PNG 的目录，帧号取文件名末尾数字。</summary>
    Folder
}

/// <summary>一条导入项：文案 + 悬浮说明 + 从哪来。</summary>
internal sealed record SequenceEffectImportMenuItem(string Text, string ToolTip, SequenceEffectImportSource Source);

/// <summary>
/// 序列帧编辑器右侧「特效层」那个「导入特效帧」菜单的内容。
///
/// 和「导出」菜单（<see cref="SequenceExportMenu"/>）同一套做法：**数据清单 + 命令在 VM 里配一次**，
/// 壳只把 (文案, 命令) 变成 <c>MenuFlyoutItem</c>。菜单本体也在壳里按这份清单建，
/// 所以**加一条来源不用动 XAML** —— `MainWindow.xaml` 的行数已经顶在棘轮上限上了。
///
/// 顺序就是菜单里的顺序，第一条是默认那条（读底板 PSD）。
/// </summary>
internal static class SequenceEffectImportMenu
{
    public static IReadOnlyList<SequenceEffectImportMenuItem> Build() =>
    [
        new(
            "从底板 PSD 读回",
            "读当前动作导出底板时那份多图层 PSD（就在底板的目录里），按图层顺序当特效帧；"
            + "图层数必须等于底板张数（帧总长 × 2），对不上会先停下来报数",
            SequenceEffectImportSource.BasePlatePsd),
        new(
            "选择文件夹…",
            "挑一个装满 PNG 的目录，帧号取文件名末尾那段数字（导出底板那批 PNG 直接就能用）",
            SequenceEffectImportSource.Folder)
    ];
}
