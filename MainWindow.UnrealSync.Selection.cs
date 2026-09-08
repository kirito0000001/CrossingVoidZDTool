using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CrossingVoidZDTool
{
    /// <summary>
    /// 右栏对当前步骤的通用操作：全选、全不选、反选、复制清单、在 Unreal 中定位。
    /// 按当前步骤分派，六步共用同一组按钮。
    /// </summary>
    public sealed partial class MainWindow
    {
        private void UnrealSyncSelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            var sync = _applicationViewModel.UnrealProjectSync;
            sync.SetStepSelection(true);
            LogUserOperation($"同步流程：全选（{sync.StepSelectionText}）");
        }

        private void UnrealSyncSelectNoneButton_Click(object sender, RoutedEventArgs e)
        {
            var sync = _applicationViewModel.UnrealProjectSync;
            sync.SetStepSelection(false);
            LogUserOperation($"同步流程：全不选（{sync.StepSelectionText}）");
        }

        private void UnrealSyncInvertSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            var sync = _applicationViewModel.UnrealProjectSync;
            sync.InvertStepSelection();
            LogUserOperation($"同步流程：反选（{sync.StepSelectionText}）");
        }

        private void UnrealSyncCopyStepReportButton_Click(object sender, RoutedEventArgs e)
        {
            var sync = _applicationViewModel.UnrealProjectSync;
            var report = sync.BuildStepChangeReport();
            if (string.IsNullOrWhiteSpace(report))
            {
                ShowFloatingTip(InfoBarSeverity.Informational, "没有可复制的内容", "这一步还没有检测结果。");
                return;
            }

            CopyTextToClipboard(report);
            LogUserOperation($"同步流程：复制第 {sync.WorkflowStep} 步清单");
            ShowFloatingTip(
                InfoBarSeverity.Success,
                "清单已复制",
                $"第 {sync.WorkflowStep} 步 {sync.WorkflowStepName} 的差异已复制到剪贴板。");
        }

        /// <summary>
        /// 在已打开的编辑器里选中这一步涉及的资产。
        ///
        /// 只走在线路径：离线启一个无头编辑器再让它「定位」到某个资产，
        /// 既慢又没人看得见，所以编辑器没开时直接说明，不白花那十几秒。
        /// </summary>
        private async void UnrealSyncBrowseAssetsButton_Click(object sender, RoutedEventArgs e)
        {
            var sync = _applicationViewModel.UnrealProjectSync;
            var service = new UnrealAssetBrowseService();
            if (!service.CanBrowse(sync.EnginePath, sync.ProjectPath))
            {
                ShowFloatingTip(
                    InfoBarSeverity.Informational,
                    "需要先打开 Unreal 编辑器",
                    "定位资产要在已打开的编辑器里进行；请先打开当前项目。");
                return;
            }

            var paths = sync.GetStepObjectPathsToBrowse();
            if (paths.Count == 0)
            {
                ShowFloatingTip(InfoBarSeverity.Informational, "没有可定位的资产", "这一步的条目没有对应的 Unreal 资产路径。");
                return;
            }

            LogUserOperation($"同步流程：在 Unreal 中定位 {paths.Count} 个资产");
            try
            {
                var workFolder = Path.Combine(
                    Path.GetDirectoryName(sync.ProjectPath)!,
                    "Intermediate",
                    "ZDToolboxBridge",
                    sync.SelectedSource?.DraftCharacter?.Code ?? "Shared",
                    "Browse");
                var result = await service.BrowseAsync(
                    sync.EnginePath, sync.ProjectPath, paths, workFolder);
                if (!result.Succeeded)
                {
                    ShowFloatingTip(InfoBarSeverity.Warning, "定位失败", result.Message);
                    AppendLog(LogKind.Warning, $"[Browse] {result.Message}");
                    return;
                }

                var missing = result.MissingPaths.Count;
                ShowFloatingTip(
                    missing > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success,
                    "已在 Unreal 中定位",
                    missing > 0
                        ? $"选中 {result.BrowsedCount} 个；{missing} 个在项目里不存在。"
                        : $"已在内容浏览器中选中 {result.BrowsedCount} 个资产。");
                if (missing > 0)
                {
                    AppendLog(LogKind.Warning,
                        "[Browse] 项目里不存在：" + string.Join("、", result.MissingPaths.Take(20)));
                }
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "定位失败", ex.Message);
                AppendLog(LogKind.Error, "在 Unreal 中定位资产失败。", ex);
            }
        }
    }
}
