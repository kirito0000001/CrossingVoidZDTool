using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace CrossingVoidZDTool.Services;

internal enum UnrealBridgeBackupDecision
{
    /// <summary>不备份：本批只新增，没有改写或删除既有资产。</summary>
    Skip,

    /// <summary>不备份，但要提醒：用户关掉了开关，而这一批会改写或删除既有资产。</summary>
    SkipWithRiskWarning,

    Backup
}

internal static class UnrealBridgeBackupPolicy
{
    public static bool ShouldBackupByDefault(IEnumerable<UnrealBridgeChange> changes)
    {
        return changes.Any(change =>
            change.IsSelected &&
            change.Kind is UnrealBridgeChangeKind.Updated or UnrealBridgeChangeKind.Renamed or UnrealBridgeChangeKind.Conflict or UnrealBridgeChangeKind.DeleteCandidate);
    }

    /// <summary>
    /// 同步前备不备份。整体设置是唯一开关：关掉就一律不备份。
    ///
    /// 以前只要计划里含更新/改名/删除就会绕过设置强制备份一次，
    /// 而第五步必然带删除项，等于这个开关对第五步完全无效——
    /// 关着开关点同步，照样先压一份几个 G 的工程出来。
    /// </summary>
    public static UnrealBridgeBackupDecision Decide(
        bool backupEnabledInSettings,
        bool planTouchesExistingAssets)
    {
        if (backupEnabledInSettings)
        {
            return UnrealBridgeBackupDecision.Backup;
        }

        return planTouchesExistingAssets
            ? UnrealBridgeBackupDecision.SkipWithRiskWarning
            : UnrealBridgeBackupDecision.Skip;
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
