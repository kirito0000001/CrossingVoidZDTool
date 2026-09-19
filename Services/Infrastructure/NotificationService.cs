using System;

namespace CrossingVoidZDTool.Services;

/// <summary>提示的严重程度。刻意不用 WinUI 的 <c>InfoBarSeverity</c>：这层不认识界面。</summary>
internal enum NotifySeverity
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>
/// 「告诉用户一句话」的唯一出口（B2）。
///
/// 以前这件事有两个各写一遍的版本：界面层直接 <c>ShowFloatingTip</c>（200 处），
/// 流程控制器自己带一个 <c>Notify(UnrealSyncNotice)</c>。于是「报错时别忘了同时写日志」
/// 全靠人记——catch 里只提示不写日志，事后就只剩用户的一句「它报了个错」。
///
/// 现在提示走这里、日志走 <c>ToolboxLog</c>，而 <see cref="Report"/>
/// 把「提示 + 日志」合成一次调用：**报错只需要记得调用一次**。
/// </summary>
internal interface INotificationService
{
    void Notify(NotifySeverity severity, string title, string message);
}

/// <summary>
/// 「用户点了什么」的出口（B3）。
///
/// 按钮以前长在界面层，顺手调 <c>LogUserOperation</c> 就记下了动作（User 配色 + 最近操作队列）。
/// 命令搬进 ViewModel 之后，它自己也得能记这一笔，否则日志里只剩结果、没有动作，
/// 「谁在什么时候点了全选」就再也查不出来。
/// </summary>
internal interface IUserOperationLog
{
    void LogUserOperation(string action);
}

internal static class NotificationServiceExtensions
{
    /// <summary>
    /// 提示 + 日志一次做完。catch 块里用它，就不会再出现「只弹了提示、日志里什么都没有」。
    /// </summary>
    public static void Report(
        this INotificationService service,
        NotifySeverity severity,
        string title,
        string message,
        Exception? error = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        service.Notify(severity, title, message);

        var detail = string.IsNullOrWhiteSpace(title) ? message : $"{title}：{message}";
        switch (severity)
        {
            case NotifySeverity.Error:
                ToolboxLog.Error(detail, error);
                break;
            case NotifySeverity.Warning:
                ToolboxLog.Warn(detail, error);
                break;
            default:
                ToolboxLog.Info(detail);
                break;
        }
    }
}
