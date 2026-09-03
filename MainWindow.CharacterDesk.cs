using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;
using CrossingVoidZDTool.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace CrossingVoidZDTool
{
    public sealed partial class MainWindow
    {
        private CharacterCard? _characterDetailCharacter;

        private async Task LoadCharacterCardsAsync()
        {
            try
            {
                await CharacterDesk.LoadCharactersAsync(
                    Settings.ProjectRootPath,
                    Settings.CurrentCharacterCode,
                    Settings.LastEditedCharacterCode);
                PersistCurrentCharacterSelection();
                AppendLog(LogKind.Info, $"已加载角色卡：{CharacterDesk.Characters.Count} 张。");
            }
            catch (Exception ex)
            {
                CharacterDesk.StatusText = $"角色卡加载失败：{ex.Message}";
                AppendLog(LogKind.Error, "角色卡加载失败。", ex);
            }
        }

        private async void AddCharacterButton_Click(object sender, RoutedEventArgs e)
        {
            var characterName = await ShowCharacterCreateDialogAsync();
            if (string.IsNullOrWhiteSpace(characterName))
            {
                return;
            }

            try
            {
                var character = await CharacterDesk.CreateCharacterAsync(characterName);
                PersistCurrentCharacterSelection();
                ShowFloatingTip(InfoBarSeverity.Success, "角色卡已创建", $"{character.Name} / {character.Code}");
                AppendLog(LogKind.User, $"创建角色卡：{character.Name} / {character.Code}");
            }
            catch (Exception ex)
            {
                CharacterDesk.StatusText = $"创建角色失败：{ex.Message}";
                AppendLog(LogKind.Error, "创建角色卡失败。", ex);
            }
        }

        private Task<string?> ShowCharacterCreateDialogAsync()
        {
            _characterCreateDialogCompletion = new TaskCompletionSource<string?>();
            CharacterCreateNameTextBox.Text = string.Empty;
            CharacterCreateErrorInfoBar.IsOpen = false;
            CharacterCreateDialogHost.Visibility = Visibility.Visible;
            AnimateCharacterCreateDialog(show: true);
            CharacterCreateDialogHost.Focus(FocusState.Programmatic);
            CharacterCreateNameTextBox.Focus(FocusState.Programmatic);
            return _characterCreateDialogCompletion.Task;
        }

        private async void CompleteCharacterCreateDialog(string? characterName)
        {
            if (_characterCreateDialogCompletion is null)
            {
                return;
            }

            var completion = _characterCreateDialogCompletion;
            _characterCreateDialogCompletion = null;
            await AnimateCharacterCreateDialogAsync(show: false);
            CharacterCreateDialogHost.Visibility = Visibility.Collapsed;
            CharacterCreateErrorInfoBar.IsOpen = false;
            CharacterCreateNameTextBox.Text = string.Empty;
            completion.TrySetResult(characterName);
        }

        private void AnimateCharacterCreateDialog(bool show)
        {
            _ = AnimateCharacterCreateDialogAsync(show);
        }

        private Task AnimateCharacterCreateDialogAsync(bool show)
        {
            var completion = new TaskCompletionSource();
            var fromOpacity = show ? 0d : 1d;
            var toOpacity = show ? 1d : 0d;
            var fromScale = show ? 0.96d : 1d;
            var toScale = show ? 1d : 0.97d;

            CharacterCreateDialogHost.Opacity = fromOpacity;
            CharacterCreateDialogCardScale.ScaleX = fromScale;
            CharacterCreateDialogCardScale.ScaleY = fromScale;

            var easing = new CubicEase { EasingMode = show ? EasingMode.EaseOut : EasingMode.EaseIn };
            var duration = TimeSpan.FromMilliseconds(show ? 180 : 140);
            var storyboard = new Storyboard();

            var opacityAnimation = new DoubleAnimation
            {
                From = fromOpacity,
                To = toOpacity,
                Duration = duration,
                EasingFunction = easing
            };
            Storyboard.SetTarget(opacityAnimation, CharacterCreateDialogHost);
            Storyboard.SetTargetProperty(opacityAnimation, nameof(UIElement.Opacity));
            storyboard.Children.Add(opacityAnimation);

            var scaleXAnimation = new DoubleAnimation
            {
                From = fromScale,
                To = toScale,
                Duration = duration,
                EasingFunction = easing
            };
            Storyboard.SetTarget(scaleXAnimation, CharacterCreateDialogCardScale);
            Storyboard.SetTargetProperty(scaleXAnimation, nameof(ScaleTransform.ScaleX));
            storyboard.Children.Add(scaleXAnimation);

            var scaleYAnimation = new DoubleAnimation
            {
                From = fromScale,
                To = toScale,
                Duration = duration,
                EasingFunction = easing
            };
            Storyboard.SetTarget(scaleYAnimation, CharacterCreateDialogCardScale);
            Storyboard.SetTargetProperty(scaleYAnimation, nameof(ScaleTransform.ScaleY));
            storyboard.Children.Add(scaleYAnimation);

            storyboard.Completed += (_, _) =>
            {
                CharacterCreateDialogHost.Opacity = toOpacity;
                CharacterCreateDialogCardScale.ScaleX = toScale;
                CharacterCreateDialogCardScale.ScaleY = toScale;
                completion.TrySetResult();
            };
            storyboard.Begin();
            return completion.Task;
        }

        private void CharacterCreateConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            var characterName = CharacterCreateNameTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(characterName))
            {
                CharacterCreateErrorInfoBar.Message = "请先填写角色名字。";
                CharacterCreateErrorInfoBar.IsOpen = true;
                CharacterCreateNameTextBox.Focus(FocusState.Programmatic);
                return;
            }

            CompleteCharacterCreateDialog(characterName);
        }

        private void CharacterCreateCancelButton_Click(object sender, RoutedEventArgs e)
        {
            CompleteCharacterCreateDialog(null);
        }

        private void CharacterCreateDialogHost_Tapped(object sender, TappedRoutedEventArgs e)
        {
            CompleteCharacterCreateDialog(null);
            e.Handled = true;
        }

        private void CharacterCreateDialogHost_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            CompleteCharacterCreateDialog(null);
            e.Handled = true;
        }

        private void CharacterCreateDialogHost_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                CompleteCharacterCreateDialog(null);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Enter)
            {
                CharacterCreateConfirmButton_Click(sender, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private void CharacterCreateDialogCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
        }

        private async void RefreshCharactersButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadCharacterCardsAsync();
        }

        private async void ContinueLastCharacterButton_Click(object sender, RoutedEventArgs e)
        {
            AppendLog(LogKind.User, $"ContinueLastCharacter clicked. LastEdited={CharacterDesk.LastEditedCharacter?.Code ?? "<null>"} Current={CharacterDesk.CurrentCharacter?.Code ?? "<null>"} LastModule={Settings.LastEditedModuleTag ?? "<null>"}");
            try
            {
                if (CharacterDesk.LastEditedCharacter is not null)
                {
                    await CharacterDesk.SetCurrentCharacterAsync(CharacterDesk.LastEditedCharacter);
                    AppendLog(LogKind.User, $"ContinueLastCharacter selected {CharacterDesk.LastEditedCharacter.Code}.");
                }

                PersistCurrentCharacterSelection();
                var lastEditedModuleTag = GetLastEditedModuleTag();
                ShowLastEditedPage(lastEditedModuleTag);
                if (string.Equals(lastEditedModuleTag, ToolboxModuleKey.ActionFrames.ToString(), StringComparison.Ordinal)
                    && CharacterDesk.CanOpenCurrentDraft)
                {
                    await CharacterDesk.OpenCurrentCharacterDraftAsync();
                    TryPlayPageEntrance(ActionFramesPage);
                    PersistCurrentCharacterSelection();
                    AppendLog(LogKind.User, $"ContinueLastCharacter opened St1 draft. Character={CharacterDesk.CurrentCharacter?.Code ?? "<null>"}");
                }

                AppendLog(LogKind.User, $"ContinueLastCharacter navigated. Current={CharacterDesk.CurrentCharacter?.Code ?? "<null>"} Module={Settings.LastEditedModuleTag ?? "<null>"}");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "继续上次编辑失败", FormatExceptionForTip(ex));
                AppendLog(LogKind.Error, "ContinueLastCharacter failed.", ex);
            }
        }

        private void CharacterCardButton_Click(object sender, RoutedEventArgs e)
        {
            var character = ResolveCharacterFromEvent(sender, e.OriginalSource);
            if (character is null)
            {
                return;
            }

            ShowCharacterDetail(character);
            AppendLog(LogKind.User, $"打开角色详情：{character.Name} / {character.Code}");
        }

        private void ShowCharacterDetail(CharacterCard character)
        {
            _characterDetailCharacter = character;
            CharacterDetailCard.DataContext = character;
            CharacterDetailHost.Visibility = Visibility.Visible;
            CharacterDetailScrollViewer.ChangeView(null, 0, null, disableAnimation: true);
            CharacterDetailHost.Focus(FocusState.Programmatic);
        }

        private void HideCharacterDetail()
        {
            CharacterDetailHost.Visibility = Visibility.Collapsed;
            CharacterDetailCard.DataContext = null;
            _characterDetailCharacter = null;
        }

        private void CharacterDetailCloseButton_Click(object sender, RoutedEventArgs e)
        {
            HideCharacterDetail();
        }

        private void CharacterDetailHost_Tapped(object sender, TappedRoutedEventArgs e)
        {
            HideCharacterDetail();
            e.Handled = true;
        }

        private void CharacterDetailHost_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            HideCharacterDetail();
            e.Handled = true;
        }

        private void CharacterDetailHost_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                HideCharacterDetail();
                e.Handled = true;
            }
        }

        private void CharacterDetailCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
        }

        private void CharacterDetailCard_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            e.Handled = true;
        }

        private async void CharacterDetailContinueButton_Click(object sender, RoutedEventArgs e)
        {
            if (_characterDetailCharacter is not { } character)
            {
                return;
            }

            if (character.IsCompleted)
            {
                try
                {
                    character = await CharacterDesk.ReopenCompletedCharacterAsync(character);
                    _characterDetailCharacter = character;
                    PersistCurrentCharacterSelection();
                    AppendLog(LogKind.User, $"恢复角色草稿：{character.Name} / {character.Code} -> {character.FolderPath}");
                }
                catch (Exception ex)
                {
                    ShowFloatingTip(InfoBarSeverity.Error, "恢复草稿失败", ex.Message);
                    AppendLog(LogKind.Error, "恢复已完成角色为草稿失败。", ex);
                    return;
                }
            }
            else
            {
                try
                {
                    await CharacterDesk.SetCurrentCharacterAsync(character);
                    PersistCurrentCharacterSelection();
                    AppendLog(LogKind.User, $"继续编辑角色：{character.Name} / {character.Code}");
                }
                catch (Exception ex)
                {
                    ShowFloatingTip(InfoBarSeverity.Error, "选择角色失败", ex.Message);
                    AppendLog(LogKind.Error, "继续编辑时选择角色失败。", ex);
                    return;
                }
            }

            HideCharacterDetail();
            var lastEditedModuleTag = GetLastEditedModuleTag();
            if (string.Equals(lastEditedModuleTag, ToolboxModuleKey.CharacterDesk.ToString(), StringComparison.Ordinal) ||
                string.Equals(lastEditedModuleTag, ToolboxModuleKey.Settings.ToString(), StringComparison.Ordinal))
            {
                ShowSt2MaterialPage();
                return;
            }

            ShowLastEditedPage(lastEditedModuleTag);
        }

        private async void CharacterDetailExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_characterDetailCharacter is not { } character)
            {
                return;
            }

            var defaultExportRoot = CharacterDesk.GetDefaultExportRootPath(Settings.ProjectRootPath);
            var exportRoot = await ShowCharacterExportLocationDialogAsync(character, defaultExportRoot);
            if (string.IsNullOrWhiteSpace(exportRoot))
            {
                return;
            }

            var targetPath = Path.Combine(exportRoot, character.Code);
            var overwrite = Directory.Exists(targetPath);
            if (overwrite && !await ConfirmCharacterExportOverwriteAsync(character, targetPath))
            {
                return;
            }

            try
            {
                var exportedPath = await ShowCharacterExportProgressAsync(character, exportRoot, overwrite);
                ShowFloatingTip(InfoBarSeverity.Success, "角色已导出", exportedPath);
                AppendLog(LogKind.User, $"导出完整角色文件夹：{character.Name} / {character.Code} -> {exportedPath}");
            }
            catch (OperationCanceledException)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "导出已取消", character.Name);
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "导出角色失败", ex.Message);
                AppendLog(LogKind.Error, "导出完整角色文件夹失败。", ex);
            }
        }

        private async Task<string?> ShowCharacterExportLocationDialogAsync(CharacterCard character, string defaultExportRoot)
        {
            var pathTextBox = new TextBox
            {
                Text = defaultExportRoot,
                PlaceholderText = "选择或输入导出位置",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var browseButton = new Button
            {
                Content = "浏览...",
                HorizontalAlignment = HorizontalAlignment.Left
            };
            browseButton.Click += async (_, _) =>
            {
                var picker = new FolderPicker
                {
                    SuggestedStartLocation = PickerLocationId.ComputerFolder
                };
                picker.FileTypeFilter.Add("*");
                InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
                var folder = await picker.PickSingleFolderAsync();
                if (folder is not null)
                {
                    pathTextBox.Text = folder.Path;
                }
            };

            var content = new StackPanel
            {
                Width = 520,
                Spacing = 10
            };
            content.Children.Add(new TextBlock
            {
                Text = $"将完整角色文件夹导出为“{character.Code}”。",
                TextWrapping = TextWrapping.Wrap
            });
            content.Children.Add(pathTextBox);
            content.Children.Add(browseButton);

            var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "选择导出位置",
                content,
                PrimaryButtonText: "导出",
                CloseButtonText: string.Empty,
                SecondaryButtonText: "取消",
                DefaultButton: ContentDialogButton.Primary,
                PrimaryButtonStyle: (Style)Application.Current.Resources["DialogAccentButtonStyle"]));
            if (result != DialogResultKind.Primary || string.IsNullOrWhiteSpace(pathTextBox.Text))
            {
                return null;
            }

            try
            {
                return Path.GetFullPath(pathTextBox.Text.Trim());
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "导出位置无效", ex.Message);
                return null;
            }
        }

        private async Task<bool> ConfirmCharacterExportOverwriteAsync(CharacterCard character, string targetPath)
        {
            var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "覆盖已有导出",
                new TextBlock
                {
                    Text = $"导出位置中已存在 {character.Code}。继续会用当前角色的完整文件夹替换它。\n\n{targetPath}",
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

        private void CharacterDetailOpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (_characterDetailCharacter is not { } character)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(character.FolderPath);
                Process.Start(new ProcessStartInfo
                {
                    FileName = character.FolderPath,
                    UseShellExecute = true
                });
                AppendLog(LogKind.User, $"打开角色目录：{character.FolderPath}");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "角色目录打开失败", ex.Message);
                AppendLog(LogKind.Error, "角色目录打开失败。", ex);
            }
        }

        private async void CharacterDetailUnrealSyncButton_Click(object sender, RoutedEventArgs e)
        {
            if (_characterDetailCharacter is not { } character)
            {
                return;
            }

            try
            {
                await CharacterDesk.SetCurrentCharacterAsync(character);
                PersistCurrentCharacterSelection();
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "选择角色失败", ex.Message);
                AppendLog(LogKind.Error, "前往虚幻同步台时选择角色失败。", ex);
                return;
            }

            HideCharacterDetail();
            ShowUnrealProjectSyncPage();
        }

        private void CharacterCardButton_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                var character = ResolveCharacterFromEvent(sender, e.OriginalSource);
                if (character is null)
                {
                    return;
                }

                ShowCharacterCardMenu(element, character, e);
                e.Handled = true;
            }
        }

        private async void PortraitEntryCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
            await OpenDraftCharacterCardAsync(ResolveCharacterFromEvent(sender, e.OriginalSource));
        }

        private async Task OpenDraftCharacterCardAsync(CharacterCard? character)
        {
            if (_isOpeningDraftCharacterCard)
            {
                return;
            }

            _isOpeningDraftCharacterCard = true;
            try
            {
                if (character is null)
                {
                    ShowFloatingTip(InfoBarSeverity.Warning, "草稿卡打开失败", "没有识别到被点击的角色卡。");
                    AppendLog(LogKind.Warning, "DraftCard open failed. CharacterFromEvent=<null>.");
                    return;
                }

                AppendLog(LogKind.User, $"DraftCard tapped. Character={character.Code} Name={character.EffectiveDisplayName} Path={character.FolderPath}");
                await RunDraftOpenStepAsync(
                    "SetCurrentCharacter",
                    () => CharacterDesk.SetCurrentCharacterAsync(character));
                AppendLog(LogKind.User, $"DraftCard SetCurrentCharacter completed. Current={CharacterDesk.CurrentCharacter?.Code ?? "<null>"}");
                PersistCurrentCharacterSelection();

                if (CharacterDesk.CurrentCharacter is null)
                {
                    ShowTextGuideOverlay("当前未选择角色", "当前未选择角色，请点击草稿卡进行选择。");
                    return;
                }

                AppendLog(LogKind.User, $"DraftCard OpenCurrentCharacterDraft starting. Character={CharacterDesk.CurrentCharacter.Code}");
                await RunDraftOpenStepAsync(
                    "OpenCurrentCharacterDraft",
                    () => CharacterDesk.OpenCurrentCharacterDraftAsync());
                AppendLog(LogKind.User, $"DraftCard OpenCurrentCharacterDraft completed. IsDraftOpen={CharacterDesk.IsDraftOpen}");
                TryPlayPageEntrance(ActionFramesPage);
                MarkLastEditedModule("ActionFrames");
                PersistCurrentCharacterSelection();
                ShowFloatingTip(InfoBarSeverity.Success, "已进入草稿", CharacterDesk.CurrentCharacterName);
                AppendLog(LogKind.User, $"打开 St1 草稿：{CharacterDesk.CurrentCharacterName}");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "草稿卡打开失败", FormatExceptionForTip(ex));
                AppendLog(LogKind.Error, "DraftCard open failed.", ex);
            }
            finally
            {
                _isOpeningDraftCharacterCard = false;
            }
        }

        private void TryPlayPageEntrance(FrameworkElement page)
        {
            try
            {
                PlayPageEntrance(page);
            }
            catch (Exception ex)
            {
                AppendLog(LogKind.Warning, $"页面入场动画失败，但草稿已经打开：{FormatExceptionForTip(ex)}");
            }
        }

        private async Task RunDraftOpenStepAsync(string stepName, Func<Task> action)
        {
            try
            {
                AppendLog(LogKind.User, $"DraftCard step begin: {stepName}");
                await action();
                AppendLog(LogKind.User, $"DraftCard step end: {stepName}");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"DraftCard step failed: {stepName}", ex);
            }
        }

        private static string FormatExceptionForTip(Exception ex)
        {
            var message = string.IsNullOrWhiteSpace(ex.Message)
                ? ex.GetType().Name
                : ex.Message;
            return ex.InnerException is null || string.IsNullOrWhiteSpace(ex.InnerException.Message)
                ? message
                : $"{message} / {ex.InnerException.Message}";
        }

        private void RefreshVisibleCharacterPageAfterSelection()
        {
            if (LineArtPage.Visibility == Visibility.Visible)
            {
                _ = RefreshBaseMaterialsWithFeedbackAsync();
                return;
            }

            if (UnrealSyncPage.Visibility == Visibility.Visible)
            {
                _ = RefreshCharacterInfoWithFeedbackAsync();
                return;
            }

            if (SkillsPage.Visibility == Visibility.Visible)
            {
                _ = RefreshSkillsWithFeedbackAsync();
                return;
            }

            if (SequenceFramesPage.Visibility == Visibility.Visible)
            {
                _ = RefreshSequenceFramesWithFeedbackAsync();
                return;
            }

            if (BuffsPage.Visibility == Visibility.Visible)
            {
                _ = RefreshBuffsWithFeedbackAsync();
                RefreshProductionStatusWithFeedback();
            }
        }

        private void PortraitEntryButton_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                var character = ResolveCharacterFromEvent(sender, e.OriginalSource) ?? CharacterDesk.CurrentCharacter;
                if (character is null)
                {
                    return;
                }

                ShowCharacterCardMenu(element, character, e);
                e.Handled = true;
            }
        }

        private static CharacterCard? ResolveCharacterFromEvent(object sender, object originalSource)
        {
            if (sender is Button { CommandParameter: CharacterCard commandCharacter })
            {
                return commandCharacter;
            }

            if (sender is FrameworkElement { DataContext: CharacterCard senderCharacter })
            {
                return senderCharacter;
            }

            if (originalSource is DependencyObject source)
            {
                for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
                {
                    if (current is Button { CommandParameter: CharacterCard buttonCharacter })
                    {
                        return buttonCharacter;
                    }

                    if (current is FrameworkElement { DataContext: CharacterCard dataCharacter })
                    {
                        return dataCharacter;
                    }
                }
            }

            return null;
        }

        private void ShowCharacterCardMenu(FrameworkElement target, CharacterCard character, RightTappedRoutedEventArgs args)
        {
            var menu = new MenuFlyout();
            var backupItem = new MenuFlyoutItem
            {
                Text = "手动备份",
                CommandParameter = character
            };
            backupItem.Click += BackupCharacterMenuItem_Click;

            var restoreItem = new MenuFlyoutItem
            {
                Text = "还原",
                CommandParameter = character
            };
            restoreItem.Click += RestoreCharacterMenuItem_Click;

            var deleteItem = new MenuFlyoutItem
            {
                Text = "删除",
                CommandParameter = character
            };
            deleteItem.Click += DeleteCharacterMenuItem_Click;

            menu.Items.Add(backupItem);
            menu.Items.Add(restoreItem);
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(deleteItem);
            ShowMenuToRightOfPointer(menu, target, args);
        }

        private static void ShowMenuToRightOfPointer(MenuFlyout menu, FrameworkElement target, RightTappedRoutedEventArgs args)
        {
            menu.ShowAt(target, new FlyoutShowOptions
            {
                Position = args.GetPosition(target),
                Placement = FlyoutPlacementMode.RightEdgeAlignedTop,
                ShowMode = FlyoutShowMode.Standard
            });
        }

        private async void BackupCharacterMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { CommandParameter: CharacterCard character })
            {
                return;
            }

            await BackupCharacterAsync(character);
        }

        private async Task BackupCharacterAsync(CharacterCard character)
        {
            var note = await ShowCharacterBackupNoteDialogAsync(character);
            if (note is null)
            {
                return;
            }

            try
            {
                var backup = await ShowCharacterBackupProgressAsync(character, note);
                ShowFloatingTip(InfoBarSeverity.Success, "角色卡已备份", backup.DisplayName);
                AppendLog(LogKind.User, $"备份角色卡：{character.Name} / {character.Code} -> {backup.Path}");
            }
            catch (OperationCanceledException)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "备份已取消", character.Name);
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "角色卡备份失败", ex.Message);
                AppendLog(LogKind.Error, "角色卡备份失败。", ex);
            }
        }

        private async void RestoreCharacterMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { CommandParameter: CharacterCard character })
            {
                return;
            }

            try
            {
                var backups = await CharacterDesk.LoadCharacterBackupsAsync(character);
                var backup = await ShowCharacterRestoreDialogAsync(character, backups);
                if (backup is null)
                {
                    return;
                }

                var restored = await ShowCharacterRestoreProgressAsync(character, backup);
                PersistCurrentCharacterSelection();
                RefreshProductionStatusWithFeedback();
                ShowFloatingTip(InfoBarSeverity.Success, "角色卡已还原", restored.EffectiveDisplayName);
                AppendLog(LogKind.User, $"还原角色卡：{character.Name} / {character.Code} <- {backup.Path}");
            }
            catch (OperationCanceledException)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "还原已取消", character.Name);
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "角色卡还原失败", ex.Message);
                AppendLog(LogKind.Error, "角色卡还原失败。", ex);
            }
        }

        private async void DeleteCharacterMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { CommandParameter: CharacterCard character })
            {
                return;
            }

            var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "删除角色卡",
                new TextBlock
                {
                    Text = $"确定删除角色卡 {character.Name} / {character.Code} 吗？这会删除整个角色文件夹，建议先备份。",
                    TextWrapping = TextWrapping.Wrap,
                    Width = 460
                },
                PrimaryButtonText: "删除",
                CloseButtonText: string.Empty,
                SecondaryButtonText: "取消",
                DefaultButton: ContentDialogButton.Primary,
                PrimaryButtonStyle: (Style)Application.Current.Resources["DialogAccentButtonStyle"]));
            if (result != DialogResultKind.Primary)
            {
                return;
            }

            try
            {
                await CharacterDesk.DeleteCharacterAsync(character);
                PersistCurrentCharacterSelection();
                RefreshProductionStatusWithFeedback();
                ShowFloatingTip(InfoBarSeverity.Success, "角色卡已删除", $"{character.Name} / {character.Code}");
                AppendLog(LogKind.User, $"删除角色卡：{character.Name} / {character.Code}");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "角色卡删除失败", ex.Message);
                AppendLog(LogKind.Error, "角色卡删除失败。", ex);
            }
        }

        private async Task<string?> ShowCharacterBackupNoteDialogAsync(CharacterCard character)
        {
            var noteBox = new TextBox
            {
                Header = "备注（可留空）",
                PlaceholderText = "例如：完成 St2 前、改技能前",
                MaxLength = 80,
                Width = 420
            };
            var panel = new StackPanel { Spacing = 12, Width = 440 };
            panel.Children.Add(new TextBlock
            {
                Text = $"{character.Name} / {character.Code}",
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(noteBox);

            var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "备份角色卡",
                panel,
                PrimaryButtonText: "备份",
                CloseButtonText: "取消",
                ConfigureDialog: dialog =>
                {
                    dialog.MinWidth = 520;
                    dialog.MaxWidth = 520;
                }));

            return result == DialogResultKind.Primary ? NormalizeBackupNote(noteBox.Text) : null;
        }

        private async Task<CharacterBackupEntry?> ShowCharacterRestoreDialogAsync(
            CharacterCard character,
            IReadOnlyList<CharacterBackupEntry> backups)
        {
            if (backups.Count == 0)
            {
                await _dialogService.ShowContentAsync(new ContentDialogRequest(
                    "还原角色卡",
                    new TextBlock
                    {
                        Text = $"{character.Name} / {character.Code} 还没有备份。请先右键角色卡执行手动备份。",
                        TextWrapping = TextWrapping.Wrap,
                        Width = 460
                    },
                    PrimaryButtonText: string.Empty,
                    CloseButtonText: "关闭"));
                return null;
            }

            var visibleBackups = backups
                .OrderBy(backup => backup.IsAutomatic)
                .ThenByDescending(backup => backup.CreatedAt)
                .Take(6)
                .ToList();
            var backupListView = new ListView
            {
                SelectionMode = ListViewSelectionMode.Single,
                ItemsSource = visibleBackups,
                SelectedIndex = 0,
                MaxHeight = 340
            };
            backupListView.ItemTemplate = CreateCharacterBackupTemplate();

            var panel = new StackPanel { Spacing = 14, Width = 540 };
            panel.Children.Add(new TextBlock
            {
                Text = $"选择要还原的备份：{character.Name} / {character.Code}",
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(new InfoBar
            {
                IsOpen = true,
                Severity = InfoBarSeverity.Informational,
                Title = "还原前会自动备份当前状态",
                Message = "自动备份最多保留 3 个，手动备份最多保留 3 个。"
            });
            panel.Children.Add(backupListView);

            var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "还原角色卡",
                panel,
                PrimaryButtonText: "还原",
                CloseButtonText: "取消",
                PrimaryButtonStyle: (Style)Application.Current.Resources["DialogAccentButtonStyle"],
                ConfigureDialog: dialog =>
                {
                    dialog.MinWidth = 620;
                    dialog.MaxWidth = 620;
                }));

            return result == DialogResultKind.Primary
                ? backupListView.SelectedItem as CharacterBackupEntry
                : null;
        }

        private static DataTemplate CreateCharacterBackupTemplate()
        {
            const string xaml = """
                <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                    <Grid Padding="10,8" RowSpacing="4">
                        <Grid.RowDefinitions>
                            <RowDefinition Height="Auto" />
                            <RowDefinition Height="Auto" />
                        </Grid.RowDefinitions>
                        <TextBlock
                            FontSize="15"
                            FontWeight="SemiBold"
                            Text="{Binding DisplayName}"
                            TextTrimming="CharacterEllipsis" />
                        <TextBlock
                            Grid.Row="1"
                            FontSize="12"
                            Opacity="0.72"
                            Text="{Binding Path}"
                            TextTrimming="CharacterEllipsis" />
                    </Grid>
                </DataTemplate>
                """;
            return (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml);
        }

        private async Task<CharacterBackupEntry> ShowCharacterBackupProgressAsync(CharacterCard character, string note)
        {
            ShowGlobalProgress("备份角色卡", $"{character.Name} / {character.Code}");
            var progress = new Progress<CharacterBackupProgress>(update =>
            {
                var byteText = update.TotalBytes > 0
                    ? $"{FormatCharacterBackupSize(update.CompletedBytes)} / {FormatCharacterBackupSize(update.TotalBytes)}"
                    : "统计大小中";
                var fileText = update.TotalFiles > 0
                    ? $"{Math.Min(update.CompletedFiles + 1, update.TotalFiles)} / {update.TotalFiles} 个文件"
                    : "扫描文件中";
                var detail = update.CurrentRelativePath is null
                    ? $"{fileText}，{byteText}"
                    : $"{fileText}，{byteText}\n{update.CurrentRelativePath}";
                UpdateGlobalProgress(update.Message, update.Percent, detail, update.Percent <= 0);
            });

            try
            {
                var result = await CharacterDesk.BackupCharacterAsync(character, note, progress, GetGlobalProgressCancellationToken());
                CompleteGlobalProgress("备份完成", result.DisplayName);
                return result;
            }
            catch (OperationCanceledException)
            {
                CompleteGlobalProgress("已取消", "当前备份操作已停止。");
                throw;
            }
            catch
            {
                CompleteGlobalProgress("备份失败", "角色卡备份没有完成。");
                throw;
            }
            finally
            {
                await HideGlobalProgressAfterDelayAsync();
            }
        }

        private async Task<string> ShowCharacterExportProgressAsync(
            CharacterCard character,
            string exportRootPath,
            bool overwrite)
        {
            ShowGlobalProgress("导出角色", $"{character.Name} / {character.Code}");
            var progress = new Progress<CharacterBackupProgress>(update =>
            {
                var byteText = update.TotalBytes > 0
                    ? $"{FormatCharacterBackupSize(update.CompletedBytes)} / {FormatCharacterBackupSize(update.TotalBytes)}"
                    : "统计大小中";
                var fileText = update.TotalFiles > 0
                    ? $"{Math.Min(update.CompletedFiles, update.TotalFiles)} / {update.TotalFiles} 个文件"
                    : "扫描文件中";
                var detail = update.CurrentRelativePath is null
                    ? $"{fileText}，{byteText}\n{exportRootPath}"
                    : $"{fileText}，{byteText}\n{update.CurrentRelativePath}";
                UpdateGlobalProgress(update.Message, update.Percent, detail, update.Percent <= 0);
            });

            try
            {
                var result = await CharacterDesk.ExportCharacterFolderAsync(
                    character,
                    exportRootPath,
                    overwrite,
                    progress,
                    GetGlobalProgressCancellationToken());
                CompleteGlobalProgress("导出完成", result);
                return result;
            }
            catch (OperationCanceledException)
            {
                CompleteGlobalProgress("已取消", "当前导出操作已停止，原角色文件夹未改变。");
                throw;
            }
            catch
            {
                CompleteGlobalProgress("导出失败", "角色文件夹未完整导出，原角色文件夹未改变。");
                throw;
            }
            finally
            {
                await HideGlobalProgressAfterDelayAsync();
            }
        }

        private async Task<CharacterCard> ShowCharacterRestoreProgressAsync(CharacterCard character, CharacterBackupEntry backup)
        {
            ShowGlobalProgress("还原角色卡", $"{character.Name} / {character.Code}");
            var progress = new Progress<CharacterBackupProgress>(update =>
            {
                var detail = update.CurrentRelativePath is null
                    ? backup.DisplayName
                    : $"{backup.DisplayName}\n{update.CurrentRelativePath}";
                UpdateGlobalProgress(update.Message, update.Percent, detail, update.Percent <= 0);
            });

            try
            {
                var result = await CharacterDesk.RestoreCharacterBackupAsync(character, backup, progress, GetGlobalProgressCancellationToken());
                CompleteGlobalProgress("还原完成", result.StatusDisplayText);
                return result;
            }
            catch (OperationCanceledException)
            {
                CompleteGlobalProgress("已取消", "当前还原操作已停止。");
                throw;
            }
            catch
            {
                CompleteGlobalProgress("还原失败", "角色卡还原没有完成。");
                throw;
            }
            finally
            {
                await HideGlobalProgressAfterDelayAsync();
            }
        }

        private static string NormalizeBackupNote(string? note)
        {
            return string.Join(" ", (note ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
        }

        private static string FormatCharacterBackupSize(long byteCount)
        {
            string[] units = ["B", "KB", "MB", "GB"];
            var size = (double)byteCount;
            var unitIndex = 0;
            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            return unitIndex == 0 ? $"{byteCount} {units[unitIndex]}" : $"{size:0.##} {units[unitIndex]}";
        }

        private void BackToPortraitEntryButton_Click(object sender, RoutedEventArgs e)
        {
            ExitDraftToPortraitEntry();
        }

        private void ExitDraftToPortraitEntry()
        {
            CharacterDesk.CloseDraftView();
            PlayPageEntrance(ActionFramesPage);
            ShowFloatingTip(InfoBarSeverity.Success, "已回到立绘入口", string.Empty);
        }

        private async Task ShowDraftOverlayAsync()
        {
            if (CharacterDesk.CurrentCharacter is null)
            {
                ShowTextGuideOverlay("当前未选择角色", "当前未选择角色，请点击草稿卡进行选择。");
                return;
            }

            var draftText = await CharacterDesk.LoadCurrentCharacterDraftTextAsync();
            PersistCurrentCharacterSelection();

            ShowTextGuideOverlay(
                $"{CharacterDesk.CurrentCharacterName} - 设计草稿",
                string.IsNullOrWhiteSpace(draftText)
                ? "当前角色还没有草稿内容。"
                : draftText);
        }

        private void ShowShortcutGuideOverlay()
        {
            if (SettingsPage.Visibility == Visibility.Visible)
            {
                ShowTextGuideOverlay(
                    "设置页 Tips 合集",
                    """
                    整体项目位置
                    这里保存所有角色数据、素材和工具箱 JSON 数据。选择位置时请选择父目录，程序会自动创建 CrossingVoidZDProject 文件夹。

                    辅助显示
                    可以控制底部工作区路径和输出日志是否显示。设置会立即保存。

                    输出日志
                    用于记录用户操作、提示和错误。关闭 log 功能后，底部日志面板会隐藏，并停止写入新日志。

                    撤回设置
                    设置页按 Ctrl+Z 可撤回最近一次设置开关修改，例如误关日志或工作区路径。不用于目录迁移、文件导入、删除、同步等素材操作。

                    全局快捷键
                    F1：设置页 Tips 合集
                    F2：当前角色草稿本
                    F3：技能数值倍率规范
                    F4：设置页填写法则
                    Ctrl+Z：撤回最近一次可撤销操作
                    """);
                return;
            }

            ShowTextGuideOverlay(
                "快捷键大全",
                """
                F1：快捷键大全
                F2：当前角色草稿本
                F3：技能数值倍率规范
                F4：当前页面填写法则
                Enter：在部分输入框中提交并取消聚焦
                Ctrl+Z：撤回最近一次可撤销操作

                St1 角色台
                继续：回到上一次编辑角色和页面。

                St2 基础素材
                导入素材：选择图片后按规格裁剪并保存到角色文件夹。

                St3 角色信息
                形态上限会控制 St4 核心技能数量和 St5 基础/技能序列帧板块数量。

                St4 技能
                左键技能图标选择 St2 技能图标，右键技能图标展开或收起详情。

                St5 序列帧
                播放按钮控制右侧预览播放/暂停，左右按钮逐帧查看；双击预览区域归位。
                """);
        }

        private void ShowTextGuideOverlay(string title, string text)
        {
            CloseTextGuideOverlayWithoutAnimation();
            DraftOverlayTitleText.Text = title;
            DraftOverlayTextBlock.Text = text;
            DraftOverlayHost.Visibility = Visibility.Visible;
            AnimateReferenceOverlay(DraftOverlayHost, DraftOverlayCardScale, show: true);
            DraftOverlayHost.Focus(FocusState.Programmatic);
        }

        private void CloseTextGuideOverlayWithoutAnimation()
        {
            if (DraftOverlayHost.Visibility == Visibility.Visible)
            {
                DraftOverlayHost.Visibility = Visibility.Collapsed;
                DraftOverlayTextBlock.Text = string.Empty;
            }

            if (SkillValueGuideOverlayHost.Visibility == Visibility.Visible)
            {
                SkillValueGuideOverlayHost.Visibility = Visibility.Collapsed;
                SkillValueGuideTextBlock.Text = string.Empty;
            }
        }

        private async void HideDraftOverlay()
        {
            if (DraftOverlayHost.Visibility != Visibility.Visible)
            {
                return;
            }

            await AnimateReferenceOverlayAsync(DraftOverlayHost, DraftOverlayCardScale, show: false);
            DraftOverlayHost.Visibility = Visibility.Collapsed;
            DraftOverlayTextBlock.Text = string.Empty;
        }

        private void DraftOverlayCloseButton_Click(object sender, RoutedEventArgs e)
        {
            HideDraftOverlay();
        }

        private void DraftOverlayHost_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
        }

        private void DraftOverlayHost_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            HideDraftOverlay();
            e.Handled = true;
        }

        private void DraftOverlayHost_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                HideDraftOverlay();
                e.Handled = true;
            }
        }

        private void DraftOverlayCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
        }

        private void ShowSkillValueGuideOverlay()
        {
            ShowTextGuideOverlay(
                "技能数值倍率规范",
                """
                2D数值规范
                个人攻击力1500封顶，标准900~1200

                倍率规范（主C偏向高，T偏低，辅助中高，奶妈中低）
                奶妈就把治疗倍率调成负的，顺带砍一半左右倍率，角色技能介绍也要有判定
                技能索引的0号固定是1，平均数保底是1，让连携技吃到

                这些倍率是异能和物理加起来的，要记得算

                功能性，耗费少技能，启动技能，1到2人，回血1/3
                0级：0.3~0.6   1级：0.8~1.4   2级：1.3~2.3   3级：2.2~2.8   4级：3~3.8

                群体AOE，中等出伤，功能性
                0级：0.4~0.8   1级：0.8~2   2级：1.6~3   3级：2.8~3.8  4级：3.2~4.8

                群体分摊，高出伤，单体大招（技能和大招至少差1,1,2,4,5）
                0级：1.5~2.6   1级：3.5~6   2级：7.4~9.8   3级：10~13   4级：11.4~16

                单体技能，单体连携技高出伤
                0级：0.8~2   1级：1.6~3   2级：2.8~4.5   3级：4.2~7   4级：6.4~9

                组合技，高功能性，普通sub
                0级：0.2~0.5   1级：0.5~1.2   2级：1~1.8   3级：1.8~2.6   4级：2.4~3

                组合大招，sub大招，群体大招
                0级：1~3   1级：2~5   2级：4~8   3级：7~10  4级：9~14
                """);
        }

        private void ShowCurrentPageRulesOverlay()
        {
            ShowTextGuideOverlay(GetCurrentPageRulesTitle(), GetCurrentPageRulesText());
        }

        private string GetCurrentPageRulesTitle()
        {
            if (ActionFramesPage.Visibility == Visibility.Visible)
            {
                return "St1 填写法则";
            }

            if (LineArtPage.Visibility == Visibility.Visible)
            {
                return "St2 填写法则";
            }

            if (UnrealSyncPage.Visibility == Visibility.Visible)
            {
                return "St3 填写法则";
            }

            if (SkillsPage.Visibility == Visibility.Visible)
            {
                return "St4 填写法则";
            }

            if (SequenceFramesPage.Visibility == Visibility.Visible)
            {
                return "St5 填写法则";
            }

            if (UnrealProjectSyncPage.Visibility == Visibility.Visible)
            {
                return "虚幻同步台填写法则";
            }

            if (BuffsPage.Visibility == Visibility.Visible)
            {
                return "St6 填写法则";
            }

            if (SettingsPage.Visibility == Visibility.Visible)
            {
                return "设置页填写法则";
            }

            return "角色台填写法则";
        }

        private string GetCurrentPageRulesText()
        {
            if (ActionFramesPage.Visibility == Visibility.Visible)
            {
                return """
                    St1 角色台 / 设计入口
                    创建角色后先确认名称和英文代号，草稿用于记录设计理念、动作想法和临时备注。

                    参考图
                    参考图只服务于设计阶段，可以导入多张图片辅助设定，不等同于正式素材。

                    继续编辑
                    继续按钮会回到最近一次真实编辑的角色和页面。
                    """;
            }

            if (LineArtPage.Visibility == Visibility.Visible)
            {
                return """
                    St2 基础素材
                    按每个素材板块导入对应图片，工具箱会裁剪并保存到当前角色文件夹。

                    技能图标
                    St4 技能图标只能从这里已经准备好的技能图标中选择。

                    素材状态
                    通告栏会提示缺失或不合规素材，修完后状态会恢复正常。
                    """;
            }

            if (UnrealSyncPage.Visibility == Visibility.Visible)
            {
                return """
                    St3 角色信息
                    英文代号用于文件夹和数据索引；名称用于工具箱显示；介绍用于角色说明。

                    形态上限
                    控制核心技能和序列帧形态数量。它会影响 St4 的一技能、二技能、终结技、护援技数量，也会影响 St5 的基础/技能板块数量。

                    数值
                    速度、生命值、攻击力、防御和暴击相关字段应按角色定位填写。数值为 0 时通告栏会提醒确认。

                    被动技能和关键词
                    关键词用于快速标记角色定位；被动技能填写角色常驻效果。
                    """;
            }

            if (SkillsPage.Visibility == Visibility.Visible)
            {
                return """
                    St4 技能
                    技能默认收起。左键技能图标从 St2 技能图标中选择图标，右键技能图标展开或收起详情。

                    定位名称
                    工具箱内用于识别这一形态的短名称。

                    技能真名
                    游戏内显示的正式技能名称。

                    Pt消耗
                    填写数值；超过 100 时按升华技处理。

                    攻击容量
                    填写该技能占用或提供的攻击容量。

                    介绍
                    填写技能描述、效果和触发条件。

                    技能状态
                    空表示未指定；常态、禁用、舍弃用于控制技能可用状态。

                    守备
                    空表示未指定；防御、反击、闪避用于标记守备类型。

                    守备数值
                    填写守备相关数值。

                    自动优先级
                    填写 0-100，数值越高自动战斗越优先。

                    技能倍率
                    物理倍率和异能倍率按等级填写；具体倍率范围可按 F3 查看。
                    """;
            }

            if (SequenceFramesPage.Visibility == Visibility.Visible)
            {
                return """
                    St5 序列帧
                    这里只管理当前角色动作序列帧。基础板块和技能板块跟随 St3 形态上限生成，连携技板块跟随 St4 连携技数量生成。

                    导入
                    每个动作板块单独导入图片序列，保存到对应动作文件夹。

                    序列合集
                    点击三张预览图或 ... 打开合集，只用于查看、删除、复制和排序，不会触发右侧预览播放。

                    预览器
                    播放按钮控制播放/暂停；左右按钮逐帧查看；滚轮缩放，拖动查看，双击归位。

                    帧速率
                    每个序列都有自己的帧速率，默认 12 帧。
                    """;
            }

            if (UnrealProjectSyncPage.Visibility == Visibility.Visible)
            {
                return """
                    基础素材命名规则
                    以下规则用于 /Game/AssetMaterial/ImageS/CharaterS/{英文代号} 下的 Texture。多张素材统一使用“英文代号-图片类型-排序”，例如 SAO_kirito-BattleAvatar-02；单张素材使用“英文代号-图片类型”，例如 SAO_kirito-ItemIcon。不要写成 Icon-SAO_kirito-01、BattleAvatar-SAO_kirito-02 这种图片类型在前的格式。修改 Unreal 里的资产名后，重新点击“获取项目角色”，工具箱会按名字重新识别素材类型。

                    图标-道具
                    标准命名：英文代号-ItemIcon，例如 SAO_kirito-ItemIcon。

                    技能图标
                    标准命名：英文代号-SkillIcon-排序，例如 SAO_kirito-SkillIcon-01。

                    对局内头像
                    标准命名：英文代号-BattleAvatar-排序，例如 SAO_kirito-BattleAvatar-02。

                    幻形完整立绘
                    标准命名：英文代号-FullMorphPortrait-排序，例如 SAO_kirito-FullMorphPortrait-01。

                    幻形立绘
                    标准命名：英文代号-MorphPortrait-排序，例如 SAO_kirito-MorphPortrait-01。

                    背景图
                    标准命名：英文代号-Background，例如 SAO_kirito-Background。

                    护援特写
                    标准命名：英文代号-SupportCutIn-排序，例如 SAO_kirito-SupportCutIn-01。

                    头像
                    标准命名：英文代号-Icon-排序，例如 SAO_kirito-Icon-01。

                    其他图片
                    标准命名：英文代号-OtherImage-排序，例如 SAO_kirito-OtherImage-01。不符合以上规则的 Texture 也会进入“其他图片”。命名不统一时先同步到其他图片也可以，后续在 Unreal 中改名再重新获取项目角色。

                    角色候选
                    基础素材目录和 ZD 目录最后一级文件夹名相同才会生成候选角色，例如 CharaterS/ALO_Yuki 和 GameActor2D/ALO_Yuki。

                    同步到素材卡
                    会按候选角色代号创建或选择工具箱里的同名草稿素材卡。

                    同步到工具箱
                    会把当前素材分类中已导出的 PNG 写入该角色 St2 的 AssetMaterial 文件夹。同步后数据以文件形式保存，后续打开 St2 会直接读取这些文件。
                    """;
            }

            if (BuffsPage.Visibility == Visibility.Visible)
            {
                return """
                    St6 BUFF
                    BUFF 用于记录角色相关状态、图标和说明。

                    英文代号
                    用于文件命名和数据索引，应简短稳定。

                    图标
                    选择并裁剪 BUFF 图标后会保存到当前角色文件夹。

                    备注
                    用来记录判定、蓝图思路和临时设计说明。
                    """;
            }

            if (SettingsPage.Visibility == Visibility.Visible)
            {
                return """
                    设置页
                    修改整体项目位置、夜晚模式、工作区路径显示和日志显示。

                    整体项目位置
                    选择父目录，工具箱会自动创建 CrossingVoidZDProject 并迁移已有数据。

                    辅助显示
                    夜晚模式影响整体外观；工作区路径和日志开关用于控制底部辅助区域。

                    撤回
                    Ctrl+Z 可以撤回最近一次设置开关修改。
                    """;
            }

            return """
                角色台
                创建和选择当前制作角色。继续按钮会进入最近一次编辑的角色和页面。

                已完成角色和草稿角色
                连携技选择角色时会从当前拥有的已完成角色和草稿角色中选择。
                """;
        }

        private async void HideSkillValueGuideOverlay()
        {
            if (SkillValueGuideOverlayHost.Visibility != Visibility.Visible)
            {
                return;
            }

            await AnimateReferenceOverlayAsync(SkillValueGuideOverlayHost, SkillValueGuideOverlayCardScale, show: false);
            SkillValueGuideOverlayHost.Visibility = Visibility.Collapsed;
        }

        private void AnimateReferenceOverlay(UIElement host, ScaleTransform cardScale, bool show)
        {
            _ = AnimateReferenceOverlayAsync(host, cardScale, show);
        }

        private Task AnimateReferenceOverlayAsync(UIElement host, ScaleTransform cardScale, bool show)
        {
            var completion = new TaskCompletionSource();
            var fromOpacity = show ? 0d : host.Opacity;
            var toOpacity = show ? 1d : 0d;
            var fromScale = show ? 0.96d : 1d;
            var toScale = show ? 1d : 0.97d;

            host.Opacity = fromOpacity;
            cardScale.ScaleX = fromScale;
            cardScale.ScaleY = fromScale;

            var easing = new CubicEase { EasingMode = show ? EasingMode.EaseOut : EasingMode.EaseIn };
            var duration = TimeSpan.FromMilliseconds(show ? 180 : 140);
            var storyboard = new Storyboard();

            var opacityAnimation = new DoubleAnimation
            {
                From = fromOpacity,
                To = toOpacity,
                Duration = duration,
                EasingFunction = easing
            };
            Storyboard.SetTarget(opacityAnimation, host);
            Storyboard.SetTargetProperty(opacityAnimation, nameof(UIElement.Opacity));
            storyboard.Children.Add(opacityAnimation);

            var scaleXAnimation = new DoubleAnimation
            {
                From = fromScale,
                To = toScale,
                Duration = duration,
                EasingFunction = easing
            };
            Storyboard.SetTarget(scaleXAnimation, cardScale);
            Storyboard.SetTargetProperty(scaleXAnimation, nameof(ScaleTransform.ScaleX));
            storyboard.Children.Add(scaleXAnimation);

            var scaleYAnimation = new DoubleAnimation
            {
                From = fromScale,
                To = toScale,
                Duration = duration,
                EasingFunction = easing
            };
            Storyboard.SetTarget(scaleYAnimation, cardScale);
            Storyboard.SetTargetProperty(scaleYAnimation, nameof(ScaleTransform.ScaleY));
            storyboard.Children.Add(scaleYAnimation);

            storyboard.Completed += (_, _) =>
            {
                host.Opacity = toOpacity;
                cardScale.ScaleX = toScale;
                cardScale.ScaleY = toScale;
                completion.TrySetResult();
            };
            storyboard.Begin();
            return completion.Task;
        }

        private void SkillValueGuideCloseButton_Click(object sender, RoutedEventArgs e)
        {
            HideSkillValueGuideOverlay();
        }

        private void SkillValueGuideOverlayHost_Tapped(object sender, TappedRoutedEventArgs e)
        {
            HideSkillValueGuideOverlay();
            e.Handled = true;
        }

        private void SkillValueGuideOverlayHost_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            HideSkillValueGuideOverlay();
            e.Handled = true;
        }

        private void SkillValueGuideOverlayHost_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                HideSkillValueGuideOverlay();
                e.Handled = true;
            }
        }

        private void SkillValueGuideOverlayCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
        }

        private void ReferenceImageCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is CharacterReferenceImage image)
            {
                ShowReferenceImageViewer(image);
                e.Handled = true;
            }
        }

        private void ReferenceImageCard_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is CharacterReferenceImage image)
            {
                ShowReferenceImageViewer(image);
                e.Handled = true;
            }
        }

        private async void ReferenceThumbnailImage_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Image thumbnail ||
                thumbnail.DataContext is not CharacterReferenceImage image ||
                !File.Exists(image.FilePath))
            {
                return;
            }

            try
            {
                thumbnail.Source = await LoadReferenceBitmapAsync(image.FilePath);
            }
            catch
            {
                thumbnail.Source = null;
            }
        }

        private void ReferenceImageCard_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not CharacterReferenceImage image ||
                sender is not FrameworkElement element)
            {
                return;
            }

            var menu = new MenuFlyout();
            var renameItem = new MenuFlyoutItem
            {
                Text = "重命名",
                CommandParameter = image
            };
            renameItem.Click += RenameReferenceImageMenuItem_Click;
            var deleteItem = new MenuFlyoutItem
            {
                Text = "删除",
                CommandParameter = image
            };
            deleteItem.Click += DeleteReferenceImageMenuItem_Click;
            menu.Items.Add(renameItem);
            menu.Items.Add(deleteItem);
            ShowMenuToRightOfPointer(menu, element, e);
            e.Handled = true;
        }

        private async void RenameReferenceImageMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { CommandParameter: CharacterReferenceImage image })
            {
                return;
            }

            var nameBox = new TextBox
            {
                Header = "文件名",
                Text = image.FileName,
                MaxLength = 120
            };
            var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "重命名参考图",
                nameBox,
                PrimaryButtonText: "重命名",
                CloseButtonText: "取消",
                ConfigureDialog: dialog =>
                {
                    dialog.MinWidth = 460;
                    dialog.MaxWidth = 460;
                    dialog.PrimaryButtonClick += (_, args) =>
                    {
                        if (string.IsNullOrWhiteSpace(nameBox.Text))
                        {
                            args.Cancel = true;
                        }
                    };
                }));
            if (result != DialogResultKind.Primary)
            {
                return;
            }

            try
            {
                await CharacterDesk.RenameReferenceImageAsync(image, nameBox.Text);
                ShowFloatingTip(InfoBarSeverity.Success, "参考图已重命名", nameBox.Text);
                AppendLog(LogKind.User, $"重命名草稿参考图：{image.FileName} -> {nameBox.Text}");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "参考图重命名失败", ex.Message);
                AppendLog(LogKind.Error, "参考图重命名失败。", ex);
            }
        }

        private async void DeleteReferenceImageMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { CommandParameter: CharacterReferenceImage image })
            {
                return;
            }

            var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "删除参考图",
                new TextBlock
                {
                    Text = $"确定删除参考图 {image.FileName} 吗？",
                    TextWrapping = TextWrapping.Wrap,
                    Width = 420
                },
                PrimaryButtonText: "删除",
                CloseButtonText: string.Empty,
                SecondaryButtonText: "取消",
                DefaultButton: ContentDialogButton.Primary,
                PrimaryButtonStyle: (Style)Application.Current.Resources["DialogAccentButtonStyle"]));
            if (result != DialogResultKind.Primary)
            {
                return;
            }

            try
            {
                await CharacterDesk.DeleteReferenceImageAsync(image);
                if (_viewingReferenceImage is not null &&
                    string.Equals(_viewingReferenceImage.FilePath, image.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    CloseReferenceImageViewer();
                }

                ShowFloatingTip(InfoBarSeverity.Success, "参考图已删除", image.FileName);
                AppendLog(LogKind.User, $"删除草稿参考图：{image.FileName}");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "参考图删除失败", ex.Message);
                AppendLog(LogKind.Error, "参考图删除失败。", ex);
            }
        }

        private async void ShowReferenceImageViewer(CharacterReferenceImage image)
        {
            if (!File.Exists(image.FilePath))
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "图片不存在", image.FileName);
                return;
            }

            _viewingReferenceImage = image;
            ReferenceImageViewerTitleText.Text = image.FileName;
            ReferenceImageViewerImage.Source = await LoadReferenceBitmapAsync(image.FilePath);
            ResetReferenceImageViewerTransform();
            ReferenceImageViewerHost.Visibility = Visibility.Visible;
            ReferenceImageViewerHost.Focus(FocusState.Programmatic);
            AppendLog(LogKind.User, $"打开草稿参考图查看：{image.FileName}");
        }

        private void CloseReferenceImageViewerButton_Click(object sender, RoutedEventArgs e)
        {
            CloseReferenceImageViewer();
        }

        private static async Task<BitmapImage> LoadReferenceBitmapAsync(string filePath)
        {
            var file = await StorageFile.GetFileFromPathAsync(filePath);
            await using var fileStream = await file.OpenStreamForReadAsync();
            using var memoryStream = new InMemoryRandomAccessStream();
            await fileStream.CopyToAsync(memoryStream.AsStreamForWrite());
            memoryStream.Seek(0);

            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(memoryStream);
            return bitmap;
        }

        private void CloseReferenceImageViewer()
        {
            _isPanningReferenceImage = false;
            _viewingReferenceImage = null;
            _viewingSequenceFrame = null;
            _viewingSequenceFrames = [];
            ReferenceImageViewerImage.Source = null;
            ReferenceImageViewerHost.Visibility = Visibility.Collapsed;
            ResetReferenceImageViewerTransform();
        }

        private void ReferenceImageViewerHost_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                CloseReferenceImageViewer();
                e.Handled = true;
                return;
            }

            if (e.Key is Windows.System.VirtualKey.Left or Windows.System.VirtualKey.NumberPad4 ||
                e.Key == Windows.System.VirtualKey.A)
            {
                if (_viewingSequenceFrame is not null)
                {
                    ShowAdjacentSequenceFrame(-1);
                }
                else
                {
                    ShowAdjacentReferenceImage(-1);
                }

                e.Handled = true;
                return;
            }

            if (e.Key is Windows.System.VirtualKey.Right or Windows.System.VirtualKey.NumberPad6 ||
                e.Key == Windows.System.VirtualKey.D)
            {
                if (_viewingSequenceFrame is not null)
                {
                    ShowAdjacentSequenceFrame(1);
                }
                else
                {
                    ShowAdjacentReferenceImage(1);
                }

                e.Handled = true;
            }
        }

        private void ReferenceImageViewerHost_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            CloseReferenceImageViewer();
            e.Handled = true;
        }

        private void ReferenceImageViewerCanvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(ReferenceImageViewerCanvas);
            var previousScale = _referenceImageViewerScale;
            _referenceImageViewerScale = Math.Clamp(
                _referenceImageViewerScale * (point.Properties.MouseWheelDelta > 0 ? 1.1 : 0.9),
                0.15,
                8);
            var actualZoomFactor = _referenceImageViewerScale / previousScale;
            var canvasCenterX = ReferenceImageViewerCanvas.ActualWidth / 2;
            var canvasCenterY = ReferenceImageViewerCanvas.ActualHeight / 2;
            var pointerOffsetX = point.Position.X - canvasCenterX - ReferenceImageViewerTransform.TranslateX;
            var pointerOffsetY = point.Position.Y - canvasCenterY - ReferenceImageViewerTransform.TranslateY;
            ReferenceImageViewerTransform.TranslateX -= pointerOffsetX * (actualZoomFactor - 1);
            ReferenceImageViewerTransform.TranslateY -= pointerOffsetY * (actualZoomFactor - 1);
            ReferenceImageViewerTransform.ScaleX = _referenceImageViewerScale;
            ReferenceImageViewerTransform.ScaleY = _referenceImageViewerScale;
            e.Handled = true;
        }

        private void ReferenceImageViewerCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(ReferenceImageViewerCanvas);
            if (!point.Properties.IsLeftButtonPressed)
            {
                return;
            }

            _isPanningReferenceImage = true;
            _lastReferenceImagePointerPosition = point.Position;
            ReferenceImageViewerCanvas.CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private void ReferenceImageViewerCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isPanningReferenceImage)
            {
                return;
            }

            var point = e.GetCurrentPoint(ReferenceImageViewerCanvas);
            ReferenceImageViewerTransform.TranslateX += point.Position.X - _lastReferenceImagePointerPosition.X;
            ReferenceImageViewerTransform.TranslateY += point.Position.Y - _lastReferenceImagePointerPosition.Y;
            _lastReferenceImagePointerPosition = point.Position;
            e.Handled = true;
        }

        private void ReferenceImageViewerCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            EndReferenceImagePan(e);
        }

        private void ReferenceImageViewerCanvas_PointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            EndReferenceImagePan(e);
        }

        private void ReferenceImageViewerCanvas_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            _isPanningReferenceImage = false;
        }

        private void ReferenceImageViewerCanvas_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            ResetReferenceImageViewerTransform();
            e.Handled = true;
        }

        private void EndReferenceImagePan(PointerRoutedEventArgs e)
        {
            _isPanningReferenceImage = false;
            ReferenceImageViewerCanvas.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        }

        private void ResetReferenceImageViewerTransform()
        {
            _referenceImageViewerScale = 1;
            ReferenceImageViewerTransform.ScaleX = 1;
            ReferenceImageViewerTransform.ScaleY = 1;
            ReferenceImageViewerTransform.TranslateX = 0;
            ReferenceImageViewerTransform.TranslateY = 0;
        }

        private void ShowAdjacentReferenceImage(int direction)
        {
            if (_viewingReferenceImage is null || CharacterDesk.ReferenceImages.Count == 0)
            {
                return;
            }

            var currentIndex = CharacterDesk.ReferenceImages
                .Select((image, index) => new { image, index })
                .FirstOrDefault(item => string.Equals(item.image.FilePath, _viewingReferenceImage.FilePath, StringComparison.OrdinalIgnoreCase))
                ?.index ?? -1;
            if (currentIndex < 0)
            {
                return;
            }

            var nextIndex = (currentIndex + direction + CharacterDesk.ReferenceImages.Count) % CharacterDesk.ReferenceImages.Count;
            ShowReferenceImageViewer(CharacterDesk.ReferenceImages[nextIndex]);
        }

        private async void ImportReferenceImagesButton_Click(object sender, RoutedEventArgs e)
        {
            if (CharacterDesk.CurrentCharacter is null)
            {
                return;
            }

            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".webp");
            picker.FileTypeFilter.Add(".bmp");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            var files = await picker.PickMultipleFilesAsync();
            var paths = files.Select(file => file.Path).Where(File.Exists).ToList();
            await ImportReferenceImagesAsync(paths);
        }

        private void ReferenceImages_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.Handled = true;
        }

        private async void ReferenceImages_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (CharacterDesk.CurrentCharacter is null ||
                !e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return;
            }

            var items = await e.DataView.GetStorageItemsAsync();
            var paths = items
                .OfType<StorageFile>()
                .Select(file => file.Path)
                .Where(File.Exists)
                .ToList();
            await ImportReferenceImagesAsync(paths);
        }

        private async Task ImportReferenceImagesAsync(IReadOnlyList<string> paths)
        {
            if (CharacterDesk.CurrentCharacter is null || paths.Count == 0)
            {
                return;
            }

            try
            {
                await CharacterDesk.ImportReferenceImagesAsync(paths);
                MarkLastEditedModule("ActionFrames");
                PersistCurrentCharacterSelection();
                ShowFloatingTip(InfoBarSeverity.Success, "参考图已导入", $"{paths.Count} 个文件");
                AppendLog(LogKind.User, $"导入草稿参考图：{paths.Count} 个文件。");
            }
            catch (Exception ex)
            {
                CharacterDesk.StatusText = $"导入参考图失败：{ex.Message}";
                AppendLog(LogKind.Error, "导入草稿参考图失败。", ex);
            }
        }

        private void OpenReferenceFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (CharacterDesk.CurrentCharacter is null)
            {
                return;
            }

            Directory.CreateDirectory(CharacterDesk.CurrentCharacter.ReferenceFolderPath);
            Process.Start(new ProcessStartInfo
            {
                FileName = CharacterDesk.CurrentCharacter.ReferenceFolderPath,
                UseShellExecute = true
            });
            AppendLog(LogKind.User, $"打开草稿参考图文件夹：{CharacterDesk.CurrentCharacter.ReferenceFolderPath}");
        }

        private void ScheduleDraftSave()
        {
            RunOnUiThread(() =>
            {
                _draftSaveTimer.Stop();
                _draftSaveTimer.Start();
            });
        }

        private async void DraftSaveTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            sender.Stop();
            await SaveDraftNowAsync();
        }

        private async Task SaveDraftNowAsync()
        {
            try
            {
                await CharacterDesk.SaveDraftNowAsync();
                MarkLastEditedModule("ActionFrames");
                PersistCurrentCharacterSelection();
            }
            catch (Exception ex)
            {
                CharacterDesk.StatusText = $"草稿保存失败：{ex.Message}";
                AppendLog(LogKind.Error, "草稿自动保存失败。", ex);
            }
        }

        private void PersistCurrentCharacterSelection()
        {
            Settings.SetCurrentCharacter(CharacterDesk.CurrentCharacter?.Code, CharacterDesk.LastEditedCharacter?.Code);
        }

        private void MarkLastEditedModule(string moduleTag)
        {
            Settings.SetLastEditedModule(moduleTag);
        }

        private void MarkLastEditedModule(ToolboxModuleKey moduleKey)
        {
            var module = _applicationViewModel.Modules.FirstOrDefault(item => item.Key == moduleKey);
            if (module is not null)
            {
                MarkLastEditedModule(module.Tag);
            }
        }

        private void RecordUserOperation(
            ToolboxModuleKey moduleKey,
            string actionName,
            string description,
            Func<Task<bool>>? undoAsync = null)
        {
            var module = _applicationViewModel.Modules.FirstOrDefault(item => item.Key == moduleKey);
            if (module is null)
            {
                return;
            }

            _applicationViewModel.UserOperations.Record(
                module,
                actionName,
                description,
                CharacterDesk.CurrentCharacter?.Code ?? string.Empty,
                undoAsync);
            MarkLastEditedModule(module.Tag);
            PersistCurrentCharacterSelection();
        }
    }
}
