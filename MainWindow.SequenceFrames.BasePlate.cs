using System;
using System.Collections.Generic;
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
        private Task ExportSelectedSequenceBasePlatesAsync() =>
            ExportSequenceBasePlatesAsync(withEffects: false);

        /// <summary>
        /// 「导出底板（带特效）」：落点、PNG、图层组形状和「导出底板（2 倍帧）」**完全一样**，
        /// 唯一的差别是每个图层组的「特效」层里放着**已经画好的特效帧**（没画过的帧仍是空层），
        /// 于是导入 PSD 之后能直接在原有特效上接着改，不用从零再画一遍。
        /// </summary>
        private Task ExportSelectedSequenceBasePlatesWithEffectsAsync() =>
            ExportSequenceBasePlatesAsync(withEffects: true);

        /// <summary>
        /// 两种底板导出共用的这一条流程（界面上的区别只有文案和"要不要填特效层"）。
        ///
        /// **覆盖确认**：两种导出写的是同一份 PSD，而那份 PSD 正是画特效的地方 ——
        /// 不吭声地重导一次，上一次画好的东西就没了。所以盘上已经有那份 PSD 时先弹一次确认，
        /// 说清"先读回再导出"这条路（晓桀 2026-09-27）。
        /// </summary>
        private async Task ExportSequenceBasePlatesAsync(bool withEffects)
        {
            var actionName = withEffects ? "导出底板（带特效）" : "导出底板";
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

            var plan = BuildBasePlatePlan(character, section, definition, parsedForm);

            // 「带特效」先问清带**哪一层**（默认上次用的那层）：这一步取消就整个停下 ——
            // 免得更烦人的是先把覆盖问一遍、结果人又不导了。
            var effectLayerIndex = _sequenceEffectLayerIndex;
            if (withEffects)
            {
                var picked = await PickEffectLayerIndexAsync(
                    character,
                    section,
                    "这次导出的「特效」层里放哪一层的特效帧？",
                    "带这一层");
                if (picked is not { } targetLayer)
                {
                    AppendLog(LogKind.User, $"{actionName}：没选特效层，按「取消」停下，盘上什么都没动。");
                    return;
                }

                SetEffectLayerIndex(character, section.Action, targetLayer);
                effectLayerIndex = _sequenceEffectLayerIndex;
            }

            // 那份 PSD 是唯一的落点，两种导出都写它 —— 已经在了就先问一声。
            var psdPath = Path.Combine(plan.OutputDirectory, BasePlateExportPlanner.FormatPsdFileName(plan));
            if (File.Exists(psdPath) && !await ConfirmBasePlatePsdOverwriteAsync(psdPath, withEffects))
            {
                AppendLog(LogKind.User,
                    $"{actionName}：{plan.OutputDirectory} 里已经有 {Path.GetFileName(psdPath)}，"
                    + "按「取消」停下，盘上什么都没动。");
                ShowFloatingTip(InfoBarSeverity.Informational, "已取消导出", "盘上那份 PSD 原样留着，没有动。");
                return;
            }

            // 「带特效」才去查特效层：格子号和图层组名同号（都是文件名末尾那段数字），直接对得上。
            IReadOnlyDictionary<int, string>? effectFramePaths = null;
            if (withEffects)
            {
                var layer = new SequenceEffectService().Load(
                    character,
                    section.Action,
                    plan.Multiplier,
                    layerIndex: effectLayerIndex,
                    expectedFrameCount: plan.Frames.Count);
                effectFramePaths = SequenceEffectService.BuildFramePathsByOrdinal(layer);
            }

            ShowGlobalProgress(actionName, $"{plan.FileNamePrefix} · {plan.Frames.Count} 张");
            BasePlateExportResult result;
            try
            {
                // 进度：导出侧报的是 0~100 的百分比（它自己那段混着两套单位：
                // 先是每张 PNG 一个单位，再是 PsdWriter 的组数 + 图层记录条数）。
                // 编组整条记录是里面最慢的一段，进度条后半段就是它在走。
                var progress = new Progress<BasePlateExportProgress>(update =>
                    UpdateGlobalProgress(
                        update.Message,
                        update.Percent,
                        $"{plan.FileNamePrefix} · {plan.OutputFps:0.##}fps"));
                result = await new BasePlateExportService().ExportAsync(
                    plan,
                    Settings.ProjectRootPath,
                    progress,
                    GetGlobalProgressCancellationToken(),
                    effectFramePaths);
            }
            catch (OperationCanceledException)
            {
                CompleteGlobalProgress($"{actionName}已取消", plan.FileNamePrefix);
                await HideGlobalProgressAfterDelayAsync();
                return;
            }
            catch (Exception ex)
            {
                CompleteGlobalProgress($"{actionName}失败", ex.Message);
                await HideGlobalProgressAfterDelayAsync();
                ShowFloatingTip(InfoBarSeverity.Error, $"{actionName}失败", ex.Message);
                AppendLog(LogKind.Error, $"{actionName}失败。", ex);
                return;
            }

            // 导出**已经成功**之后的事都和"导出失败"无关，所以放在 try 之外：
            // 打开目录失败也只是记一条，不能把成功的导出报成失败。
            var effectNote = withEffects
                ? result.EffectLayerCount > 0
                    ? $"，{result.EffectLayerCount} 组的「特效」层里是第 {effectLayerIndex} 层已经画好的特效帧"
                    : $"，第 {effectLayerIndex} 层还没有导入过特效帧，所以「特效」层都是空的"
                : string.Empty;
            CompleteGlobalProgress(
                $"底板已导出：{result.FrameCount} 张 + 1 份 PSD",
                $"{result.OutputDirectory}（{Path.GetFileName(result.PsdFilePath)} 可直接导入画世界 / PS）");
            await HideGlobalProgressAfterDelayAsync(900);

            AppendLog(LogKind.User,
                $"{actionName}：{plan.FileNamePrefix} → {result.FrameCount} 张 @{plan.OutputFps:0.##}fps，"
                + $"{plan.CanvasWidth}×{plan.CanvasHeight}，{result.OutputDirectory}"
                + $"，另附 {Path.GetFileName(result.PsdFilePath)}（{result.FrameCount} 个图层组，"
                + $"{result.PsdBytes / 1024.0 / 1024.0:0.##} MB，可直接导入画世界 / PS{effectNote}）"
                + (result.RemovedStaleFiles > 0 ? $"，清掉上次残留 {result.RemovedStaleFiles} 个文件" : string.Empty));
            ShowFloatingTip(
                InfoBarSeverity.Success,
                $"底板已导出 {result.FrameCount} 张 + 多图层 PSD",
                $"{result.OutputDirectory}{Environment.NewLine}"
                + $"画世界 / PS 直接导入 {Path.GetFileName(result.PsdFilePath)} 即可"
                + $"（一帧一个图层组，组里是「原本帧」+「特效」层；第 1 帧在最底层{effectNote}）。");

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
        /// 「这份 PSD 已经在了，要不要覆盖」—— 两种底板导出共用（写的是同一份文件）。
        ///
        /// 文案要说清两件事：**会换掉什么**（盘上那份 PSD，连同里面画过、改过的图层），
        /// 以及**怎么不丢东西**（先「导入特效帧 → 从 PSD 导入…」，再导出）。
        /// 只写"是否覆盖"是不够的 —— 出这个提示的人，往往已经忘了那份 PSD 是自己画的。
        /// </summary>
        private async Task<bool> ConfirmBasePlatePsdOverwriteAsync(string psdPath, bool withEffects)
        {
            var overwriteNote = withEffects
                ? "\n\n这次导出的图层组，「特效」层里会放进已经导入工具箱的特效帧（没画过的帧仍是空层）。"
                : string.Empty;
            var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "覆盖已有底板工程",
                new TextBlock
                {
                    Text = $"{Path.GetDirectoryName(psdPath)} 里已经有 {Path.GetFileName(psdPath)} 了。"
                        + "\n\n继续会把那份 PSD 整个换掉，里面画过、改过的图层都跟着没了。"
                        + "要留住已经画好的特效，先用「导入特效帧 → 从 PSD 导入…」收进工具箱，再导出。"
                        + overwriteNote,
                    TextWrapping = TextWrapping.Wrap,
                    Width = 500
                },
                PrimaryButtonText: "覆盖导出",
                CloseButtonText: string.Empty,
                SecondaryButtonText: "取消",
                DefaultButton: ContentDialogButton.Primary,
                PrimaryButtonStyle: (Style)Application.Current.Resources["DialogAccentButtonStyle"]));
            return result == DialogResultKind.Primary;
        }

        /// <summary>
        /// 当前动作的底板计划 —— **「导出底板」和「从 PSD 导入」共用这一处**。
        ///
        /// 它决定底板落哪个目录、画布多大；导入时前者当**文件选择框的默认位置**，
        /// 后者用来核对挑中的那份 PSD 是不是这个动作的。
        /// 两处各算一次的话，哪天动作代号的解析规则一动，就会出现"导出没问题、导入对不上"，
        /// 所以这里只留一条路径。前置条件（有没有帧、代号认不认得）由调用方先查——
        /// 两个入口要提示的文案不一样。
        /// </summary>
        private BasePlateExportPlan BuildBasePlatePlan(
            CharacterCard character,
            SequenceFrameSection section,
            SequenceActionDefinition definition,
            int parsedForm)
        {
            var formIndex = section.Action.FormIndex > 1 ? section.Action.FormIndex : parsedForm;
            return BasePlateExportPlanner.Build(
                Settings.ProjectRootPath,
                character.Code,
                SequenceActionCatalog.GetVariantCode(definition, formIndex),
                section.Frames,
                _applicationViewModel.SequenceFrames.PreviewFps);
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
