using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;
using CrossingVoidZDTool.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CrossingVoidZDTool
{
    public sealed partial class MainWindow : ISequenceFramesCommandHost, ISequenceFrameDuplicateDetectionHost, ISequenceFrameEditorHost
    {
        // S3：编辑器里改帧的流程搬进了 SequenceFrameEditorController。
        private SequenceFrameEditorController? _sequenceFrameEditor;

        private SequenceFrameEditorController SequenceFrameEditor =>
            _sequenceFrameEditor ??= new SequenceFrameEditorController(this, _applicationViewModel.SequenceFrames);

        Task ISequenceFramesCommandHost.CopySelectedEditorFrameAsync() =>
            EditorWithSelectedFrame((editor, frame) => editor.CopyFrameAsync(frame));

        Task ISequenceFramesCommandHost.DeleteSelectedEditorFrameAsync() =>
            EditorWithSelectedFrame((editor, frame) => editor.DeleteFrameAsync(frame, _applicationViewModel.SequenceFrames.SelectedSection!));

        Task ISequenceFramesCommandHost.DeleteSelectedFramesAsync() =>
            SequenceFrameEditor.DeleteFramesAsync(GetSelectedSequenceFrames().ToArray());

        void ISequenceFramesCommandHost.OpenActionFolder(SequenceFrameSection section)
        {
            if (CharacterDesk.CurrentCharacter is not { } character)
            {
                return;
            }

            var folderPath = _applicationViewModel.SequenceFrames.GetActionFolderPath(character, section);
            OpenFolderInExplorer(folderPath);
            MarkLastEditedModule("SequenceFrames");
        }

        async Task ISequenceFramesCommandHost.ImportFramesAsync(SequenceFrameSection section)
        {
            var paths = await PickSequenceFrameImagesAsync();
            if (paths.Count == 0)
            {
                return;
            }

            await SequenceFrameEditor.ImportAsync(section, paths);
        }

        void ISequenceFramesCommandHost.OpenEditorForPreviewSection()
        {
            if (_applicationViewModel.SequenceFrames.PreviewSection is not { } section)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Warning,
                    "未选择动作",
                    "先点击动作卡上的播放按钮，再编辑对应帧序列。");
                return;
            }

            ShowSequenceFrameManager(section);
        }

        void ISequenceFramesCommandHost.OpenManagerForFrame(SequenceFrameItem frame)
        {
            // 原来是从 sender 的 visual tree 往上找卡片的 DataContext；命令化之后改成从数据里查，
            // 顺带把"没有所属卡片"这种异常情形显式处理掉（原来是静默 return）。
            var section = _applicationViewModel.SequenceFrames.FindSectionContainingFrame(frame)
                ?? _applicationViewModel.SequenceFrames.PreviewSection;
            if (section is null)
            {
                return;
            }

            ShowSequenceFrameManager(section);
        }

        // ── S5 收尾：预览 / 播放 / 导航 / 图集 / 裁决器确认 ─────────────────────
        async Task ISequenceFramesCommandHost.PreviewSectionAsync(SequenceFrameSection section)
        {
            StopSequencePreview();
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

        void ISequenceFramesCommandHost.NavigateFrame(int direction) => NavigateSequenceEditorFrame(direction);

        Task ISequenceFramesCommandHost.TogglePreviewPlaybackAsync() => ToggleSequencePreviewPlaybackAsync();

        Task ISequenceFramesCommandHost.ToggleEditorPreviewPlaybackAsync() => ToggleSequenceEditorPreviewAsync();

        Task ISequenceFramesCommandHost.ReplaceSelectedEditorFrameAsync() =>
            EditorWithSelectedFrame((_, frame) => ReplaceSequenceEditorFrameAsync(frame));

        Task ISequenceFramesCommandHost.PickEditorFrameFromCollectionAsync() =>
            _applicationViewModel.SequenceFrames.SelectedEditorFrame is { } frame
                ? ShowSequenceFrameCollectionAsync(frame)
                : Task.CompletedTask;

        Task ISequenceFramesCommandHost.ExportAtlasAsync() => ExportSelectedSequenceAtlasAsync();

        Task ISequenceFramesCommandHost.ExportBasePlatesAsync() => ExportSelectedSequenceBasePlatesAsync();

        Task ISequenceFramesCommandHost.ConfirmDuplicateResolutionAsync() => ResolveSelectedDuplicateFramesAsync();

        void ISequenceFramesCommandHost.ToggleCopyTargetSelection()
        {
            if (_isSelectingSequenceFrameCopyTarget)
            {
                CancelSequenceFrameCopyTargetSelection();
                return;
            }

            StartSequenceFrameCopyTargetSelection(GetSelectedSequenceFrames());
        }

        void ISequenceFramesCommandHost.SelectReuseGroup(SequenceFrameItem frame)
        {
            var reusedFrames = _applicationViewModel.SequenceFrames.FindReuseGroupFrames(frame);
            if (reusedFrames.Count < 2)
            {
                return;
            }

            _isSynchronizingSequenceFrameSelection = true;
            try
            {
                SequenceFrameTimelineListView.SelectedItems.Clear();
                foreach (var reusedFrame in reusedFrames)
                {
                    SequenceFrameTimelineListView.SelectedItems.Add(reusedFrame);
                }
            }
            finally
            {
                _isSynchronizingSequenceFrameSelection = false;
            }

            StopSequencePreview();
            _applicationViewModel.SequenceFrames.SelectEditorFrame(frame);
            SynchronizeSequenceFrameVoiceSelection();
            TryUpdateSequencePreviewImageSource();
            UpdateSequencePreviewInterval();
            PlayCurrentSequenceFrameVoice();
            UpdateSequenceFrameSelectionPresentation(reusedFrames);
        }

        // ── S1 收尾：时间轴右键菜单五项 + 帧合集「处理重复」 ───────────────────────
        // 这几条以前都是 `sender`（MenuFlyoutItem）→ Tag 反推那一帧；命令化之后
        // 帧由 `CommandParameter` 直接传进来，壳里不再需要"从视觉树/标签猜"的路径。
        Task ISequenceFramesCommandHost.ReplaceFrameFromMenuAsync(SequenceFrameItem frame) =>
            ReplaceSequenceEditorFrameAsync(frame);

        Task ISequenceFramesCommandHost.CopyFrameFromMenuAsync(SequenceFrameItem frame) =>
            SequenceFrameEditor.CopyFrameAsync(frame);

        Task ISequenceFramesCommandHost.InsertBlankFrameAtFrameAsync(SequenceFrameItem frame, bool before) =>
            InsertBlankSequenceFrameAsync(
                frame,
                before ? SequenceFrameInsertPosition.Before : SequenceFrameInsertPosition.After);

        async Task ISequenceFramesCommandHost.DeleteFrameFromMenuAsync(SequenceFrameItem frame)
        {
            if (_applicationViewModel.SequenceFrames.SelectedSection is { } section)
            {
                await DeleteSequenceFrameAsync(frame, section);
            }
        }

        void ISequenceFramesCommandHost.ResolveDuplicatesForCollectionItem(SequenceFrameCollectionItem item)
        {
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

        /// <summary>
        /// 「打开帧序列编辑器」按钮的处理器。
        ///
        /// **注意**：这个按钮的 XAML 换成命令绑定（OpenEditorCommand）时，
        /// XamlCompiler 会静默失败（症状是无诊断、pass1 全走完、返回 1）。
        /// 命令本身已经准备好了（`SequenceFrames.OpenEditorCommand` + 宿主实现都在），
        /// 但**换绑之前要先查清这个按钮所在模板的结构**——它和已经成功的那些按钮
        /// 位置不同（在 St5 页面里而不是浮层里）。
        /// </summary>
        private void OpenSequenceEditorButton_Click(object sender, RoutedEventArgs e)
        {
            if (_applicationViewModel.SequenceFrames.PreviewSection is not { } section)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择动作", "先点击动作卡上的播放按钮，再编辑对应帧序列。");
                return;
            }

            ShowSequenceFrameManager(section);
        }

        /// <summary>
        /// 旧调用点（把文件拖到动作卡上）的兼容转发：导入流程现在只有控制器那一份实现。
        /// 这也是这一页第三次撞到「同一件事有多个入口」——每次都靠转发统一，而不是各留一份。
        /// </summary>
        private Task ImportSequenceFramesAsync(SequenceFrameSection section, IReadOnlyList<string> paths) =>
            SequenceFrameEditor.ImportAsync(section, paths);

        /// <summary>旧调用点的兼容转发：批量删除现在只有控制器那一份实现。</summary>
        private Task DeleteSelectedSequenceFramesAsync(IReadOnlyList<SequenceFrameItem> frames) =>
            SequenceFrameEditor.DeleteFramesAsync(frames);

        /// <summary>命令共用的一步：拿到当前选中帧才执行（没有帧就什么都不做）。</summary>
        private Task EditorWithSelectedFrame(Func<SequenceFrameEditorController, SequenceFrameItem, Task> action) =>
            _applicationViewModel.SequenceFrames.SelectedEditorFrame is { } frame
                ? action(SequenceFrameEditor, frame)
                : Task.CompletedTask;

        CharacterCard? ISequenceFrameEditorHost.CurrentCharacter => CharacterDesk.CurrentCharacter;

        SequenceFrameSection? ISequenceFrameEditorHost.SelectedSection =>
            _applicationViewModel.SequenceFrames.SelectedSection;

        void ISequenceFrameEditorHost.StopSequencePreview() => StopSequencePreview();

        void ISequenceFrameEditorHost.MarkSequenceFramesEdited() => MarkSequenceFramesEdited();

        Task ISequenceFrameEditorHost.RunPreservingScrollAsync(Func<Task> action) =>
            RunWithPageScrollPositionPreservedAsync(SequenceFramesPage, action);

        void ISequenceFrameEditorHost.ResetSequencePreviewTransform() => ResetSequencePreviewTransform();

        Task ISequenceFrameEditorHost.StartSequencePreviewAsync() => StartSequencePreviewAsync();

        IReadOnlyList<SequenceFrameItem> ISequenceFrameEditorHost.SelectedTimelineFrames =>
            GetSelectedSequenceFrames();

        async Task<bool> ISequenceFrameEditorHost.ConfirmBatchDeleteAsync(int frameCount)
        {
            var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "批量删除序列帧",
                new TextBlock
                {
                    Text = $"确定删除选中的 {frameCount} 帧吗？",
                    TextWrapping = TextWrapping.Wrap
                },
                PrimaryButtonText: "删除",
                CloseButtonText: "取消"));
            return result == DialogResultKind.Primary;
        }

        void ISequenceFrameEditorHost.ClearSequencePreviewCache() => ClearSequencePreviewCache();

        void ISequenceFrameEditorHost.ClearSequencePreviewSource() => ShowSequencePreviewSource(null);

        void ISequenceFrameEditorHost.UpdateSequencePreviewImageSource() => UpdateSequencePreviewImageSource();

        void ISequenceFrameEditorHost.UpdateSequencePreviewInterval() => UpdateSequencePreviewInterval();

        void ISequenceFrameEditorHost.HideSequenceFrameManager() => HideSequenceFrameManager();

        void ISequenceFrameEditorHost.RecordSequenceFrameOperation(
            string actionName,
            string description,
            SequenceFrameSection section,
            IReadOnlyList<string> snapshotFilePaths) =>
            RecordSequenceFrameOperation(actionName, description, section, snapshotFilePaths);

        void ISequenceFrameEditorHost.ShowFloatingTip(NotifySeverity severity, string title, string message) =>
            ((INotificationService)this).Notify(severity, title, message);

        void ISequenceFrameEditorHost.AppendLog(LogKind kind, string message, Exception? error) =>
            AppendLog(kind, message, error);

        /// <summary>
        /// 旧调用点的兼容转发：删除流程现在只有控制器那一份实现。
        ///
        /// 侦察时我以为「删除」只有一处实现，实际文件里有**两份**（一处带防重入、
        /// 一处不带），这是现场第二次撞到"唯一实现"的假设不成立——所以这里保留
        /// 这个小转发，把两处调用点统一到控制器，而不是在原地各留一份。
        /// </summary>
        private Task DeleteSequenceFrameAsync(SequenceFrameItem frame, SequenceFrameSection section) =>
            SequenceFrameEditor.DeleteFrameAsync(frame, section);

        // S2：检测重复的流程搬进了 SequenceFrameDuplicateDetectionController。
        private SequenceFrameDuplicateDetectionController? _duplicateDetection;

        private SequenceFrameDuplicateDetectionController DuplicateDetection =>
            _duplicateDetection ??= new SequenceFrameDuplicateDetectionController(
                this, _applicationViewModel.SequenceFrames);

        Task ISequenceFramesCommandHost.DetectDuplicatesAsync() => DuplicateDetection.DetectAsync();

        Task ISequenceFramesCommandHost.ConfirmCollectionSelectionAsync() =>
            DuplicateDetection.ConfirmCollectionSelectionAsync();

        Task ISequenceFramesCommandHost.ResolveAllDuplicatesAsync() => DuplicateDetection.ResolveAllAsync();

        Task ISequenceFramesCommandHost.InsertBlankFrameAsync(bool before) =>
            InsertBlankSequenceFrameAsync(
                _applicationViewModel.SequenceFrames.SelectedEditorFrame,
                before ? SequenceFrameInsertPosition.Before : SequenceFrameInsertPosition.After);

        bool ISequenceFrameDuplicateDetectionHost.HasDuplicateCollectionItems =>
            _applicationViewModel.SequenceFrames.CollectionItems.Any(item => item.HasDuplicate);

        SequenceFrameItem? ISequenceFrameDuplicateDetectionHost.CollectionSelectionTarget =>
            _sequenceFrameCollectionSelectionTarget;

        IReadOnlyList<SequenceFrameCollectionItem> ISequenceFrameDuplicateDetectionHost.OrderedCollectionSelection =>
            _sequenceFrameCollectionSelectionOrder.OrderedItems;

        Task<bool> ISequenceFrameDuplicateDetectionHost.ReplaceEditorFrameWithSourcesAsync(
            SequenceFrameItem target, IReadOnlyList<string> sourcePaths) =>
            ReplaceSequenceEditorFrameWithSourcesAsync(target, sourcePaths);

        void ISequenceFrameDuplicateDetectionHost.HideSequenceFrameCollection() =>
            HideSequenceFrameCollection();

        CharacterCard? ISequenceFrameDuplicateDetectionHost.CurrentCharacter => CharacterDesk.CurrentCharacter;

        void ISequenceFrameDuplicateDetectionHost.UpdateDuplicateActionButtons() =>
            UpdateSequenceFrameDuplicateActionButtons();

        void ISequenceFrameDuplicateDetectionHost.ShowGlobalProgress(string title, string detail) =>
            ShowGlobalProgress(title, detail);

        void ISequenceFrameDuplicateDetectionHost.UpdateGlobalProgress(
            string message, double percent, string? detail, bool isIndeterminate) =>
            UpdateGlobalProgress(message, percent, detail, isIndeterminate);

        void ISequenceFrameDuplicateDetectionHost.CompleteGlobalProgress(string message, string? detail) =>
            CompleteGlobalProgress(message, detail);

        Task ISequenceFrameDuplicateDetectionHost.HideGlobalProgressAfterDelayAsync(int delayMilliseconds) =>
            HideGlobalProgressAfterDelayAsync(delayMilliseconds);

        CancellationToken ISequenceFrameDuplicateDetectionHost.GetGlobalProgressCancellationToken() =>
            GetGlobalProgressCancellationToken();

        void ISequenceFrameDuplicateDetectionHost.ShowFloatingTip(
            NotifySeverity severity, string title, string message) =>
            ((INotificationService)this).Notify(severity, title, message);

        void ISequenceFrameDuplicateDetectionHost.AppendLog(LogKind kind, string message, Exception? error) =>
            AppendLog(kind, message, error);

        // S0：St5 命令宿主——「管理」按钮的命令挂在页面级 VM 上，
        // 卡片通过 CommandParameter 传进来；壳只提供「做这件事要界面做什么」。
        /// <summary>UI 冒烟用：切到 St5 序列帧页（走壳自己的导航，不模拟点菜单）。</summary>
        internal void UiSmokeShowSt5Page() => ShowSt5SequenceFramesPage();

        void ISequenceFramesCommandHost.ShowSequenceFrameManager(SequenceFrameSection section) =>
            ShowSequenceFrameManager(section);

        void ISequenceFramesCommandHost.HideSequenceFrameManager() => HideSequenceFrameManager();

        Task ISequenceFramesCommandHost.ShowSequenceFrameCollectionAsync() => ShowSequenceFrameCollectionAsync();

        void ISequenceFramesCommandHost.CancelSequenceFrameCollectionSelection() =>
            CancelSequenceFrameCollectionMultiSelection();

        void ISequenceFramesCommandHost.HideSequenceFrameCollection() => HideSequenceFrameCollection();

        void ISequenceFramesCommandHost.HideSequenceFrameDuplicateResolver() =>
            HideSequenceFrameDuplicateResolver();

        private SequenceFrameItem? _sequenceFrameCollectionSelectionTarget;
        private bool _isSequenceFrameCollectionMultiSelecting;
        private bool _isSequenceEditorPreviewPlayback;
        private bool _isSynchronizingSequenceFrameVoiceSelection;
        private readonly SequenceFrameCollectionSelectionOrder _sequenceFrameCollectionSelectionOrder = new();

        private void RegisterSequenceFrameEditorShortcuts()
        {
            RegisterSequenceFrameEditorShortcut(Windows.System.VirtualKey.A, -1);
            RegisterSequenceFrameEditorShortcut(Windows.System.VirtualKey.Left, -1);
            RegisterSequenceFrameEditorShortcut(Windows.System.VirtualKey.D, 1);
            RegisterSequenceFrameEditorShortcut(Windows.System.VirtualKey.Right, 1);
            var playbackAccelerator = new KeyboardAccelerator
            {
                Key = Windows.System.VirtualKey.Space,
                ScopeOwner = SequenceFramesManagerHost
            };
            playbackAccelerator.Invoked += SequenceFrameEditorPlaybackKeyboardAccelerator_Invoked;
            RootGrid.KeyboardAccelerators.Add(playbackAccelerator);
        }

        private async void SequenceFrameEditorPlaybackKeyboardAccelerator_Invoked(
            KeyboardAccelerator sender,
            KeyboardAcceleratorInvokedEventArgs args)
        {
            if (SequenceFramesManagerHost.Visibility != Visibility.Visible ||
                SequenceFramesCollectionHost.Visibility == Visibility.Visible ||
                SequenceFrameDuplicateResolverHost.Visibility == Visibility.Visible ||
                IsSequenceFrameEditorInputFocused())
            {
                return;
            }

            args.Handled = true;
            await ToggleSequenceEditorPreviewAsync();
        }

        private void RegisterSequenceFrameEditorShortcut(Windows.System.VirtualKey key, int direction)
        {
            var accelerator = new KeyboardAccelerator { Key = key };
            accelerator.ScopeOwner = SequenceFramesManagerHost;
            accelerator.Invoked += (_, args) =>
                SequenceFrameEditorNavigationKeyboardAccelerator_Invoked(direction, args);
            RootGrid.KeyboardAccelerators.Add(accelerator);
        }

        private void SequenceFrameEditorNavigationKeyboardAccelerator_Invoked(
            int direction,
            KeyboardAcceleratorInvokedEventArgs args)
        {
            if (SequenceFramesManagerHost.Visibility != Visibility.Visible ||
                SequenceFramesCollectionHost.Visibility == Visibility.Visible ||
                SequenceFrameDuplicateResolverHost.Visibility == Visibility.Visible ||
                IsSequenceFrameEditorInputFocused())
            {
                return;
            }

            NavigateSequenceEditorFrame(direction);
            args.Handled = true;
        }

        private bool IsSequenceFrameEditorInputFocused()
        {
            var focusedElement = FocusManager.GetFocusedElement(RootGrid.XamlRoot);
            return focusedElement is TextBox or RichEditBox or NumberBox or ComboBox or AutoSuggestBox;
        }

        private async Task RefreshSequenceFramesAsync()
        {
            // 查看模式下仍然要能看序列，只是不能改。守卫在 ViewModel 里，
            // 界面本身保持可交互——以前是整块左栏禁用命中测试，
            // 结果把「放入右侧预览器」这个纯查看的按钮一起挡住了。
            _applicationViewModel.SequenceFrames.IsReadOnly = !CharacterDesk.CanEditCurrentCharacter;
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
                .Where(SequenceFramePool.IsSupportedImage)
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



        private async Task StartSequencePreviewAsync(bool isEditorPlayback = false)
        {
            if (_applicationViewModel.SequenceFrames.PreviewFrames.Count == 0)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "暂无预览帧", "先点击动作卡右侧的播放按钮选择序列。");
                return;
            }

            try
            {
                await PreloadSequencePreviewBitmapsAsync();
                _isSequenceEditorPreviewPlayback = isEditorPlayback;
                if (isEditorPlayback)
                {
                    _applicationViewModel.SequenceFrames.StartEditorPreview();
                }
                else
                {
                    _applicationViewModel.SequenceFrames.StartPreview();
                }

                if (!TryUpdateCurrentOrNextSequencePreviewImageSource())
                {
                    StopSequencePreview();
                    ShowFloatingTip(InfoBarSeverity.Warning, "序列帧无法播放", "没有可读取的图片，请检查文件是否缺失或损坏。");
                    return;
                }

                UpdateSequencePreviewInterval();
                PlayCurrentSequenceFrameVoice();
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

        private void ShowSequenceFrameManager(SequenceFrameSection section)
        {
            if (SequenceFramesCollectionHost.Visibility == Visibility.Visible)
            {
                HideSequenceFrameCollection();
            }

            StopSequencePreview();
            ResetSequenceEditorPreviewTransform();
            _applicationViewModel.SequenceFrames.SelectSection(section);
            _applicationViewModel.SequenceFrames.SelectSectionForManagement(section);
            UpdateSequencePreviewImageSource();
            SequenceFramesManagerHost.Visibility = Visibility.Visible;
            AnimateReferenceOverlay(SequenceFramesManagerHost, SequenceFramesManagerCardScale, show: true);
            SequenceFramesManagerHost.Focus(FocusState.Programmatic);
            SynchronizeSequenceTimelineSelectionToCurrentFrame();
            MarkLastEditedModule("SequenceFrames");
        }


        private async void HideSequenceFrameManager()
        {
            if (SequenceFramesManagerHost.Visibility != Visibility.Visible)
            {
                return;
            }

            ResetSequenceFrameCopyTargetSelection();
            await AnimateReferenceOverlayAsync(SequenceFramesManagerHost, SequenceFramesManagerCardScale, show: false);
            SequenceFramesManagerHost.Visibility = Visibility.Collapsed;
            _isReorderingSequenceFrames = false;
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
                if (_isSelectingSequenceFrameCopyTarget)
                {
                    CancelSequenceFrameCopyTargetSelection();
                    e.Handled = true;
                    return;
                }

                HideSequenceFrameManager();
                e.Handled = true;
            }
        }

        private void SequenceFramesManagerCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
        }

        private async Task ShowSequenceFrameCollectionAsync(SequenceFrameItem? selectionTarget = null)
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
                _sequenceFrameCollectionSelectionTarget = selectionTarget;
                SequenceFramesCollectionGridView.SelectionMode = selectionTarget is null
                    ? ListViewSelectionMode.None
                    : ListViewSelectionMode.Extended;
                CancelSequenceFrameCollectionMultiSelection();
                SequenceFramesCollectionTitleText.Text = selectionTarget is null ? "帧合集" : "从帧合集选择";
                SequenceFramesCollectionSelectionHintText.Visibility = selectionTarget is null
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                UpdateSequenceFrameDuplicateActionButtons();
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
            CancelSequenceFrameCollectionMultiSelection();
            SequenceFramesCollectionGridView.SelectionMode = ListViewSelectionMode.None;
            _sequenceFrameCollectionSelectionTarget = null;
            SequenceFramesCollectionTitleText.Text = "帧合集";
            SequenceFramesCollectionSelectionHintText.Visibility = Visibility.Collapsed;
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
                if (_isSequenceFrameCollectionMultiSelecting)
                {
                    CancelSequenceFrameCollectionMultiSelection();
                    e.Handled = true;
                    return;
                }

                HideSequenceFrameCollection();
                e.Handled = true;
            }
        }

        private void SequenceFramesCollectionCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
        }



        private void UpdateSequenceFrameDuplicateActionButtons()
        {
            var isBusy = (_duplicateDetection?.IsDetecting ?? false) ||
                         (_duplicateDetection?.IsResolvingAll ?? false) ||
                         _isResolvingDuplicateFrames;
            DetectSequenceFrameDuplicatesButton.IsEnabled = !isBusy;
            ResolveAllSequenceFrameDuplicatesButton.IsEnabled = !isBusy &&
                _applicationViewModel.SequenceFrames.CollectionItems.Any(item => item.HasDuplicate);
        }

        private async void SequenceFramesCollectionGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not SequenceFrameCollectionItem item)
            {
                return;
            }

            if (_sequenceFrameCollectionSelectionTarget is not null)
            {
                if (_isSequenceFrameCollectionMultiSelecting || IsSequenceFrameCollectionModifierDown())
                {
                    BeginSequenceFrameCollectionMultiSelection();
                    UpdateSequenceFrameCollectionMultiSelection();
                    return;
                }

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

        private async void SequenceFrameCollectionItem_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (_isSequenceFrameCollectionMultiSelecting ||
                IsSequenceFrameCollectionModifierDown() ||
                _sequenceFrameCollectionSelectionTarget is not { } selectionTarget ||
                sender is not FrameworkElement { DataContext: SequenceFrameCollectionItem item })
            {
                return;
            }

            e.Handled = true;
            _sequenceFrameCollectionSelectionTarget = null;
            await ReplaceSequenceEditorFrameAsync(selectionTarget, item.FilePath);
            HideSequenceFrameCollection();
        }

        private void SequenceFramesCollectionGridView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_sequenceFrameCollectionSelectionTarget is null)
            {
                return;
            }

            if (!_isSequenceFrameCollectionMultiSelecting && IsSequenceFrameCollectionModifierDown())
            {
                BeginSequenceFrameCollectionMultiSelection();
            }

            if (_isSequenceFrameCollectionMultiSelecting)
            {
                _sequenceFrameCollectionSelectionOrder.ApplySelectionChange(
                    e.AddedItems.OfType<SequenceFrameCollectionItem>().ToList(),
                    e.RemovedItems.OfType<SequenceFrameCollectionItem>().ToList(),
                    SequenceFramesCollectionGridView.SelectedItems.OfType<SequenceFrameCollectionItem>().ToList(),
                    _applicationViewModel.SequenceFrames.CollectionItems.ToList());
                UpdateSequenceFrameCollectionMultiSelection();
            }
        }

        private static bool IsSequenceFrameCollectionModifierDown()
        {
            return IsSequenceFrameCollectionKeyDown(Windows.System.VirtualKey.Control) ||
                   IsSequenceFrameCollectionKeyDown(Windows.System.VirtualKey.Shift);
        }

        private static bool IsSequenceFrameCollectionKeyDown(Windows.System.VirtualKey key)
        {
            return (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key) &
                    Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
        }

        private void BeginSequenceFrameCollectionMultiSelection()
        {
            _isSequenceFrameCollectionMultiSelecting = true;
            CancelSequenceFrameCollectionSelectionButton.Visibility = Visibility.Visible;
            ConfirmSequenceFrameCollectionSelectionButton.Visibility = Visibility.Visible;
            UpdateSequenceFrameCollectionMultiSelection();
        }

        private void UpdateSequenceFrameCollectionMultiSelection()
        {
            var count = _sequenceFrameCollectionSelectionOrder.OrderedItems.Count;
            ConfirmSequenceFrameCollectionSelectionButton.IsEnabled = count > 0;
            SequenceFramesCollectionSelectionHintText.Text = $"已选择 {count} 张；按图片角标 1 → {Math.Max(1, count)} 的顺序替换并插入。";
            SequenceFramesCollectionSelectionHintText.Visibility = Visibility.Visible;
        }

        private void CancelSequenceFrameCollectionMultiSelection()
        {
            _isSequenceFrameCollectionMultiSelecting = false;
            _sequenceFrameCollectionSelectionOrder.Clear();
            if (SequenceFramesCollectionGridView.SelectionMode != ListViewSelectionMode.None)
            {
                SequenceFramesCollectionGridView.SelectedItems.Clear();
            }

            CancelSequenceFrameCollectionSelectionButton.Visibility = Visibility.Collapsed;
            ConfirmSequenceFrameCollectionSelectionButton.Visibility = Visibility.Collapsed;
            ConfirmSequenceFrameCollectionSelectionButton.IsEnabled = false;
            SequenceFramesCollectionSelectionHintText.Text = "点击一张图片，将其设为当前帧素材；按住 Ctrl 或 Shift 点击可多选。";
            SequenceFramesCollectionSelectionHintText.Visibility = _sequenceFrameCollectionSelectionTarget is null
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private async void ConfirmSequenceFrameCollectionSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_sequenceFrameCollectionSelectionTarget is not { } selectionTarget)
            {
                return;
            }

            var selectedItems = _sequenceFrameCollectionSelectionOrder.OrderedItems.ToList();
            if (selectedItems.Count == 0)
            {
                return;
            }

            if (await ReplaceSequenceEditorFrameWithSourcesAsync(
                    selectionTarget,
                    selectedItems.Select(item => item.FilePath).ToList()))
            {
                HideSequenceFrameCollection();
            }
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
                UpdateSequenceFrameDuplicateActionButtons();
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
                await _applicationViewModel.SequenceFrames.ReorderFramesAsync(CharacterDesk.CurrentCharacter, section, orderedFrames);
                UpdateSequencePreviewImageSource();
                UpdateSequencePreviewInterval();
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

        private async Task InsertBlankSequenceFrameAsync(SequenceFrameItem? frame, SequenceFrameInsertPosition position)
        {
            // S3/S1：帧一律由**调用方**决定（命令传当前选中帧，右键菜单传被点的那一帧）。
            // 以前这里还有个 `object sender` 分支，要从 MenuFlyoutItem.Tag 反推——
            // 那条路径删掉之后，"菜单项插到序列末尾"这种坑就不存在了。
            if (CharacterDesk.CurrentCharacter is null ||
                _applicationViewModel.SequenceFrames.SelectedSection is not { } section)
            {
                return;
            }

            try
            {
                var snapshot = await _applicationViewModel.SequenceFrames.CreateSectionSnapshotAsync(CharacterDesk.CurrentCharacter, section);
                StopSequencePreview();
                ClearSequencePreviewCache();
                await _applicationViewModel.SequenceFrames.InsertBlankFrameAsync(
                    CharacterDesk.CurrentCharacter,
                    section,
                    frame,
                    position);
                SynchronizeSequenceTimelineSelectionToCurrentFrame();
                var directionText = position == SequenceFrameInsertPosition.Before ? "左侧" : "右侧";
                RecordSequenceFrameOperation(
                    "插入空白序列帧",
                    $"{section.Action.DisplayName} / {frame?.DisplayName ?? "序列末尾"} / {directionText}",
                    section,
                    snapshot);
                ShowFloatingTip(InfoBarSeverity.Success, "空白帧已插入", section.Action.DisplayName);
                AppendLog(LogKind.User, $"插入空白序列帧：{section.Action.DisplayName} / {directionText}={frame?.DisplayName ?? "序列末尾"}");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "空白帧插入失败", ex.Message);
                AppendLog(LogKind.Error, "空白帧插入失败。", ex);
            }
        }

        private async void SequenceFrameTimelineListView_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (_isSelectingSequenceFrameCopyTarget)
            {
                e.Handled = true;
                return;
            }

            if (e.Key != Windows.System.VirtualKey.Delete)
            {
                return;
            }

            var selectedFrames = GetSelectedSequenceFrames();
            if (selectedFrames.Count > 1)
            {
                e.Handled = true;
                await DeleteSelectedSequenceFramesAsync(selectedFrames);
                return;
            }

            if (
                _applicationViewModel.SequenceFrames.SelectedEditorFrame is not { } frame ||
                _applicationViewModel.SequenceFrames.SelectedSection is not { } section)
            {
                return;
            }

            e.Handled = true;
            await DeleteSequenceFrameAsync(frame, section);
        }

        private void SequenceFrameTimelineListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSynchronizingSequenceFrameSelection || _isSelectingSequenceFrameCopyTarget)
            {
                return;
            }

            var selectedFrames = GetSelectedSequenceFrames();
            var frameToView = e.AddedItems
                .OfType<SequenceFrameItem>()
                .LastOrDefault();
            if (frameToView is null &&
                selectedFrames.Count > 0 &&
                (_applicationViewModel.SequenceFrames.SelectedEditorFrame is not { } currentFrame ||
                 !selectedFrames.Contains(currentFrame)))
            {
                frameToView = selectedFrames[^1];
            }

            if (frameToView is not null)
            {
                StopSequencePreview();
                _applicationViewModel.SequenceFrames.SelectEditorFrame(frameToView);
                SynchronizeSequenceFrameVoiceSelection();
                TryUpdateSequencePreviewImageSource();
                UpdateSequencePreviewInterval();
                PlayCurrentSequenceFrameVoice();
            }

            UpdateSequenceFrameSelectionPresentation(selectedFrames);
        }

        private async void SequenceFrameTimelineListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (_isSelectingSequenceFrameCopyTarget && e.ClickedItem is SequenceFrameItem afterFrame)
            {
                await DuplicatePendingSequenceFramesAfterAsync(afterFrame);
            }
        }

        private void SynchronizeSequenceTimelineSelectionToCurrentFrame()
        {
            SynchronizeSequenceFrameVoiceSelection();
            var selectedFrames = GetSelectedSequenceFrames();
            if (selectedFrames.Count > 1)
            {
                UpdateSequenceFrameSelectionPresentation(selectedFrames);
                return;
            }

            var currentFrame = _applicationViewModel.SequenceFrames.SelectedEditorFrame;
            if (currentFrame is not null)
            {
                _isSynchronizingSequenceFrameSelection = true;
                try
                {
                    SequenceFrameTimelineListView.SelectedItem = currentFrame;
                    SequenceFrameTimelineListView.ScrollIntoView(currentFrame);
                }
                finally
                {
                    _isSynchronizingSequenceFrameSelection = false;
                }
            }

            UpdateSequenceFrameSelectionPresentation(GetSelectedSequenceFrames());
        }

        private void UpdateSequenceFrameSelectionPresentation(IReadOnlyList<SequenceFrameItem> selectedFrames)
        {
            var selectionCount = selectedFrames.Count;
            var isMultiSelection = selectionCount > 1;
            SequenceSingleFramePanel.Visibility = isMultiSelection ? Visibility.Collapsed : Visibility.Visible;
            SequenceMultiFramePanel.Visibility = isMultiSelection ? Visibility.Visible : Visibility.Collapsed;
            SequenceEditorSingleFrameText.Visibility = isMultiSelection ? Visibility.Collapsed : Visibility.Visible;
            SequenceEditorMultiSelectionText.Visibility = isMultiSelection ? Visibility.Visible : Visibility.Collapsed;
            if (!isMultiSelection)
            {
                return;
            }

            var currentIndex = _applicationViewModel.SequenceFrames.SelectedEditorFrame?.Index
                ?? selectedFrames[^1].Index;
            SequenceEditorMultiSelectionText.Text = $"已选择 {selectionCount} 帧｜当前查看第 {currentIndex} 帧";
            SequenceMultiSelectionSummaryText.Text = $"已选择 {selectionCount} 帧，当前查看第 {currentIndex} 帧。";
            SequenceMultiSelectionFramesText.Text = $"帧编号：{string.Join("、", selectedFrames.Select(frame => frame.Index))}";
            SequenceMultiSelectionDurationText.Text = $"总持续帧格：{selectedFrames.Sum(frame => frame.DurationFrames)} 格";
            SequenceMultiSelectionVoiceText.Text = $"带语音标记：{selectedFrames.Count(frame => frame.HasVoice)} 帧";
        }

        private IReadOnlyList<SequenceFrameItem> GetSelectedSequenceFrames()
        {
            return SequenceFrameTimelineListView.SelectedItems
                .OfType<SequenceFrameItem>()
                .OrderBy(frame => frame.Index)
                .ToList();
        }

        private void SelectSequenceTimelineFrames(IEnumerable<int> frameIndexes)
        {
            var indexes = frameIndexes.ToHashSet();
            var frames = _applicationViewModel.SequenceFrames.SelectedSectionFrames
                .Where(frame => indexes.Contains(frame.Index))
                .OrderBy(frame => frame.Index)
                .ToList();
            _isSynchronizingSequenceFrameSelection = true;
            try
            {
                SequenceFrameTimelineListView.SelectedItems.Clear();
                foreach (var frame in frames)
                {
                    SequenceFrameTimelineListView.SelectedItems.Add(frame);
                }
            }
            finally
            {
                _isSynchronizingSequenceFrameSelection = false;
            }

            if (frames.Count > 0)
            {
                var frameToView = frames[^1];
                _applicationViewModel.SequenceFrames.SelectEditorFrame(frameToView);
                SynchronizeSequenceFrameVoiceSelection();
                TryUpdateSequencePreviewImageSource();
                SequenceFrameTimelineListView.ScrollIntoView(frameToView);
            }

            UpdateSequenceFrameSelectionPresentation(frames);
        }



        private void StartSequenceFrameCopyTargetSelection(IReadOnlyList<SequenceFrameItem> selectedFrames)
        {
            if (selectedFrames.Count < 2)
            {
                return;
            }

            _pendingSequenceFramesToDuplicate = selectedFrames.ToList();
            _isSelectingSequenceFrameCopyTarget = true;
            DuplicateSelectedSequenceFramesButton.Content = "取消选择位置";
            DeleteSelectedSequenceFramesButton.IsEnabled = false;
            SequenceTimelineModeText.Text = "点击下方帧格选择复制位置";
            SequenceFrameTimelineListView.CanDragItems = false;
            SequenceFrameTimelineListView.CanReorderItems = false;
        }

        private void CancelSequenceFrameCopyTargetSelection()
        {
            if (!_isSelectingSequenceFrameCopyTarget)
            {
                return;
            }

            var sourceIndexes = _pendingSequenceFramesToDuplicate.Select(frame => frame.Index).ToList();
            ResetSequenceFrameCopyTargetSelection();
            _pendingSequenceFramesToDuplicate = [];
            SelectSequenceTimelineFrames(sourceIndexes);
        }

        private void ResetSequenceFrameCopyTargetSelection()
        {
            _isSelectingSequenceFrameCopyTarget = false;
            DuplicateSelectedSequenceFramesButton.Content = "复制所选帧";
            DeleteSelectedSequenceFramesButton.IsEnabled = true;
            SequenceTimelineModeText.Text = "拖动帧块排序";
            SequenceFrameTimelineListView.CanDragItems = true;
            SequenceFrameTimelineListView.CanReorderItems = true;
        }

        private async Task DuplicatePendingSequenceFramesAfterAsync(SequenceFrameItem afterFrame)
        {
            if (!_isSelectingSequenceFrameCopyTarget)
            {
                return;
            }

            var selectedFrames = _pendingSequenceFramesToDuplicate.ToList();
            await DuplicateSelectedSequenceFramesAsync(selectedFrames, afterFrame);
        }

        private async Task DuplicateSelectedSequenceFramesAsync(
            IReadOnlyList<SequenceFrameItem> selectedFrames,
            SequenceFrameItem afterFrame)
        {
            if (selectedFrames.Count < 2 ||
                CharacterDesk.CurrentCharacter is null ||
                _applicationViewModel.SequenceFrames.SelectedSection is not { } section)
            {
                CancelSequenceFrameCopyTargetSelection();
                return;
            }

            try
            {
                var snapshot = await _applicationViewModel.SequenceFrames.CreateSectionSnapshotAsync(CharacterDesk.CurrentCharacter, section);
                StopSequencePreview();
                await _applicationViewModel.SequenceFrames.DuplicateFramesAsync(
                    CharacterDesk.CurrentCharacter,
                    section,
                    selectedFrames,
                    afterFrame);
                ResetSequenceFrameCopyTargetSelection();
                _pendingSequenceFramesToDuplicate = [];
                UpdateSequencePreviewImageSource();
                UpdateSequencePreviewInterval();
                SelectSequenceTimelineFrames(Enumerable.Range(afterFrame.Index + 1, selectedFrames.Count));
                RecordSequenceFrameOperation(
                    "批量复制序列帧",
                    $"{section.Action.DisplayName} / {selectedFrames.Count} 帧 / 第 {afterFrame.Index} 帧后",
                    section,
                    snapshot);
                ShowFloatingTip(InfoBarSeverity.Success, "序列帧已批量复制", $"已在第 {afterFrame.Index} 帧后插入 {selectedFrames.Count} 帧。");
                AppendLog(LogKind.User, $"批量复制序列帧：{section.Action.DisplayName} / {selectedFrames.Count} 帧 / After={afterFrame.Index}");
            }
            catch (Exception ex)
            {
                var sourceIndexes = selectedFrames.Select(frame => frame.Index).ToList();
                ResetSequenceFrameCopyTargetSelection();
                _pendingSequenceFramesToDuplicate = [];
                SelectSequenceTimelineFrames(sourceIndexes);
                ShowFloatingTip(InfoBarSeverity.Error, "序列帧批量复制失败", ex.Message);
                AppendLog(LogKind.Error, "序列帧批量复制失败。", ex);
            }
        }







        private async Task ReplaceSequenceEditorFrameAsync(SequenceFrameItem frame)
        {
            if (CharacterDesk.CurrentCharacter is null ||
                _applicationViewModel.SequenceFrames.SelectedSection is not { } section)
            {
                return;
            }

            var sourcePath = await PickSequenceFrameImageAsync();
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return;
            }

            await ReplaceSequenceEditorFrameAsync(frame, sourcePath);
        }


        private async Task ReplaceSequenceEditorFrameAsync(SequenceFrameItem frame, string sourcePath)
        {
            // S3：流程在 SequenceFrameEditorController.ReplaceFrameAsync；
            // 这里保留签名，菜单项与编辑器按钮的既有调用点都不用改。
            await SequenceFrameEditor.ReplaceFrameAsync(frame, sourcePath);
        }

        private async Task<bool> ReplaceSequenceEditorFrameWithSourcesAsync(
            SequenceFrameItem frame,
            IReadOnlyList<string> sourcePaths)
        {
            // S3：流程在 SequenceFrameEditorController.ReplaceFrameWithSourcesAsync；
            // 这里保留签名，帧合集「确认批量替换」那条链的调用点不用改。
            return await SequenceFrameEditor.ReplaceFrameWithSourcesAsync(frame, sourcePaths);
        }

        private async void SequenceFrameDurationStepper_ValueChanged(object sender, NumberBoxValueChangedEventArgs args)
        {
            if (sender is not NumberBox stepper ||
                CharacterDesk.CurrentCharacter is null ||
                _applicationViewModel.SequenceFrames.SelectedSection is not { } section ||
                _applicationViewModel.SequenceFrames.SelectedEditorFrame is not { } frame ||
                double.IsNaN(stepper.Value))
            {
                return;
            }

            var durationFrames = Math.Clamp((int)Math.Round(stepper.Value), 1, SequenceFrameService.MaxFrameDuration);
            if (durationFrames == frame.DurationFrames)
            {
                return;
            }

            try
            {
                StopSequencePreview();
                await _applicationViewModel.SequenceFrames.SetFrameDurationAsync(
                    CharacterDesk.CurrentCharacter,
                    section,
                    frame,
                    durationFrames);
                UpdateSequencePreviewImageSource();
                MarkSequenceFramesEdited();
                AppendLog(LogKind.User, $"设置序列帧时长：{section.Action.DisplayName} / #{frame.Index} / {durationFrames} 格");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "帧时长保存失败", ex.Message);
                AppendLog(LogKind.Error, "序列帧时长保存失败。", ex);
            }
        }

        private async void SequenceFrameVoiceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSynchronizingSequenceFrameVoiceSelection ||
                CharacterDesk.CurrentCharacter is null ||
                _applicationViewModel.SequenceFrames.SelectedSection is not { } section ||
                _applicationViewModel.SequenceFrames.SelectedEditorFrame is not { } frame ||
                sender is not ComboBox { SelectedItem: SequenceFrameVoiceOption option } ||
                string.Equals(frame.VoiceFilePath, option.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                StopSequencePreview();
                await _applicationViewModel.SequenceFrames.SetFrameVoiceAsync(
                    CharacterDesk.CurrentCharacter,
                    section,
                    frame,
                    option.FilePath);
                UpdateSequencePreviewImageSource();
                MarkSequenceFramesEdited();
                if (!string.IsNullOrWhiteSpace(option.FilePath))
                {
                    PlayVoiceFile(option.FilePath);
                }

                AppendLog(LogKind.User, $"设置序列帧语音：{section.Action.DisplayName} / #{frame.Index} / {option.DisplayName}");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "帧语音保存失败", ex.Message);
                AppendLog(LogKind.Error, "序列帧语音绑定失败。", ex);
            }
        }

        private void SynchronizeSequenceFrameVoiceSelection()
        {
            var selectedPath = _applicationViewModel.SequenceFrames.SelectedEditorVoicePath;
            var selectedOption = _applicationViewModel.SequenceFrames.AvailableVoices.FirstOrDefault(option =>
                string.Equals(option.FilePath, selectedPath, StringComparison.OrdinalIgnoreCase));
            _isSynchronizingSequenceFrameVoiceSelection = true;
            try
            {
                SequenceFrameVoiceComboBox.SelectedItem = selectedOption;
            }
            finally
            {
                _isSynchronizingSequenceFrameVoiceSelection = false;
            }
        }

        private SequenceFrameItem? ResolveSequenceFrameCommandFrame(object sender)
        {
            return (sender as FrameworkElement)?.Tag as SequenceFrameItem
                ?? _applicationViewModel.SequenceFrames.SelectedEditorFrame;
        }

        /// <summary>预览区（外层）的播放 / 暂停（S5 收尾：由命令调用，不再是 Click 处理器）。</summary>
        private async Task ToggleSequencePreviewPlaybackAsync()
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

        private async Task ToggleSequenceEditorPreviewAsync()
        {
            if (_applicationViewModel.SequenceFrames.PreviewFrames.Count == 0)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "暂无预览帧", "当前序列还没有可播放的帧。");
                return;
            }

            if (_applicationViewModel.SequenceFrames.IsPreviewing)
            {
                StopSequencePreview();
                return;
            }

            await StartSequencePreviewAsync(isEditorPlayback: true);
        }

        private void SequencePreviewFpsNumberBox_ValueChanged(object sender, NumberBoxValueChangedEventArgs e)
        {
            if (SequenceFramesPage.Visibility == Visibility.Visible)
            {
                MarkLastEditedModule("SequenceFrames");
            }

            if (_applicationViewModel.SequenceFrames.IsPreviewing)
            {
                UpdateSequencePreviewInterval();
            }
        }

        private void NavigateSequenceEditorFrame(int direction)
        {
            StopSequencePreview();
            _applicationViewModel.SequenceFrames.StepPreviewFrame(direction);
            TryUpdateSequencePreviewImageSource();
            SynchronizeSequenceTimelineSelectionToCurrentFrame();
            UpdateSequencePreviewInterval();
            PlayCurrentSequenceFrameVoice();
        }

        private void SequencePreviewTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            if (_isSequenceEditorPreviewPlayback)
            {
                if (!_applicationViewModel.SequenceFrames.AdvanceEditorPreviewFrame())
                {
                    StopSequencePreview();
                    return;
                }
            }
            else
            {
                _applicationViewModel.SequenceFrames.AdvancePreviewFrame();
            }

            if (!TryUpdateCurrentOrNextSequencePreviewImageSource())
            {
                StopSequencePreview();
                return;
            }

            UpdateSequencePreviewInterval();
            PlayCurrentSequenceFrameVoice();
            SynchronizeSequenceTimelineSelectionToCurrentFrame();

            if (!_applicationViewModel.SequenceFrames.IsPreviewing)
            {
                sender.Stop();
            }
        }

        private void StopSequencePreview()
        {
            _sequencePreviewTimer.Stop();
            _isSequenceEditorPreviewPlayback = false;
            _applicationViewModel.SequenceFrames.StopPreview();
        }

        private void UpdateSequencePreviewInterval()
        {
            var fps = Math.Clamp(_applicationViewModel.SequenceFrames.PreviewFps, 1, 60);
            var durationFrames = Math.Clamp(
                _applicationViewModel.SequenceFrames.CurrentPreviewFrame?.DurationFrames ?? 1,
                1,
                SequenceFrameService.MaxFrameDuration);
            _sequencePreviewTimer.Interval = TimeSpan.FromMilliseconds(1000d * durationFrames / fps);
        }

        private void PlayCurrentSequenceFrameVoice()
        {
            var voiceFilePath = _applicationViewModel.SequenceFrames.CurrentPreviewFrame?.VoiceFilePath;
            if (!string.IsNullOrWhiteSpace(voiceFilePath) && File.Exists(voiceFilePath))
            {
                PlayVoiceFile(voiceFilePath);
            }
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
            SynchronizeSequenceTimelineSelectionToCurrentFrame();
        }

        private bool TryUpdateCurrentOrNextSequencePreviewImageSource()
        {
            var frameCount = _applicationViewModel.SequenceFrames.PreviewFrames.Count;
            if (frameCount == 0)
            {
                ShowSequencePreviewSource(null);
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

            ShowSequencePreviewSource(null);
            return false;
        }

        private bool TryUpdateSequencePreviewImageSource()
        {
            var path = _applicationViewModel.SequenceFrames.CurrentFrameFilePath;
            var cacheKey = _applicationViewModel.SequenceFrames.CurrentFrameCacheKey;
            var uri = _applicationViewModel.SequenceFrames.CurrentFrameUri;
            if (string.IsNullOrWhiteSpace(path))
            {
                ShowSequencePreviewSource(null);
                var isBlank = _applicationViewModel.SequenceFrames.PreviewFrames
                    .FirstOrDefault(frame => frame.CacheKey == cacheKey)
                    ?.IsBlank == true;
                if (isBlank)
                {
                    PrepareNextSequencePreviewSource();
                }

                return isBlank;
            }

            if (_sequencePreviewBitmapCache.IsFailed(path))
            {
                ShowSequencePreviewSource(null);
                return false;
            }

            if (!string.IsNullOrWhiteSpace(cacheKey) &&
                _sequencePreviewBitmapCache.TryGet(cacheKey, out var bitmap))
            {
                ShowSequencePreviewSource(bitmap);
                PrepareNextSequencePreviewSource();
                return true;
            }

            try
            {
                if (!File.Exists(path))
                {
                    _sequencePreviewBitmapCache.MarkFailed(path);
                    ShowSequencePreviewSource(null);
                    return false;
                }

                var loadedBitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(
                    new Uri(string.IsNullOrWhiteSpace(uri) ? path : uri, UriKind.Absolute));
                _sequencePreviewBitmapCache.Store(cacheKey, loadedBitmap);
                ShowSequencePreviewSource(loadedBitmap);
                PrepareNextSequencePreviewSource();
                return true;
            }
            catch (Exception ex)
            {
                _sequencePreviewBitmapCache.MarkFailed(path);
                ShowSequencePreviewSource(null);
                ShowFloatingTip(InfoBarSeverity.Error, "序列帧显示失败", ex.Message);
                AppendLog(LogKind.Error, "序列帧显示失败。", ex);
                return false;
            }
        }

        private void ClearSequencePreviewCache()
        {
            _sequencePreviewBitmapCache.Clear();
            ShowSequencePreviewSource(null);
        }

        private void ShowSequencePreviewSource(Microsoft.UI.Xaml.Media.ImageSource? source)
        {
            SequencePreviewPresenter.Show(source);
            SequenceEditorPreviewPresenter.Show(source);
        }

        private void PrepareNextSequencePreviewSource()
        {
            var frames = _applicationViewModel.SequenceFrames.PreviewFrames;
            var currentFrame = _applicationViewModel.SequenceFrames.CurrentPreviewFrame;
            if (frames.Count == 0 || currentFrame is null)
            {
                return;
            }

            var currentIndex = frames.IndexOf(currentFrame);
            if (currentIndex < 0)
            {
                return;
            }

            var nextFrame = frames[(currentIndex + 1) % frames.Count];
            Microsoft.UI.Xaml.Media.ImageSource? nextSource = null;
            if (!nextFrame.IsBlank &&
                !_sequencePreviewBitmapCache.TryGet(nextFrame.CacheKey, out nextSource))
            {
                return;
            }

            SequencePreviewPresenter.Prepare(nextSource);
            SequenceEditorPreviewPresenter.Prepare(nextSource);
        }

        private void SequencePreviewCanvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            ZoomSequencePreview(
                SequencePreviewCanvas,
                SequencePreviewImageTransform,
                ref _sequencePreviewScale,
                e);
        }

        private void SequencePreviewCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            StartSequencePreviewPan(
                SequencePreviewCanvas,
                ref _isPanningSequencePreview,
                ref _lastSequencePreviewPointerPosition,
                e);
        }

        private void SequencePreviewCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            MoveSequencePreviewPan(
                SequencePreviewCanvas,
                SequencePreviewImageTransform,
                ref _isPanningSequencePreview,
                ref _lastSequencePreviewPointerPosition,
                e);
        }

        private void SequencePreviewCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            EndSequencePreviewPan(SequencePreviewCanvas, ref _isPanningSequencePreview, e);
        }

        private void SequencePreviewCanvas_PointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            EndSequencePreviewPan(SequencePreviewCanvas, ref _isPanningSequencePreview, e);
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

        private void SequenceEditorPreviewCanvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            ZoomSequencePreview(
                SequenceEditorPreviewCanvas,
                SequenceEditorPreviewImageTransform,
                ref _sequenceEditorPreviewScale,
                e);
        }

        private void SequenceEditorPreviewCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            StartSequencePreviewPan(
                SequenceEditorPreviewCanvas,
                ref _isPanningSequenceEditorPreview,
                ref _lastSequenceEditorPreviewPointerPosition,
                e);
        }

        private void SequenceEditorPreviewCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            MoveSequencePreviewPan(
                SequenceEditorPreviewCanvas,
                SequenceEditorPreviewImageTransform,
                ref _isPanningSequenceEditorPreview,
                ref _lastSequenceEditorPreviewPointerPosition,
                e);
        }

        private void SequenceEditorPreviewCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            EndSequencePreviewPan(SequenceEditorPreviewCanvas, ref _isPanningSequenceEditorPreview, e);
        }

        private void SequenceEditorPreviewCanvas_PointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            EndSequencePreviewPan(SequenceEditorPreviewCanvas, ref _isPanningSequenceEditorPreview, e);
        }

        private void SequenceEditorPreviewCanvas_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            _isPanningSequenceEditorPreview = false;
        }

        private void SequenceEditorPreviewCanvas_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            ResetSequenceEditorPreviewTransform();
            e.Handled = true;
        }

        private static void ZoomSequencePreview(
            Grid canvas,
            CompositeTransform transform,
            ref double scale,
            PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(canvas);
            var previousScale = scale;
            scale = Math.Clamp(
                scale * (point.Properties.MouseWheelDelta > 0 ? 1.1 : 0.9),
                0.2,
                8);
            var actualZoomFactor = scale / previousScale;
            var pointerOffsetX = point.Position.X - canvas.ActualWidth / 2 - transform.TranslateX;
            var pointerOffsetY = point.Position.Y - canvas.ActualHeight / 2 - transform.TranslateY;
            transform.TranslateX -= pointerOffsetX * (actualZoomFactor - 1);
            transform.TranslateY -= pointerOffsetY * (actualZoomFactor - 1);
            transform.ScaleX = scale;
            transform.ScaleY = scale;
            e.Handled = true;
        }

        private static void StartSequencePreviewPan(
            Grid canvas,
            ref bool isPanning,
            ref Windows.Foundation.Point lastPointerPosition,
            PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(canvas);
            if (!point.Properties.IsLeftButtonPressed)
            {
                return;
            }

            isPanning = true;
            lastPointerPosition = point.Position;
            canvas.CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private static void MoveSequencePreviewPan(
            Grid canvas,
            CompositeTransform transform,
            ref bool isPanning,
            ref Windows.Foundation.Point lastPointerPosition,
            PointerRoutedEventArgs e)
        {
            if (!isPanning)
            {
                return;
            }

            var point = e.GetCurrentPoint(canvas);
            transform.TranslateX += point.Position.X - lastPointerPosition.X;
            transform.TranslateY += point.Position.Y - lastPointerPosition.Y;
            lastPointerPosition = point.Position;
            e.Handled = true;
        }

        private static void EndSequencePreviewPan(Grid canvas, ref bool isPanning, PointerRoutedEventArgs e)
        {
            isPanning = false;
            canvas.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        }

        private void ResetSequencePreviewTransform()
        {
            ResetSequencePreviewTransform(SequencePreviewImageTransform, ref _sequencePreviewScale);
        }

        private void ResetSequenceEditorPreviewTransform()
        {
            ResetSequencePreviewTransform(SequenceEditorPreviewImageTransform, ref _sequenceEditorPreviewScale);
        }

        private static void ResetSequencePreviewTransform(CompositeTransform transform, ref double scale)
        {
            scale = 1;
            transform.ScaleX = 1;
            transform.ScaleY = 1;
            transform.TranslateX = 0;
            transform.TranslateY = 0;
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
            return (await _filePickerService.PickMultipleFilesAsync(
                    PickerLocationId.PicturesLibrary, ".png", ".jpg", ".jpeg", ".webp", ".bmp"))
                .ToList();
        }

        private async Task<string?> PickSequenceFrameImageAsync()
        {
            return await _filePickerService.PickSingleFileAsync(
                PickerLocationId.PicturesLibrary, ".png", ".jpg", ".jpeg", ".webp", ".bmp");
        }
    }
}
