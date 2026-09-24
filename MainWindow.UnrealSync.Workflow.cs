using System;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;
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
    public sealed partial class MainWindow : IUnrealSyncWorkflowHost, INotificationService, IUserOperationLog, IClipboardService
    {
        // 四个导航按钮（上一步/下一步/依次检测/重新加载）现在是 VM 上的命令，
        // 控制器也随之交给 VM 持有——见 UnrealProjectSyncViewModel.WorkflowCommands.cs。
        // 这里只留 Host 实现：各步自己的检测留在对应的分部文件里。
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
                    // 第 1 步查的就是蓝图类型（角色蓝图 / AnimBP / Item / MetaSound…）：
                    // 类型从磁盘上那份导出清单读，不跑 Unreal。口径收在
                    // `RequiresAssetTypesFor` 里，壳侧不再传开关。
                    sync.RefreshFoundationChecks(characterCode);
                    // 第一步自己的缓存：一步一个文件，只装这十几条检查项（跟着角色目录走）。
                    // 整体那份会话缓存不再喂第一步 —— 它是所有步骤共用的一大坨，步骤互相串台的根就在那儿。
                    if (sync.SelectedSource?.DraftCharacter is { } foundationCharacter)
                    {
                        sync.SaveFoundationCache(foundationCharacter);
                    }
                    // 这条提示是"这一步跑完了"的信息（绿），过没过在下面那 16 条里逐条看 ——
                    // 以前用的蓝色 Informational 是全页唯一一个"成功类"提示，和别处不一致。
                    ShowFloatingTip(InfoBarSeverity.Success, "底层检测已重新加载", sync.FoundationSummaryText);
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
                case 7:
                    // 第七步「特效同步」：**只做工具箱侧的事** —— 打网格 sheet + 建特效计划。
                    // 它不需要 Unreal 的全量导出：特效该有几张、网格几×几都来自工作区的特效帧目录，
                    // 所以这一步**不会**像第五步那样把整条序列的帧全打开一遍（用户明确要求省掉那一步）。
                    await ReloadUnrealEffectSyncStepAsync(sync);
                    break;
            }
        }

        // B2：这一层不再直接碰界面方法，提示走 INotificationService、日志走 ToolboxLog，
        // 控制器那边只认 UnrealSyncNotice，翻译只发生在这一个地方。
        void IUnrealSyncWorkflowHost.Notify(UnrealSyncNotice notice) =>
            ((INotificationService)this).Notify(ToNotifySeverity(notice.Severity), notice.Title, notice.Message);

        void IUnrealSyncWorkflowHost.Log(string message) => ToolboxLog.Info(message);

        void INotificationService.Notify(NotifySeverity severity, string title, string message) =>
            ShowFloatingTip(ToInfoBarSeverity(severity), title, message);

        // B3：命令搬进 VM 之后，「用户点了什么」还得记在同一处（User 配色 + 最近操作队列）。
        void IUserOperationLog.LogUserOperation(string action) => LogUserOperation(action);

        // 剪贴板同理：命令在 VM 里，写剪贴板的动作由壳提供。
        bool IClipboardService.TryCopyText(string text)
        {
            CopyTextToClipboard(text);
            return true;
        }

        private static NotifySeverity ToNotifySeverity(UnrealSyncNoticeSeverity severity) => severity switch
        {
            UnrealSyncNoticeSeverity.Success => NotifySeverity.Success,
            UnrealSyncNoticeSeverity.Warning => NotifySeverity.Warning,
            UnrealSyncNoticeSeverity.Error => NotifySeverity.Error,
            _ => NotifySeverity.Info,
        };

        private static InfoBarSeverity ToInfoBarSeverity(NotifySeverity severity) => severity switch
        {
            NotifySeverity.Success => InfoBarSeverity.Success,
            NotifySeverity.Warning => InfoBarSeverity.Warning,
            NotifySeverity.Error => InfoBarSeverity.Error,
            _ => InfoBarSeverity.Informational,
        };
    }
}
