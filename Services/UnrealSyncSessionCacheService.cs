using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 虚幻同步台的分步进度缓存。
///
/// 一步一个文件，存在角色自己的工具目录下：
/// <c>&lt;角色&gt;/&lt;工具目录&gt;/UnrealSync/sync-&lt;项目键&gt;-step&lt;N&gt;.json</c>。
///
/// 放进角色目录有两个好处：角色之间天然不会互相覆盖，导出/备份角色时进度会一起带走。
/// 文件名里仍然保留项目键，这样同一个角色对接多个 Unreal 项目也不会撞车。
///
/// 旧版本存在 <c>%AppData%\CrossingVoidZDTool\UnrealSync</c> 下，读取时会自动迁移过来。
/// </summary>
internal sealed class UnrealSyncSessionCacheService
{
    public const string FolderName = "UnrealSync";
    private const int SupportedProtocolVersion = 3;

    /// <summary>角色自己的进度目录；没有本地角色（例如只在虚幻侧的候选）时返回空。</summary>
    public static string GetCacheFolderPath(CharacterCard? character) =>
        character is null || string.IsNullOrWhiteSpace(character.ToolFolderPath)
            ? string.Empty
            : Path.Combine(character.ToolFolderPath, FolderName);

    /// <summary>
    /// 读某一步的缓存。只读目标步骤的文件，不回退到别的步骤——
    /// 否则请求第三步缓存时可能误读第四步或第五步的内容。
    /// </summary>
    public UnrealSyncSessionCacheLoadResult LoadStep(
        CharacterCard? character,
        string projectPath,
        string characterCode,
        int workflowStep)
    {
        if (workflowStep < UnrealSyncWorkflow.MinStep || workflowStep > UnrealSyncWorkflow.MaxStep ||
            string.IsNullOrWhiteSpace(characterCode))
        {
            return new(UnrealSyncSessionCacheLoadStatus.Missing);
        }

        return LoadFromPaths(projectPath, StepPathCandidates(character, projectPath, characterCode, workflowStep));
    }

    /// <summary>读这个角色最近写过的那一步，用于打开页面时恢复现场。</summary>
    public UnrealSyncSessionCacheLoadResult LoadLatest(
        CharacterCard? character,
        string projectPath,
        string characterCode)
    {
        if (string.IsNullOrWhiteSpace(characterCode))
        {
            return new(UnrealSyncSessionCacheLoadStatus.Missing);
        }

        UnrealSyncSessionCacheLoadResult? invalid = null;
        foreach (var path in EnumerateCachePaths(character, projectPath, characterCode))
        {
            var result = LoadFromPath(projectPath, path);
            if (result.Status == UnrealSyncSessionCacheLoadStatus.Loaded)
            {
                MigrateIfNeeded(character, projectPath, characterCode, path, result.Cache!);
                return result;
            }

            // 坏掉的一步不该拖垮整次恢复：记下来，继续找更早的那一步。
            invalid ??= result.Status == UnrealSyncSessionCacheLoadStatus.Invalid ? result : null;
        }

        return invalid ?? new(UnrealSyncSessionCacheLoadStatus.Missing);
    }

    /// <summary>在一批角色里找最近写过的那一份进度，用于没有指定角色时的恢复。</summary>
    public UnrealSyncSessionCacheLoadResult LoadLatest(IEnumerable<CharacterCard> characters, string projectPath)
    {
        UnrealSyncSessionCacheLoadResult? newest = null;
        var newestAt = DateTime.MinValue;
        UnrealSyncSessionCacheLoadResult? invalid = null;

        foreach (var character in characters)
        {
            var result = LoadLatest(character, projectPath, character.Code);
            if (result.Status == UnrealSyncSessionCacheLoadStatus.Invalid)
            {
                invalid ??= result;
                continue;
            }
            if (result.Status != UnrealSyncSessionCacheLoadStatus.Loaded || result.Cache is null)
            {
                continue;
            }

            var writtenAt = LatestWriteTimeUtc(character, projectPath, character.Code);
            if (newest is null || writtenAt > newestAt)
            {
                newest = result;
                newestAt = writtenAt;
            }
        }

        return newest ?? invalid ?? new(UnrealSyncSessionCacheLoadStatus.Missing);
    }

    /// <summary>
    /// 写这一步的缓存。写不进去时返回 false 而不是抛异常：
    /// 落盘失败不该把正在进行的同步流程打断，下一次交互会再试一遍。
    /// </summary>
    public bool Write(CharacterCard? character, string projectPath, UnrealSyncSessionCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        var path = GetStepPath(character, projectPath, cache.SelectedCharacterCode, cache.WorkflowStep);
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporaryPath = path + ".tmp";
            File.WriteAllText(
                temporaryPath,
                ToolboxPortablePathService.ToPortableJson(
                    JsonSerializer.Serialize(cache, AppJsonSerializerContext.Default.UnrealSyncSessionCache),
                    ResolveCharacterFolder(path)),
                new UTF8Encoding(false));
            File.Move(temporaryPath, path, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// 同一步可能有两个候选位置：角色目录（新）和 AppData（旧）。
    /// 先读新位置，读到旧的就顺手迁过来。
    /// </summary>
    private IEnumerable<string> StepPathCandidates(
        CharacterCard? character,
        string projectPath,
        string characterCode,
        int workflowStep)
    {
        var characterPath = GetCharacterStepPath(character, projectPath, workflowStep);
        if (!string.IsNullOrEmpty(characterPath))
        {
            yield return characterPath;
        }

        yield return GetLegacyStepPath(projectPath, characterCode, workflowStep);
    }

    private UnrealSyncSessionCacheLoadResult LoadFromPaths(string projectPath, IEnumerable<string> paths)
    {
        UnrealSyncSessionCacheLoadResult? invalid = null;
        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            var result = LoadFromPath(projectPath, path);
            if (result.Status == UnrealSyncSessionCacheLoadStatus.Loaded)
            {
                return result;
            }
            invalid ??= result.Status == UnrealSyncSessionCacheLoadStatus.Invalid ? result : null;
        }

        return invalid ?? new(UnrealSyncSessionCacheLoadStatus.Missing);
    }

    /// <summary>按步骤从大到小枚举这个角色所有可能的缓存文件，新位置优先。</summary>
    private IEnumerable<string> EnumerateCachePaths(CharacterCard? character, string projectPath, string characterCode)
    {
        for (var step = UnrealSyncWorkflow.MaxStep; step >= UnrealSyncWorkflow.MinStep; step--)
        {
            var characterPath = GetCharacterStepPath(character, projectPath, step);
            if (!string.IsNullOrEmpty(characterPath) && File.Exists(characterPath))
            {
                yield return characterPath;
            }
        }

        for (var step = UnrealSyncWorkflow.MaxStep; step >= UnrealSyncWorkflow.MinStep; step--)
        {
            var legacyPath = GetLegacyStepPath(projectPath, characterCode, step);
            if (File.Exists(legacyPath))
            {
                yield return legacyPath;
            }
        }
    }

    private DateTime LatestWriteTimeUtc(CharacterCard? character, string projectPath, string characterCode)
    {
        var latest = DateTime.MinValue;
        foreach (var path in EnumerateCachePaths(character, projectPath, characterCode))
        {
            try
            {
                var writtenAt = File.GetLastWriteTimeUtc(path);
                if (writtenAt > latest)
                {
                    latest = writtenAt;
                }
            }
            catch (IOException)
            {
            }
        }

        return latest;
    }

    /// <summary>从旧位置读到的进度顺手搬进角色目录，之后就不再依赖 AppData。</summary>
    private void MigrateIfNeeded(
        CharacterCard? character,
        string projectPath,
        string characterCode,
        string loadedPath,
        UnrealSyncSessionCache cache)
    {
        var characterPath = GetCharacterStepPath(character, projectPath, cache.WorkflowStep);
        if (string.IsNullOrEmpty(characterPath) ||
            string.Equals(loadedPath, characterPath, StringComparison.OrdinalIgnoreCase) ||
            File.Exists(characterPath))
        {
            return;
        }

        Write(character, projectPath, cache);
    }

    private static UnrealSyncSessionCacheLoadResult LoadFromPath(string projectPath, string path)
    {
        if (!File.Exists(path)) return new(UnrealSyncSessionCacheLoadStatus.Missing);
        try
        {
            // 空文件是上一次落盘没写完（例如进程被强杀），当作没有这一步的缓存，
            // 别报成「内容损坏」去打扰用户——下一次保存会把它覆盖掉。
            if (new FileInfo(path).Length == 0)
            {
                return new(UnrealSyncSessionCacheLoadStatus.Missing);
            }

            var cache = JsonSerializer.Deserialize(
                ToolboxPortablePathService.ToAbsoluteJson(File.ReadAllText(path, Encoding.UTF8), ResolveCharacterFolder(path)),
                AppJsonSerializerContext.Default.UnrealSyncSessionCache);
            if (cache is null || cache.ProtocolVersion != SupportedProtocolVersion ||
                cache.PublishChanges is null || cache.SelectedStableIds is null ||
                cache.NormalizationDecisions is null || cache.NormalizationItems is null ||
                cache.LightConfigurationItems is null || cache.SelectedLightConfigurationIds is null ||
                cache.BlueprintSetupItems is null || cache.SelectedBlueprintSetupIds is null ||
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

    /// <summary>
    /// 缓存落在 <c>&lt;角色&gt;/tool/UnrealSync/</c> 下，所以角色文件夹能从路径本身倒推出来。
    /// 老的 AppData 缓存不在这个结构里，返回 null 表示不做路径改写。
    /// </summary>
    private static string? ResolveCharacterFolder(string path)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (folder is null || !string.Equals(Path.GetFileName(folder), FolderName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var toolFolder = Path.GetDirectoryName(folder);
        return toolFolder is null ? null : Path.GetDirectoryName(toolFolder);
    }

    private string GetStepPath(CharacterCard? character, string projectPath, string characterCode, int workflowStep)
    {
        var characterPath = GetCharacterStepPath(character, projectPath, workflowStep);
        return string.IsNullOrEmpty(characterPath)
            ? string.IsNullOrWhiteSpace(characterCode)
                ? string.Empty
                : GetLegacyStepPath(projectPath, characterCode, workflowStep)
            : characterPath;
    }

    private static string GetCharacterStepPath(CharacterCard? character, string projectPath, int workflowStep)
    {
        var folder = GetCacheFolderPath(character);
        return string.IsNullOrEmpty(folder)
            ? string.Empty
            : Path.Combine(folder, $"sync-{GetProjectKey(projectPath)}-step{ClampStep(workflowStep)}.json");
    }

    private static string GetLegacyStepPath(string projectPath, string characterCode, int workflowStep)
    {
        var characterSuffix = string.IsNullOrWhiteSpace(characterCode)
            ? string.Empty
            : $"-{SanitizeFileName(characterCode)}";
        return Path.Combine(
            GetLegacyFolderPath(),
            $"session-{GetProjectKey(projectPath)}{characterSuffix}-step{ClampStep(workflowStep)}.json");
    }

    private static int ClampStep(int workflowStep) =>
        Math.Clamp(workflowStep, UnrealSyncWorkflow.MinStep, UnrealSyncWorkflow.MaxStep);

    private static string GetLegacyFolderPath() =>
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
