using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;
using CrossingVoidZDTool.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace CrossingVoidZDTool
{
    public sealed partial class MainWindow
    {
        private async Task RefreshSkillsAsync()
        {
            _skillsSaveTimer.Stop();
            await _applicationViewModel.Skills.LoadAsync(CharacterDesk.CurrentCharacter);
        }

        private void RegisterSkillIconPickerWheelHandler()
        {
            SkillIconPickerHost.AddHandler(
                UIElement.PointerWheelChangedEvent,
                new PointerEventHandler(SkillIconPickerWheelChanged),
                handledEventsToo: true);
        }

        private async Task RefreshSkillsWithFeedbackAsync()
        {
            try
            {
                await RefreshSkillsAsync();
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "技能加载失败", ex.Message);
                AppendLog(LogKind.Error, "技能加载失败。", ex);
            }
        }

        private void ScheduleSkillsSave()
        {
            RunOnUiThread(() =>
            {
                if (SkillsPage.Visibility == Visibility.Visible)
                {
                    MarkLastEditedModule(ToolboxModuleKey.Skills);
                    PersistCurrentCharacterSelection();
                }

                _skillsSaveTimer.Stop();
                _skillsSaveTimer.Start();
            });
        }

        private void ApplySkillEditorsToModel()
        {
            foreach (var editor in EnumerateVisualDescendants<SkillEditorControl>(SkillsPage))
            {
                editor.ApplyEditorValuesToModel();
            }
        }

        private async Task FlushPendingSkillsSaveAsync()
        {
            _skillsSaveTimer.Stop();
            await SaveSkillsNowAsync();
        }

        private void FlushPendingSkillsSave()
        {
            try
            {
                _skillsSaveTimer.Stop();
                ApplySkillEditorsToModel();
                _applicationViewModel.Skills.SaveNow(CharacterDesk.CurrentCharacter);
            }
            catch (Exception ex)
            {
                AppendLog(LogKind.Error, "技能关闭前保存失败。", ex);
            }
        }

        private async void SkillsSaveTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            sender.Stop();
            await SaveSkillsNowAsync();
        }

        private async Task SaveSkillsNowAsync()
        {
            try
            {
                ApplySkillEditorsToModel();
                await _applicationViewModel.Skills.SaveAsync(CharacterDesk.CurrentCharacter);
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "技能保存失败", ex.Message);
                AppendLog(LogKind.Error, "技能自动保存失败。", ex);
            }
        }

        private async void AddSkillStageButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { CommandParameter: ObservableCollection<CharacterSkillEntry> entries })
            {
                if (ReferenceEquals(entries, _applicationViewModel.Skills.ComboSkills))
                {
                    await AddComboSkillStageAsync();
                    return;
                }

                _applicationViewModel.Skills.AddStage(entries);
                MarkLastEditedModule("Skills");
            }
        }

        private async Task AddComboSkillStageAsync()
        {
            if (CharacterDesk.CurrentCharacter is null)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择角色", "请先在角色台选择当前制作角色。");
                return;
            }

            var characters = CharacterDesk.Characters
                .Where(character => !string.Equals(character.Code, CharacterDesk.CurrentCharacter.Code, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(character => character.IsCompleted)
                .ThenBy(character => character.EffectiveDisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            if (characters.Count == 0)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "不能创建连携技", "当前工程没有其他可选择的已完成或草稿角色。");
                return;
            }

            var selection = DialogContentFactory.CreateComboCharacterSelection(characters);
            var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                Title: "选择连携角色",
                Content: selection.Content,
                PrimaryButtonText: "创建",
                CloseButtonText: "取消",
                DefaultButton: ContentDialogButton.Primary,
                ConfigureDialog: dialog =>
                {
                    dialog.PrimaryButtonClick += (_, args) =>
                    {
                        if (selection.CharacterListView.SelectedItem is not ListViewItem)
                        {
                            selection.ErrorInfoBar.Message = "需要先选择一个角色。";
                            selection.ErrorInfoBar.IsOpen = true;
                            args.Cancel = true;
                        }
                    };
                }));

            if (result != DialogResultKind.Primary ||
                selection.CharacterListView.SelectedItem is not ListViewItem { Tag: CharacterCard character })
            {
                return;
            }

            _applicationViewModel.Skills.AddComboStage(character);
            MarkLastEditedModule("Skills");
            ShowFloatingTip(InfoBarSeverity.Success, "连携技已创建", $"已绑定：{character.EffectiveDisplayName}");
            AppendLog(LogKind.User, $"创建连携技：{character.EffectiveDisplayName} / {character.Code}");
        }

        private void SkillEditor_Edited(object? sender, SkillEditorEditEventArgs e)
        {
            _applicationViewModel.Skills.NotifyEdited();
            MarkLastEditedModule("Skills");
            PersistCurrentCharacterSelection();
        }

        private void SkillEditor_DeleteRequested(object? sender, object requestedEntry)
        {
            if (requestedEntry is not CharacterSkillEntry entry)
            {
                return;
            }

            if (_applicationViewModel.Skills.RemoveStage(entry))
            {
                MarkLastEditedModule("Skills");
                ShowFloatingTip(InfoBarSeverity.Success, "技能阶段已删除", "按 Ctrl+Z 可以撤回这次删除。");
            }
            else
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "不能删除", _applicationViewModel.Skills.StatusText);
            }
        }

        private void SkillEditor_FocusReleaseRequested(object? sender, EventArgs e)
        {
            ReleaseSkillsFocus();
        }

        private async void SkillEditor_IconSelectionRequested(object? sender, object requestedEntry)
        {
            if (requestedEntry is not CharacterSkillEntry entry)
            {
                return;
            }

            await ShowSkillIconPickerAsync(entry);
        }

        private bool UndoLastSkillStageChange()
        {
            if (!_applicationViewModel.Skills.UndoLastStageChange())
            {
                return false;
            }

            MarkLastEditedModule("Skills");
            ShowFloatingTip(InfoBarSeverity.Success, "已撤回技能阶段操作", "技能阶段增删已恢复。");
            return true;
        }

        private void SkillsBlankArea_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (!IsBlankAreaRightTap(SkillsPage, e.OriginalSource))
            {
                return;
            }

            ReleaseSkillsFocus();
            e.Handled = true;
        }

        private void ReleaseSkillsFocus()
        {
            SkillsFocusSink.Focus(FocusState.Programmatic);
        }

        private async Task ShowSkillIconPickerAsync(CharacterSkillEntry entry)
        {
            if (CharacterDesk.CurrentCharacter is null)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择角色", "请先在角色台选择当前制作角色。");
                return;
            }

            IReadOnlyList<BaseMaterialItem> icons;
            try
            {
                var sections = await Task.Run(() => _baseMaterialService.LoadSections(CharacterDesk.CurrentCharacter));
                icons = sections
                    .FirstOrDefault(section => section.Spec.Kind == BaseMaterialKind.SkillIcon)?
                    .Items
                    .Where(item => item.Status == BaseMaterialStatus.Ready)
                    .ToList() ?? [];
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "技能图标读取失败", ex.Message);
                AppendLog(LogKind.Error, "技能图标读取失败。", ex);
                return;
            }

            if (icons.Count == 0)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "没有可选技能图标", "请先在 St2-基础素材 导入合规的技能图标。");
                return;
            }

            _pendingSkillIconEntry = entry;
            _pendingBuffIconEntry = null;
            _selectedSkillIconItem = icons.FirstOrDefault(icon =>
                string.Equals(icon.FilePath, entry.IconPath, StringComparison.OrdinalIgnoreCase));
            SkillIconPickerStatusText.Text = $"来自 St2-基础素材 / 技能图标，共 {icons.Count} 张。";
            SkillIconPickerTitleText.Text = "技能图标可用范围";
            RefreshSkillIconPickerItems(icons);
            SkillIconPickerHost.Visibility = Visibility.Visible;
            AnimateReferenceOverlay(SkillIconPickerHost, SkillIconPickerCardScale, show: true);
            SkillIconPickerHost.Focus(FocusState.Programmatic);
        }

        private async Task ShowBuffIconPickerAsync(BuffEntry buff)
        {
            if (CharacterDesk.CurrentCharacter is null)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择角色", "请先在角色台选择当前制作角色。");
                return;
            }

            IReadOnlyList<BaseMaterialItem> icons;
            try
            {
                var sections = await Task.Run(() => _baseMaterialService.LoadSections(CharacterDesk.CurrentCharacter));
                icons = sections
                    .FirstOrDefault(section => section.Spec.Kind == BaseMaterialKind.BuffIcon)?
                    .Items
                    .Where(item => item.Status == BaseMaterialStatus.Ready)
                    .ToList() ?? [];
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "BUFF 图标读取失败", ex.Message);
                AppendLog(LogKind.Error, "BUFF 图标读取失败。", ex);
                return;
            }

            if (icons.Count == 0)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "没有可选 BUFF 图标", "请先在 St2-基础素材 导入合规的 BUFF 图标。");
                return;
            }

            _pendingSkillIconEntry = null;
            _pendingBuffIconEntry = buff;
            _selectedSkillIconItem = icons.FirstOrDefault(icon =>
                string.Equals(icon.FilePath, buff.IconPath, StringComparison.OrdinalIgnoreCase));
            SkillIconPickerTitleText.Text = "BUFF 图标可用范围";
            SkillIconPickerStatusText.Text = $"来自 St2-基础素材 / BUFF图标，共 {icons.Count} 张。";
            RefreshSkillIconPickerItems(icons);
            SkillIconPickerHost.Visibility = Visibility.Visible;
            AnimateReferenceOverlay(SkillIconPickerHost, SkillIconPickerCardScale, show: true);
            SkillIconPickerHost.Focus(FocusState.Programmatic);
        }

        private void RefreshSkillIconPickerItems(IReadOnlyList<BaseMaterialItem> icons)
        {
            SkillIconPickerItemsControl.ItemsSource = icons
                .Select(icon => new SkillIconPickerItem(
                    icon,
                    _selectedSkillIconItem is not null &&
                    string.Equals(_selectedSkillIconItem.FilePath, icon.FilePath, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        private void SkillIconPickerItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: SkillIconPickerItem pickerItem })
            {
                _selectedSkillIconItem = pickerItem.Item;
                if (SkillIconPickerItemsControl.ItemsSource is IReadOnlyList<SkillIconPickerItem> items)
                {
                    RefreshSkillIconPickerItems(items.Select(item => item.Item).ToList());
                }
            }
        }

        private async void SkillIconPickerConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingSkillIconEntry is not null && _selectedSkillIconItem is not null)
            {
                _skillsSaveTimer.Stop();
                _pendingSkillIconEntry.IconPath = _selectedSkillIconItem.FilePath;
                _pendingSkillIconEntry.IconUri = new Uri(_selectedSkillIconItem.FilePath).AbsoluteUri;
                _applicationViewModel.Skills.NotifyEdited();
                MarkLastEditedModule("Skills");
                PersistCurrentCharacterSelection();
                ShowFloatingTip(InfoBarSeverity.Success, "技能图标已选择", _selectedSkillIconItem.FileName);
                AppendLog(LogKind.User, $"选择技能图标：{_selectedSkillIconItem.FileName}");
            }
            else if (_pendingBuffIconEntry is not null && _selectedSkillIconItem is not null)
            {
                if (CharacterDesk.CurrentCharacter is null)
                {
                    ShowFloatingTip(InfoBarSeverity.Warning, "未选择角色", "请先在角色台选择当前制作角色。");
                    AppendLog(LogKind.Warning, "选择 BUFF 图标失败：当前未选择角色。");
                    HideSkillIconPicker();
                    return;
                }

                try
                {
                    _buffsSaveTimer.Stop();
                    await _applicationViewModel.Buffs.ImportIconAsync(
                        CharacterDesk.CurrentCharacter,
                        _pendingBuffIconEntry,
                        _selectedSkillIconItem.FilePath);
                    await SaveBuffsNowAsync();
                    MarkLastEditedModule(ToolboxModuleKey.Buffs);
                    PersistCurrentCharacterSelection();
                    ShowFloatingTip(InfoBarSeverity.Success, "BUFF 图标已选择", _selectedSkillIconItem.FileName);
                    AppendLog(LogKind.User, $"选择 BUFF 图标并复制到 BUFF 文件夹：{_pendingBuffIconEntry.GeneratedCode} <- {_selectedSkillIconItem.FileName}");
                }
                catch (Exception ex)
                {
                    ShowFloatingTip(InfoBarSeverity.Error, "BUFF 图标保存失败", ex.Message);
                    AppendLog(LogKind.Error, "BUFF 图标复制到 BUFF 文件夹失败。", ex);
                    return;
                }
            }

            HideSkillIconPicker();
        }

        private void SkillIconPickerCancelButton_Click(object sender, RoutedEventArgs e)
        {
            HideSkillIconPicker();
        }

        private async void HideSkillIconPicker()
        {
            if (SkillIconPickerHost.Visibility != Visibility.Visible)
            {
                return;
            }

            await AnimateReferenceOverlayAsync(SkillIconPickerHost, SkillIconPickerCardScale, show: false);
            SkillIconPickerHost.Visibility = Visibility.Collapsed;
            SkillIconPickerItemsControl.ItemsSource = null;
            _pendingSkillIconEntry = null;
            _pendingBuffIconEntry = null;
            _selectedSkillIconItem = null;
        }

        private void SkillIconPickerHost_Tapped(object sender, TappedRoutedEventArgs e)
        {
            HideSkillIconPicker();
        }

        private void SkillIconPickerHost_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            HideSkillIconPicker();
            e.Handled = true;
        }

        private void SkillIconPickerHost_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                HideSkillIconPicker();
                e.Handled = true;
            }
        }

        private void SkillIconPickerCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
        }

        private void SkillIconPickerScrollViewer_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            ScrollSkillIconPickerHorizontally(e);
        }

        private void SkillIconPickerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            if (SkillIconPickerHost.Visibility != Visibility.Visible)
            {
                return;
            }

            ScrollSkillIconPickerHorizontally(e);
        }

        private void ScrollSkillIconPickerHorizontally(PointerRoutedEventArgs e)
        {
            var delta = e.GetCurrentPoint(SkillIconPickerHost).Properties.MouseWheelDelta;
            if (delta == 0)
            {
                return;
            }

            var targetOffset = Math.Clamp(
                SkillIconPickerScrollViewer.HorizontalOffset - delta,
                0,
                SkillIconPickerScrollViewer.ScrollableWidth);
            SkillIconPickerScrollViewer.ChangeView(targetOffset, null, null, disableAnimation: true);
            e.Handled = true;
        }

        private sealed record SkillIconPickerItem(BaseMaterialItem Item, bool IsSelected);

        private static IEnumerable<T> EnumerateVisualDescendants<T>(DependencyObject root)
            where T : DependencyObject
        {
            var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
            for (var index = 0; index < count; index++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, index);
                if (child is T typed)
                {
                    yield return typed;
                }

                foreach (var descendant in EnumerateVisualDescendants<T>(child))
                {
                    yield return descendant;
                }
            }
        }
    }
}
