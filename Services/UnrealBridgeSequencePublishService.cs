using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
}

internal sealed class UnrealBridgeSequenceSyncAction
{
    public string ActionCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int Fps { get; set; } = SequenceFrameService.DefaultFps;
    public string BlueprintProperty { get; set; } = string.Empty;
    public string AnimMapsEntryName { get; set; } = string.Empty;
    public string TargetSequencePath { get; set; } = string.Empty;
    public string TargetMaterialFolder { get; set; } = string.Empty;
    public List<UnrealBridgeSequenceSyncFrame> Frames { get; set; } = [];
}

internal sealed class UnrealBridgeSequenceSyncFrame
{
    public int Index { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public int DurationFrames { get; set; } = 1;
    public bool IsBlank { get; set; }
    public string VoiceFileName { get; set; } = string.Empty;
}

internal sealed class UnrealBridgeSequencePublishService
{
    private static readonly Dictionary<string, (string DisplayName, string Property, string AnimMap)> Actions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Click"] = ("点击", "ClickSeq", ""), ["Death"] = ("死亡", "DeathAnim", ""),
        ["DefAtk"] = ("守备反击", "DefAttackSeq", ""), ["Defeat"] = ("失败", "DefeatedAnim", ""),
        ["Defence"] = ("守备防御", "DefSeq", ""), ["Defense"] = ("守备防御", "DefSeq", ""),
        ["Dodge"] = ("守备闪避", "DodgeSeq", ""), ["FlyDown"] = ("坠落", "", "FlyDown"),
        ["Flydown"] = ("坠落", "", "FlyDown"), ["Flying"] = ("飞行", "", "Flying"),
        ["FlyStart"] = ("击飞", "", "FlyStart"), ["Flystart"] = ("击飞", "", "FlyStart"),
        ["Idle"] = ("站街", "", "Idle"), ["KO"] = ("终结技", "", ""),
        ["Land"] = ("落地", "", "Land"), ["Move"] = ("移动", "", "Move"),
        ["OnDamage"] = ("受击", "OnDamageSeq", ""), ["Ondm"] = ("受击", "OnDamageSeq", ""),
        ["Sk1"] = ("一技能", "", ""), ["Sk2"] = ("二技能", "", ""),
        ["StandUP"] = ("站起", "", "StandUP"), ["Standup"] = ("站起", "", "StandUP"),
        ["Sub"] = ("护援技", "", ""), ["Victory"] = ("胜利", "VictorAnim", "")
    };

    public UnrealBridgeSequenceSyncPlan BuildSequenceSyncPlan(
        CharacterCard character,
        string projectPath,
        IReadOnlyList<UnrealBridgeChange> selectedSequenceChanges)
    {
        ArgumentNullException.ThrowIfNull(character);
        var selectedCodes = selectedSequenceChanges
            .Where(change => change.IsSelected && change.Module == UnrealBridgeModule.SequenceFrames)
            .Select(ResolveActionCode)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (selectedCodes.Length == 0) throw new InvalidOperationException("没有选择需要同步的序列动作。");

        var data = new SequenceFrameService().LoadData(character);
        var sections = new SequenceFrameService().LoadSections(character, new CharacterSkillsService().Load(character));
        var sectionByCode = sections.ToDictionary(section => section.Action.Code, StringComparer.OrdinalIgnoreCase);
        var project = Path.GetFullPath(projectPath);
        var root = $"/Game/GameActor2D/{character.Code}";
        var plan = new UnrealBridgeSequenceSyncPlan
        {
            CharacterCode = character.Code,
            UnrealProjectPath = project,
            CharacterBlueprintPath = $"{root}/{character.Code}.{character.Code}",
            AnimMapsPath = $"{root}/{character.Code}_AnimMaps.{character.Code}_AnimMaps"
        };
        foreach (var code in selectedCodes)
        {
            if (!Actions.TryGetValue(code, out var identity)) throw new InvalidOperationException($"不支持的序列动作：{code}");
            var canonical = CanonicalCode(code);
            var section = FindSection(sections, sectionByCode, canonical, code);
            if (section is null) throw new InvalidOperationException($"工具箱中找不到动作 {code} 的序列数据。");
            var fps = data.ActionSettings.FirstOrDefault(item => string.Equals(item.ActionCode, section.Action.Code, StringComparison.OrdinalIgnoreCase))?.Fps ?? SequenceFrameService.DefaultFps;
            var action = new UnrealBridgeSequenceSyncAction
            {
                ActionCode = canonical, DisplayName = identity.DisplayName, Fps = Math.Max(1, fps), BlueprintProperty = identity.Property,
                AnimMapsEntryName = identity.AnimMap,
                // TargetSequencePath is an asset path; the action folder must not be repeated.
                TargetSequencePath = $"{root}/AnimSequences/{SequenceFolder(canonical)}",
                TargetMaterialFolder = $"{root}/Material/{MaterialFolder(canonical)}"
            };
            action.Frames = section.Frames.OrderBy(frame => frame.Index).Select(frame => new UnrealBridgeSequenceSyncFrame
            {
                Index = frame.Index, FilePath = frame.IsBlank ? string.Empty : Path.GetFullPath(frame.FilePath), DurationFrames = Math.Max(1, frame.DurationFrames), IsBlank = frame.IsBlank, VoiceFileName = frame.VoiceFileName
            }).ToList();
            plan.Actions.Add(action);
        }
        return plan;
    }

    public void Save(string path, UnrealBridgeSequenceSyncPlan plan)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(plan, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }

    private static string ResolveActionCode(UnrealBridgeChange change)
    {
        var payload = change.ToolboxItem?.PayloadJson ?? change.UnrealItem?.PayloadJson ?? string.Empty;
        var actionCode = NormalizeActionCode(ExtractActionCode(payload));
        if (!string.IsNullOrWhiteSpace(actionCode)) return actionCode;
        if (change.SequenceGroupKey.StartsWith("sequence:", StringComparison.OrdinalIgnoreCase))
            return NormalizeActionCode(change.SequenceGroupKey[9..]);
        var displayName = change.DisplayName ?? string.Empty;
        var match = Regex.Match(displayName, @"^(.*?)\s*第\s*\d+\s*帧\s*$");
        return match.Success ? NormalizeActionCode(match.Groups[1].Value.Trim()) : string.Empty;
    }

    private static SequenceFrameSection? FindSection(
        IReadOnlyList<SequenceFrameSection> sections,
        IReadOnlyDictionary<string, SequenceFrameSection> sectionByCode,
        string canonical,
        string requested)
    {
        if (sectionByCode.TryGetValue(canonical, out var exact)) return exact;
        if (sectionByCode.TryGetValue(requested, out var requestedSection)) return requestedSection;
        var aliases = canonical.Equals("OnDamage", StringComparison.OrdinalIgnoreCase)
            ? new[] { "Ondm", "OnDM", "OnDamage" }
            : canonical.Equals("Defence", StringComparison.OrdinalIgnoreCase)
                ? new[] { "Defense", "Defence" }
                : canonical.Equals("FlyDown", StringComparison.OrdinalIgnoreCase)
                    ? new[] { "Flydown", "FlyDown" }
                    : canonical.Equals("FlyStart", StringComparison.OrdinalIgnoreCase)
                        ? new[] { "Flystart", "FlyStart" }
                        : canonical.Equals("StandUP", StringComparison.OrdinalIgnoreCase)
                            ? new[] { "Standup", "StandUP" }
                            : new[] { canonical };
        return aliases.Select(alias => sections.FirstOrDefault(item => string.Equals(item.Action.Code, alias, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(item => item is not null);
    }

    private static string NormalizeActionCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var candidate = value.Trim();
        var exact = Actions.Keys.FirstOrDefault(key => string.Equals(key, candidate, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;
        var prefix = candidate.Split(new[] { '-', '_', '|', ':' }, 2)[0];
        return Actions.Keys.FirstOrDefault(key => string.Equals(key, prefix, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
    }

    private static string ExtractActionCode(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return string.Empty;
        var trimmed = payload.Trim();
        if (trimmed.StartsWith("{"))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                if (doc.RootElement.TryGetProperty("actionCode", out var value) && value.ValueKind == JsonValueKind.String)
                    return value.GetString() ?? string.Empty;
            }
            catch (JsonException) { }
        }
        var parts = trimmed.Split('|', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : string.Empty;
    }

    private static string CanonicalCode(string code) => code.Equals("Ondm", StringComparison.OrdinalIgnoreCase) ? "OnDamage" : code.Equals("Flydown", StringComparison.OrdinalIgnoreCase) ? "FlyDown" : code.Equals("Flystart", StringComparison.OrdinalIgnoreCase) ? "FlyStart" : code.Equals("Standup", StringComparison.OrdinalIgnoreCase) ? "StandUP" : code.Equals("Defense", StringComparison.OrdinalIgnoreCase) ? "Defence" : code;
    private static string SequenceFolder(string code) => code switch
    {
        "DefAtk" => "Defatk",
        "FlyDown" => "FlyDown",
        "FlyStart" => "FlyStart",
        "OnDamage" => "Ondm",
        "StandUP" => "StandUP",
        _ => code
    };

    private static string MaterialFolder(string code) => code switch
    {
        "OnDamage" => "OnDM",
        _ => code
    };
}
