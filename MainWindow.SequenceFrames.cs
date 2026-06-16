using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CrossingVoidZDTool
{
    public sealed partial class MainWindow
    {
        private async Task RefreshSequenceFramesAsync()
        {
            await _applicationViewModel.SequenceFrames.LoadAsync(CharacterDesk.CurrentCharacter);
        }

        private void MarkSequenceFramesEdited()
        {
            RecordUserOperation(
                ToolboxModuleKey.SequenceFrames,
                "编辑序列帧",
                CharacterDesk.CurrentCharacter?.StatusDisplayText ?? string.Empty);
        }

        private void RecordSequenceFrameOperation(
            string actionName,
            string description,
            SequenceFrameSection section,
            IReadOnlyList<string> snapshotFilePaths)
        {
            RecordUserOperation(
                ToolboxModuleKey.SequenceFrames,
                actionName,
                description,
                async () =>
                {
                    if (CharacterDesk.CurrentCharacter is null)
                    {
                        return false;
                    }

                    StopSequencePreview();
                    await _applicationViewModel.SequenceFrames.RestoreSectionSnapshotAsync(
                        CharacterDesk.CurrentCharacter,
                        section,
                        snapshotFilePaths);
                    ClearSequencePreviewCache();
                    UpdateSequencePreviewImageSource();
                    ShowFloatingTip(InfoBarSeverity.Success, "已撤销序列帧操作", description);
                    AppendLog(LogKind.User, $"撤销序列帧操作：{description}");
                    return true;
                });
        }

        private async Task RefreshSequenceFramesWithFeedbackAsync()
        {
            try
            {
                await RefreshSequenceFramesAsync();
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "序列帧检查失败", ex.Message);
                AppendLog(LogKind.Error, "序列帧检查失败。", ex);
            }
        }

        private async void ImportSequenceFramesButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { CommandParameter: SequenceFrameSection section })
            {
                return;
            }

            var paths = await PickSequenceFrameImagesAsync();
            if (paths.Count == 0)
            {
                return;
            }

            await ImportSequenceFramesAsync(section, paths);
        }

        private async void SequenceFrameSection_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if ((sender as FrameworkElement)?.Tag is not SequenceFrameSection section ||
                !e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return;
            }

            var items = await e.DataView.GetStorageItemsAsync();
            var paths = items
                .OfType<StorageFile>()
                .Select(file => file.Path)
                .Where(SequenceFrameService.IsSupportedImage)
                .ToList();
            if (paths.Count == 0)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "没有可导入图片", "只支持 png、jpg、jpeg、webp、bmp。");
                return;
            }

            await ImportSequenceFramesAsync(section, paths);
        }

        private void SequenceFrameSection_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.Handled = true;
        }

        private async Task ImportSequenceFramesAsync(SequenceFrameSection section, IReadOnlyList<string> paths)
        {
            if (CharacterDesk.CurrentCharacter is null)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择角色", "请先在角色台选择当前制作角色。");
                return;
            }

            try
            {
                MarkSequenceFramesEdited();
                StopSequencePreview();
                ClearSequencePreviewCache();
                await _applicationViewModel.SequenceFrames.ImportAsync(CharacterDesk.CurrentCharacter, section, paths);
                ShowFloatingTip(InfoBarSeverity.Success, "序列帧已导入", $"{section.Action.DisplayName}：{paths.Count} 张。");
                AppendLog(LogKind.User, $"导入序列帧：{section.Action.DisplayName} / {section.Action.Code}，{paths.Count} 张。");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "序列帧导入失败", ex.Message);
                AppendLog(LogKind.Error, "序列帧导入失败。", ex);
            }
        }

        private void OpenSequenceFrameFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (CharacterDesk.CurrentCharacter is null ||
                sender is not Button { CommandParameter: SequenceFrameSection section })
            {
                return;
            }

            var folderPath = _applicationViewModel.SequenceFrames.GetActionFolderPath(CharacterDesk.CurrentCharacter, section);
            Directory.CreateDirectory(folderPath);
            Process.Start(new ProcessStartInfo
            {
                FileName = folderPath,
                UseShellExecute = true
            });
            MarkLastEditedModule("SequenceFrames");
        }

        private async void PreviewSequenceFramesButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { CommandParameter: SequenceFrameSection section })
            {
                return;
            }

            StopSequencePreview();
            ClearSequencePreviewCache();
            ResetSequencePreviewTransform();
            _applicationViewModel.SequenceFrames.SelectSection(section);
            UpdateSequencePreviewImageSource();
            MarkLastEditedModule("SequenceFrames");
            if (section.Frames.Count == 0)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "暂无序列帧", "先导入这一组动作图片，再进行预览。");
                return;
            }

            await StartSequencePreviewAsync();
        }

        private async Task StartSequencePreviewAsync()
        {
            if (_applicationViewModel.SequenceFrames.PreviewFrames.Count == 0)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "暂无预览帧", "先点击动作卡右侧的播放按钮选择序列。");
                return;
            }

            try
            {
                await PreloadSequencePreviewBitmapsAsync();
                if (!TryUpdateCurrentOrNextSequencePreviewImageSource())
                {
                    ShowFloatingTip(InfoBarSeverity.Warning, "序列帧无法播放", "没有可读取的图片，请检查文件是否缺失或损坏。");
                    return;
                }

                var fps = Math.Clamp(_applicationViewModel.SequenceFrames.PreviewFps, 1, 60);
                _sequencePreviewTimer.Interval = TimeSpan.FromMilliseconds(1000d / fps);
                _applicationViewModel.SequenceFrames.StartPreview();
                _sequencePreviewTimer.Start();
                MarkLastEditedModule("SequenceFrames");
            }
            catch (Exception ex)
            {
                StopSequencePreview();
                ShowFloatingTip(InfoBarSeverity.Error, "序列播放失败", ex.Message);
                AppendLog(LogKind.Error, "序列帧播放失败。", ex);
            }
        }

        private async void ViewSequenceFrameButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { CommandParameter: SequenceFrameItem frame } button)
            {
                return;
            }

            var section = ResolveSequenceFrameSection(button);
            if (section is not null)
            {
                _applicationViewModel.SequenceFrames.SelectSection(section);
                _applicationViewModel.SequenceFrames.SelectFrame(frame);
                MarkLastEditedModule("SequenceFrames");
                await ShowSequenceFrameViewerAsync(frame, section.Frames);
                return;
            }

            try
            {
                await ShowSequenceFrameViewerAsync(frame, _applicationViewModel.SequenceFrames.PreviewFrames);
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "图片打开失败", ex.Message);
                AppendLog(LogKind.Error, "序列帧图片打开失败。", ex);
            }
        }

        private void ManageSequenceFramesButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { CommandParameter: SequenceFrameSection section })
            {
                return;
            }

            ShowSequenceFrameManager(section);
        }

        private void PreviewThumbnailSequenceButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not DependencyObject source ||
                ResolveSequenceFrameSection(source) is not { } section)
            {
                return;
            }

            ShowSequenceFrameManager(section);
        }

        private void ShowSequenceFrameManager(SequenceFrameSection section)
        {
            if (SequenceFramesCollectionHost.Visibility == Visibility.Visible)
            {
                HideSequenceFrameCollection();
            }

            _applicationViewModel.SequenceFrames.SelectSectionForManagement(section);
            SequenceFramesManagerHost.Visibility = Visibility.Visible;
            AnimateReferenceOverlay(SequenceFramesManagerHost, SequenceFramesManagerCardScale, show: true);
            SequenceFramesManagerHost.Focus(FocusState.Programmatic);
            MarkLastEditedModule("SequenceFrames");
        }

        private async void HideSequenceFrameManager()
        {
            if (SequenceFramesManagerHost.Visibility != Visibility.Visible)
            {
                return;
            }

            await AnimateReferenceOverlayAsync(SequenceFramesManagerHost, SequenceFramesManagerCardScale, show: false);
            SequenceFramesManagerHost.Visibility = Visibility.Collapsed;
            _isReorderingSequenceFrames = false;
        }

        private void SequenceFramesManagerCloseButton_Click(object sender, RoutedEventArgs e)
        {
            HideSequenceFrameManager();
        }

        private void SequenceFramesManagerHost_Tapped(object sender, TappedRoutedEventArgs e)
        {
            HideSequenceFrameManager();
            e.Handled = true;
        }

        private void SequenceFramesManagerHost_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                HideSequenceFrameManager();
                e.Handled = true;
            }
        }

        private void SequenceFramesManagerCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
        }

        private async void OpenSequenceCollectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (CharacterDesk.CurrentCharacter is null)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择角色", "请先选择角色再查看帧合集。");
                return;
            }

            try
            {
                ShowGlobalProgress("打开帧合集", CharacterDesk.CurrentCharacter.StatusDisplayText);
                UpdateGlobalProgress("正在准备帧合集...", 2, CharacterDesk.CurrentCharacter.Code);
                var progress = new Progress<ProgressUpdate>(update =>
                    UpdateGlobalProgress(update.Message, update.Percent, update.Detail, update.IsIndeterminate));
                await _applicationViewModel.SequenceFrames.RefreshCollectionAsync(
                    progress,
                    GetGlobalProgressCancellationToken());
                CompleteGlobalProgress("帧合集已就绪", _applicationViewModel.SequenceFrames.CollectionSummaryText);
                await HideGlobalProgressAfterDelayAsync(600);
                SequenceFramesCollectionHost.Visibility = Visibility.Visible;
                AnimateReferenceOverlay(SequenceFramesCollectionHost, SequenceFramesCollectionCardScale, show: true);
                SequenceFramesCollectionHost.Focus(FocusState.Programmatic);
                MarkLastEditedModule("SequenceFrames");
                AppendLog(LogKind.User, "打开帧合集。");
            }
            catch (OperationCanceledException)
            {
                CompleteGlobalProgress("打开帧合集已取消", CharacterDesk.CurrentCharacter.Code);
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (Exception ex)
            {
                CompleteGlobalProgress("打开帧合集失败", ex.Message);
                await HideGlobalProgressAfterDelayAsync();
                ShowFloatingTip(InfoBarSeverity.Error, "打开帧合集失败", ex.Message);
                AppendLog(LogKind.Error, "打开帧合集失败。", ex);
            }
        }

        private async void HideSequenceFrameCollection()
        {
            if (SequenceFramesCollectionHost.Visibility != Visibility.Visible)
            {
                return;
            }

            await AnimateReferenceOverlayAsync(SequenceFramesCollectionHost, SequenceFramesCollectionCardScale, show: false);
            SequenceFramesCollectionHost.Visibility = Visibility.Collapsed;
        }

        private void SequenceFramesCollectionCloseButton_Click(object sender, RoutedEventArgs e)
        {
            HideSequenceFrameCollection();
        }

        private void SequenceFramesCollectionHost_Tapped(object sender, TappedRoutedEventArgs e)
        {
            HideSequenceFrameCollection();
            e.Handled = true;
        }

        private void SequenceFramesCollectionHost_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                HideSequenceFrameCollection();
                e.Handled = true;
            }
        }

        private void SequenceFramesCollectionCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
        }

        private async void SequenceFramesCollectionGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not SequenceFrameCollectionItem item)
            {
                return;
            }

            var frame = new SequenceFrameItem(
                item.FilePath,
                item.FileUri,
                item.FileName,
                $"{item.FilePath}|{item.ContentHash}",
                1,
                0,
                0,
                true,
                DateTime.Now);
            try
            {
                await ShowSequenceFrameViewerAsync(frame, _applicationViewModel.SequenceFrames.CollectionItems.Select(collectionItem =>
                    new SequenceFrameItem(
                        collectionItem.FilePath,
                        collectionItem.FileUri,
                        collectionItem.FileName,
                        $"{collectionItem.FilePath}|{collectionItem.ContentHash}",
                        1,
                        0,
                        0,
                        true,
                        DateTime.Now)));
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "图片打开失败", ex.Message);
                AppendLog(LogKind.Error, "帧合集图片打开失败。", ex);
            }
        }

        private void ResolveDuplicateSequenceFrameMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { Tag: SequenceFrameCollectionItem item })
            {
                return;
            }

            if (!item.HasDuplicate)
            {
                ShowFloatingTip(InfoBarSeverity.Informational, "没有重复内容", item.FileName);
                return;
            }

            var duplicates = _applicationViewModel.SequenceFrames.GetDuplicateCollectionItems(item);
            if (duplicates.Count <= 1)
            {
                ShowFloatingTip(InfoBarSeverity.Informational, "没有重复内容", item.FileName);
                return;
            }

            _pendingDuplicateFrameItems = duplicates;
            _selectedDuplicateFrameItem = duplicates
                .FirstOrDefault(candidate => string.Equals(candidate.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase))
                ?? duplicates[0];
            SequenceFrameDuplicateResolverStatusText.Text =
                $"选择一个资源作为保留项，其它 {duplicates.Count - 1} 个重复资源会删除，所有序列引用会重定向到保留项。";
            SequenceFrameDuplicateResolverItemsControl.ItemsSource = duplicates;
            SequenceFrameDuplicateResolverHost.Visibility = Visibility.Visible;
            AnimateReferenceOverlay(
                SequenceFrameDuplicateResolverHost,
                SequenceFrameDuplicateResolverCardScale,
                show: true);
            SequenceFrameDuplicateResolverHost.Focus(FocusState.Programmatic);
            SelectDuplicateResolverRadio(_selectedDuplicateFrameItem);
        }

        private async void HideSequenceFrameDuplicateResolver()
        {
            if (SequenceFrameDuplicateResolverHost.Visibility != Visibility.Visible)
            {
                return;
            }

            await AnimateReferenceOverlayAsync(
                SequenceFrameDuplicateResolverHost,
                SequenceFrameDuplicateResolverCardScale,
                show: false);
            SequenceFrameDuplicateResolverHost.Visibility = Visibility.Collapsed;
            _pendingDuplicateFrameItems = [];
            _selectedDuplicateFrameItem = null;
        }

        private void SequenceFrameDuplicateResolverHost_Tapped(object sender, TappedRoutedEventArgs e)
        {
            HideSequenceFrameDuplicateResolver();
            e.Handled = true;
        }

        private void SequenceFrameDuplicateResolverHost_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                HideSequenceFrameDuplicateResolver();
                e.Handled = true;
                return;
            }

            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                _ = ResolveSelectedDuplicateFramesAsync();
                e.Handled = true;
            }
        }

        private void SequenceFrameDuplicateResolverHost_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            HideSequenceFrameDuplicateResolver();
            e.Handled = true;
        }

        private void SequenceFrameDuplicateResolverCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
        }

        private void SequenceFrameDuplicateResolverCard_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            HideSequenceFrameDuplicateResolver();
            e.Handled = true;
        }

        private void SequenceFrameDuplicateResolverRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton { Tag: SequenceFrameCollectionItem item })
            {
                _selectedDuplicateFrameItem = item;
            }
        }

        private void SequenceFrameDuplicateResolverCancelButton_Click(object sender, RoutedEventArgs e)
        {
            HideSequenceFrameDuplicateResolver();
        }

        private async void SequenceFrameDuplicateResolverConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            await ResolveSelectedDuplicateFramesAsync();
        }

        private async Task ResolveSelectedDuplicateFramesAsync()
        {
            if (CharacterDesk.CurrentCharacter is null ||
                _selectedDuplicateFrameItem is null ||
                _pendingDuplicateFrameItems.Count <= 1 ||
                _isResolvingDuplicateFrames)
            {
                return;
            }

            _isResolvingDuplicateFrames = true;
            try
            {
                var keptItem = _selectedDuplicateFrameItem;
                ShowGlobalProgress("处理重复帧", keptItem.FileName);
                UpdateGlobalProgress("正在重定向重复帧引用...", 15, keptItem.FileName);
                var progress = new Progress<ProgressUpdate>(update =>
                    UpdateGlobalProgress(update.Message, update.Percent, update.Detail, update.IsIndeterminate));
                var count = await _applicationViewModel.SequenceFrames.ResolveDuplicateFramesAsync(
                    CharacterDesk.CurrentCharacter,
                    keptItem,
                    _pendingDuplicateFrameItems,
                    progress,
                    GetGlobalProgressCancellationToken());
                UpdateGlobalProgress("正在刷新帧合集...", 92, keptItem.FileName);
                ClearSequencePreviewCache();
                HideSequenceFrameDuplicateResolver();
                CompleteGlobalProgress("重复帧处理完成", $"已重定向 {count} 个引用。");
                await HideGlobalProgressAfterDelayAsync(700);
                ShowFloatingTip(InfoBarSeverity.Success, "重复帧已处理", $"已重定向 {count} 个引用，保留 {keptItem.FileName}。");
                AppendLog(LogKind.User, $"处理重复帧：保留 {keptItem.FileName}，重定向 {count} 个引用。");
            }
            catch (OperationCanceledException)
            {
                CompleteGlobalProgress("处理重复已取消", _selectedDuplicateFrameItem?.FileName);
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (Exception ex)
            {
                CompleteGlobalProgress("处理重复失败", ex.Message);
                await HideGlobalProgressAfterDelayAsync();
                ShowFloatingTip(InfoBarSeverity.Error, "处理重复失败", ex.Message);
                AppendLog(LogKind.Error, "处理重复帧失败。", ex);
            }
            finally
            {
                _isResolvingDuplicateFrames = false;
            }
        }

        private void SelectDuplicateResolverRadio(SequenceFrameCollectionItem? selectedItem)
        {
            if (selectedItem is null)
            {
                return;
            }

            foreach (var radioButton in FindVisualChildren<RadioButton>(SequenceFrameDuplicateResolverItemsControl))
            {
                if (radioButton.Tag is SequenceFrameCollectionItem item &&
                    string.Equals(item.FilePath, selectedItem.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    radioButton.IsChecked = true;
                    break;
                }
            }
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
            where T : DependencyObject
        {
            var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
            for (var index = 0; index < count; index++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, index);
                if (child is T typedChild)
                {
                    yield return typedChild;
                }

                foreach (var descendant in FindVisualChildren<T>(child))
                {
                    yield return descendant;
                }
            }
        }

        private async void SequenceFrameManagerGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not SequenceFrameItem frame)
            {
                return;
            }

            try
            {
                _applicationViewModel.SequenceFrames.SelectFrame(frame);
                await ShowSequenceFrameViewerAsync(frame, _applicationViewModel.SequenceFrames.SelectedSectionFrames);
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "图片打开失败", ex.Message);
                AppendLog(LogKind.Error, "序列帧管理层图片打开失败。", ex);
            }
        }

        private void SequenceFrameManagerGridView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            _isReorderingSequenceFrames = true;
        }

        private async void SequenceFrameManagerGridView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            if (!_isReorderingSequenceFrames ||
                CharacterDesk.CurrentCharacter is null ||
                _applicationViewModel.SequenceFrames.SelectedSection is not { } section)
            {
                _isReorderingSequenceFrames = false;
                return;
            }

            var orderedFrames = sender.Items
                .OfType<SequenceFrameItem>()
                .ToList();
            if (orderedFrames.Count == 0)
            {
                _isReorderingSequenceFrames = false;
                return;
            }

            try
            {
                MarkSequenceFramesEdited();
                StopSequencePreview();
                ClearSequencePreviewCache();
                await _applicationViewModel.SequenceFrames.ReorderFramesAsync(CharacterDesk.CurrentCharacter, section, orderedFrames);
                ShowFloatingTip(InfoBarSeverity.Success, "序列帧顺序已更新", section.Action.DisplayName);
                AppendLog(LogKind.User, $"调整序列帧顺序：{section.Action.DisplayName} / {section.Action.Code}");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "序列帧排序失败", ex.Message);
                AppendLog(LogKind.Error, "序列帧排序失败。", ex);
            }
            finally
            {
                _isReorderingSequenceFrames = false;
            }
        }

        private async void CopySequenceFrameMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { Tag: SequenceFrameItem frame } ||
                CharacterDesk.CurrentCharacter is null ||
                _applicationViewModel.SequenceFrames.SelectedSection is not { } section)
            {
                return;
            }

            try
            {
                var snapshot = await _applicationViewModel.SequenceFrames.CreateSectionSnapshotAsync(CharacterDesk.CurrentCharacter, section);
                StopSequencePreview();
                ClearSequencePreviewCache();
                await _applicationViewModel.SequenceFrames.DuplicateFrameAsync(CharacterDesk.CurrentCharacter, section, frame);
                RecordSequenceFrameOperation(
                    "复制序列帧",
                    $"{section.Action.DisplayName} / {frame.FileName}",
                    section,
                    snapshot);
                ShowFloatingTip(InfoBarSeverity.Success, "序列帧已复制", frame.FileName);
                AppendLog(LogKind.User, $"复制序列帧：{section.Action.DisplayName} / {frame.FileName}");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "序列帧复制失败", ex.Message);
                AppendLog(LogKind.Error, "序列帧复制失败。", ex);
            }
        }

        private async void InsertBlankSequenceFrameMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { Tag: SequenceFrameItem frame } ||
                CharacterDesk.CurrentCharacter is null ||
                _applicationViewModel.SequenceFrames.SelectedSection is not { } section)
            {
                return;
            }

            try
            {
                var snapshot = await _applicationViewModel.SequenceFrames.CreateSectionSnapshotAsync(CharacterDesk.CurrentCharacter, section);
                StopSequencePreview();
                ClearSequencePreviewCache();
                await _applicationViewModel.SequenceFrames.InsertBlankFrameAsync(CharacterDesk.CurrentCharacter, section, frame);
                RecordSequenceFrameOperation(
                    "插入空白序列帧",
                    $"{section.Action.DisplayName} / {frame.DisplayName}",
                    section,
                    snapshot);
                ShowFloatingTip(InfoBarSeverity.Success, "空白帧已插入", section.Action.DisplayName);
                AppendLog(LogKind.User, $"插入空白序列帧：{section.Action.DisplayName} / After={frame.DisplayName}");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "空白帧插入失败", ex.Message);
                AppendLog(LogKind.Error, "空白帧插入失败。", ex);
            }
        }

        private async void DeleteSequenceFrameMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { Tag: SequenceFrameItem frame } ||
                CharacterDesk.CurrentCharacter is null ||
                _applicationViewModel.SequenceFrames.SelectedSection is not { } section)
            {
                return;
            }

            try
            {
                var snapshot = await _applicationViewModel.SequenceFrames.CreateSectionSnapshotAsync(CharacterDesk.CurrentCharacter, section);
                StopSequencePreview();
                ClearSequencePreviewCache();
                await _applicationViewModel.SequenceFrames.DeleteFrameAsync(CharacterDesk.CurrentCharacter, section, frame);
                RecordSequenceFrameOperation(
                    "删除序列帧",
                    $"{section.Action.DisplayName} / {frame.FileName}",
                    section,
                    snapshot);
                ShowFloatingTip(InfoBarSeverity.Success, "序列帧已删除", frame.FileName);
                AppendLog(LogKind.User, $"删除序列帧：{section.Action.DisplayName} / {frame.FileName}");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "序列帧删除失败", ex.Message);
                AppendLog(LogKind.Error, "序列帧删除失败。", ex);
            }
        }

        private async void PlayPauseSequencePreviewButton_Click(object sender, RoutedEventArgs e)
        {
            if (_applicationViewModel.SequenceFrames.PreviewFrames.Count == 0)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "暂无预览帧", "先点击动作卡右侧的播放按钮选择序列。");
                return;
            }

            if (_applicationViewModel.SequenceFrames.IsPreviewing)
            {
                StopSequencePreview();
                return;
            }

            await StartSequencePreviewAsync();
        }

        private void SequencePreviewFpsTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (SequenceFramesPage.Visibility == Visibility.Visible)
            {
                MarkLastEditedModule("SequenceFrames");
            }

            if (_applicationViewModel.SequenceFrames.IsPreviewing)
            {
                var fps = Math.Clamp(_applicationViewModel.SequenceFrames.PreviewFps, 1, 60);
                _sequencePreviewTimer.Interval = TimeSpan.FromMilliseconds(1000d / fps);
            }
        }

        private void PreviousSequenceFrameButton_Click(object sender, RoutedEventArgs e)
        {
            StopSequencePreview();
            _applicationViewModel.SequenceFrames.StepPreviewFrame(-1);
            TryUpdateSequencePreviewImageSource();
        }

        private void NextSequenceFrameButton_Click(object sender, RoutedEventArgs e)
        {
            StopSequencePreview();
            _applicationViewModel.SequenceFrames.StepPreviewFrame(1);
            TryUpdateSequencePreviewImageSource();
        }

        private void SequencePreviewTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            _applicationViewModel.SequenceFrames.AdvancePreviewFrame();
            if (!TryUpdateCurrentOrNextSequencePreviewImageSource())
            {
                StopSequencePreview();
                return;
            }

            if (!_applicationViewModel.SequenceFrames.IsPreviewing)
            {
                sender.Stop();
            }
        }

        private void StopSequencePreview()
        {
            _sequencePreviewTimer.Stop();
            _applicationViewModel.SequenceFrames.StopPreview();
        }

        private async Task PreloadSequencePreviewBitmapsAsync()
        {
            _applicationViewModel.SequenceFrames.IsPreloadingPreview = true;
            try
            {
                var failures = await _sequencePreviewBitmapCache.PreloadAsync(_applicationViewModel.SequenceFrames.PreviewFrames);

                if (failures.Count > 0)
                {
                    ShowFloatingTip(
                        InfoBarSeverity.Warning,
                        "部分序列帧读取失败",
                        failures.Count == 1 ? failures[0].FileName : $"{failures[0].FileName} 等 {failures.Count} 张图片无法读取。");
                    AppendLog(
                        LogKind.Warning,
                        "St5 序列帧预加载部分失败：" +
                        string.Join(
                            Environment.NewLine,
                            failures
                                .Take(8)
                                .Select(failure => $"{failure.FileName} | {failure.ExceptionType ?? "LoadError"} | {failure.Message} | {failure.FilePath}")) +
                        (failures.Count > 8 ? $"{Environment.NewLine}... 还有 {failures.Count - 8} 张失败。" : string.Empty));
                }

                TryUpdateSequencePreviewImageSource();
            }
            finally
            {
                _applicationViewModel.SequenceFrames.IsPreloadingPreview = false;
            }
        }

        private void UpdateSequencePreviewImageSource()
        {
            _ = TryUpdateSequencePreviewImageSource();
        }

        private bool TryUpdateCurrentOrNextSequencePreviewImageSource()
        {
            var frameCount = _applicationViewModel.SequenceFrames.PreviewFrames.Count;
            if (frameCount == 0)
            {
                SequencePreviewImage.Source = null;
                return false;
            }

            for (var attempt = 0; attempt < frameCount; attempt++)
            {
                if (TryUpdateSequencePreviewImageSource())
                {
                    return true;
                }

                _applicationViewModel.SequenceFrames.MovePreviewFrame(1);
            }

            SequencePreviewImage.Source = null;
            return false;
        }

        private bool TryUpdateSequencePreviewImageSource()
        {
            var path = _applicationViewModel.SequenceFrames.CurrentFrameFilePath;
            var cacheKey = _applicationViewModel.SequenceFrames.CurrentFrameCacheKey;
            var uri = _applicationViewModel.SequenceFrames.CurrentFrameUri;
            if (string.IsNullOrWhiteSpace(path))
            {
                SequencePreviewImage.Source = null;
                return _applicationViewModel.SequenceFrames.PreviewFrames
                    .FirstOrDefault(frame => frame.CacheKey == cacheKey)
                    ?.IsBlank == true;
            }

            if (_sequencePreviewBitmapCache.IsFailed(path))
            {
                SequencePreviewImage.Source = null;
                return false;
            }

            if (!string.IsNullOrWhiteSpace(cacheKey) &&
                _sequencePreviewBitmapCache.TryGet(cacheKey, out var bitmap))
            {
                SequencePreviewImage.Source = bitmap;
                return true;
            }

            try
            {
                if (!File.Exists(path))
                {
                    _sequencePreviewBitmapCache.MarkFailed(path);
                    SequencePreviewImage.Source = null;
                    return false;
                }

                var loadedBitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(
                    new Uri(string.IsNullOrWhiteSpace(uri) ? path : uri, UriKind.Absolute));
                _sequencePreviewBitmapCache.Store(cacheKey, loadedBitmap);
                SequencePreviewImage.Source = loadedBitmap;
                return true;
            }
            catch (Exception ex)
            {
                _sequencePreviewBitmapCache.MarkFailed(path);
                SequencePreviewImage.Source = null;
                ShowFloatingTip(InfoBarSeverity.Error, "序列帧显示失败", ex.Message);
                AppendLog(LogKind.Error, "序列帧显示失败。", ex);
                return false;
            }
        }

        private void ClearSequencePreviewCache()
        {
            _sequencePreviewBitmapCache.Clear();
            SequencePreviewImage.Source = null;
        }

        private void SequencePreviewCanvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(SequencePreviewCanvas);
            var previousScale = _sequencePreviewScale;
            _sequencePreviewScale = Math.Clamp(
                _sequencePreviewScale * (point.Properties.MouseWheelDelta > 0 ? 1.1 : 0.9),
                0.2,
                8);
            var actualZoomFactor = _sequencePreviewScale / previousScale;
            var canvasCenterX = SequencePreviewCanvas.ActualWidth / 2;
            var canvasCenterY = SequencePreviewCanvas.ActualHeight / 2;
            var pointerOffsetX = point.Position.X - canvasCenterX - SequencePreviewImageTransform.TranslateX;
            var pointerOffsetY = point.Position.Y - canvasCenterY - SequencePreviewImageTransform.TranslateY;
            SequencePreviewImageTransform.TranslateX -= pointerOffsetX * (actualZoomFactor - 1);
            SequencePreviewImageTransform.TranslateY -= pointerOffsetY * (actualZoomFactor - 1);
            SequencePreviewImageTransform.ScaleX = _sequencePreviewScale;
            SequencePreviewImageTransform.ScaleY = _sequencePreviewScale;
            e.Handled = true;
        }

        private void SequencePreviewCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(SequencePreviewCanvas);
            if (!point.Properties.IsLeftButtonPressed)
            {
                return;
            }

            _isPanningSequencePreview = true;
            _lastSequencePreviewPointerPosition = point.Position;
            SequencePreviewCanvas.CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private void SequencePreviewCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isPanningSequencePreview)
            {
                return;
            }

            var point = e.GetCurrentPoint(SequencePreviewCanvas);
            SequencePreviewImageTransform.TranslateX += point.Position.X - _lastSequencePreviewPointerPosition.X;
            SequencePreviewImageTransform.TranslateY += point.Position.Y - _lastSequencePreviewPointerPosition.Y;
            _lastSequencePreviewPointerPosition = point.Position;
            e.Handled = true;
        }

        private void SequencePreviewCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            EndSequencePreviewPan(e);
        }

        private void SequencePreviewCanvas_PointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            EndSequencePreviewPan(e);
        }

        private void SequencePreviewCanvas_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            _isPanningSequencePreview = false;
        }

        private void SequencePreviewCanvas_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            ResetSequencePreviewTransform();
            e.Handled = true;
        }

        private void EndSequencePreviewPan(PointerRoutedEventArgs e)
        {
            _isPanningSequencePreview = false;
            SequencePreviewCanvas.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        }

        private void ResetSequencePreviewTransform()
        {
            _sequencePreviewScale = 1;
            SequencePreviewImageTransform.ScaleX = 1;
            SequencePreviewImageTransform.ScaleY = 1;
            SequencePreviewImageTransform.TranslateX = 0;
            SequencePreviewImageTransform.TranslateY = 0;
        }

        private Task ShowSequenceFrameViewerAsync(
            SequenceFrameItem frame,
            IEnumerable<SequenceFrameItem> frames)
        {
            if (!File.Exists(frame.FilePath))
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "图片不存在", frame.FileName);
                return Task.CompletedTask;
            }

            _viewingReferenceImage = null;
            _viewingSequenceFrame = frame;
            _viewingSequenceFrames = frames
                .OrderBy(item => item.Index)
                .ThenBy(item => item.FileName)
                .ToList();
            SequenceFramesManagerHost.Visibility = Visibility.Collapsed;
            _isReorderingSequenceFrames = false;
            ReferenceImageViewerTitleText.Text = frame.FileName;
            ReferenceImageViewerImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(frame.FileUri, UriKind.Absolute));
            ResetReferenceImageViewerTransform();
            ReferenceImageViewerHost.Visibility = Visibility.Visible;
            ReferenceImageViewerHost.Focus(FocusState.Programmatic);
            AppendLog(LogKind.User, $"打开序列帧查看：{frame.FileName}");
            return Task.CompletedTask;
        }

        private async void ShowAdjacentSequenceFrame(int direction)
        {
            if (_viewingSequenceFrame is null || _viewingSequenceFrames.Count == 0)
            {
                return;
            }

            var currentIndex = _viewingSequenceFrames
                .Select((frame, index) => new { frame, index })
                .FirstOrDefault(item =>
                    item.frame.Index == _viewingSequenceFrame.Index ||
                    string.Equals(item.frame.CacheKey, _viewingSequenceFrame.CacheKey, StringComparison.OrdinalIgnoreCase))
                ?.index ?? -1;
            if (currentIndex < 0)
            {
                return;
            }

            var nextIndex = (currentIndex + direction + _viewingSequenceFrames.Count) % _viewingSequenceFrames.Count;
            await ShowSequenceFrameViewerAsync(_viewingSequenceFrames[nextIndex], _viewingSequenceFrames);
        }

        private static SequenceFrameSection? ResolveSequenceFrameSection(DependencyObject source)
        {
            for (var current = source; current is not null; current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current))
            {
                if (current is FrameworkElement { Tag: SequenceFrameSection section })
                {
                    return section;
                }
            }

            return null;
        }

        private async Task<IReadOnlyList<string>> PickSequenceFrameImagesAsync()
        {
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
            return files.Select(file => file.Path).ToList();
        }
    }
}
