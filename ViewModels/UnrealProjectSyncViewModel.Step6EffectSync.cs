using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CrossingVoidZDTool.Services;
using Microsoft.UI.Xaml;

namespace CrossingVoidZDTool.ViewModels;

/// <summary>
/// 第六步「特效同步」自己的那一块（一步一个文件；前五步见各自 partial）。
///
/// 这一步和前五步都不一样：**它不看 Unreal**。特效该有几张、网格几×几、哪几格是空的，
/// 全部来自工作区的特效帧目录 —— 所以它的"检测"是纯本地的（打网格 sheet + 建计划），
/// 不跑 Unreal 全量导出（用户明确要求省掉那一步，见 Docs/特效Niagara-面片与序列同步-设计.md）。
///
/// 装三样东西：
/// 1. **工作区开关与自己的列表**（`EffectSyncItems`：每个有特效层的动作一条）；
/// 2. **加载态与文案**（`IsEffectSyncLoaded` 走步加载表的第 6 格 —— 2/4/6 才是"独立标志"的步）；
/// 3. **自己的小缓存** `step6-effect-sync.json`（清单 + 上次同步时间）。
///
/// 它**没有勾选**：凡是有特效层的动作都要同步，所以列表是只读的展示，
/// 不像第四步那样要恢复勾选（那边的树归 `SelectionTreeRoots`）。
/// </summary>
internal sealed partial class UnrealProjectSyncViewModel
{
    // ── 状态 ──────────────────────────────────────────────────────────────

    /// <summary>中栏那一列：有特效层的动作，一条一个。</summary>
    public ObservableCollection<Step6EffectSyncItem> EffectSyncItems { get; } = [];

    private DateTimeOffset? _effectSyncAppliedAt;

    public bool IsEffectSyncLoaded
    {
        get => _stepLoads.IsLoaded(6);
        private set
        {
            if (!_stepLoads.SetLoaded(6, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsEffectSyncLoaded));
            NotifyEffectSyncChanged();
        }
    }

    /// <summary>上一次真正写进 Unreal 的时间；没同步过就是 null。</summary>
    public DateTimeOffset? EffectSyncAppliedAt => _effectSyncAppliedAt;

    public int EffectSyncActionCount => EffectSyncItems.Count;

    public int EffectSyncReadySheetCount => EffectSyncItems.Count(item => item.SheetReady);

    public string EffectSyncSummaryText => !IsEffectSyncLoaded
        ? "尚未检测特效"
        : EffectSyncItems.Count == 0
            ? "这个角色没有带特效层的动作"
            : $"共 {EffectSyncActionCount} 个动作有特效："
              + $"sheet 已就绪 {EffectSyncReadySheetCount}，缺失 {EffectSyncActionCount - EffectSyncReadySheetCount}";

    public string EffectSyncEmptyTitle => !IsEffectSyncLoaded
        ? "尚未检测特效"
        : "这个角色没有带特效层的动作";

    /// <summary>最后一行：上次同步时间（有的话）。</summary>
    public string EffectSyncAppliedText => _effectSyncAppliedAt is { } applied
        ? $"上次同步：{applied.LocalDateTime:yyyy-MM-dd HH:mm:ss}"
        : "尚未同步到 Unreal";

    // ── 工作区可见性 ──────────────────────────────────────────────────────

    public bool IsEffectSyncWorkspace => !IsEngineToToolbox && WorkflowStep == 6;

    /// <summary>
    /// 这一步要不要露在右侧流程列表里。
    ///
    /// 晓桀 2026-09-25：「先把特效这一步隐藏起来吧，可能之后不会用了」。
    /// 隐藏**只影响界面**：代码、缓存、中栏面板都留着，改
    /// <see cref="UnrealSyncWorkflow.IncludesEffectSyncStep"/> 一处就整条回来。
    /// </summary>
    public bool IsEffectSyncStepVisible => UnrealSyncWorkflow.IncludesEffectSyncStep;

    public Visibility EffectSyncWorkspaceVisibility =>
        IsEffectSyncWorkspace && WorkspaceState == UnrealSyncWorkspaceState.HasContent
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility EffectSyncDetailsVisibility =>
        IsEffectSyncWorkspace ? Visibility.Visible : Visibility.Collapsed;

    // ── 结果落库 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 检测完把计划灌进中栏。**只认特效那一项**（计划里的角色序列项在这里没有意义）。
    /// 顺带写自己的小缓存 —— 这一步的检测是本地且便宜的，但重进这一步时
    /// 列表应该立刻回来，而不是让人再等一次打 sheet。
    /// </summary>
    public void SetEffectSyncPlan(UnrealBridgeSequenceSyncPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        EffectSyncItems.Clear();
        foreach (var item in plan.Actions
                     .Where(action => action.IsEffectLayer)
                     .Select(Step6EffectSyncItem.From))
        {
            EffectSyncItems.Add(item);
        }

        // 重新检测出来的这份还没同步过，时间清掉。
        _effectSyncAppliedAt = null;
        IsEffectSyncLoaded = true;
        ClearWorkspaceFailure();
        NotifyEffectSyncChanged();
        SaveEffectSyncCache(SelectedSource?.DraftCharacter);
        SaveSessionCache();
    }

    /// <summary>同步成功之后记一笔。（这一步没有"逐项勾选"，所以只有一个整批的时间。）</summary>
    public void MarkEffectSyncApplied()
    {
        _effectSyncAppliedAt = DateTimeOffset.Now;
        OnPropertyChanged(nameof(EffectSyncAppliedText));
        SaveEffectSyncCache(SelectedSource?.DraftCharacter);
    }

    /// <summary>检测失败：留痕给中栏，但**不谎报"检测过了"** —— 列表保持原样。</summary>
    public void FailEffectSync(string message)
    {
        SetWorkspaceFailure(message);
        OnPropertyChanged(nameof(EffectSyncSummaryText));
    }

    /// <summary>换角色 / 换工程 / 重置导入时把这一步清干净。</summary>
    private void ClearEffectSyncState()
    {
        EffectSyncItems.Clear();
        _effectSyncAppliedAt = null;
        IsEffectSyncLoaded = false;
        NotifyEffectSyncChanged();
    }

    private void NotifyEffectSyncChanged()
    {
        OnPropertyChanged(nameof(EffectSyncActionCount));
        OnPropertyChanged(nameof(EffectSyncReadySheetCount));
        OnPropertyChanged(nameof(EffectSyncSummaryText));
        OnPropertyChanged(nameof(EffectSyncEmptyTitle));
        OnPropertyChanged(nameof(EffectSyncAppliedText));
        OnPropertyChanged(nameof(EffectSyncWorkspaceVisibility));
        OnPropertyChanged(nameof(EffectSyncDetailsVisibility));
        OnPropertyChanged(nameof(WorkflowStep6StatusText));
        OnPropertyChanged(nameof(CanAdvanceWorkflow));
        // 「同步特效到虚幻」按钮的闸门问的就是这个条数（第六步没有勾选树）。
        OnPropertyChanged(nameof(HasPublishSelection));
        OnPropertyChanged(nameof(CanStartPublish));
        OnPropertyChanged(nameof(PublishActionText));
        NotifyWorkspaceStateChanged();
    }

    // ── 自己的缓存文件 ────────────────────────────────────────────────────

    /// <summary>
    /// 写这一步的小缓存。**空状态不写**：`WriteAllStepCaches` 每次防抖都会调它，
    /// 没检测过就把上一份有效清单盖没了（第 2/4 步踩过同一个坑）。
    /// </summary>
    internal bool SaveEffectSyncCache(CharacterCard? character) =>
        character is not null &&
        IsEffectSyncLoaded &&
        Step6EffectSyncCache.Save(character, EffectSyncItems.ToArray(), _effectSyncAppliedAt);

    /// <summary>把这一步的小缓存回填到中栏（内存里已经有清单时不覆盖）。</summary>
    internal bool TryApplyEffectSyncCache(CharacterCard? character)
    {
        if (character is null || IsEffectSyncLoaded)
        {
            return false;
        }

        var document = Step6EffectSyncCache.TryLoad(character, character.Code);
        if (document is null)
        {
            return false;
        }

        EffectSyncItems.Clear();
        foreach (var item in document.Items)
        {
            EffectSyncItems.Add(item);
        }

        _effectSyncAppliedAt = document.AppliedAtUtc;
        IsEffectSyncLoaded = true;
        NotifyEffectSyncChanged();
        return true;
    }
}
