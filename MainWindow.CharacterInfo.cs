using System;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VirtualKey = Windows.System.VirtualKey;

namespace CrossingVoidZDTool
{
    public sealed partial class MainWindow
    {
        private async void ApplyCharacterCodeButton_Click(object sender, RoutedEventArgs e)
        {
            if (CharacterDesk.CurrentCharacter is null)
            {
                return;
            }

            var character = CharacterDesk.CurrentCharacter;
            var oldCode = character.Code;
            var newCode = CharacterCodeTextBox.Text.Trim();
            if (string.Equals(oldCode, newCode, StringComparison.Ordinal))
            {
                ShowFloatingTip(InfoBarSeverity.Informational, "英文代号未变化", oldCode);
                return;
            }

            try
            {
                _characterInfoSaveTimer.Stop();
                await _applicationViewModel.UnrealSync.SaveAsync(character);
                _applicationViewModel.UnrealSync.MaterialSections.Clear();

                var renamedCharacter = await Task.Run(
                    () => new CharacterWorkspaceService().RenameCharacterCode(character, newCode));
                CharacterDesk.ReplaceCharacter(renamedCharacter, oldCode);
                Settings.SetCurrentCharacter(renamedCharacter.Code, renamedCharacter.Code);
                await _applicationViewModel.UnrealSync.LoadAsync(renamedCharacter);
                MarkLastEditedModule("UnrealSync");
                ShowFloatingTip(InfoBarSeverity.Success, "英文代号已修改", $"{oldCode} -> {renamedCharacter.Code}");
                AppendLog(LogKind.User, $"修改角色英文代号：{oldCode} -> {renamedCharacter.Code}");
            }
            catch (Exception ex)
            {
                CharacterCodeTextBox.Text = oldCode;
                ShowFloatingTip(InfoBarSeverity.Error, "英文代号修改失败", ex.Message);
                AppendLog(LogKind.Error, "角色英文代号修改失败。", ex);
            }
        }

        private async Task RefreshCharacterInfoAsync()
        {
            await _applicationViewModel.UnrealSync.LoadAsync(CharacterDesk.CurrentCharacter);
        }

        private async Task RefreshCharacterInfoWithFeedbackAsync()
        {
            try
            {
                await RefreshCharacterInfoAsync();
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "角色信息加载失败", ex.Message);
                AppendLog(LogKind.Error, "角色信息加载失败。", ex);
            }
        }

        private void ScheduleCharacterInfoSave()
        {
            RunOnUiThread(() =>
            {
                if (UnrealSyncPage.Visibility == Visibility.Visible)
                {
                    MarkLastEditedModule(ToolboxModuleKey.UnrealSync);
                    PersistCurrentCharacterSelection();
                }

                _characterInfoSaveTimer.Stop();
                _characterInfoSaveTimer.Start();
            });
        }

        private void FlushPendingCharacterInfoSave()
        {
            try
            {
                _characterInfoSaveTimer.Stop();
                if (_applicationViewModel.UnrealSync.SaveNow(CharacterDesk.CurrentCharacter))
                {
                    if (CharacterDesk.CurrentCharacter is not null &&
                        !string.IsNullOrWhiteSpace(_applicationViewModel.UnrealSync.CharacterName))
                    {
                        CharacterDesk.SynchronizeCurrentCharacterDisplayNameAsync(_applicationViewModel.UnrealSync.CharacterName)
                            .GetAwaiter()
                            .GetResult();
                    }

                }
            }
            catch (Exception ex)
            {
                AppendLog(LogKind.Error, "角色信息关闭前保存失败。", ex);
            }
        }

        private async void CharacterInfoSaveTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            sender.Stop();
            await SaveCharacterInfoNowAsync();
        }

        private async Task SaveCharacterInfoNowAsync()
        {
            try
            {
                if (await _applicationViewModel.UnrealSync.SaveAsync(CharacterDesk.CurrentCharacter))
                {
                    if (CharacterDesk.CurrentCharacter is not null &&
                        !string.IsNullOrWhiteSpace(_applicationViewModel.UnrealSync.CharacterName))
                    {
                        await CharacterDesk.SynchronizeCurrentCharacterDisplayNameAsync(_applicationViewModel.UnrealSync.CharacterName);
                    }

                }
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "角色信息保存失败", ex.Message);
                AppendLog(LogKind.Error, "角色信息自动保存失败。", ex);
            }
        }

        private void AddPassiveSkillButton_Click(object sender, RoutedEventArgs e)
        {
            _applicationViewModel.UnrealSync.AddPassiveSkill();
            MarkLastEditedModule("UnrealSync");
        }

        private void AddKeywordTagButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is CharacterKeywordTagCategory category)
            {
                _applicationViewModel.UnrealSync.AddKeywordTag(category);
                MarkLastEditedModule("UnrealSync");
            }
        }

        private void RemoveKeywordTagButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is CharacterKeywordTagEntry entry)
            {
                _applicationViewModel.UnrealSync.RemoveKeywordTag(entry);
                MarkLastEditedModule("UnrealSync");
            }
        }

        private void CharacterInfoTextEntry_TextChanged(object sender, TextChangedEventArgs e)
        {
            _applicationViewModel.UnrealSync.NotifyTextEntryEdited();
        }

        private void UnrealSyncBlankArea_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (!IsBlankAreaRightTap(UnrealSyncPage, e.OriginalSource))
            {
                return;
            }

            CommitFocusedStatInput();
            UnrealSyncFocusSink.Focus(FocusState.Programmatic);
            e.Handled = true;
        }

        private void CharacterInfoEditorControl_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
        }

        private void DecreaseStatButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is CharacterInfoStat stat)
            {
                _applicationViewModel.UnrealSync.ChangeStat(stat, -1);
            }
        }

        private void IncreaseStatButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is CharacterInfoStat stat)
            {
                _applicationViewModel.UnrealSync.ChangeStat(stat, 1);
            }
        }

        private void StatNumberBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (sender is not NumberBox numberBox ||
                numberBox.DataContext is not CharacterInfoStat stat)
            {
                return;
            }

            var controlDown = IsControlDown();
            if (controlDown && e.Key == VirtualKey.C)
            {
                _clipboardNumberValue = stat.Value;
                e.Handled = true;
                return;
            }

            if (controlDown && e.Key == VirtualKey.V && _clipboardNumberValue.HasValue)
            {
                _applicationViewModel.UnrealSync.SetStat(stat, _clipboardNumberValue.Value);
                e.Handled = true;
                return;
            }

            if (e.Key == VirtualKey.Enter)
            {
                CommitStatNumberBox(numberBox, stat);
                UnrealSyncFocusSink.Focus(FocusState.Programmatic);
                e.Handled = true;
            }
        }

        private void StatNumberBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is NumberBox numberBox &&
                numberBox.DataContext is CharacterInfoStat stat)
            {
                CommitStatNumberBox(numberBox, stat);
            }
        }

        private void CommitStatNumberBox(NumberBox numberBox, CharacterInfoStat stat)
        {
            if (!double.IsNaN(numberBox.Value) && !double.IsInfinity(numberBox.Value))
            {
                var value = Math.Max(0, (int)Math.Round(numberBox.Value));
                if (stat.Kind == CharacterInfoStatKind.FormLimit)
                {
                    value = Math.Max(1, value);
                }

                _applicationViewModel.UnrealSync.SetStat(stat, value);
            }
            else
            {
                numberBox.Value = stat.Value;
            }
        }

        private void CommitFocusedStatInput()
        {
            if (Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(UnrealSyncPage.XamlRoot) is NumberBox numberBox &&
                numberBox.DataContext is CharacterInfoStat stat)
            {
                CommitStatNumberBox(numberBox, stat);
            }
        }

        private void CopyPasteTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (sender is not TextBox textBox || !IsControlDown())
            {
                return;
            }

            if (e.Key == VirtualKey.C)
            {
                _clipboardTextValue = textBox.Text;
                e.Handled = true;
                return;
            }

            if (e.Key == VirtualKey.V && _clipboardTextValue is not null)
            {
                textBox.Text = _clipboardTextValue;
                textBox.SelectionStart = textBox.Text.Length;
                e.Handled = true;
            }
        }

        private static bool IsControlDown()
        {
            return (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
        }

    }
}
