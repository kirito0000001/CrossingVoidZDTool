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
            foreach (var change in changes.Where(item => item.Module == UnrealBridgeModule.SequenceFrames))
            {
                var canExecute = UnrealBridgePublishSupportPolicy.CanExecute(change);
                var toolboxValue = FormatSyncLogValue(change.ToolboxItem?.PayloadJson);
                var unrealValue = FormatSyncLogValue(change.UnrealItem?.PayloadJson);
                AppendLog(LogKind.Info,
                    $"{prefix} stableId={change.StableId} kind={change.Kind} selected={change.IsSelected} canExecute={canExecute} group={FormatSyncLogValue(change.SequenceGroupKey)} display={FormatSyncLogValue(change.DisplayName)} toolbox={toolboxValue} unreal={unrealValue}");
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

        private async void GetUnrealProjectCharactersButton_Click(object sender, RoutedEventArgs e)
        {
            LogUserOperation("刷新虚幻项目角色列表");
            var scrollOffset = CaptureUnrealProjectSyncScrollOffset();
            try
            {
                if (!_applicationViewModel.UnrealProjectSync.IsEngineToToolbox)
                {
                    _applicationViewModel.UnrealProjectSync.RefreshDraftSources(CharacterDesk.CompletedCharacters);
                    CompleteGlobalProgress("已完成角色来源刷新", $"当前已完成角色 {CharacterDesk.CompletedCharacters.Count} 个。");
                    RestoreUnrealProjectSyncScrollOffset(scrollOffset);
                    await HideGlobalProgressAfterDelayAsync();
                    return;
                }

                SaveUnrealProjectSyncSettings();
                ShowGlobalProgress("获取项目角色", _applicationViewModel.UnrealProjectSync.ProjectPath);
                UpdateGlobalProgress("正在读取项目角色文件...", 15, _applicationViewModel.UnrealProjectSync.ProjectPath);
                await Task.Yield();
                _applicationViewModel.UnrealProjectSync.Detect();
                RestoreUnrealProjectSyncScrollOffset(scrollOffset);
                CompleteGlobalProgress(
                    "项目角色已刷新",
                    $"已读取候选角色 {_applicationViewModel.UnrealProjectSync.CharacterCandidates.Count} 个；名称直接来自角色条目文件，未获取详情的角色仍标记为不是最新数据。");
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

        private async void ImportSelectedUnrealCharacterToDraftButton_Click(object sender, RoutedEventArgs e)
        {
            LogUserOperation("导入选中的 Unreal 角色到草稿");
            var source = _applicationViewModel.UnrealProjectSync.SelectedSource;
            var candidate = source?.UnrealCandidate;
            if (candidate is null)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择角色", "请先在左侧选择一个 Unreal 角色。");
                return;
            }

            var selectedStableIds = _applicationViewModel.UnrealProjectSync.GetSelectedStableIds();
            if (selectedStableIds.Count == 0)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "尚未选择导入内容", "请先检测角色，再在中间勾选需要导入的模块。");
                return;
            }

            try
            {
                ShowGlobalProgress("从虚幻导入", candidate.Code);
                UpdateGlobalProgress("正在写入所选 Draft 模块...", 72, candidate.Code);
                var result = await Task.Run(
                    () => new UnrealBridgeDraftImportService().Import(Settings.ProjectRootPath, candidate, selectedStableIds),
                    GetGlobalProgressCancellationToken());
                var unrealSnapshot = new UnrealBridgeSemanticSnapshotService().Build(candidate);
                var toolboxSnapshot = new UnrealBridgeToolboxSnapshotService().BuildForSynchronization(result.Character);
                var stateService = new UnrealBridgeStateService();
                var baseline = new UnrealBridgeBaselineService().BuildSelected(
                    result.Character.Code,
                    _applicationViewModel.UnrealProjectSync.ProjectPath,
                    toolboxSnapshot,
                    unrealSnapshot,
                    selectedStableIds,
                    stateService.Load(result.Character, _applicationViewModel.UnrealProjectSync.ProjectPath));
                stateService.Save(
                    result.Character,
                    _applicationViewModel.UnrealProjectSync.ProjectPath,
                    baseline);
                await CharacterDesk.LoadCharactersAsync(
                    Settings.ProjectRootPath,
                    result.Character.Code,
                    result.Character.Code,
                    GetGlobalProgressCancellationToken());
                _applicationViewModel.UnrealProjectSync.CompleteImportOperation(
                    result.Character.FolderPath,
                    result.RemovedDuplicateCount);
                CompleteGlobalProgress(
                    "导入完成",
                    $"{result.Character.Code} 已写入 Draft；模块：{string.Join("、", result.ImportedModules)}；清理重复 {result.RemovedDuplicateCount} 个。");
                ShowFloatingTip(InfoBarSeverity.Success, "已导入到 Draft", result.Character.FolderPath);
                AppendLog(
                    LogKind.User,
                    $"从虚幻统一导入角色：{result.Character.Code}，Modules={string.Join(",", result.ImportedModules)}，Created={result.CreatedNew}。");
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (OperationCanceledException ex)
            {
                _applicationViewModel.UnrealProjectSync.FailImportOperation("导入已取消，当前选择未执行。");
                CompleteGlobalProgress("导入已取消", candidate.Code);
                AppendLog(LogKind.Warning, "从虚幻导入已取消。", ex);
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (Exception ex)
            {
                _applicationViewModel.UnrealProjectSync.FailImportOperation(ex.Message);
                CompleteGlobalProgress("导入失败", ex.Message);
                ShowFloatingTip(InfoBarSeverity.Error, "从虚幻导入失败", ex.Message);
                AppendLog(LogKind.Error, "从虚幻统一导入失败。", ex);
                await HideGlobalProgressAfterDelayAsync();
            }
        }

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
                    await _applicationViewModel.UnrealProjectSync.ExportProjectCharactersAsync(
                        [character.Code],
                        cancellationToken: GetGlobalProgressCancellationToken(),
                        scope: isSequenceSynchronization ? UnrealProjectSyncExportScope.CharacterSequences : UnrealProjectSyncExportScope.CharacterMaterials);
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
                    selectPendingByDefault: _workflowStepAfterPublishDetection != 5);
                foreach (var root in _applicationViewModel.UnrealProjectSync.SelectionTreeRoots)
                {
                    root.RestoreCheckedState(selectionBeforeRefresh);
                }
                var restoredSelection = _applicationViewModel.UnrealProjectSync.GetSelectedStableIds();
                AppendLog(LogKind.Info, $"[Pre-Sync Restore Result] restored={restoredSelection.Count} ids={string.Join(",", restoredSelection.Take(12))}");
                if (publishChangesChanged)
                {
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
                if (selectionWasLost)
                {
                    _applicationViewModel.UnrealProjectSync.ReturnToWorkflowStep(isSequenceSynchronization ? 5 : 3);
                    CompleteGlobalProgress("同步已停止", "刷新后未能恢复原来的勾选，未修改 Unreal。请重新检测差异并确认选择。");
                    ShowFloatingTip(InfoBarSeverity.Warning, "未恢复同步选择", "刷新后的勾选集合与同步前不一致，已停止执行，未修改 Unreal。");
                    await HideGlobalProgressAfterDelayAsync();
                    return;
                }
                LogSequenceChanges("[Pre-Sync Selection Item]", changes.Where(change => change.IsSelected));
                if (changes.Length == 0)
                {
                    if (_applicationViewModel.UnrealProjectSync.HasNoPublishChanges)
                    {
                        _applicationViewModel.UnrealProjectSync.CompletePublishOperation(0, 0);
                        if (!_applicationViewModel.UnrealProjectSync.IsLightConfigurationLoaded)
                        {
                            UpdateGlobalProgress("正在检测基础配置...", 90, character.Code, true);
                            var configurationResult = await ExecuteUnrealLightConfigurationAsync(character, apply: false, Array.Empty<string>());
                            _applicationViewModel.UnrealProjectSync.SetLightConfigurationResult(configurationResult);
                        }
                        CompleteGlobalProgress("序列无需同步", isSequenceSynchronization ? "全部序列无差异。" : "全部素材无差异，已进入基础配置。");
                        await HideGlobalProgressAfterDelayAsync();
                        return;
                    }

                    ShowFloatingTip(InfoBarSeverity.Warning, "尚未检测差异", "请先检测差异，再勾选需要同步的内容。");
                    return;
                }

                var unsupported = changes.Where(change => change.IsSelected &&
                    !_applicationViewModel.UnrealProjectSync.CanExecutePublishChange(change)).ToArray();
                if (unsupported.Length > 0)
                {
                    ShowFloatingTip(InfoBarSeverity.Warning, "包含尚未完成重定向的同步项", $"请先完成规整：{string.Join("、", unsupported.Select(item => item.DisplayName))}");
                    return;
                }

                ShowGlobalProgress("同步所选到虚幻", character.Code);
                if (executableCount == 0)
                {
                    if (deferredCount > 0)
                    {
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
                    }
                    CompleteGlobalProgress("没有可自动同步的素材改动", deferredCount == 0
                        ? "两端已有素材一致，已进入基础配置。"
                        : $"还有 {deferredCount} 项属于新增、删除、冲突或语义结构，需要在差异树中明确处理。");
                    await HideGlobalProgressAfterDelayAsync();
                    return;
                }

                if (isSequenceSynchronization)
                {
                    var sequencePlan = new UnrealBridgeSequencePublishService().BuildSequenceSyncPlan(character, projectPath, changes);
                    AppendLog(LogKind.User, $"[Sequence Plan] character={character.Code} actions={sequencePlan.Actions.Count} animMaps={sequencePlan.AnimMapsPath}");
                    foreach (var action in sequencePlan.Actions)
                    {
                        AppendLog(LogKind.Info, $"[Sequence Plan Action] code={action.ActionCode} display={FormatSyncLogValue(action.DisplayName)} targetSequence={action.TargetSequencePath} frames={action.Frames.Count} fps={action.Fps}");
                    }
                    var sequenceFolder = Path.Combine(Path.GetDirectoryName(projectPath)!, "Intermediate", "ZDToolboxBridge", character.Code);
                    var sequencePlanPath = Path.Combine(sequenceFolder, "sequence-sync-plan.json");
                    var sequenceResultPath = Path.Combine(sequenceFolder, "sequence-sync-result.json");
                    new UnrealBridgeSequencePublishService().Save(sequencePlanPath, sequencePlan);
                    var sequenceProgressPath = Path.Combine(sequenceFolder, "sequence-sync-progress.json");
                    var sequenceExecutor = new UnrealBridgeExecutorService();
                    var sequenceStartInfo = sequenceExecutor.BuildProcessStartInfo(
                        enginePath, projectPath, sequencePlanPath, sequenceProgressPath, sequenceResultPath,
                        Path.Combine(AppContext.BaseDirectory, "Tools", "UnrealBridge", "sync_character_sequences.py"));
                    sequenceStartInfo.Environment["ZD_SEQUENCE_SYNC_PLAN_PATH"] = sequencePlanPath;
                    sequenceStartInfo.Environment["ZD_SEQUENCE_SYNC_RESULT_PATH"] = sequenceResultPath;
                    var sequenceLaunch = new UnrealPythonTaskExecutionService().BuildLaunch(
                        enginePath,
                        projectPath,
                        Path.Combine(AppContext.BaseDirectory, "Tools", "UnrealBridge", "sync_character_sequences.py"),
                        Path.Combine(sequenceFolder, "sequence-sync.remote-job.json"),
                        sequenceStartInfo);
                    if (sequenceLaunch.UsesRunningEditor)
                    {
                        UpdateGlobalProgress("阶段 3/5 · 正在连接已打开的 Unreal Editor", 58, "已发现运行中的编辑器 · 等待桥接任务开始", true);
                    }
                    var sequenceResult = await sequenceExecutor.ExecuteAsync(
                        sequenceLaunch.StartInfo,
                        sequenceProgressPath,
                        sequenceResultPath,
                        new Progress<UnrealBridgeExecutionProgress>(value =>
                            UpdateGlobalProgress(
                                $"阶段 3/5 · 执行序列动作：{value.Message}",
                                58 + value.CompletedCount * 24d / Math.Max(1, value.TotalCount),
                                $"动作进度：{value.CompletedCount}/{value.TotalCount} · {value.StableId}")),
                        GetGlobalProgressCancellationToken());
                    AppendLog(sequenceResult.Succeeded ? LogKind.Info : LogKind.Error, $"[Sequence Execution] character={character.Code} succeeded={sequenceResult.Succeeded} items={sequenceResult.Items.Count} error={FormatSyncLogValue(sequenceResult.ErrorMessage)}");
                    foreach (var item in sequenceResult.Items)
                    {
                        AppendLog(item.Succeeded ? LogKind.Info : LogKind.Error, $"[Sequence Execution Item] stableId={item.StableId} succeeded={item.Succeeded} objectPath={FormatSyncLogValue(item.ObjectPath)} message={FormatSyncLogValue(item.Message)}");
                    }
                    if (!sequenceResult.Succeeded)
                    {
                        throw new InvalidOperationException($"第五步序列同步失败：{sequenceResult.ErrorMessage}");
                    }
                    UpdateGlobalProgress("阶段 4/5 · 正在复扫验证序列资产", 86, $"角色：{character.Code} · 重新读取 Unreal 动画资源", true);
                    await _applicationViewModel.UnrealProjectSync.ExportProjectCharactersAsync(
                        [character.Code],
                        cancellationToken: GetGlobalProgressCancellationToken(),
                        scope: UnrealProjectSyncExportScope.CharacterSequences);

                    // 序列同步后必须用最新 Unreal 快照重新计算差异，不能直接清空选择树；
                    // 否则只同步一个动作时，剩余动作也会被误判为“全部完成”。
                    var sequenceRefreshedCandidate = _applicationViewModel.UnrealProjectSync.CharacterCandidates.First(item =>
                        string.Equals(item.Code, character.Code, StringComparison.OrdinalIgnoreCase));
                    var sequenceRescanned = new UnrealBridgeSemanticSnapshotService().Build(sequenceRefreshedCandidate);
                    var sequenceBaseline = new UnrealBridgeStateService().Load(character, projectPath);
                    var remainingChanges = new UnrealBridgeDiffService().Compare(
                            new UnrealBridgeToolboxSnapshotService().BuildForSynchronization(character),
                            sequenceRescanned,
                            UnrealBridgeDirection.PublishToUnreal,
                            sequenceBaseline)
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

                    var remainingActionCount = remainingChanges
                        .Where(change => change.Kind != UnrealBridgeChangeKind.Unchanged)
                        .Select(change => change.SequenceGroupKey ?? change.StableId)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count();
                    AppendLog(LogKind.Info, $"[Post-Sync Rescan] character={character.Code} remainingChanges={remainingChanges.Length} remainingActionGroups={remainingActionCount}");
                    LogSequenceChanges("[Post-Sync Rescan Item]", remainingChanges);
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
                if (plan.BackupRequired || Settings.BackupBeforeUnrealSync)
                {
                    UpdateGlobalProgress("正在压缩备份 Unreal 项目...", 40, projectPath, true);
                    var backupPath = Path.Combine(
                        Path.GetDirectoryName(projectPath)!,
                        "Saved",
                        "ZDToolboxBackups",
                        $"{character.Code}-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
                    var backupInfo = new UnrealBridgeBackupService().BuildZipProjectStartInfo(enginePath, projectPath, backupPath);
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

                UpdateGlobalProgress("阶段 4/5 · 正在复扫验证 Unreal 资产", 86, $"角色：{character.Code} · 等待 Unreal 导出结果", true);
                await _applicationViewModel.UnrealProjectSync.ExportProjectCharactersAsync(
                    [character.Code],
                    cancellationToken: GetGlobalProgressCancellationToken(),
                    scope: isSequenceSynchronization ? UnrealProjectSyncExportScope.CharacterSequences : UnrealProjectSyncExportScope.CharacterMaterials);
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

        private async void DetectUnrealImportButton_Click(object sender, RoutedEventArgs e)
        {
            LogUserOperation("检测 Unreal 角色并导入工具箱");
            var source = _applicationViewModel.UnrealProjectSync.SelectedSource;
            if (source is null)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择来源", "请先在左侧选择 Unreal 角色或项目通用素材。");
                return;
            }

            if (source.IsSharedMaterial)
            {
                _applicationViewModel.UnrealProjectSync.SetSelectionTree([]);
                ShowFloatingTip(InfoBarSeverity.Informational, "通用素材入口已保留", $"{source.DisplayName} 将在项目通用素材阶段实装。");
                return;
            }

            var code = source.UnrealCandidate?.Code;
            if (string.IsNullOrWhiteSpace(code))
            {
                return;
            }
            if (!TryBeginUnrealWorkflowOperation())
            {
                return;
            }

            try
            {
                ShowGlobalProgress("检测 Unreal 角色", code);
                await _applicationViewModel.UnrealProjectSync.ExportProjectCharactersAsync(
                    [code],
                    new Progress<ProgressUpdate>(update =>
                        UpdateGlobalProgress(update.Message, update.Percent, update.Detail, update.IsIndeterminate)),
                    GetGlobalProgressCancellationToken());
                var candidate = _applicationViewModel.UnrealProjectSync.CharacterCandidates.First(item =>
                    string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase));
                var refreshedSource = _applicationViewModel.UnrealProjectSync.CharacterSources.First(item =>
                    item.UnrealCandidate is not null &&
                    string.Equals(item.UnrealCandidate.Code, candidate.Code, StringComparison.OrdinalIgnoreCase));
                _applicationViewModel.UnrealProjectSync.SelectSource(refreshedSource);
                var snapshot = new UnrealBridgeSemanticSnapshotService().Build(candidate);
                var draft = CharacterDesk.DraftCharacters.FirstOrDefault(character =>
                    string.Equals(character.Code, candidate.Code, StringComparison.OrdinalIgnoreCase));
                var baseline = draft is null
                    ? null
                    : new UnrealBridgeStateService().Load(
                        draft,
                        _applicationViewModel.UnrealProjectSync.ProjectPath);
                var existingStableIds = baseline?.Entries.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase)
                    ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                await _applicationViewModel.UnrealProjectSync.SetImportSelectionTreeAsync(
                    snapshot,
                    existingStableIds,
                    GetGlobalProgressCancellationToken());
                CompleteGlobalProgress("检测完成", "正常模块已默认勾选；展开中间树可查看具体条目。");
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (Exception ex)
            {
                _applicationViewModel.UnrealProjectSync.FailImportDetection(ex.Message);
                CompleteGlobalProgress("检测失败", ex.Message);
                AppendLog(LogKind.Error, "检测 Unreal 导入内容失败。", ex);
                await HideGlobalProgressAfterDelayAsync();
            }
            finally
            {
                EndUnrealWorkflowOperation();
            }
        }

        private async void DetectUnrealPublishChangesButton_Click(object sender, RoutedEventArgs e)
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
                await _applicationViewModel.UnrealProjectSync.ExportProjectCharactersAsync(
                    [character.Code],
                    new Progress<ProgressUpdate>(update =>
                        UpdateGlobalProgress(update.Message, Math.Min(70, update.Percent * 0.7), update.Detail, update.IsIndeterminate)),
                    GetGlobalProgressCancellationToken(),
                    detectionExportScope);
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
                foreach (var root in _applicationViewModel.UnrealProjectSync.SelectionTreeRoots)
                {
                    root.RestoreCheckedState(selectionBeforeDetection);
                }
                AppendLog(LogKind.Info, $"[Refresh Selection Result] restored={_applicationViewModel.UnrealProjectSync.GetSelectedStableIds().Count}");
                var changedCount = changes.Count(change => change.Kind != UnrealBridgeChangeKind.Unchanged);
                CompleteGlobalProgress("差异检测完成", $"发现 {changedCount} 项变化；冲突和重定向项未默认勾选。");
                var targetWorkflowStep = _workflowStepAfterPublishDetection;
                _workflowStepAfterPublishDetection = 0;
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

        private async void OpenUnrealNormalizationButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryBeginUnrealWorkflowOperation())
            {
                return;
            }

            try
            {
                ShowGlobalProgress("加载规整素材列表", _applicationViewModel.UnrealProjectSync.SelectedSource?.DraftCharacter?.Code ?? string.Empty);
                if (!await _applicationViewModel.UnrealProjectSync.OpenNormalizationWorkspaceAsync())
                {
                    ShowFloatingTip(InfoBarSeverity.Warning, "无法打开素材规整", "请先选择已完成角色，并至少检测一次当前 Unreal 内容。");
                    return;
                }

                CompleteGlobalProgress("规整素材列表已加载", "已复用当前 Unreal 素材数据。");
                AppendLog(LogKind.User, "打开 Unreal 素材规整工作区。");
                await HideGlobalProgressAfterDelayAsync();
            }
            finally
            {
                EndUnrealWorkflowOperation();
            }
        }

        private async void ChooseUnrealNormalizationRedirectButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: UnrealAssetNormalizationItem item })
            {
                return;
            }

            var usedCandidateIds = _applicationViewModel.UnrealProjectSync.NormalizationItems
                .Where(other => !ReferenceEquals(other, item))
                .Select(other => other.SelectedCandidate?.StableId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var availableCandidates = item.Candidates
                .Where(candidate => !usedCandidateIds.Contains(candidate.StableId) ||
                    string.Equals(candidate.StableId, item.SelectedCandidate?.StableId, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (availableCandidates.Length == 0)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "没有可用的工具箱素材", "请先在工具箱中补充该类型的合规素材，或标记为不需要重定向。");
                return;
            }

            var list = new ListView
            {
                ItemsSource = availableCandidates,
                SelectionMode = ListViewSelectionMode.Single,
                MaxHeight = 420
            };
            list.ItemTemplate = (DataTemplate)XamlReader.Load($@"
                <DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
                    <Grid MinHeight='82' Padding='6' ColumnSpacing='10'>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width='72'/>
                            <ColumnDefinition Width='*'/>
                        </Grid.ColumnDefinitions>
                        <Border Width='64' Height='64' Background='{{ThemeResource CardBackgroundFillColorSecondaryBrush}}'>
                            <Image Source='{{Binding FileUri}}' Stretch='Uniform'/>
                        </Border>
                        <StackPanel Grid.Column='1' Spacing='2' VerticalAlignment='Center'>
                            <TextBlock FontWeight='SemiBold' Text='{{Binding DisplayName}}' TextTrimming='CharacterEllipsis'/>
                            <TextBlock FontSize='12' Style='{{StaticResource SubtleTextStyle}}' Text='{{Binding RelativePath}}' TextTrimming='CharacterEllipsis'/>
                        </StackPanel>
                    </Grid>
                </DataTemplate>");
            if (item.SelectedCandidate is not null)
            {
                list.SelectedItem = item.SelectedCandidate;
            }

            var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                $"选择 {item.UnrealAssetName} 的重定向素材",
                list,
                PrimaryButtonText: "使用所选素材",
                CloseButtonText: "取消",
                ConfigureDialog: dialog =>
                {
                    dialog.IsPrimaryButtonEnabled = list.SelectedItem is not null;
                    list.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = list.SelectedItem is not null;
                }));
            if (result == DialogResultKind.Primary && list.SelectedItem is UnrealAssetNormalizationCandidate candidate)
            {
                _applicationViewModel.UnrealProjectSync.SelectNormalizationRedirect(item, candidate);
            }
        }

        private void SkipUnrealNormalizationButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: UnrealAssetNormalizationItem item })
            {
                _applicationViewModel.UnrealProjectSync.MarkNormalizationNotRequired(item);
            }
        }

        private void ClearUnrealNormalizationRedirectButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: UnrealAssetNormalizationItem item })
            {
                _applicationViewModel.UnrealProjectSync.ClearNormalizationRedirect(item);
            }
        }

        private void PreviewUnrealNormalizationAudioButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: UnrealAssetNormalizationItem { IsAudio: true, HasPreview: true } item } button)
            {
                PlayVoiceFile(item.PreviewFilePath, button);
            }
        }

        private void UnrealSyncPreviousStepButton_Click(object sender, RoutedEventArgs e)
        {
            LogUserOperation("同步流程：上一步");
            var step = _applicationViewModel.UnrealProjectSync.WorkflowStep;
            _applicationViewModel.UnrealProjectSync.ReturnToWorkflowStep(Math.Max(1, step - 1));
        }

        private void UnrealFoundationCheckItem_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: UnrealPublishFoundationCheckItem item } ||
                string.IsNullOrWhiteSpace(item.CorrectName))
            {
                return;
            }

            CopyTextToClipboard(item.CorrectName);
            ShowFloatingTip(InfoBarSeverity.Success, "已复制正确名称", item.CorrectName);
        }

        private async void UnrealSyncNextStepButton_Click(object sender, RoutedEventArgs e)
        {
            LogUserOperation("同步流程：下一步");
            var sync = _applicationViewModel.UnrealProjectSync;
            if (sync.WorkflowStep == 1)
            {
                if (!sync.CanAdvanceWorkflow)
                {
                    ShowFloatingTip(InfoBarSeverity.Informational, "当前步骤尚未完成", "请先选择角色，并完成底层检测。");
                    return;
                }

                sync.ReturnToWorkflowStep(2);
                if (!sync.IsNormalizationStepLoaded)
                {
                    await ReloadUnrealNormalizationStepAsync(sync);
                }
                return;
            }

            if (sync.WorkflowStep == 2)
            {
                if (!sync.IsNormalizationStepLoaded)
                {
                    ShowFloatingTip(InfoBarSeverity.Informational, "规整素材尚未加载完成", "请等待当前加载完成，或点击“重新加载规整素材”。");
                    return;
                }

                if (sync.NormalizationItems.Any(item => !item.IsResolved))
                {
                    ShowFloatingTip(
                        InfoBarSeverity.Informational,
                        "当前步骤尚未完成",
                        $"还有 {sync.NormalizationItems.Count(item => !item.IsResolved)} 项 Unreal 素材没有选择处理方式。");
                    return;
                }

                if (sync.HasContentDetection)
                {
                    if (!TryBeginUnrealWorkflowOperation())
                    {
                        return;
                    }

                    try
                    {
                        ShowGlobalProgress("加载同步素材列表", sync.SelectedSource?.DraftCharacter?.Code ?? string.Empty);
                        await Task.Yield();
                        sync.ReturnToWorkflowStep(3);
                        CompleteGlobalProgress("同步素材列表已加载", "已复用本次同步缓存，未重新检测 Unreal。");
                        await HideGlobalProgressAfterDelayAsync();
                    }
                    finally
                    {
                        EndUnrealWorkflowOperation();
                    }
                    return;
                }

                _workflowStepAfterPublishDetection = 3;
                DetectUnrealPublishChangesButton_Click(sender, e);
                return;
            }

            if (sync.WorkflowStep == 3)
            {
                if (!sync.HasNoPublishChanges)
                {
                    ShowFloatingTip(
                        InfoBarSeverity.Informational,
                        "第三步尚未完成",
                        "请先同步已勾选素材；确认没有待同步内容后，才能进入基础配置。");
                    return;
                }

                // 第三步已经确认没有待同步内容时，只进入第四步；这里不能再次调用
                // 素材同步事件，否则会重复执行同步前检测和同步操作。
                sync.CompletePublishOperation(0, 0);
                if (!sync.IsLightConfigurationLoaded)
                {
                    await ReloadUnrealLightConfigurationStepAsync(sync);
                }
                return;
            }

            if (sync.WorkflowStep == 4)
            {
                if (!sync.CanAdvanceWorkflow)
                {
                    ShowFloatingTip(InfoBarSeverity.Informational, "第四步尚未完成", "请先完成基础配置。" );
                    return;
                }

                sync.ReturnToWorkflowStep(5);
                _workflowStepAfterPublishDetection = 5;
                DetectUnrealPublishChangesButton_Click(sender, e);
                return;
            }

            if (!sync.AdvanceWorkflowStep())
            {
                ShowFloatingTip(InfoBarSeverity.Informational, "当前步骤尚未完成", "请先完成当前步骤。");
            }
        }

        private async void ReloadUnrealWorkflowStepButton_Click(object sender, RoutedEventArgs e)
        {
            var sync = _applicationViewModel.UnrealProjectSync;
            var characterCode = sync.SelectedSource?.DraftCharacter?.Code;
            if (string.IsNullOrWhiteSpace(characterCode))
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择已完成角色", "请先在左侧选择一个已完成角色。");
                return;
            }

            if (sync.WorkflowStep == 1)
            {
                sync.RefreshFoundationChecks(characterCode);
                ShowFloatingTip(InfoBarSeverity.Informational, "底层检测已重新加载", sync.FoundationSummaryText);
                return;
            }

            if (sync.WorkflowStep == 2)
            {
                await ReloadUnrealNormalizationStepAsync(sync);
                return;
            }

            if (sync.WorkflowStep == 3)
            {
                _workflowStepAfterPublishDetection = sync.WorkflowStep;
                DetectUnrealPublishChangesButton_Click(sender, e);
                return;
            }

            if (sync.WorkflowStep == 4)
            {
                await ReloadUnrealLightConfigurationStepAsync(sync);
                return;
            }

            if (sync.WorkflowStep == 5)
            {
                _workflowStepAfterPublishDetection = 5;
                DetectUnrealPublishChangesButton_Click(sender, e);
                return;
            }

            ShowFloatingTip(
                sync.ReloadPublishResult() ? InfoBarSeverity.Success : InfoBarSeverity.Warning,
                "同步结果已重新加载",
                sync.ImportResultMessage);
        }

        private async Task ReloadUnrealNormalizationStepAsync(UnrealProjectSyncViewModel sync)
        {
            var characterCode = sync.SelectedSource?.DraftCharacter?.Code;
            if (string.IsNullOrWhiteSpace(characterCode))
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择已完成角色", "请先在左侧选择一个已完成角色。");
                return;
            }
            if (!TryBeginUnrealWorkflowOperation())
            {
                return;
            }

            ShowGlobalProgress("重新加载规整素材", characterCode);
            try
            {
                await sync.ExportProjectCharactersAsync(
                    [characterCode],
                    cancellationToken: GetGlobalProgressCancellationToken(),
                    scope: UnrealProjectSyncExportScope.Normalization);
                if (!await sync.OpenNormalizationWorkspaceAsync())
                {
                    throw new InvalidOperationException("无法读取当前角色的 Unreal 素材，请先检查项目和角色来源。");
                }

                CompleteGlobalProgress("规整素材已重新加载", sync.NormalizationSummaryText);
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (OperationCanceledException ex)
            {
                CompleteGlobalProgress("重新加载已取消", characterCode);
                AppendLog(LogKind.Warning, "重新加载 Unreal 规整素材已取消。", ex);
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (Exception ex)
            {
                CompleteGlobalProgress("重新加载规整素材失败", ex.Message);
                ShowFloatingTip(InfoBarSeverity.Error, "重新加载规整素材失败", ex.Message);
                AppendLog(LogKind.Error, "重新加载 Unreal 规整素材失败。", ex);
                await HideGlobalProgressAfterDelayAsync();
            }
            finally
            {
                EndUnrealWorkflowOperation();
            }
        }

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

                if (Settings.BackupBeforeUnrealSync)
                {
                    await CreateUnrealProjectBackupAsync(character.Code, sync.EnginePath, sync.ProjectPath);
                }

                UpdateGlobalProgress("正在写入并验证基础配置...", 45, $"{selectedIds.Count} 项", true);
                var result = await ExecuteUnrealLightConfigurationAsync(character, apply: true, selectedIds);
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
            IReadOnlyCollection<string> selectedStableIds)
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
            var offlineStartInfo = service.BuildProcessStartInfo(
                sync.EnginePath,
                sync.ProjectPath,
                requestPath,
                resultPath);
            var launch = new UnrealPythonTaskExecutionService().BuildLaunch(
                sync.EnginePath,
                sync.ProjectPath,
                service.GetScriptPath(),
                Path.Combine(workFolder, apply ? "apply.remote-job.json" : "scan.remote-job.json"),
                offlineStartInfo);
            return await service.ExecuteAsync(
                launch.StartInfo,
                resultPath,
                GetGlobalProgressCancellationToken());
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
