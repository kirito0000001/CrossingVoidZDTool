using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;

namespace CrossingVoidZDTool
{
    public sealed partial class MainWindow
    {
        private Grid? _unrealSequencePreviewOverlay;
        private readonly SequencePreviewBitmapCache _unrealSequencePreviewCache = new();
        private DispatcherQueueTimer? _unrealSequencePreviewTimer;
        private Image? _unrealSequencePreviewImage;
        private Image? _unrealSequencePreviewBackImage;
        private TextBlock? _unrealSequencePreviewFrameText;
        private FontIcon? _unrealSequencePreviewPlayPauseIcon;
        private CompositeTransform? _unrealSequencePreviewTransform;
        private Grid? _unrealSequencePreviewCanvas;
        private Grid? _unrealSequencePreviewImageLayer;
        private IReadOnlyList<SequenceFrameItem> _unrealSequencePreviewFrames = [];
        private int _unrealSequencePreviewIndex;
        private int _unrealSequencePreviewFps = 12;
        private bool _isUnrealSequencePreviewPlaying;
        private bool _isPanningUnrealSequencePreview;
        private bool _isUnrealSequencePreviewFrontActive = true;
        private double _unrealSequencePreviewScale = 1;
        private Point _lastUnrealSequencePreviewPointerPosition;
        private const double UnrealSequencePreviewWidth = 928;
        private const double UnrealSequencePreviewHeight = 640;
        private static readonly SolidColorBrush UnrealSequencePreviewDialogBrush = new(Color.FromArgb(255, 42, 42, 42));
        private static readonly SolidColorBrush UnrealSequencePreviewCanvasBrush = new(Color.FromArgb(255, 54, 54, 54));
        private static readonly SolidColorBrush UnrealSequencePreviewTextBrush = new(Colors.White);
        private static readonly SolidColorBrush UnrealSequencePreviewSubtleTextBrush = new(Color.FromArgb(255, 210, 210, 210));

        private async void ChooseUnrealProjectSyncEngineButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.ComputerFolder
            };
            picker.FileTypeFilter.Add(".exe");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            var selectedFile = await picker.PickSingleFileAsync();
            if (selectedFile is null)
            {
                return;
            }

            _applicationViewModel.UnrealProjectSync.EnginePath = selectedFile.Path;
            SaveUnrealProjectSyncSettings();
            AppendLog(LogKind.User, $"选择虚幻引擎：{selectedFile.Path}");
        }

        private async void ChooseUnrealProjectSyncProjectButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.ComputerFolder
            };
            picker.FileTypeFilter.Add(".uproject");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            var selectedFile = await picker.PickSingleFileAsync();
            if (selectedFile is null)
            {
                return;
            }

            _applicationViewModel.UnrealProjectSync.ProjectPath = selectedFile.Path;
            SaveUnrealProjectSyncSettings();
            AppendLog(LogKind.User, $"选择虚幻项目：{selectedFile.Path}");
        }

        private void CheckUnrealProjectSyncButton_Click(object sender, RoutedEventArgs e)
        {
            SaveUnrealProjectSyncSettings();
            _applicationViewModel.UnrealProjectSync.Detect();
            var status = _applicationViewModel.UnrealProjectSync.CanSync ? "通过" : "未完整";
            AppendLog(LogKind.User, $"重新检测虚幻同步台关联：{status}");
        }

        private void WriteUnrealExportScriptButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveUnrealProjectSyncSettings();
                var scriptPath = _applicationViewModel.UnrealProjectSync.GetExportScriptPath();
                ShowFloatingTip(
                    File.Exists(scriptPath) ? InfoBarSeverity.Success : InfoBarSeverity.Warning,
                    File.Exists(scriptPath) ? "已找到内置导出脚本" : "内置导出脚本缺失",
                    scriptPath);
                AppendLog(LogKind.User, $"检查 Unreal 内置导出脚本：{scriptPath}");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "导出脚本检查失败", ex.Message);
                AppendLog(LogKind.Error, "导出脚本检查失败。", ex);
            }
        }

        private async void GetUnrealProjectCharactersButton_Click(object sender, RoutedEventArgs e)
        {
            var scrollOffset = CaptureUnrealProjectSyncScrollOffset();
            try
            {
                SaveUnrealProjectSyncSettings();
                ShowGlobalProgress("获取项目角色", _applicationViewModel.UnrealProjectSync.ProjectPath);
                UpdateGlobalProgress("正在扫描项目角色文件夹...", 15, _applicationViewModel.UnrealProjectSync.ProjectPath);
                await Task.Yield();
                _applicationViewModel.UnrealProjectSync.Detect();
                RestoreUnrealProjectSyncScrollOffset(scrollOffset);
                CompleteGlobalProgress(
                    "项目角色已刷新",
                    $"已扫描到候选角色 {_applicationViewModel.UnrealProjectSync.CharacterCandidates.Count} 个；未获取详情的角色会标记为不是最新数据。");
                AppendLog(
                    LogKind.User,
                    $"虚幻项目角色列表刷新完成：Candidates={_applicationViewModel.UnrealProjectSync.CharacterCandidates.Count}，Project={_applicationViewModel.UnrealProjectSync.ProjectPath}。");
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (OperationCanceledException ex)
            {
                RestoreUnrealProjectSyncScrollOffset(scrollOffset);
                CompleteGlobalProgress("获取项目角色已取消", "项目角色扫描已停止。");
                AppendLog(LogKind.Warning, "项目角色获取已取消。", ex);
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (Exception ex)
            {
                RestoreUnrealProjectSyncScrollOffset(scrollOffset);
                CompleteGlobalProgress("项目角色获取失败", ex.Message);
                AppendLog(LogKind.Error, "项目角色获取失败。", ex);
                await HideGlobalProgressAfterDelayAsync();
            }
        }

        private async void GetSelectedUnrealCharacterDetailsButton_Click(object sender, RoutedEventArgs e)
        {
            var scrollOffset = CaptureUnrealProjectSyncScrollOffset();
            var selectedCodes = _applicationViewModel.UnrealProjectSync.GetSelectedCharacterCodes();
            if (selectedCodes.Length == 0)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未勾选角色", "请先勾选需要获取最新信息的角色卡。");
                AppendLog(LogKind.Warning, "获取选中角色信息被跳过：没有勾选任何角色。");
                return;
            }

            try
            {
                SaveUnrealProjectSyncSettings();
                ShowGlobalProgress("获取选中角色信息", string.Join("、", selectedCodes));
                UpdateGlobalProgress("正在准备 Unreal 导出...", 2, $"{selectedCodes.Length} 个角色");
                await Task.Yield();
                var progress = new Progress<ProgressUpdate>(update =>
                    UpdateGlobalProgress(update.Message, update.Percent, update.Detail, update.IsIndeterminate));
                var result = await _applicationViewModel.UnrealProjectSync.ExportProjectCharactersAsync(
                    selectedCodes,
                    progress,
                    GetGlobalProgressCancellationToken());
                RestoreUnrealProjectSyncScrollOffset(scrollOffset);
                CompleteGlobalProgress(
                    result.ExitCode == 0 ? "选中角色信息已刷新" : "Unreal 导出返回异常",
                    $"已请求 {selectedCodes.Length} 个角色，候选角色 {_applicationViewModel.UnrealProjectSync.CharacterCandidates.Count} 个，导出资产 {result.AssetCount} 个。");
                AppendLog(
                    result.ExitCode == 0 ? LogKind.User : LogKind.Warning,
                    $"选中角色信息获取完成：ExitCode={result.ExitCode}，Selected={string.Join(",", selectedCodes)}，Assets={result.AssetCount}，Manifest={result.ManifestPath}。");
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (OperationCanceledException ex)
            {
                RestoreUnrealProjectSyncScrollOffset(scrollOffset);
                CompleteGlobalProgress("获取选中角色信息已取消", "Unreal 导出进程已停止。");
                AppendLog(LogKind.Warning, "选中角色信息获取已取消。", ex);
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (Exception ex)
            {
                RestoreUnrealProjectSyncScrollOffset(scrollOffset);
                CompleteGlobalProgress("获取选中角色信息失败", ex.Message);
                AppendLog(LogKind.Error, "获取选中角色信息失败。", ex);
                await HideGlobalProgressAfterDelayAsync();
            }
        }

        private void OpenUnrealExportFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var folderPath = _applicationViewModel.UnrealProjectSync.ExportDirectoryPath;
                if (string.IsNullOrWhiteSpace(folderPath))
                {
                    ShowFloatingTip(InfoBarSeverity.Warning, "暂无导出目录", "请先选择 Unreal 项目。");
                    return;
                }

                Directory.CreateDirectory(folderPath);
                Process.Start(new ProcessStartInfo
                {
                    FileName = folderPath,
                    UseShellExecute = true
                });
                AppendLog(LogKind.User, $"打开 Unreal 导出目录：{folderPath}");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "导出目录打开失败", ex.Message);
                AppendLog(LogKind.Error, "导出目录打开失败。", ex);
            }
        }

        private void UnrealSyncDirectionToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleSwitch toggle)
            {
                if (_applicationViewModel.UnrealProjectSync.IsEngineToToolbox == toggle.IsOn)
                {
                    return;
                }

                _applicationViewModel.UnrealProjectSync.IsEngineToToolbox = toggle.IsOn;
                AppendLog(LogKind.User, $"切换虚幻同步方向：{_applicationViewModel.UnrealProjectSync.DirectionTitle}");
            }
        }

        private void UnrealCharacterCandidateButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: UnrealProjectSyncCharacterCandidate candidate })
            {
                var scrollOffset = CaptureUnrealProjectSyncScrollOffset();
                _applicationViewModel.UnrealProjectSync.SelectCharacterCandidate(candidate);
                RestoreUnrealProjectSyncScrollOffset(scrollOffset);
                AppendLog(LogKind.User, $"选择虚幻同步角色候选：{candidate.Code}");
            }
        }

        private async void SyncUnrealCandidateToMaterialLibraryButton_Click(object sender, RoutedEventArgs e)
        {
            var candidate = _applicationViewModel.UnrealProjectSync.SelectedCharacterCandidate;
            if (candidate is null)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择引擎角色", "请先点击一个引擎角色候选。");
                return;
            }

            if (!await ConfirmSyncStaleUnrealCandidateAsync(candidate))
            {
                return;
            }

            try
            {
                ShowGlobalProgress("同步到素材库", candidate.Code);
                UpdateGlobalProgress("正在准备工具箱素材卡...", 5, candidate.Code);
                await Task.Yield();
                var result = await CharacterDesk.EnsureCharacterByCodeAsync(
                    candidate.Code,
                    candidate.DisplayName,
                    GetGlobalProgressCancellationToken());
                UpdateGlobalProgress("正在同步全部 St2 基础素材...", 35, candidate.Code);
                _applicationViewModel.LineArt.Sections.Clear();
                var importedCount = await Task.Run(
                    () => _applicationViewModel.UnrealProjectSync.SyncAllMaterialBucketsToToolbox(result.Character, candidate),
                    GetGlobalProgressCancellationToken());
                UpdateGlobalProgress("正在刷新基础素材页面...", 90, result.Character.StatusDisplayText);
                _applicationViewModel.UnrealProjectSync.Detect();
                await RefreshBaseMaterialsAsync();
                MarkLastEditedModule("LineArt");
                PersistCurrentCharacterSelection();
                StartBaseMaterialWatcher();
                CompleteGlobalProgress("素材库同步完成", $"St2 已同步 {importedCount} 张基础素材。");
                ShowFloatingTip(InfoBarSeverity.Success, "同步到素材库完成", $"已同步 {importedCount} 张基础素材。");
                AppendLog(
                    LogKind.User,
                    $"同步 Unreal 基础素材库到工具箱：{result.Character.Code}，Imported={importedCount}，CreatedNew={result.CreatedNew}。");
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (OperationCanceledException ex)
            {
                CompleteGlobalProgress("素材库同步已取消", candidate.Code);
                AppendLog(LogKind.Warning, "素材库同步已取消。", ex);
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (Exception ex)
            {
                CompleteGlobalProgress("素材库同步失败", ex.Message);
                ShowFloatingTip(InfoBarSeverity.Error, "同步到素材库失败", ex.Message);
                AppendLog(LogKind.Error, "同步到素材库失败。", ex);
                await HideGlobalProgressAfterDelayAsync();
            }
        }

        private async void SyncUnrealCandidateToCharacterInfoButton_Click(object sender, RoutedEventArgs e)
        {
            var candidate = _applicationViewModel.UnrealProjectSync.SelectedCharacterCandidate;
            if (candidate is null)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择引擎角色", "请先点击一个引擎角色候选。");
                return;
            }

            if (!await ConfirmSyncStaleUnrealCandidateAsync(candidate))
            {
                return;
            }

            if (!candidate.CharacterInfo.HasItemData)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Warning,
                    "角色信息未读取成功",
                    candidate.CharacterInfo.ReadStatusText);
                AppendLog(LogKind.Warning, $"同步 St3 角色信息被跳过：{candidate.Code}，{candidate.CharacterInfo.ReadStatusText}");
                return;
            }

            try
            {
                ShowGlobalProgress("同步到角色信息", candidate.Code);
                UpdateGlobalProgress("正在准备工具箱角色卡...", 8, candidate.Code);
                await Task.Yield();
                var ensureResult = await CharacterDesk.EnsureCharacterByCodeAsync(
                    candidate.Code,
                    candidate.DisplayName,
                    GetGlobalProgressCancellationToken());

                UpdateGlobalProgress("正在写入 St3 角色信息...", 45, candidate.CharacterInfo.SourceText);
                await Task.Run(
                    () => _applicationViewModel.UnrealProjectSync.SyncCharacterInfoToToolbox(ensureResult.Character, candidate),
                    GetGlobalProgressCancellationToken());

                UpdateGlobalProgress("正在刷新角色信息页面...", 85, ensureResult.Character.StatusDisplayText);
                await RefreshCharacterInfoAsync();
                if (!string.IsNullOrWhiteSpace(candidate.CharacterInfo.Name))
                {
                    await CharacterDesk.SynchronizeCurrentCharacterDisplayNameAsync(
                        candidate.CharacterInfo.Name,
                        GetGlobalProgressCancellationToken());
                }

                MarkLastEditedModule("UnrealSync");
                PersistCurrentCharacterSelection();
                CompleteGlobalProgress("角色信息同步完成", $"St3 已同步：{CharacterDesk.CurrentCharacterName}");
                ShowFloatingTip(InfoBarSeverity.Success, "同步到角色信息完成", CharacterDesk.CurrentCharacterName);
                AppendLog(
                    LogKind.User,
                    $"同步 Unreal 角色信息到工具箱 St3：{candidate.Code}，Name={candidate.CharacterInfo.Name}，CreatedNew={ensureResult.CreatedNew}。");
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (OperationCanceledException ex)
            {
                CompleteGlobalProgress("角色信息同步已取消", candidate.Code);
                AppendLog(LogKind.Warning, "角色信息同步已取消。", ex);
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (Exception ex)
            {
                CompleteGlobalProgress("角色信息同步失败", ex.Message);
                ShowFloatingTip(InfoBarSeverity.Error, "同步到角色信息失败", ex.Message);
                AppendLog(LogKind.Error, "同步到角色信息失败。", ex);
                await HideGlobalProgressAfterDelayAsync();
            }
        }

        private async void SyncUnrealMaterialBucketToToolboxButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: UnrealProjectSyncMaterialBucket bucket })
            {
                return;
            }

            var candidate = _applicationViewModel.UnrealProjectSync.SelectedCharacterCandidate;
            if (candidate is null)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择引擎角色", "请先点击一个引擎角色候选。");
                return;
            }

            if (!await ConfirmSyncStaleUnrealCandidateAsync(candidate))
            {
                return;
            }

            try
            {
                ShowGlobalProgress("同步对应项", $"{candidate.Code} / {bucket.DisplayName}");
                UpdateGlobalProgress("正在准备工具箱素材卡...", 5, candidate.Code);
                await Task.Yield();
                var ensureResult = await CharacterDesk.EnsureCharacterByCodeAsync(
                    candidate.Code,
                    candidate.DisplayName,
                    GetGlobalProgressCancellationToken());
                var character = ensureResult.Character;

                UpdateGlobalProgress("正在导入 St2 基础素材...", 35, bucket.DisplayName);
                _applicationViewModel.LineArt.Sections.Clear();
                var importedCount = await Task.Run(
                    () => _applicationViewModel.UnrealProjectSync.SyncMaterialBucketToToolbox(character, bucket),
                    GetGlobalProgressCancellationToken());
                UpdateGlobalProgress("正在刷新基础素材页面...", 90, character.StatusDisplayText);
                _applicationViewModel.UnrealProjectSync.Detect();
                await RefreshBaseMaterialsAsync();
                MarkLastEditedModule("LineArt");
                PersistCurrentCharacterSelection();
                CompleteGlobalProgress("对应项同步完成", $"{bucket.DisplayName} 同步 {importedCount} 张。");
                ShowFloatingTip(InfoBarSeverity.Success, "同步到对应项完成", $"{bucket.DisplayName}：{importedCount} 张");
                AppendLog(LogKind.User, $"同步 Unreal 基础素材到工具箱：{candidate.Code}/{bucket.Kind}，Imported={importedCount}。");
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (OperationCanceledException ex)
            {
                CompleteGlobalProgress("对应项同步已取消", bucket.DisplayName);
                AppendLog(LogKind.Warning, "对应项同步已取消。", ex);
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (Exception ex)
            {
                CompleteGlobalProgress("对应项同步失败", ex.Message);
                ShowFloatingTip(InfoBarSeverity.Error, "同步到对应项失败", ex.Message);
                AppendLog(LogKind.Error, "同步到对应项失败。", ex);
                await HideGlobalProgressAfterDelayAsync();
            }
        }

        private async void SyncUnrealCandidateSkillsToToolboxButton_Click(object sender, RoutedEventArgs e)
        {
            var candidate = _applicationViewModel.UnrealProjectSync.SelectedCharacterCandidate;
            if (candidate is null)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择引擎角色", "请先点击一个引擎角色候选。");
                return;
            }

            if (!await ConfirmSyncStaleUnrealCandidateAsync(candidate))
            {
                return;
            }

            await SyncUnrealSkillsAsync(
                candidate,
                "同步全部技能",
                "正在写入 St4 全部技能...",
                () => _applicationViewModel.UnrealProjectSync.SyncAllSkillsToToolbox(CharacterDesk.CurrentCharacter!, candidate),
                count => $"St4 已同步 {count} 个技能阶段。",
                "同步 Unreal 全部技能到工具箱 St4");
        }

        private async void SyncUnrealSkillSlotToToolboxButton_Click(object sender, RoutedEventArgs e)
        {
            var candidate = _applicationViewModel.UnrealProjectSync.SelectedCharacterCandidate;
            var slot = sender switch
            {
                Button { DataContext: UnrealProjectSyncSkillSlotPreview dataContextSlot } => dataContextSlot,
                Button { Tag: UnrealProjectSyncSkillSlotPreview taggedSlot } => taggedSlot,
                _ => null
            };
            if (candidate is null || slot is null)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择技能项", "请先选择引擎角色和需要同步的技能。");
                return;
            }

            if (!await ConfirmSyncStaleUnrealCandidateAsync(candidate))
            {
                return;
            }

            await SyncUnrealSkillsAsync(
                candidate,
                $"同步{slot.DisplayName}",
                $"正在写入 St4：{slot.DisplayName}...",
                () => _applicationViewModel.UnrealProjectSync.SyncSkillSlotToToolbox(CharacterDesk.CurrentCharacter!, candidate, slot),
                count => $"{slot.DisplayName} 已同步 {count} 个阶段。",
                $"同步 Unreal 技能槽到工具箱 St4：{slot.SlotKey}/{slot.DisplayName}");
        }

        private async void SyncUnrealLinkSkillToToolboxButton_Click(object sender, RoutedEventArgs e)
        {
            var candidate = _applicationViewModel.UnrealProjectSync.SelectedCharacterCandidate;
            if (candidate is null ||
                sender is not Button { DataContext: UnrealProjectSyncLinkSkillPreview linkSkill })
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择连携技", "请先选择引擎角色和需要同步的连携技。");
                return;
            }

            if (!await ConfirmSyncStaleUnrealCandidateAsync(candidate))
            {
                return;
            }

            await SyncUnrealSkillsAsync(
                candidate,
                "同步连携技",
                $"正在写入 St4：{linkSkill.Title}...",
                () => _applicationViewModel.UnrealProjectSync.SyncLinkSkillToToolbox(CharacterDesk.CurrentCharacter!, candidate, linkSkill),
                count => $"{linkSkill.Title} 已同步 {count} 个阶段。",
                $"同步 Unreal 连携技到工具箱 St4：{linkSkill.Title}");
        }

        private async void SyncUnrealCandidateSequenceFramesToToolboxButton_Click(object sender, RoutedEventArgs e)
        {
            var candidate = _applicationViewModel.UnrealProjectSync.SelectedCharacterCandidate;
            if (candidate is null)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择引擎角色", "请先点击一个引擎角色候选。");
                return;
            }

            if (!await ConfirmSyncStaleUnrealCandidateAsync(candidate))
            {
                return;
            }

            await SyncUnrealSequenceFramesAsync(
                candidate,
                "同步全部序列",
                "正在写入 St5 全部序列帧...",
                () => _applicationViewModel.UnrealProjectSync.SyncAllSequenceFramesToToolbox(CharacterDesk.CurrentCharacter!, candidate),
                count => $"St5 已同步 {count} 帧。",
                "同步 Unreal 全部序列帧到工具箱 St5");
        }

        private async void SyncUnrealSequenceActionToToolboxButton_Click(object sender, RoutedEventArgs e)
        {
            var candidate = _applicationViewModel.UnrealProjectSync.SelectedCharacterCandidate;
            if (candidate is null ||
                sender is not Button { DataContext: UnrealProjectSyncSequenceActionPreview action })
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择序列项", "请先选择引擎角色和需要同步的序列。");
                return;
            }

            if (!await ConfirmSyncStaleUnrealCandidateAsync(candidate))
            {
                return;
            }

            await SyncUnrealSequenceFramesAsync(
                candidate,
                $"同步{action.Title}",
                $"正在写入 St5：{action.Title}...",
                () => _applicationViewModel.UnrealProjectSync.SyncSequenceActionToToolbox(CharacterDesk.CurrentCharacter!, action),
                count => $"{action.Title} 已同步 {count} 帧。",
                $"同步 Unreal 序列帧到工具箱 St5：{action.ActionCode}/{action.Title}");
        }

        private async void SyncUnrealCandidateBuffsToToolboxButton_Click(object sender, RoutedEventArgs e)
        {
            var candidate = _applicationViewModel.UnrealProjectSync.SelectedCharacterCandidate;
            if (candidate is null)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择引擎角色", "请先点击一个引擎角色候选。");
                return;
            }

            if (!await ConfirmSyncStaleUnrealCandidateAsync(candidate))
            {
                return;
            }

            await SyncUnrealBuffsAsync(
                candidate,
                "同步全部 BUFF",
                "正在写入 St6 全部 BUFF...",
                () => _applicationViewModel.UnrealProjectSync.SyncAllBuffsToToolbox(CharacterDesk.CurrentCharacter!, candidate),
                count => $"St6 已同步 {count} 个 BUFF。",
                "同步 Unreal 全部 BUFF 到工具箱 St6");
        }

        private async void SyncUnrealBuffToToolboxButton_Click(object sender, RoutedEventArgs e)
        {
            var candidate = _applicationViewModel.UnrealProjectSync.SelectedCharacterCandidate;
            if (candidate is null ||
                sender is not Button { DataContext: UnrealProjectSyncBuffPreview buff })
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择 BUFF", "请先选择引擎角色和需要同步的 BUFF。");
                return;
            }

            if (!await ConfirmSyncStaleUnrealCandidateAsync(candidate))
            {
                return;
            }

            await SyncUnrealBuffsAsync(
                candidate,
                $"同步{buff.Title}",
                $"正在写入 St6：{buff.Title}...",
                () => _applicationViewModel.UnrealProjectSync.SyncBuffToToolbox(CharacterDesk.CurrentCharacter!, buff),
                count => $"{buff.Title} 已同步。",
                $"同步 Unreal BUFF 到工具箱 St6：{buff.AssetName}");
        }

        private async Task SyncUnrealSkillsAsync(
            UnrealProjectSyncCharacterCandidate candidate,
            string progressTitle,
            string progressMessage,
            Func<int> syncAction,
            Func<int, string> completeMessage,
            string logPrefix)
        {
            var scrollOffset = CaptureUnrealProjectSyncScrollOffset();
            try
            {
                ShowGlobalProgress(progressTitle, candidate.Code);
                UpdateGlobalProgress("正在准备工具箱角色卡...", 8, candidate.Code);
                await Task.Yield();
                var ensureResult = await CharacterDesk.EnsureCharacterByCodeAsync(
                    candidate.Code,
                    candidate.DisplayName,
                    GetGlobalProgressCancellationToken());

                UpdateGlobalProgress(progressMessage, 45, ensureResult.Character.StatusDisplayText);
                var syncedCount = await Task.Run(syncAction, GetGlobalProgressCancellationToken());

                UpdateGlobalProgress("正在刷新技能页面...", 88, ensureResult.Character.StatusDisplayText);
                await RefreshSkillsAsync();
                RestoreUnrealProjectSyncScrollOffset(scrollOffset);
                MarkLastEditedModule(ToolboxModuleKey.Skills);
                PersistCurrentCharacterSelection();
                CompleteGlobalProgress("技能同步完成", completeMessage(syncedCount));
                ShowFloatingTip(InfoBarSeverity.Success, "同步到技能完成", completeMessage(syncedCount));
                AppendLog(LogKind.User, $"{logPrefix}：{candidate.Code}，Synced={syncedCount}，CreatedNew={ensureResult.CreatedNew}。");
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (OperationCanceledException ex)
            {
                RestoreUnrealProjectSyncScrollOffset(scrollOffset);
                CompleteGlobalProgress("技能同步已取消", candidate.Code);
                AppendLog(LogKind.Warning, "技能同步已取消。", ex);
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (Exception ex)
            {
                RestoreUnrealProjectSyncScrollOffset(scrollOffset);
                CompleteGlobalProgress("技能同步失败", ex.Message);
                ShowFloatingTip(InfoBarSeverity.Error, "同步到技能失败", ex.Message);
                AppendLog(LogKind.Error, "同步到技能失败。", ex);
                await HideGlobalProgressAfterDelayAsync();
            }
        }

        private async Task<bool> ConfirmSyncStaleUnrealCandidateAsync(UnrealProjectSyncCharacterCandidate candidate)
        {
            if (candidate.HasLatestData)
            {
                return true;
            }

            var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "同步旧数据",
                new TextBlock
                {
                    Text = $"角色 {candidate.DisplayName} / {candidate.Code} 当前标记为“不是最新数据”。如果继续同步，可能会把旧的 Unreal 导出内容写入工具箱。\n\n建议先勾选这个角色，点击“获取选中角色信息”，确认最新后再同步。",
                    TextWrapping = TextWrapping.Wrap,
                    Width = 460
                },
                PrimaryButtonText: "继续同步",
                CloseButtonText: string.Empty,
                SecondaryButtonText: "取消",
                DefaultButton: ContentDialogButton.Secondary,
                PrimaryButtonStyle: (Style)Application.Current.Resources["DialogAccentButtonStyle"]));
            if (result == DialogResultKind.Primary)
            {
                AppendLog(LogKind.Warning, $"用户确认使用旧 Unreal 数据同步：{candidate.Code}。");
                return true;
            }

            AppendLog(LogKind.Warning, $"同步已取消：{candidate.Code} 当前不是最新数据。");
            return false;
        }

        private async Task SyncUnrealBuffsAsync(
            UnrealProjectSyncCharacterCandidate candidate,
            string progressTitle,
            string progressMessage,
            Func<int> syncAction,
            Func<int, string> completeMessage,
            string logPrefix)
        {
            var scrollOffset = CaptureUnrealProjectSyncScrollOffset();
            try
            {
                ShowGlobalProgress(progressTitle, candidate.Code);
                UpdateGlobalProgress("正在准备工具箱角色卡...", 8, candidate.Code);
                await Task.Yield();
                var ensureResult = await CharacterDesk.EnsureCharacterByCodeAsync(
                    candidate.Code,
                    candidate.DisplayName,
                    GetGlobalProgressCancellationToken());

                UpdateGlobalProgress(progressMessage, 45, ensureResult.Character.StatusDisplayText);
                var syncedCount = await Task.Run(syncAction, GetGlobalProgressCancellationToken());

                UpdateGlobalProgress("正在刷新 BUFF 页面...", 88, ensureResult.Character.StatusDisplayText);
                await RefreshBuffsAsync();
                RestoreUnrealProjectSyncScrollOffset(scrollOffset);
                MarkLastEditedModule(ToolboxModuleKey.Buffs);
                PersistCurrentCharacterSelection();
                CompleteGlobalProgress("BUFF 同步完成", completeMessage(syncedCount));
                ShowFloatingTip(InfoBarSeverity.Success, "同步到 BUFF 完成", completeMessage(syncedCount));
                AppendLog(LogKind.User, $"{logPrefix}：{candidate.Code}，SyncedBuffs={syncedCount}，CreatedNew={ensureResult.CreatedNew}。");
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (OperationCanceledException ex)
            {
                RestoreUnrealProjectSyncScrollOffset(scrollOffset);
                CompleteGlobalProgress("BUFF 同步已取消", candidate.Code);
                AppendLog(LogKind.Warning, "BUFF 同步已取消。", ex);
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (Exception ex)
            {
                RestoreUnrealProjectSyncScrollOffset(scrollOffset);
                CompleteGlobalProgress("BUFF 同步失败", ex.Message);
                ShowFloatingTip(InfoBarSeverity.Error, "同步到 BUFF 失败", ex.Message);
                AppendLog(LogKind.Error, "同步到 BUFF 失败。", ex);
                await HideGlobalProgressAfterDelayAsync();
            }
        }

        private async Task SyncUnrealSequenceFramesAsync(
            UnrealProjectSyncCharacterCandidate candidate,
            string progressTitle,
            string progressMessage,
            Func<int> syncAction,
            Func<int, string> completeMessage,
            string logPrefix)
        {
            var scrollOffset = CaptureUnrealProjectSyncScrollOffset();
            try
            {
                ShowGlobalProgress(progressTitle, candidate.Code);
                UpdateGlobalProgress("正在准备工具箱角色卡...", 8, candidate.Code);
                await Task.Yield();
                var ensureResult = await CharacterDesk.EnsureCharacterByCodeAsync(
                    candidate.Code,
                    candidate.DisplayName,
                    GetGlobalProgressCancellationToken());

                UpdateGlobalProgress(progressMessage, 45, ensureResult.Character.StatusDisplayText);
                var syncedCount = await Task.Run(syncAction, GetGlobalProgressCancellationToken());

                UpdateGlobalProgress("正在刷新序列帧页面...", 88, ensureResult.Character.StatusDisplayText);
                await RefreshSequenceFramesAsync();
                RestoreUnrealProjectSyncScrollOffset(scrollOffset);
                MarkLastEditedModule(ToolboxModuleKey.SequenceFrames);
                PersistCurrentCharacterSelection();
                CompleteGlobalProgress("序列帧同步完成", completeMessage(syncedCount));
                ShowFloatingTip(InfoBarSeverity.Success, "同步到序列帧完成", completeMessage(syncedCount));
                AppendLog(LogKind.User, $"{logPrefix}：{candidate.Code}，SyncedFrames={syncedCount}，CreatedNew={ensureResult.CreatedNew}。");
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (OperationCanceledException ex)
            {
                RestoreUnrealProjectSyncScrollOffset(scrollOffset);
                CompleteGlobalProgress("序列帧同步已取消", candidate.Code);
                AppendLog(LogKind.Warning, "序列帧同步已取消。", ex);
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (Exception ex)
            {
                RestoreUnrealProjectSyncScrollOffset(scrollOffset);
                CompleteGlobalProgress("序列帧同步失败", ex.Message);
                ShowFloatingTip(InfoBarSeverity.Error, "同步到序列帧失败", ex.Message);
                AppendLog(LogKind.Error, "同步到序列帧失败。", ex);
                await HideGlobalProgressAfterDelayAsync();
            }
        }

        private async void OpenUnrealSequenceFramesButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: UnrealProjectSyncSequenceActionPreview action })
            {
                return;
            }

            var frames = action.OrderedFrames.Count > 0 ? action.OrderedFrames : action.PreviewFrames;
            if (frames.Count == 0)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "暂无序列帧", action.Title);
                return;
            }

            await ShowUnrealSequencePreviewDialogAsync(action, frames);
        }

        private async Task ShowUnrealSequencePreviewDialogAsync(
            UnrealProjectSyncSequenceActionPreview action,
            IReadOnlyList<UnrealProjectSyncExportAssetView> frames)
        {
            CloseUnrealSequencePreviewOverlay();
            StopUnrealSequencePreview();
            _unrealSequencePreviewCache.Clear();
            _unrealSequencePreviewFrames = frames
                .Select(CreateExternalSequenceFrameItem)
                .ToArray();
            _unrealSequencePreviewIndex = 0;
            _unrealSequencePreviewFps = Math.Clamp((int)Math.Round(action.FramesPerSecond <= 0 ? 12 : action.FramesPerSecond), 1, 60);
            _unrealSequencePreviewScale = 1;

            _unrealSequencePreviewImage = new Image
            {
                Width = UnrealSequencePreviewWidth,
                Height = UnrealSequencePreviewHeight,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Stretch = Stretch.Uniform,
                Opacity = 1,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            _unrealSequencePreviewBackImage = new Image
            {
                Width = UnrealSequencePreviewWidth,
                Height = UnrealSequencePreviewHeight,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Stretch = Stretch.Uniform,
                Opacity = 0,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            _unrealSequencePreviewTransform = new CompositeTransform();
            _unrealSequencePreviewImageLayer = new Grid
            {
                Width = UnrealSequencePreviewWidth,
                Height = UnrealSequencePreviewHeight,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = _unrealSequencePreviewTransform
            };
            _unrealSequencePreviewImageLayer.Children.Add(_unrealSequencePreviewBackImage);
            _unrealSequencePreviewImageLayer.Children.Add(_unrealSequencePreviewImage);
            _isUnrealSequencePreviewFrontActive = true;

            _unrealSequencePreviewCanvas = new Grid
            {
                Width = UnrealSequencePreviewWidth,
                Height = UnrealSequencePreviewHeight,
                Background = UnrealSequencePreviewCanvasBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _unrealSequencePreviewCanvas.Children.Add(_unrealSequencePreviewImageLayer);
            _unrealSequencePreviewCanvas.PointerWheelChanged += UnrealSequencePreviewCanvas_PointerWheelChanged;
            _unrealSequencePreviewCanvas.PointerPressed += UnrealSequencePreviewCanvas_PointerPressed;
            _unrealSequencePreviewCanvas.PointerMoved += UnrealSequencePreviewCanvas_PointerMoved;
            _unrealSequencePreviewCanvas.PointerReleased += UnrealSequencePreviewCanvas_PointerReleased;
            _unrealSequencePreviewCanvas.PointerCanceled += UnrealSequencePreviewCanvas_PointerCanceled;
            _unrealSequencePreviewCanvas.PointerCaptureLost += UnrealSequencePreviewCanvas_PointerCaptureLost;
            _unrealSequencePreviewCanvas.DoubleTapped += UnrealSequencePreviewCanvas_DoubleTapped;

            _unrealSequencePreviewFrameText = new TextBlock
            {
                Foreground = UnrealSequencePreviewSubtleTextBrush,
                FontSize = 16,
                TextWrapping = TextWrapping.Wrap
            };

            _unrealSequencePreviewPlayPauseIcon = new FontIcon { Glyph = "\uE769" };
            var playPauseButton = new Button
            {
                Style = (Style)Application.Current.Resources["IconToolButtonStyle"],
                Content = _unrealSequencePreviewPlayPauseIcon
            };
            ToolTipService.SetToolTip(playPauseButton, "播放/暂停");
            playPauseButton.Click += UnrealSequencePreviewPlayPauseButton_Click;

            var controls = new Grid
            {
                ColumnSpacing = 8,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            controls.Children.Add(new TextBlock
            {
                Text = $"帧率 {_unrealSequencePreviewFps}",
                Foreground = UnrealSequencePreviewTextBrush,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });

            var previousButton = new Button
            {
                Style = (Style)Application.Current.Resources["IconToolButtonStyle"],
                Content = new FontIcon { Glyph = "\uE892" }
            };
            ToolTipService.SetToolTip(previousButton, "上一帧");
            previousButton.Click += UnrealSequencePreviewPreviousButton_Click;
            Grid.SetColumn(previousButton, 1);
            controls.Children.Add(previousButton);

            Grid.SetColumn(playPauseButton, 2);
            controls.Children.Add(playPauseButton);

            var nextButton = new Button
            {
                Style = (Style)Application.Current.Resources["IconToolButtonStyle"],
                Content = new FontIcon { Glyph = "\uE893" }
            };
            ToolTipService.SetToolTip(nextButton, "下一帧");
            nextButton.Click += UnrealSequencePreviewNextButton_Click;
            Grid.SetColumn(nextButton, 3);
            controls.Children.Add(nextButton);

            var hintText = new TextBlock
            {
                Text = "滚轮缩放，拖动查看，双击归位",
                Foreground = UnrealSequencePreviewSubtleTextBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(hintText, 4);
            controls.Children.Add(hintText);

            var content = new Grid
            {
                RowSpacing = 12,
                Width = UnrealSequencePreviewWidth,
                Background = UnrealSequencePreviewDialogBrush
            };
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new StackPanel { Spacing = 4 };
            header.Children.Add(new TextBlock
            {
                Text = "序列帧预览器",
                Foreground = UnrealSequencePreviewTextBrush,
                FontSize = 22,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            header.Children.Add(new TextBlock
            {
                Text = action.Title,
                Foreground = UnrealSequencePreviewSubtleTextBrush,
                FontSize = 16
            });
            header.Children.Add(new TextBlock
            {
                Text = $"{action.DetailLine1} / {action.DetailLine2}",
                Foreground = UnrealSequencePreviewSubtleTextBrush,
                FontSize = 16
            });
            content.Children.Add(header);

            Grid.SetRow(_unrealSequencePreviewCanvas, 1);
            content.Children.Add(_unrealSequencePreviewCanvas);
            Grid.SetRow(_unrealSequencePreviewFrameText, 2);
            content.Children.Add(_unrealSequencePreviewFrameText);
            Grid.SetRow(controls, 3);
            content.Children.Add(controls);

            var card = new Border
            {
                Width = UnrealSequencePreviewWidth + 48,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(24),
                Background = UnrealSequencePreviewDialogBrush,
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 68, 68, 68)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Child = content
            };
            card.Tapped += (_, e) => e.Handled = true;
            card.RightTapped += (_, e) => e.Handled = true;

            var closeButton = new Button
            {
                Content = "关闭",
                Width = 306,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            closeButton.Click += (_, _) => CloseUnrealSequencePreviewOverlay();
            var footer = new Border
            {
                Padding = new Thickness(0, 18, 0, 0),
                Child = closeButton
            };
            Grid.SetRow(footer, 4);
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.Children.Add(footer);

            _unrealSequencePreviewOverlay = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(210, 0, 0, 0)),
                IsTabStop = true,
                Visibility = Visibility.Visible
            };
            Canvas.SetZIndex(_unrealSequencePreviewOverlay, 181);
            Grid.SetRowSpan(_unrealSequencePreviewOverlay, Math.Max(1, RootGrid.RowDefinitions.Count));
            _unrealSequencePreviewOverlay.Children.Add(card);
            _unrealSequencePreviewOverlay.KeyDown += UnrealSequencePreviewOverlay_KeyDown;
            _unrealSequencePreviewOverlay.RightTapped += UnrealSequencePreviewOverlay_RightTapped;
            RootGrid.Children.Add(_unrealSequencePreviewOverlay);

            ResetUnrealSequencePreviewTransform();
            var failures = await _unrealSequencePreviewCache.PreloadDecodedAsync(_unrealSequencePreviewFrames);
            if (failures.Count > 0)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Warning,
                    "部分序列帧读取失败",
                    failures.Count == 1 ? failures[0].FileName : $"{failures[0].FileName} 等 {failures.Count} 张图片无法读取。");
                AppendLog(
                    LogKind.Warning,
                    "同步台序列帧预加载部分失败：" +
                    string.Join(
                        Environment.NewLine,
                        failures
                            .Take(8)
                            .Select(failure => $"{failure.FileName} | {failure.ExceptionType ?? "LoadError"} | {failure.Message} | {failure.FilePath}")) +
                    (failures.Count > 8 ? $"{Environment.NewLine}... 还有 {failures.Count - 8} 张失败。" : string.Empty));
            }

            UpdateUnrealSequencePreviewFrame();
            StartUnrealSequencePreview();
            _unrealSequencePreviewOverlay.Focus(FocusState.Programmatic);
        }

        private static SequenceFrameItem CreateExternalSequenceFrameItem(UnrealProjectSyncExportAssetView frame, int index)
        {
            return new SequenceFrameItem(
                frame.IsBlank ? string.Empty : frame.ExportedFilePath,
                frame.IsBlank ? string.Empty : frame.FileUri,
                frame.IsBlank ? "空白帧" : frame.AssetName,
                frame.IsBlank ? $"blank-{index}" : frame.FileUri,
                index,
                0,
                0,
                frame.IsBlank || !string.IsNullOrWhiteSpace(frame.FileUri),
                DateTime.Now,
                frame.IsBlank);
        }

        private void StartUnrealSequencePreview()
        {
            if (_unrealSequencePreviewFrames.Count == 0)
            {
                return;
            }

            _unrealSequencePreviewTimer ??= DispatcherQueue.CreateTimer();
            _unrealSequencePreviewTimer.Tick -= UnrealSequencePreviewTimer_Tick;
            _unrealSequencePreviewTimer.Tick += UnrealSequencePreviewTimer_Tick;
            _unrealSequencePreviewTimer.Interval = TimeSpan.FromMilliseconds(1000d / Math.Clamp(_unrealSequencePreviewFps, 1, 60));
            _isUnrealSequencePreviewPlaying = true;
            UpdateUnrealSequencePreviewPlayPauseVisual();
            _unrealSequencePreviewTimer.Start();
        }

        private void StopUnrealSequencePreview()
        {
            _unrealSequencePreviewTimer?.Stop();
            _isUnrealSequencePreviewPlaying = false;
            UpdateUnrealSequencePreviewPlayPauseVisual();
        }

        private void UnrealSequencePreviewTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            StepUnrealSequencePreview(1, keepPlaying: true);
        }

        private void UnrealSequencePreviewPlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isUnrealSequencePreviewPlaying)
            {
                StopUnrealSequencePreview();
                return;
            }

            StartUnrealSequencePreview();
        }

        private void UnrealSequencePreviewPreviousButton_Click(object sender, RoutedEventArgs e)
        {
            StopUnrealSequencePreview();
            StepUnrealSequencePreview(-1, keepPlaying: false);
        }

        private void UnrealSequencePreviewNextButton_Click(object sender, RoutedEventArgs e)
        {
            StopUnrealSequencePreview();
            StepUnrealSequencePreview(1, keepPlaying: false);
        }

        private void StepUnrealSequencePreview(int direction, bool keepPlaying)
        {
            if (_unrealSequencePreviewFrames.Count == 0)
            {
                StopUnrealSequencePreview();
                return;
            }

            _unrealSequencePreviewIndex = (_unrealSequencePreviewIndex + direction + _unrealSequencePreviewFrames.Count) % _unrealSequencePreviewFrames.Count;
            if (!UpdateUnrealSequencePreviewFrame() && keepPlaying)
            {
                for (var attempt = 0; attempt < _unrealSequencePreviewFrames.Count; attempt++)
                {
                    _unrealSequencePreviewIndex = (_unrealSequencePreviewIndex + 1) % _unrealSequencePreviewFrames.Count;
                    if (UpdateUnrealSequencePreviewFrame())
                    {
                        return;
                    }
                }

                StopUnrealSequencePreview();
            }
        }

        private bool UpdateUnrealSequencePreviewFrame()
        {
            if (_unrealSequencePreviewImage is null ||
                _unrealSequencePreviewBackImage is null ||
                _unrealSequencePreviewFrameText is null)
            {
                return false;
            }

            if (_unrealSequencePreviewFrames.Count == 0)
            {
                ClearUnrealSequencePreviewImages();
                _unrealSequencePreviewFrameText.Text = "暂无预览帧";
                return false;
            }

            _unrealSequencePreviewIndex = Math.Clamp(_unrealSequencePreviewIndex, 0, _unrealSequencePreviewFrames.Count - 1);
            var frame = _unrealSequencePreviewFrames[_unrealSequencePreviewIndex];
            var frameIndexText = $"{_unrealSequencePreviewIndex + 1:D3}/{_unrealSequencePreviewFrames.Count:D3}";
            if (frame.IsBlank)
            {
                ClearUnrealSequencePreviewImages();
                _unrealSequencePreviewFrameText.Text = $"{frameIndexText}  空白帧";
                return true;
            }

            if (_unrealSequencePreviewCache.IsFailed(frame.FilePath))
            {
                ClearUnrealSequencePreviewImages();
                _unrealSequencePreviewFrameText.Text = $"{frameIndexText}  {frame.FileName} 读取失败";
                return false;
            }

            if (_unrealSequencePreviewCache.TryGet(frame.CacheKey, out var bitmap))
            {
                SwapUnrealSequencePreviewImage(bitmap);
                _unrealSequencePreviewFrameText.Text = $"{frameIndexText}  {frame.FileName}";
                return true;
            }

            if (!File.Exists(frame.FilePath))
            {
                _unrealSequencePreviewCache.MarkFailed(frame.FilePath);
                ClearUnrealSequencePreviewImages();
                _unrealSequencePreviewFrameText.Text = $"{frameIndexText}  {frame.FileName} 不存在";
                return false;
            }

            _unrealSequencePreviewFrameText.Text = $"{frameIndexText}  {frame.FileName} 等待预加载";
            return false;
        }

        private void SwapUnrealSequencePreviewImage(ImageSource bitmap)
        {
            if (_unrealSequencePreviewImage is null || _unrealSequencePreviewBackImage is null)
            {
                return;
            }

            var nextImage = _isUnrealSequencePreviewFrontActive
                ? _unrealSequencePreviewBackImage
                : _unrealSequencePreviewImage;
            var previousImage = _isUnrealSequencePreviewFrontActive
                ? _unrealSequencePreviewImage
                : _unrealSequencePreviewBackImage;

            if (ReferenceEquals(previousImage.Source, bitmap))
            {
                previousImage.Opacity = 1;
                nextImage.Opacity = 0;
                return;
            }

            nextImage.Source = bitmap;
            nextImage.Opacity = 1;
            previousImage.Opacity = 0;
            _isUnrealSequencePreviewFrontActive = !_isUnrealSequencePreviewFrontActive;
        }

        private void ClearUnrealSequencePreviewImages()
        {
            if (_unrealSequencePreviewImage is not null)
            {
                _unrealSequencePreviewImage.Opacity = 0;
            }

            if (_unrealSequencePreviewBackImage is not null)
            {
                _unrealSequencePreviewBackImage.Opacity = 0;
            }
        }

        private void UpdateUnrealSequencePreviewPlayPauseVisual()
        {
            if (_unrealSequencePreviewPlayPauseIcon is not null)
            {
                _unrealSequencePreviewPlayPauseIcon.Glyph = _isUnrealSequencePreviewPlaying ? "\uE769" : "\uE768";
            }
        }

        private void UnrealSequencePreviewCanvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            if (_unrealSequencePreviewCanvas is null || _unrealSequencePreviewTransform is null)
            {
                return;
            }

            var point = e.GetCurrentPoint(_unrealSequencePreviewCanvas);
            var previousScale = _unrealSequencePreviewScale;
            _unrealSequencePreviewScale = Math.Clamp(
                _unrealSequencePreviewScale * (point.Properties.MouseWheelDelta > 0 ? 1.1 : 0.9),
                0.35,
                6);
            var actualZoomFactor = _unrealSequencePreviewScale / previousScale;
            var canvasCenterX = _unrealSequencePreviewCanvas.ActualWidth / 2;
            var canvasCenterY = _unrealSequencePreviewCanvas.ActualHeight / 2;
            var pointerOffsetX = point.Position.X - canvasCenterX - _unrealSequencePreviewTransform.TranslateX;
            var pointerOffsetY = point.Position.Y - canvasCenterY - _unrealSequencePreviewTransform.TranslateY;
            _unrealSequencePreviewTransform.TranslateX -= pointerOffsetX * (actualZoomFactor - 1);
            _unrealSequencePreviewTransform.TranslateY -= pointerOffsetY * (actualZoomFactor - 1);
            _unrealSequencePreviewTransform.ScaleX = _unrealSequencePreviewScale;
            _unrealSequencePreviewTransform.ScaleY = _unrealSequencePreviewScale;
        }

        private void UnrealSequencePreviewCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_unrealSequencePreviewCanvas is null)
            {
                return;
            }

            var point = e.GetCurrentPoint(_unrealSequencePreviewCanvas);
            if (point.Properties.PointerUpdateKind is not PointerUpdateKind.LeftButtonPressed)
            {
                return;
            }

            _isPanningUnrealSequencePreview = true;
            _lastUnrealSequencePreviewPointerPosition = point.Position;
            _unrealSequencePreviewCanvas.CapturePointer(e.Pointer);
        }

        private void UnrealSequencePreviewCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isPanningUnrealSequencePreview ||
                _unrealSequencePreviewCanvas is null ||
                _unrealSequencePreviewTransform is null)
            {
                return;
            }

            var point = e.GetCurrentPoint(_unrealSequencePreviewCanvas);
            _unrealSequencePreviewTransform.TranslateX += point.Position.X - _lastUnrealSequencePreviewPointerPosition.X;
            _unrealSequencePreviewTransform.TranslateY += point.Position.Y - _lastUnrealSequencePreviewPointerPosition.Y;
            _lastUnrealSequencePreviewPointerPosition = point.Position;
        }

        private void UnrealSequencePreviewCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            EndUnrealSequencePreviewPan(e);
        }

        private void UnrealSequencePreviewCanvas_PointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            EndUnrealSequencePreviewPan(e);
        }

        private void UnrealSequencePreviewCanvas_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            _isPanningUnrealSequencePreview = false;
        }

        private void UnrealSequencePreviewCanvas_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            ResetUnrealSequencePreviewTransform();
        }

        private void EndUnrealSequencePreviewPan(PointerRoutedEventArgs e)
        {
            _isPanningUnrealSequencePreview = false;
            _unrealSequencePreviewCanvas?.ReleasePointerCapture(e.Pointer);
        }

        private void ResetUnrealSequencePreviewTransform()
        {
            _unrealSequencePreviewScale = 1;
            if (_unrealSequencePreviewTransform is null)
            {
                return;
            }

            _unrealSequencePreviewTransform.ScaleX = 1;
            _unrealSequencePreviewTransform.ScaleY = 1;
            _unrealSequencePreviewTransform.TranslateX = 0;
            _unrealSequencePreviewTransform.TranslateY = 0;
        }

        private void CloseUnrealSequencePreviewOverlay()
        {
            StopUnrealSequencePreview();
            if (_unrealSequencePreviewOverlay is not null)
            {
                RootGrid.Children.Remove(_unrealSequencePreviewOverlay);
                _unrealSequencePreviewOverlay = null;
            }

            _isPanningUnrealSequencePreview = false;
            ClearUnrealSequencePreviewImages();
            _unrealSequencePreviewImage?.ClearValue(Image.SourceProperty);
            _unrealSequencePreviewBackImage?.ClearValue(Image.SourceProperty);
            _unrealSequencePreviewCache.Clear();
            _unrealSequencePreviewImage = null;
            _unrealSequencePreviewBackImage = null;
            _unrealSequencePreviewImageLayer = null;
            _unrealSequencePreviewCanvas = null;
            _unrealSequencePreviewFrameText = null;
            _unrealSequencePreviewPlayPauseIcon = null;
            _unrealSequencePreviewTransform = null;
        }

        private void UnrealSequencePreviewOverlay_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                CloseUnrealSequencePreviewOverlay();
                e.Handled = true;
            }
        }

        private void UnrealSequencePreviewOverlay_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            CloseUnrealSequencePreviewOverlay();
            e.Handled = true;
        }

        private double CaptureUnrealProjectSyncScrollOffset()
        {
            return UnrealProjectSyncPage.VerticalOffset;
        }

        private void RestoreUnrealProjectSyncScrollOffset(double verticalOffset)
        {
            _ = DispatcherQueue.TryEnqueue(() =>
                UnrealProjectSyncPage.ChangeView(null, verticalOffset, null, disableAnimation: true));
        }

        private void SaveUnrealProjectSyncSettings()
        {
            Settings.SetUnrealProjectSyncPaths(
                _applicationViewModel.UnrealProjectSync.EnginePath,
                _applicationViewModel.UnrealProjectSync.ProjectPath);
        }
    }
}
