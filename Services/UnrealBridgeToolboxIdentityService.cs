using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace CrossingVoidZDTool.Services;

internal sealed class UnrealBridgeToolboxIdentityService
{
    private const string StateFolderName = "UnrealSync";
    private const string FileName = "toolbox-identities.json";

    public IReadOnlyDictionary<string, string> Reconcile(
        CharacterCard character,
        IReadOnlyList<UnrealBridgeToolboxFileCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(candidates);

        var map = Load(character);
        var normalizedCandidates = candidates
            .Select(candidate => new Candidate(
                candidate,
                ToRelativePath(character, candidate.AssetPath)))
            .ToList();
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var claimedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in normalizedCandidates)
        {
            var exact = map.Entries.Values.FirstOrDefault(entry =>
                entry.Module == candidate.Source.Module &&
                string.Equals(entry.ToolboxRelativePath, candidate.RelativePath, StringComparison.OrdinalIgnoreCase));
            if (exact is null)
            {
                continue;
            }

            UpdateEntry(exact, candidate);
            claimedIds.Add(exact.SyncId);
            result[candidate.Source.AssetPath] = exact.SyncId;
        }

        foreach (var candidate in normalizedCandidates.Where(candidate => !result.ContainsKey(candidate.Source.AssetPath)))
        {
            var orphanMatches = map.Entries.Values
                .Where(entry =>
                    entry.Module == candidate.Source.Module &&
                    !claimedIds.Contains(entry.SyncId) &&
                    string.Equals(entry.ContentHash, candidate.Source.ContentHash, StringComparison.OrdinalIgnoreCase) &&
                    !File.Exists(ResolvePath(character, entry.ToolboxRelativePath)))
                .ToList();
            var entry = orphanMatches.Count == 1
                ? orphanMatches[0]
                : new UnrealBridgeToolboxIdentityEntry
                {
                    SyncId = Guid.NewGuid().ToString("N")
                };
            UpdateEntry(entry, candidate);
            map.Entries[entry.SyncId] = entry;
            claimedIds.Add(entry.SyncId);
            result[candidate.Source.AssetPath] = entry.SyncId;
        }

        Save(character, map);
        return result;
    }

    public void RemapPath(CharacterCard character, string previousPath, string nextPath)
    {
        ArgumentNullException.ThrowIfNull(character);
        var map = Load(character);
        var previousRelativePath = ToRelativePath(character, previousPath);
        var nextRelativePath = ToRelativePath(character, nextPath);
        var changed = false;
        foreach (var entry in map.Entries.Values.Where(entry =>
                     string.Equals(entry.ToolboxRelativePath, previousRelativePath, StringComparison.OrdinalIgnoreCase)))
        {
            entry.ToolboxRelativePath = nextRelativePath;
            changed = true;
        }

        if (changed)
        {
            Save(character, map);
        }
    }

    public void Assign(
        CharacterCard character,
        UnrealBridgeToolboxFileCandidate candidate,
        string stableId)
    {
        ArgumentNullException.ThrowIfNull(character);
        if (string.IsNullOrWhiteSpace(stableId))
        {
            throw new ArgumentException("同步身份不能为空。", nameof(stableId));
        }

        var normalizedId = stableId.Trim();
        var map = Load(character);
        var relativePath = ToRelativePath(character, candidate.AssetPath);
        foreach (var existing in map.Entries.Values.Where(entry =>
                     entry.Module == candidate.Module &&
                     string.Equals(entry.ToolboxRelativePath, relativePath, StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(entry.SyncId, normalizedId, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            map.Entries.Remove(existing.SyncId);
        }

        map.Entries[normalizedId] = new UnrealBridgeToolboxIdentityEntry
        {
            SyncId = normalizedId,
            Module = candidate.Module,
            ToolboxRelativePath = relativePath,
            ContentHash = candidate.ContentHash
        };
        Save(character, map);
    }

    public bool TryResolveAssignedPath(
        CharacterCard character,
        UnrealBridgeModule module,
        string stableId,
        out string path)
    {
        ArgumentNullException.ThrowIfNull(character);
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(stableId))
        {
            return false;
        }

        var map = Load(character);
        if (!map.Entries.TryGetValue(stableId.Trim(), out var entry) || entry.Module != module)
        {
            return false;
        }

        var resolvedPath = ResolvePath(character, entry.ToolboxRelativePath);
        if (!File.Exists(resolvedPath))
        {
            return false;
        }

        path = resolvedPath;
        return true;
    }

    public IReadOnlySet<string> GetAssignedPaths(
        CharacterCard character,
        UnrealBridgeModule module)
    {
        ArgumentNullException.ThrowIfNull(character);
        return Load(character).Entries.Values
            .Where(entry => entry.Module == module && !string.IsNullOrWhiteSpace(entry.ToolboxRelativePath))
            .Select(entry => ResolvePath(character, entry.ToolboxRelativePath))
            .Where(File.Exists)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static UnrealBridgeToolboxIdentityMap Load(CharacterCard character)
    {
        var path = GetPath(character);
        if (!File.Exists(path))
        {
            return new UnrealBridgeToolboxIdentityMap();
        }

        try
        {
            return JsonSerializer.Deserialize(
                File.ReadAllText(path, Encoding.UTF8),
                AppJsonSerializerContext.Default.UnrealBridgeToolboxIdentityMap)
                ?? new UnrealBridgeToolboxIdentityMap();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"工具箱素材身份清单读取失败：{path}", ex);
        }
    }

    private static void Save(CharacterCard character, UnrealBridgeToolboxIdentityMap map)
    {
        map.ProtocolVersion = 1;
        var path = GetPath(character);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicFileWriter.WriteAllText(
            path,
            JsonSerializer.Serialize(map, AppJsonSerializerContext.Default.UnrealBridgeToolboxIdentityMap));
    }

    private static void UpdateEntry(UnrealBridgeToolboxIdentityEntry entry, Candidate candidate)
    {
        entry.SyncId = string.IsNullOrWhiteSpace(entry.SyncId)
            ? Guid.NewGuid().ToString("N")
            : entry.SyncId;
        entry.Module = candidate.Source.Module;
        entry.ToolboxRelativePath = candidate.RelativePath;
        entry.ContentHash = candidate.Source.ContentHash;
    }

    private static string GetPath(CharacterCard character) =>
        Path.Combine(character.ToolFolderPath, StateFolderName, FileName);

    private static string ToRelativePath(CharacterCard character, string path)
    {
        var rootPath = Path.GetFullPath(character.FolderPath);
        var fullPath = Path.GetFullPath(path);
        var relativePath = Path.GetRelativePath(rootPath, fullPath);
        if (relativePath == ".." ||
            relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException($"素材不在当前角色目录内：{fullPath}");
        }

        return relativePath.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string ResolvePath(CharacterCard character, string relativePath) =>
        Path.GetFullPath(Path.Combine(
            character.FolderPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private sealed record Candidate(
        UnrealBridgeToolboxFileCandidate Source,
        string RelativePath);
}
