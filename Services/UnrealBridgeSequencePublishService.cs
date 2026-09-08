using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

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
    public string TextureAssetName { get; set; } = string.Empty;
    public string SpriteAssetName { get; set; } = string.Empty;
}

internal sealed class UnrealBridgeSequencePublishService
{
    private static readonly Regex FrameDisplayNamePattern = new(@"^(.*?)\s*第\s*\d+\s*帧\s*$");

    /// <summary>本次因为没有序列帧数据而被跳过的动作，供调用方提示用户。</summary>
    public IReadOnlyList<string> SkippedActionCodes { get; private set; } = [];

    public UnrealBridgeSequenceSyncPlan BuildSequenceSyncPlan(
        CharacterCard character,
        string projectPath,
        IReadOnlyList<UnrealBridgeChange> selectedSequenceChanges)
    {
        ArgumentNullException.ThrowIfNull(character);
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
                HasStaleAssetSelection = true
            };
            // 虚幻侧的帧序号按整条序列的位置从 0 开始编号，空白帧同样占一个位置，
            // 这样资产编号和 Flipbook 的关键帧下标始终一一对应。
            var orderedFrames = section.Frames.OrderBy(frame => frame.Index).ToArray();
            action.Frames = orderedFrames
                .Select((frame, ordinal) => new UnrealBridgeSequenceSyncFrame
                {
                    Index = frame.Index,
                    Ordinal = ordinal,
                    FilePath = frame.IsBlank ? string.Empty : Path.GetFullPath(frame.FilePath),
                    DurationFrames = Math.Max(1, frame.DurationFrames),
                    IsBlank = frame.IsBlank,
                    VoiceFileName = frame.VoiceFileName,
                    TextureAssetName = frame.IsBlank
                        ? string.Empty
                        : SequenceActionCatalog.GetFrameTextureName(definition, formIndex, ordinal, orderedFrames.Length),
                    SpriteAssetName = frame.IsBlank
                        ? string.Empty
                        : SequenceActionCatalog.GetFrameSpriteName(definition, formIndex, ordinal, orderedFrames.Length)
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
