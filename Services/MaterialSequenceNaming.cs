using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CrossingVoidZDTool.Services;

internal readonly record struct MaterialPathRename(string SourcePath, string TargetPath);

internal static class MaterialSequenceNaming
{
    public static int GetWidth(int totalCount)
    {
        return Math.Max(1, Math.Max(1, totalCount).ToString().Length);
    }

    public static string FormatIndex(int index, int totalCount)
    {
        if (index < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return index.ToString().PadLeft(GetWidth(totalCount), '0');
    }

    public static IReadOnlyList<MaterialPathRename> RenameFilesAtomically(
        IEnumerable<MaterialPathRename> renames)
    {
        return RenamePathsAtomically(renames, isDirectory: false);
    }

    public static IReadOnlyList<MaterialPathRename> RenameDirectoriesAtomically(
        IEnumerable<MaterialPathRename> renames)
    {
        return RenamePathsAtomically(renames, isDirectory: true);
    }

    private static IReadOnlyList<MaterialPathRename> RenamePathsAtomically(
        IEnumerable<MaterialPathRename> renames,
        bool isDirectory)
    {
        var moves = renames
            .Select(rename => new MaterialPathRename(
                Path.GetFullPath(rename.SourcePath),
                Path.GetFullPath(rename.TargetPath)))
            .Where(rename => !string.Equals(
                rename.SourcePath,
                rename.TargetPath,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (moves.Count == 0)
        {
            return [];
        }

        var sourcePaths = moves
            .Select(move => move.SourcePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var move in moves)
        {
            var sourceExists = isDirectory ? Directory.Exists(move.SourcePath) : File.Exists(move.SourcePath);
            if (!sourceExists)
            {
                throw new FileNotFoundException("要重命名的素材不存在。", move.SourcePath);
            }

            var targetExists = isDirectory ? Directory.Exists(move.TargetPath) : File.Exists(move.TargetPath);
            if (targetExists && !sourcePaths.Contains(move.TargetPath))
            {
                throw new IOException($"素材编号目标已存在：{move.TargetPath}");
            }
        }

        var staged = new List<(MaterialPathRename Move, string TemporaryPath)>();
        try
        {
            foreach (var move in moves)
            {
                var parentPath = Path.GetDirectoryName(move.SourcePath)
                    ?? throw new InvalidOperationException("素材路径缺少父目录。");
                var temporaryPath = Path.Combine(parentPath, $".material-rename-{Guid.NewGuid():N}.tmp");
                if (isDirectory)
                {
                    Directory.Move(move.SourcePath, temporaryPath);
                }
                else
                {
                    File.Move(move.SourcePath, temporaryPath);
                }

                staged.Add((move, temporaryPath));
            }

            foreach (var entry in staged)
            {
                if (isDirectory)
                {
                    Directory.Move(entry.TemporaryPath, entry.Move.TargetPath);
                }
                else
                {
                    File.Move(entry.TemporaryPath, entry.Move.TargetPath);
                }
            }
        }
        catch
        {
            foreach (var entry in staged.AsEnumerable().Reverse())
            {
                var temporaryExists = isDirectory
                    ? Directory.Exists(entry.TemporaryPath)
                    : File.Exists(entry.TemporaryPath);
                if (!temporaryExists)
                {
                    continue;
                }

                if (isDirectory)
                {
                    Directory.Move(entry.TemporaryPath, entry.Move.SourcePath);
                }
                else
                {
                    File.Move(entry.TemporaryPath, entry.Move.SourcePath);
                }
            }

            throw;
        }

        return moves;
    }
}
