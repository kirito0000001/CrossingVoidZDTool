using System;

namespace CrossingVoidZDTool.ViewModels;

/// <summary>
/// 算中栏状态要的全部输入。
///
/// 刻意只放布尔和计数：没有集合、没有界面类型，所以「第几步该显示什么」
/// 可以脱离 WinUI 直接断言。以前这段逻辑是 <c>UnrealProjectSyncViewModel.WorkspaceState</c>
/// 里六十行嵌套三元，和集合、字段、界面类型缠在一起，只能靠跑起界面去看。
/// </summary>
internal readonly record struct UnrealSyncWorkflowInputs(
    bool IsImportDirection,
    bool HasSelectedSource,
    bool HasSelectedCharacter,
    int CurrentStep,
    bool HasFailure,
    bool IsOperationRunning,
    bool Step1Loaded,
    int Step1ItemCount,
    bool Step2Loaded,
    int Step2ItemCount,
    bool Step3Loaded,
    bool Step4Loaded,
    int SharedTreeItemCount,
    /// <summary>第四步那棵树里**还需要处理**的差异条数（Unchanged 不算）。</summary>
    int Step4PendingCount,
    int Step3ItemCount,
    int Step3ErrorCount,
    int Step3PendingCount,
    bool Step5Loaded,
    int Step5ItemCount,
    int Step5ErrorCount,
    int Step5PendingCount,
    bool Step6Loaded,
    int Step6ItemCount,
    bool HasDetectionRun);

/// <summary>
/// 六步流程的状态投影：**唯一一份**「现在这一步该显示什么」的规则。
///
/// 这是 P5 的第一步（只读投影）：先把规则从那个几千行的 ViewModel 里摘出来、
/// 变成纯函数并单测，再谈把状态字段搬进来。
///
/// 三条语义是用户当面确认过的，这里也写成断言（见回归用例）：
/// 1. **每一步的缓存互相独立** —— 某一步没加载/失败了，不影响别的步骤的已加载状态；
/// 2. **失败只影响当前步的显示**，不会把别的步已经设置好的数据作废；
/// 3. **忙碌优先于一切**（正在跑的时候，旧数据不再拿来显示，否则会以为检测没开始）。
/// </summary>
internal static class UnrealSyncWorkflowState
{
    public static bool IsStepLoaded(in UnrealSyncWorkflowInputs inputs, int step) => step switch
    {
        1 => inputs.Step1Loaded,
        2 => inputs.Step2Loaded,
        // 第 3 步是真的「基础配置」（收口后 4~7 前移成 3~6，空号那支已删）
        3 => inputs.Step3Loaded,
        4 => inputs.Step4Loaded,
        5 => inputs.Step5Loaded,
        // 第 6 步「特效同步」不看 Unreal，所以它的"加载过没有"就是自己的标志
        // （步加载表里 2/4/6 才是独立标志的步；1 看内容、3/5 看各自的检测结果）。
        6 => inputs.Step6Loaded,
        _ => false,
    };

    /// <summary>顶部那六个「待处理 / 进行中 / 已完成」徽标。</summary>
    public static string StepStatusText(in UnrealSyncWorkflowInputs inputs, int step) => step switch
    {
        1 => SimpleStatus(inputs.CurrentStep, 1),
        2 => SimpleStatus(inputs.CurrentStep, 2),
        // 第 3 步「基础配置」（收口后旧空号没了，原来那支 `3 => SimpleStatus` 已删）
        3 => inputs.CurrentStep < 3
            ? "待处理"
            : !inputs.Step3Loaded
                ? "进行中"
                : inputs.Step3ErrorCount > 0
                    ? "有错误"
                    : inputs.Step3PendingCount > 0
                        ? "待设置"
                        : "已完成",
        // 第 4 步「序列同步」：进过这一步要检测，检测完要看**还剩几条差异**。
        //
        // 🔴 这里以前是"检测过就恒报进行中"，少了最后那一档 —— 于是序列已经
        // 「共检查 173 项 · 无差异 173 项」了，徽标还挂着「进行中」，
        // 走到下一步回头看也是「进行中」（晓桀 2026-09-25 截图报的就是这个）。
        // 判据和第 2 步那条 `HasNoPublishChanges` 用同一个：**全是 Unchanged 才算完**。
        4 => inputs.CurrentStep < 4
            ? "待处理"
            : !inputs.HasDetectionRun
                ? "待检测"
                : inputs.Step4PendingCount > 0
                    ? "进行中"
                    : "已完成",
        // 第 5 步「蓝图置入」
        5 => inputs.CurrentStep < 5
            ? "待处理"
            : !inputs.Step5Loaded
                ? "进行中"
                : inputs.Step5ErrorCount > 0
                    ? "存在错误"
                    : inputs.Step5PendingCount > 0
                        ? "进行中"
                        : "已完成",
        // 第 6 步「特效同步」：检测是本地且便宜的，所以徽标说清"查过没有 / 有几个动作"。
        // 不说"已完成" —— 那要等真正写进 Unreal 才算，而这一步没有逐项勾选可看。
        6 => inputs.CurrentStep < 6
            ? "待处理"
            : !inputs.Step6Loaded
                ? "待检测"
                : inputs.Step6ItemCount > 0
                    ? "待同步"
                    : "无特效",
        _ => "待处理",
    };

    /// <summary>中栏这一刻该显示内容、占位、忙碌还是失败。</summary>
    public static UnrealSyncWorkspaceState ResolveWorkspaceState(in UnrealSyncWorkflowInputs inputs)
    {
        if (inputs.IsImportDirection)
        {
            // 导入方向没有分步流程，只有「检测出的差异树」一种内容。
            return !inputs.HasSelectedSource
                ? UnrealSyncWorkspaceState.NoCharacter
                : inputs.HasFailure
                    ? UnrealSyncWorkspaceState.Failed
                    : inputs.IsOperationRunning
                        ? UnrealSyncWorkspaceState.Busy
                        : !inputs.HasDetectionRun
                            ? UnrealSyncWorkspaceState.NotDetected
                            : inputs.SharedTreeItemCount == 0
                                ? UnrealSyncWorkspaceState.NoChanges
                                : UnrealSyncWorkspaceState.HasContent;
        }

        if (inputs.HasFailure)
        {
            return UnrealSyncWorkspaceState.Failed;
        }

        // 正在跑的时候一律是忙碌态：这一步的旧数据可能已经被清掉了，
        // 继续按旧数据显示会让人以为检测没开始。
        if (inputs.IsOperationRunning)
        {
            return UnrealSyncWorkspaceState.Busy;
        }

        // 这一步已经有数据就按数据说话。放在选角色判断之前是刻意的：
        // 手里明明有检测结果却显示「尚未选择角色」，比显示结果更让人困惑。
        if (!IsStepLoaded(inputs, inputs.CurrentStep))
        {
            return inputs.HasSelectedCharacter
                ? UnrealSyncWorkspaceState.NotDetected
                : UnrealSyncWorkspaceState.NoCharacter;
        }

        var itemCount = inputs.CurrentStep switch
        {
            // 第一、二步的列表本身就是内容；第四步和**第 2 步**共用同一棵差异树。
            // 第 2 步是合并后的「同步素材」，规整项和差异项都算它的内容，所以相加。
            1 => inputs.Step1ItemCount,
            2 => inputs.Step2ItemCount + inputs.SharedTreeItemCount,
            4 => inputs.SharedTreeItemCount,
            3 => inputs.Step3ItemCount,
            5 => inputs.Step5ItemCount,
            // 第 6 步的内容就是它自己的动作清单（没有共用树、没有勾选）。
            6 => inputs.Step6ItemCount,
            _ => 0,
        };

        return itemCount == 0
            ? UnrealSyncWorkspaceState.NoChanges
            : UnrealSyncWorkspaceState.HasContent;
    }

    private static string SimpleStatus(int currentStep, int step) =>
        currentStep > step ? "已完成" : currentStep == step ? "进行中" : "待处理";
}
