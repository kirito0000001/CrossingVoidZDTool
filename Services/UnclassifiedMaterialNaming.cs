using System;
using System.Collections.Generic;
using System.IO;

namespace CrossingVoidZDTool.Services;

internal static class UnclassifiedMaterialNaming
{
    public static string BuildFileName(
        string characterCode,
        string marker,
        int index,
        int totalCount,
        string? originalName,
        string extension)
    {
        var prefix = $"{characterCode}-{marker}-{MaterialSequenceNaming.FormatIndex(index, totalCount)}";
        var suffix = string.IsNullOrWhiteSpace(originalName)
            ? string.Empty
            : $"-{originalName.Trim()}";
        return $"{prefix}{suffix}{extension}";
    }

    public static bool TryParse(
        string characterCode,
        IReadOnlyList<string> markers,
        string path,
        out int index,
        out string originalName)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        foreach (var marker in markers)
        {
            var prefix = $"{characterCode}-{marker}-";
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var remainder = name[prefix.Length..];
            var separatorIndex = remainder.IndexOf('-');
            var indexText = separatorIndex >= 0 ? remainder[..separatorIndex] : remainder;
            if (!int.TryParse(indexText, out index) || index < 1)
            {
                continue;
            }

            originalName = separatorIndex >= 0 && separatorIndex < remainder.Length - 1
                ? remainder[(separatorIndex + 1)..]
                : string.Empty;
            return true;
        }

        index = 0;
        originalName = string.Empty;
        return false;
    }

    public static string ResolveOriginalName(
        string characterCode,
        IReadOnlyList<string> markers,
        string path)
    {
        return TryParse(characterCode, markers, path, out _, out var originalName)
            ? originalName
            : Path.GetFileNameWithoutExtension(path);
    }
}
