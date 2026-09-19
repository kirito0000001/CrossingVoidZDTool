using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// <c>runtime.log</c> 的轮转与按批次回捞。
///
/// 轮转以前完全没有：单文件涨到 4MB、一万两千多行，事后要按一次同步去翻，
/// 只能从头往下滚。现在的规则是**超过 8MB、或者跨天**就在写入前改名归档，
/// 只保留最近 5 份；回溯固定在当前文件里 <c>rg "&lt;RunId&gt;"</c>。
/// </summary>
internal static class RuntimeLogFile
{
    public const long DefaultMaxBytes = 8L * 1024 * 1024;

    public const int DefaultMaxArchives = 5;

    public const string ArchivePrefix = "runtime-";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// 需要的话把现有文件改名归档。返回归档后的文件名；没轮转就返回 null。
    /// 归档名带的是**文件自己的**最后写入日期，不是今天的日期——
    /// 关掉几天再打开，那份日志属于它自己那天。
    /// </summary>
    public static string? RotateIfNeeded(
        string path,
        DateTimeOffset now,
        long maxBytes = DefaultMaxBytes,
        int maxArchives = DefaultMaxArchives)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (maxArchives <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxArchives), maxArchives, "至少保留一份归档。");
        }

        var info = new FileInfo(path);
        if (!info.Exists || info.Length == 0 || info.DirectoryName is null)
        {
            return null;
        }

        var lastWrite = new DateTimeOffset(info.LastWriteTime);
        var oversized = info.Length > maxBytes;
        var crossedDay = lastWrite.Date != now.Date;
        if (!oversized && !crossedDay)
        {
            return null;
        }

        var archivePath = BuildUniqueArchivePath(info.DirectoryName, lastWrite);
        File.Move(path, archivePath);
        PruneArchives(info.DirectoryName, maxArchives);
        return Path.GetFileName(archivePath);
    }

    /// <summary>读取属于某一次流程的所有行（按写入顺序）。</summary>
    public static IReadOnlyList<string> ReadRunLines(string path, string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (string.IsNullOrWhiteSpace(runId) || runId == RuntimeLogFormat.NoRunId || !File.Exists(path))
        {
            return [];
        }

        var marker = $" run={runId} ";
        return File.ReadAllLines(path, Encoding.UTF8)
            .Where(line => line.Contains(marker, StringComparison.Ordinal))
            .ToArray();
    }

    /// <summary>列出归档文件，按文件名（也就是日期）升序——最旧的排在最前。</summary>
    public static IReadOnlyList<string> ListArchives(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory, ArchivePrefix + "*.log")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToArray();
    }

    public static void AppendLine(string path, string line)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(line);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.AppendAllText(path, line + Environment.NewLine, Utf8NoBom);
    }

    private static string BuildUniqueArchivePath(string directory, DateTimeOffset lastWrite)
    {
        var baseName = $"{ArchivePrefix}{lastWrite:yyyyMMdd}";
        var candidate = Path.Combine(directory, baseName + ".log");
        var index = 1;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{baseName}-{index}.log");
            index++;
        }

        return candidate;
    }

    private static void PruneArchives(string directory, int maxArchives)
    {
        var archives = ListArchives(directory);
        var excess = archives.Count - maxArchives;
        for (var index = 0; index < excess; index++)
        {
            AtomicFileWriter.TryDelete(archives[index]);
        }
    }
}
