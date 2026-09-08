using System;
using System.Collections.Generic;
using System.Linq;

namespace CrossingVoidZDTool.Services;

/// <summary>进度条上分配给某个阶段的区间。</summary>
internal readonly record struct WorkflowProgressBand(double Start, double End)
{
    /// <summary>把阶段内部的 0-100 子进度映射到这一段。</summary>
    public double At(double subPercent) =>
        Start + (End - Start) * Math.Clamp(subPercent, 0, 100) / 100d;
}

/// <summary>
/// 按阶段实测耗时分配进度百分比。
///
/// 以前是写死的几个数字（导出完直接跳 40，桥接 58，复扫 86），而且两次 Unreal 导出
/// 根本没接进度回调：一次同步里有两段各十几秒的纯静默，进度条一动不动，
/// 看起来就像卡死。权重取自实测：同步前导出 ≈15s、桥接 ≈13s、复扫 ≈15s，
/// 开了同步前备份时备份本身 ≈66s——它确实会主导整条进度条，这是事实，不该抹平。
/// </summary>
internal sealed class WorkflowProgressPlan
{
    public const string PreflightExport = "preflight-export";
    public const string Backup = "backup";
    public const string BridgeExecute = "bridge-execute";
    public const string RescanExport = "rescan-export";
    public const string Scan = "scan";
    public const string Apply = "apply";
    public const string Verify = "verify";

    /// <summary>收尾（写基线、重建选择树）留出的尾巴，不参与权重分配。</summary>
    private const double FinalizeReserve = 2d;

    private readonly Dictionary<string, WorkflowProgressBand> _bands;
    private readonly List<string> _order;

    private WorkflowProgressPlan(IReadOnlyList<(string Phase, double Weight)> phases)
    {
        var total = phases.Sum(item => item.Weight);
        _bands = new Dictionary<string, WorkflowProgressBand>(StringComparer.OrdinalIgnoreCase);
        _order = phases.Select(item => item.Phase).ToList();
        var cursor = 0d;
        var usable = 100d - FinalizeReserve;
        foreach (var (phase, weight) in phases)
        {
            var span = total <= 0 ? 0 : usable * weight / total;
            _bands[phase] = new WorkflowProgressBand(cursor, cursor + span);
            cursor += span;
        }
    }

    /// <summary>这条流程一共几个阶段，用于「阶段 N/M」。</summary>
    public int PhaseCount => _order.Count;

    /// <summary>某个阶段是第几个，从 1 开始；不认识的阶段返回 1。</summary>
    public int PhaseNumber(string phase)
    {
        var index = _order.FindIndex(item => string.Equals(item, phase, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? 1 : index + 1;
    }

    /// <summary>「阶段 2/4 · 正在压缩备份」里的前半截。</summary>
    public string Caption(string phase, string title) =>
        PhaseCount <= 1 ? title : $"阶段 {PhaseNumber(phase)}/{PhaseCount} · {title}";

    /// <summary>第五步序列同步：同步前导出 → （备份）→ 桥接执行 → 复扫导出。</summary>
    public static WorkflowProgressPlan ForSequenceSync(bool includesBackup)
    {
        var phases = new List<(string, double)> { (PreflightExport, 15d) };
        if (includesBackup)
        {
            // 压一份几个 G 的工程实测约 66 秒，是这条流程里最长的一段。
            phases.Add((Backup, 66d));
        }

        phases.Add((BridgeExecute, 13d));
        phases.Add((RescanExport, 15d));
        return new WorkflowProgressPlan(phases);
    }

    /// <summary>只做差异检测：唯一的重活就是那次 Unreal 导出。</summary>
    public static WorkflowProgressPlan ForDetection() =>
        new([(PreflightExport, 100d)]);

    /// <summary>
    /// 第四步和第六步的扫描：一次 Unreal 往返，没有别的重活。
    /// 单阶段也走这套，是为了「阶段 N/M · 已用时」的显示对所有步骤一致。
    /// </summary>
    public static WorkflowProgressPlan ForStepScan() =>
        new([(Scan, 100d)]);

    /// <summary>
    /// 第四步和第六步的写入：（备份）→ 写入 → 复查。
    /// 复查是脚本写完之后顺手重扫的那一遍，占比不大但确实要等。
    /// </summary>
    public static WorkflowProgressPlan ForStepApply(bool includesBackup)
    {
        var phases = new List<(string, double)>();
        if (includesBackup)
        {
            phases.Add((Backup, 66d));
        }

        phases.Add((Apply, 12d));
        phases.Add((Verify, 6d));
        return new WorkflowProgressPlan(phases);
    }

    public WorkflowProgressBand this[string phase] =>
        _bands.TryGetValue(phase, out var band) ? band : new WorkflowProgressBand(0, 100 - FinalizeReserve);
}
