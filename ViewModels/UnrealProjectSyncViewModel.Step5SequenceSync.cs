using System;
using System.Collections.Generic;
using System.Linq;
using CrossingVoidZDTool.Services;
using Microsoft.UI.Xaml;

namespace CrossingVoidZDTool.ViewModels;

/// <summary>
/// 第五步「序列同步」自己的那一块（一步一个文件；前四步见各自 partial）。
///
/// 第五步和前四步不一样：它**没有**一大坨私有集合。它的差异列表就住在
/// <see cref="SelectionTreeRoots"/> —— 它和第三步共用同一个槽位（范围不同），
/// 靠 <c>_loadedPublishStep</c> 区分这棵树归谁。所以这个文件装三样东西：
///
/// 1. 这一步的工作区开关与进入前的目录校验；
/// 2. **「第五步要什么」的唯一口径**（导出范围 / 只看哪些模块 / 要不要比素材内容 /
///    默认勾不勾 / 走哪个发布阶段）——以前这些判断以 `== 5`、`is 5 or 7` 的字面量
///    散在壳侧的检测与发布两条长方法里，改一处口径要翻两个文件；
/// 3. 它自己的小缓存 `step5-sequence-sync.json`（序列差异 + 勾选）。
///
/// **第五步和第七步的关系**：第七步「特效同步」复用第五步这块界面，也要
/// "序列帧那一套"导出（只是计划换成 `BuildEffectSyncPlan`）。所以下面凡是
/// "要序列数据"的判断都写成 <c>5 or 7</c>，而"这就是第五步"写成 <c>== 5</c>，
/// 两者不要混。
/// </summary>
internal sealed partial class UnrealProjectSyncViewModel
{
    // ── 工作区（第五步和第七步共用同一块界面）──────────────────────────────

    public bool IsSequenceSynchronizationWorkspace => !IsEngineToToolbox && WorkflowStep is 5 or 7;

    public Visibility SequenceSynchronizationDetailsVisibility => IsSequenceSynchronizationWorkspace
        ? Visibility.Visible
        : Visibility.Collapsed;

    /// <summary>当前正好停在第五步（不含第七步）。壳侧要区分五/七时问这个。</summary>
    public bool IsSequenceSyncStep => WorkflowStep == 5;

    /// <summary>
    /// 进这一步之前要校验的 Unreal 目录：序列那套和素材那套查的东西完全不同
    /// （序列要 AnimSequences / Material / Item，素材要的是角色素材目录）。
    /// </summary>
    public void ValidateFoldersForStep(int step, string projectPath, string characterCode)
    {
        if (UsesSequenceData(step))
        {
            ValidateSequenceCharacterFolders(projectPath, characterCode);
            return;
        }

        ValidatePublishCharacterFolders(characterCode);
    }

    public void ValidateSequenceCharacterFolders(string projectPath, string characterCode) =>
        _syncService.ValidateSequenceCharacterFolders(projectPath, characterCode);

    // ── 「这一步要什么」的唯一口径（按步号问，壳侧别再写字面量）─────────────

    /// <summary>第五/七步都要"序列帧那一套"的导出范围。</summary>
    public static bool UsesSequenceData(int step) => step is 5 or 7;

    /// <summary>这一步检测时只看哪些模块（别的模块各有自己的步骤）。</summary>
    public static UnrealBridgeModule[] DetectionModulesFor(int step) =>
        UsesSequenceData(step)
            ? [UnrealBridgeModule.SequenceFrames]
            : [UnrealBridgeModule.BaseMaterials, UnrealBridgeModule.Voices];

    /// <summary>
    /// 序列检测要比"素材内容"，而图集改造后每一帧指向的都是同一张图集 ——
    /// 贴图路径再也分不出帧与帧的区别，只能拿上次同步记下的摘要补上。
    /// 素材那一路没这个问题。
    /// </summary>
    public static bool UsesRecordedSequenceContent(int step) => UsesSequenceData(step);

    /// <summary>
    /// 第五步算出来的差异默认**不勾**（一条条序列要人确认过再同步），
    /// 第三步默认勾上（素材那批通常整批同步）。
    /// </summary>
    public static bool SelectsPendingChangesByDefault(int step) => !UsesSequenceData(step);

    /// <summary>这一步该停在哪个发布阶段：序列 → ZD 动画轨道；素材 → 角色素材。</summary>
    public static UnrealBridgePublishStage PublishStageFor(int step) =>
        UsesSequenceData(step)
            ? UnrealBridgePublishStage.ZdAnimationTracks
            : UnrealBridgePublishStage.CharacterMaterials;

    /// <summary>这一步导出 Unreal 时的范围。</summary>
    public static UnrealProjectSyncExportScope ExportScopeFor(int step) =>
        UsesSequenceData(step)
            ? UnrealProjectSyncExportScope.CharacterSequences
            : UnrealProjectSyncExportScope.CharacterMaterials;

    // ── 自己的缓存文件 ────────────────────────────────────────────────────

    /// <summary>把当前这棵序列差异树 + 勾选写进第 5 步自己的缓存文件。</summary>
    internal bool SaveSequenceSyncCache(CharacterCard? character) =>
        character is not null &&
        Step5SequenceSyncCache.Save(
            character,
            _lastPublishChanges,
            GetSelectedStableIds(),
            GetSelectedGroupAndLeafStableIds(),
            CurrentDetectionAlgorithmVersion);

    /// <summary>
    /// 从一份"正要落盘的会话缓存快照"里，把第五步那一份抄进它自己的小文件。
    ///
    /// 用同一份快照是有意的：两个文件里第五步的内容因此不会各说各话 ——
    /// "界面上是新树、缓存里还是旧树"这类错就是两份来源各自演化来的。
    /// 快照里的 <c>WorkflowStep</c> 不是 5（正在最后一步收尾）时什么都不写，
    /// 那种时刻由 <c>SetLoadedPublishStep(5)</c> 那条路负责。
    /// </summary>
    internal static bool SaveSequenceSyncCacheFromSnapshot(CharacterCard? character, UnrealSyncSessionCache cache) =>
        character is not null &&
        cache.WorkflowStep == 5 &&
        cache.IsPublishDetection &&
        cache.PublishChanges.Count > 0 &&
        Step5SequenceSyncCache.Save(
            character,
            cache.PublishChanges,
            cache.SelectedStableIds,
            cache.SelectedGroupStableIds,
            cache.DetectionAlgorithmVersion);

    /// <summary>
    /// 把第 5 步自己的小缓存回填成差异树（内存里已经有一棵树时不覆盖）。
    /// 返回 true 表示确实用文件里的结果建好了树。
    ///
    /// 建树的姿势和第三步/会话缓存那条路保持一致：先认领这棵树（`SetLoadedPublishStep(5)`），
    /// 再按序列口径建树 + 恢复勾选，最后交给 <see cref="SetPublishSelectionTree"/>。
    /// </summary>
    internal bool TryApplySequenceSyncCache(CharacterCard? character)
    {
        if (character is null || HasImportDetection)
        {
            return false;
        }

        var document = Step5SequenceSyncCache.TryLoad(
            character,
            character.Code,
            CurrentDetectionAlgorithmVersion);
        if (document is null || document.Changes.Length == 0)
        {
            return false;
        }

        // 缓存里存的是整份序列差异（含 Unchanged）；过滤口径要和检测时一致，
        // 否则会拿第三步的素材变更去建第五步的树。
        var changes = FilterPublishChanges(document.Changes).ToArray();
        if (changes.Length == 0)
        {
            return false;
        }

        // 恢复期间必须屏蔽写盘：SetLoadedPublishStep(5) 会顺手把这一步的小缓存存一次，
        // 而那一刻 _lastPublishChanges 还是上一次的（树还没建），存下去就是把好缓存
        // 覆盖成旧的。整段恢复跑完，下次真正检测时自然会重新写一份。
        var wasRestoring = _isRestoringSession;
        _isRestoringSession = true;
        try
        {
            SetLoadedPublishStep(5);
            var roots = UnrealSyncSelectionTreeBuilder.FromSequenceChanges(
                changes,
                UnrealBridgePublishSupportPolicy.CanExecute,
                selectPendingByDefault: false);
            var restoreIds = document.SelectedStableIds
                .Union(document.SelectedGroupStableIds, StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            ApplySelection(roots, restoreIds);
            SetPublishSelectionTree(roots, changes);
        }
        finally
        {
            _isRestoringSession = wasRestoring;
        }

        return true;
    }
}
