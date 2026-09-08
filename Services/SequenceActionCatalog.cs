using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.RegularExpressions;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 一个序列动作的规范定义。识别侧用 <c>Aliases</c> 兼容历史命名，
/// 写入侧只使用 <c>Code</c> 派生出的规范名称。
/// </summary>
/// <param name="BlueprintSequenceArrayProperty">
/// 角色蓝图上的 <c>TArray&lt;UPaperZDAnimSequence*&gt;</c> 属性名，按形态下标存放
/// （<c>CharShape</c> 从 0 开始，即形态 N 写入下标 N-1）。为空表示该动作不通过
/// 角色蓝图引用，只靠 AnimMaps/AnimSource 绑定。
/// 注意：导出脚本里 Sk1/Sk2/KO/Sub 记录的 SkillSlot1..4 是 <c>FSkillData2D</c>
/// 技能数值结构，不含序列引用，不能当作序列绑定属性写入。
/// </param>
internal sealed record SequenceActionDefinition(
    string Code,
    string DisplayName,
    string BlueprintSequenceArrayProperty,
    string AnimMapsEntryName,
    SequenceActionCategory Category,
    IReadOnlyList<string> Aliases);

internal enum SequenceActionCategory
{
    Base,
    Skill
}

/// <summary>
/// 序列动作代号的唯一事实来源。C# 侧的发布、差异归一化和选择树分组都必须从这里取值，
/// Python 侧通过同步计划接收解析结果，不再各自维护一份代号表。
///
/// 这里的代号、蓝图属性和别名与 <c>Tools/Unreal/export_zd_assets.py</c> 的
/// <c>SEQUENCE_ACTION_SPECS</c> 保持一致；回归测试会校验两边不会漂移。
/// </summary>
internal static class SequenceActionCatalog
{
    /// <summary>形态大于 1 时追加的后缀，与导出脚本的 <c>_parse_sequence_shape_suffix</c> 对应。</summary>
    public const string FormSuffixSeparator = "_Shape";

    public const string FramePrefix = "_Frame";
    public const string SpriteSuffix = "_Sprite";
    public const string FlipbookSuffix = "_Flipbook";

    private static readonly SequenceActionDefinition[] AllDefinitions =
    [
        new("Click", "点击", "ClickSeq", string.Empty, SequenceActionCategory.Base, ["click"]),
        new("Death", "死亡", "DeathAnim", string.Empty, SequenceActionCategory.Base, ["death", "dead"]),
        new("DefAtk", "守备反击", "DefAttackSeq", string.Empty, SequenceActionCategory.Base, ["defatk", "defattack"]),
        new("Defeat", "失败", "DefeatedAnim", string.Empty, SequenceActionCategory.Base, ["defeat", "defeated"]),
        new("Defence", "守备防御", "DefSeq", string.Empty, SequenceActionCategory.Base, ["defence", "defense"]),
        new("Dodge", "守备闪避", "DodgeSeq", string.Empty, SequenceActionCategory.Base, ["dodge"]),
        new("FlyDown", "坠落", string.Empty, "FlyDown", SequenceActionCategory.Base, ["flydown"]),
        new("Flying", "飞行", string.Empty, "Flying", SequenceActionCategory.Base, ["flying", "fly"]),
        new("FlyStart", "击飞", string.Empty, "FlyStart", SequenceActionCategory.Base, ["flystart"]),
        new("Idle", "站街", string.Empty, "Idle", SequenceActionCategory.Base, ["idle"]),
        new("KO", "终结技", string.Empty, string.Empty, SequenceActionCategory.Skill, ["ko"]),
        new("Land", "落地", string.Empty, "Land", SequenceActionCategory.Base, ["land"]),
        new("Move", "移动", string.Empty, "Move", SequenceActionCategory.Base, ["move"]),
        new("OnDamage", "受击", "OnDamageSeq", string.Empty, SequenceActionCategory.Base, ["ondm", "ondamage"]),
        new("Sk1", "一技能", string.Empty, string.Empty, SequenceActionCategory.Skill, ["sk1", "skill1"]),
        new("Sk2", "二技能", string.Empty, string.Empty, SequenceActionCategory.Skill, ["sk2", "skill2"]),
        new("StandUP", "站起", string.Empty, "StandUP", SequenceActionCategory.Base, ["standup", "stand"]),
        new("Sub", "护援技", string.Empty, string.Empty, SequenceActionCategory.Skill, ["sub"]),
        new("Victory", "胜利", "VictorAnim", string.Empty, SequenceActionCategory.Base, ["victory", "victor"])
    ];

    private static readonly Dictionary<string, SequenceActionDefinition> DefinitionByAlias =
        AllDefinitions
            .SelectMany(definition => definition.Aliases
                .Append(NormalizeToken(definition.Code))
                .Select(alias => (alias, definition)))
            .DistinctBy(pair => pair.alias, StringComparer.Ordinal)
            .ToDictionary(pair => pair.alias, pair => pair.definition, StringComparer.Ordinal);

    private static readonly Regex ShapeSuffixPattern =
        new(@"[_-]?shape\s*0*(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TrailingFormSuffixPattern =
        new(@"^(?<base>.*?)(?<form>[2-9]\d*)$", RegexOptions.Compiled);

    public static IReadOnlyList<SequenceActionDefinition> Definitions => AllDefinitions;

    /// <summary>去掉大小写和分隔符差异，得到用于别名比较的 token。</summary>
    public static string NormalizeToken(string? value) =>
        new((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>
    /// 解析任意来源的动作写法：工具箱代号（<c>Idle</c>、<c>Idle02</c>）、
    /// Unreal 资产名或素材文件夹名（<c>Ondm</c>、<c>Defense</c>、<c>SK2</c>、<c>Click2</c>、<c>Sk1_Shape2</c>）。
    /// </summary>
    public static bool TryResolve(
        string? rawCode,
        [NotNullWhen(true)] out SequenceActionDefinition? definition,
        out int formIndex)
    {
        definition = null;
        formIndex = 1;
        var text = (rawCode ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return false;
        }

        // 1. 整体命中别名。必须放在拆分形态后缀之前，否则 Sk2 会被拆成 Sk + 形态 2。
        if (DefinitionByAlias.TryGetValue(NormalizeToken(text), out definition))
        {
            return true;
        }

        // 2. 显式形态后缀：Sk1_Shape2 / SK1Shape02。
        var shapeMatch = ShapeSuffixPattern.Match(text);
        if (shapeMatch.Success &&
            DefinitionByAlias.TryGetValue(NormalizeToken(text[..shapeMatch.Index]), out definition))
        {
            formIndex = Math.Max(1, int.Parse(shapeMatch.Groups[1].Value));
            return true;
        }

        // 3. 工具箱两位形态后缀：Idle02 / Sk102。基础代号本身可能以数字结尾（Sk1），
        // 所以这里按固定两位拆分，不能要求基础部分以非数字结尾。
        if (text.Length > 2 &&
            char.IsAsciiDigit(text[^1]) &&
            char.IsAsciiDigit(text[^2]) &&
            int.Parse(text[^2..]) >= 2 &&
            DefinitionByAlias.TryGetValue(NormalizeToken(text[..^2]), out definition))
        {
            formIndex = int.Parse(text[^2..]);
            return true;
        }

        // 4. Unreal 历史尾随形态：Click2 / Defeated2。
        var trailingMatch = TrailingFormSuffixPattern.Match(text);
        if (trailingMatch.Success &&
            DefinitionByAlias.TryGetValue(NormalizeToken(trailingMatch.Groups["base"].Value), out definition))
        {
            formIndex = Math.Max(1, int.Parse(trailingMatch.Groups["form"].Value));
            return true;
        }

        definition = null;
        formIndex = 1;
        return false;
    }

    public static SequenceActionDefinition Resolve(string? rawCode, out int formIndex) =>
        TryResolve(rawCode, out var definition, out formIndex)
            ? definition
            : throw new InvalidOperationException($"不支持的序列动作：{rawCode}");

    /// <summary>把任意写法归一化为可比较的分组键，例如 <c>Ondm</c> 和 <c>OnDamage</c> 都得到 <c>ondamage</c>。</summary>
    public static string NormalizeActionKey(string? rawCode) =>
        TryResolve(rawCode, out var definition, out var formIndex)
            ? formIndex > 1
                ? $"{NormalizeToken(definition.Code)}-shape{formIndex}"
                : NormalizeToken(definition.Code)
            : NormalizeToken(rawCode);

    /// <summary>规范变体代号：形态 1 为 <c>Idle</c>，形态 2 为 <c>Idle_Shape2</c>。</summary>
    public static string GetVariantCode(SequenceActionDefinition definition, int formIndex)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return formIndex > 1 ? $"{definition.Code}{FormSuffixSeparator}{formIndex}" : definition.Code;
    }

    /// <summary><c>/Game/GameActor2D/&lt;角色&gt;/AnimSequences/</c> 下的 PaperZD 序列资产名。</summary>
    public static string GetAnimSequenceName(SequenceActionDefinition definition, int formIndex) =>
        GetVariantCode(definition, formIndex);

    /// <summary><c>/Game/GameActor2D/&lt;角色&gt;/Material/</c> 下的动作素材文件夹名。</summary>
    public static string GetMaterialFolderName(SequenceActionDefinition definition, int formIndex) =>
        GetVariantCode(definition, formIndex);

    /// <summary>
    /// 帧序号的规范写法：虚幻侧从 0 开始，位宽跟随总帧数——
    /// 个位数是 <c>0,1</c>，十位数是 <c>00,01</c>，百位数是 <c>000,001</c>。
    /// </summary>
    public static string FormatFrameOrdinal(int frameOrdinal, int totalFrameCount)
    {
        if (frameOrdinal < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameOrdinal));
        }

        if (totalFrameCount < 1 || frameOrdinal >= totalFrameCount)
        {
            throw new ArgumentOutOfRangeException(nameof(totalFrameCount));
        }

        return frameOrdinal.ToString().PadLeft(MaterialSequenceNaming.GetWidth(totalFrameCount), '0');
    }

    /// <param name="frameOrdinal">帧在整条序列中的位置，从 0 开始（空白帧同样占一个位置）。</param>
    public static string GetFrameTextureName(
        SequenceActionDefinition definition,
        int formIndex,
        int frameOrdinal,
        int totalFrameCount) =>
        $"{GetVariantCode(definition, formIndex)}{FramePrefix}{FormatFrameOrdinal(frameOrdinal, totalFrameCount)}";

    public static string GetFrameSpriteName(
        SequenceActionDefinition definition,
        int formIndex,
        int frameOrdinal,
        int totalFrameCount) =>
        GetFrameTextureName(definition, formIndex, frameOrdinal, totalFrameCount) + SpriteSuffix;

    public static string GetFlipbookName(SequenceActionDefinition definition, int formIndex) =>
        GetVariantCode(definition, formIndex) + FlipbookSuffix;

    /// <summary>
    /// 该动作在 Unreal 里可能出现过的历史命名 token，用于把清理范围限定在本动作内。
    /// 只返回动作名 token，调用方必须再按资产名分段匹配（而不是整条路径），
    /// 否则角色目录名里恰好含有 token 时会误伤整个角色。
    ///
    /// 会剔除那些是别的动作 token 前缀的别名：<c>Flying</c> 的别名 <c>fly</c>
    /// 只用于识别历史的 <c>Material/Fly</c> 文件夹，一旦拿去删除就会连
    /// <c>FlyStart</c>、<c>FlyDown</c> 的资产一起删掉。
    /// </summary>
    public static IReadOnlyList<string> GetLegacyNameTokens(SequenceActionDefinition definition, int formIndex)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var foreignTokens = AllDefinitions
            .Where(other => !ReferenceEquals(other, definition))
            .SelectMany(other => other.Aliases.Append(NormalizeToken(other.Code)))
            .ToArray();
        return definition.Aliases
            .Append(NormalizeToken(definition.Code))
            .Append(NormalizeToken(GetVariantCode(definition, formIndex)))
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Where(token => !foreignTokens.Any(foreign =>
                foreign.Length > token.Length && foreign.StartsWith(token, StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(token => token, StringComparer.Ordinal)
            .ToArray();
    }
}
