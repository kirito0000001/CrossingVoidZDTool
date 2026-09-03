using System;
using System.Linq;
using System.Text.Json;

namespace CrossingVoidZDTool.Services;

internal static class UnrealBridgeVoicePathPolicy
{
    public static bool TryBuildCanonicalObjectPath(
        string currentObjectPath,
        string payloadJson,
        string normalizedName,
        out string targetObjectPath)
    {
        targetObjectPath = string.Empty;
        if (!TryReadVoiceKind(payloadJson, out var kind) || kind == VoiceMaterialKind.Other)
        {
            return false;
        }

        var packagePath = currentObjectPath.Split('.', 2)[0].Replace('\\', '/');
        var soundSegmentIndex = packagePath.IndexOf("/Sound/", StringComparison.OrdinalIgnoreCase);
        var targetName = SanitizeUnrealName(normalizedName);
        if (soundSegmentIndex < 0 || string.IsNullOrWhiteSpace(targetName))
        {
            return false;
        }

        var soundRoot = packagePath[..(soundSegmentIndex + "/Sound".Length)];
        var categoryFolder = VoiceMaterialService.GetSpec(kind).FolderName;
        targetObjectPath = $"{soundRoot}/{categoryFolder}/{targetName}.{targetName}";
        return true;
    }

    private static bool TryReadVoiceKind(string payloadJson, out VoiceMaterialKind kind)
    {
        kind = VoiceMaterialKind.Other;
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var kindProperty = document.RootElement
                .EnumerateObject()
                .FirstOrDefault(property => string.Equals(property.Name, "kind", StringComparison.OrdinalIgnoreCase));
            return kindProperty.Value.ValueKind == JsonValueKind.String &&
                Enum.TryParse(kindProperty.Value.GetString(), ignoreCase: true, out kind);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string SanitizeUnrealName(string value)
    {
        return new string((value ?? string.Empty)
            .Select(character => char.IsLetterOrDigit(character) || character == '_' ? character : '_')
            .ToArray())
            .Trim('_');
    }
}
