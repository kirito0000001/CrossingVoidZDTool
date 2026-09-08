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
    /// <summary>虚幻 -> 工具箱方向：拉取角色候选、导入草稿、检测导入差异。</summary>
    public sealed partial class MainWindow
    {
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
    }
}
