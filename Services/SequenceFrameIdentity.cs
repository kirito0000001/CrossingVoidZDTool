using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

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
    /// 该动作同步之后应当存在的全部资产包路径（序列、Flipbook、逐帧贴图与 Sprite）。
    /// 必须按完整路径比较：旧 Flipbook 在 Material 目录下也可能叫 Sk1，
    /// 只比资产名会把它误认成 AnimSequences 里的规范序列而漏掉清理。
    /// </summary>
    public static IReadOnlyCollection<string> BuildCanonicalAssetPackagePaths(
        string characterCode,
        string? actionCode,
        int totalFrameCount)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(characterCode) ||
            !SequenceActionCatalog.TryResolve(actionCode, out var definition, out var formIndex))
        {
            return paths;
        }

        var root = $"/Game/GameActor2D/{characterCode}";
        var materialFolder = $"{root}/Material/{SequenceActionCatalog.GetMaterialFolderName(definition, formIndex)}";
        paths.Add(NormalizePackagePath($"{root}/AnimSequences/{SequenceActionCatalog.GetAnimSequenceName(definition, formIndex)}"));
        paths.Add(NormalizePackagePath($"{materialFolder}/{SequenceActionCatalog.GetFlipbookName(definition, formIndex)}"));
        for (var ordinal = 0; ordinal < totalFrameCount; ordinal++)
        {
            paths.Add(NormalizePackagePath(
                $"{materialFolder}/{SequenceActionCatalog.GetFrameTextureName(definition, formIndex, ordinal, totalFrameCount)}"));
            paths.Add(NormalizePackagePath(
                $"{materialFolder}/{SequenceActionCatalog.GetFrameSpriteName(definition, formIndex, ordinal, totalFrameCount)}"));
        }

        return paths;
    }

    /// <summary>
    /// 动作节点的比较载荷。两侧必须逐字节一致，否则每个动作都会被永久判成冲突，
    /// 第五步也就永远报不出「已无差异」。这里只放真正可同步的动作级属性：规范代号和帧率。
    /// </summary>
    public static string BuildActionPayload(string? actionCode, int fps)
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
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

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
