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

    /// <summary>
    /// 这一格用的是哪张图集：图集资产名、图集图片（打成图集的那个 PNG）、
    /// 以及图集落在哪个材质目录。
    ///
    /// 自己的图 → 本动作的图集；借来的图 → **来源动作**的图集（那些图已经在那边了，
    /// 不再重复打包一份）。三样都给全，Python 侧才能按同一套流程把图集导进来 / 用起来。
    /// </summary>
    public string AtlasName { get; set; } = string.Empty;
    public string AtlasImagePath { get; set; } = string.Empty;
    public string AtlasMaterialFolder { get; set; } = string.Empty;

    /// <summary>
    /// 要不要在借用方自己的材质目录里建/更新这只精灵。
    ///
    /// 借来的图连精灵一起借：那张图在来源动作里已经切好一只精灵了，这里直接用那只，
    /// 借用方一只都不建（整条都借用别人的动作因此只剩一个 Flipbook）。
    /// </summary>
    public bool CreateSprite { get; set; } = true;

    /// <summary>不建精灵时，这只借来的精灵所在的材质目录。</summary>
    public string SpriteMaterialFolder { get; set; } = string.Empty;

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
        // 勾选的动作 + 它们借用素材的来源动作（来源在前）。借用方的精灵指向来源图集，
        // 所以来源那一版必须跟着重建，不然矩形可能和工程里的图集对不上。
        var selectedActions = ResolveActionsWithSourceOwners(character, selectedSequenceChanges).ToArray();
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
            // 只有「这个动作占用的资产」那一行才对应磁盘上的一个文件。
            // 帧行（…:delete）和「图集贴图」行是界面上的重建说明：图集时代一帧的 Unreal 路径
            // 指向的是**整条动作共用的那张图集**，把它塞进待删列表就会去删一张规范图集。
            SequenceFrameIdentity.IsOwnedAssetStableId(change.StableId) &&
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
            //
            // 「一帧都没有」和「有帧但没有一张图」要一起放过：后者是整条都由空白帧组成的动作，
            // 它没有图集可打（打包那边同样跳过），硬要在计划里找图集只会把这一批整体拦下来。
            if (!sectionByAction.TryGetValue((definition.Code, formIndex), out var section) ||
                section.Frames.Count == 0 ||
                SequenceActionFolderLayout.ResolveSourceImages(section.Frames).Count == 0)
            {
                skipped.Add(SequenceActionCatalog.GetVariantCode(definition, formIndex));
                continue;
            }

            var fps = frameService.GetActionFps(character, section.Action);
            var variantCode = SequenceActionCatalog.GetVariantCode(definition, formIndex);
            // 自己的图集：整条都复用别人的图时**没有**（那些图在来源图集里）。
            var ownAtlas = atlases.TryGetValue(AtlasKey(definition.Code, formIndex), out var resolvedAtlas)
                ? resolvedAtlas
                : null;
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
                Atlas = ownAtlas is null
                    ? null
                    : new UnrealBridgeSequenceSyncAtlas
                    {
                        AtlasName = ownAtlas.AtlasName,
                        ImagePath = ownAtlas.ImagePath,
                        Width = ownAtlas.Width,
                        Height = ownAtlas.Height
                    },
                SourceImages = BuildSourceImages(
                    character, section, definition, formIndex, atlases, sectionByAction, root)
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
    /// 勾选的动作 **加上** 它们借用素材的那些来源动作。
    ///
    /// 借用方的精灵指向来源动作的图集（那张图已经在那边了，不再重复打包），
    /// 所以来源动作必须是**当前**那一版 —— 不一起同步，借来的矩形就可能和工程里的图集对不上。
    /// 来源动作本身也是幂等重建，多做一次没有副作用。
    ///
    /// 顺序按「先来源、后借用」，同一批里先重建图集、再被引用，日志读起来也顺。
    /// 打包和生成计划都用这一份解析 —— 两边各写一套的话，迟早出现
    /// 「给 A 打了图集、计划里却在找 B」。
    /// </summary>
    public static IReadOnlyList<(SequenceActionDefinition Definition, int FormIndex)> ResolveActionsWithSourceOwners(
        CharacterCard character,
        IReadOnlyList<UnrealBridgeChange> selectedSequenceChanges)
    {
        ArgumentNullException.ThrowIfNull(character);
        var selected = ResolveSelectedActions(selectedSequenceChanges);
        if (selected.Count == 0)
        {
            return selected;
        }

        var sections = new SequenceFrameService()
            .LoadSections(character, new CharacterSkillsService().Load(character));
        var sectionByAction = sections
            .Select(section => (section, resolved: TryResolveActionCode(section.Action.Code)))
            .Where(pair => pair.resolved is not null)
            .GroupBy(pair => (pair.resolved!.Value.Definition.Code, pair.resolved!.Value.FormIndex))
            .ToDictionary(group => group.Key, group => group.First().section);

        var included = new Dictionary<(string Code, int FormIndex), (SequenceActionDefinition Definition, int FormIndex)>();
        var ownerOf = new Dictionary<(string Code, int FormIndex), HashSet<(string Code, int FormIndex)>>();
        foreach (var entry in selected)
        {
            included[(entry.Definition.Code, entry.FormIndex)] = entry;
        }

        // 借用关系可能不止一层（A 借 B、B 又借 C），所以按队列推到收敛。
        var queue = new Queue<(SequenceActionDefinition Definition, int FormIndex)>(selected);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!sectionByAction.TryGetValue((current.Definition.Code, current.FormIndex), out var section))
            {
                continue;
            }

            var borrowOwners = SequenceActionFolderLayout
                .ResolveSourceImagePlan(character, section.Action, section.Frames)
                .Where(image => !image.IsOwn)
                .Select(image => image.OwnerActionCode)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var ownerCode in borrowOwners)
            {
                if (!SequenceActionCatalog.TryResolve(ownerCode, out var ownerDefinition, out var ownerFormIndex) ||
                    !sectionByAction.ContainsKey((ownerDefinition.Code, ownerFormIndex)))
                {
                    continue;
                }

                var key = (ownerDefinition.Code, ownerFormIndex);
                var currentKey = (current.Definition.Code, current.FormIndex);
                if (!ownerOf.TryGetValue(currentKey, out var owners))
                {
                    owners = [];
                    ownerOf[currentKey] = owners;
                }
                owners.Add(key);

                if (included.ContainsKey(key))
                {
                    continue;
                }

                var entry = (ownerDefinition, ownerFormIndex);
                included[key] = entry;
                queue.Enqueue(entry);
            }
        }

        // 拓扑：来源在前。图很小（十几个动作），直接稳住顺序即可。
        var ordered = new List<(SequenceActionDefinition Definition, int FormIndex)>();
        var emitted = new HashSet<(string Code, int FormIndex)>();
        void Emit((SequenceActionDefinition Definition, int FormIndex) entry)
        {
            var key = (entry.Definition.Code, entry.FormIndex);
            if (!emitted.Add(key) || !included.ContainsKey(key))
            {
                return;
            }

            if (ownerOf.TryGetValue(key, out var owners))
            {
                foreach (var owner in owners)
                {
                    if (included.TryGetValue(owner, out var ownerEntry))
                    {
                        Emit(ownerEntry);
                    }
                }
            }

            ordered.Add(entry);
        }

        foreach (var entry in selected)
        {
            Emit(entry);
        }

        foreach (var entry in included.Values)
        {
            Emit(entry);
        }

        return ordered;
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
        IReadOnlyDictionary<string, UnrealBridgeSequenceAtlasInput> atlases,
        IReadOnlyDictionary<(string Code, int FormIndex), SequenceFrameSection> sectionByAction,
        string unrealRoot)
    {
        // 每一格素材「用哪张图集、用哪只精灵、要不要在本动作里新建」。
        // 借来的图**连精灵一起借**（来源动作那只），所以这些格子既不需要矩形、
        // 也不需要为自己再打一份图集。
        var resolutions = SequenceSourceImageResolver.Resolve(
            character,
            section.Action,
            section.Frames,
            ownerCode => ResolveSectionByActionCode(sectionByAction, ownerCode));
        var images = new List<UnrealBridgeSequenceSyncSourceImage>(resolutions.Count);
        var variantCode = SequenceActionCatalog.GetVariantCode(definition, formIndex);
        for (var ordinal = 0; ordinal < resolutions.Count; ordinal++)
        {
            var resolution = resolutions[ordinal];
            var index = ordinal + 1;
            var image = new UnrealBridgeSequenceSyncSourceImage
            {
                Index = index,
                SpriteAssetName = resolution.SpriteAssetName,
                FilePath = resolution.FilePath,
                AtlasName = resolution.AtlasName,
                AtlasMaterialFolder = resolution.AtlasMaterialFolder,
                SpriteMaterialFolder = resolution.SpriteMaterialFolder,
                CreateSprite = resolution.CreateSprite,
            };

            if (!resolution.CreateSprite)
            {
                // 直接用来源动作那只精灵，不需要矩形。
                images.Add(image);
                continue;
            }

            if (!TryResolveAtlasCell(character, resolution, sectionByAction, atlases, out var atlas, out var placed))
            {
                throw new InvalidOperationException(
                    $"动作「{variantCode}」的第 {index} 张素材（{Path.GetFileName(resolution.FilePath)}）"
                    + "找不到它所在的图集格，无法生成同步计划。图集帧表和素材目录可能不是同一轮的结果；"
                    + "借用别人的素材时，来源动作必须一起同步（界面上会自动带上）。");
            }

            image.AtlasName = atlas!.AtlasName;
            image.AtlasImagePath = atlas.ImagePath;
            image.X = placed!.Frame!.X;
            image.Y = placed.Frame.Y;
            image.Width = placed.Frame.W;
            image.Height = placed.Frame.H;
            image.Rotated = placed.Rotated;
            image.Trimmed = placed.Trimmed;
            // 裁剪偏移是「这块内容原本在原图的哪儿」。没裁过就是 0，
            // 原图尺寸退回这一格自己的尺寸，等价于「整张图就是这一格」。
            image.TrimOriginX = placed.SpriteSourceSize?.X ?? 0;
            image.TrimOriginY = placed.SpriteSourceSize?.Y ?? 0;
            image.SourceImageWidth = placed.SourceSize?.W ?? placed.Frame.W;
            image.SourceImageHeight = placed.SourceSize?.H ?? placed.Frame.H;
            images.Add(image);
        }

        return images;
    }

    private static SequenceFrameSection? ResolveSectionByActionCode(
        IReadOnlyDictionary<(string Code, int FormIndex), SequenceFrameSection> sectionByAction,
        string actionCode) =>
        SequenceActionCatalog.TryResolve(actionCode, out var definition, out var formIndex) &&
        sectionByAction.TryGetValue((definition.Code, formIndex), out var section)
            ? section
            : null;

    /// <summary>
    /// 这张素材在「它归属的那张图集」里是第几格，以及那一格的矩形。
    ///
    /// 格子序号按**图集归属动作自己的素材列表**数（图集里只放它自己的图），
    /// 自己的图和借来的图走的是同一套算法。
    /// </summary>
    private static bool TryResolveAtlasCell(
        CharacterCard character,
        SequenceSourceImageResolution resolution,
        IReadOnlyDictionary<(string Code, int FormIndex), SequenceFrameSection> sectionByAction,
        IReadOnlyDictionary<string, UnrealBridgeSequenceAtlasInput> atlases,
        out UnrealBridgeSequenceAtlasInput? atlas,
        out AtlasSequenceFrame? placed)
    {
        atlas = null;
        placed = null;
        if (!SequenceActionCatalog.TryResolve(resolution.OwnerActionCode, out var ownerDefinition, out var ownerFormIndex) ||
            !atlases.TryGetValue(AtlasKey(ownerDefinition.Code, ownerFormIndex), out atlas) ||
            !sectionByAction.TryGetValue((ownerDefinition.Code, ownerFormIndex), out var ownerSection))
        {
            return false;
        }

        var cellIndex = 0;
        foreach (var own in SequenceActionFolderLayout
            .ResolveSourceImagePlan(character, ownerSection.Action, ownerSection.Frames)
            .Where(item => item.IsOwn))
        {
            cellIndex++;
            if (!string.Equals(own.FilePath, resolution.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return atlas.FramesByOrdinal.TryGetValue(cellIndex, out placed) && placed.Frame is not null;
        }

        return false;
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
