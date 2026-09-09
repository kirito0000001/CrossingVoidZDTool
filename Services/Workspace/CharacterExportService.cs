using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 把整个角色目录原样复制到指定位置，交付给别人。
///
/// 从 <see cref="CharacterWorkspaceService"/> 里搬出来的。它跟工作区的其他职责没有交集：
/// 输入是一个角色目录加一个目标位置，输出是磁盘上的一份拷贝，既不改角色数据，
/// 也不影响角色台上的任何状态。
///
/// 这里唯一值得慢慢读的是「临时目录 + 旧目标移位」那一段，它解决的是覆盖导出时的一个真实事故：
/// 直接删旧目录再复制，复制到一半失败（磁盘满、目标被占用、用户按了取消），
/// 用户手上就既没有新的也没有旧的了。现在的顺序是——先整份复制到同盘的临时目录，
/// 成功之后才把旧目标挪开、把临时目录改名顶上，最后才删掉挪开的旧目标；
/// 中途任何一步失败都会把旧目标搬回原位。<c>finally</c> 里的兜底不是多余的：
/// 抛异常和取消都会走到那里。
/// </summary>
internal sealed class CharacterExportService
{
    /// <summary>默认导出到工作区根下的 Export/，和 Draft/Completed 平级。</summary>
    public string GetDefaultExportRootPath(string projectRootPath)
    {
        return Path.Combine(Path.GetFullPath(projectRootPath), CharacterFolderLayout.Export);
    }

    public string ExportCharacterFolder(
        CharacterCard character,
        string exportRootPath,
        bool overwrite,
        IProgress<CharacterBackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        CharacterWorkspaceService.EnsureCharacterLayout(character.FolderPath);
        var sourcePath = Path.GetFullPath(character.FolderPath);
        var normalizedExportRoot = Path.GetFullPath(exportRootPath);
        // 导到自己肚子里会边复制边把复制出来的东西再复制一遍，直到磁盘满。
        if (CharacterWorkspaceService.IsPathInsideDirectory(normalizedExportRoot, sourcePath))
        {
            throw new InvalidOperationException("导出位置不能放在当前角色文件夹内部。");
        }

        Directory.CreateDirectory(normalizedExportRoot);
        var targetPath = Path.Combine(normalizedExportRoot, character.Code);
        if (string.Equals(sourcePath, Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("导出目标不能与当前角色文件夹相同。");
        }

        if (Directory.Exists(targetPath) && !overwrite)
        {
            throw new IOException($"导出目标已存在：{targetPath}");
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new CharacterBackupProgress("正在扫描角色文件...", 0, 0, 0, 0, 0, null));
        var files = Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories).ToList();
        var directories = Directory.EnumerateDirectories(sourcePath, "*", SearchOption.AllDirectories).ToList();
        var totalBytes = files.Sum(filePath => new FileInfo(filePath).Length);
        // 临时名带 GUID：两次导出撞在一起也不会互相踩。
        var tempPath = Path.Combine(normalizedExportRoot, $".{character.Code}.exporting-{Guid.NewGuid():N}");
        var displacedTargetPath = Path.Combine(normalizedExportRoot, $".{character.Code}.replacing-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempPath);
            foreach (var directoryPath in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.Combine(tempPath, Path.GetRelativePath(sourcePath, directoryPath)));
            }

            long completedBytes = 0;
            for (var index = 0; index < files.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var filePath = files[index];
                var relativePath = Path.GetRelativePath(sourcePath, filePath);
                var targetFilePath = Path.Combine(tempPath, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(targetFilePath)!);
                File.Copy(filePath, targetFilePath, overwrite: false);
                completedBytes += new FileInfo(filePath).Length;
                var percent = files.Count == 0
                    ? 90
                    : Math.Min(90, Math.Max(1, (index + 1) * 90d / files.Count));
                progress?.Report(new CharacterBackupProgress(
                    $"正在导出 {index + 1}/{files.Count}：{relativePath}",
                    percent,
                    index + 1,
                    files.Count,
                    completedBytes,
                    totalBytes,
                    relativePath));
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new CharacterBackupProgress("正在写入导出目录...", 95, files.Count, files.Count, totalBytes, totalBytes, null));
            if (Directory.Exists(targetPath))
            {
                Directory.Move(targetPath, displacedTargetPath);
            }

            try
            {
                Directory.Move(tempPath, targetPath);
            }
            catch
            {
                if (Directory.Exists(displacedTargetPath) && !Directory.Exists(targetPath))
                {
                    Directory.Move(displacedTargetPath, targetPath);
                }

                throw;
            }

            CharacterWorkspaceService.TryDeleteDirectory(displacedTargetPath);
            progress?.Report(new CharacterBackupProgress("导出完成。", 100, files.Count, files.Count, totalBytes, totalBytes, null));
            return targetPath;
        }
        finally
        {
            CharacterWorkspaceService.TryDeleteDirectory(tempPath);
            if (Directory.Exists(displacedTargetPath) && !Directory.Exists(targetPath))
            {
                Directory.Move(displacedTargetPath, targetPath);
            }
        }
    }
}
