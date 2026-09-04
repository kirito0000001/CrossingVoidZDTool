using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CrossingVoidZDTool.Services;

internal sealed class UnrealSyncSessionCacheService
{
    private const string FolderName = "UnrealSync";

    public UnrealSyncSessionCacheLoadResult Load(string projectPath)
    {
        try
        {
            var folder = GetFolderPath();
            var prefix = $"session-{GetProjectKey(projectPath)}";
            var latestPath = Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder, $"{prefix}*.json")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault()
                : null;
            return latestPath is null
                ? new(UnrealSyncSessionCacheLoadStatus.Missing)
                : LoadFromPath(projectPath, latestPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(UnrealSyncSessionCacheLoadStatus.Invalid, ErrorMessage: $"无法查找同步进度文件。\n{ex.Message}");
        }
    }

    public UnrealSyncSessionCacheLoadResult Load(string projectPath, string characterCode)
    {
        try
        {
            var folder = GetFolderPath();
            var prefix = $"session-{GetProjectKey(projectPath)}-{SanitizeFileName(characterCode)}-step";
            var stepPaths = Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder, $"{prefix}*.json")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .ToArray()
                : Array.Empty<string>();
            foreach (var stepPath in stepPaths)
            {
                var stepResult = LoadFromPath(projectPath, stepPath);
                if (stepResult.Status == UnrealSyncSessionCacheLoadStatus.Loaded)
                {
                    return stepResult;
                }
            }

            var path = GetPath(projectPath, characterCode);
            if (File.Exists(path))
            {
                return LoadFromPath(projectPath, path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(UnrealSyncSessionCacheLoadStatus.Invalid, ErrorMessage: $"无法查找角色同步进度文件。\n{ex.Message}");
        }

        return LoadLegacyProjectCache(projectPath, characterCode);
    }

    public UnrealSyncSessionCacheLoadResult LoadStep(string projectPath, string characterCode, int workflowStep)
    {
        if (workflowStep is < 1 or > 4 || string.IsNullOrWhiteSpace(characterCode))
        {
            return new(UnrealSyncSessionCacheLoadStatus.Missing);
        }

        var path = GetStepPath(projectPath, characterCode, workflowStep);
        var preferred = File.Exists(path)
            ? LoadFromPath(projectPath, path)
            : new UnrealSyncSessionCacheLoadResult(UnrealSyncSessionCacheLoadStatus.Missing);
        if (preferred.Status == UnrealSyncSessionCacheLoadStatus.Loaded)
        {
            return preferred;
        }

        var fallback = Load(projectPath, characterCode);
        return fallback.Status == UnrealSyncSessionCacheLoadStatus.Loaded ? fallback : preferred;
    }

    private UnrealSyncSessionCacheLoadResult LoadLegacyProjectCache(string projectPath, string characterCode)
    {

        // 兼容迁移前按项目保存的单文件进度。
        var legacyPath = GetPath(projectPath, string.Empty);
        var legacy = File.Exists(legacyPath)
            ? LoadFromPath(projectPath, legacyPath)
            : new UnrealSyncSessionCacheLoadResult(UnrealSyncSessionCacheLoadStatus.Missing);
        if (legacy.Status == UnrealSyncSessionCacheLoadStatus.Invalid)
        {
            return legacy;
        }

        return legacy.Cache is not null && string.Equals(legacy.Cache.SelectedCharacterCode, characterCode, StringComparison.OrdinalIgnoreCase)
            ? legacy
            : new(UnrealSyncSessionCacheLoadStatus.Missing);
    }

    private static UnrealSyncSessionCacheLoadResult LoadFromPath(string projectPath, string path)
    {
        if (!File.Exists(path)) return new(UnrealSyncSessionCacheLoadStatus.Missing);
        try
        {
            var cache = JsonSerializer.Deserialize(
                File.ReadAllText(path, Encoding.UTF8),
                AppJsonSerializerContext.Default.UnrealSyncSessionCache);
            if (cache is null || cache.ProtocolVersion != 3 || cache.PublishChanges is null || cache.SelectedStableIds is null || cache.NormalizationDecisions is null || cache.NormalizationItems is null ||
                cache.LightConfigurationItems is null || cache.SelectedLightConfigurationIds is null ||
                cache.NormalizationItems.Any(item => item is null || item.Candidates is null) ||
                !string.Equals(Normalize(cache.ProjectPath), Normalize(projectPath), StringComparison.OrdinalIgnoreCase))
            {
                return new(UnrealSyncSessionCacheLoadStatus.Invalid, ErrorMessage: $"同步进度文件内容无效：{path}");
            }

            return new(UnrealSyncSessionCacheLoadStatus.Loaded, cache);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return new(UnrealSyncSessionCacheLoadStatus.Invalid, ErrorMessage: $"同步进度文件读取失败：{path}\n{ex.Message}");
        }
    }

    public void Write(string projectPath, UnrealSyncSessionCache cache)
    {
        var path = GetStepPath(projectPath, cache.SelectedCharacterCode, cache.WorkflowStep);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(cache, AppJsonSerializerContext.Default.UnrealSyncSessionCache),
            new UTF8Encoding(false));
        File.Move(temporaryPath, path, true);
    }

    private static string GetPath(string projectPath, string characterCode)
    {
        var characterSuffix = string.IsNullOrWhiteSpace(characterCode)
            ? string.Empty
            : $"-{SanitizeFileName(characterCode)}";
        return Path.Combine(GetFolderPath(), $"session-{GetProjectKey(projectPath)}{characterSuffix}.json");
    }

    private static string GetStepPath(string projectPath, string characterCode, int workflowStep)
    {
        var characterSuffix = string.IsNullOrWhiteSpace(characterCode)
            ? string.Empty
            : $"-{SanitizeFileName(characterCode)}";
        return Path.Combine(GetFolderPath(), $"session-{GetProjectKey(projectPath)}{characterSuffix}-step{Math.Clamp(workflowStep, 1, 4)}.json");
    }

    private static string GetFolderPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CrossingVoidZDTool", FolderName);

    private static string GetProjectKey(string projectPath)
    {
        var normalized = Normalize(projectPath);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized.ToUpperInvariant())))[..16];
    }

    private static string SanitizeFileName(string value) =>
        string.Concat(value.Trim().Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

    private static string Normalize(string path) =>
        string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFullPath(path.Trim());
}
