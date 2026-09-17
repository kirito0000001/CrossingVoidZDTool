using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CrossingVoidZDTool.Services.Atlas;

namespace CrossingVoidZDTool.Services;

internal sealed class UnrealBridgeSequenceSyncPlan
{
    public int ProtocolVersion { get; set; } = 1;
    public string CharacterCode { get; set; } = string.Empty;
    public string UnrealProjectPath { get; set; } = string.Empty;
    public string CharacterBlueprintPath { get; set; } = string.Empty;
    public string AnimMapsPath { get; set; } = string.Empty;
    public List<UnrealBridgeSequenceSyncAction> Actions { get; set; } = [];

    /// <summary>
    /// 要从角色动画源上解绑的非规范序列（对象路径）。
    /// 只解绑、不删资产：串错位置的序列往往仍是有用素材，
    /// 因为它看起来不该在这儿就把它删掉，代价太大。
    /// </summary>
    public List<string> DetachSequenceObjectPaths { get; set; } = [];
}

internal sealed class UnrealBridgeSequenceSyncAction
{
    /// <summary>规范变体代号，形态 1 为 <c>Idle</c>，形态 2 为 <c>Idle_Shape2</c>。</summary>
    public string ActionCode { get; set; } = string.Empty;
    /// <summary>不含形态后缀的规范代号。</summary>
    public string BaseActionCode { get; set; } = string.Empty;
    public int FormIndex { get; set; } = 1;
    public string DisplayName { get; set; } = string.Empty;
    public int Fps { get; set; } = SequenceFrameService.DefaultFps;
    /// <summary>角色蓝图上的序列数组属性名；为空表示该动作不写角色蓝图。</summary>
    public string BlueprintProperty { get; set; } = string.Empty;
    /// <summary>序列在蓝图数组里的下标，等于形态序号减一（CharShape 从 0 开始）。</summary>
    public int BlueprintFormSlotIndex { get; set; }
    public string AnimMapsEntryName { get; set; } = string.Empty;
    public string TargetSequencePath { get; set; } = string.Empty;
    public string TargetMaterialFolder { get; set; } = string.Empty;
    public string SequenceAssetName { get; set; } = string.Empty;
    public string FlipbookAssetName { get; set; } = string.Empty;
    /// <summary>该动作的历史命名 token；仅在计划没有给出显式待删列表时作为兜底。</summary>
    public List<string> LegacyNameTokens { get; set; } = [];
    /// <summary>
    /// 用户在差异树里勾选的待删除资产对象路径。这是清理的权威依据：
    /// 按资产名 token 猜测既会漏删（历史命名不含动作 token），也会误删
    /// （规范目录里用户特意没勾的资产）。
    /// </summary>
    public List<string> StaleAssetObjectPaths { get; set; } = [];
    /// <summary>是否由计划给出了显式待删列表；为真时 Python 不再做 token 猜测。</summary>
    public bool HasStaleAssetSelection { get; set; }
    public List<UnrealBridgeSequenceSyncFrame> Frames { get; set; } = [];

    /// <summary>
    /// 这个动作的图集。整条序列共用一张贴图，每帧只切出自己那一格。
    ///
    /// 换成图集之前是「每帧导入一张 PNG」，复用的帧会被各导一份 ——
    /// Sk2 素材目录里 17 张图，导入后变成 23 张贴图加 23 个精灵。
    /// 图集把这件事收敛成「一张贴图 + 每张素材一个精灵」。
    /// </summary>
    public UnrealBridgeSequenceSyncAtlas? Atlas { get; set; }

    /// <summary>
    /// 图集里的每一格，对应素材目录里的一张 PNG。
    /// 帧引用它们而不是各自带一份图片信息，复用位置因此共享同一个精灵。
    /// </summary>
    public List<UnrealBridgeSequenceSyncSourceImage> SourceImages { get; set; } = [];
}

/// <summary>动作的图集：一张贴图，加上它在磁盘上的出处。</summary>
internal sealed class UnrealBridgeSequenceSyncAtlas
{
    /// <summary>图集名，形如 <c>Misaka_Sk2</c>。同时就是要导入的贴图资产名。</summary>
    public string AtlasName { get; set; } = string.Empty;

    /// <summary>图集 PNG 的绝对路径。</summary>
    public string ImagePath { get; set; } = string.Empty;

    public int Width { get; set; }

    public int Height { get; set; }
}

/// <summary>
/// 图集里的一格（= 素材目录里的一张 PNG）。
///
/// <c>Width</c>/<c>Height</c> 是这一格在图集贴图里的尺寸，
/// 而 <c>SourceImageWidth</c>/<c>SourceImageHeight</c> 是裁剪前的原图尺寸 ——
/// 两者不同正是「被裁过」。精灵必须同时知道这两件事：矩形决定从哪儿取图，
/// 原图尺寸决定这一格该落在画布的哪个位置。只给矩形的话，每一帧都会被贴到画布左上角。
/// </summary>
internal sealed class UnrealBridgeSequenceSyncSourceImage
{
    /// <summary>序号，从 1 起，与图集清单和帧表一致。</summary>
    public int Index { get; set; }

    public string SpriteAssetName { get; set; } = string.Empty;

    /// <summary>素材原图路径，仅供排查用。</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>在图集贴图里的矩形。</summary>
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public bool Rotated { get; set; }
    public bool Trimmed { get; set; }

    /// <summary>裁剪后这块内容在**原图**里的左上角。</summary>
    public int TrimOriginX { get; set; }
    public int TrimOriginY { get; set; }

    /// <summary>裁剪前的原图尺寸。</summary>
    public int SourceImageWidth { get; set; }
    public int SourceImageHeight { get; set; }
}

internal sealed class UnrealBridgeSequenceSyncFrame
{
    /// <summary>工具箱侧的帧序号，从 1 开始。</summary>
    public int Index { get; set; }
    /// <summary>虚幻侧的帧位置，从 0 开始，与 Flipbook 关键帧下标一致。</summary>
    public int Ordinal { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public int DurationFrames { get; set; } = 1;
    public bool IsBlank { get; set; }
    public string VoiceFileName { get; set; } = string.Empty;

    /// <summary>
    /// 这一帧用图集里的第几格（<see cref="UnrealBridgeSequenceSyncSourceImage.Index"/>）。
    /// 空白帧为 0 —— 它在图集里没有对应的图。
    ///
    /// 复用位置会指向同一个序号，这对 Flipbook 来说就是「同一个精灵出现在多个关键帧」，
    /// 正是复用该有的样子。
    /// </summary>
    public int SourceImageIndex { get; set; }

    /// <summary>精灵名。取自它引用的那一格，不再跟着帧位置变。</summary>
    public string SpriteAssetName { get; set; } = string.Empty;
}

/// <summary>
/// 一个动作的图集输入：打包产物的出处，加上「哪一格是哪个矩形」。
///
/// 计划生成拿它把帧指向图集的一格。没有它就拼不出图集版本的同步计划 ——
/// 装箱是打包器做的，框在哪只有它说了算，工具箱不能自己算。
/// </summary>
internal sealed class UnrealBridgeSequenceAtlasInput
{
    public string AtlasName { get; set; } = string.Empty;

    public string ImagePath { get; set; } = string.Empty;

    public int Width { get; set; }

    public int Height { get; set; }

    /// <summary>帧表按序号建的索引。</summary>
    public IReadOnlyDictionary<int, AtlasSequenceFrame> FramesByOrdinal { get; set; } =
        new Dictionary<int, AtlasSequenceFrame>();
}

internal sealed class UnrealBridgeSequencePublishService
{
    private static readonly Regex FrameDisplayNamePattern = new(@"^(.*?)\s*第\s*\d+\s*帧\s*$");

    /// <summary>本次因为没有序列帧数据而被跳过的动作，供调用方提示用户。</summary>
    public IReadOnlyList<string> SkippedActionCodes { get; private set; } = [];

    public UnrealBridgeSequenceSyncPlan BuildSequenceSyncPlan(
        CharacterCard character,
        string projectPath,
        IReadOnlyList<UnrealBridgeChange> selectedSequenceChanges,
        IReadOnlyDictionary<string, UnrealBridgeSequenceAtlasInput>? atlases = null)
    {
        ArgumentNullException.ThrowIfNull(character);
        atlases ??= new Dictionary<string, UnrealBridgeSequenceAtlasInput>(StringComparer.OrdinalIgnoreCase);
        // 孤儿序列不属于任何动作，单独收集；它们只需要解绑，不参与逐动作的重建。
        var detachPaths = selectedSequenceChanges
            .Where(change => change.IsSelected &&
                change.Module == UnrealBridgeModule.SequenceFrames &&
                SequenceFrameIdentity.IsOrphanSequenceStableId(change.StableId))
            .Select(change => change.UnrealItem?.SourceObjectPath ?? string.Empty)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var selectedActions = selectedSequenceChanges
            .Where(change => change.IsSelected && change.Module == UnrealBridgeModule.SequenceFrames)
            .Select(ResolveAction)
            .Where(resolved => resolved is not null)
            .Select(resolved => resolved!.Value)
            .DistinctBy(resolved => (resolved.Definition.Code, resolved.FormIndex))
            .ToArray();
        // 只勾了非规范序列（只需解绑、不重建任何动作）也是合法的一批。
        if (selectedActions.Length == 0 && detachPaths.Count == 0)
        {
            throw new InvalidOperationException("没有选择需要同步的序列动作。");
        }

        // 按动作归集用户勾选的待删除资产路径。
        var staleByAction = new Dictionary<(string Code, int FormIndex), List<string>>();
        foreach (var change in selectedSequenceChanges.Where(change =>
            change.IsSelected &&
            change.Module == UnrealBridgeModule.SequenceFrames &&
            change.Kind == UnrealBridgeChangeKind.DeleteCandidate &&
            !string.IsNullOrWhiteSpace(change.UnrealItem?.SourceObjectPath)))
        {
            var resolved = ResolveAction(change);
            if (resolved is null)
            {
                continue;
            }

            var key = (resolved.Value.Definition.Code, resolved.Value.FormIndex);
            if (!staleByAction.TryGetValue(key, out var paths))
            {
                paths = [];
                staleByAction[key] = paths;
            }

            paths.Add(change.UnrealItem!.SourceObjectPath);
        }

        var frameService = new SequenceFrameService();
        var sections = frameService.LoadSections(character, new CharacterSkillsService().Load(character));
        var sectionByAction = sections
            .Select(section => (section, resolved: TryResolveActionCode(section.Action.Code)))
            .Where(pair => pair.resolved is not null)
            .GroupBy(pair => (pair.resolved!.Value.Definition.Code, pair.resolved!.Value.FormIndex))
            .ToDictionary(group => group.Key, group => group.First().section);

        var skipped = new List<string>();
        var project = Path.GetFullPath(projectPath);
        var root = $"/Game/GameActor2D/{character.Code}";
        var plan = new UnrealBridgeSequenceSyncPlan
        {
            CharacterCode = character.Code,
            UnrealProjectPath = project,
            CharacterBlueprintPath = $"{root}/{character.Code}.{character.Code}",
            AnimMapsPath = $"{root}/{character.Code}_AnimMaps.{character.Code}_AnimMaps",
            DetachSequenceObjectPaths = detachPaths
        };
        foreach (var (definition, formIndex) in selectedActions)
        {
            // 找不到数据、或者工具箱侧一帧都没有的动作，跳过即可。
            // 以前这里直接抛异常，会让同一批里其它本来能成功的动作全部失败；
            // 零帧动作送到 Python 也只会换个地方抛"no frames in toolbox data"。
            if (!sectionByAction.TryGetValue((definition.Code, formIndex), out var section) ||
                section.Frames.Count == 0)
            {
                skipped.Add(SequenceActionCatalog.GetVariantCode(definition, formIndex));
                continue;
            }

            var fps = frameService.GetActionFps(character, section.Action);
            var variantCode = SequenceActionCatalog.GetVariantCode(definition, formIndex);
            var atlas = ResolveAtlas(atlases, definition, formIndex);
            var action = new UnrealBridgeSequenceSyncAction
            {
                ActionCode = variantCode,
                BaseActionCode = definition.Code,
                FormIndex = formIndex,
                DisplayName = formIndex > 1 ? $"{definition.DisplayName}-{formIndex}" : definition.DisplayName,
                Fps = Math.Max(1, fps),
                BlueprintProperty = definition.BlueprintSequenceArrayProperty,
                BlueprintFormSlotIndex = formIndex - 1,
                AnimMapsEntryName = string.IsNullOrEmpty(definition.AnimMapsEntryName)
                    ? string.Empty
                    : SequenceActionCatalog.GetVariantCode(definition, formIndex),
                // TargetSequencePath 是资产路径，动作名不能重复成为上级文件夹。
                TargetSequencePath = $"{root}/AnimSequences/{SequenceActionCatalog.GetAnimSequenceName(definition, formIndex)}",
                TargetMaterialFolder = $"{root}/Material/{SequenceActionCatalog.GetMaterialFolderName(definition, formIndex)}",
                SequenceAssetName = SequenceActionCatalog.GetAnimSequenceName(definition, formIndex),
                FlipbookAssetName = SequenceActionCatalog.GetFlipbookName(definition, formIndex),
                LegacyNameTokens = SequenceActionCatalog.GetLegacyNameTokens(definition, formIndex).ToList(),
                StaleAssetObjectPaths = staleByAction.TryGetValue((definition.Code, formIndex), out var stalePaths)
                    ? stalePaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                    : [],
                HasStaleAssetSelection = true,
                Atlas = new UnrealBridgeSequenceSyncAtlas
                {
                    AtlasName = atlas.AtlasName,
                    ImagePath = atlas.ImagePath,
                    Width = atlas.Width,
                    Height = atlas.Height
                },
                SourceImages = BuildSourceImages(character, section, definition, formIndex, atlas)
            };
            // 虚幻侧的帧序号按整条序列的位置从 0 开始编号，空白帧同样占一个位置，
            // 这样资产编号和 Flipbook 的关键帧下标始终一一对应。
            var orderedFrames = section.Frames.OrderBy(frame => frame.Index).ToArray();
            // 帧指向「图集里的哪一格」。同一张素材被复用时，这里会算出同一个序号 ——
            // Flipbook 因此复用同一个精灵，而不是各建一份。
            var sourceIndexByPath = action.SourceImages
                .Where(image => !string.IsNullOrWhiteSpace(image.FilePath))
                .GroupBy(image => Path.GetFullPath(image.FilePath), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.OrdinalIgnoreCase);
            var spriteNameByIndex = action.SourceImages
                .GroupBy(image => image.Index)
                .ToDictionary(group => group.Key, group => group.First().SpriteAssetName);
            action.Frames = orderedFrames
                .Select((frame, ordinal) => new UnrealBridgeSequenceSyncFrame
                {
                    Index = frame.Index,
                    Ordinal = ordinal,
                    FilePath = frame.IsBlank ? string.Empty : Path.GetFullPath(frame.FilePath),
                    DurationFrames = Math.Max(1, frame.DurationFrames),
                    IsBlank = frame.IsBlank,
                    VoiceFileName = frame.VoiceFileName,
                    SourceImageIndex = ResolveSourceImageIndex(frame, sourceIndexByPath),
                    SpriteAssetName = ResolveFrameSpriteName(frame, sourceIndexByPath, spriteNameByIndex),
                })
                .ToList();
            plan.Actions.Add(action);
        }

        if (plan.Actions.Count == 0 && plan.DetachSequenceObjectPaths.Count == 0)
        {
            throw new InvalidOperationException(skipped.Count > 0
                ? $"选中的动作在工具箱里没有序列帧数据：{string.Join("、", skipped)}。"
                : "没有选择需要同步的序列动作。");
        }

        SkippedActionCodes = skipped;
        return plan;
    }

    public void Save(string path, UnrealBridgeSequenceSyncPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        // 使用源生成上下文序列化；Release 构建启用了 PublishTrimmed，反射序列化会被裁剪掉。
        File.WriteAllText(
            fullPath,
            JsonSerializer.Serialize(plan, AppJsonSerializerContext.Default.UnrealBridgeSequenceSyncPlan),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>图集输入在字典里的键。用「代号 + 形态」的字符串，日志里能直接看懂。</summary>
    public static string AtlasKey(string actionCode, int formIndex) => $"{actionCode}|{formIndex}";

    /// <summary>
    /// 从勾选的变化里解析出要处理的动作，去重。
    ///
    /// 打包图集和生成计划都要用这一份解析 —— 各写一套的话，迟早出现
    /// 「给 A 打了图集、计划里却在找 B」，而两边都不会报错，只会在 Unreal 里露馅。
    /// </summary>
    public static IReadOnlyList<(SequenceActionDefinition Definition, int FormIndex)> ResolveSelectedActions(
        IReadOnlyList<UnrealBridgeChange> selectedSequenceChanges)
    {
        ArgumentNullException.ThrowIfNull(selectedSequenceChanges);
        return selectedSequenceChanges
            .Where(change => change.IsSelected && change.Module == UnrealBridgeModule.SequenceFrames)
            .Select(ResolveAction)
            .Where(resolved => resolved is not null)
            .Select(resolved => resolved!.Value)
            .DistinctBy(resolved => (resolved.Definition.Code, resolved.FormIndex))
            .ToArray();
    }

    /// <summary>
    /// 取这个动作的图集。**没有就报错，不退化成「每帧一张贴图」的老路。**
    ///
    /// 退化的代价太大：老路会把复用的帧各导一份，正是要修掉的那个 bug；
    /// 而且两条路产出的资产布局完全不同，混着跑会让差异树永远在「重建」和「已同步」之间跳。
    /// 与其悄悄退回去，不如当场说清是哪一步没做。
    /// </summary>
    private static UnrealBridgeSequenceAtlasInput ResolveAtlas(
        IReadOnlyDictionary<string, UnrealBridgeSequenceAtlasInput> atlases,
        SequenceActionDefinition definition,
        int formIndex)
    {
        if (atlases.TryGetValue(AtlasKey(definition.Code, formIndex), out var atlas))
        {
            return atlas;
        }

        throw new InvalidOperationException(
            $"动作「{SequenceActionCatalog.GetVariantCode(definition, formIndex)}」没有图集，无法生成同步计划。"
            + "同步前会先为勾选的动作打包图集，请检查打包是否被跳过或失败。");
    }

    /// <summary>
    /// 把图集里的每一格摊成同步计划里的素材项。
    ///
    /// 索引关系必须严丝合缝：素材目录按文件名升序的第 i 张（0 起），
    /// 在清单和帧表里都是第 i+1 格，精灵名也按同一个 i 生成。
    /// 三处一旦错位，切出来的精灵就是张冠李戴 —— 而且画面看着还挺正常，只是帧对不上。
    /// </summary>
    private static List<UnrealBridgeSequenceSyncSourceImage> BuildSourceImages(
        CharacterCard character,
        SequenceFrameSection section,
        SequenceActionDefinition definition,
        int formIndex,
        UnrealBridgeSequenceAtlasInput atlas)
    {
        var framesFolder = SequenceActionFolderLayout.GetFramesFolderPath(character, section.Action);
        var files = AtlasManifestWriter.EnumerateSourceImages(framesFolder);
        var images = new List<UnrealBridgeSequenceSyncSourceImage>(files.Count);
        for (var ordinal = 0; ordinal < files.Count; ordinal++)
        {
            var index = ordinal + 1;
            if (!atlas.FramesByOrdinal.TryGetValue(index, out var placed) || placed.Frame is null)
            {
                throw new InvalidOperationException(
                    $"图集「{atlas.AtlasName}」里没有第 {index} 格的矩形，无法生成同步计划。"
                    + "图集帧表和素材目录可能不是同一轮的结果。");
            }

            images.Add(new UnrealBridgeSequenceSyncSourceImage
            {
                Index = index,
                SpriteAssetName = SequenceActionCatalog.GetFrameSpriteName(definition, formIndex, ordinal, files.Count),
                FilePath = files[ordinal],
                X = placed.Frame.X,
                Y = placed.Frame.Y,
                Width = placed.Frame.W,
                Height = placed.Frame.H,
                Rotated = placed.Rotated,
                Trimmed = placed.Trimmed,
                // 裁剪偏移是「这块内容原本在原图的哪儿」。没裁过就是 0，
                // 原图尺寸退回这一格自己的尺寸，等价于「整张图就是这一格」。
                TrimOriginX = placed.SpriteSourceSize?.X ?? 0,
                TrimOriginY = placed.SpriteSourceSize?.Y ?? 0,
                SourceImageWidth = placed.SourceSize?.W ?? placed.Frame.W,
                SourceImageHeight = placed.SourceSize?.H ?? placed.Frame.H,
            });
        }

        return images;
    }

    /// <summary>
    /// 这一帧用图集里的第几格。
    ///
    /// 空白帧返回 0（它在图集里没有图）。非空白帧找不到对应格子时**当场报错** ——
    /// 悄悄当成空白的话，界面会显示同步成功，实际那几帧从动画里消失了。
    /// 最常见的原因是帧用的是 jpg/webp：素材池允许，但图集只收 PNG。
    /// </summary>
    private static int ResolveSourceImageIndex(
        SequenceFrameItem frame,
        IReadOnlyDictionary<string, int> sourceIndexByPath)
    {
        if (frame.IsBlank || string.IsNullOrWhiteSpace(frame.FilePath))
        {
            return 0;
        }

        var fullPath = Path.GetFullPath(frame.FilePath);
        if (sourceIndexByPath.TryGetValue(fullPath, out var index))
        {
            return index;
        }

        throw new InvalidOperationException(
            $"帧素材「{Path.GetFileName(fullPath)}」不在本动作的图集里。"
            + "图集只收录素材目录下的 PNG，请把这张帧换成 PNG 后重新打包。");
    }

    private static string ResolveFrameSpriteName(
        SequenceFrameItem frame,
        IReadOnlyDictionary<string, int> sourceIndexByPath,
        IReadOnlyDictionary<int, string> spriteNameByIndex)
    {
        var index = ResolveSourceImageIndex(frame, sourceIndexByPath);
        return index > 0 && spriteNameByIndex.TryGetValue(index, out var name) ? name : string.Empty;
    }

    private static (SequenceActionDefinition Definition, int FormIndex)? ResolveAction(UnrealBridgeChange change)
    {
        var payload = change.ToolboxItem?.PayloadJson ?? change.UnrealItem?.PayloadJson ?? string.Empty;
        var resolved = TryResolveActionCode(ExtractActionCode(payload));
        if (resolved is not null)
        {
            return resolved;
        }

        if (change.SequenceGroupKey.StartsWith("sequence:", StringComparison.OrdinalIgnoreCase))
        {
            resolved = TryResolveActionCode(change.SequenceGroupKey[9..]);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        var match = FrameDisplayNamePattern.Match(change.DisplayName ?? string.Empty);
        return match.Success ? TryResolveActionCode(match.Groups[1].Value.Trim()) : null;
    }

    private static (SequenceActionDefinition Definition, int FormIndex)? TryResolveActionCode(string? value) =>
        SequenceActionCatalog.TryResolve(value, out var definition, out var formIndex)
            ? (definition, formIndex)
            : null;

    private static string ExtractActionCode(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return string.Empty;
        }

        var trimmed = payload.Trim();
        if (trimmed.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                if (doc.RootElement.TryGetProperty("actionCode", out var value) && value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString() ?? string.Empty;
                }
            }
            catch (JsonException)
            {
                // 回退到竖线分隔的旧格式。
            }
        }

        var parts = trimmed.Split('|', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : string.Empty;
    }
}
