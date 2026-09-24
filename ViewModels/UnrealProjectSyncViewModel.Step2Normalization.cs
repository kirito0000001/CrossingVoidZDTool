using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CrossingVoidZDTool.Services;
using Microsoft.UI.Xaml;

namespace CrossingVoidZDTool.ViewModels;

/// <summary>
/// 第二步「规整素材」自己的那一块（一步一个文件；第一步见 Step1Foundation.cs）。
///
/// 这一块先接管**它自己的缓存文件**（用户做过的规整决策）。
/// 决策以前存在整体会话缓存里（`UnrealSyncSessionCache.NormalizationDecisions`），
/// 和别的步骤共用一坨；现在读的时候**先读自己的文件**，没有再回退整体缓存（兼容旧数据，不丢历史决策）。
/// </summary>
internal sealed partial class UnrealProjectSyncViewModel
{
    // ── 规整项状态（第 2 步自己的）──────────────────────────────────────────

    public ObservableCollection<UnrealAssetNormalizationItem> NormalizationItems { get; } = [];

    private IReadOnlyList<UnrealAssetNormalizationItem> _visibleNormalizationItems = [];

    public IReadOnlyList<UnrealAssetNormalizationItem> VisibleNormalizationItems
    {
        get => _visibleNormalizationItems;
        private set => SetProperty(ref _visibleNormalizationItems, value);
    }

    private bool _hideResolvedNormalizationItems;

    public bool HideResolvedNormalizationItems
    {
        get => _hideResolvedNormalizationItems;
        set
        {
            if (SetProperty(ref _hideResolvedNormalizationItems, value))
            {
                RefreshVisibleNormalizationItems();
                SaveSessionCache();
            }
        }
    }

    private bool _isNormalizationWorkspace;

    public bool IsNormalizationWorkspace
    {
        get => _isNormalizationWorkspace;
        private set
        {
            if (SetProperty(ref _isNormalizationWorkspace, value))
            {
                OnPropertyChanged(nameof(IsDetectionWorkspace));
                OnPropertyChanged(nameof(SelectionContentVisibility));
            }
        }
    }

    public string NormalizationSummaryText
    {
        get
        {
            var actionableItems = NormalizationItems.Where(item => !item.IsAlreadyNormalized).ToArray();
            return actionableItems.Length == 0
                ? "没有需要规整的 Unreal 素材"
                : $"共 {actionableItems.Length} 项：已处理 {actionableItems.Count(item => item.IsResolved)}，待处理 {actionableItems.Count(item => !item.IsResolved)}";
        }
    }

    // ── 工作区可见性 ──────────────────────────────────────────────────────

    public Visibility NormalizationDetailsVisibility =>
        WorkflowStep == 2 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// 取这一步的规整决策：**先读自己的缓存文件**，没有才回退到整体会话缓存。
    ///
    /// 回退只为了兼容"搬之前留下的旧决策"；第 2 步做完决策就会写进自己的文件。
    /// </summary>
    private IReadOnlyDictionary<string, string> LoadNormalizationDecisions(
        CharacterCard? character,
        Func<IReadOnlyDictionary<string, string>> fallback)
    {
        if (character is not null &&
            Step2NormalizationCache.TryLoad(character, character.Code) is { } document &&
            document.Decisions.Count > 0)
        {
            return document.Decisions;
        }

        return fallback();
    }

    /// <summary>把这一步的规整决策写进第 2 步自己的缓存文件。</summary>
    internal bool SaveNormalizationDecisionsCache(
        CharacterCard? character,
        IReadOnlyDictionary<string, string> decisions) =>
        character is not null && Step2NormalizationCache.Save(character, decisions);

    // ── 构建 / 应用 ───────────────────────────────────────────────────────

    /// <summary>
    /// 按当前 Unreal 侧候选重建规整项，并把**缓存里的旧决策**回填上去
    /// （稳定 ID 对不上时再按"资产名"兜一次，兼容早期用路径当键的缓存）。
    /// </summary>
    private static IReadOnlyList<UnrealAssetNormalizationItem> BuildNormalizationItems(
        CharacterCard character,
        UnrealProjectSyncCharacterCandidate candidate,
        IReadOnlyDictionary<string, string> cachedDecisions)
    {
        var rebuiltItems = new UnrealAssetNormalizationService().Build(character, candidate).ToArray();
        foreach (var item in rebuiltItems)
        {
            var decisionFound = cachedDecisions.TryGetValue(item.StableId, out var decision);
            if (!decisionFound)
            {
                var assetName = item.UnrealObjectPath.Split('/').LastOrDefault()?.Split('.', 2)[0];
                if (!string.IsNullOrWhiteSpace(assetName))
                {
                    var legacyDecision = cachedDecisions.FirstOrDefault(pair =>
                        pair.Key.EndsWith($"/{assetName}.{assetName}", StringComparison.OrdinalIgnoreCase));
                    decisionFound = !string.IsNullOrWhiteSpace(legacyDecision.Key);
                    decision = legacyDecision.Value;
                }
            }

            if (decisionFound && !string.IsNullOrWhiteSpace(decision))
            {
                if (string.Equals(decision, "__not_required__", StringComparison.Ordinal))
                {
                    item.MarkNotRequired();
                }
                else
                {
                    var selected = item.Candidates.FirstOrDefault(candidateItem =>
                        string.Equals(candidateItem.StableId, decision, StringComparison.OrdinalIgnoreCase));
                    var identityId = decision.StartsWith("material:", StringComparison.OrdinalIgnoreCase)
                        ? decision["material:".Length..]
                        : decision.StartsWith("voice:", StringComparison.OrdinalIgnoreCase)
                            ? decision["voice:".Length..]
                            : decision;
                    if (selected is null &&
                        new UnrealBridgeToolboxIdentityService().TryResolveAssignedPath(
                            character,
                            item.Module,
                            identityId,
                            out var assignedPath))
                    {
                        selected = item.Candidates.FirstOrDefault(candidateItem =>
                            string.Equals(
                                Path.GetFullPath(candidateItem.AssetPath),
                                Path.GetFullPath(assignedPath),
                                StringComparison.OrdinalIgnoreCase));
                    }

                    if (selected is not null)
                    {
                        item.SelectRedirect(selected);
                    }
                }
            }
        }

        return rebuiltItems;
    }

    private void ApplyNormalizationItems(
        IReadOnlyList<UnrealAssetNormalizationItem> rebuiltItems,
        bool activateWorkspace)
    {
        NormalizationItems.Clear();
        foreach (var item in rebuiltItems)
        {
            NormalizationItems.Add(item);
        }

        RefreshVisibleNormalizationItems();
        SetNormalizationStepLoaded(true);

        if (activateWorkspace)
        {
            IsNormalizationWorkspace = true;
            WorkflowStep = 2;
        }

        OnPropertyChanged(nameof(NormalizationSummaryText));
        OnPropertyChanged(nameof(CanAdvanceWorkflow));
    }

    // ── 界面上的三种决策动作 ──────────────────────────────────────────────

    public void SelectNormalizationRedirect(
        UnrealAssetNormalizationItem item,
        UnrealAssetNormalizationCandidate candidate)
    {
        item.SelectRedirect(candidate);
        NormalizationResolutionChanged();
    }

    public void MarkNormalizationNotRequired(UnrealAssetNormalizationItem item)
    {
        item.MarkNotRequired();
        NormalizationResolutionChanged();
    }

    public void ClearNormalizationRedirect(UnrealAssetNormalizationItem item)
    {
        item.ClearRedirect();
        NormalizationResolutionChanged();
    }

    public void BeginNormalizationStepLoad() => SetNormalizationStepLoaded(false);

    private void SetNormalizationStepLoaded(bool value) => IsNormalizationStepLoaded = value;

    private void RefreshVisibleNormalizationItems()
    {
        VisibleNormalizationItems = NormalizationItems.Where(item =>
                !item.IsAlreadyNormalized &&
                (!HideResolvedNormalizationItems || !item.IsResolved))
            .ToArray();
        OnPropertyChanged(nameof(NormalizationSummaryText));
    }

    public void CloseNormalizationWorkspace()
    {
        IsNormalizationWorkspace = false;
        if (WorkflowStep == 2)
        {
            WorkflowStep = 1;
        }
    }
}
