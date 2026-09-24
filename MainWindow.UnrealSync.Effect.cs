using System.Threading.Tasks;
using CrossingVoidZDTool.Services;
using CrossingVoidZDTool.Services.Atlas;
using Microsoft.UI.Xaml.Controls;

namespace CrossingVoidZDTool
{
    /// <summary>
    /// 第七步「特效同步」的壳侧接线。
    ///
    /// 这一步和第五步最大的不同：**它不需要 Unreal 的全量导出**。
    /// 特效该有几张、网格几×几、哪几格是空的，全部来自工作区的特效帧目录；
    /// 所以这一步只做两件本地的事 —— 打网格 sheet、建特效计划 ——
    /// 不会像第五步那样把整条序列的帧从 Unreal 打开一遍（用户明确要求省掉那一步）。
    ///
    /// 真正写进 Unreal 的动作仍然走同一条发布链路（见 <c>UnrealSyncPublishController</c>）。
    /// </summary>
    public sealed partial class MainWindow
    {
        private async Task ReloadUnrealEffectSyncStepAsync(ViewModels.UnrealProjectSyncViewModel sync)
        {
            var character = sync.SelectedSource?.DraftCharacter;
            if (character is null)
            {
                return;
            }

            // 1) 本地打网格 sheet：每个有特效层的动作一张（空帧用全透明图占格）。
            var atlases = await new SequenceAtlasPackService().PackEffectSheetsAsync(
                character,
                Settings.AtlasPythonPath,
                null,
                GetGlobalProgressCancellationToken());

            // 2) 本地建计划：凡是有特效层的动作都算一条（不看 Unreal 的差异树）。
            var plan = new UnrealBridgeSequencePublishService()
                .BuildEffectSyncPlanForAll(character, Settings.UnrealProjectPath, atlases);

            foreach (var action in plan.Actions)
            {
                var sheetState = string.IsNullOrEmpty(action.EffectSheetImagePath)
                    ? "sheet 没打出来（会被跳过）"
                    : "sheet 已就绪";
                AppendLog(
                    LogKind.Info,
                    $"[St7] 特效同步 · {action.DisplayName}：{action.Frames.Count} 帧 · "
                    + $"网格 {action.EffectColumns}×{action.EffectRows} · {sheetState} · "
                    + $"MI={action.EffectMaterialName}");
            }

            ShowFloatingTip(
                InfoBarSeverity.Informational,
                "特效同步已检测（本地）",
                plan.Actions.Count == 0
                    ? "这个角色没有任何动作带特效层，没有要同步的东西。"
                    : $"{plan.Actions.Count} 个动作有特效：网格 sheet 已打好，未跑 Unreal 导出。");
        }
    }
}
