namespace CrossingVoidZDTool.Services;

/// <summary>
/// 剪贴板出口（B3 收尾）。
///
/// 「复制第 N 步清单」以前是 MainWindow 的 Click 处理器：取报告、写剪贴板、提示、记日志。
/// 前两件是界面能力，后两件是命令自己的事——把它拆开之后，
/// 命令能在没有窗口的情况下跑，剪贴板由壳提供。
///
/// 返回值表示**有没有真的写进去**：拿不到剪贴板时命令不该谎报「已复制」。
/// </summary>
internal interface IClipboardService
{
    bool TryCopyText(string text);
}
