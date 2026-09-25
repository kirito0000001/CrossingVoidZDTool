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

    // 收口之后（4~7 → 3~6、MaxStep = 6）**没有空号了**：原来那套
    // `VoidStep` / `SkipVoidStep` / `SkipVoidStepBackwards` 已经删掉 ——
    // 第 3 步现在是真的「基础配置」，再跳过它就跳错了。

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
        // 认**界面上**的最后一步：特效隐藏时它就是第 5 步（蓝图置入），
        // 再点「下一步」不该把人送进一个看不见的步骤。
        if (step >= UnrealSyncWorkflow.LastVisibleStep)
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

        // **往前走不再顺手"收尾"**：旧第 3 步（现第 2 步）时代这里会调 `CompletePublishOperation(0, 0)`，
        // 把同步结果面板收好再走。但那个方法会 `SetSelectionTree([])` + 清掉差异 + 跳到第 3 步 ——
        // 于是"从第 2 步往前走一趟再回来"就等于把这一步的检测结果扔了
        // （2026-09-24 实测：从基础配置往回切，第 2 步变成"尚未检测"）。
        // 每步的状态现在归自己（各步自己的小缓存），导航不该清它。
        await EnterStepAsync(step + 1);
    }

    /// <summary>
    /// 进入某一步：**只落步 + 读这一步自己的缓存**，绝不跑虚幻。
    ///
    /// 晓桀 2026-09-24 定：「去除自动检测，只由我进行检测操作」。
    /// 以前这里会"该步没数据就顺手检测一次"，于是往上一步、往下走、切角色
    /// 都可能背地里起一次虚幻（离线一次十几秒）。现在检测只剩两个**手动**入口：
    /// <see cref="ReloadCurrentStepAsync"/>（「重新加载」）与
    /// <see cref="DetectAllStepsAsync"/>（「依次检测后续步骤」）。
    ///
    /// 这也是"把检查做成一个独立阶段"的前提：进入步骤只负责**把界面切过去**。
    /// </summary>
    public Task EnterStepAsync(int step)
    {
        _sync.ReturnToWorkflowStep(step);
        return Task.CompletedTask;
    }

    /// <summary>「重新加载」：用户明确要求重查 —— 这是**手动检测入口**，真跑一次。</summary>
    public async Task ReloadCurrentStepAsync()
    {
        if (!RequireSelectedCharacter())
        {
            return;
        }

        var step = _sync.WorkflowStep;
        _sync.ReturnToWorkflowStep(step);
        await _host.RunStepDetectionAsync(step);
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
        for (var step = startStep; step <= UnrealSyncWorkflow.LastVisibleStep; step++)
        {
            await EnterStepAsync(step);
            // 进步骤本身不再检测了，所以"依次检测"要自己显式跑这一步的检测。
            await _host.RunStepDetectionAsync(step);
            if (_sync.WorkspaceState == UnrealSyncWorkspaceState.Failed)
            {
                _host.Notify(new UnrealSyncNotice(
                    UnrealSyncNoticeSeverity.Error,
                    $"第 {step} 步检测失败",
                    _sync.WorkspacePlaceholderDescription));
                return;
            }

            if (step == UnrealSyncWorkflow.LastVisibleStep)
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
            // 「六步」是收口前的说法；特效隐藏之后界面上只有五步，标题别再报数字。
            "依次检测已跑完",
            $"从第 {startStep} 步检测到第 {UnrealSyncWorkflow.LastVisibleStep} 步，没有需要先处理的内容。"));
    }

    /// <summary>
    /// 当前步骤是否满足离开条件。
    ///
    /// **③ 已定：每一步都不阻断同步 → 这里恒放行。**
    /// 以前这一支是六道门（底层检测要全绿、素材要规整全处理完、要没有待同步…），
    /// 现在全部摘掉。晓桀的原话是"以后每一步都不会阻断同步，我最后会专门设计**检查阶段**"——
    /// 也就是说"检查"将来会是一件独立的事，不该长在每步的出口上。
    ///
    /// **这个方法是留给那个阶段的唯一接管点**：现在的判断散在
    /// <c>AdvanceWorkflowStep</c> / <c>CanAdvanceWorkflow</c> / 各处 <c>Validate…</c> 里，
    /// 以后收时候一并收进这里（或它旁边的纯函数），别再散回去。
    /// 本轮**只卸不加**，不预设那个阶段长什么样。
    /// </summary>
    public UnrealSyncStepGate CanLeaveStep(int step)
    {
        _ = step;
        return UnrealSyncStepGate.Pass;
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
