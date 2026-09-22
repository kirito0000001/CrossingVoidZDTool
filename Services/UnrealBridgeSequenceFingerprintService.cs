using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CrossingVoidZDTool.Services.Atlas;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 序列素材的「内容指纹」：上一轮同步成功时每条序列用到的源图长什么样。
///
/// 为什么需要它：现在的检测看的是布局 / 精灵对应 / 帧位结构。把某张图**同名换内容**
/// （覆盖同名 PNG）时两侧都看不出来 —— 工具箱的帧哈希是「源 PNG + JSON 载荷」，
/// Unreal 那边是「图集 PNG + 分隔符载荷」，天生不可比。
/// 所以同步成功后把哈希记下来，下次检测拿当前值和记录值比。
///
/// 比对的落点是**动作载荷里的 content 字段**：
/// <list type="bullet">
/// <item>工具箱侧填「当前」摘要；</item>
/// <item>Unreal 侧用 <see cref="ApplyRecordedContent"/> 填上「上次同步时记下的」摘要。</item>
/// </list>
/// 两者不一致 → 载荷哈希不同 → 这个动作被判成需要重建，走的还是原来那套差异树，
/// 不需要给差异服务加新概念。
/// </summary>
internal static class UnrealBridgeSequenceFingerprintService
{
    private const string FolderName = "UnrealSync";
    private const string FileName = "sequence-content.json";

    /// <summary>动作载荷里承载内容摘要的字段名。</summary>
    public const string ContentFieldName = "content";

    public static string GetPath(CharacterCard character) =>
        Path.Combine(character.ToolFolderPath, FolderName, FileName);

    public static UnrealBridgeSequenceContentFingerprints? Load(CharacterCard character)
    {
        ArgumentNullException.ThrowIfNull(character);
        var path = GetPath(character);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var fingerprints = JsonSerializer.Deserialize(
                File.ReadAllText(path, Encoding.UTF8),
                AppJsonSerializerContext.Default.UnrealBridgeSequenceContentFingerprints);
            if (fingerprints is null ||
                fingerprints.SchemaVersion != UnrealBridgeSequenceContentFingerprints.CurrentSchemaVersion ||
                !string.Equals(fingerprints.CharacterCode, character.Code, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            fingerprints.Actions ??= new Dictionary<string, UnrealBridgeSequenceActionFingerprint>(StringComparer.OrdinalIgnoreCase);
            return fingerprints;
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            // 读坏了当没有：这一轮会按「没有记录」走（不判内容变化），下一轮同步会重新写。
            ToolboxLog.Warn($"序列素材指纹读取失败，已忽略：{path}", error);
            return null;
        }
    }

    public static void Save(
        CharacterCard character,
        IEnumerable<UnrealBridgeSequenceSyncAction> executedActions,
        DateTimeOffset recordedAt)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(executedActions);

        var executed = executedActions.ToArray();
        // 指纹是**按动作**记的，而一个动作现在有两段内容：角色帧 + 它自己的特效层。
        // 所以这里不能照抄计划里的 SourceImages —— 那样记的是角色帧，读的时候却是
        // 「角色帧 + 特效帧」，两侧永远不相等，每个有特效的动作都会长期显示「素材变了」。
        // 记的那一侧必须和读的那一侧调**同一个**函数（ComputeCurrentSourceHashes）。
        var executedCodes = new HashSet<string>(
            executed.Where(action => !action.IsEffectLayer).Select(action => action.ActionCode),
            StringComparer.OrdinalIgnoreCase);
        var executedEffectLayers = new HashSet<string>(
            executed.Where(action => action.IsEffectLayer).Select(action => action.ActionCode),
            StringComparer.OrdinalIgnoreCase);
        var sectionByActionCode = LoadSectionsByActionCode(character);

        var fingerprints = new UnrealBridgeSequenceContentFingerprints
        {
            CharacterCode = character.Code,
            RecordedAt = recordedAt,
        };
        foreach (var code in executedCodes)
        {
            if (!sectionByActionCode.TryGetValue(code, out var section))
            {
                // 认不出这个动作（老计划里的历史代号）：宁可这一条不记。
                // 不记的后果是这一条不做内容比较（下一轮按没有记录走），
                // 而记错内容的后果是它被永久判成「素材变了」。
                ToolboxLog.Warn($"序列素材指纹：认不出动作 {code}，这一条不记指纹。");
                continue;
            }

            // 特效帧只在**这一层也真的同步成功**时才算进来：
            // 特效失败了却把它记成"已经同步过"，下一轮检测就会说无差异，特效再也上不去。
            var includeEffectFrames = executedEffectLayers.Contains(
                SequenceEffectSyncService.BuildLayerCode(code));
            var hashes = ComputeCurrentSourceHashes(character, section, includeEffectFrames);
            fingerprints.Actions[SequenceFrameIdentity.BuildActionStableId(code)] =
                new UnrealBridgeSequenceActionFingerprint
            {
                SourceHashes = hashes,
                ContentDigest = ComputeDigest(hashes),
            };
        }

        var path = GetPath(character);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicFileWriter.WriteAllText(
            path,
            JsonSerializer.Serialize(fingerprints, AppJsonSerializerContext.Default.UnrealBridgeSequenceContentFingerprints));
    }

    /// <summary>动作代号 → 帧段。取不到（读盘失败之类）就返回空表，指纹那一步照常走完。</summary>
    private static IReadOnlyDictionary<string, SequenceFrameSection> LoadSectionsByActionCode(
        CharacterCard character)
    {
        try
        {
            return new SequenceFrameService()
                .LoadSections(character, new CharacterSkillsService().Load(character))
                .GroupBy(section => section.Action.Code, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or JsonException)
        {
            ToolboxLog.Warn($"序列素材指纹：读取动作帧段失败，这一轮不记内容指纹。角色 {character.Code}", error);
            return new Dictionary<string, SequenceFrameSection>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// 这条序列当前用到的源图 → 内容哈希（自己的 + 借来的，都算）。
    /// 借来的图也算：换了来源那张图，借用方同样要重做。
    ///
    /// **特效层的图也算进来**：特效随动作同步，而"特效改了"这件事只有内容指纹能体现 ——
    /// 不算进来的话，只改特效（序列没动）时这个动作会被判成"无差异"，特效就上不去。
    /// </summary>
    public static Dictionary<string, string> ComputeCurrentSourceHashes(
        CharacterCard character,
        SequenceFrameSection section,
        bool includeEffectFrames = true)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(section);

        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in SequenceActionFolderLayout.ResolveSourceImages(section.Frames))
        {
            if (hashes.ContainsKey(file) || !File.Exists(file))
            {
                continue;
            }

            hashes[file] = ComputeFileHash(file);
        }

        if (includeEffectFrames)
        {
            foreach (var effectFrame in new SequenceEffectService()
                .Load(character, section.Action)
                .Frames
                .Where(frame => !frame.IsEmpty))
            {
                var path = effectFrame.FilePath;
                if (string.IsNullOrWhiteSpace(path) || hashes.ContainsKey(path) || !File.Exists(path))
                {
                    continue;
                }

                hashes[path] = ComputeFileHash(path);
            }
        }

        return hashes;
    }

    /// <summary>把「上次同步时记下的内容摘要」写进 Unreal 侧的动作载荷，供差异比较。</summary>
    public static UnrealBridgeSnapshot ApplyRecordedContent(
        CharacterCard character,
        UnrealBridgeSnapshot unrealSnapshot)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(unrealSnapshot);

        var fingerprints = Load(character);
        if (fingerprints is null || fingerprints.Actions.Count == 0)
        {
            return unrealSnapshot;
        }

        var items = unrealSnapshot.Items
            .Select(item => IsSequenceActionItem(item) &&
                    fingerprints.Actions.TryGetValue(item.StableId, out var fingerprint) &&
                    !string.IsNullOrWhiteSpace(fingerprint.ContentDigest)
                ? ApplyContent(item, fingerprint.ContentDigest)
                : item)
            .ToArray();
        return unrealSnapshot with { Items = items };
    }

    /// <summary>
    /// 换掉载荷里的内容摘要，**并且重算 ContentHash**。
    ///
    /// 差异比较比的是 ContentHash（不是载荷本身），改了载荷不重算哈希的话，
    /// Unreal 侧那一项还带着「改之前」的哈希 —— 于是两侧永远不相等，
    /// 每个有记录的动作都会被判成「素材变了」。这个坑是靠探针发现的。
    /// 序列动作项没有 assetPath，语义快照那边的哈希就是 SHA256(载荷)，这里按同一口径重算。
    /// </summary>
    private static UnrealBridgeSnapshotItem ApplyContent(UnrealBridgeSnapshotItem item, string digest)
    {
        var payload = WithContentField(item.PayloadJson, digest);
        return item with
        {
            PayloadJson = payload,
            ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))),
        };
    }

    /// <summary>把「当前的」内容摘要写进工具箱侧的动作载荷，供差异比较。</summary>
    public static string WithCurrentContent(
        CharacterCard character,
        SequenceFrameSection section,
        string payloadJson,
        UnrealBridgeSequenceContentFingerprints? fingerprints)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(section);

        // 没有记录就不带这个字段：否则每个动作都会因为「一边有、一边没有」被判成变化。
        var stableId = SequenceFrameIdentity.BuildActionStableId(section.Action.Code);
        if (fingerprints is null || !fingerprints.Actions.ContainsKey(stableId))
        {
            return payloadJson;
        }

        var digest = ComputeDigest(ComputeCurrentSourceHashes(character, section));
        return WithContentField(payloadJson, digest);
    }

    /// <summary>
    /// 把内容摘要塞进动作载荷。载荷是手拼的 JSON（字段顺序固定、两侧同源），
    /// 所以这里也按同样的方式拼一个字段进去，而不是反序列化再序列化。
    /// </summary>
    private static string WithContentField(string? payloadJson, string digest)
    {
        var payload = (payloadJson ?? string.Empty).Trim();
        if (payload.Length == 0 || payload[^1] != '}')
        {
            return payloadJson ?? string.Empty;
        }

        var insertAt = payload.Length - 1;
        var prefix = payload[..insertAt].TrimEnd();
        var separator = prefix.EndsWith('{') ? string.Empty : ",";
        return $"{prefix}{separator}\"{ContentFieldName}\":\"{digest}\"}}";
    }

    private static bool IsSequenceActionItem(UnrealBridgeSnapshotItem item) =>
        item.Module == UnrealBridgeModule.SequenceFrames &&
        SequenceFrameIdentity.IsActionStableId(item.StableId);

    /// <summary>按文件名排序后拼「名字:哈希」，再取摘要 —— 增删图、改内容都会变。</summary>
    public static string ComputeDigest(IReadOnlyDictionary<string, string> hashes)
    {
        ArgumentNullException.ThrowIfNull(hashes);
        if (hashes.Count == 0)
        {
            return string.Empty;
        }

        var text = string.Join(
            "\n",
            hashes.OrderBy(pair => Path.GetFileName(pair.Key), StringComparer.OrdinalIgnoreCase)
                .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => $"{Path.GetFileName(pair.Key)}:{pair.Value}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();
    }

    private static string ComputeFileHash(string path)
    {
        try
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ToolboxLog.Warn($"读取素材内容哈希失败：{path}", error);
            return string.Empty;
        }
    }
}
