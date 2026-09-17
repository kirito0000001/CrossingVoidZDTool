using System.Text.Json;
using System.Text.Json.Serialization;
using CrossingVoidZDTool;

namespace CrossingVoidZDTool.Services.Atlas;

/// <summary>
/// 图集工具那条链路的 JSON 上下文，单独一份。
///
/// 为什么要另开一个而不是塞进 <see cref="AppJsonSerializerContext"/>：
/// <c>Tools/Atlas/MANIFEST.md</c> 约定的字段名是 **camelCase**（Python 侧写的），
/// 而工具箱其余协议是 PascalCase。<see cref="JsonSourceGenerationOptionsAttribute"/>
/// 的命名策略是**整个上下文一起生效**的，混在一起必然有一边读不到字段——
/// 而且 source-generated 的上下文读不到字段时是**静默丢弃**，不报错。
/// 蓝图置入（<c>UnrealBlueprintSetupJsonContext</c>）踩过同一个坑，所以照它的样子办。
///
/// 只放和图集工具直接打交道的类型：清单（我们写、它读）和 report（它写、我们读）。
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(AtlasManifest))]
[JsonSerializable(typeof(AtlasManifestFrame))]
[JsonSerializable(typeof(AtlasReport))]
[JsonSerializable(typeof(AtlasReportSize))]
[JsonSerializable(typeof(AtlasSequenceManifest))]
[JsonSerializable(typeof(AtlasSequenceFrame))]
[JsonSerializable(typeof(AtlasRect))]
internal sealed partial class AtlasJsonContext : JsonSerializerContext
{
    private static AtlasJsonContext? s_indented;

    /// <summary>
    /// 写人类会看的文件时用这一份。
    ///
    /// 和 <see cref="AppJsonSerializerContext.Indented"/> 同一个道理：
    /// 从 <c>Default.Options</c> 复制再只改需要的开关，这样将来往特性上加参数时
    /// 这条路径自动跟上，不会出现「上下文有一份干净默认值、大小写不敏感悄悄没了」。
    /// </summary>
    public static AtlasJsonContext Indented =>
        s_indented ??= new AtlasJsonContext(new JsonSerializerOptions(Default.Options)
        {
            WriteIndented = true,
        });
}
