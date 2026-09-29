using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;
using CrossingVoidZDTool.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;

namespace CrossingVoidZDTool
{
    public sealed partial class MainWindow
    {
        private readonly SequencePreviewBitmapCache _sequenceEffectPreviewCache = new();
        private DispatcherQueueTimer? _sequenceEffectSubFrameTimer;
        private int _sequenceEffectSubFrame;
        private int _sequenceEffectShownFrameOrdinal = -1;
        /// <summary>
        /// 时间轴上被点住的那一格（帧序号 + 第几张特效帧）。开着「预览叠特效」时时间轴
        /// 按特效帧展开，点哪一格就把那一张**固定**在预览里；播放时清空，让定时器接管。
        /// </summary>
        private (int FrameIndex, int SubIndex)? _sequenceEffectPinnedSlot;

        /// <summary>
        /// 现在在弄第几层特效（1 起）。导入、带特效导出、打开目录、清空都跟着它走；
        /// 在「导入到第几层」的弹窗里选过哪一层，就记住哪一层（一次会话内有效，换动作也跟着走）。
        /// </summary>
        private int _sequenceEffectLayerIndex = SequenceEffectService.DefaultLayerIndex;

        // ── 导入 / 打开目录 / 清空 ────────────────────────────────────────────

        async Task ISequenceFramesCommandHost.ImportEffectFramesFromPsdAsync()
        {
            if (!TryResolveEffectImportTarget(out var character, out var section, out var expectedFrameCount))
            {
                return;
            }

            // 先问「导进第几层」（晓桀 2026-09-27：分层特效用），再挑 PSD 读。
            var layerIndex = await PickEffectLayerIndexAsync(
                character,
                section,
                "读回来的特效帧放进哪一层？",
                "导入到这一层");
            if (layerIndex is not { } targetLayer)
            {
                return;
            }

            SetEffectLayerIndex(character, section.Action, targetLayer);
            await ImportEffectFramesFromPickedPsdAsync(character, section, expectedFrameCount);
        }

        Task ISequenceFramesCommandHost.ImportEffectFramesFromFolderAsync() =>
            ImportEffectFramesFromPickedFolderAsync();

        /// <summary>
        /// 「导入特效帧 → 从 PSD 导入…」（菜单里的第一条，也就是默认那条）。
        ///
        /// 弹一个文件选择框，默认开在**当前动作的底板目录**（也就是「导出底板」的落点：
        /// 画完存回原处的人点两下就读回来了），也可以挑别处的 PSD —— 晓桀 2026-09-27：
        /// 「这样子就可以同事存在不同的PSD了」，同一份底板可以有好几版 PSD 并存。
        ///
        /// 读的是**图层组**（一帧一组、帧号认组名），见 <see cref="SequenceEffectPsdImportService"/>。
        /// 组数不必和帧数相等：少的算空帧、多的忽略（组里画几层都行，随便返工）。
        /// 会停下报错的是"这个文件里根本没有图层组"（拿旧版一帧一层的底板画的），
        /// 以及"画布尺寸和这个动作的底板对不上"（多半是挑到别的动作那份 PSD 了）。
        ///
        /// 导进**哪一层**由调用方（命令入口里的那个弹窗）定好，这里只管挑文件、读和落盘。
        /// </summary>
        private async Task ImportEffectFramesFromPickedPsdAsync(
            CharacterCard character,
            SequenceFrameSection section,
            int expectedFrameCount)
        {
            // 底板落点要靠动作代号解析出来（"OnDamage" 与 "Ondm" 指向同一份底板）：
            // 画布尺寸得和它对齐（对不上说明挑错 PSD 了），文件选择框也默认开在那儿。
            if (!SequenceActionCatalog.TryResolve(section.Action.Code, out var definition, out var parsedForm))
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "读不回特效帧",
                    $"不认识的动作代号：{section.Action.Code}——认不出底板落在哪个目录。");
                return;
            }

            var plan = BuildBasePlatePlan(character, section, definition, parsedForm);
            var psdPath = await _filePickerService.PickSingleFileAsync(
                BasePlateExportPlanner.ResolvePsdPickerStartFolder(plan),
                $"选择要导入的 PSD（{section.Action.Code}）",
                BasePlateExportPlanner.PsdExtension);
            if (string.IsNullOrWhiteSpace(psdPath))
            {
                // 取消 / 直接关掉选择框：什么都不做，也不报错（和「选择文件夹…」那条一致）。
                return;
            }

            // 读回来的特效帧**就放在底板目录里**（`<动作>-2x/Effect/`）：和对照底板 PNG、
            // 那份 PSD 挨在一起，一眼就能看到自己画了什么（晓桀 2026-09-25）。
            // 这个子目录**不受"重新导出底板时清空目录"的影响**（见 BasePlateExportService）。
            var effectFolder = BasePlateExportPlanner.ResolveEffectFolderPath(plan.OutputDirectory);
            var layerIndex = _sequenceEffectLayerIndex;

            ShowGlobalProgress("导入特效帧", $"{section.Action.Code} · 第 {layerIndex} 层");
            // 进度条的回调得回到 UI 线程上跑，所以 `Progress<T>` 在 UI 线程建（就在这儿）。
            var progress = new Progress<SequenceEffectImportProgress>(update =>
                UpdateGlobalProgress(
                    update.Message,
                    update.Percent,
                    $"{section.Action.Code} · {Path.GetFileName(psdPath)}"));
            var cancellationToken = GetGlobalProgressCancellationToken();
            try
            {
                // 解 PSD + 清层 + 落盘全是磁盘活（每次都解一遍 PackBits），放到后台线程去做，
                // 界面这边只等进度和结果 —— 和「导出底板」接进度条是同一个理由。
                var result = await Task.Run(
                    () =>
                    {
                        var staged = new SequenceEffectPsdImportService().StageFrames(
                            psdPath,
                            expectedFrameCount,
                            plan.CanvasWidth,
                            plan.CanvasHeight,
                            effectFolder,
                            progress,
                            cancellationToken);
                        return new SequenceEffectService().ImportStaged(
                            character,
                            section.Action,
                            staged,
                            expectedFrameCount,
                            layerIndex: layerIndex,
                            progress: progress,
                            cancellationToken: cancellationToken);
                    },
                    cancellationToken);
                ReloadEffectLayer(character, section);
                ReportEffectImport(section, result, expectedFrameCount, Path.GetFileName(psdPath));
                CompleteGlobalProgress(
                    $"特效帧已导入：{result.ImportedFrames} 张",
                    $"第 {layerIndex} 层 · {Path.GetFileName(psdPath)}");
                await HideGlobalProgressAfterDelayAsync(900);
                AppendLog(
                    LogKind.Info,
                    $"[Effect PSD] 读回的特效帧已写到：{effectFolder}（第 {layerIndex} 层）");
            }
            catch (OperationCanceledException)
            {
                // 大 PSD 解到一半按下"取消"：停下来，如实说一句（不报错）。
                CompleteGlobalProgress("导入特效帧已取消", $"{section.Action.Code} · 第 {layerIndex} 层");
                await HideGlobalProgressAfterDelayAsync();
                AppendLog(LogKind.User, $"特效帧导入已取消（第 {layerIndex} 层）。");
            }
            catch (InvalidOperationException ex)
            {
                // 层数 / 画布对不上属于"画的时候要对齐一下"，不是工具坏了。
                CompleteGlobalProgress("读不回特效帧", ex.Message);
                await HideGlobalProgressAfterDelayAsync();
                ShowFloatingTip(InfoBarSeverity.Warning, "读不回特效帧", ex.Message);
                AppendLog(LogKind.Warning, $"从 PSD 导入特效帧：{ex.Message}");
            }
            catch (Exception ex)
            {
                CompleteGlobalProgress("读回特效失败", ex.Message);
                await HideGlobalProgressAfterDelayAsync();
                ShowFloatingTip(InfoBarSeverity.Error, "读回特效失败", ex.Message);
                AppendLog(LogKind.Error, $"从 PSD 导入特效帧失败：{psdPath}", ex);
            }
            // 注意：这里**不再删**那个目录 —— 它现在就是给用户看结果的落点（以前是 `.from-psd` 暂存）。
        }

        /// <summary>
        /// 「导入特效帧 → 选择文件夹…」：挑一个装满 PNG 的目录，帧号取文件名末尾那段数字。
        ///
        /// 导出底板那批 PNG 和这套编号同名（<c>&lt;角色&gt;_&lt;动作&gt;_0001.png</c>），
        /// 所以"导出 → 画 → 选目录"不用改名。之所以还留着这条路：改图、从别处凑素材、
        /// 或者 PSD 那条走不通时，它不依赖任何别的东西。
        /// </summary>
        private async Task ImportEffectFramesFromPickedFolderAsync()
        {
            if (!TryResolveEffectImportTarget(out var character, out var section, out var expectedFrameCount))
            {
                return;
            }

            var layerIndex = await PickEffectLayerIndexAsync(
                character,
                section,
                "选来的这批帧放进哪一层？",
                "导入到这一层");
            if (layerIndex is not { } targetLayer)
            {
                return;
            }

            var folderPath = await _filePickerService.PickFolderAsync(PickerLocationId.ComputerFolder);
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                return;
            }

            var sourceFiles = Directory
                .EnumerateFiles(folderPath, "*.png", SearchOption.TopDirectoryOnly)
                .ToArray();
            if (sourceFiles.Length == 0)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Warning,
                    "这个文件夹里没有 PNG",
                    $"特效帧要从导出的底板改出来，期望 {expectedFrameCount} 张：{folderPath}");
                return;
            }

            SetEffectLayerIndex(character, section.Action, targetLayer);
            ShowGlobalProgress("导入特效帧", $"{section.Action.Code} · 第 {targetLayer} 层");
            // 进度条的回调得回到 UI 线程上跑，所以 `Progress<T>` 在 UI 线程建（就在这儿）。
            var progress = new Progress<SequenceEffectImportProgress>(update =>
                UpdateGlobalProgress(
                    update.Message,
                    update.Percent,
                    $"{section.Action.Code} · {Path.GetFileName(folderPath)}"));
            var cancellationToken = GetGlobalProgressCancellationToken();
            try
            {
                var result = await Task.Run(
                    () => new SequenceEffectService().Import(
                        character,
                        section.Action,
                        sourceFiles,
                        expectedFrameCount,
                        layerIndex: targetLayer,
                        progress: progress,
                        cancellationToken: cancellationToken),
                    cancellationToken);
                ReloadEffectLayer(character, section);
                ReportEffectImport(section, result, expectedFrameCount, Path.GetFileName(folderPath));
                CompleteGlobalProgress(
                    $"特效帧已导入：{result.ImportedFrames} 张",
                    $"第 {targetLayer} 层 · {Path.GetFileName(folderPath)}");
                await HideGlobalProgressAfterDelayAsync(900);
            }
            catch (OperationCanceledException)
            {
                // 收一大堆帧收到一半按下"取消"：停下来，如实说一句（不报错）。
                CompleteGlobalProgress("导入特效帧已取消", $"{section.Action.Code} · 第 {targetLayer} 层");
                await HideGlobalProgressAfterDelayAsync();
                AppendLog(LogKind.User, $"特效帧导入已取消（第 {targetLayer} 层）。");
            }
            catch (Exception ex)
            {
                CompleteGlobalProgress("导入特效失败", ex.Message);
                await HideGlobalProgressAfterDelayAsync();
                ShowFloatingTip(InfoBarSeverity.Error, "导入特效失败", ex.Message);
                AppendLog(LogKind.Error, "导入特效帧失败。", ex);
            }
        }

        /// <summary>
        /// 记住「现在弄第几层」并把这层的目录建出来。导入 / 带特效导出 / 切层都先经过这里，
        /// 之后的 <see cref="ReloadEffectLayer"/> 和「打开特效目录」就都跟着这一层走。
        /// </summary>
        private void SetEffectLayerIndex(CharacterCard character, SequenceFrameAction action, int layerIndex)
        {
            _sequenceEffectLayerIndex = Math.Max(SequenceEffectService.DefaultLayerIndex, layerIndex);
            SequenceEffectService.EnsureLayerFolder(character, action, _sequenceEffectLayerIndex);
        }

        /// <summary>
        /// 「导进第几层」的弹窗：列出这个动作盘上**现有的层**（空的层目录也算 ——
        /// 人常是先建好目录再慢慢画），末尾再挂一个「新建第 N 层」；默认选中上次用的那一层。
        ///
        /// 取消（或按 Esc）返回 null，调用方原样停下、一张图都不动。
        /// </summary>
        private async Task<int?> PickEffectLayerIndexAsync(
            CharacterCard character,
            SequenceFrameSection section,
            string message,
            string primaryText)
        {
            var existing = SequenceEffectService.FindLayerIndexes(character, section.Action);
            var lastLayer = Math.Max(
                existing.Count > 0 ? existing[^1] : SequenceEffectService.DefaultLayerIndex,
                _sequenceEffectLayerIndex);
            var service = new SequenceEffectService();
            var expectedFrameCount = ResolveEffectFrameCount(character, section);

            var combo = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinWidth = 360
            };
            var selectedItemIndex = 0;
            for (var index = SequenceEffectService.DefaultLayerIndex; index <= lastLayer; index++)
            {
                var layer = service.Load(
                    character,
                    section.Action,
                    layerIndex: index,
                    expectedFrameCount: expectedFrameCount);
                combo.Items.Add(new ComboBoxItem
                {
                    Content = $"第 {index} 层（{SequenceEffectService.FormatLayerFolderName(index)}）· {layer.SummaryText}",
                    Tag = index
                });
                if (index == _sequenceEffectLayerIndex)
                {
                    selectedItemIndex = combo.Items.Count - 1;
                }
            }

            // 已经顶到上限就不再挂「新建」——层号是目录名，不能无限往上加。
            var newLayer = lastLayer + 1;
            if (newLayer <= SequenceEffectService.MaxLayerIndex)
            {
                combo.Items.Add(new ComboBoxItem
                {
                    Content = $"新建第 {newLayer} 层（{SequenceEffectService.FormatLayerFolderName(newLayer)}）",
                    Tag = newLayer
                });
            }

            combo.SelectedIndex = selectedItemIndex;

            var content = new StackPanel { Spacing = 8 };
            content.Children.Add(new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 420
            });
            content.Children.Add(combo);

            var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "导入到第几层",
                content,
                PrimaryButtonText: primaryText,
                CloseButtonText: string.Empty,
                SecondaryButtonText: "取消",
                DefaultButton: ContentDialogButton.Primary,
                PrimaryButtonStyle: (Style)Application.Current.Resources["DialogAccentButtonStyle"]));
            if (result != DialogResultKind.Primary || combo.SelectedItem is not ComboBoxItem chosen)
            {
                return null;
            }

            return chosen.Tag is int pickedLayer ? pickedLayer : null;
        }

        /// <summary>
        /// 两个导入入口共用的前置检查：有没有选角色、有没有打开动作、这个动作该有几张特效。
        /// 任何一条不满足都只提示、不动盘（返回 false）。
        /// </summary>
        private bool TryResolveEffectImportTarget(
            out CharacterCard character,
            out SequenceFrameSection section,
            out int expectedFrameCount)
        {
            character = null!;
            section = null!;
            expectedFrameCount = 0;

            if (CharacterDesk.CurrentCharacter is not { } currentCharacter)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择角色", "请先在角色台选择当前制作角色。");
                return false;
            }

            character = currentCharacter;
            if (_applicationViewModel.SequenceFrames.SelectedSection is not { } currentSection)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择动作", "先打开一个动作的序列编辑器，再导入特效。");
                return false;
            }

            section = currentSection;
            expectedFrameCount = ResolveEffectFrameCount(character, section);
            if (expectedFrameCount <= 0)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "没有素材帧", "这个动作还没有帧，先导入帧素材。");
                return false;
            }

            return true;
        }

        /// <summary>导入成功后那一套提示：写日志 + 报"进来几张、空几张"。</summary>
        private void ReportEffectImport(
            SequenceFrameSection section,
            SequenceEffectImportResult result,
            int expectedFrameCount,
            string sourceName)
        {
            AppendLog(LogKind.User,
                $"导入特效帧：{section.Action.DisplayName} ← {sourceName} → "
                + $"{result.ImportedFrames}/{expectedFrameCount} 张"
                + $"，空帧 {result.EmptyFrames}"
                + (result.IgnoredFrames > 0 ? $"，忽略越界 {result.IgnoredFrames} 张" : string.Empty)
                + (result.ClearedFrames > 0
                    ? $"，换掉旧帧 {result.ClearedFrames} 张（这一层别的帧原样留着）"
                    : string.Empty));
            ShowFloatingTip(
                InfoBarSeverity.Success,
                $"特效已导入 {result.ImportedFrames} 张",
                result.EmptyFrames > 0
                    ? $"另有 {result.EmptyFrames} 帧没有内容（空帧），同步到虚幻时是空关键帧。"
                    : "全部帧都有内容。");
        }

        void ISequenceFramesCommandHost.OpenEffectFolder()
        {
            if (CharacterDesk.CurrentCharacter is not { } character ||
                _applicationViewModel.SequenceFrames.SelectedSection is not { } section)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择动作", "先打开一个动作的序列编辑器。");
                return;
            }

            // 当前这一层的目录可能还没建（这层还是空的），先建出来再打开 ——
            // 不然资源管理器会弹一个"找不到"的框。
            OpenFolderInExplorer(SequenceEffectService.EnsureLayerFolder(
                character,
                section.Action,
                _sequenceEffectLayerIndex));
        }

        void ISequenceFramesCommandHost.ClearEffectLayer()
        {
            if (CharacterDesk.CurrentCharacter is not { } character ||
                _applicationViewModel.SequenceFrames.SelectedSection is not { } section)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择动作", "先打开一个动作的序列编辑器。");
                return;
            }

            try
            {
                var removed = new SequenceEffectService().ClearLayer(
                    character,
                    section.Action,
                    _sequenceEffectLayerIndex);
                ReloadEffectLayer(character, section);
                AppendLog(
                    LogKind.User,
                    $"清空特效层：{section.Action.DisplayName} 第 {_sequenceEffectLayerIndex} 层"
                    + $"（{SequenceEffectService.FormatLayerFolderName(_sequenceEffectLayerIndex)}，删了 {removed} 张）");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "清空特效失败", ex.Message);
                AppendLog(LogKind.Error, "清空特效层失败。", ex);
            }
        }

        /// <summary>
        /// 把「导入特效帧」按钮的菜单装上：清单来自 <see cref="SequenceEffectImportMenu"/>（纯函数），
        /// 命令在 ViewModel 里配一次。
        ///
        /// **菜单本体也是在这儿建的**：`MainWindow.xaml` 的行数已经顶在棘轮上限上，
        /// 而 XAML 那边只需要给按钮挂一个 `x:Name`（零行开销）。代价是看 XAML 看不出
        /// 这个按钮有菜单 —— 所以这条注释得留着。
        /// </summary>
        private void BuildSequenceEffectImportMenu()
        {
            var flyout = new MenuFlyout();
            foreach (var (item, command) in _applicationViewModel.SequenceFrames.EffectImportMenuActions)
            {
                var menuItem = new MenuFlyoutItem
                {
                    Text = item.Text,
                    Command = command
                };
                ToolTipService.SetToolTip(menuItem, item.ToolTip);
                flyout.Items.Add(menuItem);
            }

            SequenceEffectImportButton.Flyout = flyout;
        }

        /// <summary>
        /// 把「特效层」摘要那一行做成**换层的入口**：点一下弹层列表（当前层打勾），
        /// 末尾挂一个「新建第 N 层」。
        ///
        /// 菜单本体在代码里建（`MainWindow.xaml` 顶在行数棘轮上，那边只加了 `x:Name`）；
        /// 层列表是**按当前动作现算的**，所以每次展开前重建一次 —— 换了动作、
        /// 或者人在资源管理器里新建了目录，这里都能立刻看到。
        /// </summary>
        private void BuildSequenceEffectLayerMenu()
        {
            var flyout = new MenuFlyout();
            flyout.Opening += (_, _) => FillSequenceEffectLayerMenu(flyout);
            FlyoutBase.SetAttachedFlyout(SequenceEffectLayerSummaryText, flyout);
            SequenceEffectLayerSummaryText.Tapped += (_, _) =>
                FlyoutBase.ShowAttachedFlyout(SequenceEffectLayerSummaryText);
            ToolTipService.SetToolTip(
                SequenceEffectLayerSummaryText,
                "点一下换层：同一个动作可以有好几层特效，第 1 层在 Effects\\，第 2 层起在 Effects2\\…");
        }

        private void FillSequenceEffectLayerMenu(MenuFlyout flyout)
        {
            flyout.Items.Clear();
            if (CharacterDesk.CurrentCharacter is not { } character ||
                _applicationViewModel.SequenceFrames.SelectedSection is not { } section)
            {
                flyout.Items.Add(new MenuFlyoutItem { Text = "先打开一个动作", IsEnabled = false });
                return;
            }

            var existing = SequenceEffectService.FindLayerIndexes(character, section.Action);
            var lastLayer = Math.Max(
                existing.Count > 0 ? existing[^1] : SequenceEffectService.DefaultLayerIndex,
                _sequenceEffectLayerIndex);
            var service = new SequenceEffectService();
            var expectedFrameCount = ResolveEffectFrameCount(character, section);
            for (var index = SequenceEffectService.DefaultLayerIndex; index <= lastLayer; index++)
            {
                var layerIndex = index;
                var layer = service.Load(
                    character,
                    section.Action,
                    layerIndex: layerIndex,
                    expectedFrameCount: expectedFrameCount);
                var item = new RadioMenuFlyoutItem
                {
                    Text = $"第 {layerIndex} 层（{SequenceEffectService.FormatLayerFolderName(layerIndex)}）· {layer.SummaryText}",
                    GroupName = "SequenceEffectLayer",
                    IsChecked = layerIndex == _sequenceEffectLayerIndex
                };
                item.Click += (_, _) => SwitchSequenceEffectLayer(character, section, layerIndex);
                flyout.Items.Add(item);
            }

            // 已经顶到上限就不再挂「新建」——层号就是目录名，不能无限往上加。
            var newLayer = lastLayer + 1;
            if (newLayer <= SequenceEffectService.MaxLayerIndex)
            {
                flyout.Items.Add(new MenuFlyoutSeparator());
                var createItem = new MenuFlyoutItem
                {
                    Text = $"新建第 {newLayer} 层（{SequenceEffectService.FormatLayerFolderName(newLayer)}）"
                };
                createItem.Click += (_, _) => SwitchSequenceEffectLayer(character, section, newLayer);
                flyout.Items.Add(createItem);
            }
        }

        /// <summary>切到第 N 层：记住层号（顺带把目录建出来）、把这一层读进 VM 和预览、记一条日志。</summary>
        private void SwitchSequenceEffectLayer(CharacterCard character, SequenceFrameSection section, int layerIndex)
        {
            SetEffectLayerIndex(character, section.Action, layerIndex);
            ReloadEffectLayer(character, section);
            AppendLog(
                LogKind.User,
                $"特效层：切到第 {_sequenceEffectLayerIndex} 层"
                + $"（{SequenceEffectService.FormatLayerFolderName(_sequenceEffectLayerIndex)}）");
        }

        /// <summary>
        /// 把**当前这一层**的现状读进 ViewModel，并立刻刷新预览上的特效层。
        ///
        /// 顺手把盘上**所有有内容的层**读出来（<see cref="_sequenceEffectStackLayers"/>）——
        /// 预览是**多层叠加**的：第 1 层在最下、层号大的盖上去（晓桀 2026-09-27：
        /// 「我之前想要的是多层叠加播放」）。以前只显示选中的那一层，导进第 2 层之后
        /// 看上去就像"第 1 层被清空了"。
        /// </summary>
        private void ReloadEffectLayer(CharacterCard character, SequenceFrameSection section)
        {
            var expectedFrameCount = ResolveEffectFrameCount(character, section);
            var service = new SequenceEffectService();
            var layer = service.Load(
                character,
                section.Action,
                layerIndex: _sequenceEffectLayerIndex,
                expectedFrameCount: expectedFrameCount);

            var stackLayers = new List<SequenceEffectLayer>();
            foreach (var layerIndex in SequenceEffectService.FindLayerIndexes(character, section.Action))
            {
                var stacked = layerIndex == _sequenceEffectLayerIndex
                    ? layer
                    : service.Load(
                        character,
                        section.Action,
                        layerIndex: layerIndex,
                        expectedFrameCount: expectedFrameCount);
                if (stacked.HasFrames)
                {
                    stackLayers.Add(stacked);
                }
            }

            // 兜底：选中的这一层有内容、却没出现在盘上的层列表里（目录被人手工挪过），
            // 也得叠进去，别让"正在画的那一层"看不见。
            if (layer.HasFrames &&
                !stackLayers.Any(item =>
                    string.Equals(item.LayerName, layer.LayerName, StringComparison.Ordinal)))
            {
                stackLayers.Add(layer);
            }

            _sequenceEffectStackLayers = stackLayers;
            _sequenceEffectPreviewCache.Clear();
            // 清完立刻补一次预加载 —— 否则播放中重载特效层（改帧/换动作）后缓存是空的，
            // 接下来每一帧都命中不了（晓桀 2026-09-25：那会一路闪到最后全看不见）。
            _ = PreloadSequenceEffectBitmapsAsync();
            _sequenceEffectShownFrameOrdinal = -1;
            _sequenceEffectSubFrame = 0;
            _sequenceEffectLastSubFrame = -1;
            _sequenceEffectPinnedSlot = null;
            _applicationViewModel.SequenceFrames.SetEffectLayer(
                layer.HasFrames ? layer : null,
                _sequenceEffectLayerIndex);
            UpdateSequenceEffectLayerSource();
        }

        /// <summary>
        /// 帧清单被改过之后（增删 / 替换 / 改帧率）重读一次特效层。
        ///
        /// 特效层是"按动作算出来的"：一个动作帧占几格变了，特效该有多少张、每一格对应哪一张
        /// 都会跟着变（时间轴按特效帧摊开的那份列表也一样）。编辑器没开着就什么都不用做。
        /// </summary>
        private void RefreshSequenceEffectLayerIfEditorOpen()
        {
            if (SequenceFramesManagerHost.Visibility != Visibility.Visible)
            {
                return;
            }

            if (CharacterDesk.CurrentCharacter is not { } character ||
                _applicationViewModel.SequenceFrames.SelectedSection is not { } section)
            {
                return;
            }

            ReloadEffectLayer(character, section);
        }

        /// <summary>这个动作的特效该有多少张 = 导出底板会出多少张（总格数 × 倍数）。</summary>
        private int ResolveEffectFrameCount(CharacterCard character, SequenceFrameSection section)
        {
            if (section.Frames.Count == 0)
            {
                return 0;
            }

            return BasePlateExportPlanner
                .Build(
                    Settings.ProjectRootPath,
                    character.Code,
                    section.Action.Code,
                    section.Frames,
                    _applicationViewModel.SequenceFrames.PreviewFps)
                .Frames.Count;
        }

        // ── 两层预览 ────────────────────────────────────────────────────────

        /// <summary>
        /// 逐格的**叠层**序列：第 N 格该显示哪几帧（各层在这一格的帧，第 1 层在最下）。
        /// 空一格就是空数组（这一格谁都没画）。
        ///
        /// 为什么要"建"而不是"算"：以前每个 tick 现算序号 `startOrdinal + subFrame`，再去
        /// `layer.Frames` 里**线性查** `Ordinal` 相等的那一帧。而 `Frames` 里**只有导进来的那些帧**
        /// —— 缺号的根本不在列表里；预加载覆盖的又是"池子里的帧"，跟播放要的"逐格的帧"不是同一个集合。
        /// 于是池子里缺哪一号，播放就解析成空、白一帧：看上去就是"**有些图片闪一下**"
        /// （晓桀 2026-09-25 报的，特效本来就快，一闪就没了）。
        ///
        /// 建成序列之后：**缺号在"建"的时候一次定下来**（预加载照着它一次多带一张），
        /// 播放只按下标取，不再每 tick 查一遍。
        /// 重建的触发就是下面那把 key：换动作 / 改倍数 / 重新导入 / 帧数变了，叠层的组成变了也算。
        /// </summary>
        private List<IReadOnlyList<SequenceEffectFrame>> _sequenceEffectStackSlots = [];
        private string _sequenceEffectSlotsKey = string.Empty;

        /// <summary>
        /// 预览要叠加的所有特效层（**盘上有内容的层**，按层号从小到大 = 第 1 层在最下）。
        ///
        /// 预览是**多层叠加**的（晓桀 2026-09-27：「我之前想要的是多层叠加播放」）——
        /// 以前只显示"当前选中的那一层"，导进第 2 层之后看上去就像"第 1 层被清空了"。
        /// 在 <see cref="ReloadEffectLayer"/> 里读一次。
        /// </summary>
        private IReadOnlyList<SequenceEffectLayer> _sequenceEffectStackLayers = [];

        private void RebuildSequenceEffectSlots()
        {
            var frames = _applicationViewModel.SequenceFrames.PreviewFrames;
            var key = string.Join(
                "|",
                string.Join(
                    ",",
                    _sequenceEffectStackLayers.Select(layer =>
                        $"{layer.LayerName}#{layer.Frames.Count}#{layer.ImportedAt?.Ticks ?? 0}")),
                frames.Count,
                frames.Sum(frame => Math.Max(1, frame.DurationFrames)));
            if (string.Equals(key, _sequenceEffectSlotsKey, StringComparison.Ordinal))
            {
                return;
            }

            _sequenceEffectSlotsKey = key;
            _sequenceEffectStackSlots = [];
            if (_sequenceEffectStackLayers.Count == 0)
            {
                return;
            }

            // 每层一份"帧号 → 帧"，逐格查；层序就是叠放顺序（列表第 0 个在最下）。
            var framesByLayer = _sequenceEffectStackLayers
                .Select(layer => layer.Frames
                    .GroupBy(frame => frame.Ordinal)
                    .ToDictionary(group => group.Key, group => group.First()))
                .ToArray();
            var multiplier = ResolveSequenceEffectMultiplier();
            foreach (var frame in frames)
            {
                var slotCount = Math.Max(1, frame.DurationFrames) * multiplier;
                for (var slot = 0; slot < slotCount; slot++)
                {
                    var ordinal = _sequenceEffectStackSlots.Count + 1;
                    var stacked = new List<SequenceEffectFrame>(framesByLayer.Length);
                    foreach (var layerFrames in framesByLayer)
                    {
                        if (layerFrames.TryGetValue(ordinal, out var effectFrame) &&
                            effectFrame is { IsEmpty: false } &&
                            !string.IsNullOrWhiteSpace(effectFrame.FilePath))
                        {
                            stacked.Add(effectFrame);
                        }
                    }

                    _sequenceEffectStackSlots.Add(stacked);
                }
            }
        }

        /// <summary>
        /// 预览的节拍倍数：优先用选中那一层的（各层其实都一样），
        /// 选中层是空的（新建的层还没画）就退回动作的底板倍数。
        /// </summary>
        private int ResolveSequenceEffectMultiplier() => Math.Max(
            1,
            _applicationViewModel.SequenceFrames.EffectLayer?.Multiplier
                ?? BasePlateExportPlanner.Multiplier);

        /// <summary>按**格号**取这一格该显示哪张图（1 起；越界 / 没人画 → null）。</summary>
        private ImageSource? ResolveSequenceEffectSourceAt(int ordinal)
        {
            if (ordinal < 1 || ordinal > _sequenceEffectStackSlots.Count)
            {
                return null;
            }

            var stack = _sequenceEffectStackSlots[ordinal - 1];
            if (stack.Count == 0)
            {
                return null;
            }

            if (stack.Count == 1)
            {
                // 只有一层：老路（缓存键就是文件路径），不用合成。
                var single = stack[0];
                if (_sequenceEffectPreviewCache.TryGet(single.FilePath, out var cached))
                {
                    _sequenceEffectLastSource = cached;
                    return cached;
                }

                // **播放中绝不现解**。
                //
                // 以前这里同步 `LoadFile` 一张：一张 PNG 解在 UI 线程上要十几毫秒，特效层按"倍数"
                // 播（最狠 40fps，每帧只有 25ms）—— 解不过来就整条都跟不上：**每一帧都闪、最后全看不见**
                // （晓桀 2026-09-25 报的）。现在改成：命中不了就**沿用上一张**（画面冻一下也比闪强），
                // 同时把这个文件丢给预加载去补 —— 补上了下一 tick 就是命中。
                QueueSequenceEffectPreload(single.FilePath, single.FileName);
                return _sequenceEffectLastSource;
            }

            // 多层叠在同一格：合成图的缓存键带上"这一格到底叠了哪些文件"，
            // 换层 / 重新导入 / 删了某一层，键就变了，自然不会拿到旧的合成图。
            var stackKey = BuildSequenceEffectStackKey(ordinal, stack);
            if (_sequenceEffectPreviewCache.TryGet(stackKey, out var composed))
            {
                _sequenceEffectLastSource = composed;
                return composed;
            }

            QueueSequenceEffectStackPreload(stackKey, stack, ordinal);
            return _sequenceEffectLastSource;
        }

        /// <summary>
        /// 叠层合成图在缓存里的键：格号 + 参与的文件路径（顺序固定 = 从下往上叠）。
        /// 用路径而不是层号 —— 同一层重新导入会换文件，键跟着换。
        /// </summary>
        private static string BuildSequenceEffectStackKey(
            int ordinal,
            IReadOnlyList<SequenceEffectFrame> stack) =>
            $"stack:{ordinal}:" + string.Join("+", stack.Select(frame => frame.FilePath));

        private ImageSource? _sequenceEffectLastSource;
        private int _sequenceEffectLastSubFrame = -1;

        /// <summary>
        /// 播放中发现没预加载到的图：**异步**解一张放回缓存，不阻塞这一 tick。
        /// 同一个文件同时只会排一次（用一张"排队中"的表挡重复）。
        /// </summary>
        private readonly HashSet<string> _sequenceEffectPreloadQueue = new(StringComparer.OrdinalIgnoreCase);

        private void QueueSequenceEffectPreload(string filePath, string displayName)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !_sequenceEffectPreloadQueue.Add(filePath))
            {
                return;
            }

            _ = Task.Run(() =>
            {
                // ⚠️ 后台线程**只能解码**：`WriteableBitmap` 是 WinRT 对象，在这里建会抛
                // `0x8001010E`（RPC_E_WRONG_THREAD）—— 上一版就是这么错的（晓桀 2026-09-25 贴的日志）。
                // 解出来的字节回到 UI 线程再装成 bitmap。
                SequencePreviewBitmapCache.DecodedBitmap decoded;
                try
                {
                    decoded = SequencePreviewBitmapCache.DecodeToPixels(filePath);
                }
                catch (Exception ex)
                {
                    _sequenceEffectPreloadQueue.Remove(filePath);
                    DispatcherQueue.TryEnqueue(() =>
                        AppendLog(LogKind.Warning, $"特效帧解码不出来：{displayName}", ex));
                    return;
                }

                DispatcherQueue.TryEnqueue(() =>
                {
                    _sequenceEffectPreloadQueue.Remove(filePath);
                    try
                    {
                        var loaded = SequencePreviewBitmapCache.CreateFromPixels(decoded);
                        _sequenceEffectPreviewCache.Store(filePath, loaded);
                        _sequenceEffectLastSource = loaded;
                    }
                    catch (Exception ex)
                    {
                        AppendLog(LogKind.Warning, $"特效帧装不进画面：{displayName}", ex);
                    }
                });
            });
        }

        /// <summary>
        /// 播放中发现"这一格叠了多层、合成图还没备好"：**异步**在后台把几层解出来叠成一张，
        /// 回到 UI 线程装成 bitmap 放回缓存。同一个合成键同时只会排一次。
        ///
        /// 和 <see cref="QueueSequenceEffectPreload"/> 一个道理：后台线程**只能解码 / 合成字节**，
        /// `WriteableBitmap` 必须回 UI 线程建（否则 `0x8001010E`）。
        /// </summary>
        private readonly HashSet<string> _sequenceEffectStackPreloadQueue = new(StringComparer.OrdinalIgnoreCase);

        private void QueueSequenceEffectStackPreload(
            string stackKey,
            IReadOnlyList<SequenceEffectFrame> stack,
            int ordinal)
        {
            if (!_sequenceEffectStackPreloadQueue.Add(stackKey))
            {
                return;
            }

            var displayName = $"第 {ordinal} 格（{stack.Count} 层叠一起）";
            _ = Task.Run(() =>
            {
                SequencePreviewBitmapCache.DecodedBitmap composed;
                try
                {
                    composed = ComposeSequenceEffectStack(stack);
                }
                catch (Exception ex)
                {
                    _sequenceEffectStackPreloadQueue.Remove(stackKey);
                    DispatcherQueue.TryEnqueue(() =>
                        AppendLog(LogKind.Warning, $"特效叠层合成不出来：{displayName}", ex));
                    return;
                }

                DispatcherQueue.TryEnqueue(() =>
                {
                    _sequenceEffectStackPreloadQueue.Remove(stackKey);
                    try
                    {
                        var loaded = SequencePreviewBitmapCache.CreateFromPixels(composed);
                        _sequenceEffectPreviewCache.Store(stackKey, loaded);
                        _sequenceEffectLastSource = loaded;
                    }
                    catch (Exception ex)
                    {
                        AppendLog(LogKind.Warning, $"特效叠层装不进画面：{displayName}", ex);
                    }
                });
            });
        }

        /// <summary>
        /// 后台线程：把同一格的几层解码后叠成一张（第 0 个在最下）。
        /// 文件不在了 / 尺寸和第一层对不上的层跳过；一层都没解出来就抛，交给调用方记一条日志。
        /// </summary>
        private static SequencePreviewBitmapCache.DecodedBitmap ComposeSequenceEffectStack(
            IReadOnlyList<SequenceEffectFrame> stack)
        {
            var decoded = new List<SequencePreviewBitmapCache.DecodedBitmap>(stack.Count);
            foreach (var frame in stack)
            {
                if (!File.Exists(frame.FilePath))
                {
                    continue;
                }

                decoded.Add(SequencePreviewBitmapCache.DecodeToPixels(frame.FilePath));
            }

            if (decoded.Count == 0)
            {
                throw new InvalidOperationException("这一格的特效帧文件都不在了。");
            }

            var width = decoded[0].Width;
            var height = decoded[0].Height;
            var pixels = decoded
                .Where(bitmap => bitmap.Width == width && bitmap.Height == height)
                .Select(bitmap => bitmap.Pixels)
                .ToArray();
            return new SequencePreviewBitmapCache.DecodedBitmap(
                width, height, SequenceEffectFrameComposer.Compose(width, height, pixels));
        }

        /// <summary>
        /// 把当前时间点该显示的特效帧贴到两层预览上。
        ///
        /// 时序：一个角色帧占 <c>格数 × 倍数</c> 张特效帧（倍数就是导出底板那个 2），
        /// 所以特效层在自己的小定时器上推进，角色层照旧按素材帧推进 —— 两层合起来
        /// 正好是"角色 10fps、特效 20fps"的样子。一格上叠了好几层时，先在内存里叠成一张再贴。
        /// </summary>
        private void UpdateSequenceEffectLayerSource()
        {
            var frames = _applicationViewModel.SequenceFrames.PreviewFrames;
            var current = _applicationViewModel.SequenceFrames.CurrentPreviewFrame;
            // 门槛是"**盘上有没有特效**"，不是"选中那一层有没有"：选中的可能是个刚建的空层，
            // 而别的层画了东西 —— 那就照样叠出来播（晓桀 2026-09-27 报的"第 1 层像被清空了"）。
            if (frames.Count == 0 || current is null || _sequenceEffectStackLayers.Count == 0)
            {
                ShowSequenceEffectSource(null);
                return;
            }

            var currentIndex = frames.IndexOf(current);
            if (currentIndex < 0)
            {
                ShowSequenceEffectSource(null);
                return;
            }

            RebuildSequenceEffectSlots();

            // 这一帧在特效序列里的起点：前面所有帧的格数 × 倍数，再加本帧内的子帧号。
            var multiplier = ResolveSequenceEffectMultiplier();
            var startOrdinal = 1;
            for (var index = 0; index < currentIndex; index++)
            {
                startOrdinal += Math.Max(1, frames[index].DurationFrames) * multiplier;
            }

            // ── 诊断（只在两个事件上打，不刷屏）─────────────────────────────
            // "来回播"只有两个可能：① **当前帧**在被来回改（角色/时间轴那一侧）；
            // ② 当前帧没变、但**子帧自己绕回**了（`% slotCount` 到了头）。
            // 这两条日志分开报，跑一次就能看出是哪一侧。
            var slotCount = Math.Max(1, current.DurationFrames) * multiplier;
            if (_sequenceEffectShownFrameOrdinal != currentIndex)
            {
                AppendDiagnosticLog(
                    LogKind.Info,
                    $"[Effect Cursor] 切帧：{_sequenceEffectShownFrameOrdinal} → {currentIndex}" +
                    $"（子帧清零，原 counter={_sequenceEffectSubFrame}，本帧格数={slotCount}）");
                _sequenceEffectShownFrameOrdinal = currentIndex;
                _sequenceEffectSubFrame = 0;
            }

            var subFrame = ResolveSequenceEffectSubFrameOfCurrentFrame(current);
            if (_sequenceEffectLastSubFrame >= 0 && subFrame < _sequenceEffectLastSubFrame)
            {
                AppendDiagnosticLog(
                    LogKind.Info,
                    $"[Effect Cursor] 子帧绕回：frame={currentIndex} {_sequenceEffectLastSubFrame}→{subFrame}" +
                    $"（counter={_sequenceEffectSubFrame}，本帧格数={slotCount}）");
            }

            _sequenceEffectLastSubFrame = subFrame;

            var ordinal = startOrdinal + subFrame;
            ShowSequenceEffectSource(ResolveSequenceEffectSourceAt(ordinal));

            // **提前把下一格准备到暗的那张上**（同角色层的 `PrepareNextSequencePreviewSource`）：
            // 下一 tick 的 `Show` 就只剩"翻一下可见性"。
            PrepareSequenceEffectSource(ResolveSequenceEffectSourceAt(ordinal + 1));
        }

        /// <summary>
        /// 当前这一帧现在该显示第几张特效帧（0 起）—— 一个动作帧占"格数 × 倍数"张。
        ///
        /// 点住某一格时看钉住的那张（不跟定时器走）；否则按特效层自己的节拍走。
        /// **预览显示的那一张和时间轴上高亮的那一格共用这一个来源**，
        /// 所以两边不会各说各话（播放时高亮就是一格一格往前走的）。
        /// </summary>
        private int ResolveSequenceEffectSubFrameOfCurrentFrame(SequenceFrameItem frame)
        {
            var multiplier = ResolveSequenceEffectMultiplier();
            var slotCount = Math.Max(1, frame.DurationFrames) * multiplier;
            if (_sequenceEffectPinnedSlot is { } pinned && pinned.FrameIndex == frame.Index)
            {
                return Math.Min(pinned.SubIndex, slotCount - 1);
            }

            return _sequenceEffectSubFrame % slotCount;
        }

        private void ShowSequenceEffectSource(ImageSource? source)
        {
            SequencePreviewEffectPresenter.Show(source);
            SequenceEditorPreviewEffectPresenter.Show(source);
        }

        /// <summary>
        /// 把某张图**先铺到暗的那张上**（不翻页）。下一 tick 的
        /// <see cref="ShowSequenceEffectSource"/> 于是只是切换可见性，
        /// 不会出现"已经翻过去了、图还没铺好"的那一帧空窗。
        /// </summary>
        private void PrepareSequenceEffectSource(ImageSource? source)
        {
            SequencePreviewEffectPresenter.Prepare(source);
            SequenceEditorPreviewEffectPresenter.Prepare(source);
        }

        /// <summary>
        /// 特效层**先解码好再播** —— 和角色层那条 `PreloadSequencePreviewBitmapsAsync` 同一个道理。
        ///
        /// 以前只有角色层预加载，特效层是**播放中现用现解**（`UpdateSequenceEffectLayerSource()`
        /// 在 UI 线程的定时器回调里同步 `LoadFile`）。特效层比角色层快"倍数"倍、每帧都要换图，
        /// 于是播放一闪一闪、卡到看不清（晓桀 2026-09-25 报的）。
        /// 现在开播前把所有非空的特效帧一口气解完，播放路径只查缓存。
        ///
        /// 空帧不进预加载（它本来就没有文件）；解不出来的给一条提示 + 一行日志，
        /// 和角色层保持一致 —— 不静默。
        /// </summary>
        private async Task PreloadSequenceEffectBitmapsAsync()
        {
            if (_sequenceEffectStackLayers.Count == 0)
            {
                return;
            }

            // 照着**逐格序列**加载（一次多带一张）：播放要的是"逐格的帧"，
            // 以前加载的是"池子里的帧" —— 两个集合不是一回事（缺号的格子到时候才现解，卡一下）。
            RebuildSequenceEffectSlots();
            // 每次开播都重新试一遍（失败不该是永久的）。
            _sequenceEffectPreviewCache.ResetFailures();

            // 单层的格子照老路按文件路径解；叠了多层的格子解完还要**合成一张**，
            // 缓存键是叠层键 —— 两种分开走。
            var singleFiles = new List<(string FilePath, string DisplayName)>();
            var stackSlots = new List<(
                string StackKey,
                int Ordinal,
                List<(string FilePath, string DisplayName)> Files)>();
            for (var index = 0; index < _sequenceEffectStackSlots.Count; index++)
            {
                var stack = _sequenceEffectStackSlots[index];
                if (stack.Count == 1)
                {
                    singleFiles.Add((stack[0].FilePath, stack[0].FileName));
                }
                else if (stack.Count > 1)
                {
                    var ordinal = index + 1;
                    stackSlots.Add((
                        BuildSequenceEffectStackKey(ordinal, stack),
                        ordinal,
                        stack.Select(frame => (FilePath: frame.FilePath, DisplayName: frame.FileName)).ToList()));
                }
            }

            var failures = new List<SequencePreviewBitmapLoadFailure>(
                await _sequenceEffectPreviewCache.PreloadPathsAsync(singleFiles.Distinct()));
            foreach (var (stackKey, ordinal, files) in stackSlots)
            {
                failures.AddRange(await _sequenceEffectPreviewCache.PreloadStackAsync(
                    stackKey,
                    files,
                    $"第 {ordinal} 格（{files.Count} 层叠一起）"));
            }

            if (failures.Count == 0)
            {
                return;
            }

            ShowFloatingTip(
                InfoBarSeverity.Warning,
                "部分特效帧读取失败",
                failures.Count == 1
                    ? failures[0].FileName
                    : $"{failures[0].FileName} 等 {failures.Count} 张特效帧无法读取。");
            AppendLog(
                LogKind.Warning,
                "St5 特效层预加载部分失败：" + string.Join(
                    Environment.NewLine,
                    failures
                        .Take(8)
                        .Select(failure =>
                            $"{failure.FileName} | {failure.ExceptionType ?? "LoadError"} | {failure.Message} | {failure.FilePath}"))
                    + (failures.Count > 8 ? $"{Environment.NewLine}... 还有 {failures.Count - 8} 张失败。" : string.Empty));
        }

        /// <summary>特效层自己的节拍（比角色层快"倍数"倍）；跟着预览的播放/暂停一起开关。</summary>
        private void StartSequenceEffectSubFrameTimer()
        {
            _sequenceEffectSubFrameTimer ??= CreateSequenceEffectSubFrameTimer();
            _sequenceEffectSubFrameTimer.Interval = ResolveSequenceEffectSubFrameInterval();
            _sequenceEffectSubFrameTimer.Start();
        }

        private void StopSequenceEffectSubFrameTimer() => _sequenceEffectSubFrameTimer?.Stop();

        private DispatcherQueueTimer CreateSequenceEffectSubFrameTimer()
        {
            var timer = DispatcherQueue.CreateTimer();
            timer.Tick += (_, _) =>
            {
                _sequenceEffectSubFrame++;
                UpdateSequenceEffectLayerSource();
                // **高亮必须跟着特效层一起走。**
                //
                // 时间轴展开之后，一个动作帧占"格数 × 倍数"格；真正一格一格往前的是特效层
                // （它有自己的、快"倍数"倍的节拍）。以前高亮只挂在**角色层**那次 tick 上，
                // 于是它永远停在每帧的第一格，中间那些展开出来的特效帧格在时间轴上
                // **一次都不会亮** —— 看着就像"还在按原来的帧格播"（晓桀 2026-09-25 报的）。
                SynchronizeSequenceTimelineSelectionToCurrentFrame();
            };
            return timer;
        }

        private TimeSpan ResolveSequenceEffectSubFrameInterval()
        {
            var fps = Math.Clamp(_applicationViewModel.SequenceFrames.PreviewFps, 1, 60);
            var multiplier = Math.Max(1, _applicationViewModel.SequenceFrames.EffectLayer?.Multiplier ?? 1);
            return TimeSpan.FromMilliseconds(1000d / (fps * multiplier));
        }
    }
}
