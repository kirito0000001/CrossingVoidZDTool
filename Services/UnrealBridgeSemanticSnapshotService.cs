using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CrossingVoidZDTool.Services;

internal sealed class UnrealBridgeSemanticSnapshotService
{
    public UnrealBridgeSnapshot Build(UnrealProjectSyncCharacterCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var items = new List<UnrealBridgeSnapshotItem>();
        if (candidate.CharacterInfo.HasItemData)
        {
            Add(items, "character:info", string.Empty, UnrealBridgeModule.CharacterInfo, "角色信息",
                candidate.CharacterInfo.ObjectPath,
                Join(candidate.CharacterInfo.Name, candidate.CharacterInfo.Description,
                    candidate.CharacterInfo.Speed, candidate.CharacterInfo.Health, candidate.CharacterInfo.Attack,
                    candidate.CharacterInfo.Anti));
        }

        foreach (var bucket in candidate.MaterialBuckets)
        {
            foreach (var asset in bucket.Assets)
            {
                var identity = CreateOriginIdentity(asset.ObjectPath);
                Add(items, $"material:{identity}", $"module:{UnrealBridgeModule.BaseMaterials}",
                    UnrealBridgeModule.BaseMaterials, asset.AssetName, asset.ObjectPath,
                    Join(bucket.Kind, asset.AssetName), asset.ExportedFilePath, asset.AssetName);
            }
        }

        for (var slotIndex = 0; slotIndex < candidate.SkillsPreview.CoreSlots.Count; slotIndex++)
        {
            AddSkillSlot(items, candidate.Code, candidate.SkillsPreview.CoreSlots[slotIndex], $"core:{slotIndex}");
        }

        AddSkillSlot(items, candidate.Code, candidate.SkillsPreview.SupportSkillSlot, "support");

        for (var linkIndex = 0; linkIndex < candidate.SkillsPreview.LinkSkills.Count; linkIndex++)
        {
            var link = candidate.SkillsPreview.LinkSkills[linkIndex];
            AddSkillSlot(
                items,
                candidate.Code,
                link.SkillSlot,
                $"link:{linkIndex}:{link.SupportCharacterCode}:{link.SkillIndex}");
        }

        foreach (var action in candidate.SequenceFramesPreview.Actions.Where(action => action.HasData))
        {
            var actionIdentity = CreateOriginIdentity($"{candidate.Code}|sequence|{action.ActionCode}|{action.FormIndex}");
            var actionId = $"sequence:{actionIdentity}";
            Add(items, actionId, $"module:{UnrealBridgeModule.SequenceFrames}", UnrealBridgeModule.SequenceFrames,
                action.Title, candidate.SequenceFramesPreview.AnimMapsObjectPath,
                Join(action.ActionCode, action.FormIndex, action.FramesPerSecond));
            var frames = action.OrderedFrames.Count > 0 ? action.OrderedFrames : action.PreviewFrames;
            for (var index = 0; index < frames.Count; index++)
            {
                var frame = frames[index];
                var identity = CreateOriginIdentity(
                    $"{candidate.Code}|sequence-frame|{action.ActionCode}|{action.FormIndex}|{index}|{frame.ObjectPath}");
                Add(items, $"sequence-frame:{identity}", actionId, UnrealBridgeModule.SequenceFrames,
                    $"{action.Title} 第 {index + 1} 帧", frame.ObjectPath,
                    Join(action.ActionCode, action.FormIndex, index + 1, frame.IsBlank),
                    frame.ExportedFilePath, frame.AssetName);
            }
        }

        foreach (var buff in candidate.BuffsPreview.Buffs)
        {
            var identity = CreateOriginIdentity(buff.ObjectPath);
            Add(items, $"buff:{identity}", $"module:{UnrealBridgeModule.Buffs}", UnrealBridgeModule.Buffs,
                buff.Title, buff.ObjectPath,
                Join(buff.AssetName, buff.Title, buff.Description, buff.DamageType, buff.GainType,
                    buff.Count, buff.CompleteCount, buff.Power, buff.CompletePower),
                buff.IconExportedFilePath, buff.AssetName);
        }

        foreach (var bucket in candidate.VoiceBuckets)
        {
            foreach (var asset in bucket.Assets)
            {
                var identity = CreateOriginIdentity(asset.ObjectPath);
                Add(items, $"voice:{identity}", $"module:{UnrealBridgeModule.Voices}",
                    UnrealBridgeModule.Voices, asset.AssetName, asset.ObjectPath,
                    Join(bucket.Kind, asset.AssetName), asset.ExportedFilePath, asset.AssetName);
            }
        }

        return new UnrealBridgeSnapshot(candidate.Code, items);
    }

    public static string CreateOriginIdentity(string value)
    {
        var normalized = (value ?? string.Empty).Trim().Replace('\\', '/').ToLowerInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    private static void AddSkillSlot(
        ICollection<UnrealBridgeSnapshotItem> items,
        string characterCode,
        UnrealProjectSyncSkillSlotPreview slot,
        string suffix = "")
    {
        for (var index = 0; index < slot.Stages.Count; index++)
        {
            var stage = slot.Stages[index];
            var identity = CreateOriginIdentity($"{characterCode}|skill|{suffix}|{slot.SlotKey}|{index}");
            Add(items, $"skill:{identity}", $"skill:{slot.SlotKey}", UnrealBridgeModule.Skills,
                stage.Title, string.Empty,
                Join(slot.SlotKey, suffix, index, stage.PositionName, stage.TrueName, stage.Description,
                    stage.PtCost, stage.AttackCapacity, stage.AutoPriority, stage.SkillState,
                    stage.GuardState, stage.GuardValue), stage.IconExportedFilePath, stage.TrueName);
        }
    }

    private static void Add(
        ICollection<UnrealBridgeSnapshotItem> items,
        string stableId,
        string parentStableId,
        UnrealBridgeModule module,
        string displayName,
        string objectPath,
        string payload,
        string assetPath = "",
        string normalizedName = "")
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(payload));
        if (!string.IsNullOrWhiteSpace(assetPath) && File.Exists(assetPath))
        {
            hash.AppendData(File.ReadAllBytes(assetPath));
        }

        items.Add(new UnrealBridgeSnapshotItem(
            stableId,
            parentStableId,
            module,
            displayName,
            Convert.ToHexString(hash.GetHashAndReset()),
            payload,
            assetPath,
            objectPath,
            string.IsNullOrWhiteSpace(objectPath) ? string.Empty : $"object:{objectPath.ToLowerInvariant()}",
            string.Empty,
            normalizedName));
    }

    private static string Join(params object?[] values) =>
        string.Join("\u001f", values.Select(value => value?.ToString() ?? string.Empty));
}
