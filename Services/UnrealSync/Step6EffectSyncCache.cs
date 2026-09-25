using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 第六步「特效同步」自己的缓存：一步一个文件，只装这一步的东西 ——
/// **这次算出来的特效动作清单**（每个有特效层的动作各一条：几帧 / 网格几×几 / sheet 在不在 / MI 叫什么）
/// 以及**上一次真正同步的时间**。
///
/// 落点：`&lt;角色&gt;/&lt;工具目录&gt;/UnrealSync/step6-effect-sync.json`。
///
/// 为什么不像第四步那样直接存整份计划：第四步存的是差异树（`UnrealBridgeChange`），
/// 那份东西的用途是"恢复勾选"；这一步**没有勾选**（凡是有特效层的动作都同步），
/// 界面上要看的只有"有哪些动作、各自什么形状"。所以这里存的是**列表快照**，
/// 比整份计划小得多，也不会把 `effect-sync-plan.json`（那份要喂 Python）变成第二真相源。
/// </summary>
internal static class Step6EffectSyncCache
{
    /// <summary>文件格式版本：加字段就抬一版，读到不认识的版本一律当没缓存。</summary>
    public const int CurrentVersion = 1;

    public const string FileName = "step6-effect-sync.json";

    public static string GetFilePath(CharacterCard? character)
    {
        var folder = UnrealSyncCacheFolder.GetCacheFolderPath(character);
        return string.IsNullOrWhiteSpace(folder) ? string.Empty : Path.Combine(folder, FileName);
    }

    /// <summary>写缓存。落盘失败不打断流程，但要**留一行日志**（不许静默）。</summary>
    public static bool Save(
        CharacterCard? character,
        IReadOnlyList<Step6EffectSyncItem> items,
        DateTimeOffset? appliedAtUtc = null)
    {
        var path = GetFilePath(character);
        if (character is null || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var document = new Step6EffectSyncCacheDocument
        {
            Version = CurrentVersion,
            CharacterCode = character.Code,
            SavedAtUtc = DateTimeOffset.UtcNow,
            AppliedAtUtc = appliedAtUtc,
            Items = items.ToArray()
        };

        try
        {
            AtomicFileWriter.WriteAllText(
                path,
                System.Text.Json.JsonSerializer.Serialize(
                    document,
                    AppJsonSerializerContext.Default.Step6EffectSyncCacheDocument));
            return true;
        }
        catch (Exception error)
        {
            ToolboxLog.Warn($"[UnrealSync] 第 6 步特效清单写入失败：{path}", error);
            return false;
        }
    }

    /// <summary>读缓存；没有 / 版本不认识 / 角色代号对不上 / 坏了，都返回 null（当作"没查过"）。</summary>
    public static Step6EffectSyncCacheDocument? TryLoad(CharacterCard? character, string characterCode)
    {
        var path = GetFilePath(character);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var document = System.Text.Json.JsonSerializer.Deserialize(
                File.ReadAllText(path),
                AppJsonSerializerContext.Default.Step6EffectSyncCacheDocument);
            if (document is null || document.Version != CurrentVersion)
            {
                return null;
            }

            return string.Equals(document.CharacterCode, characterCode, StringComparison.OrdinalIgnoreCase)
                ? document
                : null;
        }
        catch (Exception)
        {
            // 读不出来就当"没查过"：这一步的检测很便宜（本地打 sheet），重查一遍比报错友好。
            return null;
        }
    }
}

/// <summary>第六步缓存的落盘形状。</summary>
internal sealed record Step6EffectSyncCacheDocument
{
    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("characterCode")]
    public string CharacterCode { get; init; } = string.Empty;

    [JsonPropertyName("savedAtUtc")]
    public DateTimeOffset SavedAtUtc { get; init; }

    /// <summary>上一次真正把这批特效写进 Unreal 的时间（没同步过就是 null）。</summary>
    [JsonPropertyName("appliedAtUtc")]
    public DateTimeOffset? AppliedAtUtc { get; init; }

    [JsonPropertyName("items")]
    public Step6EffectSyncItem[] Items { get; init; } = [];
}

/// <summary>
/// 一条特效动作：**既是要落盘的形状，也是中栏直接绑的那一项**
/// （这一步没有勾选，所以不需要再包一层 ViewItem）。
/// </summary>
internal sealed record Step6EffectSyncItem
{
    [JsonPropertyName("actionCode")]
    public string ActionCode { get; init; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>网格里占的格数 = 输出帧数（含空帧占位格）。</summary>
    [JsonPropertyName("frameCount")]
    public int FrameCount { get; init; }

    [JsonPropertyName("columns")]
    public int Columns { get; init; }

    [JsonPropertyName("rows")]
    public int Rows { get; init; }

    /// <summary>网格 sheet 的 PNG 有没有真的打出来（没打出来这一步会跳过它）。</summary>
    [JsonPropertyName("sheetReady")]
    public bool SheetReady { get; init; }

    [JsonPropertyName("materialName")]
    public string MaterialName { get; init; } = string.Empty;

    /// <summary>特效自己的帧率（= 动作 fps × 2）。</summary>
    [JsonPropertyName("fps")]
    public double Fps { get; init; }

    // ── 下面几条只给界面用，不落盘 ────────────────────────────────────────

    [JsonIgnore]
    public string GridText => Columns > 0 && Rows > 0 ? $"{Columns}×{Rows}" : "—";

    [JsonIgnore]
    public string StatusText => SheetReady ? "sheet 已就绪" : "sheet 缺失";

    [JsonIgnore]
    public string DetailText =>
        $"{FrameCount} 帧 · 网格 {GridText} · {Fps:0.##} fps · {StatusText}";

    /// <summary>
    /// 这一条最终落到 Unreal 的哪两样。
    ///
    /// 只写 sheet + 材质实例：面片 / 母材质 / 粒子系统都是**插件里的共享资产**
    /// （`/ZDBridge/FX/…`），不随动作走，所以列表上不该出现"每个动作一个系统"那种说法。
    ///
    /// （计划模型里那个 `EffectNiagaraSystemName` 从头到尾没有任何地方赋值，一直是空 ——
    /// 是早期"一个动作一个 Niagara 系统"那版设计留下的字段，别拿它当依据。）
    /// </summary>
    [JsonIgnore]
    public string TargetText => string.IsNullOrWhiteSpace(MaterialName)
        ? "材质实例未生成"
        : $"MI = {MaterialName}";

    /// <summary>从计划里的一条特效动作折过来（命名规则只在工具箱侧算一次）。</summary>
    public static Step6EffectSyncItem From(UnrealBridgeSequenceSyncAction action) => new()
    {
        ActionCode = action.ActionCode,
        DisplayName = string.IsNullOrWhiteSpace(action.DisplayName) ? action.ActionCode : action.DisplayName,
        FrameCount = action.Frames.Count,
        Columns = action.EffectColumns,
        Rows = action.EffectRows,
        SheetReady = !string.IsNullOrWhiteSpace(action.EffectSheetImagePath)
            && File.Exists(action.EffectSheetImagePath),
        MaterialName = action.EffectMaterialName,
        Fps = action.EffectFps
    };
}
