using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;
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

        private async void GetUnrealProjectCharactersButton_Click(object sender, RoutedEventArgs e)
        {
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
            var character = _applicationViewModel.UnrealProjectSync.SelectedSource?.DraftCharacter;
            if (character is null)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择已完成角色", "请先在左侧选择一个已完成角色。");
                return;
            }

            var enginePath = _applicationViewModel.UnrealProjectSync.EnginePath;
            var projectPath = _applicationViewModel.UnrealProjectSync.ProjectPath;
            var stateService = new UnrealBridgeStateService();
            var baseline = stateService.Load(character, projectPath);

            try
            {
                _applicationViewModel.UnrealProjectSync.ValidatePublishCharacterFolders(character.Code);
                // 最终同步前始终刷新 Unreal 快照，页面缓存只用于恢复工作进度。
                await _applicationViewModel.UnrealProjectSync.ExportProjectCharactersAsync(
                    [character.Code],
                    cancellationToken: GetGlobalProgressCancellationToken());
                _applicationViewModel.UnrealProjectSync.ValidatePublishCharacterFolders(character.Code, requireAssetTypes: true);
                var latestCandidate = _applicationViewModel.UnrealProjectSync.CharacterCandidates.FirstOrDefault(item =>
                    string.Equals(item.Code, character.Code, StringComparison.OrdinalIgnoreCase));
                if (latestCandidate is null || !latestCandidate.CharacterInfo.HasData)
                {
                    throw new InvalidOperationException($"未找到 Unreal 角色 Item 资产。\n正确名称示例：Item_{character.Code}\n期望路径：/Game/ITems/CharItemS/Item_{character.Code}.Item_{character.Code}");
                }
                var latestChanges = new UnrealBridgeDiffService().Compare(
                    new UnrealBridgeToolboxSnapshotService().BuildForSynchronization(character),
                    new UnrealBridgeSemanticSnapshotService().Build(latestCandidate),
                    UnrealBridgeDirection.PublishToUnreal,
                    baseline)
                    .Select(change => change with { IsSelected = change.IsSelected && UnrealBridgePublishSupportPolicy.CanExecute(change) })
                    .ToArray();
                latestChanges = _applicationViewModel.UnrealProjectSync.FilterPublishChanges(latestChanges).ToArray();
                var previousSelection = _applicationViewModel.UnrealProjectSync.GetSelectedStableIds();
                _applicationViewModel.UnrealProjectSync.SetPublishSelectionTree(
                    UnrealSyncSelectionTreeBuilder.FromChanges(latestChanges, UnrealBridgePublishSupportPolicy.CanExecute));
                foreach (var leaf in _applicationViewModel.UnrealProjectSync.SelectionTreeRoots.SelectMany(root => root.Children))
                {
                    leaf.IsChecked = previousSelection.Contains(leaf.StableId) && leaf.IsSelectable;
                }
                var changes = _applicationViewModel.UnrealProjectSync.SelectionTreeRoots
                    .SelectMany(root => root.Children)
                    .Where(item => item.Change is not null)
                    .Select(item => item.Change! with { IsSelected = item.IsChecked == true })
                    .ToArray();
                if (changes.Length == 0)
                {
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
                var executableCount = changes.Count(change => change.IsSelected);
                var deferredCount = changes.Count(change =>
                    change.Kind != UnrealBridgeChangeKind.Unchanged && !change.IsSelected);
                if (executableCount == 0)
                {
                    _applicationViewModel.UnrealProjectSync.CompletePublishOperation(0, deferredCount);
                    CompleteGlobalProgress("没有可自动同步的素材改动", deferredCount == 0
                        ? "两端已有素材一致。"
                        : $"还有 {deferredCount} 项属于新增、删除、冲突或语义结构，需要在差异树中明确处理。");
                    await HideGlobalProgressAfterDelayAsync();
                    return;
                }

                var plan = new UnrealBridgeExecutionPlanService().Build(
                    UnrealBridgeDirection.PublishToUnreal,
                    character.Code,
                    projectPath,
                    changes,
                    deletionsConfirmed: false,
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

                UpdateGlobalProgress("正在写入 Unreal 素材...", 58, $"{executableCount} 项", true);
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
                var result = await executor.ExecuteAsync(
                    executor.BuildProcessStartInfo(enginePath, projectPath, planPath, progressPath, resultPath),
                    progressPath,
                    resultPath,
                    new Progress<UnrealBridgeExecutionProgress>(value =>
                        UpdateGlobalProgress(value.Message, 58 + value.CompletedCount * 24d / Math.Max(1, value.TotalCount), value.StableId)),
                    GetGlobalProgressCancellationToken());

                UpdateGlobalProgress("正在复扫验证...", 86, character.Code, true);
                await _applicationViewModel.UnrealProjectSync.ExportProjectCharactersAsync(
                    [character.Code],
                    cancellationToken: GetGlobalProgressCancellationToken());
                var refreshedCandidate = _applicationViewModel.UnrealProjectSync.CharacterCandidates.First(item =>
                    string.Equals(item.Code, character.Code, StringComparison.OrdinalIgnoreCase));
                var rescanned = new UnrealBridgeSemanticSnapshotService().Build(refreshedCandidate);
                rescanned = new UnrealBridgePostExecutionIdentityService().Restore(rescanned, result);
                var verifiedState = new UnrealBridgeVerificationService().BuildVerifiedState(
                    plan,
                    result,
                    new UnrealBridgeToolboxSnapshotService().Build(character),
                    rescanned);
                stateService.Save(character, projectPath, verifiedState);
                _applicationViewModel.UnrealProjectSync.CompletePublishOperation(executableCount, deferredCount);
                CompleteGlobalProgress("同步到虚幻完成", $"已验证 {executableCount} 项；另有 {deferredCount} 项未执行。");
                ShowFloatingTip(InfoBarSeverity.Success, "同步到虚幻完成", $"已验证 {executableCount} 项。");
                AppendLog(LogKind.User, $"同步所选内容到 Unreal：{character.Code}，Executed={executableCount}，Deferred={deferredCount}。");
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
                AppendLog(LogKind.Error, "同步已有素材到 Unreal 失败。", ex);
                await HideGlobalProgressAfterDelayAsync();
            }
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
                    AppendLog(LogKind.User, $"切换虚幻同步方向：{_applicationViewModel.UnrealProjectSync.DirectionTitle}");
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
            AppendLog(LogKind.User, $"选择虚幻同步来源：{source.DisplayName} / {source.SecondaryText}");
        }

        private async void DetectUnrealImportButton_Click(object sender, RoutedEventArgs e)
        {
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
                _applicationViewModel.UnrealProjectSync.SetImportSelectionTree(
                    UnrealSyncSelectionTreeBuilder.FromSnapshot(snapshot),
                    existingStableIds,
                    snapshot);
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
        }

        private async void DetectUnrealPublishChangesButton_Click(object sender, RoutedEventArgs e)
        {
            var character = _applicationViewModel.UnrealProjectSync.SelectedSource?.DraftCharacter;
            if (character is null)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择已完成角色", "请先在左侧选择一个已完成角色。");
                return;
            }

            var baseline = new UnrealBridgeStateService().Load(character, _applicationViewModel.UnrealProjectSync.ProjectPath);

            try
            {
                _applicationViewModel.UnrealProjectSync.ValidatePublishCharacterFolders(character.Code);
                ShowGlobalProgress("检测同步差异", character.Code);
                await _applicationViewModel.UnrealProjectSync.ExportProjectCharactersAsync(
                    [character.Code],
                    new Progress<ProgressUpdate>(update =>
                        UpdateGlobalProgress(update.Message, Math.Min(70, update.Percent * 0.7), update.Detail, update.IsIndeterminate)),
                    GetGlobalProgressCancellationToken());
                _applicationViewModel.UnrealProjectSync.ValidatePublishCharacterFolders(character.Code, requireAssetTypes: true);
                var candidate = _applicationViewModel.UnrealProjectSync.CharacterCandidates.FirstOrDefault(item =>
                    string.Equals(item.Code, character.Code, StringComparison.OrdinalIgnoreCase));
                if (candidate is null || !candidate.CharacterInfo.HasData)
                {
                    throw new InvalidOperationException(
                        $"未找到 Unreal 角色 Item 资产。\n" +
                        $"正确名称示例：Item_{character.Code}\n" +
                        $"期望路径：/Game/ITems/CharItemS/Item_{character.Code}.Item_{character.Code}\n" +
                        $"请检查 Unreal 内容浏览器中的资产名称和路径是否正确。");
                }
                var changes = new UnrealBridgeDiffService().Compare(
                    new UnrealBridgeToolboxSnapshotService().BuildForSynchronization(character),
                    new UnrealBridgeSemanticSnapshotService().Build(candidate),
                    UnrealBridgeDirection.PublishToUnreal,
                    baseline)
                    .Select(change => change with
                    {
                        IsSelected = change.IsSelected && UnrealBridgePublishSupportPolicy.CanExecute(change)
                    })
                    .ToArray();
                changes = _applicationViewModel.UnrealProjectSync.FilterPublishChanges(changes).ToArray();
                _applicationViewModel.UnrealProjectSync.ReturnToWorkflowStep(1);
                _applicationViewModel.UnrealProjectSync.SetPublishSelectionTree(
                    UnrealSyncSelectionTreeBuilder.FromChanges(changes, UnrealBridgePublishSupportPolicy.CanExecute));
                var changedCount = changes.Count(change => change.Kind != UnrealBridgeChangeKind.Unchanged);
                CompleteGlobalProgress("差异检测完成", $"发现 {changedCount} 项变化；冲突和重定向项未默认勾选。");
                var targetWorkflowStep = _workflowStepAfterPublishDetection;
                _workflowStepAfterPublishDetection = 0;
                if (targetWorkflowStep == 2)
                {
                    _applicationViewModel.UnrealProjectSync.AdvanceWorkflowStep();
                }
                else if (targetWorkflowStep == 3)
                {
                    _applicationViewModel.UnrealProjectSync.OpenNormalizationWorkspace();
                    _applicationViewModel.UnrealProjectSync.ReturnToWorkflowStep(3);
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
        }

        private void OpenUnrealNormalizationButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_applicationViewModel.UnrealProjectSync.OpenNormalizationWorkspace())
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "无法打开素材规整", "请先选择已完成角色，并至少检测一次当前 Unreal 内容。");
                return;
            }

            AppendLog(LogKind.User, "打开 Unreal 素材规整工作区。");
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

        private void UnrealSyncNextStepButton_Click(object sender, RoutedEventArgs e)
        {
            if (_applicationViewModel.UnrealProjectSync.WorkflowStep == 1 &&
                !_applicationViewModel.UnrealProjectSync.HasContentDetection)
            {
                _workflowStepAfterPublishDetection = 2;
                DetectUnrealPublishChangesButton_Click(sender, e);
                return;
            }

            if (_applicationViewModel.UnrealProjectSync.WorkflowStep == 3)
            {
                PublishCurrentCharacterAssetsToUnrealButton_Click(sender, e);
                return;
            }

            if (!_applicationViewModel.UnrealProjectSync.AdvanceWorkflowStep())
            {
                var message = _applicationViewModel.UnrealProjectSync.WorkflowStep == 2
                    ? $"还有 {_applicationViewModel.UnrealProjectSync.NormalizationItems.Count(item => !item.IsResolved)} 项 Unreal 素材没有选择处理方式。"
                    : "选择角色后会自动检测，请等待检测完成。";
                ShowFloatingTip(InfoBarSeverity.Informational, "当前步骤尚未完成", message);
            }
        }

        private void ReloadUnrealWorkflowStepButton_Click(object sender, RoutedEventArgs e)
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

            if (sync.WorkflowStep is 2 or 3)
            {
                _workflowStepAfterPublishDetection = sync.WorkflowStep;
                DetectUnrealPublishChangesButton_Click(sender, e);
                return;
            }

            ShowFloatingTip(
                sync.ReloadPublishResult() ? InfoBarSeverity.Success : InfoBarSeverity.Warning,
                "同步结果已重新加载",
                sync.ImportResultMessage);
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
