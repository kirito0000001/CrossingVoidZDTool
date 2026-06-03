using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace CrossingVoidZDTool.Services;

internal sealed class ProjectRootMigrationService
{
    public MigrationResult Migrate(
        string oldProjectRootPath,
        string newProjectRootPath,
        IProgress<ProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new ProgressUpdate("正在准备迁移目录...", 2, $"{oldProjectRootPath} -> {newProjectRootPath}", true));
        if (!Directory.Exists(oldProjectRootPath))
        {
            Directory.CreateDirectory(newProjectRootPath);
            progress?.Report(new ProgressUpdate("旧目录不存在，已创建新目录。", 100, newProjectRootPath));
            return new MigrationResult(0, 0);
        }

        Directory.CreateDirectory(newProjectRootPath);

        var sourceDirectories = Directory.EnumerateDirectories(oldProjectRootPath, "*", SearchOption.AllDirectories).ToList();
        for (var index = 0; index < sourceDirectories.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceDirectory = sourceDirectories[index];
            var targetDirectory = Path.Combine(newProjectRootPath, Path.GetRelativePath(oldProjectRootPath, sourceDirectory));
            Directory.CreateDirectory(targetDirectory);
            var percent = sourceDirectories.Count == 0
                ? 10
                : 5 + (index + 1d) / sourceDirectories.Count * 15;
            progress?.Report(new ProgressUpdate("正在创建目录结构...", percent, targetDirectory));
        }

        var sourceFiles = Directory.EnumerateFiles(oldProjectRootPath, "*", SearchOption.AllDirectories).ToList();
        for (var index = 0; index < sourceFiles.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceFile = sourceFiles[index];
            var targetFile = Path.Combine(newProjectRootPath, Path.GetRelativePath(oldProjectRootPath, sourceFile));
            var targetDirectory = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrEmpty(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            File.Copy(sourceFile, targetFile, overwrite: true);
            var percent = sourceFiles.Count == 0
                ? 60
                : 20 + (index + 1d) / sourceFiles.Count * 45;
            progress?.Report(new ProgressUpdate("正在复制项目文件...", percent, Path.GetRelativePath(oldProjectRootPath, sourceFile)));
        }

        cancellationToken.ThrowIfCancellationRequested();
        VerifyMigratedFiles(oldProjectRootPath, newProjectRootPath, sourceFiles, progress, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new ProgressUpdate("正在删除旧目录...", 96, oldProjectRootPath));
        Directory.Delete(oldProjectRootPath, recursive: true);
        progress?.Report(new ProgressUpdate("目录迁移完成", 100, newProjectRootPath));

        return new MigrationResult(sourceFiles.Count, sourceDirectories.Count);
    }

    private static void VerifyMigratedFiles(
        string oldProjectRootPath,
        string newProjectRootPath,
        IReadOnlyCollection<string> sourceFiles,
        IProgress<ProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var failures = new List<string>();
        var sourceFileList = sourceFiles.ToList();
        for (var index = 0; index < sourceFileList.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceFile = sourceFileList[index];
            var relativePath = Path.GetRelativePath(oldProjectRootPath, sourceFile);
            var targetFile = Path.Combine(newProjectRootPath, relativePath);
            var percent = sourceFileList.Count == 0
                ? 92
                : 65 + (index + 1d) / sourceFileList.Count * 28;
            progress?.Report(new ProgressUpdate("正在校验迁移结果...", percent, relativePath));

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
