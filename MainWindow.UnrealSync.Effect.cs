using System;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;
using CrossingVoidZDTool.Services.Atlas;
using CrossingVoidZDTool.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace CrossingVoidZDTool
{
    /// <summary>
    /// 第六步「特效同步」的壳侧接线。
    ///
    /// 这一步和第四步最大的不同：**它不需要 Unreal 的全量导出**。
    /// 特效该有几张、网格几×几、哪几格是空的，全部来自工作区的特效帧目录；
    /// 所以这一步只做两件本地的事 —— 打网格 sheet、建特效计划 ——
    /// 不会像第四步那样把整条序列的帧从 Unreal 打开一遍（用户明确要求省掉那一步）。
    ///
    /// 真正写进 Unreal 的动作仍然走同一条发布链路（见 <c>UnrealSyncPublishController</c>）。
    /// </summary>
    public sealed partial class MainWindow
    {
        /// <summary>打 sheet 这一段的进度窗口：一个动作一张图，逐张推进。</summary>
        private const double EffectSheetProgressFloor = 6;
        private const double EffectSheetProgressCeiling = 92;

        private async Task ReloadUnrealEffectSyncStepAsync(UnrealProjectSyncViewModel sync)
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

            ShowGlobalProgress("检测特效同步", character.Code);
            try
            {
                sync.ReturnToWorkflowStep(6);

                // 阶段 1/2：本地打网格 sheet。每个有特效层的动作一张，空帧用全透明图占格。
                //
                // 这一段以前**一条进度都不报**（`PackEffectSheetsAsync` 明明收 `IProgress`，
                // 调用处传的是 null）—— 打十几张 sheet 的几十秒里进度条一动不动。
                UpdateGlobalProgress(
                    "阶段 1/2 · 正在打包特效网格",
                    EffectSheetProgressFloor,
                    $"角色：{character.Code}",
                    true);
                var atlases = await new SequenceAtlasPackService().PackEffectSheetsAsync(
                    character,
                    Settings.AtlasPythonPath,
                    new Progress<AtlasPackProgress>(state => UpdateGlobalProgress(
                        "阶段 1/2 · 正在打包特效网格",
                        EffectSheetProgressFloor
                            + (EffectSheetProgressCeiling - EffectSheetProgressFloor)
                            * (state.Stage switch
                            {
                                AtlasPackStage.Preparing => 0.2,
                                AtlasPackStage.Packing => 0.6,
                                _ => 1.0
                            }),
                        state.Message,
                        true)),
                    GetGlobalProgressCancellationToken());

                // 阶段 2/2：本地建计划。凡是有特效层的动作都算一条（不看 Unreal 的差异树）。
                UpdateGlobalProgress(
                    "阶段 2/2 · 正在生成特效清单",
                    EffectSheetProgressCeiling,
                    $"已打 {atlases.Count} 张网格 sheet",
                    true);
                var plan = new UnrealBridgeSequencePublishService()
                    .BuildEffectSyncPlanForAll(character, Settings.UnrealProjectPath, atlases);
                sync.SetEffectSyncPlan(plan);

                foreach (var item in sync.EffectSyncItems)
                {
                    AppendLog(
                        LogKind.Info,
                        $"[St6] 特效同步 · {item.DisplayName}：{item.FrameCount} 帧 · "
                        + $"网格 {item.GridText} · {item.StatusText} · MI={item.MaterialName}");
                }

                CompleteGlobalProgress("特效检测完成", sync.EffectSyncSummaryText);
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (OperationCanceledException ex)
            {
                CompleteGlobalProgress("特效检测已取消", character.Code);
                AppendLog(LogKind.Warning, "检测特效同步已取消。", ex);
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (Exception ex)
            {
                sync.FailEffectSync(ex.Message);
                CompleteGlobalProgress("特效检测失败", ex.Message);
                ShowFloatingTip(InfoBarSeverity.Error, "特效检测失败", ex.Message);
                AppendLog(LogKind.Error, "检测特效同步失败。", ex);
                await HideGlobalProgressAfterDelayAsync();
            }
            finally
            {
                EndUnrealWorkflowOperation();
            }
        }
    }
}
