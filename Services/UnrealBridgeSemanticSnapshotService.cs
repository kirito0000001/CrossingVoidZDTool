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

        // 挂在角色动画源上、却不属于任何规范动作的序列。
        // 工具箱侧永远不会产出这些条目，所以它们只会以「删除候选」的形式出现。
        // 注意：第五步只把它们从动画源上解绑，不删资产——串错位置的序列往往仍是有用素材。
        var orphanSequences = candidate.SequenceFramesPreview.OrphanSequences;
        if (orphanSequences.Count > 0)
        {
            // 不产出分组表头条目：工具箱侧不可能有对应项，它自己会被判成一条"待删除"，
            // 于是分组标题混进删除候选里，计数比实际多一项。
            // 分组由子项的 SequenceGroupKey 合成，标题另有解析规则。
            foreach (var orphan in orphanSequences)
            {
                if (string.IsNullOrWhiteSpace(orphan.ObjectPath))
                {
                    continue;
                }

                Add(items,
                    SequenceFrameIdentity.BuildOrphanSequenceStableId(orphan.ObjectPath),
                    SequenceFrameIdentity.OrphanGroupStableId,
                    UnrealBridgeModule.SequenceFrames,
                    $"非规范序列 · {orphan.AssetName}",
                    orphan.ObjectPath,
                    Join(orphan.AssetName, SequenceFrameIdentity.NormalizeAssetClass(orphan.AssetClass)),
                    string.Empty,
                    orphan.AssetName);
            }
        }

        foreach (var action in candidate.SequenceFramesPreview.Actions.Where(action => action.HasData))
        {
            // 动作和帧的稳定 ID 与工具箱快照共用一套规则：动作 + 帧位置。
            // 之前这里把 Unreal 对象路径算进身份，发布改名后身份就变了，两侧再也配不上对。
            var variantCode = SequenceFrameIdentity.ResolveVariantCode(action.ActionCode, action.FormIndex);
            var actionId = SequenceFrameIdentity.BuildActionStableId(variantCode);
            Add(items, actionId, $"module:{UnrealBridgeModule.SequenceFrames}", UnrealBridgeModule.SequenceFrames,
                action.Title, candidate.SequenceFramesPreview.AnimMapsObjectPath,
                // 与工具箱动作节点共用同一份载荷，帧率取整后比较（工具箱只能产出整数帧率）。
                SequenceFrameIdentity.BuildActionPayload(
                    variantCode,
                    (int)Math.Round(action.FramesPerSecond <= 0 ? SequenceFrameService.DefaultFps : action.FramesPerSecond)));
            var frames = action.OrderedFrames.Count > 0 ? action.OrderedFrames : action.PreviewFrames;
            for (var index = 0; index < frames.Count; index++)
            {
                var frame = frames[index];
                Add(items, SequenceFrameIdentity.BuildFrameStableId(variantCode, index), actionId,
                    UnrealBridgeModule.SequenceFrames,
                    $"{action.Title} 第 {index + 1} 帧", frame.ObjectPath,
                    Join(action.ActionCode, action.FormIndex, index + 1, frame.IsBlank),
                    frame.ExportedFilePath, frame.AssetName);
            }

            // 该动作在 Unreal 里实际占用的资产。规范命名的那些会被差异比较过滤掉，
            // 剩下的（断了引用的旧 Sprite、旧 Flipbook、旧拼写的序列）就是需要清理的删除项。
            foreach (var owned in action.OwnedAssets ?? [])
            {
                if (string.IsNullOrWhiteSpace(owned.ObjectPath))
                {
                    continue;
                }

                Add(items, SequenceFrameIdentity.BuildOwnedAssetStableId(variantCode, owned.ObjectPath), actionId,
                    UnrealBridgeModule.SequenceFrames,
                    $"{action.Title} · {owned.AssetName}", owned.ObjectPath,
                    Join(action.ActionCode, action.FormIndex, owned.AssetName,
                        SequenceFrameIdentity.NormalizeAssetClass(owned.AssetClass)),
                    string.Empty, owned.AssetName);
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
