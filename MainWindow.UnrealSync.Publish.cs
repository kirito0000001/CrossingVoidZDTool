using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;
using CrossingVoidZDTool.Services.Atlas;
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
    /// 第二步「同步素材」和第四步「序列同步」。两步共用同一条差异检测与发布链路，
    /// 只是导出范围、默认勾选和执行阶段不同。
    /// </summary>
    public sealed partial class MainWindow : IUnrealSyncPublishHost
    {
        /// <summary>第三、五步共同的发布编排（见 UnrealSyncPublishController）。</summary>
        private UnrealSyncPublishController? _publishController;

        // B4：把这套编排需要的壳能力显式列出来（接口定义见 ViewModels/UnrealSyncPublishHost.cs）。
        // 实现体全部是转发，流程本身一行没动——这一步只是让编译器确认
        // 「名单列全了、签名对得上」，下一步才能安全地把方法体整体搬进 Controller。
        SettingsViewModel IUnrealSyncPublishHost.Settings => Settings;

        bool IUnrealSyncPublishHost.IsPublishRunning
        {
            get => _isUnrealPublishRunning;
            set => _isUnrealPublishRunning = value;
        }

        int IUnrealSyncPublishHost.WorkflowStepAfterPublishDetection
        {
            get => _workflowStepAfterPublishDetection;
            set => _workflowStepAfterPublishDetection = value;
        }

        void IUnrealSyncPublishHost.AppendLog(LogKind kind, string message, Exception? exception, bool sticky, int? stepOverride) =>
            AppendLog(kind, message, exception, sticky, stepOverride);

        void IUnrealSyncPublishHost.AppendDiagnosticLog(LogKind kind, string message) =>
            AppendDiagnosticLog(kind, message);

        void IUnrealSyncPublishHost.AppendRuntimeLog(string line) => AppendRuntimeLog(line);

        void IUnrealSyncPublishHost.LogUserOperation(string action, bool startsRun) =>
            LogUserOperation(action, startsRun);

        string IUnrealSyncPublishHost.FormatSyncLogValue(string? value, int maxLength) =>
            FormatSyncLogValue(value, maxLength);

        void IUnrealSyncPublishHost.LogExportWarning(UnrealProjectSyncExportRunResult result) =>
            LogExportWarning(result);

        void IUnrealSyncPublishHost.LogSequenceChanges(string prefix, IEnumerable<UnrealBridgeChange> changes) =>
            LogSequenceChanges(prefix, changes);

        void IUnrealSyncPublishHost.LogLightConfigurationPreflight(string characterCode, UnrealLightConfigurationResult result) =>
            LogLightConfigurationPreflight(characterCode, result);

        void IUnrealSyncPublishHost.ShowFloatingTip(InfoBarSeverity severity, string title, string message) =>
            ShowFloatingTip(severity, title, message);

        void IUnrealSyncPublishHost.ShowGlobalProgress(string title, string detail) =>
            ShowGlobalProgress(title, detail);

        void IUnrealSyncPublishHost.UpdateGlobalProgress(string message, double percent, string? detail, bool isIndeterminate) =>
            UpdateGlobalProgress(message, percent, detail, isIndeterminate);

        void IUnrealSyncPublishHost.CompleteGlobalProgress(string message, string? detail) =>
            CompleteGlobalProgress(message, detail);

        Task IUnrealSyncPublishHost.HideGlobalProgressAfterDelayAsync(int delayMilliseconds) =>
            HideGlobalProgressAfterDelayAsync(delayMilliseconds);

        CancellationToken IUnrealSyncPublishHost.GetGlobalProgressCancellationToken() =>
            GetGlobalProgressCancellationToken();

        bool IUnrealSyncPublishHost.TryBeginUnrealWorkflowOperation() => TryBeginUnrealWorkflowOperation();

        void IUnrealSyncPublishHost.EndUnrealWorkflowOperation() => EndUnrealWorkflowOperation();

        Task IUnrealSyncPublishHost.DetectUnrealPublishChangesAsync() => DetectUnrealPublishChangesAsync();

        Task IUnrealSyncPublishHost.BackupUnrealProjectIfRequestedAsync(
            string enginePath,
            string projectPath,
            string characterCode,
            bool planTouchesExistingAssets,
            WorkflowProgressBand band) =>
            BackupUnrealProjectIfRequestedAsync(enginePath, projectPath, characterCode, planTouchesExistingAssets, band);

        Task<UnrealLightConfigurationResult> IUnrealSyncPublishHost.ExecuteUnrealLightConfigurationAsync(
            CharacterCard character,
            bool apply,
            IReadOnlyCollection<string> selectedStableIds,
            WorkflowProgressPlan? progressPlan) =>
            ExecuteUnrealLightConfigurationAsync(character, apply, selectedStableIds, progressPlan);

        bool IUnrealSyncPublishHost.TrySkipRescanExport(string manifestPath, DateTime syncStartedAtUtc) =>
            TrySkipRescanExport(manifestPath, syncStartedAtUtc);

        private async void PublishCurrentCharacterAssetsToUnrealButton_Click(object sender, RoutedEventArgs e) =>
            await PublishCurrentCharacterAssetsToUnrealAsync();

        /// <summary>
        /// 「同步素材到虚幻」的完整编排（第三、五步共用）。
        ///
        /// 从按钮处理器里拆出来，是为了让整套流程**能 await**：
        /// 以前它整个长在 <c>async void</c> 里，调用方拿不到「跑完了」的信号，
        /// 异常也只能靠处理器自己那三个 catch 兜。
        /// 这与隔壁 <c>DetectUnrealPublishChangesButton_Click</c> 是同一个模具。
        ///
        /// 主体（编排）在 <c>UnrealSyncPublishController</c> 里，可用假 Host 单测；
        /// 这里只负责"把壳能力交过去 + 收尾提示"。搬的时候先把控制器摸到的
        /// MainWindow 成员列全，再一次性搬，别跟别的改动混着做。
        /// </summary>
        internal async Task PublishCurrentCharacterAssetsToUnrealAsync()
        {
            _publishController ??= new UnrealSyncPublishController(this, _applicationViewModel.UnrealProjectSync);
            await _publishController.PublishCurrentCharacterAssetsToUnrealAsync();
        }

        private async void DetectUnrealPublishChangesButton_Click(object sender, RoutedEventArgs e) =>
            await DetectUnrealPublishChangesAsync();

        /// <summary>
        /// 第三、五步的差异检测。
        ///
        /// 单独拆出可等待的版本，是因为按钮处理器是 async void：
        /// 流程编排 await 不到它，会在检测还没跑完时就往下走。
        /// </summary>
        private async Task DetectUnrealPublishChangesAsync()
        {
            LogUserOperation("检测工具箱到 Unreal 的差异", startsRun: true);
            var character = _applicationViewModel.UnrealProjectSync.SelectedSource?.DraftCharacter;
            if (character is null)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择已完成角色", "请先在左侧选择一个已完成角色。");
                return;
            }
            if (!TryBeginUnrealWorkflowOperation())
            {
                return;
            }

            try
            {
                var baseline = new UnrealBridgeStateService().Load(character, _applicationViewModel.UnrealProjectSync.ProjectPath);
                var selectionBeforeDetection = _applicationViewModel.UnrealProjectSync.GetSelectedGroupAndLeafStableIds();
                AppendLog(LogKind.Info, $"[Refresh Selection] before={selectionBeforeDetection.Count} ids={string.Join(",", selectionBeforeDetection.Take(12))}");
                // 校验哪套目录、导哪些数据、默认勾不勾，口径都在
                // UnrealProjectSyncViewModel.Step4SequenceSync.cs 里，别在这儿再写一遍 == 4。
                _applicationViewModel.UnrealProjectSync.ValidateFoldersForStep(
                    _workflowStepAfterPublishDetection,
                    _applicationViewModel.UnrealProjectSync.ProjectPath,
                    character.Code);
                ShowGlobalProgress("检测同步差异", character.Code);
                // 导出范围的口径归第 4 步自己那个文件（`ResolveDetectionExportScope`），
                // 别在这儿再就地写一遍三元 —— 同一条口径两处写法迟早漂开。
                var detectionExportScope = UnrealProjectSyncViewModel.ResolveDetectionExportScope(
                    _applicationViewModel.UnrealProjectSync.WorkflowStep,
                    _workflowStepAfterPublishDetection);
                var detectionExportRun = await _applicationViewModel.UnrealProjectSync.ExportProjectCharactersAsync(
                    [character.Code],
                    new Progress<ProgressUpdate>(update =>
                        UpdateGlobalProgress(update.Message, Math.Min(70, update.Percent * 0.7), update.Detail, update.IsIndeterminate)),
                    GetGlobalProgressCancellationToken(),
                    detectionExportScope);
                LogExportWarning(detectionExportRun);
                // 这里以前还调 `ValidatePublishCharacterFolders` —— 它内部会
                // **重刷第 1 步那一整张检查表**（`RefreshFoundationChecks`），顺带校验素材目录，
                // 而这两件事都不是"检测当前这一步"该干的（晓桀：「只管自己阶段的」）。
                // 目录口径上面已经按**本步**问过 `ValidateFoldersForStep`；
                // Item 资产真缺，下面那句 `candidate is null` 会明确报出来。
                var candidate = _applicationViewModel.UnrealProjectSync.CharacterCandidates.FirstOrDefault(item =>
                    string.Equals(item.Code, character.Code, StringComparison.OrdinalIgnoreCase));
                if (candidate is null)
                {
                    throw new InvalidOperationException(
                        $"未找到 Unreal 角色 Item 资产。\n" +
                        $"正确名称示例：Item_{character.Code}\n" +
                        $"期望路径：/Game/ITems/CharItemS/Item_{character.Code}.Item_{character.Code}\n" +
                        $"请检查 Unreal 内容浏览器中的资产名称和路径是否正确。");
                }
                var detectionResult = await Task.Run(() =>
                {
                    var toolboxSnapshot = new UnrealBridgeToolboxSnapshotService().BuildForSynchronization(character);
                    var unrealSnapshot = new UnrealBridgeSemanticSnapshotService().Build(candidate);
                    var allowedModules = UnrealProjectSyncViewModel.DetectionModulesFor(
                        _workflowStepAfterPublishDetection);
                    toolboxSnapshot = toolboxSnapshot with { Items = toolboxSnapshot.Items.Where(item => allowedModules.Contains(item.Module)).ToArray() };
                    unrealSnapshot = unrealSnapshot with { Items = unrealSnapshot.Items.Where(item => allowedModules.Contains(item.Module)).ToArray() };
                    if (UnrealProjectSyncViewModel.UsesRecordedSequenceContent(_workflowStepAfterPublishDetection))
                    {
                        // 同上：序列检测要比「素材内容」，Unreal 侧用上次同步记下的摘要。
                        unrealSnapshot = UnrealBridgeSequenceFingerprintService.ApplyRecordedContent(character, unrealSnapshot);
                    }
                    return new UnrealBridgeDiffService().Compare(
                        toolboxSnapshot,
                        unrealSnapshot,
                        UnrealBridgeDirection.PublishToUnreal,
                        baseline)
                        .Select(change => change with
                        {
                            IsSelected = change.IsSelected && UnrealBridgePublishSupportPolicy.CanExecute(change)
                        })
                        .ToArray();
                }, GetGlobalProgressCancellationToken());
                var changes = detectionResult;
                // 把发布阶段拨到这一步该在的地方。
                // 不拨的话，会话里残留的"序列动画轨道"会让 FilterPublishChanges 把素材变更整批丢掉 ——
                // 界面就变成"共检查 0 项、无差异 0 项"（2026-09-24 实测）。
                if (_workflowStepAfterPublishDetection is 2 or 4)
                {
                    var stageForStep = UnrealProjectSyncViewModel.PublishStageFor(_workflowStepAfterPublishDetection);
                    _applicationViewModel.UnrealProjectSync.SelectedPublishStage =
                        _applicationViewModel.UnrealProjectSync.PublishStages.First(stage => stage.Stage == stageForStep);
                }
                changes = _applicationViewModel.UnrealProjectSync.FilterPublishChanges(changes).ToArray();
                var selectPendingByDefault =
                    UnrealProjectSyncViewModel.SelectsPendingChangesByDefault(_workflowStepAfterPublishDetection);
                AppendLog(LogKind.Info, $"[Step5 Detection] character={character.Code} changes={changes.Length} stepAfterPublishDetection={_workflowStepAfterPublishDetection} selectPendingByDefault={selectPendingByDefault}");
                LogSequenceChanges("[Step5 Detection Item]", changes);
                var matchedChanges = changes
                    .Where(change => change.ToolboxItem is not null && change.UnrealItem is not null)
                    .ToArray();
                AppendLog(LogKind.Info, $"[Step5 Detection Summary] character={character.Code} added={changes.Count(change => change.Kind == UnrealBridgeChangeKind.Added)} updated={changes.Count(change => change.Kind is UnrealBridgeChangeKind.Updated or UnrealBridgeChangeKind.Renamed)} deleted={changes.Count(change => change.Kind == UnrealBridgeChangeKind.DeleteCandidate)} conflicts={changes.Count(change => change.Kind == UnrealBridgeChangeKind.Conflict)}");
                // 旧版状态使用了不可比较的复合哈希；第一次使用新协议时，以当前已配对端点建立迁移基线。
                if (baseline is null && matchedChanges.Length > 0)
                {
                    var migratedBaseline = new UnrealBridgeBaselineService().BuildFromChanges(
                        character.Code,
                        _applicationViewModel.UnrealProjectSync.ProjectPath,
                        matchedChanges);
                    new UnrealBridgeStateService().Save(
                        character,
                        _applicationViewModel.UnrealProjectSync.ProjectPath,
                        migratedBaseline);
                    AppendLog(LogKind.Info, $"已为 {character.Code} 建立源文件哈希同步基线。");
                }
                await _applicationViewModel.UnrealProjectSync.SetPublishSelectionTreeAsync(
                    changes,
                    UnrealBridgePublishSupportPolicy.CanExecute,
                    GetGlobalProgressCancellationToken(),
                    selectPendingByDefault: selectPendingByDefault);
                _applicationViewModel.UnrealProjectSync.RestoreSelectionState(selectionBeforeDetection);
                // 与同步路径对称：建树时写入的是默认勾选态，恢复完必须再存一次，
                // 否则紧接着的 ReturnToWorkflowStep 会用默认态重建这棵树。
                _applicationViewModel.UnrealProjectSync.SaveSelectionStateToSessionCache();
                AppendLog(LogKind.Info, $"[Refresh Selection Result] restored={_applicationViewModel.UnrealProjectSync.GetSelectedStableIds().Count}");
                var changedCount = changes.Count(change => change.Kind != UnrealBridgeChangeKind.Unchanged);
                CompleteGlobalProgress("差异检测完成", $"发现 {changedCount} 项变化；冲突和重定向项未默认勾选。");
                var targetWorkflowStep = _workflowStepAfterPublishDetection;
                _workflowStepAfterPublishDetection = 0;
                // 记下这棵差异树属于哪一步：第二步和第四步共用同一棵树、范围不同，
                // 不区分的话回到另一步会误以为已经检测过而直接复用。
                //
                // **不属于 2/4/6 的检测必须把归属清成 0，绝不能默认写成 2** ——
                // 以前这里写的是"不是 3/5 就记成 3"，于是第三步的预检跑完，第二步会误以为
                // "我有缓存"，直接复用一个跟它无关（常常是空）的树，界面显示成
                // "共检查 0 项、无差异 0 项"，把真正该报的新增（幻形立绘 #2 / 失败语音 #1）吞掉。
                _applicationViewModel.UnrealProjectSync.SetLoadedPublishStep(
                    targetWorkflowStep is 2 or 4 or 6 ? targetWorkflowStep : 0);
                // 第 2 步 = 合并后的「同步素材」（规整 + 素材同步）。
                // 旧的第 3 步（「同步素材」）并进它了，所以这里不再有那一支。
                if (targetWorkflowStep == 2)
                {
                    _applicationViewModel.UnrealProjectSync.ReturnToWorkflowStep(2);
                }
                else if (targetWorkflowStep == 4)
                {
                    _applicationViewModel.UnrealProjectSync.ReturnToWorkflowStep(4);
                }
                else if (targetWorkflowStep == 6)
                {
                    // 第六步「特效同步」：和第四步共用这套检测/发布链路，只是计划来自
                    // BuildEffectSyncPlan（只含特效项）。步骤上限见 UnrealSyncWorkflow.MaxStep。
                    _applicationViewModel.UnrealProjectSync.ReturnToWorkflowStep(6);
                }
                else
                {
                    _applicationViewModel.UnrealProjectSync.ReturnToWorkflowStep(1);
                }
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (Exception ex)
            {
                _workflowStepAfterPublishDetection = 0;
                _applicationViewModel.UnrealProjectSync.FailImportDetection(ex.Message);
                CompleteGlobalProgress("差异检测失败", ex.Message);
                AppendLog(LogKind.Error, "检测工具箱到 Unreal 的同步差异失败。", ex);
                await HideGlobalProgressAfterDelayAsync();
            }
            finally
            {
                EndUnrealWorkflowOperation();
            }
        }

        // LogLightConfigurationPreflight 搬到了 MainWindow.UnrealSync.LightConfiguration.cs ——
        // 名字是第三步的，人就该住第三步那个文件（一步一个文件）。
    }
}
