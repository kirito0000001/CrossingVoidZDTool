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
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CrossingVoidZDTool
{
    public sealed partial class MainWindow
    {
        private MediaPlayer? _voiceMediaPlayer;
        private readonly WaveAudioDurationReader _voiceDurationReader = new();
        private DispatcherQueueTimer? _voicePlaybackStopTimer;
        private Button? _playingVoiceButton;
        private string? _playingVoiceFilePath;
        private string? _playingVoicePreviewPath;
        private VoiceMaterialKind? _managedVoiceMaterialKind;
        private bool _voiceDuplicateScanCompleted;
        private int _pendingVoiceDuplicateResultCount;
        private bool _isDeletingSelectedVoiceMaterials;

        private void InitializeVoicePlayback()
        {
            VoiceMaterialService.CleanupPlaybackCopies();
        }

        private void DisposeVoicePlayback()
        {
            StopVoicePlayback();
        }

        private async void AddVoiceMaterialButton_Click(object sender, RoutedEventArgs e)
        {
            if (CharacterDesk.CurrentCharacter is null ||
                sender is not Button { Tag: VoiceMaterialSpec spec })
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择角色", "请先在角色台选择当前制作角色。");
                return;
            }

            var sourcePaths = await PickWaveFilesAsync(allowMultiple: true);
            if (sourcePaths.Count == 0)
            {
                return;
            }

            try
            {
                StopVoicePlayback();
                await RunBaseMaterialInternalWriteAsync(() =>
                    _applicationViewModel.LineArt.ImportVoicesAsync(
                        CharacterDesk.CurrentCharacter,
                        spec.Kind,
                        sourcePaths));

                RefreshVoiceMaterialManager();
                MarkLastEditedModule("LineArt");
                ShowFloatingTip(InfoBarSeverity.Success, "语音已导入", $"{spec.DisplayName}：{sourcePaths.Count} 个 WAV");
                AppendLog(LogKind.User, $"导入语音素材：{spec.DisplayName}，共 {sourcePaths.Count} 个 WAV。");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "语音导入失败", ex.Message);
                AppendLog(LogKind.Error, $"导入语音素材失败：{spec.DisplayName}。", ex);
            }
        }

        private async void ReplaceVoiceMaterialButton_Click(object sender, RoutedEventArgs e)
        {
            if (CharacterDesk.CurrentCharacter is null ||
                sender is not Button { Tag: VoiceMaterialItem item })
            {
                return;
            }

            var sourcePaths = await PickWaveFilesAsync(allowMultiple: false);
            if (sourcePaths.Count == 0)
            {
                return;
            }

            try
            {
                StopVoicePlayback();
                await RunBaseMaterialInternalWriteAsync(() =>
                    _applicationViewModel.LineArt.ReplaceVoiceAsync(
                        CharacterDesk.CurrentCharacter,
                        item,
                        sourcePaths[0]));
                RefreshVoiceMaterialManager();
                MarkLastEditedModule("LineArt");
                ShowFloatingTip(InfoBarSeverity.Success, "语音已替换", item.FileName);
                AppendLog(LogKind.User, $"替换语音素材：{item.FileName}。");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "语音替换失败", ex.Message);
                AppendLog(LogKind.Error, $"替换语音素材失败：{item.FileName}。", ex);
            }
        }

        private async void DeleteVoiceMaterialButton_Click(object sender, RoutedEventArgs e)
        {
            if (CharacterDesk.CurrentCharacter is null ||
                sender is not Button { Tag: VoiceMaterialItem item })
            {
                return;
            }

            var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "删除语音",
                new TextBlock
                {
                    Text = $"是否删除 {item.FileName}？此操作会删除角色 Sound 目录中的文件。",
                    TextWrapping = TextWrapping.Wrap,
                    Width = 440
                },
                PrimaryButtonText: "删除",
                CloseButtonText: string.Empty,
                SecondaryButtonText: "取消",
                DefaultButton: ContentDialogButton.None,
                PrimaryButtonStyle: (Style)Application.Current.Resources["DialogAccentButtonStyle"]));
            if (result != DialogResultKind.Primary)
            {
                return;
            }

            try
            {
                if (string.Equals(_playingVoiceFilePath, item.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    StopVoicePlayback();
                }

                await RunBaseMaterialInternalWriteAsync(() =>
                    _applicationViewModel.LineArt.DeleteVoiceAsync(CharacterDesk.CurrentCharacter, item));
                RefreshVoiceMaterialManager();
                MarkLastEditedModule("LineArt");
                ShowFloatingTip(InfoBarSeverity.Success, "语音已删除", item.FileName);
                AppendLog(LogKind.User, $"删除语音素材：{item.FileName}。");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "语音删除失败", ex.Message);
                AppendLog(LogKind.Error, $"删除语音素材失败：{item.FileName}。", ex);
            }
        }

        private void OpenVoiceMaterialFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (CharacterDesk.CurrentCharacter is null ||
                sender is not Button { Tag: VoiceMaterialKind kind })
            {
                return;
            }

            var folderPath = _voiceMaterialService.GetCategoryFolderPath(CharacterDesk.CurrentCharacter, kind);
            Directory.CreateDirectory(folderPath);
            Process.Start(new ProcessStartInfo
            {
                FileName = folderPath,
                UseShellExecute = true
            });
        }

        private void OpenVoiceMaterialManagerButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: VoiceMaterialSection section } || section.Items.Count == 0)
            {
                return;
            }

            ShowVoiceMaterialManager(section);
        }

        private void ShowVoiceMaterialManager(VoiceMaterialSection section)
        {
            _managedVoiceMaterialKind = section.Spec.Kind;
            VoiceMaterialManagerTitleText.Text = section.Spec.DisplayName;
            VoiceMaterialManagerStatusText.Text = section.StatusText;
            var supportsAssignment = section.Spec.Kind == VoiceMaterialKind.Other;
            VoiceMaterialAssignmentBar.Visibility = supportsAssignment ? Visibility.Visible : Visibility.Collapsed;
            ClearVoiceMaterialManagerSelection();
            VoiceMaterialManagerListView.SelectionMode = supportsAssignment
                ? ListViewSelectionMode.Extended
                : ListViewSelectionMode.None;
            ApplyVoiceUsageTexts(section.Items);
            VoiceMaterialManagerListView.ItemsSource = section.Items;
            ResetPendingVoiceDuplicateResults(section.Items);
            UpdateVoiceMaterialAssignmentState();
            VoiceMaterialManagerHost.Visibility = Visibility.Visible;
            AnimateReferenceOverlay(VoiceMaterialManagerHost, VoiceMaterialManagerCardScale, show: true);
            VoiceMaterialManagerHost.Focus(FocusState.Programmatic);
        }

        private async void HideVoiceMaterialManager()
        {
            if (VoiceMaterialManagerHost.Visibility != Visibility.Visible)
            {
                return;
            }

            StopVoicePlayback();
            await AnimateReferenceOverlayAsync(VoiceMaterialManagerHost, VoiceMaterialManagerCardScale, show: false);
            VoiceMaterialManagerHost.Visibility = Visibility.Collapsed;
            ClearVoiceMaterialManagerSelection();
            VoiceMaterialManagerListView.ItemsSource = null;
            VoiceMaterialAssignmentBar.Visibility = Visibility.Collapsed;
            ResetPendingVoiceDuplicateResults([]);
            _managedVoiceMaterialKind = null;
        }

        private void RefreshVoiceMaterialManager()
        {
            if (VoiceMaterialManagerHost.Visibility != Visibility.Visible || _managedVoiceMaterialKind is null)
            {
                return;
            }

            var section = _applicationViewModel.LineArt.VoiceSections
                .FirstOrDefault(item => item.Spec.Kind == _managedVoiceMaterialKind.Value);
            if (section is null || section.Items.Count == 0)
            {
                HideVoiceMaterialManager();
                return;
            }

            VoiceMaterialManagerTitleText.Text = section.Spec.DisplayName;
            VoiceMaterialManagerStatusText.Text = section.StatusText;
            ApplyVoiceUsageTexts(section.Items);
            VoiceMaterialManagerListView.ItemsSource = section.Items;
            ClearVoiceMaterialManagerSelection();
            ResetPendingVoiceDuplicateResults(section.Items);
            UpdateVoiceMaterialAssignmentState();
        }

        private void ApplyVoiceUsageTexts(IEnumerable<VoiceMaterialItem> items)
        {
            if (CharacterDesk.CurrentCharacter is null)
            {
                return;
            }

            var usages = new SequenceFrameService().GetVoiceUsages(CharacterDesk.CurrentCharacter);
            foreach (var item in items)
            {
                var path = Path.GetFullPath(item.FilePath);
                item.SetUsageText(usages.TryGetValue(path, out var locations)
                    ? $"使用位置：{string.Join("、", locations.Select(location => location.DisplayText))}"
                    : string.Empty);
            }
        }

        private void ClearVoiceMaterialManagerSelection()
        {
            if (VoiceMaterialManagerListView.SelectionMode == ListViewSelectionMode.None)
            {
                return;
            }

            VoiceMaterialManagerListView.SelectedItems.Clear();
        }

        private void VoiceMaterialManagerListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateVoiceMaterialAssignmentState();
        }

        private void UpdateVoiceMaterialAssignmentState()
        {
            var count = _managedVoiceMaterialKind == VoiceMaterialKind.Other
                ? VoiceMaterialManagerListView.SelectedItems.Count
                : 0;
            VoiceMaterialManagerSelectionText.Text = count > 0
                ? $"已选择 {count} 条语音"
                : _voiceDuplicateScanCompleted
                    ? _pendingVoiceDuplicateResultCount == 0
                        ? "未发现重复语音"
                        : $"发现 {_pendingVoiceDuplicateResultCount} 条重复语音"
                    : "请选择要分配的语音";
            AssignVoiceMaterialsButton.IsEnabled = count > 0;
        }

        private async void CheckVoiceMaterialDuplicatesButton_Click(object sender, RoutedEventArgs e)
        {
            if (CharacterDesk.CurrentCharacter is null ||
                _managedVoiceMaterialKind != VoiceMaterialKind.Other ||
                sender is not Button button)
            {
                return;
            }

            button.IsEnabled = false;
            VoiceMaterialManagerSelectionText.Text = "正在检查音频内容...";
            try
            {
                var matches = await _applicationViewModel.LineArt.FindPendingVoiceDuplicatesAsync(
                    CharacterDesk.CurrentCharacter);
                ShowPendingVoiceDuplicateResults(matches);
                AppendLog(LogKind.User, $"检查待分配语音重复：发现 {matches.Count} 条。");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "语音查重失败", ex.Message);
                AppendLog(LogKind.Error, "检查待分配语音重复失败。", ex);
                UpdateVoiceMaterialAssignmentState();
            }
            finally
            {
                button.IsEnabled = true;
            }
        }

        private void ShowPendingVoiceDuplicateResults(IReadOnlyList<VoiceMaterialDuplicateMatch> matches)
        {
            var section = _applicationViewModel.LineArt.VoiceSections
                .FirstOrDefault(item => item.Spec.Kind == VoiceMaterialKind.Other);
            if (section is null)
            {
                return;
            }

            foreach (var item in section.Items)
            {
                item.SetDuplicateStatus(null);
            }

            var currentItems = section.Items.ToDictionary(
                item => Path.GetFullPath(item.FilePath),
                StringComparer.OrdinalIgnoreCase);
            var duplicateItems = new List<VoiceMaterialItem>();
            foreach (var match in matches)
            {
                if (!currentItems.TryGetValue(Path.GetFullPath(match.PendingItem.FilePath), out var currentItem))
                {
                    continue;
                }

                currentItem.SetDuplicateStatus(BuildVoiceDuplicateStatusText(match));
                duplicateItems.Add(currentItem);
            }

            ClearVoiceMaterialManagerSelection();
            VoiceMaterialManagerListView.ItemsSource = duplicateItems.Count > 0
                ? duplicateItems
                : section.Items;
            _voiceDuplicateScanCompleted = true;
            _pendingVoiceDuplicateResultCount = duplicateItems.Count;
            ShowAllVoiceMaterialsButton.Visibility = duplicateItems.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            UpdateVoiceMaterialAssignmentState();
        }

        private static string BuildVoiceDuplicateStatusText(VoiceMaterialDuplicateMatch match)
        {
            var parts = new List<string>();
            if (match.AssignedMatches.Count > 0)
            {
                var assignedLocations = match.AssignedMatches
                    .Select(item => $"{item.DisplayName} #{item.Index}")
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                parts.Add($"已存在于：{string.Join("、", assignedLocations)}");
            }

            if (match.PendingMatches.Count > 0)
            {
                var pendingLocations = match.PendingMatches
                    .Select(item => $"#{item.Index}")
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                parts.Add($"待分配内重复：{string.Join("、", pendingLocations)}");
            }

            return string.Join("；", parts);
        }

        private void ShowAllVoiceMaterialsButton_Click(object sender, RoutedEventArgs e)
        {
            var section = _applicationViewModel.LineArt.VoiceSections
                .FirstOrDefault(item => item.Spec.Kind == VoiceMaterialKind.Other);
            if (section is null)
            {
                return;
            }

            ClearVoiceMaterialManagerSelection();
            VoiceMaterialManagerListView.ItemsSource = section.Items;
            ShowAllVoiceMaterialsButton.Visibility = Visibility.Collapsed;
            UpdateVoiceMaterialAssignmentState();
        }

        private void ResetPendingVoiceDuplicateResults(IEnumerable<VoiceMaterialItem> items)
        {
            foreach (var item in items)
            {
                item.SetDuplicateStatus(null);
            }

            _voiceDuplicateScanCompleted = false;
            _pendingVoiceDuplicateResultCount = 0;
            ShowAllVoiceMaterialsButton.Visibility = Visibility.Collapsed;
            CheckVoiceMaterialDuplicatesButton.IsEnabled = true;
        }

        private void AssignVoiceMaterialsButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || VoiceMaterialManagerListView.SelectedItems.Count == 0)
            {
                return;
            }

            var flyout = new MenuFlyout();
            foreach (var spec in VoiceMaterialService.Specs.Where(spec => spec.Kind != VoiceMaterialKind.Other))
            {
                var item = new MenuFlyoutItem
                {
                    Text = spec.DisplayName,
                    Tag = spec.Kind
                };
                item.Click += AssignVoiceMaterialTargetMenuItem_Click;
                flyout.Items.Add(item);
            }

            flyout.ShowAt(button);
        }

        private async void AssignVoiceMaterialTargetMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem { Tag: VoiceMaterialKind targetKind })
            {
                await AssignSelectedVoiceMaterialsAsync(targetKind);
            }
        }

        private async Task AssignSelectedVoiceMaterialsAsync(VoiceMaterialKind targetKind)
        {
            if (CharacterDesk.CurrentCharacter is null ||
                _managedVoiceMaterialKind != VoiceMaterialKind.Other)
            {
                return;
            }

            var selectedItems = VoiceMaterialManagerListView.SelectedItems
                .OfType<VoiceMaterialItem>()
                .ToArray();
            if (selectedItems.Length == 0)
            {
                return;
            }

            var targetSpec = VoiceMaterialService.GetSpec(targetKind);
            try
            {
                StopVoicePlayback();
                await RunBaseMaterialInternalWriteAsync(() =>
                    _applicationViewModel.LineArt.MovePendingVoicesAsync(
                        CharacterDesk.CurrentCharacter,
                        selectedItems,
                        targetKind));
                RefreshVoiceMaterialManager();
                MarkLastEditedModule("LineArt");
                ShowFloatingTip(
                    InfoBarSeverity.Success,
                    "语音已分配",
                    $"{selectedItems.Length} 条语音已移动到{targetSpec.DisplayName}。");
                AppendLog(
                    LogKind.User,
                    $"分配待分配语音：{selectedItems.Length} 条移动到{targetSpec.DisplayName}。");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "语音分配失败", ex.Message);
                AppendLog(LogKind.Error, $"分配待分配语音到{targetSpec.DisplayName}失败。", ex);
            }
        }

        private void VoiceMaterialManagerCloseButton_Click(object sender, RoutedEventArgs e)
        {
            HideVoiceMaterialManager();
        }

        private async void VoiceMaterialManagerHost_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                HideVoiceMaterialManager();
                e.Handled = true;
                return;
            }

            if (e.Key == Windows.System.VirtualKey.Delete &&
                _managedVoiceMaterialKind == VoiceMaterialKind.Other)
            {
                var selectedItems = VoiceMaterialManagerListView.SelectedItems
                    .OfType<VoiceMaterialItem>()
                    .ToArray();
                if (selectedItems.Length > 0)
                {
                    e.Handled = true;
                    await DeleteSelectedPendingVoicesAsync(selectedItems);
                }
            }
        }

        private async Task DeleteSelectedPendingVoicesAsync(IReadOnlyList<VoiceMaterialItem> selectedItems)
        {
            if (CharacterDesk.CurrentCharacter is null ||
                _managedVoiceMaterialKind != VoiceMaterialKind.Other ||
                selectedItems.Count == 0 ||
                _isDeletingSelectedVoiceMaterials)
            {
                return;
            }

            var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "批量删除待分配语音",
                new TextBlock
                {
                    Text = $"确定删除选中的 {selectedItems.Count} 条语音吗？此操作会删除对应 WAV 文件。",
                    TextWrapping = TextWrapping.Wrap,
                    Width = 440
                },
                PrimaryButtonText: "删除",
                CloseButtonText: string.Empty,
                SecondaryButtonText: "取消",
                DefaultButton: ContentDialogButton.None,
                PrimaryButtonStyle: (Style)Application.Current.Resources["DialogAccentButtonStyle"]));
            if (result != DialogResultKind.Primary)
            {
                return;
            }

            _isDeletingSelectedVoiceMaterials = true;
            try
            {
                StopVoicePlayback();
                await RunBaseMaterialInternalWriteAsync(() =>
                    _applicationViewModel.LineArt.DeletePendingVoicesAsync(
                        CharacterDesk.CurrentCharacter,
                        selectedItems));
                RefreshVoiceMaterialManager();
                MarkLastEditedModule("LineArt");
                ShowFloatingTip(
                    InfoBarSeverity.Success,
                    "语音已删除",
                    $"已删除 {selectedItems.Count} 条待分配语音。");
                AppendLog(LogKind.User, $"批量删除待分配语音：共 {selectedItems.Count} 条。");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "语音删除失败", ex.Message);
                AppendLog(LogKind.Error, "批量删除待分配语音失败。", ex);
            }
            finally
            {
                _isDeletingSelectedVoiceMaterials = false;
            }
        }

        private void VoiceMaterialManagerHost_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            HideVoiceMaterialManager();
            e.Handled = true;
        }

        private void VoiceMaterialManagerHost_Tapped(object sender, TappedRoutedEventArgs e)
        {
            HideVoiceMaterialManager();
            e.Handled = true;
        }

        private void VoiceMaterialManagerCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
        }

        private void PlayVoiceMaterialButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not VoiceMaterialItem { CanPlay: true } item)
            {
                return;
            }

            if (string.Equals(_playingVoiceFilePath, item.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                StopVoicePlayback();
                return;
            }

            PlayVoiceFile(item.FilePath, button);
            button.Content = new SymbolIcon { Symbol = Symbol.Stop };
            ToolTipService.SetToolTip(button, "停止试听");
        }

        private void PlayVoiceFile(string filePath, Button? sourceButton = null)
        {
            StopVoicePlayback();
            var effectiveDuration = _voiceDurationReader.GetEffectiveDuration(filePath);
            VoiceMaterialService.CleanupPlaybackCopies();
            var previewPath = VoiceMaterialService.CreatePlaybackCopy(filePath);
            _playingVoiceButton = sourceButton;
            _playingVoiceFilePath = filePath;
            _playingVoicePreviewPath = previewPath;
            var mediaPlayer = new MediaPlayer();
            mediaPlayer.MediaEnded += VoiceMediaPlayer_MediaEnded;
            mediaPlayer.MediaFailed += VoiceMediaPlayer_MediaFailed;
            _voiceMediaPlayer = mediaPlayer;
            try
            {
                mediaPlayer.Source = MediaSource.CreateFromUri(new Uri(previewPath));
                mediaPlayer.Play();
                if (effectiveDuration is { } duration && duration > TimeSpan.Zero)
                {
                    ScheduleVoicePlaybackStop(mediaPlayer, duration);
                }
            }
            catch
            {
                StopVoicePlayback();
                throw;
            }
        }

        private void StopVoicePlayback()
        {
            _voicePlaybackStopTimer?.Stop();
            _voicePlaybackStopTimer = null;
            var mediaPlayer = _voiceMediaPlayer;
            _voiceMediaPlayer = null;
            var previewPath = _playingVoicePreviewPath;
            if (mediaPlayer is not null)
            {
                mediaPlayer.MediaEnded -= VoiceMediaPlayer_MediaEnded;
                mediaPlayer.MediaFailed -= VoiceMediaPlayer_MediaFailed;
                mediaPlayer.Pause();
                mediaPlayer.Source = null;
                mediaPlayer.Dispose();
            }

            if (_playingVoiceButton is not null)
            {
                _playingVoiceButton.Content = new SymbolIcon { Symbol = Symbol.Play };
                ToolTipService.SetToolTip(_playingVoiceButton, "播放或停止试听");
            }

            _playingVoiceButton = null;
            _playingVoiceFilePath = null;
            _playingVoicePreviewPath = null;
            VoiceMaterialService.TryDeletePlaybackCopy(previewPath);
        }

        private void ScheduleVoicePlaybackStop(MediaPlayer mediaPlayer, TimeSpan effectiveDuration)
        {
            var timer = DispatcherQueue.CreateTimer();
            timer.Interval = effectiveDuration;
            timer.IsRepeating = false;
            timer.Tick += (sender, _) =>
            {
                sender.Stop();
                if (!ReferenceEquals(sender, _voicePlaybackStopTimer))
                {
                    return;
                }

                _voicePlaybackStopTimer = null;
                if (ReferenceEquals(mediaPlayer, _voiceMediaPlayer))
                {
                    StopVoicePlayback();
                    if (_isSequenceEditorPreviewPlayback &&
                        _applicationViewModel.SequenceFrames.IsPreviewing &&
                        _applicationViewModel.SequenceFrames.PauseEditorPreviewWhenVoiceEnds)
                    {
                        StopSequencePreview();
                    }
                }
            };
            _voicePlaybackStopTimer = timer;
            timer.Start();
        }

        private void VoiceMediaPlayer_MediaEnded(MediaPlayer sender, object args)
        {
            if (!ReferenceEquals(sender, _voiceMediaPlayer))
            {
                return;
            }

            RunOnUiThread(() =>
            {
                if (ReferenceEquals(sender, _voiceMediaPlayer))
                {
                    StopVoicePlayback();
                }
            });
        }

        private void VoiceMediaPlayer_MediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
        {
            if (!ReferenceEquals(sender, _voiceMediaPlayer))
            {
                return;
            }

            var message = string.IsNullOrWhiteSpace(args.ErrorMessage) ? "无法播放这个 WAV 文件。" : args.ErrorMessage;
            RunOnUiThread(() =>
            {
                if (ReferenceEquals(sender, _voiceMediaPlayer))
                {
                    StopVoicePlayback();
                    ShowFloatingTip(InfoBarSeverity.Error, "语音播放失败", message);
                }
            });
        }

        private async Task<IReadOnlyList<string>> PickWaveFilesAsync(bool allowMultiple)
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.MusicLibrary
            };
            picker.FileTypeFilter.Add(".wav");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            if (allowMultiple)
            {
                var files = await picker.PickMultipleFilesAsync();
                return files.Select(file => file.Path).ToArray();
            }

            var file = await picker.PickSingleFileAsync();
            return file is null ? [] : [file.Path];
        }
    }
}
