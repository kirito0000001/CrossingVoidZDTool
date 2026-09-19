using System;
using System.Collections.Generic;
using System.Linq;

namespace CrossingVoidZDTool.ViewModels;

/// <summary>
/// 「哪个输入喂着哪些派生属性」的唯一清单（P3a）。
///
/// 这些名字以前是**手写**在各个 setter 里的：改步号要顺着三十多行对齐着补
/// <c>OnPropertyChanged</c>，改某个加载标志再补几条；而改同一个输入的路径有十几条，
/// 每条都得记得补。漏一条就是一次真实的界面 bug——中栏一片空白、
/// 第三/五步勾选计数不刷新，都是这么来的。
///
/// 现在每条输入只在自己的 setter 里报「我属于哪一组」，清单集中在这一个文件；
/// 回归用例 <c>DerivedNotificationsCoverWorkflowProperties</c> 用反射检查
/// 「新加的 Workflow* 派生属性有没有进清单」，所以再加属性时漏不掉。
///
/// **为什么不用 <c>[NotifyPropertyChangedFor]</c>**（2026-09-18 实测）：
/// 本仓库的 CommunityToolkit.Mvvm 8.4.2 不支持分部属性——把 <c>LangVersion</c> 提到 13
/// 并写成 <c>public partial bool X { get; set; }</c>，生成器不产出实现部分，直接 CS9248；
/// 而挂在字段上的写法在 WinUI 3 下会报 MVVMTK0045（生成的代码在 WinRT/AOT 场景不可用），
/// 本项目 Release 是 <c>PublishTrimmed</c>。两条路都要么编译不过、要么引入新的运行时风险，
/// 所以改用「私有 setter + 集中清单 + 反射守卫」——同样让「漏通知」编译期就撞红，
/// 且不依赖生成器行为。
/// </summary>
internal static class UnrealSyncDerivedNotifications
{
    /// <summary>步号变化时受影响的一切。</summary>
    public static readonly string[] WorkflowStep =
    [
        nameof(UnrealProjectSyncViewModel.WorkflowStep1StatusText),
        nameof(UnrealProjectSyncViewModel.WorkflowStep2StatusText),
        nameof(UnrealProjectSyncViewModel.WorkflowStep3StatusText),
        nameof(UnrealProjectSyncViewModel.WorkflowStep4StatusText),
        nameof(UnrealProjectSyncViewModel.WorkflowStep5StatusText),
        nameof(UnrealProjectSyncViewModel.WorkflowStep6StatusText),
        nameof(UnrealProjectSyncViewModel.WorkflowNextText),
        nameof(UnrealProjectSyncViewModel.WorkflowReloadText),
        nameof(UnrealProjectSyncViewModel.WorkflowConfirmationVisibility),
        nameof(UnrealProjectSyncViewModel.NormalizationDetailsVisibility),
        nameof(UnrealProjectSyncViewModel.WorkflowNextButtonVisibility),
        nameof(UnrealProjectSyncViewModel.WorkflowNextButtonEnabled),
        nameof(UnrealProjectSyncViewModel.IsFoundationWorkspace),
        nameof(UnrealProjectSyncViewModel.IsLightConfigurationWorkspace),
        nameof(UnrealProjectSyncViewModel.IsBlueprintSetupWorkspace),
        nameof(UnrealProjectSyncViewModel.IsSequenceSynchronizationWorkspace),
        nameof(UnrealProjectSyncViewModel.FoundationWorkspaceVisibility),
        nameof(UnrealProjectSyncViewModel.FoundationDetailsVisibility),
        nameof(UnrealProjectSyncViewModel.LightConfigurationWorkspaceVisibility),
        nameof(UnrealProjectSyncViewModel.LightConfigurationDetailsVisibility),
        nameof(UnrealProjectSyncViewModel.BlueprintSetupWorkspaceVisibility),
        nameof(UnrealProjectSyncViewModel.BlueprintSetupDetailsVisibility),
        nameof(UnrealProjectSyncViewModel.SequenceSynchronizationDetailsVisibility),
        nameof(UnrealProjectSyncViewModel.WorkspaceTitle),
        nameof(UnrealProjectSyncViewModel.WorkspaceDescription),
        nameof(UnrealProjectSyncViewModel.SelectionContentVisibility),
        nameof(UnrealProjectSyncViewModel.CanAdvanceWorkflow),
        nameof(UnrealProjectSyncViewModel.CanApplyLightConfiguration),
        nameof(UnrealProjectSyncViewModel.CanApplyBlueprintSetup),
        nameof(UnrealProjectSyncViewModel.HasNoPublishChanges),
        nameof(UnrealProjectSyncViewModel.CanStartPublish),
        nameof(UnrealProjectSyncViewModel.PublishActionText),
    ];

    /// <summary>第二步「规整素材」加载完成。</summary>
    public static readonly string[] NormalizationStepLoaded =
    [
        nameof(UnrealProjectSyncViewModel.CanAdvanceWorkflow),
        nameof(UnrealProjectSyncViewModel.WorkflowNextButtonEnabled),
    ];

    /// <summary>差异树有没有跑过（第三、五步共用）。</summary>
    public static readonly string[] ImportDetection =
    [
        nameof(UnrealProjectSyncViewModel.HasContentDetection),
        nameof(UnrealProjectSyncViewModel.WorkflowStep5StatusText),
        nameof(UnrealProjectSyncViewModel.WorkflowStep6StatusText),
        nameof(UnrealProjectSyncViewModel.CanAdvanceWorkflow),
        nameof(UnrealProjectSyncViewModel.HasNoPublishChanges),
        nameof(UnrealProjectSyncViewModel.CanStartPublish),
        nameof(UnrealProjectSyncViewModel.WorkflowNextButtonEnabled),
    ];

    /// <summary>有没有 Unreal 任务正在跑（各处的可用性闸门）。</summary>
    public static readonly string[] WorkflowOperationRunning =
    [
        nameof(UnrealProjectSyncViewModel.IsWorkflowOperationIdle),
        nameof(UnrealProjectSyncViewModel.CanStartPublish),
        nameof(UnrealProjectSyncViewModel.CanDetectSelectedSource),
        nameof(UnrealProjectSyncViewModel.WorkflowNextButtonEnabled),
        nameof(UnrealProjectSyncViewModel.CanApplyLightConfiguration),
        nameof(UnrealProjectSyncViewModel.CanApplyBlueprintSetup),
    ];

    /// <summary>第四步「基础配置」加载完成。</summary>
    public static readonly string[] LightConfigurationLoaded =
    [
        nameof(UnrealProjectSyncViewModel.LightConfigurationSummaryText),
        nameof(UnrealProjectSyncViewModel.WorkflowStep4StatusText),
        nameof(UnrealProjectSyncViewModel.CanAdvanceWorkflow),
        nameof(UnrealProjectSyncViewModel.WorkflowNextButtonEnabled),
        nameof(UnrealProjectSyncViewModel.CanApplyLightConfiguration),
    ];

    /// <summary>第六步「蓝图置入」加载完成。</summary>
    public static readonly string[] BlueprintSetupLoaded =
    [
        nameof(UnrealProjectSyncViewModel.BlueprintSetupSummaryText),
        nameof(UnrealProjectSyncViewModel.CanApplyBlueprintSetup),
        nameof(UnrealProjectSyncViewModel.WorkflowStep6StatusText),
    ];

    /// <summary>上面所有清单的并集。守卫用例拿它比对反射出来的派生属性。</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(
        WorkflowStep
            .Concat(NormalizationStepLoaded)
            .Concat(ImportDetection)
            .Concat(WorkflowOperationRunning)
            .Concat(LightConfigurationLoaded)
            .Concat(BlueprintSetupLoaded),
        StringComparer.Ordinal);

    /// <summary>所有清单本身，守卫用例遍历用。</summary>
    public static readonly IReadOnlyList<string[]> Groups =
    [
        WorkflowStep,
        NormalizationStepLoaded,
        ImportDetection,
        WorkflowOperationRunning,
        LightConfigurationLoaded,
        BlueprintSetupLoaded,
    ];
}
