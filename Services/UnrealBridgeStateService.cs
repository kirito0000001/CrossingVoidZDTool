using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CrossingVoidZDTool.Services;

internal sealed class UnrealBridgeStateService
{
    private const string StateFolderName = "UnrealSync";

    public UnrealBridgeSyncState? Load(CharacterCard character, string unrealProjectPath)
    {
        var path = GetStatePath(character, unrealProjectPath);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var state = JsonSerializer.Deserialize(
                File.ReadAllText(path, Encoding.UTF8),
                AppJsonSerializerContext.Default.UnrealBridgeSyncState);
            if (state is null ||
                !string.Equals(state.HashScheme, UnrealBridgeSyncState.SourceFileHashScheme, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(state.CharacterCode, character.Code, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(NormalizeProjectPath(state.UnrealProjectPath), NormalizeProjectPath(unrealProjectPath), StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return state;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Save(CharacterCard character, string unrealProjectPath, UnrealBridgeSyncState state)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(state);
        state.CharacterCode = character.Code;
        state.UnrealProjectPath = NormalizeProjectPath(unrealProjectPath);
        state.HashScheme = UnrealBridgeSyncState.SourceFileHashScheme;
        state.LastVerifiedAt = DateTimeOffset.Now;

        var path = GetStatePath(character, unrealProjectPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicFileWriter.WriteAllText(
            path,
            JsonSerializer.Serialize(state, AppJsonSerializerContext.Default.UnrealBridgeSyncState));
    }

    public string GetStatePath(CharacterCard character, string unrealProjectPath)
    {
        var normalizedPath = NormalizeProjectPath(unrealProjectPath).ToUpperInvariant();
        var projectKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath)))[..16];
        return Path.Combine(character.ToolFolderPath, StateFolderName, $"{projectKey}.json");
    }

    private static string NormalizeProjectPath(string path)
    {
        return string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFullPath(path.Trim());
    }
}
