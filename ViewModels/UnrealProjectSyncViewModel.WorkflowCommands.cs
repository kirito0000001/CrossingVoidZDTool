using System.Threading.Tasks;
using CrossingVoidZDTool.Services;

namespace CrossingVoidZDTool.ViewModels;

/// <summary>
/// 右栏那四个流程按钮的命令（B3 尾款）：上一步、下一步、依次检测、重新加载。
///
/// 它们以前是 MainWindow 的 Click 处理器，直接持有 <c>UnrealSyncWorkflowController</c>。
/// 现在控制器由 ViewModel 持有——控制器本来就不认识界面，它要的只是
/// <see cref="IUnrealSyncWorkflowHost"/>，所以换个持有者不影响任何规则，
/// 却让这四条命令可以脱离 XAML 被直接执行和断言。
/// </summary>
internal sealed partial class UnrealProjectSyncViewModel
{
    private UnrealSyncWorkflowController? _workflowController;

    /// <summary>
    /// 六步流程需要界面提供的能力（跑某一步的检测、显示提示、写日志）。
    /// 由壳在启动时接上，和 <c>UserOperations</c> / <c>ToolboxLog.SetSink</c> 同一时机；
    /// 回归里单独构造的 VM 没有它，命令就什么都不做（而不是抛）。
    /// </summary>
    internal IUnrealSyncWorkflowHost? WorkflowHost { get; set; }

    private RelayCommand? _previousStepCommand;
    private AsyncRelayCommand? _nextStepCommand;
    private AsyncRelayCommand? _detectAllStepsCommand;
    private AsyncRelayCommand? _reloadStepCommand;

    /// <summary>上一步：只导航，绝不触发检测（「我要看看上一步」不是「重新查一遍」）。</summary>
    public RelayCommand PreviousStepCommand => _previousStepCommand ??= new RelayCommand(() =>
    {
        LogWorkflowOperation("同步流程：上一步");
        WorkflowController?.GoToPreviousStep();
    });

    /// <summary>下一步：先落步，再按需检测这一步。</summary>
    public AsyncRelayCommand NextStepCommand => _nextStepCommand ??= new AsyncRelayCommand(async () =>
    {
        LogWorkflowOperation("同步流程：下一步");
        if (WorkflowController is { } controller)
        {
            await controller.GoToNextStepAsync();
        }
    });

    /// <summary>从当前步往后依次检测，停在第一个需要人处理的步骤；只检测、不写入。</summary>
    public AsyncRelayCommand DetectAllStepsCommand => _detectAllStepsCommand ??= new AsyncRelayCommand(async () =>
    {
        LogWorkflowOperation($"同步流程：从第 {WorkflowStep} 步起依次检测");
        if (WorkflowController is { } controller)
        {
            await controller.DetectAllStepsAsync();
        }
    });

    /// <summary>重新加载当前步：用户明确要求重查，不看缓存。</summary>
    public AsyncRelayCommand ReloadStepCommand => _reloadStepCommand ??= new AsyncRelayCommand(async () =>
    {
        LogWorkflowOperation($"同步流程：重新加载第 {WorkflowStep} 步");
        if (WorkflowController is { } controller)
        {
            await controller.ReloadCurrentStepAsync();
        }
    });

    /// <summary>提示出口（B2）。命令自己报错用，没接上就只写日志。</summary>
    internal INotificationService? Notifications { get; set; }

    /// <summary>剪贴板出口。命令不直接碰剪贴板。</summary>
    internal IClipboardService? Clipboard { get; set; }

    private RelayCommand? _copyStepReportCommand;

    /// <summary>复制当前步骤的差异清单。</summary>
    public RelayCommand CopyStepReportCommand => _copyStepReportCommand ??= new RelayCommand(() =>
    {
        var report = BuildStepChangeReport();
        if (string.IsNullOrWhiteSpace(report))
        {
            Notifications?.Report(NotifySeverity.Info, "没有可复制的内容", "这一步还没有检测结果。");
            return;
        }

        // 拿不到剪贴板时不谎报「已复制」——以前这一步是无条件提示成功的。
        if (Clipboard?.TryCopyText(report) != true)
        {
            Notifications?.Report(NotifySeverity.Warning, "剪贴板不可用", "清单没有复制成功，请稍后再试。");
            return;
        }

        UserOperations?.LogUserOperation($"同步流程：复制第 {WorkflowStep} 步清单");
        Notifications?.Report(
            NotifySeverity.Success,
            "清单已复制",
            $"第 {WorkflowStep} 步 {WorkflowStepName} 的差异已复制到剪贴板。");
    });

    private UnrealSyncWorkflowController? WorkflowController =>
        WorkflowHost is null
            ? null
            : _workflowController ??= new UnrealSyncWorkflowController(this, WorkflowHost);

    private void LogWorkflowOperation(string action) => UserOperations?.LogUserOperation(action);
}
