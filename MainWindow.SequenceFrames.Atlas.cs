using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;
using CrossingVoidZDTool.Services.Atlas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CrossingVoidZDTool
{
    public sealed partial class MainWindow
    {
        /// <summary>
        /// 把当前动作的序列帧打成一张图集，落 <c>Export/&lt;角色&gt;/Atlas/&lt;动作&gt;/</c>。
        ///
        /// 这是「手动导出」那条链路：产物是**交付物**，用户会拿走，所以不自动清理。
        /// 序列同步时顺带打的那份落 <c>tool/AtlasCache</c>，是另一条链路（见 AtlasDestination）。
        ///
        /// 两处刻意不做：
        /// <list type="bullet">
        /// <item>不传装箱参数 —— 那是图集工具自己的事，工具箱硬编码进去两边就锁死了。</item>
        /// <item>不因打包失败而改动序列数据 —— 这只是「看一眼成品」，不该有副作用。</item>
        /// </list>
        /// </summary>
        private async void SequenceEditorExportAtlasButton_Click(object sender, RoutedEventArgs e)
        {
            if (SequenceEditorExportAtlasButton.IsEnabled == false)
            {
                return;
            }

            if (CharacterDesk.CurrentCharacter is not { } character)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择角色", "请先在角色台选择当前制作角色。");
                return;
            }

            // 编辑器里正在编辑哪个动作，就导出哪个动作 —— 与「打开序列编辑器」同一套判定，
            // 免得出现「看着 A 却导出了 B」。
            //
            // 注意这里**只看「有没有选中动作」，不看帧数**：图集的帧来自素材目录，
            // 一个动作的序列帧全被删了、但素材目录里还留着 PNG，图集照样能打。
            var section = _applicationViewModel.SequenceFrames.SelectedSection;
            if (section is null)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择动作", "先打开一个动作的序列编辑器，再导出图集。");
                return;
            }

            SequenceEditorExportAtlasButton.IsEnabled = false;
            try
            {
                var action = section.Action;
                if (!SequenceActionCatalog.TryResolve(action.Code, out var definition, out var parsedForm))
                {
                    ShowFloatingTip(InfoBarSeverity.Error, "无法导出图集", $"不认识的动作代号：{action.Code}");
                    return;
                }

                // 动作自带的 FormIndex 优先，解析出来的兜底 —— 两者不一致时以动作卡为准。
                var formIndex = action.FormIndex > 1 ? action.FormIndex : parsedForm;
                var variantCode = SequenceActionCatalog.GetVariantCode(definition, formIndex);
                // 图集的帧列表来自**素材目录**，不是序列清单：
                // 素材目录里有几张 PNG 就打几个格子，谁复用谁是序列那边的事。
                var framesFolder = SequenceActionFolderLayout.GetFramesFolderPath(character, action);
                var outputDirectory = AtlasPackService.ResolveOutputDirectory(
                    AtlasDestination.Export,
                    Settings.ProjectRootPath,
                    character.FolderPath,
                    character.Code,
                    variantCode);

                ShowGlobalProgress("导出图集", $"{character.Code} / {variantCode}");
                var progress = new Progress<AtlasPackProgress>(update =>
                    UpdateGlobalProgress(
                        update.Message,
                        update.Stage == AtlasPackStage.Finished ? 100 : 0,
                        $"{character.Code} / {variantCode}",
                        update.Stage != AtlasPackStage.Finished));

                var result = await new AtlasPackService().PackAsync(
                    character.Code,
                    framesFolder,
                    definition,
                    formIndex,
                    outputDirectory,
                    Settings.AtlasPythonPath,
                    progress,
                    GetGlobalProgressCancellationToken());

                CompleteGlobalProgress($"图集已导出：{result.SizeText}", result.OutputDirectory);
                await HideGlobalProgressAfterDelayAsync(900);

                AppendLog(LogKind.User,
                    $"导出图集：{character.Code} / {variantCode} → {result.SizeText}，"
                    + $"{result.FrameCount} 帧，{result.OutputDirectory}");

                // 尺寸和帧数是用户唯一需要当场确认的两件事，所以直接开对话框报出来，
                // 不要求他再去翻目录。打开目录的入口就放在对话框里。
                await ShowAtlasExportedDialogAsync(character.Code, variantCode, result);
            }
            catch (OperationCanceledException)
            {
                CompleteGlobalProgress("导出图集已取消", CharacterDesk.CurrentCharacter?.Code ?? string.Empty);
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (Exception ex)
            {
                CompleteGlobalProgress("导出图集失败", ex.Message);
                await HideGlobalProgressAfterDelayAsync();
                ShowFloatingTip(InfoBarSeverity.Error, "导出图集失败", ex.Message);
                AppendLog(LogKind.Error, "导出图集失败。", ex);
            }
            finally
            {
                SequenceEditorExportAtlasButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// 导出成功后的小结对话框：给出尺寸、帧数、落点，并直接提供「打开目录」。
        /// 图集是给美术拿去核对的东西，所以这里给出路径本身（可选可复制），
        /// 而不只是个「成功了」。
        /// </summary>
        private async Task ShowAtlasExportedDialogAsync(
            string characterCode,
            string variantCode,
            AtlasPackResult result)
        {
            var openFolderButton = new Button
            {
                Content = "打开目录",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Style = (Style)Application.Current.Resources["DialogAccentButtonStyle"],
            };
            openFolderButton.Click += (_, _) =>
            {
                try
                {
                    Directory.CreateDirectory(result.OutputDirectory);
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = result.OutputDirectory,
                        UseShellExecute = true,
                    });
                }
                catch (Exception ex)
                {
                    ShowFloatingTip(InfoBarSeverity.Error, "目录打开失败", ex.Message);
                    AppendLog(LogKind.Error, "图集目录打开失败。", ex);
                }
            };

            var dialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = $"图集已导出 · {characterCode} / {variantCode}",
                CloseButtonText = "关闭",
                DefaultButton = ContentDialogButton.Close,
                Content = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"{result.SizeText} 像素 · {result.FrameCount} 帧",
                            FontSize = 18,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        },
                        new TextBlock
                        {
                            Text = result.OutputDirectory,
                            IsTextSelectionEnabled = true,
                            TextWrapping = TextWrapping.Wrap,
                            Style = (Style)Application.Current.Resources["SubtleTextStyle"],
                        },
                        openFolderButton,
                    },
                },
            };
            await dialog.ShowAsync();
        }
    }
}
