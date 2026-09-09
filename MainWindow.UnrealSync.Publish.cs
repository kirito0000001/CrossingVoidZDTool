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
    /// 第三步「同步素材」和第五步「序列同步」。两步共用同一条差异检测与发布链路，
    /// 只是导出范围、默认勾选和执行阶段不同。
    /// </summary>
    public sealed partial class MainWindow
    {
        private async void PublishCurrentCharacterAssetsToUnrealButton_Click(object sender, RoutedEventArgs e)
        {
            LogUserOperation("同步当前角色素材到 Unreal");
            if (_isUnrealPublishRunning)
            {
                return;
            }

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

            _isUnrealPublishRunning = true;
            _applicationViewModel.UnrealProjectSync.SetPublishRunning(true);
            var isSequenceSynchronization = _applicationViewModel.UnrealProjectSync.WorkflowStep == 5;
            // 百分比按各阶段实测耗时分配；备份开着时它会占掉大半条，这是事实。
            var progressPlan = WorkflowProgressPlan.ForSequenceSync(Settings.BackupBeforeUnrealSync);
            ShowGlobalProgress("同步前检测", character.Code);
            try
            {
                await Task.Yield();
                var enginePath = _applicationViewModel.UnrealProjectSync.EnginePath;
                var projectPath = _applicationViewModel.UnrealProjectSync.ProjectPath;
                var stateService = new UnrealBridgeStateService();
                var baseline = stateService.Load(character, projectPath);
                // 在任何重新扫描、重建差异树之前保存用户当前勾选的叶子项。
                // 不能在刷新后再读取：刷新会先替换选择树并清空旧勾选。
                var selectionBeforeRefresh = _applicationViewModel.UnrealProjectSync.GetSelectedGroupAndLeafStableIds();
                AppendLog(LogKind.Info, $"[Pre-Refresh Selection] count={selectionBeforeRefresh.Count} ids={string.Join(",", selectionBeforeRefresh.Take(12))}");
                UnrealProjectSyncCharacterCandidate latestCandidate;
                try
                {
                    if (isSequenceSynchronization)
                        _applicationViewModel.UnrealProjectSync.ValidateSequenceCharacterFolders(_applicationViewModel.UnrealProjectSync.ProjectPath, character.Code);
                    else
                        _applicationViewModel.UnrealProjectSync.ValidatePublishCharacterFolders(character.Code);
                    // 第三步执行前累计校验第一至第三步；不会检查尚未进入的后续阶段。
                    var preflightExportRun = await _applicationViewModel.UnrealProjectSync.ExportProjectCharactersAsync(
                        [character.Code],
                        // 这一段以前完全没有进度回调，十几秒里进度条一动不动。
                        new Progress<ProgressUpdate>(update => UpdateGlobalProgress(
                            $"阶段 1/4 · {update.Message}",
                            progressPlan[WorkflowProgressPlan.PreflightExport].At(update.Percent),
                            update.Detail,
                            update.IsIndeterminate)),
                        GetGlobalProgressCancellationToken(),
                        isSequenceSynchronization ? UnrealProjectSyncExportScope.CharacterSequences : UnrealProjectSyncExportScope.CharacterMaterials);
                    // 导出结果以前在这里被整个丢掉。Warning 记的是「退出码非 0 但清单照常写出来了」，
                    // 也就是编辑器在别处报过错——多半无关，但同步出问题时它是唯一的线索。
                    // 检测那条链路（DetectUnrealPublishChangesAsync）一直有这一行，只有同步这条漏了。
                    LogExportWarning(preflightExportRun);
                    _applicationViewModel.UnrealProjectSync.ValidatePublishCharacterFolders(character.Code, requireAssetTypes: !isSequenceSynchronization);
                    latestCandidate = _applicationViewModel.UnrealProjectSync.CharacterCandidates.FirstOrDefault(item =>
                        string.Equals(item.Code, character.Code, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException(
                            $"未找到 Unreal 角色 Item 资产。\n正确名称示例：Item_{character.Code}\n期望路径：/Game/ITems/CharItemS/Item_{character.Code}.Item_{character.Code}");
                }
                catch
                {
                    _applicationViewModel.UnrealProjectSync.ReturnToWorkflowStep(1);
                    throw;
                }

                try
                {
                    if (!isSequenceSynchronization && !await _applicationViewModel.UnrealProjectSync.OpenNormalizationWorkspaceAsync())
                    {
                        throw new InvalidOperationException("同步前无法重新加载素材规整状态。");
                    }
                }
                catch
                {
                    _applicationViewModel.UnrealProjectSync.ReturnToWorkflowStep(2);
                    throw;
                }
                if (!isSequenceSynchronization && _applicationViewModel.UnrealProjectSync.NormalizationItems.Any(item => !item.IsResolved))
                {
                    _applicationViewModel.UnrealProjectSync.ReturnToWorkflowStep(2);
                    CompleteGlobalProgress("需要重新确认规整", "检测发现部分重定向映射已失效，请完成第二步后再同步。");
                    ShowFloatingTip(InfoBarSeverity.Warning, "规整状态已变化", "请先完成第二步素材规整。");
                    await HideGlobalProgressAfterDelayAsync();
                    return;
                }
                _applicationViewModel.UnrealProjectSync.ReturnToWorkflowStep(isSequenceSynchronization ? 5 : 3);
                var latestToolboxSnapshot = new UnrealBridgeToolboxSnapshotService().BuildForSynchronization(character);
                var latestUnrealSnapshot = new UnrealBridgeSemanticSnapshotService().Build(latestCandidate);
                var latestAllowedModules = isSequenceSynchronization
                    ? new[] { UnrealBridgeModule.SequenceFrames }
                    : new[] { UnrealBridgeModule.BaseMaterials, UnrealBridgeModule.Voices };
                latestToolboxSnapshot = latestToolboxSnapshot with { Items = latestToolboxSnapshot.Items.Where(item => latestAllowedModules.Contains(item.Module)).ToArray() };
                latestUnrealSnapshot = latestUnrealSnapshot with { Items = latestUnrealSnapshot.Items.Where(item => latestAllowedModules.Contains(item.Module)).ToArray() };
                var latestChanges = new UnrealBridgeDiffService().Compare(
                    latestToolboxSnapshot,
                    latestUnrealSnapshot,
                    UnrealBridgeDirection.PublishToUnreal,
                    baseline)
                    .Select(change => change with { IsSelected = change.IsSelected && UnrealBridgePublishSupportPolicy.CanExecute(change) })
                    .ToArray();
                latestChanges = _applicationViewModel.UnrealProjectSync.FilterPublishChanges(latestChanges).ToArray();
                if (baseline is null)
                {
                    var matchedChanges = latestChanges
                        .Where(change => change.ToolboxItem is not null && change.UnrealItem is not null)
                        .ToArray();
                    if (matchedChanges.Length > 0)
                    {
                        var migratedBaseline = new UnrealBridgeBaselineService().BuildFromChanges(
                            character.Code,
                            projectPath,
                            matchedChanges);
                        stateService.Save(character, projectPath, migratedBaseline);
                        baseline = migratedBaseline;
                        AppendLog(LogKind.Info, $"已在同步前为 {character.Code} 建立源文件哈希同步基线。");
                    }
                }
                var publishChangesChanged = !_applicationViewModel.UnrealProjectSync.MatchesCurrentPublishChanges(latestChanges);
                AppendLog(LogKind.Info, $"[Pre-Sync Restore] previousSelection={selectionBeforeRefresh.Count} ids={string.Join(",", selectionBeforeRefresh.Take(12))}");
                await _applicationViewModel.UnrealProjectSync.SetPublishSelectionTreeAsync(
                    latestChanges,
                    UnrealBridgePublishSupportPolicy.CanExecute,
                    GetGlobalProgressCancellationToken(),
                    // 同步路径里 _workflowStepAfterPublishDetection 早已被检测流程清零，
                    // 用它判断会恒传 true，把第五步默认勾选成"全选"，和检测路径语义相反。
                    selectPendingByDefault: !isSequenceSynchronization);
                _applicationViewModel.UnrealProjectSync.RestoreSelectionState(selectionBeforeRefresh);
                // 重建树时写过一次缓存，那时勾选还是默认态。恢复完必须再存一次，
                // 否则后面任何一次「读缓存重建」都会拿到默认态而不是用户的勾选。
                _applicationViewModel.UnrealProjectSync.SaveSelectionStateToSessionCache();
                var restoredSelection = _applicationViewModel.UnrealProjectSync.GetSelectedStableIds();
                AppendLog(LogKind.Info, $"[Pre-Sync Restore Result] restored={restoredSelection.Count} ids={string.Join(",", restoredSelection.Take(12))}");
                if (publishChangesChanged)
                {
                    AppendLog(LogKind.Warning,
                        $"[Sync Aborted] reason=changes-drifted character={character.Code} latest={latestChanges.Length}");
                    _applicationViewModel.UnrealProjectSync.ReturnToWorkflowStep(isSequenceSynchronization ? 5 : 3);
                    CompleteGlobalProgress("同步内容发生变化", "已刷新第三步列表，请重新确认后再次同步。");
                    ShowFloatingTip(InfoBarSeverity.Warning, "同步内容已刷新", "最终检测发现素材发生变化，请重新确认本次选择。");
                    await HideGlobalProgressAfterDelayAsync();
                    return;
                }
                var selectionLeaves = _applicationViewModel.UnrealProjectSync.SelectionTreeRoots
                    .SelectMany(root => root.Children)
                    .Where(item => item.Change is not null)
                    .ToArray();
                var changes = selectionLeaves
                    .Select(item => item.Change! with { IsSelected = item.IsChecked == true })
                    .ToArray();
                var executableCount = changes.Count(change => change.IsSelected);
                var deferredCount = changes.Count(change => change.Kind != UnrealBridgeChangeKind.Unchanged && !change.IsSelected);
                var selectionWasLost = selectionBeforeRefresh.Count > 0 && restoredSelection.Count == 0;
                AppendLog(LogKind.Info, $"[Pre-Sync Selection] character={character.Code} leaves={selectionLeaves.Length} executable={executableCount} deferred={deferredCount} selectionBeforeRefresh={selectionBeforeRefresh.Count} restored={restoredSelection.Count} sequence={isSequenceSynchronization}");
                LogSequenceChanges("[Pre-Sync Selection Item]", changes.Where(change => change.IsSelected));
                if (changes.Length == 0)
                {
                    if (_applicationViewModel.UnrealProjectSync.HasNoPublishChanges)
                    {
                        _applicationViewModel.UnrealProjectSync.CompletePublishOperation(0, 0);
                        // 第五步不进基础配置，和 653 行的收尾保持一致。
                        if (!isSequenceSynchronization &&
                            !_applicationViewModel.UnrealProjectSync.IsLightConfigurationLoaded)
                        {
                            UpdateGlobalProgress("正在检测基础配置...", 90, character.Code, true);
                            var configurationResult = await ExecuteUnrealLightConfigurationAsync(character, apply: false, Array.Empty<string>());
                            _applicationViewModel.UnrealProjectSync.SetLightConfigurationResult(configurationResult);
                            LogLightConfigurationPreflight(character.Code, configurationResult);
                        }
                        CompleteGlobalProgress("序列无需同步", isSequenceSynchronization ? "全部序列无差异。" : "全部素材无差异，已进入基础配置。");
                        await HideGlobalProgressAfterDelayAsync();
                        return;
                    }

                    AppendLog(LogKind.Warning,
                        $"[Sync Aborted] reason=no-detection character={character.Code} leaves={selectionLeaves.Length}");
                    CompleteGlobalProgress("尚未检测差异", "请先检测差异，再勾选需要同步的内容。");
                    ShowFloatingTip(InfoBarSeverity.Warning, "尚未检测差异", "请先检测差异，再勾选需要同步的内容。");
                    await HideGlobalProgressAfterDelayAsync();
                    return;
                }

                // 这个判断必须排在「无差异」之后：全部同步完成时叶子本来就归零，
                // 早退在前会把「已经做完了」误报成「勾选失效」。
                if (selectionWasLost)
                {
                    AppendLog(LogKind.Warning,
                        $"[Sync Aborted] reason=selection-lost character={character.Code} before={selectionBeforeRefresh.Count} restored={restoredSelection.Count}");
                    _applicationViewModel.UnrealProjectSync.ReturnToWorkflowStep(isSequenceSynchronization ? 5 : 3);
                    CompleteGlobalProgress("同步已停止", "刷新后未能恢复原来的勾选，未修改 Unreal。请重新检测差异并确认选择。");
                    ShowFloatingTip(InfoBarSeverity.Warning, "未恢复同步选择", "刷新后的勾选集合与同步前不一致，已停止执行，未修改 Unreal。");
                    await HideGlobalProgressAfterDelayAsync();
                    return;
                }

                var unsupported = changes.Where(change => change.IsSelected &&
                    !_applicationViewModel.UnrealProjectSync.CanExecutePublishChange(change)).ToArray();
                if (unsupported.Length > 0)
                {
                    // 逐条列出来：全选时最常见的原因是某个动作的工具箱侧源文件已经不在了。
                    AppendLog(LogKind.Warning,
                        $"[Sync Aborted] reason=unsupported character={character.Code} count={unsupported.Length}/{executableCount}");
                    foreach (var item in unsupported)
                    {
                        AppendDiagnosticLog(LogKind.Warning,
                            $"[Sync Aborted Item] stableId={item.StableId} kind={item.Kind} display={FormatSyncLogValue(item.DisplayName)} " +
                            $"toolboxAsset={FormatSyncLogValue(item.ToolboxItem?.AssetPath)} unrealPath={FormatSyncLogValue(item.UnrealItem?.SourceObjectPath)}");
                    }

                    CompleteGlobalProgress("包含尚未完成重定向的同步项", "请先完成第二步素材规整后再同步。");
                    ShowFloatingTip(InfoBarSeverity.Warning, "包含尚未完成重定向的同步项", $"请先完成规整：{string.Join("、", unsupported.Select(item => item.DisplayName))}");
                    await HideGlobalProgressAfterDelayAsync();
                    return;
                }

                ShowGlobalProgress("同步所选到虚幻", character.Code);
                if (executableCount == 0)
                {
                    if (deferredCount > 0)
                    {
                        AppendLog(LogKind.Warning,
                            $"[Sync Aborted] reason=nothing-executable character={character.Code} deferred={deferredCount} leaves={selectionLeaves.Length}");
                        CompleteGlobalProgress("尚有未同步素材", $"还有 {deferredCount} 项未执行，请完成第三步后再进入基础配置。");
                        ShowFloatingTip(InfoBarSeverity.Warning, "第三步尚未完成", $"还有 {deferredCount} 项素材未同步。");
                        await HideGlobalProgressAfterDelayAsync();
                        return;
                    }

                    _applicationViewModel.UnrealProjectSync.CompletePublishOperation(0, deferredCount);
                    if (!_applicationViewModel.UnrealProjectSync.IsLightConfigurationLoaded)
                    {
                        UpdateGlobalProgress("正在检测基础配置...", 90, character.Code, true);
                        var configurationResult = await ExecuteUnrealLightConfigurationAsync(character, apply: false, Array.Empty<string>());
                        _applicationViewModel.UnrealProjectSync.SetLightConfigurationResult(configurationResult);
                        LogLightConfigurationPreflight(character.Code, configurationResult);
                    }
                    CompleteGlobalProgress("没有可自动同步的素材改动", deferredCount == 0
                        ? "两端已有素材一致，已进入基础配置。"
                        : $"还有 {deferredCount} 项属于新增、删除、冲突或语义结构，需要在差异树中明确处理。");
                    await HideGlobalProgressAfterDelayAsync();
                    return;
                }

                if (isSequenceSynchronization)
                {
                    var sequencePublishService = new UnrealBridgeSequencePublishService();
                    var sequencePlan = sequencePublishService.BuildSequenceSyncPlan(character, projectPath, changes);
                    if (sequencePublishService.SkippedActionCodes.Count > 0)
                    {
                        AppendLog(LogKind.Warning, $"[Sequence Plan Skipped] character={character.Code} actions={string.Join("、", sequencePublishService.SkippedActionCodes)}（工具箱侧没有序列帧数据）");
                        ShowFloatingTip(InfoBarSeverity.Warning, "部分动作已跳过",
                            $"以下动作在工具箱里没有序列帧：{string.Join("、", sequencePublishService.SkippedActionCodes)}");
                    }
                    AppendLog(LogKind.User, $"[Sequence Plan] character={character.Code} actions={sequencePlan.Actions.Count} animMaps={sequencePlan.AnimMapsPath}");
                    foreach (var action in sequencePlan.Actions)
                    {
                        AppendLog(LogKind.Info, $"[Sequence Plan Action] code={action.ActionCode} display={FormatSyncLogValue(action.DisplayName)} targetSequence={action.TargetSequencePath} frames={action.Frames.Count} fps={action.Fps}");
                    }
                    // 第五步会重命名并删除历史序列资产，备份策略与第三步一致：
                    // 整体设置开启，或本批包含更新、改名、冲突、删除时都先备份。
                    await BackupUnrealProjectIfRequestedAsync(
                        enginePath,
                        projectPath,
                        character.Code,
                        UnrealBridgeBackupPolicy.ShouldBackupByDefault(changes),
                        progressPlan[WorkflowProgressPlan.Backup]);
                    var sequenceFolder = Path.Combine(Path.GetDirectoryName(projectPath)!, "Intermediate", "ZDToolboxBridge", character.Code);
                    var sequencePlanPath = Path.Combine(sequenceFolder, "sequence-sync-plan.json");
                    var sequenceResultPath = Path.Combine(sequenceFolder, "sequence-sync-result.json");
                    sequencePublishService.Save(sequencePlanPath, sequencePlan);
                    var sequenceProgressPath = Path.Combine(sequenceFolder, "sequence-sync-progress.json");
                    var sequenceExecutor = new UnrealBridgeExecutorService();
                    var sequenceStartInfo = sequenceExecutor.BuildProcessStartInfo(
                        enginePath, projectPath, sequencePlanPath, sequenceProgressPath, sequenceResultPath,
                        Path.Combine(AppContext.BaseDirectory, "Tools", "UnrealBridge", "sync_character_sequences.py"));
                    sequenceStartInfo.Environment["ZD_SEQUENCE_SYNC_PLAN_PATH"] = sequencePlanPath;
                    sequenceStartInfo.Environment["ZD_SEQUENCE_SYNC_RESULT_PATH"] = sequenceResultPath;
                    // 让桥接脚本在同一个编辑器会话里顺手做复扫导出：
                    // 一次同步原本要开三次编辑器，每次约 9 秒纯启动开销。
                    var syncService = new UnrealProjectSyncService();
                    foreach (var (key, value) in syncService.BuildExportEnvironment(
                        projectPath, UnrealProjectSyncExportScope.CharacterSequences, [character.Code]))
                    {
                        sequenceStartInfo.Environment[key] = value;
                    }

                    sequenceStartInfo.Environment["ZD_POST_SYNC_EXPORT_SCRIPT"] =
                        Path.Combine(AppContext.BaseDirectory, "Tools", "Unreal", "export_zd_assets.py");
                    var sequenceManifestPath = syncService.GetExportManifestPath(
                        projectPath, UnrealProjectSyncExportScope.CharacterSequences);
                    var sequenceSyncStartedAt = DateTime.UtcNow;
                    var sequenceLaunch = new UnrealPythonTaskExecutionService().BuildLaunch(
                        enginePath,
                        projectPath,
                        Path.Combine(AppContext.BaseDirectory, "Tools", "UnrealBridge", "sync_character_sequences.py"),
                        Path.Combine(sequenceFolder, "sequence-sync.remote-job.json"),
                        sequenceStartInfo);
                    UpdateGlobalProgress(
                        sequenceLaunch.UsesRunningEditor
                            ? "阶段 3/4 · 正在连接已打开的 Unreal Editor"
                            : "阶段 3/4 · 正在启动 Unreal 执行序列同步",
                        progressPlan[WorkflowProgressPlan.BridgeExecute].At(0),
                        sequenceLaunch.UsesRunningEditor
                            ? "已发现运行中的编辑器 · 等待桥接任务开始"
                            : $"待执行 {sequencePlan.Actions.Count} 个动作 · 首次启动编辑器约需十几秒",
                        true);
                    var sequenceResult = await sequenceExecutor.ExecuteAsync(
                        sequenceLaunch.StartInfo,
                        sequenceProgressPath,
                        sequenceResultPath,
                        new Progress<UnrealBridgeExecutionProgress>(value =>
                            UpdateGlobalProgress(
                                $"阶段 3/4 · 执行序列动作：{value.Message}",
                                progressPlan[WorkflowProgressPlan.BridgeExecute].At(
                                    value.CompletedCount * 100d / Math.Max(1, value.TotalCount)),
                                $"动作进度：{value.CompletedCount}/{value.TotalCount} · {value.StableId}")),
                        GetGlobalProgressCancellationToken());
                    AppendLog(sequenceResult.Succeeded ? LogKind.Info : LogKind.Error, $"[Sequence Execution] character={character.Code} succeeded={sequenceResult.Succeeded} items={sequenceResult.Items.Count} error={FormatSyncLogValue(sequenceResult.ErrorMessage)}");
                    if (!string.IsNullOrWhiteSpace(sequenceResult.ProcessExitWarning))
                    {
                        AppendLog(LogKind.Warning, $"[Sequence Execution] {sequenceResult.ProcessExitWarning}");
                    }
                    foreach (var item in sequenceResult.Items)
                    {
                        AppendLog(item.Succeeded ? LogKind.Info : LogKind.Error, $"[Sequence Execution Item] stableId={item.StableId} succeeded={item.Succeeded} objectPath={FormatSyncLogValue(item.ObjectPath)} message={FormatSyncLogValue(item.Message)}");
                    }
                    // Python 是逐动作串行执行的：失败之前的动作已经改了 Unreal。
                    // 以前这里直接抛异常，复扫、写基线、刷新树全被跳过，
                    // 已经写进去的动作既不落基线也不从列表里消失。
                    // 挑选逻辑提到了模型里，因为它挑错的后果很重：结果里混着一条
                    // orphan-sequences 的诊断条目（孤儿序列解绑），它不对应任何动作。
                    // 以前它被算进「已成功的动作」，于是所有真实动作都失败时，
                    // 下面那句「一个都没成功就抛」失效，界面反而报「已成功 1 个动作并写入基线」，
                    // 那个假 ID 还会被拿去生成基线条目。
                    var succeededActionCodes =
                        UnrealBridgeExecutionItemResult.SelectSucceededActionStableIds(sequenceResult.Items);
                    if (!sequenceResult.Succeeded && succeededActionCodes.Length == 0)
                    {
                        throw new InvalidOperationException($"第五步序列同步失败：{sequenceResult.ErrorMessage}");
                    }
                    UpdateGlobalProgress(
                        "阶段 4/4 · 正在复扫验证序列资产",
                        progressPlan[WorkflowProgressPlan.RescanExport].At(0),
                        $"角色：{character.Code} · 重新读取 Unreal 动画资源",
                        true);
                    // 桥接会话里已经顺手导出过一次，清单够新就不必再开一次编辑器。
                    // 判据只认文件写入时间：同会话导出失败时脚本只记日志不抛异常，
                    // 拿旧清单去复扫会把"还剩多少差异"算错，所以宁可退回独立导出。
                    if (TrySkipRescanExport(sequenceManifestPath, sequenceSyncStartedAt))
                    {
                        UpdateGlobalProgress(
                            "阶段 4/4 · 正在复扫验证序列资产",
                            progressPlan[WorkflowProgressPlan.RescanExport].At(70),
                            "已复用同步会话内的导出结果 · 省去一次编辑器启动",
                            false);
                        _applicationViewModel.UnrealProjectSync.Detect();
                    }
                    else
                    {
                        var sequenceRescanExportRun = await _applicationViewModel.UnrealProjectSync.ExportProjectCharactersAsync(
                            [character.Code],
                            // 复扫也是一次完整的 Unreal 导出，不该静默十几秒。
                            new Progress<ProgressUpdate>(update => UpdateGlobalProgress(
                                $"阶段 4/4 · {update.Message}",
                                progressPlan[WorkflowProgressPlan.RescanExport].At(update.Percent),
                                update.Detail,
                                update.IsIndeterminate)),
                            GetGlobalProgressCancellationToken(),
                            UnrealProjectSyncExportScope.CharacterSequences);
                        // 复扫的结果直接决定「还剩多少差异」，导出侧的告警更不能丢。
                        LogExportWarning(sequenceRescanExportRun);
                    }

                    // 序列同步后必须用最新 Unreal 快照重新计算差异，不能直接清空选择树；
                    // 否则只同步一个动作时，剩余动作也会被误判为“全部完成”。
                    var sequenceRefreshedCandidate = _applicationViewModel.UnrealProjectSync.CharacterCandidates.First(item =>
                        string.Equals(item.Code, character.Code, StringComparison.OrdinalIgnoreCase));
                    var sequenceRescanned = new UnrealBridgeSemanticSnapshotService().Build(sequenceRefreshedCandidate);
                    var sequenceBaseline = new UnrealBridgeStateService().Load(character, projectPath);
                    // 只有真正执行成功的动作才允许刷新基线；失败的动作保留原有条目。
                    var executedActionStableIds = succeededActionCodes
                        .Select(code => SequenceFrameIdentity.BuildActionStableId(code))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    // 复扫必须剔除本次执行动作的旧基线条目：那些条目记的是执行前的 Unreal 哈希，
                    // 拿它比较会把刚同步成功的帧全部判成冲突。
                    var rescanBaseline = new UnrealBridgeBaselineService().WithoutActions(
                        sequenceBaseline,
                        executedActionStableIds);
                    var remainingChanges = new UnrealBridgeDiffService().Compare(
                            new UnrealBridgeToolboxSnapshotService().BuildForSynchronization(character),
                            sequenceRescanned,
                            UnrealBridgeDirection.PublishToUnreal,
                            rescanBaseline)
                        .Where(change => change.Module == UnrealBridgeModule.SequenceFrames)
                        .Select(change => change with
                        {
                            IsSelected = false
                        })
                        .ToArray();
                    await _applicationViewModel.UnrealProjectSync.SetPublishSelectionTreeAsync(
                        remainingChanges,
                        UnrealBridgePublishSupportPolicy.CanExecute,
                        GetGlobalProgressCancellationToken(),
                        selectPendingByDefault: false);

                    // 增量提交基线：只覆盖本次执行过的动作，未执行的动作保留原有条目。
                    var verifiedSequenceState = new UnrealBridgeBaselineService().MergeVerifiedSequenceState(
                        sequenceBaseline,
                        character.Code,
                        projectPath,
                        executedActionStableIds,
                        remainingChanges);
                    new UnrealBridgeStateService().Save(character, projectPath, verifiedSequenceState);
                    AppendLog(LogKind.Info, $"[Sequence Baseline] character={character.Code} actions={executedActionStableIds.Count} entries={verifiedSequenceState.Entries.Count}");

                    // 按选择树实际展示的分组统计。直接数 Kind != Unchanged 会把动作级节点
                    // （例如 Unreal 侧缺 Flipbook 导致帧率对不上）也算进来，而这类节点
                    // 既不在树里也不可勾选，会让界面永远显示"仍有 N 个动作差异"。
                    var remainingActionCount = _applicationViewModel.UnrealProjectSync.SelectionTreeRoots.Count;
                    AppendLog(LogKind.Info, $"[Post-Sync Rescan] character={character.Code} remainingChanges={remainingChanges.Length} remainingActionGroups={remainingActionCount}");
                    LogSequenceChanges("[Post-Sync Rescan Item]", remainingChanges);
                    if (!sequenceResult.Succeeded)
                    {
                        CompleteGlobalProgress(
                            "部分序列同步失败",
                            $"已成功 {succeededActionCodes.Length} 个动作并写入基线；其余失败：{sequenceResult.ErrorMessage}");
                        ShowFloatingTip(InfoBarSeverity.Warning, "部分序列同步失败",
                            $"已成功 {succeededActionCodes.Length} 个动作，剩余请查看日志后重试。");
                        LogUserOperation($"第五步序列部分同步：{character.Code}，Succeeded={succeededActionCodes.Length}，Error={sequenceResult.ErrorMessage}");
                        await HideGlobalProgressAfterDelayAsync();
                        return;
                    }

                    if (remainingActionCount == 0)
                    {
                        _applicationViewModel.UnrealProjectSync.CompletePublishOperation(executableCount, 0);
                        CompleteGlobalProgress("阶段 5/5 · 序列同步完成且已确认无差异", $"已执行 {executableCount} 项动作，复扫未发现剩余差异。");
                        ShowFloatingTip(InfoBarSeverity.Success, "序列同步完成", "复扫确认当前角色已无剩余序列差异。");
                    }
                    else
                    {
                        CompleteGlobalProgress("阶段 5/5 · 本批序列同步完成", $"已执行 {executableCount} 项动作；复扫发现 {remainingActionCount} 个动作仍有差异，请确认后继续。");
                        ShowFloatingTip(InfoBarSeverity.Success, "本批序列已同步", $"仍有 {remainingActionCount} 个动作差异，未自动勾选。");
                    }
                    LogUserOperation($"第五步序列同步完成：{character.Code}，Executed={executableCount}，RemainingActions={remainingActionCount}");
                    await HideGlobalProgressAfterDelayAsync();
                    return;
                }

                var plan = new UnrealBridgeExecutionPlanService().Build(
                    UnrealBridgeDirection.PublishToUnreal,
                    character.Code,
                    projectPath,
                    changes,
                    deletionsConfirmed: isSequenceSynchronization,
                    isFirstPublish: false,
                    templateCharacterCode: baseline?.TemplateCharacterCode ?? string.Empty,
                    baseline: baseline,
                    normalizationItems: _applicationViewModel.UnrealProjectSync.NormalizationItems);
                await BackupUnrealProjectIfRequestedAsync(
                    enginePath,
                    projectPath,
                    character.Code,
                    plan.BackupRequired);

                UpdateGlobalProgress("阶段 3/5 · 正在写入 Unreal 素材", 58, $"待执行：{executableCount} 项 · 正在启动桥接任务", true);
                var workFolder = Path.Combine(
                    Path.GetDirectoryName(projectPath)!,
                    "Intermediate",
                    "ZDToolboxBridge",
                    character.Code);
                Directory.CreateDirectory(workFolder);
                var planPath = Path.Combine(workFolder, "plan.json");
                var progressPath = Path.Combine(workFolder, "progress.json");
                var resultPath = Path.Combine(workFolder, "result.json");
                var executor = new UnrealBridgeExecutorService();
                executor.SavePlan(planPath, plan);
                var offlineStartInfo = executor.BuildProcessStartInfo(
                    enginePath,
                    projectPath,
                    planPath,
                    progressPath,
                    resultPath);
                var launch = new UnrealPythonTaskExecutionService().BuildLaunch(
                    enginePath,
                    projectPath,
                    executor.GetExecuteScriptPath(),
                    Path.Combine(workFolder, "execute.remote-job.json"),
                    offlineStartInfo);
                if (launch.UsesRunningEditor)
                {
                    UpdateGlobalProgress("阶段 3/5 · 正在连接已打开的 Unreal Editor", 58, "已发现运行中的编辑器 · 等待桥接任务开始", true);
                }
                var result = await executor.ExecuteAsync(
                    launch.StartInfo,
                    progressPath,
                    resultPath,
                    new Progress<UnrealBridgeExecutionProgress>(value =>
                        UpdateGlobalProgress(
                            $"阶段 3/5 · 执行同步动作：{value.Message}",
                            58 + value.CompletedCount * 24d / Math.Max(1, value.TotalCount),
                            $"动作进度：{value.CompletedCount}/{value.TotalCount} · {value.StableId}")),
                    GetGlobalProgressCancellationToken());
                // 第五步早就逐条记执行结果，第三步这里以前拿到 result 之后一个字段都没读过：
                // 失败时用户只看到「以下同步项没有成功结果：xxx」这句由后面验证阶段拼出来的话，
                // 而 Python 侧真正的异常文本（result.Items[].Message）从头到尾没人展示，
                // 排查只能靠猜——又是一例「显示成功但实际没做成」。
                //
                // 明细分两路走，沿用 LogSequenceChanges 定下的规矩：第三步的条目数是按
                // 素材张数算的（整角色首同步上百条很正常），全塞进日志面板会被 300 条上限
                // 当场挤掉，还要为每条建一次 XAML 元素。所以成功项只落 runtime.log，
                // 失败项——本来就没几条，而且正是这次要救的那批——才进面板。
                AppendLog(
                    result.Succeeded ? LogKind.Info : LogKind.Error,
                    $"[Sync Execution] character={character.Code} succeeded={result.Succeeded} " +
                    $"items={result.Items.Count} failed={result.Items.Count(item => !item.Succeeded)} " +
                    $"error={FormatSyncLogValue(result.ErrorMessage)}");
                if (!string.IsNullOrWhiteSpace(result.ProcessExitWarning))
                {
                    // 退出码非 0 但结果文件判成功时的诊断信息。丢了它，
                    // 「同步成功但编辑器其实报过错」就再也查不出来。
                    AppendLog(LogKind.Warning, $"[Sync Execution] {result.ProcessExitWarning}");
                }
                foreach (var item in result.Items)
                {
                    var line =
                        $"[Sync Execution Item] stableId={item.StableId} succeeded={item.Succeeded} " +
                        $"objectPath={FormatSyncLogValue(item.ObjectPath)} message={FormatSyncLogValue(item.Message)}";
                    // 成功项也进日志面板，和第五步保持一致：用户要能看到「这一条到底做了什么」，
                    // 而不只是失败时才有交代。面板有 300 条上限，超出的会被挤掉，
                    // 但完整明细同时也落在 runtime.log 里，回头查得到。
                    AppendLog(item.Succeeded ? LogKind.Info : LogKind.Error, line);
                }

                if (result.Items.Count > 0)
                {
                    AppendLog(LogKind.Info, $"[Sync Execution] 共 {result.Items.Count} 条明细，完整记录见 runtime.log。");
                }

                UpdateGlobalProgress("阶段 4/5 · 正在复扫验证 Unreal 资产", 86, $"角色：{character.Code} · 等待 Unreal 导出结果", true);
                var rescanExportRun = await _applicationViewModel.UnrealProjectSync.ExportProjectCharactersAsync(
                    [character.Code],
                    cancellationToken: GetGlobalProgressCancellationToken(),
                    scope: isSequenceSynchronization ? UnrealProjectSyncExportScope.CharacterSequences : UnrealProjectSyncExportScope.CharacterMaterials);
                // 同上：这次复扫的结果要拿去写基线、判「同步到底成没成」，告警必须留痕。
                LogExportWarning(rescanExportRun);
                var refreshedCandidate = _applicationViewModel.UnrealProjectSync.CharacterCandidates.First(item =>
                    string.Equals(item.Code, character.Code, StringComparison.OrdinalIgnoreCase));
                var rescanned = new UnrealBridgeSemanticSnapshotService().Build(refreshedCandidate);
                rescanned = new UnrealBridgePostExecutionIdentityService().Restore(rescanned, result);
                var verifiedState = new UnrealBridgeVerificationService().BuildVerifiedState(
                    plan,
                    result,
                    new UnrealBridgeToolboxSnapshotService().Build(character),
                    rescanned,
                    baseline);
                stateService.Save(character, projectPath, verifiedState);
                _applicationViewModel.UnrealProjectSync.OpenNormalizationWorkspace(activateWorkspace: false);
                if (deferredCount > 0)
                {
                    var remainingChanges = new UnrealBridgeDiffService().Compare(
                            new UnrealBridgeToolboxSnapshotService().BuildForSynchronization(character),
                            rescanned,
                            UnrealBridgeDirection.PublishToUnreal,
                            verifiedState)
                        .Select(change => change with
                        {
                            // 复扫后的剩余差异只展示，不自动重新勾选；用户需要明确确认下一批同步项。
                            IsSelected = false
                        })
                        .ToArray();
                    remainingChanges = _applicationViewModel.UnrealProjectSync.FilterPublishChanges(remainingChanges).ToArray();
                    await _applicationViewModel.UnrealProjectSync.SetPublishSelectionTreeAsync(
                        remainingChanges,
                        UnrealBridgePublishSupportPolicy.CanExecute,
                        GetGlobalProgressCancellationToken(),
                        selectPendingByDefault: false);
                    foreach (var root in _applicationViewModel.UnrealProjectSync.SelectionTreeRoots)
                    {
                        root.SetInitialCheckedState(false);
                        foreach (var child in root.Children)
                        {
                            child.SetInitialCheckedState(false);
                        }
                    }
                    CompleteGlobalProgress(isSequenceSynchronization ? "本次序列同步完成" : "本次素材同步完成", $"已验证 {executableCount} 项；仍有 {deferredCount} 项需要处理。");
                    ShowFloatingTip(InfoBarSeverity.Success, isSequenceSynchronization ? "本次序列已同步" : "本次素材已同步", isSequenceSynchronization ? $"仍有 {deferredCount} 项序列差异。" : $"仍有 {deferredCount} 项，完成后才能进入基础配置。");
                    await HideGlobalProgressAfterDelayAsync();
                    return;
                }

                _applicationViewModel.UnrealProjectSync.CompletePublishOperation(executableCount, deferredCount);
                if (!isSequenceSynchronization && !_applicationViewModel.UnrealProjectSync.IsLightConfigurationLoaded)
                {
                    UpdateGlobalProgress("阶段 5/5 · 正在检测基础配置", 94, $"角色：{character.Code} · 准备进入基础配置", true);
                    var configurationResult = await ExecuteUnrealLightConfigurationAsync(character, apply: false, Array.Empty<string>());
                    _applicationViewModel.UnrealProjectSync.SetLightConfigurationResult(configurationResult);
                    LogLightConfigurationPreflight(character.Code, configurationResult);
                }
                CompleteGlobalProgress(isSequenceSynchronization ? "序列同步到虚幻完成" : "同步到虚幻完成", $"已验证 {executableCount} 项；另有 {deferredCount} 项未执行。");
                ShowFloatingTip(InfoBarSeverity.Success, isSequenceSynchronization ? "序列同步完成" : "同步到虚幻完成", $"已验证 {executableCount} 项。");
                LogUserOperation($"同步完成：{character.Code}，Executed={executableCount}，Deferred={deferredCount}");
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (OperationCanceledException ex)
            {
                _applicationViewModel.UnrealProjectSync.FailPublishOperation("同步已取消，当前差异选择仍保留。");
                CompleteGlobalProgress("同步已取消", character.Code);
                AppendLog(LogKind.Warning, "同步已有素材已取消。", ex);
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (Exception ex)
            {
                _applicationViewModel.UnrealProjectSync.FailPublishOperation(ex.Message);
                CompleteGlobalProgress("同步失败", ex.Message);
                ShowFloatingTip(InfoBarSeverity.Error, "同步到 Unreal 失败", ex.Message);
                AppendRuntimeLog($"[Sync Result] character={character.Code} status=failed message={FormatSyncLogValue(ex.Message)}");
                AppendLog(LogKind.Error, "同步已有素材到 Unreal 失败。", ex);
                await HideGlobalProgressAfterDelayAsync();
            }
            finally
            {
                _isUnrealPublishRunning = false;
                _applicationViewModel.UnrealProjectSync.SetPublishRunning(false);
                EndUnrealWorkflowOperation();
            }
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
            LogUserOperation("检测工具箱到 Unreal 的差异");
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
