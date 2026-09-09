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
        catch (Exception error)
        {
            // 回滚。这里以前只还原「还停在临时名」的那些，已经落到目标名的一律不管——
            // 而第二阶段（临时名 -> 目标名）失败时，前面几个恰恰已经落到目标名了，
            // 于是一半改了名一半没改，编号从此对不上，函数名却叫 Atomic。
            //
            // 另外原来的回滚整段裸奔：回滚自己抛一下，原始异常就被顶掉了，
            // 用户看到的是「拒绝访问某个临时文件」，而真正的失败原因再也找不回来。
            var rollbackFailures = new List<Exception>();
            foreach (var entry in staged.AsEnumerable().Reverse())
            {
                try
                {
                    // 已经落到目标名的，先退回临时名，再和其余项一起退回原名。
                    var landedOnTarget = isDirectory
                        ? Directory.Exists(entry.Move.TargetPath)
                        : File.Exists(entry.Move.TargetPath);
                    var stillTemporary = isDirectory
                        ? Directory.Exists(entry.TemporaryPath)
                        : File.Exists(entry.TemporaryPath);

                    var restoreFrom = stillTemporary
                        ? entry.TemporaryPath
                        : landedOnTarget
                            ? entry.Move.TargetPath
                            : null;
                    if (restoreFrom is null)
                    {
                        continue;
                    }

                    if (isDirectory)
                    {
                        Directory.Move(restoreFrom, entry.Move.SourcePath);
                    }
                    else
                    {
                        File.Move(restoreFrom, entry.Move.SourcePath);
                    }
                }
                catch (Exception rollbackError) when (
                    rollbackError is IOException or UnauthorizedAccessException)
                {
                    // 单项回滚失败不能中断其余项的回滚，也不能盖掉原始异常。
                    rollbackFailures.Add(rollbackError);
                }
            }

            if (rollbackFailures.Count > 0)
            {
                ToolboxLog.Error(
                    $"素材重命名失败后回滚也没完全成功，有 {rollbackFailures.Count} 项还停在中间状态，" +
                    "请检查动作目录里是否残留 .material-rename-*.tmp。",
                    new AggregateException(rollbackFailures));
            }

            _ = error;
            throw;
        }

        return moves;
    }
}
