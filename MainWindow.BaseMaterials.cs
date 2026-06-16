using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace CrossingVoidZDTool
{
    public sealed partial class MainWindow
    {
        private async Task RefreshBaseMaterialsAsync()
        {
            await _applicationViewModel.LineArt.RefreshAsync(CharacterDesk.CurrentCharacter);
        }

        private void StartBaseMaterialWatcher()
        {
            if (CharacterDesk.CurrentCharacter is null)
            {
                StopBaseMaterialWatcher();
                return;
            }

            var root = Path.Combine(CharacterDesk.CurrentCharacter.FolderPath, "AssetMaterial");
            Directory.CreateDirectory(root);
            if (string.Equals(_watchedBaseMaterialRoot, root, StringComparison.OrdinalIgnoreCase) &&
                _baseMaterialWatcher is not null)
            {
                return;
            }

            StopBaseMaterialWatcher();
            _watchedBaseMaterialRoot = root;
            _baseMaterialWatcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.DirectoryName
            };
            _baseMaterialWatcher.Created += BaseMaterialFolder_Changed;
            _baseMaterialWatcher.Changed += BaseMaterialFolder_Changed;
            _baseMaterialWatcher.Deleted += BaseMaterialFolder_Changed;
            _baseMaterialWatcher.Renamed += BaseMaterialFolder_Changed;
            _baseMaterialWatcher.EnableRaisingEvents = true;
        }

        private void StopBaseMaterialWatcher()
        {
            _baseMaterialRefreshTimer.Stop();
            if (_baseMaterialWatcher is null)
            {
                return;
            }

            _baseMaterialWatcher.EnableRaisingEvents = false;
            _baseMaterialWatcher.Created -= BaseMaterialFolder_Changed;
            _baseMaterialWatcher.Changed -= BaseMaterialFolder_Changed;
            _baseMaterialWatcher.Deleted -= BaseMaterialFolder_Changed;
            _baseMaterialWatcher.Renamed -= BaseMaterialFolder_Changed;
            _baseMaterialWatcher.Dispose();
            _baseMaterialWatcher = null;
            _watchedBaseMaterialRoot = null;
        }

        private void BaseMaterialFolder_Changed(object sender, FileSystemEventArgs e)
        {
            if (!DispatcherQueue.TryEnqueue(() =>
                {
                    if (LineArtPage.Visibility != Visibility.Visible ||
                        _baseMaterialInternalWriteDepth > 0)
                    {
                        return;
                    }

                    _baseMaterialRefreshTimer.Stop();
                    _baseMaterialRefreshTimer.Start();
                }))
            {
                // The window is closing; the watcher will be disposed with the process.
            }
        }

        private async void BaseMaterialRefreshTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            sender.Stop();
            if (LineArtPage.Visibility != Visibility.Visible)
            {
                return;
            }

            await RefreshBaseMaterialsWithFeedbackAsync();
        }

        private async Task RefreshBaseMaterialsWithFeedbackAsync()
        {
            var scrollOffset = LineArtScrollViewer.VerticalOffset;
            try
            {
                await RefreshBaseMaterialsAsync();
                await RestoreBaseMaterialScrollOffsetAsync(scrollOffset);
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "基础素材检查失败", ex.Message);
                AppendLog(LogKind.Error, "基础素材检查失败。", ex);
            }
        }

        private async Task RestoreBaseMaterialScrollOffsetAsync(double scrollOffset)
        {
            await Task.Yield();
            LineArtScrollViewer.UpdateLayout();
            LineArtScrollViewer.ChangeView(
                null,
                Math.Min(scrollOffset, LineArtScrollViewer.ScrollableHeight),
                null,
                true);
        }

        private async Task RunBaseMaterialInternalWriteAsync(Func<Task> writeAction)
        {
            _baseMaterialRefreshTimer.Stop();
            _baseMaterialInternalWriteDepth++;
            try
            {
                await writeAction();
            }
            finally
            {
                await Task.Delay(550);
                _baseMaterialRefreshTimer.Stop();
                _baseMaterialInternalWriteDepth = Math.Max(0, _baseMaterialInternalWriteDepth - 1);
            }
        }

        private async void AddBaseMaterialButton_Click(object sender, RoutedEventArgs e)
        {
            if (CharacterDesk.CurrentCharacter is null ||
                sender is not Button { Tag: BaseMaterialKind kind })
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择角色", "请先在角色台选择当前制作角色。");
                return;
            }

            var sourcePath = await PickImageAsync();
            if (sourcePath is null)
            {
                return;
            }

            try
            {
                var scrollOffset = LineArtScrollViewer.VerticalOffset;
                var crop = await GetOptionalBaseMaterialCropAsync(kind, sourcePath);
                if (crop is null && NeedsManualCrop(kind, sourcePath))
                {
                    return;
                }

                if (crop is { } cropRectangle)
                {
                    await RunBaseMaterialInternalWriteAsync(() =>
                        _applicationViewModel.LineArt.ImportWithCropAsync(CharacterDesk.CurrentCharacter, kind, sourcePath, cropRectangle));
                }
                else
                {
                    await RunBaseMaterialInternalWriteAsync(() =>
                        _applicationViewModel.LineArt.ImportAsync(CharacterDesk.CurrentCharacter, kind, sourcePath));
                }
                await RestoreBaseMaterialScrollOffsetAsync(scrollOffset);
                MarkLastEditedModule("LineArt");
                ShowFloatingTip(InfoBarSeverity.Success, "基础素材已导入", BaseMaterialService.GetSpec(kind).DisplayName);
                AppendLog(LogKind.User, $"导入基础素材：{BaseMaterialService.GetSpec(kind).DisplayName} <- {Path.GetFileName(sourcePath)}");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "基础素材导入失败", ex.Message);
                AppendLog(LogKind.Error, "基础素材导入失败。", ex);
            }
        }

        private async void RepairBaseMaterialMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (CharacterDesk.CurrentCharacter is null ||
                sender is not MenuFlyoutItem { Tag: BaseMaterialItem item })
            {
                return;
            }

            var sourcePath = item.FilePath;
            if (!File.Exists(sourcePath))
            {
                ShowFloatingTip(InfoBarSeverity.Error, "基础素材修复失败", "当前文件夹里没有找到这张素材。");
                await RefreshBaseMaterialsWithFeedbackAsync();
                return;
            }

            try
            {
                var scrollOffset = LineArtScrollViewer.VerticalOffset;
                var crop = await ShowBaseMaterialCropDialogAsync(item.Kind, sourcePath);
                if (crop is not { } cropRectangle)
                {
                    return;
                }

                await RunBaseMaterialInternalWriteAsync(() =>
                    _applicationViewModel.LineArt.RepairWithCropAsync(CharacterDesk.CurrentCharacter, item.Kind, sourcePath, item.Index, cropRectangle));
                await RestoreBaseMaterialScrollOffsetAsync(scrollOffset);
                MarkLastEditedModule("LineArt");
                ShowFloatingTip(InfoBarSeverity.Success, "基础素材已修复", item.DisplayName);
                AppendLog(LogKind.User, $"修复并重新裁剪基础素材：{item.DisplayName} #{item.Index} <- {Path.GetFileName(sourcePath)}");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "基础素材修复失败", ex.Message);
                AppendLog(LogKind.Error, "基础素材修复失败。", ex);
            }
        }

        private async void ReimportBaseMaterialMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (CharacterDesk.CurrentCharacter is null ||
                sender is not MenuFlyoutItem { Tag: BaseMaterialItem item })
            {
                return;
            }

            var sourcePath = await PickImageAsync();
            if (sourcePath is null)
            {
                return;
            }

            try
            {
                var scrollOffset = LineArtScrollViewer.VerticalOffset;
                var crop = await GetOptionalBaseMaterialCropAsync(item.Kind, sourcePath);
                if (crop is null && NeedsManualCrop(item.Kind, sourcePath))
                {
                    return;
                }

                if (crop is { } cropRectangle)
                {
                    await RunBaseMaterialInternalWriteAsync(() =>
                        _applicationViewModel.LineArt.RepairWithCropAsync(CharacterDesk.CurrentCharacter, item.Kind, sourcePath, item.Index, cropRectangle));
                }
                else
                {
                    await RunBaseMaterialInternalWriteAsync(() =>
                        _applicationViewModel.LineArt.RepairAsync(CharacterDesk.CurrentCharacter, item.Kind, sourcePath, item.Index));
                }
                await RestoreBaseMaterialScrollOffsetAsync(scrollOffset);
                MarkLastEditedModule("LineArt");
                ShowFloatingTip(InfoBarSeverity.Success, "基础素材已重新导入", item.DisplayName);
                AppendLog(LogKind.User, $"重新导入基础素材：{item.DisplayName} #{item.Index} <- {Path.GetFileName(sourcePath)}");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "基础素材重新导入失败", ex.Message);
                AppendLog(LogKind.Error, "基础素材重新导入失败。", ex);
            }
        }

        private void OpenBaseMaterialFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (CharacterDesk.CurrentCharacter is null ||
                sender is not Button { Tag: BaseMaterialKind kind })
            {
                return;
            }

            var folderPath = _baseMaterialService.GetMaterialFolderPath(CharacterDesk.CurrentCharacter, kind);
            Directory.CreateDirectory(folderPath);
            Process.Start(new ProcessStartInfo
            {
                FileName = folderPath,
                UseShellExecute = true
            });
        }

        private async Task<string?> PickImageAsync()
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
            var file = await picker.PickSingleFileAsync();
            return file?.Path;
        }

        private async Task<Rectangle?> GetOptionalBaseMaterialCropAsync(BaseMaterialKind kind, string sourcePath)
        {
            if (!NeedsManualCrop(kind, sourcePath))
            {
                return null;
            }

            return await ShowBaseMaterialCropDialogAsync(kind, sourcePath);
        }

        private static bool NeedsManualCrop(BaseMaterialKind kind, string sourcePath)
        {
            var spec = BaseMaterialService.GetSpec(kind);
            if (!spec.HasFixedSize)
            {
                return false;
            }

            var (width, height) = BaseMaterialService.GetImageSize(sourcePath);
            return width != spec.Width || height != spec.Height;
        }

        private async Task<Rectangle?> ShowBaseMaterialCropDialogAsync(BaseMaterialKind kind, string sourcePath)
        {
            var spec = BaseMaterialService.GetSpec(kind);
            var (width, height) = BaseMaterialService.GetImageSize(sourcePath);
            _baseMaterialCropCompletion = new TaskCompletionSource<Rectangle?>();
            _baseMaterialCropSourcePath = sourcePath;
            _baseMaterialCropSourceWidth = width;
            _baseMaterialCropSourceHeight = height;
            _baseMaterialCropSpec = spec;

            BaseMaterialCropTitleText.Text = $"裁剪{spec.DisplayName}";
            BaseMaterialCropSubtitleText.Text = $"源图 {width}x{height}，目标 {spec.Width}x{spec.Height}。滚轮缩放，拖动平移，右键或 Esc 取消。";
            BaseMaterialCropImage.Source = await LoadBitmapFromFileAsync(sourcePath);
            ResetBaseMaterialCropControls();
            BaseMaterialCropHost.Visibility = Visibility.Visible;
            BaseMaterialCropHost.Focus(FocusState.Programmatic);
            return await _baseMaterialCropCompletion.Task;
        }

        private static async Task<BitmapImage> LoadBitmapFromFileAsync(string filePath)
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

        private void ResetBaseMaterialCropControls()
        {
            _baseMaterialCropScale = 1;
            _baseMaterialCropOffsetX = 0;
            _baseMaterialCropOffsetY = 0;
            _isPanningBaseMaterialCrop = false;
            UpdateBaseMaterialCropPreview();
        }

        private void UpdateBaseMaterialCropPreview()
        {
            if (_baseMaterialCropSpec is null)
            {
                return;
            }

            var sourceRatio = _baseMaterialCropSourceWidth / (double)Math.Max(1, _baseMaterialCropSourceHeight);
            const double maxPreview = 460;
            if (sourceRatio >= 1)
            {
                BaseMaterialCropPreviewFrame.Width = maxPreview;
                BaseMaterialCropPreviewFrame.Height = maxPreview / sourceRatio;
            }
            else
            {
                BaseMaterialCropPreviewFrame.Height = maxPreview;
                BaseMaterialCropPreviewFrame.Width = maxPreview * sourceRatio;
            }

            BaseMaterialCropPreviewFrame.Clip = new Microsoft.UI.Xaml.Media.RectangleGeometry
            {
                Rect = new Windows.Foundation.Rect(0, 0, BaseMaterialCropPreviewFrame.Width, BaseMaterialCropPreviewFrame.Height)
            };
            UpdateBaseMaterialCropTargetFrameSize();
            var imageSize = GetBaseMaterialCropImagePreviewSize();
            BaseMaterialCropImage.Width = imageSize.Width;
            BaseMaterialCropImage.Height = imageSize.Height;
            ClampBaseMaterialCropPan();
            BaseMaterialCropImageTransform.ScaleX = _baseMaterialCropScale;
            BaseMaterialCropImageTransform.ScaleY = _baseMaterialCropScale;
            BaseMaterialCropImageTransform.TranslateX = _baseMaterialCropOffsetX;
            BaseMaterialCropImageTransform.TranslateY = _baseMaterialCropOffsetY;
        }

        private void UpdateBaseMaterialCropTargetFrameSize()
        {
            if (_baseMaterialCropSpec is null)
            {
                return;
            }

            var targetRatio = _baseMaterialCropSpec.Width / (double)_baseMaterialCropSpec.Height;
            var frameWidth = Math.Max(1, BaseMaterialCropPreviewFrame.Width);
            var frameHeight = Math.Max(1, BaseMaterialCropPreviewFrame.Height);
            if (frameWidth / frameHeight >= targetRatio)
            {
                BaseMaterialCropTargetFrame.Height = frameHeight;
                BaseMaterialCropTargetFrame.Width = frameHeight * targetRatio;
            }
            else
            {
                BaseMaterialCropTargetFrame.Width = frameWidth;
                BaseMaterialCropTargetFrame.Height = frameWidth / targetRatio;
            }

            UpdateBaseMaterialCropMasks(frameWidth, frameHeight);
        }

        private void UpdateBaseMaterialCropMasks(double frameWidth, double frameHeight)
        {
            var targetWidth = Math.Max(0, BaseMaterialCropTargetFrame.Width);
            var targetHeight = Math.Max(0, BaseMaterialCropTargetFrame.Height);
            var horizontalGap = Math.Max(0, (frameWidth - targetWidth) / 2);
            var verticalGap = Math.Max(0, (frameHeight - targetHeight) / 2);

            BaseMaterialCropMaskTop.Height = verticalGap;
            BaseMaterialCropMaskBottom.Height = verticalGap;
            BaseMaterialCropMaskLeft.Width = horizontalGap;
            BaseMaterialCropMaskLeft.Height = targetHeight;
            BaseMaterialCropMaskRight.Width = horizontalGap;
            BaseMaterialCropMaskRight.Height = targetHeight;
        }

        private void ClampBaseMaterialCropPan()
        {
            var imageSize = GetBaseMaterialCropImagePreviewSize();
            var maxX = Math.Max(0, (imageSize.Width * _baseMaterialCropScale - BaseMaterialCropTargetFrame.Width) / 2);
            var maxY = Math.Max(0, (imageSize.Height * _baseMaterialCropScale - BaseMaterialCropTargetFrame.Height) / 2);
            _baseMaterialCropOffsetX = Math.Clamp(_baseMaterialCropOffsetX, -maxX, maxX);
            _baseMaterialCropOffsetY = Math.Clamp(_baseMaterialCropOffsetY, -maxY, maxY);
        }

        private (double Width, double Height) GetBaseMaterialCropImagePreviewSize()
        {
            var frameWidth = Math.Max(1, BaseMaterialCropPreviewFrame.Width);
            var frameHeight = Math.Max(1, BaseMaterialCropPreviewFrame.Height);
            if (_baseMaterialCropSourceWidth <= 0 || _baseMaterialCropSourceHeight <= 0)
            {
                return (frameWidth, frameHeight);
            }

            var sourceRatio = _baseMaterialCropSourceWidth / (double)_baseMaterialCropSourceHeight;
            var frameRatio = frameWidth / frameHeight;
            return sourceRatio >= frameRatio
                ? (frameHeight * sourceRatio, frameHeight)
                : (frameWidth, frameWidth / sourceRatio);
        }

        private Rectangle BuildBaseMaterialCropRectangle()
        {
            if (_baseMaterialCropSpec is null)
            {
                return Rectangle.Empty;
            }

            var scale = Math.Max(1, _baseMaterialCropScale);
            var imageSize = GetBaseMaterialCropImagePreviewSize();
            var scaledImageWidth = imageSize.Width * scale;
            var scaledImageHeight = imageSize.Height * scale;
            var imageLeft = (BaseMaterialCropPreviewFrame.Width - scaledImageWidth) / 2 + _baseMaterialCropOffsetX;
            var imageTop = (BaseMaterialCropPreviewFrame.Height - scaledImageHeight) / 2 + _baseMaterialCropOffsetY;
            var targetLeft = (BaseMaterialCropPreviewFrame.Width - BaseMaterialCropTargetFrame.Width) / 2;
            var targetTop = (BaseMaterialCropPreviewFrame.Height - BaseMaterialCropTargetFrame.Height) / 2;

            var cropX = (targetLeft - imageLeft) / scaledImageWidth * _baseMaterialCropSourceWidth;
            var cropY = (targetTop - imageTop) / scaledImageHeight * _baseMaterialCropSourceHeight;
            var cropWidth = BaseMaterialCropTargetFrame.Width / scaledImageWidth * _baseMaterialCropSourceWidth;
            var cropHeight = BaseMaterialCropTargetFrame.Height / scaledImageHeight * _baseMaterialCropSourceHeight;
            cropWidth = Math.Min(cropWidth, _baseMaterialCropSourceWidth);
            cropHeight = Math.Min(cropHeight, _baseMaterialCropSourceHeight);

            var maxX = Math.Max(0, _baseMaterialCropSourceWidth - cropWidth);
            var maxY = Math.Max(0, _baseMaterialCropSourceHeight - cropHeight);
            var x = (int)Math.Round(Math.Clamp(cropX, 0, maxX));
            var y = (int)Math.Round(Math.Clamp(cropY, 0, maxY));
            var width = Math.Clamp((int)Math.Round(cropWidth), 1, _baseMaterialCropSourceWidth - x);
            var height = Math.Clamp((int)Math.Round(cropHeight), 1, _baseMaterialCropSourceHeight - y);
            return new Rectangle(x, y, width, height);
        }

        private void CompleteBaseMaterialCrop(Rectangle? crop)
        {
            var completion = _baseMaterialCropCompletion;
            _baseMaterialCropCompletion = null;
            _baseMaterialCropSourcePath = null;
            _baseMaterialCropSpec = null;
            BaseMaterialCropImage.Source = null;
            BaseMaterialCropHost.Visibility = Visibility.Collapsed;
            completion?.TrySetResult(crop);
        }

        private void BaseMaterialCropConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            CompleteBaseMaterialCrop(BuildBaseMaterialCropRectangle());
        }

        private void BaseMaterialCropCancelButton_Click(object sender, RoutedEventArgs e)
        {
            CompleteBaseMaterialCrop(null);
        }

        private void BaseMaterialCropResetButton_Click(object sender, RoutedEventArgs e)
        {
            ResetBaseMaterialCropControls();
        }

        private void BaseMaterialCropHost_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            CompleteBaseMaterialCrop(null);
            e.Handled = true;
        }

        private void BaseMaterialCropPreviewFrame_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            CompleteBaseMaterialCrop(null);
            e.Handled = true;
        }

        private void BaseMaterialCropPreviewFrame_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            ResetBaseMaterialCropControls();
            e.Handled = true;
        }

        private void BaseMaterialCropPreviewFrame_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(BaseMaterialCropPreviewFrame);
            var factor = point.Properties.MouseWheelDelta > 0 ? 1.08 : 1 / 1.08;
            _baseMaterialCropScale = Math.Clamp(_baseMaterialCropScale * factor, 1, 6);
            UpdateBaseMaterialCropPreview();
            e.Handled = true;
        }

        private void BaseMaterialCropPreviewFrame_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(BaseMaterialCropPreviewFrame);
            if (!point.Properties.IsLeftButtonPressed)
            {
                return;
            }

            _isPanningBaseMaterialCrop = true;
            _lastBaseMaterialCropPointerPosition = point.Position;
            BaseMaterialCropPreviewFrame.CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private void BaseMaterialCropPreviewFrame_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isPanningBaseMaterialCrop)
            {
                return;
            }

            var point = e.GetCurrentPoint(BaseMaterialCropPreviewFrame);
            if (!point.Properties.IsLeftButtonPressed)
            {
                EndBaseMaterialCropPan(e);
                return;
            }

            var position = point.Position;
            _baseMaterialCropOffsetX += position.X - _lastBaseMaterialCropPointerPosition.X;
            _baseMaterialCropOffsetY += position.Y - _lastBaseMaterialCropPointerPosition.Y;
            _lastBaseMaterialCropPointerPosition = position;
            UpdateBaseMaterialCropPreview();
            e.Handled = true;
        }

        private void BaseMaterialCropPreviewFrame_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            EndBaseMaterialCropPan(e);
        }

        private void BaseMaterialCropPreviewFrame_PointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            EndBaseMaterialCropPan(e);
        }

        private void BaseMaterialCropPreviewFrame_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            EndBaseMaterialCropPan(e);
        }

        private void EndBaseMaterialCropPan(PointerRoutedEventArgs e)
        {
            if (!_isPanningBaseMaterialCrop)
            {
                return;
            }

            _isPanningBaseMaterialCrop = false;
            BaseMaterialCropPreviewFrame.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        }

        private void BaseMaterialCropHost_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                CompleteBaseMaterialCrop(null);
                e.Handled = true;
            }
        }
    }
}
