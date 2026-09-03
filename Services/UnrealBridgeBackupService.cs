using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace CrossingVoidZDTool.Services;

internal static class UnrealBridgeBackupPolicy
{
    public static bool ShouldBackupByDefault(IEnumerable<UnrealBridgeChange> changes)
    {
        return changes.Any(change =>
            change.IsSelected &&
            change.Kind is UnrealBridgeChangeKind.Updated or UnrealBridgeChangeKind.Renamed or UnrealBridgeChangeKind.Conflict or UnrealBridgeChangeKind.DeleteCandidate);
    }
}

internal sealed class UnrealBridgeBackupService
{
    public ProcessStartInfo BuildZipProjectStartInfo(
        string editorPath,
        string unrealProjectPath,
        string backupPath)
    {
        var normalizedEditorPath = Path.GetFullPath(editorPath);
        var normalizedProjectPath = Path.GetFullPath(unrealProjectPath);
        var normalizedBackupPath = Path.GetFullPath(backupPath);
        if (!File.Exists(normalizedEditorPath))
        {
            throw new FileNotFoundException("未找到 Unreal Editor。", normalizedEditorPath);
        }

        if (!File.Exists(normalizedProjectPath) ||
            !string.Equals(Path.GetExtension(normalizedProjectPath), ".uproject", StringComparison.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException("未找到有效的 Unreal 项目。", normalizedProjectPath);
        }

        var win64Folder = Directory.GetParent(normalizedEditorPath)
            ?? throw new InvalidOperationException("无法定位 Unreal Editor 所在目录。");
        var binariesFolder = win64Folder.Parent
            ?? throw new InvalidOperationException("无法定位 Unreal Engine/Binaries 目录。");
        var engineFolder = binariesFolder.Parent
            ?? throw new InvalidOperationException("无法定位 Unreal Engine 目录。");
        var runUatPath = Path.Combine(engineFolder.FullName, "Build", "BatchFiles", "RunUAT.bat");
        if (!File.Exists(runUatPath))
        {
            throw new FileNotFoundException("当前引擎缺少 RunUAT.bat，无法使用原生压缩项目。", runUatPath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(normalizedBackupPath)!);
        var startInfo = new ProcessStartInfo
        {
            FileName = runUatPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = engineFolder.FullName
        };
        startInfo.ArgumentList.Add("ZipProjectUp");
        startInfo.ArgumentList.Add($"-project={normalizedProjectPath}");
        startInfo.ArgumentList.Add($"-install={normalizedBackupPath}");
        return startInfo;
    }
}
