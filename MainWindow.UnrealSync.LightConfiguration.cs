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
    /// <summary>第四步「基础配置」：入队语音、Item、MetaSound 和语音并发。</summary>
    public sealed partial class MainWindow
    {
        private async void ApplyUnrealLightConfigurationButton_Click(object sender, RoutedEventArgs e)
        {
            LogUserOperation("应用 Unreal 基础配置");
            var sync = _applicationViewModel.UnrealProjectSync;
            var character = sync.SelectedSource?.DraftCharacter;
            var selectedIds = sync.GetSelectedLightConfigurationIds();
            if (character is null)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择已完成角色", "请先在左侧选择一个已完成角色。");
                return;
            }
            if (selectedIds.Count == 0)
            {
                ShowFloatingTip(InfoBarSeverity.Informational, "没有选择配置", "请至少勾选一项待设置内容。");
                return;
            }
            if (!TryBeginUnrealWorkflowOperation())
            {
                return;
            }

            sync.SetApplyingLightConfiguration(true);
            ShowGlobalProgress("应用基础配置", character.Code);
            try
            {
                try
                {
                    sync.ValidatePublishCharacterFolders(character.Code);
                }
                catch
                {
                    sync.ReturnToWorkflowStep(1);
                    throw;
                }
                if (sync.NormalizationItems.Any(item => !item.IsResolved))
                {
                    sync.ReturnToWorkflowStep(2);
                    throw new InvalidOperationException("第二步仍有未完成的素材规整项目。");
                }

                var plan = WorkflowProgressPlan.ForStepApply(Settings.BackupBeforeUnrealSync);
                if (Settings.BackupBeforeUnrealSync)
                {
                    UpdateGlobalProgress(
                        plan.Caption(WorkflowProgressPlan.Backup, "正在压缩备份 Unreal 项目"),
                        plan[WorkflowProgressPlan.Backup].At(0),
                        character.Code,
                        true);
                    await CreateUnrealProjectBackupAsync(character.Code, sync.EnginePath, sync.ProjectPath);
                }

                UpdateGlobalProgress(
                    plan.Caption(WorkflowProgressPlan.Apply, "正在写入基础配置"),
                    plan[WorkflowProgressPlan.Apply].At(0),
                    $"{selectedIds.Count} 项",
                    true);
                var result = await ExecuteUnrealLightConfigurationAsync(character, apply: true, selectedIds, plan);
                sync.SetLightConfigurationResult(
                    result,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    selectPendingByDefault: false);
                var foundationError = result.Items.FirstOrDefault(item =>
                    item.StableId == "foundation.assets" &&
                    item.Status == UnrealLightConfigurationStatus.Error);
                if (foundationError is not null)
                {
                    sync.SetFoundationConfigurationError(foundationError);
                    sync.ReturnToWorkflowStep(1);
                }
                if (!result.Succeeded)
                {
                    throw new InvalidOperationException(result.ErrorMessage);
                }

                var remaining = result.Items.Count(item => item.Status == UnrealLightConfigurationStatus.Pending);
                var errors = result.Items.Count(item => item.Status == UnrealLightConfigurationStatus.Error);
                CompleteGlobalProgress("基础配置已应用", $"已验证 {result.AppliedStableIds.Count} 项；剩余 {remaining} 项，错误 {errors} 项。");
                ShowFloatingTip(InfoBarSeverity.Success, "基础配置已应用", $"已验证 {result.AppliedStableIds.Count} 项配置。");
                AppendLog(LogKind.User, $"应用 Unreal 基础配置：{character.Code}，Applied={result.AppliedStableIds.Count}，Remaining={remaining}，Errors={errors}。");
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (OperationCanceledException ex)
            {
                sync.FailLightConfiguration("基础配置已取消，当前选择仍保留。");
                CompleteGlobalProgress("基础配置已取消", character.Code);
                AppendLog(LogKind.Warning, "应用 Unreal 基础配置已取消。", ex);
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (Exception ex)
            {
                sync.FailLightConfiguration(ex.Message);
                CompleteGlobalProgress("基础配置失败", ex.Message);
                ShowFloatingTip(InfoBarSeverity.Error, "基础配置失败", ex.Message);
                AppendLog(LogKind.Error, "应用 Unreal 基础配置失败。", ex);
                await HideGlobalProgressAfterDelayAsync();
            }
            finally
            {
                sync.SetApplyingLightConfiguration(false);
                EndUnrealWorkflowOperation();
            }
        }

        private async Task ReloadUnrealLightConfigurationStepAsync(UnrealProjectSyncViewModel sync)
        {
            var character = sync.SelectedSource?.DraftCharacter;
            if (character is null)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择已完成角色", "请先在左侧选择一个已完成角色。");
                return;
            }
            if (!TryBeginUnrealWorkflowOperation())
            {
                return;
            }

            ShowGlobalProgress("检测基础配置", character.Code);
            try
            {
                try
                {
                    sync.ValidatePublishCharacterFolders(character.Code);
                }
                catch
                {
                    sync.ReturnToWorkflowStep(1);
                    throw;
                }
                sync.ReturnToWorkflowStep(4);
                var result = await ExecuteUnrealLightConfigurationAsync(character, apply: false, Array.Empty<string>());
                sync.SetLightConfigurationResult(result);
                var foundationError = result.Items.FirstOrDefault(item =>
                    item.StableId == "foundation.assets" &&
                    item.Status == UnrealLightConfigurationStatus.Error);
                if (foundationError is not null)
                {
                    sync.SetFoundationConfigurationError(foundationError);
                    sync.ReturnToWorkflowStep(1);
                }
                var pending = result.Items.Count(item => item.Status == UnrealLightConfigurationStatus.Pending);
                var errors = result.Items.Count(item => item.Status == UnrealLightConfigurationStatus.Error);
                CompleteGlobalProgress("基础配置检测完成", $"待设置 {pending} 项，错误 {errors} 项。");
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (OperationCanceledException ex)
            {
                CompleteGlobalProgress("基础配置检测已取消", character.Code);
                AppendLog(LogKind.Warning, "检测 Unreal 基础配置已取消。", ex);
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (Exception ex)
            {
                sync.FailLightConfiguration(ex.Message);
                CompleteGlobalProgress("基础配置检测失败", ex.Message);
                ShowFloatingTip(InfoBarSeverity.Error, "基础配置检测失败", ex.Message);
                AppendLog(LogKind.Error, "检测 Unreal 基础配置失败。", ex);
                await HideGlobalProgressAfterDelayAsync();
            }
            finally
            {
                EndUnrealWorkflowOperation();
            }
        }

        private async Task<UnrealLightConfigurationResult> ExecuteUnrealLightConfigurationAsync(
            CharacterCard character,
            bool apply,
            IReadOnlyCollection<string> selectedStableIds,
            WorkflowProgressPlan? progressPlan = null)
        {
            var sync = _applicationViewModel.UnrealProjectSync;
            var service = new UnrealLightConfigurationService();
            var workFolder = Path.Combine(
                Path.GetDirectoryName(sync.ProjectPath)!,
                "Intermediate",
                "ZDToolboxBridge",
                character.Code,
                "LightConfiguration");
            Directory.CreateDirectory(workFolder);
            var requestPath = Path.Combine(workFolder, apply ? "apply.request.json" : "scan.request.json");
            var resultPath = Path.Combine(workFolder, apply ? "apply.result.json" : "scan.result.json");
            var request = service.BuildRequest(character, apply, selectedStableIds);
            service.SaveRequest(requestPath, request);
            var progressPath = Path.Combine(workFolder, apply ? "apply.progress.json" : "scan.progress.json");
            var offlineStartInfo = service.BuildProcessStartInfoWithProgress(
                sync.EnginePath,
                sync.ProjectPath,
                requestPath,
                resultPath,
                progressPath);
            var launch = new UnrealPythonTaskExecutionService().BuildLaunch(
                sync.EnginePath,
                sync.ProjectPath,
                service.GetScriptPath(),
                Path.Combine(workFolder, apply ? "apply.remote-job.json" : "scan.remote-job.json"),
                offlineStartInfo);
            // 虚幻那一侧十几秒是纯静默的，脚本会把阶段写进进度文件，
            // 这里把它转成进度条上的分段推进。
            var plan = progressPlan ?? (apply
                ? WorkflowProgressPlan.ForStepApply(includesBackup: false)
                : WorkflowProgressPlan.ForStepScan());
            var phase = apply ? WorkflowProgressPlan.Apply : WorkflowProgressPlan.Scan;
            var band = plan[phase];
            var progress = new Progress<UnrealExportProgressState>(state => UpdateGlobalProgress(
                plan.Caption(phase, state.Message),
                band.At(state.Percent),
                state.Detail,
                state.IsIndeterminate));
            return await RunUnrealTaskWithOfflineFallbackAsync(
                launch,
                offlineStartInfo,
                startInfo => service.ExecuteAsync(
                    startInfo,
                    resultPath,
                    GetGlobalProgressCancellationToken(),
                    progressPath,
                    progress));
        }

        /// <summary>
        /// 按整体设置备份 Unreal 项目。第三步和第五步共用同一份实现，
        /// 整体设置对两步都是唯一开关：关掉就一律不备份。
        /// </summary>
    }
}
