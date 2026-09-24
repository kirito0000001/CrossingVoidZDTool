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
                HandleLightConfigurationFoundationIssue(sync, result);
                if (!result.Succeeded)
                {
                    throw new InvalidOperationException(result.ErrorMessage);
                }

                var remaining = result.Items.Count(item => item.Status == UnrealLightConfigurationStatus.Pending);
                var errors = result.Items.Count(item => item.Status == UnrealLightConfigurationStatus.Error);
                var unavailable = result.Items.Count(item => item.Status == UnrealLightConfigurationStatus.Unavailable);
                CompleteGlobalProgress(
                    "基础配置已应用",
                    $"已验证 {result.AppliedStableIds.Count} 项；剩余 {remaining} 项，错误 {errors} 项"
                    + (unavailable > 0 ? $"，本次读不到 {unavailable} 项（请连上编辑器重跑）" : string.Empty) + "。");
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
        /// 处理"基础配置依赖"那一条：**读不到** 和 **真有问题** 两条路分开走。
        ///
        /// 第一种是**运行环境**的事 —— 离线实例（`UnrealEditor-Cmd -run=pythonscript`）
        /// 加载不了 WidgetBlueprint / MetaSoundSource 这类"类由编辑器模块提供"的资产。
        /// 脚本现在会自己分清（先问资产注册表：资产在、只是这个实例加载不了它），
        /// 所以这里**认状态**，不再靠"离线 + 文案含未找到"去猜。
        /// 这一档只记一条 warning + 一个黄色 tip，**不算错误、也不往第 1 步挂错误** ——
        /// 那等于把假报升级成"底层检测红"（2026-09-24 实测踩过）。
        ///
        /// 第二种才是真问题：错误 log + 红色 tip，并汇总到第 1 步的检查列表
        /// （那是"配置错误"的落点，回到第 1 步看得到），但**不把人挪走**。
        /// </summary>
        private void HandleLightConfigurationFoundationIssue(
            UnrealProjectSyncViewModel sync,
            UnrealLightConfigurationResult result)
        {
            var item = result.Items.FirstOrDefault(entry =>
                entry.StableId == "foundation.assets" &&
                entry.Status is UnrealLightConfigurationStatus.Error or UnrealLightConfigurationStatus.Unavailable);
            if (item is null)
            {
                return;
            }

            if (item.Status == UnrealLightConfigurationStatus.Unavailable)
            {
                AppendLog(
                    LogKind.Warning,
                    $"[Light Config] 本次运行读不到依赖资产，已留在第 3 步：{FormatSyncLogValue(item.ErrorMessage)}");
                ShowFloatingTip(
                    InfoBarSeverity.Warning,
                    "离线实例读不到依赖资产",
                    "已留在基础配置这一步；请连上 Unreal 编辑器后重跑。");
                return;
            }

            sync.SetFoundationConfigurationError(item);
            AppendLog(
                LogKind.Error,
                $"[Light Config] 基础配置依赖有问题：{FormatSyncLogValue(item.ErrorMessage)}");
            ShowFloatingTip(InfoBarSeverity.Error, "基础配置依赖有问题", item.ErrorMessage);
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
                // **只刷新自己这一步**（晓桀 2026-09-24：「包括刷新也只刷新自己的」）。
                //
                // 原来这里先调 `ValidatePublishCharacterFolders` —— 那是第 1 步的目录校验，
                // 顺带还会**重刷第 1 步那一整张检查表**，失败时再把人踢回第 1 步。
                // 现在都不做了：缺前置条件由这一轮扫描自己扫出来（脚本逐条 `_load_asset`
                // 过一遍），用一条报错 log + 一个红色 tip 说清楚，人留在原地。
                sync.ReturnToWorkflowStep(3);
                var result = await ExecuteUnrealLightConfigurationAsync(character, apply: false, Array.Empty<string>());
                sync.SetLightConfigurationResult(result);
                HandleLightConfigurationFoundationIssue(sync, result);
                var pending = result.Items.Count(item => item.Status == UnrealLightConfigurationStatus.Pending);
                var errors = result.Items.Count(item => item.Status == UnrealLightConfigurationStatus.Error);
                var unavailable = result.Items.Count(item => item.Status == UnrealLightConfigurationStatus.Unavailable);
                CompleteGlobalProgress(
                    "基础配置检测完成",
                    $"待设置 {pending} 项，错误 {errors} 项"
                    + (unavailable > 0 ? $"，本次读不到 {unavailable} 项（请连上编辑器重跑）" : string.Empty) + "。");
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
