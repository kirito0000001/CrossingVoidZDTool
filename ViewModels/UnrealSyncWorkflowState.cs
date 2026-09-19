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
    bool Step5Loaded,
    int Step35ItemCount,
    bool Step4Loaded,
    int Step4ItemCount,
    int Step4ErrorCount,
    int Step4PendingCount,
    bool Step6Loaded,
    int Step6ItemCount,
    int Step6ErrorCount,
    int Step6PendingCount,
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
        3 => inputs.Step3Loaded,
        4 => inputs.Step4Loaded,
        5 => inputs.Step5Loaded,
        6 => inputs.Step6Loaded,
        _ => false,
    };

    /// <summary>顶部那六个「待处理 / 进行中 / 已完成」徽标。</summary>
    public static string StepStatusText(in UnrealSyncWorkflowInputs inputs, int step) => step switch
    {
        1 => SimpleStatus(inputs.CurrentStep, 1),
        2 => SimpleStatus(inputs.CurrentStep, 2),
        3 => SimpleStatus(inputs.CurrentStep, 3),
        4 => inputs.CurrentStep < 4
            ? "待处理"
            : !inputs.Step4Loaded
                ? "进行中"
                : inputs.Step4ErrorCount > 0
                    ? "有错误"
                    : inputs.Step4PendingCount > 0
                        ? "待设置"
                        : "已完成",
        5 => inputs.CurrentStep < 5
            ? "待处理"
            : !inputs.HasDetectionRun
                ? "待检测"
                : "进行中",
        6 => inputs.CurrentStep < 6
            ? "待处理"
            : !inputs.Step6Loaded
                ? "进行中"
                : inputs.Step6ErrorCount > 0
                    ? "存在错误"
                    : inputs.Step6PendingCount > 0
                        ? "进行中"
                        : "已完成",
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
                            : inputs.Step35ItemCount == 0
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
            // 第一、二步的列表本身就是内容；三和五共用同一棵差异树。
            1 => inputs.Step1ItemCount,
            2 => inputs.Step2ItemCount,
            3 or 5 => inputs.Step35ItemCount,
            4 => inputs.Step4ItemCount,
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
