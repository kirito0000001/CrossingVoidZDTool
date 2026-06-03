using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace CrossingVoidZDTool.Services;

internal sealed class ProjectRootMigrationService
{
    public MigrationResult Migrate(string oldProjectRootPath, string newProjectRootPath, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(oldProjectRootPath))
        {
            Directory.CreateDirectory(newProjectRootPath);
            return new MigrationResult(0, 0);
        }

        Directory.CreateDirectory(newProjectRootPath);

        var sourceDirectories = Directory.EnumerateDirectories(oldProjectRootPath, "*", SearchOption.AllDirectories).ToList();
        foreach (var sourceDirectory in sourceDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetDirectory = Path.Combine(newProjectRootPath, Path.GetRelativePath(oldProjectRootPath, sourceDirectory));
            Directory.CreateDirectory(targetDirectory);
        }

        var sourceFiles = Directory.EnumerateFiles(oldProjectRootPath, "*", SearchOption.AllDirectories).ToList();
        foreach (var sourceFile in sourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetFile = Path.Combine(newProjectRootPath, Path.GetRelativePath(oldProjectRootPath, sourceFile));
            var targetDirectory = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrEmpty(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            File.Copy(sourceFile, targetFile, overwrite: true);
        }

        cancellationToken.ThrowIfCancellationRequested();
        VerifyMigratedFiles(oldProjectRootPath, newProjectRootPath, sourceFiles, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.Delete(oldProjectRootPath, recursive: true);

        return new MigrationResult(sourceFiles.Count, sourceDirectories.Count);
    }

    private static void VerifyMigratedFiles(
        string oldProjectRootPath,
        string newProjectRootPath,
        IReadOnlyCollection<string> sourceFiles,
        CancellationToken cancellationToken = default)
    {
        var failures = new List<string>();

        foreach (var sourceFile in sourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(oldProjectRootPath, sourceFile);
            var targetFile = Path.Combine(newProjectRootPath, relativePath);

            if (!File.Exists(targetFile))
            {
                failures.Add($"{relativePath} 缺失");
                continue;
            }

            var sourceInfo = new FileInfo(sourceFile);
            var targetInfo = new FileInfo(targetFile);
            if (sourceInfo.Length != targetInfo.Length)
            {
                failures.Add($"{relativePath} 大小不一致");
                continue;
            }

            if (!HashesEqual(sourceFile, targetFile))
            {
                failures.Add($"{relativePath} 内容校验失败");
            }
        }

        if (failures.Count > 0)
        {
            throw new IOException($"迁移校验失败：{string.Join("；", failures.Take(5))}");
        }
    }

    private static bool HashesEqual(string firstPath, string secondPath)
    {
        using var firstStream = File.OpenRead(firstPath);
        using var secondStream = File.OpenRead(secondPath);
        var firstHash = SHA256.HashData(firstStream);
        var secondHash = SHA256.HashData(secondStream);
        return firstHash.AsSpan().SequenceEqual(secondHash);
    }
}
