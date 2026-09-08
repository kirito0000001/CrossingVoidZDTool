using System;
using System.Linq;
using System.Threading.Tasks;

namespace CrossingVoidZDTool.ViewModels;

/// <summary>提示的严重程度，和界面上的 InfoBar 一一对应，但不依赖界面类型。</summary>
internal enum UnrealSyncNoticeSeverity
{
    Informational,
    Success,
    Warning,
    Error,
}

/// <summary>要告诉用户的一句话。控制器只负责说什么，怎么显示由界面决定。</summary>
internal readonly record struct UnrealSyncNotice(
    UnrealSyncNoticeSeverity Severity,
    string Title,
    string Message);

/// <summary>能不能离开这一步；不能的话 <see cref="Blocker"/> 说明卡在哪。</summary>
internal readonly record struct UnrealSyncStepGate(bool CanLeave, UnrealSyncNotice? Blocker)
{
    public static UnrealSyncStepGate Pass { get; } = new(true, null);

    public static UnrealSyncStepGate Block(string title, string message) =>
        new(false, new UnrealSyncNotice(UnrealSyncNoticeSeverity.Informational, title, message));
}

/// <summary>
/// 六步流程里界面那侧必须提供的东西。
///
/// 各步自己的检测实现牵着服务、进度条和一堆界面状态，留在 MainWindow 的
/// 分部文件里；控制器只要能「让第 N 步去检测」并且知道它跑完了就够了。
/// </summary>
internal interface IUnrealSyncWorkflowHost
{
    /// <summary>跑某一步自己的检测。必须是可等待的，否则依次检测会在没跑完时就往下走。</summary>
    Task RunStepDetectionAsync(int step);

    void Notify(UnrealSyncNotice notice);

    void Log(string message);
}

/// <summary>
/// 六步流程的编排：上一步、下一步、重新加载、依次检测，以及唯一的进入口。
///
/// 从 MainWindow 里搬出来是为了能真跑着测。之前这些规则只能靠在回归用例里
/// 匹配 MainWindow 的源码文本来保护——「源码里出现过 EnterWorkflowStepAsync」
/// 这种断言既挡不住逻辑写错，改个命名还会假报警。搬出来之后可以拿一个假的
/// Host 把六步走一遍，直接断言「第几步真的去检测了、卡在哪一步、有没有多跑」。
///
/// 这里不碰界面，也不直接调服务：所有会改动 Unreal 工程的动作都在各步自己的
/// 按钮里，控制器只做导航和「要不要检测」的决定。
/// </summary>
internal sealed class UnrealSyncWorkflowController(
    UnrealProjectSyncViewModel sync,
    IUnrealSyncWorkflowHost host)
{
    private readonly UnrealProjectSyncViewModel _sync = sync;
    private readonly IUnrealSyncWorkflowHost _host = host;

    private string? SelectedCharacterCode => _sync.SelectedSource?.DraftCharacter?.Code;

    /// <summary>
    /// 回退只导航，绝不触发检测。
    ///
    /// 往回走是「我要看看上一步」，不是「重新查一遍上一步」。那一步没缓存时
    /// 中栏会显示未检测占位，要不要真查由用户点「重新加载」决定。
    /// </summary>
    public void GoToPreviousStep()
    {
        _sync.ReturnToWorkflowStep(Math.Max(UnrealSyncWorkflow.MinStep, _sync.WorkflowStep - 1));
    }

    public async Task GoToNextStepAsync()
    {
        var step = _sync.WorkflowStep;
        if (step >= UnrealSyncWorkflow.MaxStep)
        {
            _host.Notify(new UnrealSyncNotice(
                UnrealSyncNoticeSeverity.Informational, "已经是最后一步", "蓝图置入完成后本次同步就结束了。"));
            return;
        }

        var gate = CanLeaveStep(step);
        if (!gate.CanLeave)
        {
            if (gate.Blocker is { } blocker)
            {
                _host.Notify(blocker);
            }

            return;
        }

        // 第三步确认没有待同步内容后，要把「同步结果」面板收好再走，
        // 否则第四步会带着第三步的操作提示。这里不能再触发一次素材同步事件，
        // 那会重复执行同步前检测和同步操作。
        if (step == 3)
        {
            _sync.CompletePublishOperation(0, 0);
        }

        await EnterStepAsync(step + 1);
    }

    /// <summary>
    /// 进入某一步：先落步，再按需检测。
    ///
    /// 两件事必须分开。先落步是按钮的本职——检测失败、被别的操作占用、
    /// 或者干脆不检测，都不该把人卡在上一步。按需是指这一步已经有数据
    /// （内存里的，或刚从该步缓存恢复的）时就不再跑虚幻：六步来回切，
    /// 每次都重检测纯粹是干等，离线一次就是十几秒。
    /// </summary>
    public async Task EnterStepAsync(int step, bool forceReload = false)
    {
        _sync.ReturnToWorkflowStep(step);
        if (!forceReload && _sync.IsWorkflowStepLoaded(step))
        {
            _host.Log($"[Workflow] step={step} 复用本步缓存，未重新检测 Unreal。");
            return;
        }

        if (!RequireSelectedCharacter())
        {
            return;
        }

        await _host.RunStepDetectionAsync(step);
    }

    /// <summary>「重新加载」是用户明确要求重查，无论本步有没有缓存都要真跑一次。</summary>
    public async Task ReloadCurrentStepAsync()
    {
        if (!RequireSelectedCharacter())
        {
            return;
        }

        await EnterStepAsync(_sync.WorkflowStep, forceReload: true);
    }

    /// <summary>
    /// 从当前步骤往后依次检测，停在第一个需要人处理的步骤。
    ///
    /// 只检测，不写入。第三、五步真正的同步和第四、六步的写入都会改动
    /// Unreal 工程，那是要人确认的事，不该被一个按钮顺手做掉；
    /// 这里的价值是把六步的等待一次排完，而不是替人做决定。
    /// </summary>
    public async Task DetectAllStepsAsync()
    {
        if (!RequireSelectedCharacter())
        {
            return;
        }

        var startStep = _sync.WorkflowStep;
        for (var step = startStep; step <= UnrealSyncWorkflow.MaxStep; step++)
        {
            await EnterStepAsync(step);
            if (_sync.WorkspaceState == UnrealSyncWorkspaceState.Failed)
            {
                _host.Notify(new UnrealSyncNotice(
                    UnrealSyncNoticeSeverity.Error,
                    $"第 {step} 步检测失败",
                    _sync.WorkspacePlaceholderDescription));
                return;
            }

            if (step == UnrealSyncWorkflow.MaxStep)
            {
                break;
            }

            // 这一步还有事要做就停下来，让人处理完再继续。
            var gate = CanLeaveStep(step);
            if (!gate.CanLeave)
            {
                if (gate.Blocker is { } blocker)
                {
                    _host.Notify(blocker);
                }

                return;
            }
        }

        _host.Notify(new UnrealSyncNotice(
            UnrealSyncNoticeSeverity.Success,
            "六步检测已跑完",
            $"从第 {startStep} 步检测到第 {UnrealSyncWorkflow.MaxStep} 步，没有需要先处理的内容。"));
    }

    /// <summary>
    /// 当前步骤是否满足离开条件；不满足时给出这一步自己的提示。
    /// 只做判断，不改任何状态——依次检测要靠它试探能不能往下走。
    /// </summary>
    public UnrealSyncStepGate CanLeaveStep(int step)
    {
        switch (step)
        {
            case 1 when !_sync.CanAdvanceWorkflow:
                return UnrealSyncStepGate.Block("第一步尚未完成", "请先选择角色，并完成底层检测。");
            case 2 when !_sync.IsNormalizationStepLoaded:
                return UnrealSyncStepGate.Block(
                    "规整素材尚未加载完成", "请等待当前加载完成，或点击“重新加载规整素材”。");
            case 2 when _sync.NormalizationItems.Any(item => !item.IsResolved):
                return UnrealSyncStepGate.Block(
                    "第二步尚未完成",
                    $"还有 {_sync.NormalizationItems.Count(item => !item.IsResolved)} 项 Unreal 素材没有选择处理方式。");
            case 3 when !_sync.HasNoPublishChanges:
                return UnrealSyncStepGate.Block(
                    "第三步尚未完成", "请先同步已勾选素材；确认没有待同步内容后，才能进入基础配置。");
            case 4 when !_sync.CanAdvanceWorkflow:
                return UnrealSyncStepGate.Block("第四步尚未完成", "请先完成基础配置。");
            case 5 when !_sync.CanAdvanceWorkflow:
                return UnrealSyncStepGate.Block(
                    "第五步尚未完成", "请先同步已勾选的序列；确认没有待同步内容后，才能进入蓝图置入。");
            default:
                return UnrealSyncStepGate.Pass;
        }
    }

    private bool RequireSelectedCharacter()
    {
        if (!string.IsNullOrWhiteSpace(SelectedCharacterCode))
        {
            return true;
        }

        _host.Notify(new UnrealSyncNotice(
            UnrealSyncNoticeSeverity.Warning, "未选择已完成角色", "请先在左侧选择一个已完成角色。"));
        return false;
    }
}
