using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace CrossingVoidZDTool.Services;

internal sealed class UnrealBridgeDraftImportService
{
    private readonly CharacterWorkspaceService _workspaceService;
    private readonly UnrealProjectSyncService _semanticImportService;

    public UnrealBridgeDraftImportService()
        : this(new CharacterWorkspaceService(), new UnrealProjectSyncService())
    {
    }

    internal UnrealBridgeDraftImportService(
        CharacterWorkspaceService workspaceService,
        UnrealProjectSyncService semanticImportService)
    {
        _workspaceService = workspaceService;
        _semanticImportService = semanticImportService;
    }

    public UnrealBridgeDraftImportResult Import(
        string projectRootPath,
        UnrealProjectSyncCharacterCandidate candidate)
    {
        var selectedStableIds = new UnrealBridgeSemanticSnapshotService().Build(candidate).Items
            .Select(item => item.StableId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Import(projectRootPath, candidate, selectedStableIds);
    }

    public UnrealBridgeDraftImportResult Import(
        string projectRootPath,
        UnrealProjectSyncCharacterCandidate candidate,
        IReadOnlySet<string> selectedStableIds)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(selectedStableIds);
        if (selectedStableIds.Count == 0)
        {
            throw new InvalidOperationException("请至少选择一个需要导入的项目。");
        }
        var completedPath = Path.Combine(Path.GetFullPath(projectRootPath), "Completed", candidate.Code);
        if (Directory.Exists(completedPath))
        {
            throw new InvalidOperationException(
                $"角色 {candidate.Code} 已位于 Completed。请先在零境角色台选择继续编辑，再从虚幻导入。\n{completedPath}");
        }

        var ensureResult = _workspaceService.EnsureCharacterByCode(
            projectRootPath,
            candidate.Code,
            candidate.DisplayName);
        var character = ensureResult.Character;
        if (character.IsCompleted)
        {
            throw new InvalidOperationException($"不能把虚幻内容直接写入已完成角色：{character.Code}。");
        }

        CharacterBackupEntry? rollbackBackup = null;
        if (!ensureResult.CreatedNewFolder)
        {
            rollbackBackup = _workspaceService.BackupCharacter(
                character,
                "从虚幻导入前自动保护",
                CharacterBackupKinds.Automatic);
        }

        try
        {
            var importedModules = new List<UnrealBridgeModule>();
            if (selectedStableIds.Contains("character:info") && candidate.CharacterInfo.HasItemData)
            {
                _semanticImportService.SyncCharacterInfoToToolbox(character, candidate);
                importedModules.Add(UnrealBridgeModule.CharacterInfo);
            }

            var importedMaterialCount = ImportSelectedMaterials(
                character,
                candidate,
                selectedStableIds,
                out var removedDuplicateCount);
            if (importedMaterialCount > 0)
            {
                importedModules.Add(UnrealBridgeModule.BaseMaterials);
            }

            var importedSkillCount = ImportSelectedSkills(character, candidate, selectedStableIds);
            if (importedSkillCount > 0)
            {
                importedModules.Add(UnrealBridgeModule.Skills);
            }

            var importedVoicePaths = ImportVoiceBuckets(character, candidate, selectedStableIds);
            if (importedVoicePaths.Count > 0)
            {
                importedModules.Add(UnrealBridgeModule.Voices);
            }

            var selectedActions = candidate.SequenceFramesPreview.Actions
                .Where(action => selectedStableIds.Contains(GetSequenceStableId(candidate.Code, action)))
                .ToArray();
            if (selectedActions.Length > 0 && candidate.SequenceFramesPreview.HasData)
            {
                foreach (var action in selectedActions)
                {
                    _semanticImportService.SyncSequenceActionToToolbox(character, action);
                }

                AssignImportedSequenceIdentities(character, candidate, selectedStableIds);
                BindImportedSequenceVoices(character, candidate, importedVoicePaths, selectedStableIds);
                importedModules.Add(UnrealBridgeModule.SequenceFrames);
            }

            var selectedBuffs = candidate.BuffsPreview.Buffs
                .Where(buff => selectedStableIds.Contains(GetBuffStableId(buff)))
                .ToArray();
            if (selectedBuffs.Length > 0 && candidate.BuffsPreview.HasData)
            {
                foreach (var buff in selectedBuffs)
                {
                    _semanticImportService.SyncBuffToToolbox(character, buff);
                }

                AssignImportedBuffIdentities(character, candidate, selectedStableIds);
                importedModules.Add(UnrealBridgeModule.Buffs);
            }

            if (selectedActions.Length > 0)
            {
                ImportSharedSequenceSounds(projectRootPath, candidate, selectedStableIds);
            }

            return new UnrealBridgeDraftImportResult(
                _workspaceService.RefreshCharacterCard(character),
                ensureResult.CreatedNewFolder,
                importedModules,
                removedDuplicateCount);
        }
        catch (Exception importException)
        {
            try
            {
                RollBackFailedImport(projectRootPath, character, ensureResult.CreatedNewFolder, rollbackBackup);
            }
            catch (Exception rollbackException)
            {
                throw new InvalidOperationException(
                    $"角色 {candidate.Code} 导入失败，且自动恢复导入前状态也失败。",
                    new AggregateException(importException, rollbackException));
            }

            throw;
        }
    }

    private void RollBackFailedImport(
        string projectRootPath,
        CharacterCard character,
        bool createdNewFolder,
        CharacterBackupEntry? rollbackBackup)
    {
        if (!createdNewFolder)
        {
            if (rollbackBackup is null)
            {
                throw new InvalidOperationException("已有 Draft 导入失败，但没有可用于自动恢复的备份。");
            }

            _workspaceService.RestoreCharacterBackup(character, rollbackBackup);
            return;
        }

        var expectedDraftPath = Path.GetFullPath(Path.Combine(projectRootPath, "Draft", character.Code));
        var actualCharacterPath = Path.GetFullPath(character.FolderPath);
        if (!string.Equals(expectedDraftPath, actualCharacterPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"拒绝清理不在预期 Draft 位置的半成品：{actualCharacterPath}");
        }

        if (Directory.Exists(actualCharacterPath))
        {
            Directory.Delete(actualCharacterPath, recursive: true);
        }
    }

    private static int ImportSelectedMaterials(
        CharacterCard character,
        UnrealProjectSyncCharacterCandidate candidate,
        IReadOnlySet<string> selectedStableIds,
        out int removedDuplicateCount)
    {
        var materialService = new BaseMaterialService();
        var identityService = new UnrealBridgeToolboxIdentityService();
        var importedCount = 0;
        removedDuplicateCount = 0;
        foreach (var bucket in candidate.MaterialBuckets)
        {
            if (!Enum.TryParse<BaseMaterialKind>(bucket.Kind, ignoreCase: true, out var kind))
            {
                kind = BaseMaterialKind.OtherImage;
            }

            for (var index = 0; index < bucket.Assets.Count; index++)
            {
                var asset = bucket.Assets[index];
                if (!asset.HasPreview || !selectedStableIds.Contains(GetMaterialStableId(asset)))
                {
                    continue;
                }

                var originIdentity = UnrealBridgeSemanticSnapshotService.CreateOriginIdentity(asset.ObjectPath);
                var imported = ImportOrUpdateMaterial(
                    character,
                    materialService,
                    identityService,
                    kind,
                    asset.ExportedFilePath,
                    originIdentity,
                    index + 1);
                var contentHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(imported.FilePath)));
                identityService.Assign(
                    character,
                    new UnrealBridgeToolboxFileCandidate(UnrealBridgeModule.BaseMaterials, imported.FilePath, contentHash),
                    originIdentity);
                removedDuplicateCount += RemoveUnclaimedMaterialDuplicates(
                    character,
                    materialService,
                    identityService,
                    kind,
                    imported.FilePath,
                    contentHash);
                importedCount++;
            }
        }

        return importedCount;
    }

    private static BaseMaterialItem ImportOrUpdateMaterial(
        CharacterCard character,
        BaseMaterialService materialService,
        UnrealBridgeToolboxIdentityService identityService,
        BaseMaterialKind kind,
        string sourcePath,
        string originIdentity,
        int sourceIndex)
    {
        if (kind == BaseMaterialKind.BattleAvatar)
        {
            return materialService.ImportAtIndex(character, kind, sourcePath, sourceIndex);
        }

        if (!identityService.TryResolveAssignedPath(
                character,
                UnrealBridgeModule.BaseMaterials,
                originIdentity,
                out var assignedPath))
        {
            return materialService.ImportAndCrop(character, kind, sourcePath);
        }

        var assignedItem = materialService.LoadSections(character)
            .Single(section => section.Spec.Kind == kind)
            .Items
            .FirstOrDefault(item => string.Equals(item.FilePath, assignedPath, StringComparison.OrdinalIgnoreCase));
        return assignedItem is null
            ? materialService.ImportAndCrop(character, kind, sourcePath)
            : materialService.Repair(character, kind, sourcePath, assignedItem.Index);
    }

    private static int RemoveUnclaimedMaterialDuplicates(
        CharacterCard character,
        BaseMaterialService materialService,
        UnrealBridgeToolboxIdentityService identityService,
        BaseMaterialKind kind,
        string keepPath,
        string contentHash)
    {
        var assignedPaths = identityService.GetAssignedPaths(character, UnrealBridgeModule.BaseMaterials);
        var duplicates = materialService.LoadSections(character)
            .Single(section => section.Spec.Kind == kind)
            .Items
            .Where(item =>
                !string.Equals(item.FilePath, keepPath, StringComparison.OrdinalIgnoreCase) &&
                !assignedPaths.Contains(item.FilePath) &&
                string.Equals(
                    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(item.FilePath))),
                    contentHash,
                    StringComparison.OrdinalIgnoreCase))
            .Select(item => item.FilePath)
            .ToArray();
        foreach (var duplicatePath in duplicates)
        {
            File.Delete(duplicatePath);
        }

        if (duplicates.Length > 0)
        {
            _ = materialService.LoadSections(character);
        }

        return duplicates.Length;
    }

    private int ImportSelectedSkills(
        CharacterCard character,
        UnrealProjectSyncCharacterCandidate candidate,
        IReadOnlySet<string> selectedStableIds)
    {
        var importedCount = 0;
        // 身份后缀必须和 UnrealBridgeSemanticSnapshotService 用的一模一样：
        // 主技能是 core:{下标}、护援技是 support、连携技是 link:{下标}:{搭档}:{序号}。
        // 这里以前一律传空串，于是选中的技能永远匹配不上快照里的稳定身份，
        // 结果是「从虚幻导入角色时技能一条都进不来」，而且不报任何错。
        foreach (var (slot, suffix) in EnumerateSkillSlotsWithSuffix(candidate))
        {
            for (var stageIndex = 0; stageIndex < slot.Stages.Count; stageIndex++)
            {
                if (selectedStableIds.Contains(GetSkillStableId(candidate.Code, slot.SlotKey, suffix, stageIndex)))
                {
                    importedCount += _semanticImportService.SyncSkillStageToToolbox(
                        character, candidate, slot, stageIndex, suffix);
                }
            }
        }

        for (var linkIndex = 0; linkIndex < candidate.SkillsPreview.LinkSkills.Count; linkIndex++)
        {
            var link = candidate.SkillsPreview.LinkSkills[linkIndex];
            var suffix = $"link:{linkIndex}:{link.SupportCharacterCode}:{link.SkillIndex}";
            if (link.SkillSlot.Stages.Count > 0 &&
                selectedStableIds.Contains(GetSkillStableId(candidate.Code, link.SkillSlot.SlotKey, suffix, 0)))
            {
                importedCount += _semanticImportService.SyncLinkSkillToToolbox(character, candidate, link, suffix);
            }
        }

        if (importedCount > 0)
        {
            AssignImportedSkillIdentities(character, candidate, selectedStableIds);
        }

        return importedCount;
    }

    private static IReadOnlyDictionary<string, string> ImportVoiceBuckets(
        CharacterCard character,
        UnrealProjectSyncCharacterCandidate candidate,
        IReadOnlySet<string> selectedStableIds)
    {
        var sequenceKinds = new Dictionary<string, VoiceMaterialKind>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in candidate.SequenceFramesPreview.Actions)
        {
            var kind = UnrealBridgeVoiceClassification.ClassifySequenceAction(action);
            foreach (var notify in action.SequenceSounds.Where(notify => notify.IsCharacterVoice))
            {
                var objectPath = NormalizeObjectPath(notify.SoundObjectPath);
                if (!string.IsNullOrWhiteSpace(objectPath) &&
                    (!sequenceKinds.TryGetValue(objectPath, out var existing) || existing == VoiceMaterialKind.Other))
                {
                    sequenceKinds[objectPath] = kind;
                }
            }
        }

        var allSources = candidate.VoiceBuckets
            .SelectMany(bucket => bucket.Assets.Select(asset => (Bucket: bucket, Asset: asset)))
            .Where(item => item.Asset.HasPreview)
            .GroupBy(item => NormalizeObjectPath(item.Asset.ObjectPath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(item =>
            {
                var fallbackKind = Enum.TryParse<VoiceMaterialKind>(item.Bucket.Kind, ignoreCase: true, out var parsed)
                    ? parsed
                    : VoiceMaterialKind.Other;
                var objectPath = NormalizeObjectPath(item.Asset.ObjectPath);
                return (
                    // 反推得 Other 时要退回上一层的分类结论，理由同 UnrealProjectSyncService：
                    // 十九个标准动作里有十个反推不出分类，一律返回 Other。
                    Kind: sequenceKinds.TryGetValue(objectPath, out var sequenceKind)
                        && sequenceKind != VoiceMaterialKind.Other
                        ? sequenceKind
                        : fallbackKind,
                    Asset: item.Asset,
                    ObjectPath: objectPath);
            })
            .ToArray();
        var sources = allSources
            .Where(item => selectedStableIds.Contains(GetVoiceStableId(item.Asset)))
            .ToArray();
        if (sources.Length == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var service = new VoiceMaterialService();
        var identityService = new UnrealBridgeToolboxIdentityService();
        var importedPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in sources.GroupBy(item => item.Kind).OrderBy(group => group.Key))
        {
            var orderedSources = group
                .OrderBy(item => item.Asset.AssetName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var sourcePaths = orderedSources.Select(item => item.Asset.ExportedFilePath).ToArray();
            var imported = allSources.Count(item => item.Kind == group.Key) == orderedSources.Length
                ? service.ReplaceWithVoices(character, group.Key, sourcePaths)
                : service.ImportMany(character, group.Key, sourcePaths);
            for (var index = 0; index < Math.Min(imported.Count, orderedSources.Length); index++)
            {
                var source = orderedSources[index];
                var filePath = imported[index].FilePath;
                importedPaths[source.ObjectPath] = filePath;
                var contentHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(filePath)));
                identityService.Assign(
                    character,
                    new UnrealBridgeToolboxFileCandidate(UnrealBridgeModule.Voices, filePath, contentHash),
                    UnrealBridgeSemanticSnapshotService.CreateOriginIdentity(source.Asset.ObjectPath));
            }
        }

        return importedPaths;
    }

    private static void BindImportedSequenceVoices(
        CharacterCard character,
        UnrealProjectSyncCharacterCandidate candidate,
        IReadOnlyDictionary<string, string> importedVoicePaths,
        IReadOnlySet<string> selectedStableIds)
    {
        if (importedVoicePaths.Count == 0)
        {
            return;
        }

        var service = new SequenceFrameService();
        var skills = new CharacterSkillsService().Load(character);
        var toolboxActions = SequenceFrameService.BuildActions(
            skills,
            new CharacterFormService().GetFormLimit(character));
        var sections = service.LoadSections(character, skills)
            .ToDictionary(section => section.Action.Code, StringComparer.OrdinalIgnoreCase);
        foreach (var sourceAction in candidate.SequenceFramesPreview.Actions.Where(action =>
                     selectedStableIds.Contains(GetSequenceStableId(candidate.Code, action))))
        {
            var targetAction = ResolveToolboxSequenceAction(toolboxActions, sourceAction);
            if (targetAction is null || !sections.TryGetValue(targetAction.Code, out var section))
            {
                continue;
            }

            var frames = section.Frames;
            foreach (var notify in sourceAction.SequenceSounds
                         .Where(notify => notify.IsCharacterVoice)
                         .OrderBy(notify => notify.FrameIndex)
                         .ThenBy(notify => notify.TrackIndex))
            {
                if (notify.FrameIndex < 0 || notify.FrameIndex >= frames.Count ||
                    !importedVoicePaths.TryGetValue(NormalizeObjectPath(notify.SoundObjectPath), out var voicePath))
                {
                    continue;
                }

                frames = service.SetFrameVoice(character, targetAction, frames[notify.FrameIndex], voicePath);
            }
        }
    }

    private static string NormalizeObjectPath(string value)
    {
        return (value ?? string.Empty).Trim().Replace('\\', '/');
    }

    private static void ImportSharedSequenceSounds(
        string projectRootPath,
        UnrealProjectSyncCharacterCandidate candidate,
        IReadOnlySet<string> selectedStableIds)
    {
        var service = new ProjectSharedMaterialService();
        foreach (var action in candidate.SequenceFramesPreview.Actions.Where(action =>
                     selectedStableIds.Contains(GetSequenceStableId(candidate.Code, action))))
        {
            foreach (var notify in action.SequenceSounds.Where(notify => !notify.IsCharacterVoice))
            {
                var usage = new ProjectSharedMaterialUsage(candidate.Code, action.ActionCode, notify.FrameIndex);
                if (!string.IsNullOrWhiteSpace(notify.ExportedFilePath) && File.Exists(notify.ExportedFilePath))
                {
                    service.Import(
                        projectRootPath,
                        ProjectSharedMaterialCategory.BattleEffects,
                        notify.ExportedFilePath,
                        notify.SoundObjectPath,
                        usage);
                }
                else
                {
                    service.RegisterReference(
                        projectRootPath,
                        ProjectSharedMaterialCategory.BattleEffects,
                        notify.SoundAssetName,
                        notify.SoundAssetClass,
                        notify.SoundObjectPath,
                        usage);
                }
            }
        }
    }

    private static void AssignImportedSequenceIdentities(
        CharacterCard character,
        UnrealProjectSyncCharacterCandidate candidate,
        IReadOnlySet<string> selectedStableIds)
    {
        var service = new SequenceFrameService();
        var skills = new CharacterSkillsService().Load(character);
        var toolboxActions = SequenceFrameService.BuildActions(
            skills,
            new CharacterFormService().GetFormLimit(character));
        foreach (var sourceAction in candidate.SequenceFramesPreview.Actions.Where(action =>
                     action.HasFramePreview && selectedStableIds.Contains(GetSequenceStableId(candidate.Code, action))))
        {
            var targetAction = ResolveToolboxSequenceAction(toolboxActions, sourceAction);
            if (targetAction is null)
            {
                continue;
            }

            var frames = sourceAction.OrderedFrames.Count > 0
                ? sourceAction.OrderedFrames
                : sourceAction.PreviewFrames;
            var identities = frames.Select((frame, index) =>
                UnrealBridgeSemanticSnapshotService.CreateOriginIdentity(
                    $"{candidate.Code}|sequence-frame|{sourceAction.ActionCode}|{sourceAction.FormIndex}|{index}|{frame.ObjectPath}"))
                .ToArray();
            service.AssignFrameSyncIds(character, targetAction, identities);
        }
    }

    /// <summary>
    /// 把 Unreal 侧的动作对应到工具箱动作。原来这里手写了一张 Defence→Defense、OnDamage→Ondm 的表，
    /// 而工具箱的动作代号其实是 Defence / OnDamage，映射出来的代号在工具箱里根本不存在，
    /// 这些动作会被静默跳过。现在统一交给 SequenceActionCatalog 解析。
    /// </summary>
    private static SequenceFrameAction? ResolveToolboxSequenceAction(
        IReadOnlyList<SequenceFrameAction> toolboxActions,
        UnrealProjectSyncSequenceActionPreview sourceAction)
    {
        var variantCode = SequenceFrameIdentity.ResolveVariantCode(sourceAction.ActionCode, sourceAction.FormIndex);
        var actionKey = SequenceActionCatalog.NormalizeActionKey(variantCode);
        return toolboxActions.FirstOrDefault(action =>
            string.Equals(
                SequenceActionCatalog.NormalizeActionKey(
                    SequenceFrameIdentity.ResolveVariantCode(action.Code, action.FormIndex)),
                actionKey,
                StringComparison.OrdinalIgnoreCase));
    }

    private static void AssignImportedSkillIdentities(
        CharacterCard character,
        UnrealProjectSyncCharacterCandidate candidate,
        IReadOnlySet<string> selectedStableIds)
    {
        var service = new CharacterSkillsService();
        var data = service.Load(character);
        foreach (var (slot, suffix) in EnumerateSkillSlotsWithSuffix(candidate))
        {
            var target = ResolveSkillCollection(data, slot.SlotKey);
            if (target is null)
            {
                continue;
            }

            var count = Math.Min(target.Count, slot.Stages.Count);
            for (var index = 0; index < count; index++)
            {
                if (!selectedStableIds.Contains(GetSkillStableId(candidate.Code, slot.SlotKey, suffix, index)))
                {
                    continue;
                }

                target[index].SyncId = UnrealBridgeSemanticSnapshotService.CreateOriginIdentity(
                    $"{candidate.Code}|skill|{suffix}|{slot.SlotKey}|{index}");
            }
        }

        for (var index = 0; index < Math.Min(data.ComboSkills.Count, candidate.SkillsPreview.LinkSkills.Count); index++)
        {
            var link = candidate.SkillsPreview.LinkSkills[index];
            var suffix = $"link:{index}:{link.SupportCharacterCode}:{link.SkillIndex}";
            if (!selectedStableIds.Contains(GetSkillStableId(candidate.Code, link.SkillSlot.SlotKey, suffix, 0)))
            {
                continue;
            }

            data.ComboSkills[index].SyncId = UnrealBridgeSemanticSnapshotService.CreateOriginIdentity(
                $"{candidate.Code}|skill|{suffix}|{link.SkillSlot.SlotKey}|0");
        }

        service.Save(character, data);
    }

    private static System.Collections.ObjectModel.ObservableCollection<CharacterSkillEntry>? ResolveSkillCollection(
        CharacterSkillsData data,
        string slotKey) => slotKey switch
        {
            "SkillSlot1" => data.FirstSkill,
            "SkillSlot2" => data.SecondSkill,
            "SkillSlot3" => data.UltimateSkill,
            "SkillSlot4" => data.SupportSkill,
            _ => null
        };

    private static void AssignImportedBuffIdentities(
        CharacterCard character,
        UnrealProjectSyncCharacterCandidate candidate,
        IReadOnlySet<string> selectedStableIds)
    {
        var service = new BuffService();
        var data = service.Load(character);
        foreach (var entry in data.Buffs)
        {
            var source = candidate.BuffsPreview.Buffs.FirstOrDefault(buff =>
                selectedStableIds.Contains(GetBuffStableId(buff)) &&
                string.Equals(buff.ObjectPath, entry.SourceAssetPath, StringComparison.OrdinalIgnoreCase));
            if (source is not null)
            {
                entry.SyncId = UnrealBridgeSemanticSnapshotService.CreateOriginIdentity(source.ObjectPath);
            }
        }

        service.Save(character, data);
    }

    private static string GetMaterialStableId(UnrealProjectSyncExportAssetView asset) =>
        $"material:{UnrealBridgeSemanticSnapshotService.CreateOriginIdentity(asset.ObjectPath)}";

    private static string GetVoiceStableId(UnrealProjectSyncExportAssetView asset) =>
        $"voice:{UnrealBridgeSemanticSnapshotService.CreateOriginIdentity(asset.ObjectPath)}";

    private static string GetBuffStableId(UnrealProjectSyncBuffPreview buff) =>
        $"buff:{UnrealBridgeSemanticSnapshotService.CreateOriginIdentity(buff.ObjectPath)}";

    // 必须和语义快照生成的动作稳定 ID 完全一致，否则勾选的动作在导入时一个都匹配不上。
    private static string GetSequenceStableId(
        string characterCode,
        UnrealProjectSyncSequenceActionPreview action) =>
        SequenceFrameIdentity.BuildActionStableId(action.ActionCode, action.FormIndex);

    /// <summary>
    /// 技能槽和它在稳定身份里的后缀。顺序和后缀写法都必须与语义快照一致，
    /// 两边对不上就等于这一项没有被选中过。
    /// </summary>
    private static IEnumerable<(UnrealProjectSyncSkillSlotPreview Slot, string Suffix)> EnumerateSkillSlotsWithSuffix(
        UnrealProjectSyncCharacterCandidate candidate)
    {
        for (var slotIndex = 0; slotIndex < candidate.SkillsPreview.CoreSlots.Count; slotIndex++)
        {
            yield return (candidate.SkillsPreview.CoreSlots[slotIndex], $"core:{slotIndex}");
        }

        yield return (candidate.SkillsPreview.SupportSkillSlot, "support");
    }

    private static string GetSkillStableId(
        string characterCode,
        string slotKey,
        string suffix,
        int stageIndex) =>
        // 字段顺序必须和 UnrealBridgeSemanticSnapshotService 一致：suffix 在 slotKey 之前。
        // 两边曾经写反过，于是选中的技能永远匹配不上快照里的稳定身份，
        // 表现是「从虚幻导入角色时技能一条都进不来」，而且不报任何错。
        $"skill:{UnrealBridgeSemanticSnapshotService.CreateOriginIdentity($"{characterCode}|skill|{suffix}|{slotKey}|{stageIndex}")}";
}
