using System.Collections.Generic;

namespace CrossingVoidZDTool.ViewModels;

/// <summary>序列帧编辑器「导出」菜单里的一项能干什么。</summary>
internal enum SequenceExportAction
{
    /// <summary>把当前动作打成一张图集（含坐标 json）。</summary>
    Atlas,

    /// <summary>按动作帧率的倍数逐帧导出底板 PNG（给特效绘制对照用）。</summary>
    BasePlate
}

/// <summary>一条导出项：文案 + 悬浮说明 + 干什么。</summary>
internal sealed record SequenceExportMenuItem(string Text, string ToolTip, SequenceExportAction Action);

/// <summary>
/// 序列帧编辑器右上角那个「导出」菜单的内容。
///
/// **为什么要有这么一层**：工具条那一排已经快满了（FPS / 循环播放 / 语音结束暂停 / 导出…），
/// 以后还会有更多导出物，往一排里塞按钮迟早放不下。所以入口收成**一个**「导出」按钮，
/// 菜单内容走这份**数据清单**：
///
/// <code>
/// 加一个新导出 = 这里加一条 + SequenceFramesCommands 加一个命令 + 宿主实现一个方法
/// </code>
///
/// 三处都在一处能看见，XAML 完全不用动 —— 这就是"能长久扩展"的做法。
/// </summary>
internal static class SequenceExportMenu
{
    public static IReadOnlyList<SequenceExportMenuItem> Build() =>
    [
        new(
            "导出图集",
            "把当前动作的序列帧打成一张图集（含坐标 json），落到 Export/<角色>/Atlas/",
            SequenceExportAction.Atlas),
        new(
            "导出底板（2 倍帧）",
            "按动作帧率的 2 倍逐帧导出 PNG，给特效绘制对照用；落 Export/<角色>/BasePlate/<动作>-2x/",
            SequenceExportAction.BasePlate)
    ];
}
