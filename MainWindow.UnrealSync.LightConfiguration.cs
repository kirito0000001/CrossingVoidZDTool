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
    /// <summary>第三步「基础配置」：入队语音、Item、MetaSound 和语音并发。</summary>
    public sealed partial class MainWindow
    {
        private async void ApplyUnrealLightConfigurationButton_Click(object sender, RoutedEventArgs e)
        {
            LogUserOperation("应用 Unreal 基础配置", startsRun: true);
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
                // **缺前置条件就直说，不去替用户重跑前一步**（晓桀 2026-09-24：
                // 「之后如果缺失什么前置条件，就直接用报错log和红色tips了，
                // 这样子就不用重复检测第二步的东西了」）。
                //
                // 原来这里先 `ValidatePublishCharacterFolders` —— 那是第 1 步的目录校验，
                // 顺带还会**重刷第 1 步那一整张检查表**，失败时再把人踢回第 1 步；
                // 接着又拿"规整没做完"把人踢回第 2 步。两条都不做了：
                // 报错 log + 红色 tip 说清楚，人留在原地。
                if (sync.NormalizationItems.Any(item => !item.IsResolved))
                {
                    const string blocked = "还有未完成的素材规整项，先把它们处理掉再应用基础配置。";
                    AppendLog(LogKind.Error, $"[Light Config] {blocked}");
                    ShowFloatingTip(InfoBarSeverity.Error, "还有未完成的素材规整", blocked);
                    CompleteGlobalProgress("基础配置未应用", blocked);
                    await HideGlobalProgressAfterDelayAsync();
                    return;
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
                    // 仍然汇总到第 1 步的检查列表（那是"配置错误"的落点，回到第 1 步看得到），
                    // 但**不再把人挪走** —— 他站在第 3 步，红条就在眼前。
                    sync.SetFoundationConfigurationError(foundationError);
                    AppendLog(
                        LogKind.Error,
                        $"[Light Config] 基础配置依赖有问题：{FormatSyncLogValue(foundationError.ErrorMessage)}");
                    if (IsOfflineAssetLoadFailure(foundationError))
                    {
                        // 离线实例加载不到资产 —— 这是**扫描环境**的问题，不是工程资产的问题。
                        // 以前这种假报也会把人踢回第 1 步，看着像"第 1 步的东西坏了"（2026-09-24 实测）。
                        AppendLog(
                            LogKind.Warning,
                            "离线实例加载不到依赖资产，已留在第 3 步；建议重试，或关掉 Unreal 编辑器后重跑。");
                        ShowFloatingTip(
                            InfoBarSeverity.Warning,
                            "离线实例读不到依赖资产",
                            "已留在基础配置这一步；建议重试，或关掉 Unreal 编辑器后重跑。");
                    }
                    else
                    {
                        ShowFloatingTip(InfoBarSeverity.Error, "基础配置依赖有问题", foundationError.ErrorMessage);
                    }
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

        /// <summary>
        /// 这条错误是不是"离线实例加载不到资产"造成的**假报**。
        ///
        /// 两个条件同时成立才算：
        /// ① 上一次任务确实是离线跑的（<see cref="_lastUnrealTaskRanOffline"/>）；
        /// ② 文案是脚本里 `_load_asset` 失败时写的那种"未找到…"。
        /// 只认①会吞掉真缺资产的情况，只认②会把在线跑出来的真错误也当环境问题 —— 所以两条都要。
        /// </summary>
        private bool IsOfflineAssetLoadFailure(UnrealLightConfigurationResultItem item) =>
            _lastUnrealTaskRanOffline &&
            !string.IsNullOrEmpty(item.ErrorMessage) &&
            item.ErrorMessage.Contains("未找到", StringComparison.Ordinal);

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
                // **只刷新自己这一步**（晓桀 2026-09-24：「包括刷新也只刷新自己的」）。
                //
                // 原来这里先调 `ValidatePublishCharacterFolders` —— 那是第 1 步的目录校验，
                // 顺带还会**重刷第 1 步那一整张检查表**，失败时再把人踢回第 1 步。
                // 现在都不做了：缺前置条件由这一轮扫描自己扫出来（脚本逐条 `_load_asset`
                // 过一遍），用一条报错 log + 一个红色 tip 说清楚，人留在原地。
                sync.ReturnToWorkflowStep(3);
                var result = await ExecuteUnrealLightConfigurationAsync(character, apply: false, Array.Empty<string>());
                sync.SetLightConfigurationResult(result);
                var foundationError = result.Items.FirstOrDefault(item =>
                    item.StableId == "foundation.assets" &&
                    item.Status == UnrealLightConfigurationStatus.Error);
                if (foundationError is not null)
                {
                    // 仍然汇总到第 1 步的检查列表（那是"配置错误"的落点，回到第 1 步看得到），
                    // 但**不再把人挪走** —— 他站在第 3 步，红条就在眼前。
                    sync.SetFoundationConfigurationError(foundationError);
                    AppendLog(
                        LogKind.Error,
                        $"[Light Config] 基础配置依赖有问题：{FormatSyncLogValue(foundationError.ErrorMessage)}");
                    if (IsOfflineAssetLoadFailure(foundationError))
                    {
                        AppendLog(
                            LogKind.Warning,
                            "离线实例加载不到依赖资产，已留在第 3 步；建议重试，或关掉 Unreal 编辑器后重跑。");
                        ShowFloatingTip(
                            InfoBarSeverity.Warning,
                            "离线实例读不到依赖资产",
                            "已留在基础配置这一步；建议重试，或关掉 Unreal 编辑器后重跑。");
                    }
                    else
                    {
                        ShowFloatingTip(InfoBarSeverity.Error, "基础配置依赖有问题", foundationError.ErrorMessage);
                    }
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
        /// **同步收尾时顺手做的基础配置预检**的日志出口（从
        /// `MainWindow.UnrealSync.Publish.cs` 搬过来：名字是这一步的，人就该住这一步的文件）。
        ///
        /// 扫描失败不该把已经做完的同步一起判失败，所以这里不抛；但也不能像以前那样
        /// 连 <see cref="UnrealLightConfigurationResult.Succeeded"/> 都不看就扔进视图层——
        /// 失败时界面上只会摆出一条「无法读取基础配置」，Unreal 那边真正的报错
        /// 一个字都留不下来，排查只能靠猜。
        /// </summary>
        private void LogLightConfigurationPreflight(string characterCode, UnrealLightConfigurationResult result)
        {
            if (result.Succeeded)
            {
                return;
            }

            AppendLog(
                LogKind.Warning,
                $"[Light Config Preflight] character={characterCode} succeeded=false " +
                $"items={result.Items.Count} error={FormatSyncLogValue(result.ErrorMessage)}");
        }

        /// <summary>
        /// 按整体设置备份 Unreal 项目。第二步和第四步共用同一份实现，
        /// 整体设置对两步都是唯一开关：关掉就一律不备份。
        /// </summary>
    }
}
