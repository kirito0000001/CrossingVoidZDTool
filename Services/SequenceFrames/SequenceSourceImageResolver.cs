using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CrossingVoidZDTool.Services.Atlas;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 一条序列的每一格素材「同步之后长什么样」：用哪张图集、用哪只精灵、要不要新建。
///
/// 规则（2026-09-18 定稿）：**借来的图连精灵也一起借。**
/// 那张图已经在来源动作的图集里切好了一只精灵，借用方再建一只就是纯复制品
/// （Misaka 实测 134 只精灵里有 23 只是复制品）。所以：
/// <list type="bullet">
/// <item>自己的图 → 自己的图集 + 自己的一只精灵（在借用方自己的材质目录里）。</item>
/// <item>借来的图 → 来源动作的图集 + **来源动作那只精灵**，借用方一只都不建。</item>
/// </list>
/// 整条都借用别人的动作因此只剩一个 Flipbook。
/// </summary>
/// <param name="Entry">素材归属（自己的还是谁的、落在哪张图集）。</param>
/// <param name="SpriteAssetName">这一格用的精灵名（自己的名字，或来源动作那只的名字）。</param>
/// <param name="CreateSprite">true = 在借用方自己的材质目录里建/更新这只精灵。</param>
/// <param name="AtlasMaterialFolder">图集所在的材质目录（借用时是来源动作的目录）。</param>
/// <param name="SpriteMaterialFolder">精灵所在的材质目录（自己的目录，或来源动作的目录）。</param>
internal sealed record SequenceSourceImageResolution(
    SequenceActionFolderLayout.SequenceSourceImageEntry Entry,
    string SpriteAssetName,
    bool CreateSprite,
    string AtlasMaterialFolder,
    string SpriteMaterialFolder)
{
    public string FilePath => Entry.FilePath;

    public string AtlasName => Entry.AtlasName;

    public string OwnerActionCode => Entry.OwnerActionCode;

    public bool IsOwnSourceImage => Entry.IsOwn;
}

internal static class SequenceSourceImageResolver
{
    /// <summary>
    /// 把一条序列的素材解析成「每一格用哪只精灵」。
    ///
    /// <paramref name="ownerSectionLookup"/> 用来取来源动作的帧列表 —— 来源那只精灵的名字按
    /// **来源动作自己那份素材列表**算（和它自己同步时用的是同一份），所以必须拿得到它的帧。
    /// 取不到就退化成「借用方自己建一只」，宁可多建一只也不要把引用指到不存在的资产上。
    /// </summary>
    public static IReadOnlyList<SequenceSourceImageResolution> Resolve(
        CharacterCard character,
        SequenceFrameAction action,
        IReadOnlyList<SequenceFrameItem> frames,
        Func<string, SequenceFrameSection?> ownerSectionLookup)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(ownerSectionLookup);

        var plan = SequenceActionFolderLayout.ResolveSourceImagePlan(character, action, frames);
        var result = new List<SequenceSourceImageResolution>(plan.Count);
        var materialRoot = $"/Game/GameActor2D/{character.Code}/Material";

        var ownImages = plan.Where(entry => entry.IsOwn).ToArray();
        var ownSpriteNameByFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // 认不出来源动作那只精灵时的兜底名字：按「本动作整份素材列表」序号起名，
        // 和共享之前的老口径一致 —— 宁可自己建一只，也不要把引用指到不存在的资产上。
        var fallbackNameByFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (SequenceActionCatalog.TryResolve(action.Code, out var ownDefinition, out var ownForm))
        {
            for (var ordinal = 0; ordinal < ownImages.Length; ordinal++)
            {
                ownSpriteNameByFile[ownImages[ordinal].FilePath] = SequenceActionCatalog.GetFrameSpriteName(
                    ownDefinition, ownForm, ordinal, ownImages.Length);
            }

            for (var ordinal = 0; ordinal < plan.Count; ordinal++)
            {
                fallbackNameByFile[plan[ordinal].FilePath] = SequenceActionCatalog.GetFrameSpriteName(
                    ownDefinition, ownForm, ordinal, plan.Count);
            }
        }

        var ownerSpriteNameByFile = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var ownerFolderByCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var code in plan.Where(entry => !entry.IsOwn)
                     .Select(entry => entry.OwnerActionCode)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ownerSpriteNameByFile[code] = BuildOwnerSpriteNames(character, code, ownerSectionLookup);
            ownerFolderByCode[code] = ResolveOwnerMaterialFolder(character, code, materialRoot);
        }

        var ownMaterialFolder = materialRoot + "/" + (SequenceActionCatalog.TryResolve(
            action.Code, out var definition, out var formIndex)
                ? SequenceActionCatalog.GetMaterialFolderName(definition, formIndex)
                : action.Code);

        foreach (var entry in plan)
        {
            if (entry.IsOwn)
            {
                ownSpriteNameByFile.TryGetValue(entry.FilePath, out var ownName);
                result.Add(new SequenceSourceImageResolution(
                    entry, ownName ?? string.Empty, CreateSprite: true, ownMaterialFolder, ownMaterialFolder));
                continue;
            }

            var names = ownerSpriteNameByFile[entry.OwnerActionCode];
            var folder = ownerFolderByCode[entry.OwnerActionCode];
            if (names.TryGetValue(entry.FilePath, out var ownerName) && !string.IsNullOrWhiteSpace(ownerName))
            {
                result.Add(new SequenceSourceImageResolution(entry, ownerName, CreateSprite: false, folder, folder));
            }
            else
            {
                // 认不出来源那只精灵的名字：退回自己建一只，引用一定指得到。
                // 图集仍然用来源动作那张（图在那儿），精灵建在自己的目录里。
                fallbackNameByFile.TryGetValue(entry.FilePath, out var fallbackName);
                result.Add(new SequenceSourceImageResolution(
                    entry, fallbackName ?? string.Empty, CreateSprite: true, folder, ownMaterialFolder));
            }
        }

        return result;
    }

    /// <summary>来源动作自己那份「素材 → 精灵名」表。和它自己同步时算的是同一份。</summary>
    private static Dictionary<string, string> BuildOwnerSpriteNames(
        CharacterCard character,
        string ownerActionCode,
        Func<string, SequenceFrameSection?> ownerSectionLookup)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!SequenceActionCatalog.TryResolve(ownerActionCode, out var definition, out var formIndex))
        {
            return result;
        }

        var section = ownerSectionLookup(ownerActionCode);
        if (section is null)
        {
            return result;
        }

        var ownImages = SequenceActionFolderLayout
            .ResolveSourceImagePlan(character, section.Action, section.Frames)
            .Where(entry => entry.IsOwn)
            .ToArray();
        for (var ordinal = 0; ordinal < ownImages.Length; ordinal++)
        {
            result[ownImages[ordinal].FilePath] = SequenceActionCatalog.GetFrameSpriteName(
                definition, formIndex, ordinal, ownImages.Length);
        }

        return result;
    }

    private static string ResolveOwnerMaterialFolder(
        CharacterCard character,
        string ownerActionCode,
        string materialRoot) =>
        SequenceActionCatalog.TryResolve(ownerActionCode, out var definition, out var formIndex)
            ? $"{materialRoot}/{SequenceActionCatalog.GetMaterialFolderName(definition, formIndex)}"
            : $"{materialRoot}/{ownerActionCode}";
}
