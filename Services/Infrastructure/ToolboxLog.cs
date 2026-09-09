using System;

namespace CrossingVoidZDTool.Services;

/// <summary>日志分级。和界面上的日志分类对齐，但不依赖任何 WinUI 类型。</summary>
internal enum ToolboxLogLevel
{
    Info,
    Warning,
    Error,
}

/// <summary>Service 层写日志的出口。界面层实现它并接到底部日志面板。</summary>
internal interface IToolboxLogSink
{
    void Write(ToolboxLogLevel level, string message, Exception? error);
}

/// <summary>
/// Service 层的日志出口。
///
/// 在此之前，Services 整层一万六千行、约九十处 <c>catch</c>，**一个日志出口都没有**。
/// 每一次「悄悄吞掉异常」都是彻底静默的，出问题只能靠猜——本项目反复出现的
/// 「显示成功但实际没做成」（蓝图保存失败、settings 损坏、读到上一轮的结果文件……）
/// 都和这件事直接相关。
///
/// 这里刻意做成静态门面而不是构造函数注入：Services 层有约三百处
/// <c>new XxxService()</c>，逐个加构造参数意味着三百处改动，对单人维护的工具
/// 不划算。代价是引入了一处全局可变状态——所以把它限制到最小：
/// 只有一个 sink、只在组合根设一次、默认空实现，且不参与任何业务判断。
///
/// **约定：从此每个 <c>catch</c> 要么向上抛，要么写一行日志说明为什么吞。
/// 不允许静默 catch。**
/// </summary>
internal static class ToolboxLog
{
    private static IToolboxLogSink? _sink;

    /// <summary>组合根在启动时接上界面的日志面板。传 null 可以摘掉（测试收尾用）。</summary>
    public static void SetSink(IToolboxLogSink? sink) => _sink = sink;

    public static void Info(string message) => Write(ToolboxLogLevel.Info, message, null);

    public static void Warn(string message, Exception? error = null) =>
        Write(ToolboxLogLevel.Warning, message, error);

    public static void Error(string message, Exception? error = null) =>
        Write(ToolboxLogLevel.Error, message, error);

    private static void Write(ToolboxLogLevel level, string message, Exception? error)
    {
        var sink = _sink;
        if (sink is null || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        try
        {
            sink.Write(level, message, error);
        }
        catch
        {
            // 日志本身不能把业务流程带崩。这是全项目唯一允许的静默 catch。
        }
    }
}
