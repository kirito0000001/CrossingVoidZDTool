using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CrossingVoidZDTool.Services;

internal sealed class UnrealBridgeUnrealSnapshotService
{
    public const int SupportedProtocolVersion = 1;
    public const string ScanScriptRelativePath = @"Tools\UnrealBridge\scan_unreal_character.py";

    public string GetScanScriptPath() =>
        Path.Combine(AppContext.BaseDirectory, ScanScriptRelativePath);

    public ProcessStartInfo BuildScanProcessStartInfo(
        string editorPath,
        string projectPath,
        string characterCode,
        string outputPath)
    {
        var normalizedEditorPath = Path.GetFullPath(editorPath);
        var normalizedProjectPath = Path.GetFullPath(projectPath);
        var normalizedOutputPath = Path.GetFullPath(outputPath);
        if (!File.Exists(normalizedEditorPath))
        {
            throw new FileNotFoundException("没有找到 UnrealEditor.exe。", normalizedEditorPath);
        }

        if (!File.Exists(normalizedProjectPath) ||
            !string.Equals(Path.GetExtension(normalizedProjectPath), ".uproject", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("请选择有效的 Unreal .uproject 文件。");
        }

        if (string.IsNullOrWhiteSpace(characterCode))
        {
            throw new InvalidOperationException("扫描虚幻角色前必须指定角色代码。");
        }

        var scriptPath = GetScanScriptPath();
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException("工具箱内置的虚幻扫描脚本不存在。", scriptPath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(normalizedOutputPath)!);
        var commandPath = ResolveEditorCommandPath(normalizedEditorPath);
        var startInfo = new ProcessStartInfo
        {
            FileName = commandPath,
            Arguments = $"{Quote(normalizedProjectPath)} -run=pythonscript -script={Quote(scriptPath)} -unattended -nop4 -nosplash",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(commandPath) ?? string.Empty
        };
        startInfo.Environment["ZD_BRIDGE_CHARACTER_CODE"] = characterCode.Trim();
        startInfo.Environment["ZD_BRIDGE_PROJECT_PATH"] = normalizedProjectPath;
        startInfo.Environment["ZD_BRIDGE_SCAN_OUTPUT"] = normalizedOutputPath;
        return startInfo;
    }

    public UnrealBridgeSnapshot LoadSnapshot(
        string manifestPath,
        UnrealBridgeSyncState? baseline)
    {
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("虚幻扫描结果不存在。", manifestPath);
        }

        UnrealBridgeScanManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize(
                File.ReadAllText(manifestPath, Encoding.UTF8),
                AppJsonSerializerContext.Default.UnrealBridgeScanManifest)
                ?? throw new InvalidDataException("虚幻扫描结果为空。");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"虚幻扫描结果无法解析：{manifestPath}", ex);
        }

        return BuildSnapshot(manifest, baseline);
    }

    public UnrealBridgeSnapshot BuildSnapshot(
        UnrealBridgeScanManifest manifest,
        UnrealBridgeSyncState? baseline)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.ProtocolVersion != SupportedProtocolVersion)
        {
            throw new InvalidDataException(
                $"不支持的虚幻扫描协议版本：{manifest.ProtocolVersion}，当前支持 {SupportedProtocolVersion}。");
        }

        if (string.IsNullOrWhiteSpace(manifest.CharacterCode))
        {
            throw new InvalidDataException("虚幻扫描结果缺少角色代码。");
        }

        var items = manifest.Items.Select(item =>
        {
            var stableId = ResolveStableId(item, baseline);
            var payload = string.IsNullOrWhiteSpace(item.PayloadJson) ? "{}" : item.PayloadJson;
            var contentHash = string.IsNullOrWhiteSpace(item.ContentHash)
                ? ComputeFallbackHash(item.ObjectPath, payload)
                : item.ContentHash;
            return new UnrealBridgeSnapshotItem(
                stableId,
                string.IsNullOrWhiteSpace(item.ParentStableId)
                    ? $"module:{item.Module}"
                    : item.ParentStableId,
                item.Module,
                string.IsNullOrWhiteSpace(item.DisplayName) ? item.NormalizedName : item.DisplayName,
                contentHash,
                payload,
                string.Empty,
                item.ObjectPath,
                item.OriginIdentity,
                string.Empty,
                item.NormalizedName);
        }).ToArray();
        return new UnrealBridgeSnapshot(manifest.CharacterCode, items);
    }

    private static string ResolveStableId(
        UnrealBridgeScanItem item,
        UnrealBridgeSyncState? baseline)
    {
        if (!string.IsNullOrWhiteSpace(item.SyncId))
        {
            return item.SyncId;
        }

        if (baseline is not null)
        {
            var match = baseline.Entries.FirstOrDefault(pair =>
                !string.IsNullOrWhiteSpace(item.OriginIdentity) &&
                string.Equals(pair.Value.UnrealIdentity, item.OriginIdentity, StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrWhiteSpace(item.ObjectPath) &&
                string.Equals(pair.Value.UnrealObjectPath, item.ObjectPath, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match.Key))
            {
                return match.Key;
            }
        }

        var origin = !string.IsNullOrWhiteSpace(item.OriginIdentity)
            ? item.OriginIdentity
            : item.ObjectPath;
        return $"unreal:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(origin))).ToLowerInvariant()}";
    }

    private static string ComputeFallbackHash(string objectPath, string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{objectPath}\n{payload}")));

    private static string ResolveEditorCommandPath(string editorPath)
    {
        var folderPath = Path.GetDirectoryName(editorPath);
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return editorPath;
        }

        var commandPath = Path.Combine(folderPath, "UnrealEditor-Cmd.exe");
        return File.Exists(commandPath) ? commandPath : editorPath;
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
