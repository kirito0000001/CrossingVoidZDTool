using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;
using CrossingVoidZDTool.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Input;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CrossingVoidZDTool
{
    /// <summary>
    /// 虚幻同步台的公共部分：路径选择、方向切换、来源列表、
    /// 流程占用闸门、备份，以及几个跨步骤共用的日志助手。
    /// </summary>
    public sealed partial class MainWindow
    {
        private int _workflowStepAfterPublishDetection;
        private bool _isUnrealPublishRunning;
        private bool _isUnrealWorkflowOperationRunning;

        private static string FormatSyncLogValue(string? value, int maxLength = 180)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "<empty>";
            }

            var compact = value.Replace("\\r", " ").Replace("\\n", " ").Trim();
            return compact.Length <= maxLength ? compact : compact[..maxLength] + "...";
        }

        private void LogSequenceChanges(string prefix, IEnumerable<UnrealBridgeChange> changes)
        {
            var count = 0;
            foreach (var change in changes.Where(item => item.Module == UnrealBridgeModule.SequenceFrames))
            {
                var canExecute = UnrealBridgePublishSupportPolicy.CanExecute(change);
                var toolboxValue = FormatSyncLogValue(change.ToolboxItem?.PayloadJson);
                var unrealValue = FormatSyncLogValue(change.UnrealItem?.PayloadJson);
                AppendDiagnosticLog(LogKind.Info,
                    $"{prefix} stableId={change.StableId} kind={change.Kind} selected={change.IsSelected} canExecute={canExecute} group={FormatSyncLogValue(change.SequenceGroupKey)} display={FormatSyncLogValue(change.DisplayName)} toolbox={toolboxValue} unreal={unrealValue}");
                count++;
            }

            if (count > 0)
            {
                AppendLog(LogKind.Info, $"{prefix} 共 {count} 条明细，已写入 runtime.log。");
            }
        }

        private bool TryBeginUnrealWorkflowOperation()
        {
            if (_isUnrealWorkflowOperationRunning)
            {
                ShowFloatingTip(InfoBarSeverity.Informational, "Unreal 任务正在执行", "请等待当前读取或同步完成后再操作。");
                return false;
            }

            _isUnrealWorkflowOperationRunning = true;
            _applicationViewModel.UnrealProjectSync.SetWorkflowOperationRunning(true);
            return true;
        }

        private void EndUnrealWorkflowOperation()
        {
            _isUnrealWorkflowOperationRunning = false;
            _applicationViewModel.UnrealProjectSync.SetWorkflowOperationRunning(false);
        }

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
            LogUserOperation($"选择虚幻引擎：{selectedFile.Path}");
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
            LogUserOperation($"选择虚幻项目：{selectedFile.Path}");
        }

        private void UnrealSyncSelectionCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox checkBox || checkBox.DataContext is not UnrealSyncSelectionTreeItem item)
            {
                return;
            }

            var state = item.IsChecked == true ? "勾选" : item.IsChecked == false ? "取消勾选" : "部分勾选";
            var scope = item.IsLeaf ? "同步项" : "同步组";
            LogUserOperation($"{state}{scope}：{item.DisplayName} / {item.StableId}");
        }

        private void UnrealSyncDirectionToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleSwitch toggle)
            {
                var directionChanged = _applicationViewModel.UnrealProjectSync.IsEngineToToolbox != toggle.IsOn;
                if (directionChanged)
                {
                    _applicationViewModel.UnrealProjectSync.IsEngineToToolbox = toggle.IsOn;
                }

                _applicationViewModel.UnrealProjectSync.RefreshDraftSources(CharacterDesk.CompletedCharacters);
                if (directionChanged)
                {
                    LogUserOperation($"切换虚幻同步方向：{_applicationViewModel.UnrealProjectSync.DirectionTitle}");
                }
            }
        }

        private void UnrealSyncSourceListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not UnrealSyncSourceItem source)
            {
                return;
            }

            if (!source.IsAvailable)
            {
                ShowFloatingTip(InfoBarSeverity.Informational, "功能尚未开放", $"{source.DisplayName} 将在项目通用素材阶段接入。");
                return;
            }

            _applicationViewModel.UnrealProjectSync.SelectSource(source);
            LogUserOperation($"选择虚幻同步来源：{source.DisplayName} / {source.SecondaryText}");
        }

        /// <summary>
        /// 备份没有可读的百分比，只能看产物长了多少。
        /// 这一段实测一分多钟，静止不动的进度条会让人以为卡死。
        /// </summary>
        private async Task ReportBackupProgressWhileRunningAsync(
            Process backupProcess,
            string backupPath,
            WorkflowProgressBand band)
        {
            var token = GetGlobalProgressCancellationToken();
            var startedAt = DateTime.UtcNow;
            while (!backupProcess.HasExited && !token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(700, token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                var elapsed = DateTime.UtcNow - startedAt;
                var writtenMb = 0L;
                try
                {
                    var info = new FileInfo(backupPath);
                    if (info.Exists)
                    {
                        writtenMb = info.Length / (1024 * 1024);
                    }
                }
                catch (IOException)
                {
                    // 压缩进程正持有这个文件，读不到大小就只报用时。
                }

                // 以实测约 66 秒为参照推进，封顶 95% 免得压完前就顶到头。
                var ratio = Math.Min(95d, elapsed.TotalSeconds / 66d * 100d);
                UpdateGlobalProgress(
                    "阶段 2/4 · 正在压缩备份 Unreal 项目",
                    band.At(ratio),
                    writtenMb > 0
                        ? $"已写入 {writtenMb} MB · 用时 {elapsed.Minutes:00}:{elapsed.Seconds:00}"
                        : $"用时 {elapsed.Minutes:00}:{elapsed.Seconds:00}",
                    false);
            }
        }

        /// <summary>
        /// 桥接会话里顺带做的复扫导出是否可用。
        ///
        /// 只认清单文件的写入时间：同会话导出失败时脚本只记日志、不抛异常，
        /// 此时清单还是同步前那一份，拿它复扫会把「还剩多少差异」算错。
        /// 判不准就退回独立导出——多花十几秒，总好过报错误的结果。
        /// </summary>
        private bool TrySkipRescanExport(string manifestPath, DateTime syncStartedAtUtc)
        {
            try
            {
                var info = new FileInfo(manifestPath);
                if (!info.Exists || info.Length == 0)
                {
                    return false;
                }

                var fresh = info.LastWriteTimeUtc > syncStartedAtUtc;
                AppendLog(LogKind.Info,
                    $"[Post-Sync Export] reused={fresh} manifest={manifestPath} writtenAt={info.LastWriteTimeUtc:HH:mm:ss} syncStartedAt={syncStartedAtUtc:HH:mm:ss}");
                return fresh;
            }
            catch (IOException)
            {
                return false;
            }
        }

        /// <summary>
        /// 导出退出码非 0、但清单确实写出来了时的提醒。
        /// commandlet 只要编辑器在别处报过错（例如某个蓝图编译不过）就会返回非 0，
        /// 那跟导出成没成功无关，但值得让人知道工程里有东西坏了。
        /// </summary>
        private void LogExportWarning(UnrealProjectSyncExportRunResult result)
        {
            if (!string.IsNullOrWhiteSpace(result.Warning))
            {
                AppendLog(LogKind.Warning, $"[Export] {result.Warning}");
            }
        }

        private async Task BackupUnrealProjectIfRequestedAsync(
            string enginePath,
            string projectPath,
            string characterCode,
            bool planTouchesExistingAssets,
            WorkflowProgressBand band = default)
        {
            var decision = UnrealBridgeBackupPolicy.Decide(
                Settings.BackupBeforeUnrealSync,
                planTouchesExistingAssets);
            if (decision != UnrealBridgeBackupDecision.Backup)
            {
                if (decision == UnrealBridgeBackupDecision.SkipWithRiskWarning)
                {
                    // 关了开关就不拦，但这一批会改写或删除既有资产，留一条记录便于事后追。
                    AppendLog(LogKind.Warning,
                        $"[Backup] character={characterCode} 已跳过备份：整体设置已关闭「同步前备份」，而本批包含更新或删除项。");
                }

                return;
            }

            if (band.End <= band.Start)
            {
                band = new WorkflowProgressBand(40, 55);
            }

            UpdateGlobalProgress("阶段 2/4 · 正在压缩备份 Unreal 项目", band.At(0), projectPath, true);
            var backupPath = Path.Combine(
                Path.GetDirectoryName(projectPath)!,
                "Saved",
                "ZDToolboxBackups",
                $"{characterCode}-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            var backupInfo = new UnrealBridgeBackupService().BuildZipProjectStartInfo(enginePath, projectPath, backupPath);
            using var backupProcess = Process.Start(backupInfo)
                ?? throw new InvalidOperationException("无法启动 Unreal 项目备份进程。");
            var backupOutput = backupProcess.StandardOutput.ReadToEndAsync();
            var backupError = backupProcess.StandardError.ReadToEndAsync();
            // 压缩整个工程实测约一分钟，是这条流程里最长的一段。
            // 没有可读的百分比，就报已经写出多少 MB，至少让人看得出它在动。
            await ReportBackupProgressWhileRunningAsync(backupProcess, backupPath, band);
            await backupProcess.WaitForExitAsync(GetGlobalProgressCancellationToken());
            if (backupProcess.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Unreal 项目备份失败，退出码 {backupProcess.ExitCode}。\n{await backupOutput}\n{await backupError}");
            }

            AppendLog(LogKind.Info, $"[Backup] character={characterCode} path={backupPath}");
        }

        private async Task CreateUnrealProjectBackupAsync(
            string characterCode,
            string enginePath,
            string projectPath)
        {
            UpdateGlobalProgress("正在压缩备份 Unreal 项目...", 20, projectPath, true);
            var backupPath = Path.Combine(
                Path.GetDirectoryName(projectPath)!,
                "Saved",
                "ZDToolboxBackups",
                $"{characterCode}-基础配置-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            var backupInfo = new UnrealBridgeBackupService().BuildZipProjectStartInfo(
                enginePath,
                projectPath,
                backupPath);
            using var backupProcess = Process.Start(backupInfo)
                ?? throw new InvalidOperationException("无法启动 Unreal 项目备份进程。");
            var backupOutput = backupProcess.StandardOutput.ReadToEndAsync();
            var backupError = backupProcess.StandardError.ReadToEndAsync();
            await backupProcess.WaitForExitAsync(GetGlobalProgressCancellationToken());
            if (backupProcess.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Unreal 项目备份失败，退出码 {backupProcess.ExitCode}。\n{await backupOutput}\n{await backupError}");
            }
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
