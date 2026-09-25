using System;
using System.Collections.Generic;
using System.Linq;
using CrossingVoidZDTool.Services;
using Microsoft.UI.Xaml;

namespace CrossingVoidZDTool.ViewModels;

/// <summary>
/// 第四步「序列同步」自己的那一块（一步一个文件；前三步见各自 partial）。
///
/// 第四步和前几步不一样：它**没有**一大坨私有集合。它的差异列表就住在
/// <see cref="SelectionTreeRoots"/> —— 它和第二步共用同一个槽位（范围不同），
/// 靠 <c>_loadedPublishStep</c> 区分这棵树归谁。所以这个文件装三样东西：
///
/// 1. 这一步的工作区开关与进入前的目录校验；
/// 2. **「第四步要什么」的唯一口径**（导出范围 / 只看哪些模块 / 要不要比素材内容 /
///    默认勾不勾 / 走哪个发布阶段）——以前这些判断以 `== 4`、`is 4 or 6` 的字面量
///    散在壳侧的检测与发布两条长方法里，改一处口径要翻两个文件；
/// 3. 它自己的小缓存 `step4-sequence-sync.json`（序列差异 + 勾选）。
///
/// **第四步和第六步的关系**（2026-09-24 改过一次，别按旧的看）：
///
/// - **发布链路共用**：第六步仍走同一条桥接脚本与发布管线（`isEffectLayer` 那条只出
///   sheet + 材质实例），所以"要序列帧那一套数据"的判断继续写成 <c>4 or 6</c>，
///   而"这就是第四步"写成 <c>== 4</c>，两者不要混。
/// - **界面不再共用**：第六步现在有自己的一块中栏（`Step6EffectSync.cs` + 自己那段 XAML）。
///   以前它借第四步的差异树面板，而那棵树对第六步永远是空的 —— 特效清单根本没地方显示。
/// </summary>
internal sealed partial class UnrealProjectSyncViewModel
{
    // ── 工作区（第四步和第六步共用同一块界面）──────────────────────────────

    /// <summary>
    /// 第四步「序列同步」的工作区。
    ///
    /// ⚠️ 这里**只认第 4 步**。2026-09-24 之前它是 `4 or 6`（当时特效还挂在序列那一步的界面上），
    /// 于是第六步的中栏显示的是**第四步那棵空差异树** —— 特效自己的清单没有地方显示。
    /// 现在第六步有自己的一块（见 <see cref="IsEffectSyncWorkspace"/>）。
    /// </summary>
    public bool IsSequenceSynchronizationWorkspace => !IsEngineToToolbox && WorkflowStep == 4;

    public Visibility SequenceSynchronizationDetailsVisibility => IsSequenceSynchronizationWorkspace
        ? Visibility.Visible
        : Visibility.Collapsed;

    /// <summary>当前正好停在第四步（不含第六步）。壳侧要区分四/六时问这个。</summary>
    public bool IsSequenceSyncStep => WorkflowStep == 4;

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

    /// <summary>第四/六步都要"序列帧那一套"的导出范围。</summary>
    public static bool UsesSequenceData(int step) => step is 4 or 6;

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
    /// 这一步要不要拿 Unreal 导出清单**校验资产类型**（蓝图 / WidgetBlueprint / MetaSoundSource…）。
    ///
    /// 素材那几档都要（第 1 步的「底层检测」也在这档里 —— 它查的就是蓝图类型），
    /// 第四/六步不校验：它们看的是序列内容，不看资产类型。
    /// 以前这个开关散在几处默认参数里，第 1 步因此漏成了"不查"，
    /// 界面上只能显示占位串「等待 Unreal 类型复检」——现在按口径统一问这里。
    /// </summary>
    public static bool RequiresAssetTypesFor(int step) => !UsesSequenceData(step);

    /// <summary>
    /// 第四步算出来的差异默认**不勾**（一条条序列要人确认过再同步），
    /// 第二步默认勾上（素材那批通常整批同步）。
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

    /// <summary>
    /// **差异检测**那一次导出该用哪个范围：当前步和这次检测的目标步，
    /// 只要有一个要"序列帧那一套"，就按序列导。
    ///
    /// 为什么当前步也要算进来：导出结果同时喂着当前步的预览与候选，只看目标步会把它喂漏。
    /// 口径收在这一处 —— 壳侧原来就地写了一遍同样的三元（两处写法，迟早漂开）。
    /// </summary>
    public static UnrealProjectSyncExportScope ResolveDetectionExportScope(int currentStep, int targetStep) =>
        ExportScopeFor(UsesSequenceData(currentStep) ? currentStep : targetStep);

    // ── 自己的缓存文件 ────────────────────────────────────────────────────

    /// <summary>把当前这棵序列差异树 + 勾选写进第 4 步自己的缓存文件。</summary>
    /// ⚠️ **树不归这一步就别写**（同第 2 步：防抖会全量写一遍，空状态不该覆盖有效文件）。
    internal bool SaveSequenceSyncCache(CharacterCard? character) =>
        character is not null &&
        _loadedPublishStep == 4 &&
        Step4SequenceSyncCache.Save(
            character,
            _lastPublishChanges,
            GetSelectedStableIds(),
            GetSelectedGroupAndLeafStableIds(),
            CurrentDetectionAlgorithmVersion);


    /// <summary>
    /// 把第 4 步自己的小缓存回填成差异树（内存里已经有一棵树时不覆盖）。
    /// 返回 true 表示确实用文件里的结果建好了树。
    ///
    /// 建树的姿势和第二步/会话缓存那条路保持一致：先认领这棵树（`SetLoadedPublishStep(4)`），
    /// 再按序列口径建树 + 恢复勾选，最后交给 <see cref="SetPublishSelectionTree"/>。
    /// </summary>
    internal bool TryApplySequenceSyncCache(CharacterCard? character)
    {
        if (character is null || HasImportDetection)
        {
            return false;
        }

        var document = Step4SequenceSyncCache.TryLoad(
            character,
            character.Code,
            CurrentDetectionAlgorithmVersion);
        if (document is null || document.Changes.Length == 0)
        {
            return false;
        }

        // 缓存里存的是整份序列差异（含 Unchanged）；过滤口径要和检测时一致，
        // 否则会拿第二步的素材变更去建第四步的树。
        var changes = FilterPublishChanges(document.Changes).ToArray();
        if (changes.Length == 0)
        {
            return false;
        }

        // 恢复期间必须屏蔽写盘：SetLoadedPublishStep(4) 会顺手把这一步的小缓存存一次，
        // 而那一刻 _lastPublishChanges 还是上一次的（树还没建），存下去就是把好缓存
        // 覆盖成旧的。整段恢复跑完，下次真正检测时自然会重新写一份。
        var wasRestoring = _isRestoringSession;
        _isRestoringSession = true;
        try
        {
            SetLoadedPublishStep(4);
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
