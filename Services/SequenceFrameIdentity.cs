using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CrossingVoidZDTool.Services.Atlas;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 序列动作和序列帧在两侧快照里的共用身份规则。
///
/// 工具箱侧的 <c>SyncId</c> 是随机 GUID，Unreal 侧原来用对象路径算哈希，
/// 两者永远配不上对；而且一旦发布把贴图改名，基于路径的身份还会再变一次。
/// 序列帧真正稳定的身份是「哪个动作的第几帧」——这也正是 Flipbook 关键帧的下标，
/// 所以两侧都按 动作 + 位置 生成稳定 ID。
/// </summary>
internal static class SequenceFrameIdentity
{
    public const string ActionPrefix = "sequence:";
    public const string FramePrefix = "sequence-frame:";
    /// <summary>该动作在 Unreal 里占用、但不属于规范命名的历史资产。</summary>
    public const string OwnedAssetPrefix = "sequence-asset:";

    /// <summary>挂在角色动画源上、却不属于任何规范动作的序列。</summary>
    public const string OrphanSequencePrefix = "sequence-orphan:";

    /// <summary>
    /// 图集贴图这个条目。它在检测阶段还不存在（打包发生在同步时），没有对应的帧节点，
    /// 所以是单独合成的一条；单列一个前缀，是为了让中栏的子项过滤器认得它 ——
    /// 借帧或占用资产的前缀会让它被当成别的东西。
    /// </summary>
    public const string AtlasPrefix = "sequence-atlas:";

    public static bool IsAtlasStableId(string? stableId) =>
        (stableId ?? string.Empty).StartsWith(AtlasPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>由动作稳定 ID 推出它那条图集条目的稳定 ID。</summary>
    public static string BuildAtlasStableId(string? actionStableId)
    {
        var value = actionStableId ?? string.Empty;
        return AtlasPrefix + (value.StartsWith(ActionPrefix, StringComparison.OrdinalIgnoreCase)
            ? value[ActionPrefix.Length..]
            : value);
    }

    /// <summary>孤儿序列没有归属动作，全部挂在这一个分组下。</summary>
    public const string OrphanGroupStableId = "sequence:__orphan__";

    public static string BuildOrphanSequenceStableId(string? objectPath) =>
        OrphanSequencePrefix + NormalizePackagePath(objectPath);

    public static bool IsOrphanSequenceStableId(string? stableId) =>
        (stableId ?? string.Empty).StartsWith(OrphanSequencePrefix, StringComparison.OrdinalIgnoreCase);

    private static readonly Regex StructAddressPattern = new(@"\s*\(0x[0-9A-Fa-f]+\)", RegexOptions.Compiled);
    private static readonly Regex StructAssetNamePattern =
        new("asset_name:\\s*\"([^\"]+)\"", RegexOptions.Compiled);

    /// <summary>
    /// 把资产类名归一成稳定文本。
    ///
    /// Unreal 的 asset_class_path 是 TopLevelAssetPath 结构体，str() 出来是
    /// <c>&lt;Struct 'TopLevelAssetPath' (0x000001C0DE94853C) {... asset_name: "PaperSprite"}&gt;</c>，
    /// 带着对象内存地址，每次导出都不一样。这个值会进 payload 参与内容哈希，
    /// 于是同一个资产每次检测都被判成「有变化」，同步前的最终比对永远通不过，
    /// 表现就是点同步什么也没发生、差异列表还在。
    ///
    /// 导出脚本已经改成取 asset_name，这里是边界兜底：清单由外部进程生成，
    /// 哈希输入里绝不能出现指针，任何来源的脏值都要在进哈希前拦掉。
    /// </summary>
    public static string NormalizeAssetClass(string? assetClass)
    {
        if (string.IsNullOrWhiteSpace(assetClass))
        {
            return string.Empty;
        }

        var text = assetClass.Trim();
        if (!text.Contains("(0x", StringComparison.Ordinal))
        {
            return text;
        }

        var match = StructAssetNamePattern.Match(text);
        return match.Success
            ? match.Groups[1].Value
            : StructAddressPattern.Replace(text, string.Empty).Trim();
    }

    /// <summary>两侧语义载荷用的字段分隔符（Unreal 侧是 \u001f 拼接，工具箱侧是 JSON）。</summary>
    public const char SemanticPayloadSeparator = (char)0x1F;

    /// <summary>
    /// 这一帧是不是空白帧。
    ///
    /// 空白帧在 Unreal 里没有对应资产——它就是 Flipbook 里 sprite 为 null 的关键帧，
    /// 所以既没有对象路径也没有源文件。差异比较和可执行性判定都要认得它，
    /// 因此判定放在共用处：以前只有差异服务有一份私有实现，
    /// 发布策略那边不认，含空白帧的动作被勾选后整批同步会被拦下。
    /// </summary>
    public static bool IsBlankFramePayload(string? payloadJson)
    {
        var payload = payloadJson ?? string.Empty;
        if (payload.TrimStart().StartsWith("{", StringComparison.Ordinal))
        {
            try
            {
                using var document = JsonDocument.Parse(payload);
                return document.RootElement.TryGetProperty("isBlank", out var value) &&
                    string.Equals(value.GetString(), "true", StringComparison.OrdinalIgnoreCase);
            }
            catch (JsonException)
            {
                return false;
            }
        }

        var fields = payload.Split(SemanticPayloadSeparator);
        return fields.Length > 0 &&
            string.Equals(fields[^1], "True", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>动作节点的稳定 ID。别名写法（Ondm/OnDamage、Defense/Defence）会归一到同一个键。</summary>
    public static string BuildActionStableId(string? actionCode) =>
        ActionPrefix + SequenceActionCatalog.NormalizeActionKey(actionCode);

    /// <summary>导出侧给的是基础代号加形态序号，这里合成带形态后缀的规范变体代号再取稳定 ID。</summary>
    public static string BuildActionStableId(string? actionCode, int formIndex) =>
        BuildActionStableId(ResolveVariantCode(actionCode, formIndex));

    /// <summary>把「基础代号 + 形态序号」合成规范变体代号；无法识别时原样返回。</summary>
    public static string ResolveVariantCode(string? actionCode, int formIndex)
    {
        var normalizedForm = Math.Max(1, formIndex);
        if (SequenceActionCatalog.TryResolve(actionCode, out var definition, out var parsedForm))
        {
            return SequenceActionCatalog.GetVariantCode(
                definition,
                normalizedForm > 1 ? normalizedForm : parsedForm);
        }

        var raw = (actionCode ?? string.Empty).Trim();
        return normalizedForm > 1 ? raw + SequenceActionCatalog.FormSuffixSeparator + normalizedForm : raw;
    }

    /// <param name="frameOrdinal">帧在整条序列中的位置，从 0 开始。</param>
    public static string BuildFrameStableId(string? actionCode, int frameOrdinal)
    {
        if (frameOrdinal < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameOrdinal));
        }

        return $"{FramePrefix}{SequenceActionCatalog.NormalizeActionKey(actionCode)}:{frameOrdinal}";
    }

    public static bool IsActionStableId(string? stableId) =>
        (stableId ?? string.Empty).StartsWith(ActionPrefix, StringComparison.OrdinalIgnoreCase);

    public static bool IsFrameStableId(string? stableId) =>
        (stableId ?? string.Empty).StartsWith(FramePrefix, StringComparison.OrdinalIgnoreCase);

    public static bool IsOwnedAssetStableId(string? stableId) =>
        (stableId ?? string.Empty).StartsWith(OwnedAssetPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 占用资产的稳定 ID 必须按完整包路径生成：同一个动作下，
    /// AnimSequences/Sk1 和 Material/Sk1/Sk1 是两个不同的资产，只用资产名会撞 ID。
    /// </summary>
    public static string BuildOwnedAssetStableId(string? actionCode, string? objectPath) =>
        $"{OwnedAssetPrefix}{SequenceActionCatalog.NormalizeActionKey(actionCode)}:{NormalizePackagePath(objectPath)}";

    /// <summary>把对象路径归一为可比较的包路径（去掉 .Object 后缀并转小写）。</summary>
    public static string NormalizePackagePath(string? objectPath)
    {
        var path = (objectPath ?? string.Empty).Trim().Replace('\\', '/');
        var dot = path.IndexOf('.');
        return (dot >= 0 ? path[..dot] : path).ToLowerInvariant();
    }

    /// <summary>
    /// 该动作同步之后应当存在的全部资产包路径（序列、Flipbook、图集贴图，以及每个素材一个的 Sprite）。
    /// 必须按完整路径比较：旧 Flipbook 在 Material 目录下也可能叫 Sk1，
    /// 只比资产名会把它误认成 AnimSequences 里的规范序列而漏掉清理。
    ///
    /// 换成图集之后，逐帧贴图**不在**这份名单里了 —— 这正是关键：工程里那批
    /// 按帧命名的旧贴图和旧精灵因此不再是「规范资产」，会被正常列进删除候选。
    /// 名单留在旧口径的话，旧贴图会被当成「应该存在」而过滤掉，界面上就只剩帧位数可数，
    /// 看着像「删除 23 项」，实际那 23 条跟工程里的文件对不上。
    /// </summary>
    /// <param name="ownSpriteNames">
    /// 这个动作**自己**那几只精灵的名字（借用来的图连精灵一起借，不在这个动作里建）。
    /// 名单里没有精灵 = 整条都在借别人的素材 → 这个动作既没有自己的图集、也没有自己的精灵，
    /// 只剩 Flipbook 和序列 —— 工程里遗留的重复图集/精灵因此会被正常列成待删。
    /// </param>
    public static IReadOnlyCollection<string> BuildCanonicalAssetPackagePaths(
        string characterCode,
        string? actionCode,
        IReadOnlyList<string> ownSpriteNames)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(characterCode) ||
            !SequenceActionCatalog.TryResolve(actionCode, out var definition, out var formIndex))
        {
            return paths;
        }

        var root = $"/Game/GameActor2D/{characterCode}";
        var materialFolder = $"{root}/Material/{SequenceActionCatalog.GetMaterialFolderName(definition, formIndex)}";
        var variantCode = SequenceActionCatalog.GetVariantCode(definition, formIndex);
        paths.Add(NormalizePackagePath($"{root}/AnimSequences/{SequenceActionCatalog.GetAnimSequenceName(definition, formIndex)}"));
        paths.Add(NormalizePackagePath($"{materialFolder}/{SequenceActionCatalog.GetFlipbookName(definition, formIndex)}"));
        if (ownSpriteNames.Count > 0)
        {
            // 有自己素材的动作才有自己的图集；整条借用的动作没有。
            paths.Add(NormalizePackagePath(
                $"{materialFolder}/{AtlasManifestWriter.BuildAtlasName(characterCode, variantCode)}"));
        }

        foreach (var spriteName in ownSpriteNames)
        {
            paths.Add(NormalizePackagePath($"{materialFolder}/{spriteName}"));
        }

        return paths;
    }

    /// <summary>
    /// 动作节点的比较载荷。两侧必须逐字节一致，否则每个动作都会被永久判成冲突，
    /// 第五步也就永远报不出「已无差异」。
    ///
    /// 除了规范代号和帧率，还带上两样：
    /// <list type="bullet">
    /// <item><c>layout</c> —— 图集布局写 <c>atlas:&lt;图集名&gt;</c>，还是逐帧导入时写
    /// 实际用到的贴图名集合。两者不同即判「需要重建」——这正是从逐帧切到图集之后
    /// 最该被发现的一种变化，光比帧内容看不出来。</item>
    /// <item><c>slots</c>/<c>blanks</c> —— 整条序列占多少格、其中几格是空白。
    /// Flipbook 里每帧停留的格数是展开的，所以这一对数两侧都算得出来；
    /// 帧被增删、或者某帧的停留格数被改，都会在这里露出来。</item>
    /// </list>
    /// </summary>
    public static string BuildActionPayload(string? actionCode, int fps, string layout, int slots, int blanks)
    {
        var canonicalCode = SequenceActionCatalog.TryResolve(actionCode, out var definition, out var formIndex)
            ? SequenceActionCatalog.GetVariantCode(definition, formIndex)
            : (actionCode ?? string.Empty).Trim();
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("actionCode", canonicalCode);
            writer.WriteString("fps", Math.Clamp(fps, 1, 60).ToString(CultureInfo.InvariantCulture));
            writer.WriteString("layout", layout ?? string.Empty);
            writer.WriteString("slots", slots.ToString(CultureInfo.InvariantCulture));
            writer.WriteString("blanks", blanks.ToString(CultureInfo.InvariantCulture));
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// 不带布局与帧结构的最小载荷。**只给「两侧用同一段代码算同一个字符串」这类断言用** ——
    /// 生产路径一律走上面那个五参版本，否则布局变化会被漏掉。
    /// </summary>
    public static string BuildActionPayload(string? actionCode, int fps) =>
        BuildActionPayload(actionCode, fps, layout: string.Empty, slots: -1, blanks: -1);

    /// <summary>
    /// 图集布局的载荷文字。两侧各算一次，算出来一样才算布局对齐。
    /// </summary>
    public static string BuildAtlasLayout(string atlasName) => "atlas:" + atlasName;

    /// <summary>
    /// 读回动作载荷里的布局与帧结构。
    ///
    /// 中栏的「Unreal 现有 N 个帧位」必须从这里取，不能去数差异条目 ——
    /// 从缓存恢复时，两侧已经一致的帧（Unchanged）按设计会被剔掉，
    /// 列表里只剩待办项，数出来永远是 0。动作节点自己不会被剔，载荷里带着两侧的帧位数。
    /// </summary>
    public static bool TryReadActionPayload(
        string? payloadJson,
        out string layout,
        out int slots,
        out int blanks)
    {
        layout = string.Empty;
        slots = -1;
        blanks = -1;
        if (string.IsNullOrWhiteSpace(payloadJson) || !payloadJson.TrimStart().StartsWith('{'))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            if (root.TryGetProperty("layout", out var layoutValue) && layoutValue.ValueKind == JsonValueKind.String)
            {
                layout = layoutValue.GetString() ?? string.Empty;
            }

            if (root.TryGetProperty("slots", out var slotsValue) &&
                int.TryParse(slotsValue.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSlots))
            {
                slots = parsedSlots;
            }

            if (root.TryGetProperty("blanks", out var blanksValue) &&
                int.TryParse(blanksValue.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedBlanks))
            {
                blanks = parsedBlanks;
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>还没换成图集时的布局文字：实际用到的贴图名，按名字排序后拼起来。</summary>
    public static string BuildFrameLayout(IEnumerable<string> textureNames) =>
        "frames:" + string.Join(
            ",",
            textureNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// 该帧同步到 Unreal 之后应有的贴图对象路径。差异比较用它判断某一帧是否已经落在规范命名上：
    /// 落在规范位置说明只是内容比较，不在规范位置说明这次同步会把它改名过去。
    /// </summary>
    public static string BuildCanonicalTextureObjectPath(
        string characterCode,
        string? actionCode,
        int frameOrdinal,
        int totalFrameCount)
    {
        if (string.IsNullOrWhiteSpace(characterCode) ||
            !SequenceActionCatalog.TryResolve(actionCode, out var definition, out var formIndex))
        {
            return string.Empty;
        }

        var folder = SequenceActionCatalog.GetMaterialFolderName(definition, formIndex);
        var assetName = SequenceActionCatalog.GetFrameTextureName(definition, formIndex, frameOrdinal, totalFrameCount);
        return $"/Game/GameActor2D/{characterCode}/Material/{folder}/{assetName}.{assetName}";
    }
}
