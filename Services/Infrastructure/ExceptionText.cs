using System;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 异常要显示给人看时的那一句话（C2 起成为共用规则）。
///
/// 「消息为空就用类型名、有内层异常就补一段」这套逻辑原来只长在
/// <c>MainWindow.CharacterDesk.cs</c> 的私有方法里；打开草稿的流程搬进 ViewModel 之后，
/// 两边都要用，就提到这里——**同一句话只该有一种拼法**，否则同一个错误在状态栏和
/// 浮层上会显示成两种样子。
/// </summary>
internal static class ExceptionText
{
    public static string ForTip(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var message = string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name
            : exception.Message;

        return exception.InnerException is null || string.IsNullOrWhiteSpace(exception.InnerException.Message)
            ? message
            : $"{message} / {exception.InnerException.Message}";
    }
}
