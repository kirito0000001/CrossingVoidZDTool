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
    /// <summary>第六步「蓝图置入」：角色蓝图白名单字段与 2DInfor 三张数据表。</summary>
    public sealed partial class MainWindow
    {
        private async void ApplyUnrealBlueprintSetupButton_Click(object sender, RoutedEventArgs e)
        {
            LogUserOperation("应用 Unreal 蓝图置入");
            var sync = _applicationViewModel.UnrealProjectSync;
            var character = sync.SelectedSource?.DraftCharacter;
            var selectedIds = sync.GetSelectedBlueprintSetupIds();
            if (character is null)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择已完成角色", "请先在左侧选择一个已完成角色。");
                return;
            }
            if (selectedIds.Count == 0)
            {
                ShowFloatingTip(InfoBarSeverity.Informational, "没有选择字段", "请至少勾选一项待写入内容。");
                return;
            }
            if (!TryBeginUnrealWorkflowOperation())
            {
                return;
            }

            sync.SetApplyingBlueprintSetup(true);
            ShowGlobalProgress("写入蓝图数据", character.Code);
            try
            {
                // 备份开关只看整体设置，和第三、五步同一条规则。
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
                    plan.Caption(WorkflowProgressPlan.Apply, "正在写入蓝图数据"),
                    plan[WorkflowProgressPlan.Apply].At(0),
                    $"{selectedIds.Count} 项",
                    true);
                var result = await ExecuteUnrealBlueprintSetupAsync(character, apply: true, selectedIds, plan);
                // 写完后脚本会重扫一遍，这里直接用复查结果刷新界面，
                // 勾选清空避免把已写入的项再算作待处理。
                sync.SetBlueprintSetupResult(
                    result,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    selectPendingByDefault: false);
                CompleteGlobalProgress(
                    "蓝图数据写入完成",
                    $"已写入 {result.AppliedStableIds.Count} 项，剩余待写入 {sync.BlueprintSetupPendingCount} 项。");
                AppendLog(LogKind.Info,
                    $"[Blueprint Setup] applied={result.AppliedStableIds.Count} saved={result.SavedAssets.Count} pending={sync.BlueprintSetupPendingCount}");
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (OperationCanceledException ex)
            {
                CompleteGlobalProgress("蓝图数据写入已取消", character.Code);
                AppendLog(LogKind.Warning, "写入 Unreal 蓝图数据已取消。", ex);
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (Exception ex)
            {
                sync.FailBlueprintSetup(ex.Message);
                CompleteGlobalProgress("蓝图数据写入失败", ex.Message);
                ShowFloatingTip(InfoBarSeverity.Error, "蓝图数据写入失败", ex.Message);
                AppendLog(LogKind.Error, "写入 Unreal 蓝图数据失败。", ex);
                await HideGlobalProgressAfterDelayAsync();
            }
            finally
            {
                sync.SetApplyingBlueprintSetup(false);
                EndUnrealWorkflowOperation();
            }
        }

        private async Task ReloadUnrealBlueprintSetupStepAsync(UnrealProjectSyncViewModel sync)
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

            ShowGlobalProgress("检测蓝图数据", character.Code);
            try
            {
                sync.ReturnToWorkflowStep(6);
                var result = await ExecuteUnrealBlueprintSetupAsync(character, apply: false, Array.Empty<string>());
                sync.SetBlueprintSetupResult(result);
                CompleteGlobalProgress(
                    "蓝图数据检测完成",
                    $"待写入 {sync.BlueprintSetupPendingCount} 项，错误 {sync.BlueprintSetupErrorCount} 项。");
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (OperationCanceledException ex)
            {
                CompleteGlobalProgress("蓝图数据检测已取消", character.Code);
                AppendLog(LogKind.Warning, "检测 Unreal 蓝图数据已取消。", ex);
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (Exception ex)
            {
                sync.FailBlueprintSetup(ex.Message);
                CompleteGlobalProgress("蓝图数据检测失败", ex.Message);
                ShowFloatingTip(InfoBarSeverity.Error, "蓝图数据检测失败", ex.Message);
                AppendLog(LogKind.Error, "检测 Unreal 蓝图数据失败。", ex);
                await HideGlobalProgressAfterDelayAsync();
            }
            finally
            {
                EndUnrealWorkflowOperation();
            }
        }

        /// <summary>
        /// 目标值全部由工具箱这边推导，Unreal 只负责比对和写入，
        /// 所以扫描和写入用的是同一份请求载荷，只差一个 Mode。
        /// </summary>
        private async Task<UnrealBlueprintSetupResult> ExecuteUnrealBlueprintSetupAsync(
            CharacterCard character,
            bool apply,
            IReadOnlyCollection<string> selectedStableIds,
            WorkflowProgressPlan? progressPlan = null)
        {
            var sync = _applicationViewModel.UnrealProjectSync;
            var service = new UnrealBlueprintSetupService();
            var workFolder = Path.Combine(
                Path.GetDirectoryName(sync.ProjectPath)!,
                "Intermediate",
                "ZDToolboxBridge",
                character.Code,
                "BlueprintSetup");
            Directory.CreateDirectory(workFolder);
            var requestPath = Path.Combine(workFolder, apply ? "apply.request.json" : "scan.request.json");
            var resultPath = Path.Combine(workFolder, apply ? "apply.result.json" : "scan.result.json");

            var request = service.BuildRequest(
                character,
                new CharacterInfoService().Load(character),
                new CharacterSkillsService().Load(character),
                new BaseMaterialService().LoadSections(character),
                selectedStableIds);
            request.Mode = apply ? "Apply" : "Scan";
            service.SaveRequest(requestPath, request);

            var progressPath = Path.Combine(workFolder, apply ? "apply.progress.json" : "scan.progress.json");
            var offlineStartInfo = service.BuildProcessStartInfo(
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
    }
}
