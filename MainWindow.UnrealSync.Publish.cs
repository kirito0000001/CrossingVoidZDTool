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
    /// 第三步「同步素材」和第五步「序列同步」。两步共用同一条差异检测与发布链路，
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
                if (_workflowStepAfterPublishDetection == 5)
                    _applicationViewModel.UnrealProjectSync.ValidateSequenceCharacterFolders(_applicationViewModel.UnrealProjectSync.ProjectPath, character.Code);
                else
                    _applicationViewModel.UnrealProjectSync.ValidatePublishCharacterFolders(character.Code);
                ShowGlobalProgress("检测同步差异", character.Code);
                var detectionExportScope = _applicationViewModel.UnrealProjectSync.WorkflowStep == 5 || _workflowStepAfterPublishDetection == 5
                    ? UnrealProjectSyncExportScope.CharacterSequences
                    : UnrealProjectSyncExportScope.CharacterMaterials;
                var detectionExportRun = await _applicationViewModel.UnrealProjectSync.ExportProjectCharactersAsync(
                    [character.Code],
                    new Progress<ProgressUpdate>(update =>
                        UpdateGlobalProgress(update.Message, Math.Min(70, update.Percent * 0.7), update.Detail, update.IsIndeterminate)),
                    GetGlobalProgressCancellationToken(),
                    detectionExportScope);
                LogExportWarning(detectionExportRun);
                _applicationViewModel.UnrealProjectSync.ValidatePublishCharacterFolders(character.Code, requireAssetTypes: false);
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
                    var allowedModules = _workflowStepAfterPublishDetection == 5
                        ? new[] { UnrealBridgeModule.SequenceFrames }
                        : new[] { UnrealBridgeModule.BaseMaterials, UnrealBridgeModule.Voices };
                    toolboxSnapshot = toolboxSnapshot with { Items = toolboxSnapshot.Items.Where(item => allowedModules.Contains(item.Module)).ToArray() };
                    unrealSnapshot = unrealSnapshot with { Items = unrealSnapshot.Items.Where(item => allowedModules.Contains(item.Module)).ToArray() };
                    if (_workflowStepAfterPublishDetection == 5)
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
                if (_workflowStepAfterPublishDetection == 5)
                {
                    _applicationViewModel.UnrealProjectSync.SelectedPublishStage =
                        _applicationViewModel.UnrealProjectSync.PublishStages.First(stage =>
                            stage.Stage == UnrealBridgePublishStage.ZdAnimationTracks);
                }
                changes = _applicationViewModel.UnrealProjectSync.FilterPublishChanges(changes).ToArray();
                AppendLog(LogKind.Info, $"[Step5 Detection] character={character.Code} changes={changes.Length} stepAfterPublishDetection={_workflowStepAfterPublishDetection} selectPendingByDefault={_workflowStepAfterPublishDetection != 5}");
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
                    selectPendingByDefault: _workflowStepAfterPublishDetection != 5);
                _applicationViewModel.UnrealProjectSync.RestoreSelectionState(selectionBeforeDetection);
                // 与同步路径对称：建树时写入的是默认勾选态，恢复完必须再存一次，
                // 否则紧接着的 ReturnToWorkflowStep 会用默认态重建这棵树。
                _applicationViewModel.UnrealProjectSync.SaveSelectionStateToSessionCache();
                AppendLog(LogKind.Info, $"[Refresh Selection Result] restored={_applicationViewModel.UnrealProjectSync.GetSelectedStableIds().Count}");
                var changedCount = changes.Count(change => change.Kind != UnrealBridgeChangeKind.Unchanged);
                CompleteGlobalProgress("差异检测完成", $"发现 {changedCount} 项变化；冲突和重定向项未默认勾选。");
                var targetWorkflowStep = _workflowStepAfterPublishDetection;
                _workflowStepAfterPublishDetection = 0;
                // 记下这棵差异树属于哪一步：第三步和第五步共用同一棵树、范围不同，
                // 不区分的话回到另一步会误以为已经检测过而直接复用。
                _applicationViewModel.UnrealProjectSync.SetLoadedPublishStep(
                    targetWorkflowStep is 3 or 5 ? targetWorkflowStep : 3);
                if (targetWorkflowStep == 2)
                {
                    _applicationViewModel.UnrealProjectSync.ReturnToWorkflowStep(1);
                    _applicationViewModel.UnrealProjectSync.AdvanceWorkflowStep();
                }
                else if (targetWorkflowStep == 3)
                {
                    _applicationViewModel.UnrealProjectSync.ReturnToWorkflowStep(3);
                }
                else if (targetWorkflowStep == 5)
                {
                    _applicationViewModel.UnrealProjectSync.ReturnToWorkflowStep(5);
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

        /// <summary>
        /// 第三步收尾时顺手做的第四步预检。
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
    }
}
