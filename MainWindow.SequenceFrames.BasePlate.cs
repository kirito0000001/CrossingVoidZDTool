using System;
using System.IO;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CrossingVoidZDTool
{
    public sealed partial class MainWindow
    {
        /// <summary>
        /// 「导出底板」：把当前动作按**动作帧率的 2 倍**逐帧导出成 PNG，
        /// 给"对着帧画特效"的人当参考底。
        ///
        /// 边界都在这里收口：没选角色 / 没选动作 / 动作没帧 / 动作代号不认识，都只提示不动盘。
        /// 真正的规则在 <see cref="BasePlateExportPlanner"/>（纯函数，回归可断言），
        /// 写盘在 <see cref="BasePlateExportService"/>。
        /// </summary>
        private async Task ExportSelectedSequenceBasePlatesAsync()
        {
            if (CharacterDesk.CurrentCharacter is not { } character)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择角色", "请先在角色台选择当前制作角色。");
                return;
            }

            // 和「导出图集」同一套判定：编辑哪个动作就导出哪个动作。
            var section = _applicationViewModel.SequenceFrames.SelectedSection;
            if (section is null)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择动作", "先打开一个动作的序列编辑器，再导出底板。");
                return;
            }

            if (section.Frames.Count == 0)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "没有素材帧", "这个动作还没有帧，先导入帧素材。");
                return;
            }

            if (!SequenceActionCatalog.TryResolve(section.Action.Code, out var definition, out var parsedForm))
            {
                ShowFloatingTip(InfoBarSeverity.Error, "无法导出底板", $"不认识的动作代号：{section.Action.Code}");
                return;
            }

            var formIndex = section.Action.FormIndex > 1 ? section.Action.FormIndex : parsedForm;
            var variantCode = SequenceActionCatalog.GetVariantCode(definition, formIndex);
            var plan = BasePlateExportPlanner.Build(
                Settings.ProjectRootPath,
                character.Code,
                variantCode,
                section.Frames,
                _applicationViewModel.SequenceFrames.PreviewFps);

            ShowGlobalProgress("导出底板", $"{plan.FileNamePrefix} · {plan.Frames.Count} 张");
            BasePlateExportResult result;
            try
            {
                var progress = new Progress<BasePlateExportProgress>(update =>
                    UpdateGlobalProgress(
                        update.Message,
                        plan.Frames.Count == 0 ? 0 : update.CompletedFrames * 100.0 / plan.Frames.Count,
                        $"{plan.FileNamePrefix} · {plan.OutputFps:0.##}fps"));
                result = await new BasePlateExportService().ExportAsync(
                    plan,
                    Settings.ProjectRootPath,
                    progress,
                    GetGlobalProgressCancellationToken());
            }
            catch (OperationCanceledException)
            {
                CompleteGlobalProgress("导出底板已取消", plan.FileNamePrefix);
                await HideGlobalProgressAfterDelayAsync();
                return;
            }
            catch (Exception ex)
            {
                CompleteGlobalProgress("导出底板失败", ex.Message);
                await HideGlobalProgressAfterDelayAsync();
                ShowFloatingTip(InfoBarSeverity.Error, "导出底板失败", ex.Message);
                AppendLog(LogKind.Error, "导出底板失败。", ex);
                return;
            }

            // 导出**已经成功**之后的事都和"导出失败"无关，所以放在 try 之外：
            // 打开目录失败也只是记一条，不能把成功的导出报成失败。
            CompleteGlobalProgress($"底板已导出：{result.FrameCount} 张", result.OutputDirectory);
            await HideGlobalProgressAfterDelayAsync(900);

            AppendLog(LogKind.User,
                $"导出底板：{plan.FileNamePrefix} → {result.FrameCount} 张 @{plan.OutputFps:0.##}fps，"
                + $"{plan.CanvasWidth}×{plan.CanvasHeight}，{result.OutputDirectory}"
                + (result.RemovedStaleFiles > 0 ? $"，清掉上次残留 {result.RemovedStaleFiles} 个文件" : string.Empty));
            ShowFloatingTip(
                InfoBarSeverity.Success,
                $"底板已导出 {result.FrameCount} 张",
                result.OutputDirectory);

            // 底板是马上要拿去画特效的，直接把目录弹出来，省一次翻目录。
            try
            {
                OpenFolderInExplorer(result.OutputDirectory);
            }
            catch (Exception ex)
            {
                AppendLog(LogKind.Warning, "底板已导出，但没能自动打开导出目录。", ex);
            }
        }

        /// <summary>
        /// 把「导出」菜单的内容装上：清单来自 <see cref="SequenceExportMenu"/>（纯函数），
        /// 壳只把 (文案, 命令) 变成 <c>MenuFlyoutItem</c>。
        ///
        /// 这样加新导出物**不用改 XAML**——这一排按钮已经放不下更多了。
        /// </summary>
        private void BuildSequenceExportMenu()
        {
            SequenceExportMenuFlyout.Items.Clear();
            foreach (var (item, command) in _applicationViewModel.SequenceFrames.ExportMenuActions)
            {
                var menuItem = new MenuFlyoutItem
                {
                    Text = item.Text,
                    Command = command
                };
                ToolTipService.SetToolTip(menuItem, item.ToolTip);
                SequenceExportMenuFlyout.Items.Add(menuItem);
            }
        }
    }
}
