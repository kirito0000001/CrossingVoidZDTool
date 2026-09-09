using System;
using System.IO;
using System.Text.Json;

namespace CrossingVoidZDTool.Services;

internal sealed class AppSettingsService
{
    public const string ProjectRootFolderName = "CrossingVoidZDProject";
    public const string DefaultProjectRootPath = @"D:\CrossingVoidZDProject";

    public string SettingsDirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CrossingVoidZDTool");

    public string SettingsFilePath => Path.Combine(SettingsDirectoryPath, "settings.json");

    public AppSettings Load()
    {
        if (!File.Exists(SettingsFilePath))
        {
            return new AppSettings();
        }

        try
        {
            var settingsJson = File.ReadAllText(SettingsFilePath);
            return JsonSerializer.Deserialize(settingsJson, AppJsonSerializerContext.Default.AppSettings) ?? new AppSettings();
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            // 以前这里是静默返回默认设置，后果是一个闭环：
            // 设置读不出来 -> 工作区路径回落到默认值 -> 界面显示「目录已就绪」，
            // 用户的真实工程连同全部角色看起来空了 -> 下一次改任何设置调用 Save()，
            // 损坏的文件被默认值覆盖，丢失变成永久。
            // 而 Save 本身不是原子写，正好又会制造出这种损坏文件。
            //
            // 所以：把坏文件改名留档（下一次 Save 就不会盖掉证据），并且说出来。
            var quarantined = TryQuarantineSettingsFile();
            ToolboxLog.Error(
                quarantined is null
                    ? "设置文件读取失败，本次使用默认设置。"
                    : $"设置文件读取失败，已备份到 {quarantined}，本次使用默认设置。",
                error);
            return new AppSettings();
        }
    }

    /// <summary>把读不出来的设置文件改名留档，避免下一次保存直接覆盖掉证据。</summary>
    private string? TryQuarantineSettingsFile()
    {
        try
        {
            var quarantinePath = $"{SettingsFilePath}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            if (File.Exists(quarantinePath))
            {
                return quarantinePath;
            }

            File.Move(SettingsFilePath, quarantinePath);
            return quarantinePath;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectoryPath);
        // 非原子写会在断电/崩溃时留下半截 JSON，而半截 JSON 正是上面那个
        // 「静默重置 -> 永久覆盖」闭环的入口，所以这里必须原子替换。
        AtomicFileWriter.WriteAllText(
            SettingsFilePath,
            JsonSerializer.Serialize(settings, AppJsonSerializerContext.Default.AppSettings));
    }

    public string ResolveProjectRootPath(AppSettings settings)
    {
        return string.IsNullOrWhiteSpace(settings.ProjectRootPath)
            ? DefaultProjectRootPath
            : settings.ProjectRootPath;
    }

    public void EnsureProjectRootDirectory(string projectRootPath)
    {
        Directory.CreateDirectory(projectRootPath);
    }

    public string BuildProjectRootPathFromParent(string parentPath)
    {
        return Path.GetFullPath(Path.Combine(parentPath, ProjectRootFolderName));
    }
}
