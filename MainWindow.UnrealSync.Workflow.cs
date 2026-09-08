using System;
using System.Threading.Tasks;
using CrossingVoidZDTool.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CrossingVoidZDTool
{
    /// <summary>
    /// 六步流程的按钮接线。
    ///
    /// 编排规则本身搬到了 <see cref="UnrealSyncWorkflowController"/>，那边不依赖界面、
    /// 能真跑着测。这里只剩两件事：把点击转过去，以及作为 Host 提供各步自己的检测
    /// 和提示——各步的检测牵着服务、进度条和一堆界面状态，留在对应的分部文件里。
    /// </summary>
    public sealed partial class MainWindow : IUnrealSyncWorkflowHost
    {
        private UnrealSyncWorkflowController? _unrealSyncWorkflow;

        private UnrealSyncWorkflowController UnrealSyncWorkflowController =>
            _unrealSyncWorkflow ??= new(_applicationViewModel.UnrealProjectSync, this);

        private void UnrealSyncPreviousStepButton_Click(object sender, RoutedEventArgs e)
        {
            LogUserOperation("同步流程：上一步");
            UnrealSyncWorkflowController.GoToPreviousStep();
        }

        private async void UnrealSyncNextStepButton_Click(object sender, RoutedEventArgs e)
        {
            LogUserOperation("同步流程：下一步");
            await UnrealSyncWorkflowController.GoToNextStepAsync();
        }

        private async void UnrealSyncDetectAllStepsButton_Click(object sender, RoutedEventArgs e)
        {
            LogUserOperation($"同步流程：从第 {_applicationViewModel.UnrealProjectSync.WorkflowStep} 步起依次检测");
            await UnrealSyncWorkflowController.DetectAllStepsAsync();
        }

        private async void ReloadUnrealWorkflowStepButton_Click(object sender, RoutedEventArgs e)
        {
            LogUserOperation($"同步流程：重新加载第 {_applicationViewModel.UnrealProjectSync.WorkflowStep} 步");
            await UnrealSyncWorkflowController.ReloadCurrentStepAsync();
        }

        /// <summary>跑某一步自己的检测。每一步的范围不同，但入口只有这一个。</summary>
        async Task IUnrealSyncWorkflowHost.RunStepDetectionAsync(int step)
        {
            var sync = _applicationViewModel.UnrealProjectSync;
            var characterCode = sync.SelectedSource?.DraftCharacter?.Code;
            if (string.IsNullOrWhiteSpace(characterCode))
            {
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

        void IUnrealSyncWorkflowHost.Notify(UnrealSyncNotice notice) =>
            ShowFloatingTip(ToInfoBarSeverity(notice.Severity), notice.Title, notice.Message);

        void IUnrealSyncWorkflowHost.Log(string message) => AppendLog(LogKind.Info, message);

        private static InfoBarSeverity ToInfoBarSeverity(UnrealSyncNoticeSeverity severity) => severity switch
        {
            UnrealSyncNoticeSeverity.Success => InfoBarSeverity.Success,
            UnrealSyncNoticeSeverity.Warning => InfoBarSeverity.Warning,
            UnrealSyncNoticeSeverity.Error => InfoBarSeverity.Error,
            _ => InfoBarSeverity.Informational,
        };
    }
}
