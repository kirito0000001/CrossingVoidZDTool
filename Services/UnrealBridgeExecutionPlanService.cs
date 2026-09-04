using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CrossingVoidZDTool.Services;

internal sealed class UnrealBridgeExecutionPlanService
{
    public UnrealBridgeExecutionPlan Build(
        UnrealBridgeDirection direction,
        string characterCode,
        string unrealProjectPath,
        IReadOnlyList<UnrealBridgeChange> changes,
        bool deletionsConfirmed,
        bool isFirstPublish,
        string templateCharacterCode,
        UnrealBridgeSyncState? baseline = null,
        IReadOnlyList<UnrealAssetNormalizationItem>? normalizationItems = null)
    {
        if (string.IsNullOrWhiteSpace(characterCode))
        {
            throw new InvalidOperationException("执行同步前必须选择一个角色。");
        }

        var selectedChanges = changes
            .Where(change => change.IsSelected && change.Kind != UnrealBridgeChangeKind.Unchanged)
            .ToArray();
        var requestedRedirects = direction == UnrealBridgeDirection.PublishToUnreal && normalizationItems is not null
            ? normalizationItems
                .Where(item => item.Decision == UnrealAssetNormalizationDecision.Redirect && item.SelectedCandidate is not null)
                .Where(item => selectedChanges.Any(change =>
                    change.Kind == UnrealBridgeChangeKind.DeleteCandidate &&
                    string.Equals(change.UnrealItem?.SourceObjectPath, item.UnrealObjectPath, StringComparison.OrdinalIgnoreCase)))
                .ToArray()
            : Array.Empty<UnrealAssetNormalizationItem>();
        var redirectedSourcePaths = requestedRedirects
            .Select(item => item.UnrealObjectPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = selectedChanges
            .Where(change => change.Kind != UnrealBridgeChangeKind.DeleteCandidate ||
                !redirectedSourcePaths.Contains(change.UnrealItem?.SourceObjectPath ?? string.Empty))
            .ToArray();
        if (selected.Any(change => change.Kind == UnrealBridgeChangeKind.DeleteCandidate) && !deletionsConfirmed)
        {
            throw new InvalidOperationException("删除虚幻或工具箱中的多余项前必须完成二次确认。");
        }

        if (direction == UnrealBridgeDirection.PublishToUnreal &&
            isFirstPublish &&
            selected.Any(change => change.Kind == UnrealBridgeChangeKind.Added) &&
            string.IsNullOrWhiteSpace(templateCharacterCode))
        {
            throw new InvalidOperationException("新角色首次发布到虚幻前必须选择模板角色。");
        }

        var operations = selected
            .Select(change => BuildOperation(direction, characterCode, baseline, change))
            .ToList();
        if (direction == UnrealBridgeDirection.PublishToUnreal)
        {
            foreach (var item in requestedRedirects)
            {
                var targetPath = operations.FirstOrDefault(operation =>
                    string.Equals(operation.StableId, item.SelectedCandidate!.StableId, StringComparison.OrdinalIgnoreCase))?.TargetObjectPath;
                if (string.IsNullOrWhiteSpace(targetPath))
                {
                    targetPath = changes.FirstOrDefault(change =>
                        string.Equals(change.StableId, item.SelectedCandidate!.StableId, StringComparison.OrdinalIgnoreCase))?.UnrealItem?.SourceObjectPath;
                }

                if (string.IsNullOrWhiteSpace(targetPath))
                {
                    throw new InvalidOperationException($"重定向目标尚未加入同步范围：{item.SelectedCandidate!.DisplayName}。");
                }

                if (!string.Equals(item.UnrealObjectPath, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    operations.Add(new UnrealBridgeOperation
                    {
                        StableId = $"normalize:{item.StableId}",
                        Module = item.Module,
                        Kind = UnrealBridgeOperationKind.Consolidate,
                        DisplayName = $"重定向 {item.UnrealAssetName}",
                        SourceObjectPath = item.UnrealObjectPath,
                        TargetObjectPath = targetPath
                    });
                }
            }
        }

        operations = operations
            .OrderBy(operation => operation.Kind switch
            {
                UnrealBridgeOperationKind.Delete => 2,
                UnrealBridgeOperationKind.Consolidate => 1,
                _ => 0
            })
            .ThenBy(operation => operation.Module)
            .ThenBy(operation => operation.StableId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new UnrealBridgeExecutionPlan
        {
            Direction = direction,
            CharacterCode = characterCode.Trim(),
            UnrealProjectPath = Path.GetFullPath(unrealProjectPath),
            TemplateCharacterCode = templateCharacterCode.Trim(),
            BackupRequired = UnrealBridgeBackupPolicy.ShouldBackupByDefault(selected),
            Operations = operations
        };
    }

    private static UnrealBridgeOperation BuildOperation(
        UnrealBridgeDirection direction,
        string characterCode,
        UnrealBridgeSyncState? baseline,
        UnrealBridgeChange change)
    {
        var sourceItem = direction == UnrealBridgeDirection.PublishToUnreal
            ? change.ToolboxItem
            : change.UnrealItem;
        var targetItem = direction == UnrealBridgeDirection.PublishToUnreal
            ? change.UnrealItem
            : change.ToolboxItem;
        var referenceItem = sourceItem ?? targetItem
            ?? throw new InvalidOperationException($"同步项缺少两端数据：{change.StableId}。");
        return new UnrealBridgeOperation
        {
            StableId = change.StableId,
            Module = change.Module,
            Kind = change.Kind switch
            {
                UnrealBridgeChangeKind.Added => UnrealBridgeOperationKind.Add,
                UnrealBridgeChangeKind.Updated or UnrealBridgeChangeKind.Conflict => UnrealBridgeOperationKind.Update,
                UnrealBridgeChangeKind.Renamed => UnrealBridgeOperationKind.Rename,
                UnrealBridgeChangeKind.DeleteCandidate => UnrealBridgeOperationKind.Delete,
                _ => throw new InvalidOperationException($"不能为 {change.Kind} 生成执行项。")
            },
            DisplayName = change.DisplayName,
            SourceFilePath = sourceItem?.AssetPath ?? string.Empty,
            SourceObjectPath = change.UnrealItem?.SourceObjectPath ?? string.Empty,
            TargetObjectPath = ResolveTargetObjectPath(direction, characterCode, baseline, change, referenceItem),
            ToolboxRelativePath = change.ToolboxItem?.ToolboxRelativePath ?? string.Empty,
            NormalizedName = referenceItem.NormalizedName,
            PayloadJson = referenceItem.PayloadJson
        };
    }

    private static string ResolveTargetObjectPath(
        UnrealBridgeDirection direction,
        string characterCode,
        UnrealBridgeSyncState? baseline,
        UnrealBridgeChange change,
        UnrealBridgeSnapshotItem referenceItem)
    {
        if (direction != UnrealBridgeDirection.PublishToUnreal)
        {
            return change.ToolboxItem?.ToolboxRelativePath ?? string.Empty;
        }

        var currentObjectPath = change.UnrealItem?.SourceObjectPath ?? string.Empty;
        if (change.Kind == UnrealBridgeChangeKind.Added)
        {
            return ResolveAddedTargetObjectPath(characterCode, baseline, referenceItem);
        }

        if (change.Kind != UnrealBridgeChangeKind.Renamed || string.IsNullOrWhiteSpace(currentObjectPath))
        {
            return currentObjectPath;
        }

        var packagePath = currentObjectPath.Split('.', 2)[0];
        var slashIndex = packagePath.LastIndexOf('/');
        var normalizedName = SanitizeUnrealName(referenceItem.NormalizedName);
        if (referenceItem.Module == UnrealBridgeModule.Voices &&
            UnrealBridgeVoicePathPolicy.TryBuildCanonicalObjectPath(
                currentObjectPath,
                referenceItem.PayloadJson,
                referenceItem.NormalizedName,
                out var voiceTargetPath))
        {
            return voiceTargetPath;
        }

        return slashIndex < 0 || string.IsNullOrWhiteSpace(normalizedName)
            ? currentObjectPath
            : $"{packagePath[..(slashIndex + 1)]}{normalizedName}.{normalizedName}";
    }

    private static string ResolveAddedTargetObjectPath(
        string characterCode,
        UnrealBridgeSyncState? baseline,
        UnrealBridgeSnapshotItem referenceItem)
    {
        var fileName = Path.GetFileNameWithoutExtension(referenceItem.AssetPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = SanitizeUnrealName(referenceItem.NormalizedName);
        }

        fileName = SanitizeUnrealAssetName(fileName);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidOperationException($"无法为新增素材生成 Unreal 名称：{referenceItem.StableId}。");
        }

        if (referenceItem.Module == UnrealBridgeModule.BaseMaterials)
        {
            using var materialDocument = JsonDocument.Parse(referenceItem.PayloadJson);
            var materialKind = materialDocument.RootElement.TryGetProperty("kind", out var kindValue)
                ? kindValue.GetString()
                : null;
            if (string.Equals(materialKind, BaseMaterialKind.BuffIcon.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                var buffCharacterFolder = ResolveCharacterFolderName(
                    UnrealProjectSyncService.TargetZdContentPath,
                    characterCode,
                    baseline);
                return $"{UnrealProjectSyncService.TargetZdContentPath}/{buffCharacterFolder}/BUFF/{fileName}.{fileName}";
            }

            var materialCharacterFolder = ResolveCharacterFolderName(
                UnrealProjectSyncService.TargetBaseMaterialContentPath,
                characterCode,
                baseline);
            return $"{UnrealProjectSyncService.TargetBaseMaterialContentPath}/{materialCharacterFolder}/{fileName}.{fileName}";
        }

        if (referenceItem.Module == UnrealBridgeModule.Buffs)
        {
            var buffCharacterFolder = ResolveCharacterFolderName(
                UnrealProjectSyncService.TargetZdContentPath,
                characterCode,
                baseline);
            return $"{UnrealProjectSyncService.TargetZdContentPath}/{buffCharacterFolder}/BUFF/{fileName}.{fileName}";
        }

        if (referenceItem.Module == UnrealBridgeModule.Voices)
        {
            using var document = JsonDocument.Parse(referenceItem.PayloadJson);
            var kindText = document.RootElement.TryGetProperty("kind", out var kind)
                ? kind.GetString()
                : null;
            if (!Enum.TryParse<VoiceMaterialKind>(kindText, true, out var voiceKind))
            {
                throw new InvalidOperationException($"新增语音缺少可识别分类：{referenceItem.DisplayName}。");
            }

            var category = VoiceMaterialService.GetSpec(voiceKind).FolderName;
            var characterFolder = ResolveCharacterFolderName(UnrealProjectSyncService.TargetZdContentPath, characterCode, baseline);
            return $"{UnrealProjectSyncService.TargetZdContentPath}/{characterFolder}/Sound/{category}/{fileName}.{fileName}";
        }

        using (var document = JsonDocument.Parse(referenceItem.PayloadJson))
        {
            var actionCode = document.RootElement.TryGetProperty("actionCode", out var action)
                ? action.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(actionCode))
            {
                throw new InvalidOperationException($"新增序列帧缺少动作编号：{referenceItem.DisplayName}。");
            }

            var characterFolder = ResolveCharacterFolderName(UnrealProjectSyncService.TargetZdContentPath, characterCode, baseline);
            return $"{UnrealProjectSyncService.TargetZdContentPath}/{characterFolder}/Material/{SanitizeUnrealName(actionCode)}/{fileName}.{fileName}";
        }
    }

    private static string ResolveCharacterFolderName(
        string targetRoot,
        string characterCode,
        UnrealBridgeSyncState? baseline)
    {
        var prefix = targetRoot.TrimEnd('/') + "/";
        var existingPath = baseline?.Entries.Values
            .Select(entry => entry.UnrealObjectPath)
            .FirstOrDefault(path => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(existingPath))
        {
            var relative = existingPath[prefix.Length..];
            var slashIndex = relative.IndexOf('/');
            var folder = slashIndex >= 0 ? relative[..slashIndex] : relative.Split('.', 2)[0];
            if (!string.IsNullOrWhiteSpace(folder))
            {
                if (!string.Equals(folder, characterCode, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Unreal 角色目录名与工具箱角色代号不一致：实际目录“{folder}”，应为“{characterCode}”。请先修正目录或角色代号后再同步。");
                }

                return folder;
            }
        }

        return SanitizeUnrealName(characterCode);
    }

    private static string SanitizeUnrealName(string value)
    {
        return new string((value ?? string.Empty)
            .Select(character => char.IsLetterOrDigit(character) || character == '_' ? character : '_')
            .ToArray())
            .Trim('_');
    }

    private static string SanitizeUnrealAssetName(string value)
    {
        return new string((value ?? string.Empty)
            .Select(character => char.IsLetterOrDigit(character) || character is '_' or '-' ? character : '_')
            .ToArray())
            .Trim('_', '-');
    }
}
