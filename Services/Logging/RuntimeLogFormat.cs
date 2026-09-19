using System;
using System.Text.RegularExpressions;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 一条日志属于「哪一次流程、哪一步」。两个值都写进文件的每一行，
/// 面板上只把步骤显示成 <c>[St5]</c> 前缀。
/// </summary>
internal readonly record struct RuntimeLogScope(string RunId, int Step)
{
    /// <summary>没有当前批次、也和步骤无关时用的空范围。</summary>
    public static RuntimeLogScope None { get; } = new(RuntimeLogFormat.NoRunId, 0);

    /// <summary>面板上的步骤前缀；step 为 0 时是空串（不显示前缀）。</summary>
    public string StepTag => Step is >= 1 and <= 6 ? $"St{Step}" : string.Empty;
}

/// <summary>
/// 日志的三种行形态。全是纯函数，所以「格式有没有真的按约定写」可以直接断言，
/// 不用启动界面。
///
/// 以前这三样都散在 <c>MainWindow.Logging.cs</c> 里现拼字符串：面板行拼一次、
/// 文件行再拼一次（还各自嵌了一遍 <c>[HH:mm:ss]</c>），而「这是哪一步、哪一次」
/// 干脆没有——第三步和第五步共用 <c>[Sync Execution]</c> 这种 tag，
/// 事后翻日志分不出谁是谁。
/// </summary>
internal static class RuntimeLogFormat
{
    /// <summary>没有当前批次时的占位。用 <c>-</c> 而不是空串，方便 rg 一眼看出来。</summary>
    public const string NoRunId = "-";

    public const string RunHeaderPrefix = "=====";
    public const string StepStartGlyph = "▶";
    public const string StepEndGlyph = "■";

    private static readonly Regex ScopePattern = new(
        @"run=(?<run>\S+)\s+step=(?<step>\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// 批次号：<c>R-yyyyMMdd-HHmm-XXXX</c>。后四位是十六进制，
    /// 同一分钟里连着点两次也能分开。
    /// </summary>
    public static string CreateRunId(DateTimeOffset now, int suffix) =>
        $"R-{now:yyyyMMdd-HHmm}-{(ushort)suffix:x4}";

    public static string CreateRunId(DateTimeOffset now) =>
        CreateRunId(now, Random.Shared.Next(0, 0x10000));

    /// <summary>文件行：时间戳精确到毫秒，后面固定跟 run/step。</summary>
    public static string FormatFileLine(DateTimeOffset now, RuntimeLogScope scope, string body) =>
        $"[{now:yyyy-MM-dd HH:mm:ss.fff}] run={scope.RunId} step={scope.Step} {body}";

    /// <summary>面板行：只到秒，步骤用 <c>[StN]</c> 前缀；step=0 时不加前缀。</summary>
    public static string FormatPanelLine(DateTimeOffset now, RuntimeLogScope scope, string body)
    {
        var tag = scope.StepTag;
        return tag.Length == 0
            ? $"[{now:HH:mm:ss}] {body}"
            : $"[{now:HH:mm:ss}] [{tag}] {body}";
    }

    /// <summary>批次首行。它是一条 Sticky 行，面板里永远不会被明细挤掉。</summary>
    public static string FormatRunHeader(string runId, string characterName, string directionTitle) =>
        $"{RunHeaderPrefix} run {runId} · 角色={characterName} · 方向={directionTitle} {RunHeaderPrefix}";

    /// <summary>进入某一步的标题行。</summary>
    public static string FormatStepStart(int step, string stepName) =>
        $"{StepStartGlyph} 第 {step} 步 · {stepName}";

    /// <summary>
    /// 离开某一步的标题行。结论复用各步**已经存在**的摘要文案
    /// （<c>UnrealProjectSyncViewModel.WorkflowStepConclusionText</c>），
    /// 不在这里另造一套说法。
    /// </summary>
    public static string FormatStepEnd(int step, string conclusion) =>
        $"{StepEndGlyph} 第 {step} 步 · 结束：{(string.IsNullOrWhiteSpace(conclusion) ? "无摘要" : conclusion)}";

    /// <summary>
    /// 从一行文件日志里读回 run/step。守卫用例用它，将来要做「按批次过滤」也用它。
    /// </summary>
    public static bool TryParseScope(string? line, out RuntimeLogScope scope)
    {
        scope = RuntimeLogScope.None;
        if (string.IsNullOrEmpty(line))
        {
            return false;
        }

        var match = ScopePattern.Match(line);
        if (!match.Success || !int.TryParse(match.Groups["step"].Value, out var step))
        {
            return false;
        }

        scope = new RuntimeLogScope(match.Groups["run"].Value, step);
        return true;
    }
}
