using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;
using CrossingVoidZDTool.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Input;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CrossingVoidZDTool
{
    /// <summary>
    /// 六步流程的公共骨架：上一步、下一步、重新加载，以及唯一的进入口。
    /// 各步自己的检测实现在对应的分部文件里。
    /// </summary>
    public sealed partial class MainWindow
    {
        private void UnrealSyncPreviousStepButton_Click(object sender, RoutedEventArgs e)
        {
            LogUserOperation("同步流程：上一步");
            var sync = _applicationViewModel.UnrealProjectSync;
            // 回退只导航，绝不触发检测。往回走是「我要看看上一步」，
            // 不是「重新查一遍上一步」——那一步没缓存时中栏会显示未检测占位，
            // 要不要真查由用户点「重新加载」决定。
            sync.ReturnToWorkflowStep(Math.Max(UnrealSyncWorkflow.MinStep, sync.WorkflowStep - 1));
        }

        private async void UnrealSyncNextStepButton_Click(object sender, RoutedEventArgs e)
        {
            LogUserOperation("同步流程：下一步");
            var sync = _applicationViewModel.UnrealProjectSync;
            var step = sync.WorkflowStep;
            if (step >= UnrealSyncWorkflow.MaxStep)
            {
                ShowFloatingTip(InfoBarSeverity.Informational, "已经是最后一步", "蓝图置入完成后本次同步就结束了。");
                return;
            }

            if (!TryLeaveWorkflowStep(sync, step))
            {
                return;
            }

            // 第三步确认没有待同步内容后，要把「同步结果」面板收好再走，
            // 否则第四步会带着第三步的操作提示。这里不能再触发一次素材同步事件，
            // 那会重复执行同步前检测和同步操作。
            if (step == 3)
            {
                sync.CompletePublishOperation(0, 0);
            }

            await EnterWorkflowStepAsync(sync, step + 1);
        }

        /// <summary>
        /// 当前步骤是否满足离开条件；不满足时给出这一步自己的提示。
        /// 只做判断，不改任何状态。
        /// </summary>
        private bool TryLeaveWorkflowStep(UnrealProjectSyncViewModel sync, int step)
        {
            switch (step)
            {
                case 1 when !sync.CanAdvanceWorkflow:
                    ShowFloatingTip(InfoBarSeverity.Informational, "第一步尚未完成", "请先选择角色，并完成底层检测。");
                    return false;
                case 2 when !sync.IsNormalizationStepLoaded:
                    ShowFloatingTip(InfoBarSeverity.Informational, "规整素材尚未加载完成", "请等待当前加载完成，或点击“重新加载规整素材”。");
                    return false;
                case 2 when sync.NormalizationItems.Any(item => !item.IsResolved):
                    ShowFloatingTip(
                        InfoBarSeverity.Informational,
                        "第二步尚未完成",
                        $"还有 {sync.NormalizationItems.Count(item => !item.IsResolved)} 项 Unreal 素材没有选择处理方式。");
                    return false;
                case 3 when !sync.HasNoPublishChanges:
                    ShowFloatingTip(
                        InfoBarSeverity.Informational,
                        "第三步尚未完成",
                        "请先同步已勾选素材；确认没有待同步内容后，才能进入基础配置。");
                    return false;
                case 4 when !sync.CanAdvanceWorkflow:
                    ShowFloatingTip(InfoBarSeverity.Informational, "第四步尚未完成", "请先完成基础配置。");
                    return false;
                case 5 when !sync.CanAdvanceWorkflow:
                    ShowFloatingTip(
                        InfoBarSeverity.Informational,
                        "第五步尚未完成",
                        "请先同步已勾选的序列；确认没有待同步内容后，才能进入蓝图置入。");
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>
        /// 进入某一步：先落步，再按需检测。
        ///
        /// 两件事必须分开。先落步是按钮的本职——检测失败、被别的操作占用、
        /// 或者干脆不检测，都不该把人卡在上一步。按需是指这一步已经有数据
        /// （内存里的，或刚从该步缓存恢复的）时就不再跑虚幻：六步来回切，
        /// 每次都重检测纯粹是干等，离线一次就是十几秒。
        /// </summary>
        private async Task EnterWorkflowStepAsync(
            UnrealProjectSyncViewModel sync,
            int step,
            bool forceReload = false)
        {
            sync.ReturnToWorkflowStep(step);
            if (!forceReload && sync.IsWorkflowStepLoaded(step))
            {
                AppendLog(LogKind.Info, $"[Workflow] step={step} 复用本步缓存，未重新检测 Unreal。");
                return;
            }

            await RunWorkflowStepDetectionAsync(sync, step);
        }

        /// <summary>跑某一步自己的检测。每一步的范围不同，但入口只有这一个。</summary>
        private async Task RunWorkflowStepDetectionAsync(UnrealProjectSyncViewModel sync, int step)
        {
            var characterCode = sync.SelectedSource?.DraftCharacter?.Code;
            if (string.IsNullOrWhiteSpace(characterCode))
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择已完成角色", "请先在左侧选择一个已完成角色。");
                return;
            }

            switch (step)
            {
                case 1:
                    sync.RefreshFoundationChecks(characterCode);
                    ShowFloatingTip(InfoBarSeverity.Informational, "底层检测已重新加载", sync.FoundationSummaryText);
                    break;
                case 2:
                    await ReloadUnrealNormalizationStepAsync(sync);
                    break;
                case 3:
                case 5:
                    // 第三步和第五步走同一条差异检测，只是导出范围和默认勾选不同。
                    _workflowStepAfterPublishDetection = step;
                    await DetectUnrealPublishChangesAsync();
                    break;
                case 4:
                    await ReloadUnrealLightConfigurationStepAsync(sync);
                    break;
                case 6:
                    await ReloadUnrealBlueprintSetupStepAsync(sync);
                    break;
            }
        }

        /// <summary>
        /// 从当前步骤往后依次检测，停在第一个需要人处理的步骤。
        ///
        /// 只检测，不写入。第三、五步真正的同步和第四、六步的写入都会改动
        /// Unreal 工程，那是要人确认的事，不该被一个按钮顺手做掉；
        /// 这里的价值是把六步的等待一次排完，而不是替人做决定。
        /// </summary>
        private async void UnrealSyncDetectAllStepsButton_Click(object sender, RoutedEventArgs e)
        {
            var sync = _applicationViewModel.UnrealProjectSync;
            if (string.IsNullOrWhiteSpace(sync.SelectedSource?.DraftCharacter?.Code))
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择已完成角色", "请先在左侧选择一个已完成角色。");
                return;
            }

            LogUserOperation($"同步流程：从第 {sync.WorkflowStep} 步起依次检测");
            var startStep = sync.WorkflowStep;
            for (var step = startStep; step <= UnrealSyncWorkflow.MaxStep; step++)
            {
                await EnterWorkflowStepAsync(sync, step);
                if (sync.WorkspaceState == UnrealSyncWorkspaceState.Failed)
                {
                    ShowFloatingTip(
                        InfoBarSeverity.Error,
                        $"第 {step} 步检测失败",
                        sync.WorkspacePlaceholderDescription);
                    return;
                }

                if (step == UnrealSyncWorkflow.MaxStep)
                {
                    break;
                }

                // 这一步还有事要做就停下来，让人处理完再继续。
                // TryLeaveWorkflowStep 自己会说明卡在哪。
                if (!TryLeaveWorkflowStep(sync, step))
                {
                    return;
                }
            }

            ShowFloatingTip(
                InfoBarSeverity.Success,
                "六步检测已跑完",
                $"从第 {startStep} 步检测到第 {UnrealSyncWorkflow.MaxStep} 步，没有需要先处理的内容。");
        }

        private async void ReloadUnrealWorkflowStepButton_Click(object sender, RoutedEventArgs e)
        {
            var sync = _applicationViewModel.UnrealProjectSync;
            if (string.IsNullOrWhiteSpace(sync.SelectedSource?.DraftCharacter?.Code))
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择已完成角色", "请先在左侧选择一个已完成角色。");
                return;
            }

            // 「重新加载」是用户明确要求重查，无论本步有没有缓存都要真跑一次。
            LogUserOperation($"同步流程：重新加载第 {sync.WorkflowStep} 步");
            await EnterWorkflowStepAsync(sync, sync.WorkflowStep, forceReload: true);
        }
    }
}
