using System.Collections.Generic;

namespace CrossingVoidZDTool.ViewModels;

/// <summary>特效帧从哪来。</summary>
internal enum SequenceEffectImportSource
{
    /// <summary>默认那条：自己挑一份多图层 PSD，按图层组逐帧当特效帧。</summary>
    PsdFile,

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
/// 顺序就是菜单里的顺序，第一条是默认那条（从 PSD 导入）。
/// </summary>
internal static class SequenceEffectImportMenu
{
    public static IReadOnlyList<SequenceEffectImportMenuItem> Build() =>
    [
        new(
            "从 PSD 导入…",
            "挑一份多图层 PSD（默认开在当前动作的底板目录 —— 「导出底板」那份就在那儿），"
            + "按图层组逐帧读回特效；组少几个算空帧、多几个忽略，都会报数；"
            + "读回来之前会先问放进第几层",
            SequenceEffectImportSource.PsdFile),
        new(
            "选择文件夹…",
            "挑一个装满 PNG 的目录，帧号取文件名末尾那段数字（导出底板那批 PNG 直接就能用）；"
            + "导进来之前会先问放进第几层",
            SequenceEffectImportSource.Folder)
    ];
}
