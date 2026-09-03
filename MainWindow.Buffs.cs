using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace CrossingVoidZDTool
{
    public sealed partial class MainWindow
    {
        private async Task RefreshBuffsAsync()
        {
            await _applicationViewModel.Buffs.LoadAsync(CharacterDesk.CurrentCharacter);
        }

        private async Task RefreshBuffsWithFeedbackAsync()
        {
            try
            {
                await RefreshBuffsAsync();
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "BUFF 加载失败", ex.Message);
                AppendLog(LogKind.Error, "BUFF 加载失败。", ex);
            }
        }

        private void ScheduleBuffsSave()
        {
            RunOnUiThread(() =>
            {
                if (BuffsPage.Visibility == Visibility.Visible)
                {
                    MarkLastEditedModule(ToolboxModuleKey.Buffs);
                    PersistCurrentCharacterSelection();
                }

                _buffsSaveTimer.Stop();
                _buffsSaveTimer.Start();
            });
        }

        private async void BuffsSaveTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            sender.Stop();
            await SaveBuffsNowAsync();
        }

        private async Task SaveBuffsNowAsync()
        {
            try
            {
                await _applicationViewModel.Buffs.SaveAsync(CharacterDesk.CurrentCharacter);
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "BUFF 保存失败", ex.Message);
                AppendLog(LogKind.Error, "BUFF 自动保存失败。", ex);
            }
        }

        private void FlushPendingBuffsSave()
        {
            try
            {
                _buffsSaveTimer.Stop();
                _applicationViewModel.Buffs.SaveNow(CharacterDesk.CurrentCharacter);
            }
            catch (Exception ex)
            {
                AppendLog(LogKind.Error, "BUFF 关闭前保存失败。", ex);
            }
        }

        private async void AddBuffButton_Click(object sender, RoutedEventArgs e)
        {
            if (CharacterDesk.CurrentCharacter is null)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择角色", "请先在角色台选择当前制作角色。");
                AppendLog(LogKind.Warning, "新增 BUFF 失败：当前未选择角色。");
                return;
            }

            try
            {
                await _applicationViewModel.Buffs.AddBuffAsync(CharacterDesk.CurrentCharacter);
                MarkLastEditedModule("Buffs");
                AppendLog(LogKind.User, $"新增 BUFF 卡：{CharacterDesk.CurrentCharacter.Code}");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "新增 BUFF 失败", ex.Message);
                AppendLog(LogKind.Error, "新增 BUFF 卡失败。", ex);
            }
        }

        private async void DeleteBuffButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { CommandParameter: BuffEntry buff })
            {
                return;
            }

            await ConfirmAndDeleteBuffAsync(buff);
        }

        private async void DeleteBuffMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { CommandParameter: BuffEntry buff })
            {
                return;
            }

            await ConfirmAndDeleteBuffAsync(buff);
        }

        private async Task ConfirmAndDeleteBuffAsync(BuffEntry buff)
        {
            var result = await _dialogService.ShowContentAsync(new CrossingVoidZDTool.Services.ContentDialogRequest(
                "删除 BUFF",
                new TextBlock
                {
                    Text = $"是否要删除 {buff.DisplayName}？",
                    TextWrapping = TextWrapping.Wrap,
                    Width = 420
                },
                PrimaryButtonText: "删除",
                CloseButtonText: string.Empty,
                SecondaryButtonText: "取消",
                DefaultButton: ContentDialogButton.Secondary,
                PrimaryButtonStyle: (Style)Application.Current.Resources["DialogAccentButtonStyle"]));
            if (result != CrossingVoidZDTool.Services.DialogResultKind.Primary)
            {
                AppendLog(LogKind.Info, $"取消删除 BUFF：{buff.GeneratedCode}");
                return;
            }

            if (_applicationViewModel.Buffs.RemoveBuff(CharacterDesk.CurrentCharacter, buff))
            {
                if (ReferenceEquals(_applicationViewModel.Buffs.SelectedBuff, buff))
                {
                    CloseBuffEditor();
                }

                MarkLastEditedModule("Buffs");
                AppendLog(LogKind.User, $"删除 BUFF 卡：{buff.GeneratedCode}");
            }
        }

        private void OpenBuffEditorButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { CommandParameter: BuffEntry buff })
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "BUFF 打开失败", "没有识别到被点击的 BUFF 卡。");
                AppendLog(LogKind.Warning, "BUFF editor open failed. BuffFromEvent=<null>.");
                return;
            }

            _applicationViewModel.Buffs.OpenEditor(buff);
            BuffEditorHost.Visibility = Visibility.Visible;
            AnimateReferenceOverlay(BuffEditorHost, BuffEditorCardScale, show: true);
            BuffEditorHost.Focus(FocusState.Programmatic);
            AppendLog(LogKind.User, $"打开 BUFF 编辑：{buff.GeneratedCode}");
        }

        private async void CloseBuffEditor()
        {
            if (BuffEditorHost.Visibility != Visibility.Visible)
            {
                _applicationViewModel.Buffs.CloseEditor();
                return;
            }

            await AnimateReferenceOverlayAsync(BuffEditorHost, BuffEditorCardScale, show: false);
            BuffEditorHost.Visibility = Visibility.Collapsed;
            _applicationViewModel.Buffs.CloseEditor();
        }

        private void BuffEditorCloseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseBuffEditor();
        }

        private void BuffEditorHost_Tapped(object sender, TappedRoutedEventArgs e)
        {
            CloseBuffEditor();
        }

        private void BuffEditorCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
        }

        private void BuffEditorHost_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            CloseBuffEditor();
            e.Handled = true;
        }

        private void BuffEditorHost_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                CloseBuffEditor();
                e.Handled = true;
            }
        }

        private async void ImportBuffIconButton_Click(object sender, RoutedEventArgs e)
        {
            if (CharacterDesk.CurrentCharacter is null ||
                sender is not Button { CommandParameter: BuffEntry buff })
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "无法导入图标", "请先选择角色和 BUFF 卡。");
                AppendLog(LogKind.Warning, "导入 BUFF 图标失败：当前未选择角色或未找到 BUFF 卡。");
                return;
            }

            try
            {
                await ShowBuffIconPickerAsync(buff);
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "BUFF 图标选择失败", ex.Message);
                AppendLog(LogKind.Error, "BUFF 图标选择失败。", ex);
            }
        }

        private async void UseDefaultBuffIconButton_Click(object sender, RoutedEventArgs e)
        {
            if (CharacterDesk.CurrentCharacter is null ||
                sender is not Button { CommandParameter: BuffEntry buff })
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "无法使用默认图标", "请先选择角色和 BUFF 卡。");
                AppendLog(LogKind.Warning, "使用默认 BUFF 图标失败：当前未选择角色或未找到 BUFF 卡。");
                return;
            }

            try
            {
                var changed = await _applicationViewModel.Buffs.EnsureDefaultIconAsync(CharacterDesk.CurrentCharacter, buff);
                if (changed)
                {
                    MarkLastEditedModule(ToolboxModuleKey.Buffs);
                    AppendLog(LogKind.User, $"使用默认 BUFF 图标：{buff.GeneratedCode} <- 内置默认图标");
                }
                else
                {
                    ShowFloatingTip(InfoBarSeverity.Warning, "默认图标未应用", "可能已存在图标，或内置默认图标缺失。");
                    AppendLog(LogKind.Warning, $"默认 BUFF 图标未应用：{buff.GeneratedCode}。可能已存在图标，或内置默认图标缺失。");
                }
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "默认图标应用失败", ex.Message);
                AppendLog(LogKind.Error, "默认 BUFF 图标应用失败。", ex);
            }
        }

        private void BuffTextBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key != Windows.System.VirtualKey.Enter)
            {
                return;
            }

            if (sender is TextBox textBox && !textBox.AcceptsReturn)
            {
                RootGrid.Focus(FocusState.Programmatic);
                e.Handled = true;
            }
        }

        private void AddBuffEffectButton_Click(object sender, RoutedEventArgs e)
        {
            if (_applicationViewModel.Buffs.SelectedBuff is not BuffEntry buff)
            {
                return;
            }

            _applicationViewModel.Buffs.AddEffect(buff);
        }

        private void RemoveBuffEffectButton_Click(object sender, RoutedEventArgs e)
        {
            if (_applicationViewModel.Buffs.SelectedBuff is not BuffEntry buff ||
                sender is not Button { CommandParameter: BuffEffectModule effect })
            {
                return;
            }

            _applicationViewModel.Buffs.RemoveEffect(buff, effect);
        }

        private void OpenBuffFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (CharacterDesk.CurrentCharacter is null)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择角色", "请先在角色台选择当前制作角色。");
                AppendLog(LogKind.Warning, "打开 BUFF 文件夹失败：当前未选择角色。");
                return;
            }

            var folderPath = _applicationViewModel.Buffs.GetBuffRootPath(CharacterDesk.CurrentCharacter);
            Directory.CreateDirectory(folderPath);
            Process.Start(new ProcessStartInfo
            {
                FileName = folderPath,
                UseShellExecute = true
            });
        }
    }
}
