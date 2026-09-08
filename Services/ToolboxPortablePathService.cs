using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 角色目录里的 JSON 落盘时，把指向角色自己文件夹的绝对路径换成可移植写法。
///
/// 之前 ZDToolboxData.json 和分步缓存里存的是 <c>G:\WinUI\CrossingVoidZDProject\Completed\Misaka\...</c>
/// 这种整机绝对路径，换台机器、挪一次工作区、或者角色在 Draft/Completed 之间搬家，
/// 路径就全指丢了——SAO_kirito 的 BUFF 图标至今还写着 <c>D:\NewData\...</c> 就是这么坏的。
///
/// 规则只有一条：<b>落在角色文件夹里面的绝对路径改写成 <c>$char/</c> 开头的相对路径，
/// 指向文件夹外面的（虚幻引擎、虚幻工程、导出中间目录）原样保留。</b>
/// 加前缀是为了读回来时不用猜哪个字符串是路径——没前缀就不是本工具箱管的路径。
/// 相对基准取角色文件夹本身而不是工作区根，这样角色整个目录搬到哪儿都不用改写。
/// </summary>
internal static class ToolboxPortablePathService
{
    /// <summary>可移植路径的前缀，后面接相对角色文件夹的路径。</summary>
    public const string CharacterPrefix = "$char/";

    /// <summary>图标类字段，读的时候会额外尝试修复历史遗留的死路径。</summary>
    private static readonly string[] IconFieldNames = ["IconPath", "ItemIconPath"];

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true
    };

    public static string ToPortable(string? value, string? characterFolder)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(characterFolder))
        {
            return value ?? string.Empty;
        }

        if (value.StartsWith(CharacterPrefix, StringComparison.Ordinal) || !Path.IsPathFullyQualified(value))
        {
            return value;
        }

        var root = TrimSeparator(Path.GetFullPath(characterFolder));
        string full;
        try
        {
            full = Path.GetFullPath(value);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return value;
        }

        // 只收编角色文件夹里面的路径；外面的（引擎、虚幻工程）必须保持绝对。
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var relative = full[(root.Length + 1)..].Replace(Path.DirectorySeparatorChar, '/');
        return CharacterPrefix + relative;
    }

    public static string ToAbsolute(string? value, string? characterFolder)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith(CharacterPrefix, StringComparison.Ordinal))
        {
            return value ?? string.Empty;
        }

        var relative = value[CharacterPrefix.Length..].Replace('/', Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(characterFolder))
        {
            return relative;
        }

        return Path.GetFullPath(Path.Combine(characterFolder, relative));
    }

    /// <summary>把整份 JSON 里的 <c>$char/</c> 路径还原成绝对路径，顺带修历史死路径。</summary>
    public static string ToAbsoluteJson(string json, string? characterFolder)
    {
        return Rewrite(json, node => WalkStrings(node, characterFolder, isWrite: false));
    }

    /// <summary>把整份 JSON 里指向角色文件夹的绝对路径换成 <c>$char/</c> 写法。</summary>
    public static string ToPortableJson(string json, string? characterFolder)
    {
        return Rewrite(json, node => WalkStrings(node, characterFolder, isWrite: true));
    }

    /// <summary>从 <c>&lt;角色&gt;/tool/UnrealSync/xxx.json</c> 这样的路径倒推角色文件夹。</summary>
    public static string? ResolveCharacterFolderFromToolPath(string? filePath, int levelsUp)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        var folder = Path.GetDirectoryName(Path.GetFullPath(filePath));
        for (var i = 0; i < levelsUp && folder is not null; i++)
        {
            folder = Path.GetDirectoryName(folder);
        }

        return string.IsNullOrWhiteSpace(folder) ? null : folder;
    }

    private static string Rewrite(string json, Func<JsonNode, bool> walk)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return json;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            // 解析不了就原样交回去，让原来的读取逻辑去报那个更准确的错。
            return json;
        }

        if (root is null || !walk(root))
        {
            return json;
        }

        return root.ToJsonString(WriteOptions);
    }

    private static bool WalkStrings(JsonNode node, string? characterFolder, bool isWrite, string? fieldName = null)
    {
        var changed = false;
        switch (node)
        {
            case JsonObject obj:
                foreach (var pair in obj.ToArray())
                {
                    changed |= RewriteChild(obj, pair.Key, pair.Value, characterFolder, isWrite, pair.Key);
                }

                break;

            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    changed |= RewriteChild(array, i, array[i], characterFolder, isWrite, fieldName);
                }

                break;
        }

        return changed;
    }

    private static bool RewriteChild(
        JsonNode parent,
        object key,
        JsonNode? child,
        string? characterFolder,
        bool isWrite,
        string? fieldName)
    {
        if (child is JsonObject or JsonArray)
        {
            return WalkStrings(child, characterFolder, isWrite, fieldName);
        }

        if (child is not JsonValue value || !value.TryGetValue<string>(out var text) || string.IsNullOrEmpty(text))
        {
            return false;
        }

        var next = isWrite
            ? ToPortable(text, characterFolder)
            : RepairIcon(ToAbsolute(text, characterFolder), characterFolder, fieldName);
        if (string.Equals(text, next, StringComparison.Ordinal))
        {
            return false;
        }

        switch (parent)
        {
            case JsonObject obj:
                obj[(string)key] = next;
                break;
            case JsonArray array:
                array[(int)key] = next;
                break;
        }

        return true;
    }

    /// <summary>
    /// 图标字段的历史包袱：老数据里存的是别的机器上的绝对路径（比如 <c>D:\NewData\...\SAO_kirito\BUFF\x.png</c>）。
    /// 文件确实不在了、但按角色文件夹拼回去能找到同一个文件时，就地修好。
    /// 只对图标字段做，因为别的 AssetPath 可能本来就该指向虚幻那边。
    /// </summary>
    private static string RepairIcon(string value, string? characterFolder, string? fieldName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || string.IsNullOrWhiteSpace(characterFolder)
            || fieldName is null
            || !IconFieldNames.Contains(fieldName, StringComparer.Ordinal)
            || !Path.IsPathFullyQualified(value)
            || File.Exists(value))
        {
            return value;
        }

        var characterName = Path.GetFileName(TrimSeparator(Path.GetFullPath(characterFolder)));
        if (string.IsNullOrWhiteSpace(characterName))
        {
            return value;
        }

        // 先按「角色名之后的那一段」拼：D:\NewData\...\SAO_kirito\BUFF\x.png -> BUFF\x.png
        var segments = value.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        var at = Array.FindLastIndex(segments, item => string.Equals(item, characterName, StringComparison.OrdinalIgnoreCase));
        if (at >= 0 && at < segments.Length - 1)
        {
            var rebuilt = Path.Combine([characterFolder, .. segments[(at + 1)..]]);
            if (File.Exists(rebuilt))
            {
                return rebuilt;
            }
        }

        // 再退一步：整个角色目录里找同名文件，唯一命中才认。
        try
        {
            var name = segments.Length > 0 ? segments[^1] : string.Empty;
            if (!string.IsNullOrWhiteSpace(name) && Directory.Exists(characterFolder))
            {
                var matches = Directory
                    .EnumerateFiles(characterFolder, name, SearchOption.AllDirectories)
                    .Take(2)
                    .ToArray();
                if (matches.Length == 1)
                {
                    return matches[0];
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 找不动就算了，保持原值。
        }

        return value;
    }

    private static string TrimSeparator(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
