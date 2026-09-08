using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CrossingVoidZDTool.Services;

internal sealed class UnrealLightConfigurationService
{
    public const int SupportedProtocolVersion = 1;
    public const string ScriptRelativePath = @"Tools\UnrealBridge\configure_unreal_light_settings.py";

    public string GetScriptPath() => Path.Combine(AppContext.BaseDirectory, ScriptRelativePath);

    public UnrealLightConfigurationRequest BuildRequest(
        CharacterCard character,
        bool apply,
        IReadOnlyCollection<string>? selectedStableIds = null)
    {
        ArgumentNullException.ThrowIfNull(character);
        var info = new CharacterInfoService().Load(character);
        var materials = new BaseMaterialService().LoadSections(character);
        var voices = new VoiceMaterialService().LoadSections(character);
        var itemIcon = GetMaterialObjectPaths(character.Code, materials, BaseMaterialKind.ItemIcon).FirstOrDefault() ?? string.Empty;
        var shapeIcons = GetMaterialObjectPaths(character.Code, materials, BaseMaterialKind.Icon);
        var shapePortraits = GetMaterialObjectPaths(character.Code, materials, BaseMaterialKind.MorphPortrait);
        var shapeCompletes = GetMaterialObjectPaths(character.Code, materials, BaseMaterialKind.FullMorphPortrait);
        var formation = GetVoiceObjectPaths(character.Code, voices, VoiceMaterialKind.Formation).FirstOrDefault() ?? string.Empty;
        var hurt = GetVoiceObjectPaths(character.Code, voices, VoiceMaterialKind.Hurt);
        var talk = voices
            .Where(section => section.Spec.Kind != VoiceMaterialKind.Hurt)
            .SelectMany(section => section.Items)
            .OrderBy(item => item.Kind)
            .ThenBy(item => item.Index)
            .Select(item => BuildVoiceObjectPath(character.Code, item))
            .ToList();

        return new UnrealLightConfigurationRequest
        {
            Mode = apply ? "Apply" : "Scan",
            CharacterCode = character.Code,
            CharacterName = info.Name,
            Description = info.Description,
            Keywords = info.KeywordTags
                .Select(value => value.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            TeamSelectObjectPath = "/Game/UIWidget/2DPvpUI/UI_TeamSelect.UI_TeamSelect",
            TeamVoiceObjectPath = formation,
            ItemObjectPath = $"/Game/ITems/CharItemS/Item_{character.Code}.Item_{character.Code}",
            ItemTypeObjectPath = "/Game/ITems/Chara_Type.Chara_Type",
            ItemIconObjectPath = itemIcon,
            ShapeIconObjectPaths = shapeIcons,
            ShapePortraitObjectPaths = shapePortraits,
            ShapeCompleteObjectPaths = shapeCompletes,
            PassiveSkills = info.PassiveSkills
                .Select(value => value.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList(),
            Speed = info.Speed,
            Health = info.Health,
            Attack = info.Attack,
            PhysicalDefense = info.PhysicalDefense,
            EnergyDefense = info.EnergyDefense,
            CriticalRate = info.CriticalRate,
            CriticalDamage = info.CriticalDamage,
            MetaSoundObjectPath = $"/Game/GameActor2D/{character.Code}/Sound/{character.Code}_OnDM.{character.Code}_OnDM",
            HurtConcurrencyObjectPath = $"/Game/GameActor2D/{character.Code}/Sound/{character.Code}_Con_Ondm.{character.Code}_Con_Ondm",
            TalkConcurrencyObjectPath = $"/Game/GameActor2D/{character.Code}/Sound/{character.Code}_Con_Talk.{character.Code}_Con_Talk",
            HurtVoiceObjectPaths = hurt,
            TalkVoiceObjectPaths = talk,
            SelectedStableIds = selectedStableIds?.ToHashSet(StringComparer.OrdinalIgnoreCase) ??
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        };
    }

    public void SaveRequest(string path, UnrealLightConfigurationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(request, AppJsonSerializerContext.Default.UnrealLightConfigurationRequest),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, path, overwrite: true);
    }

    public ProcessStartInfo BuildProcessStartInfoWithProgress(
        string editorPath,
        string projectPath,
        string requestPath,
        string resultPath,
        string progressPath)
    {
        var startInfo = BuildProcessStartInfo(editorPath, projectPath, requestPath, resultPath);
        if (!string.IsNullOrWhiteSpace(progressPath))
        {
            startInfo.EnvironmentVariables["ZD_LIGHT_CONFIG_PROGRESS"] = progressPath;
        }

        return startInfo;
    }

    public ProcessStartInfo BuildProcessStartInfo(
        string editorPath,
        string projectPath,
        string requestPath,
        string resultPath)
    {
        var normalizedEditorPath = Path.GetFullPath(editorPath);
        var normalizedProjectPath = Path.GetFullPath(projectPath);
        var normalizedRequestPath = Path.GetFullPath(requestPath);
        var normalizedResultPath = Path.GetFullPath(resultPath);
        if (!File.Exists(normalizedEditorPath))
        {
            throw new FileNotFoundException("没有找到 UnrealEditor.exe。", normalizedEditorPath);
        }
        if (!File.Exists(normalizedProjectPath))
        {
            throw new FileNotFoundException("没有找到 Unreal 项目。", normalizedProjectPath);
        }
        if (!File.Exists(normalizedRequestPath))
        {
            throw new FileNotFoundException("基础配置请求文件不存在。", normalizedRequestPath);
        }

        var scriptPath = GetScriptPath();
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException("工具箱内置基础配置脚本不存在。", scriptPath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(normalizedResultPath)!);
        var commandPath = ResolveEditorCommandPath(normalizedEditorPath);
        var startInfo = new ProcessStartInfo
        {
            FileName = commandPath,
            Arguments = $"{Quote(normalizedProjectPath)} -run=pythonscript -script={Quote(scriptPath)} -unattended -nop4 -nosplash",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(commandPath) ?? string.Empty,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.Environment["ZD_LIGHT_CONFIG_REQUEST"] = normalizedRequestPath;
        startInfo.Environment["ZD_LIGHT_CONFIG_RESULT"] = normalizedResultPath;
        return startInfo;
    }

    public async Task<UnrealLightConfigurationResult> ExecuteAsync(
        ProcessStartInfo startInfo,
        string resultPath,
        CancellationToken cancellationToken = default,
        string progressPath = "",
        IProgress<UnrealExportProgressState>? progress = null)
    {
        TryDelete(resultPath);
        TryDelete(progressPath);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 Unreal 基础配置进程。");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var startedAt = DateTime.UtcNow;
        var lastProgressAt = DateTime.MinValue;
        while (!process.HasExited)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                KillProcessTree(process);
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (DateTime.UtcNow - startedAt > TimeSpan.FromMinutes(20))
            {
                KillProcessTree(process);
                throw new TimeoutException("Unreal 基础配置超过 20 分钟，已终止命令进程。");
            }

            // 虚幻那一侧十几秒的静默期：脚本会把阶段写进进度文件，
            // 这里轮询转发出去，进度条才不会整段一动不动。
            ReportProgress(progressPath, progress, ref lastProgressAt);
            await Task.Delay(500, cancellationToken);
        }

        var output = await outputTask + await errorTask;
        if (!File.Exists(resultPath))
        {
            throw new InvalidOperationException(
                $"Unreal 基础配置没有生成结果文件。退出码：{process.ExitCode}。{Environment.NewLine}{output}");
        }

        try
        {
            var result = JsonSerializer.Deserialize(
                File.ReadAllText(resultPath, Encoding.UTF8),
                AppJsonSerializerContext.Default.UnrealLightConfigurationResult)
                ?? throw new InvalidDataException("Unreal 基础配置结果为空。");
            if (result.ProtocolVersion != SupportedProtocolVersion)
            {
                throw new InvalidDataException($"不支持的基础配置结果协议：{result.ProtocolVersion}。");
            }
            if (result.Items is null || result.AppliedStableIds is null || result.SavedAssets is null)
            {
                throw new InvalidDataException("Unreal 基础配置结果缺少必要集合。");
            }

            return result;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Unreal 基础配置结果无法解析：{resultPath}", ex);
        }
    }

    /// <summary>进度文件没变就不重复转发，免得每 500 毫秒刷一次同样的文案。</summary>
    private static void ReportProgress(
        string progressPath,
        IProgress<UnrealExportProgressState>? progress,
        ref DateTime lastWriteUtc)
    {
        if (progress is null || string.IsNullOrWhiteSpace(progressPath))
        {
            return;
        }

        try
        {
            var info = new FileInfo(progressPath);
            if (!info.Exists || info.LastWriteTimeUtc <= lastWriteUtc)
            {
                return;
            }

            lastWriteUtc = info.LastWriteTimeUtc;
            var state = JsonSerializer.Deserialize(
                File.ReadAllText(progressPath, Encoding.UTF8),
                AppJsonSerializerContext.Default.UnrealExportProgressState);
            if (state is not null)
            {
                progress.Report(state);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // 正在被写的进度文件读不全是常态，下一轮再读。
        }
    }

    private static List<string> GetMaterialObjectPaths(
        string characterCode,
        IEnumerable<BaseMaterialSection> sections,
        BaseMaterialKind kind)
    {
        return sections
            .Where(section => section.Spec.Kind == kind)
            .SelectMany(section => section.Items)
            .OrderBy(item => item.Index)
            .Select(item =>
            {
                var assetName = SanitizeUnrealName(Path.GetFileNameWithoutExtension(item.FilePath));
                var folder = kind == BaseMaterialKind.BuffIcon
                    ? $"/Game/GameActor2D/{characterCode}/BUFF"
                    : $"/Game/AssetMaterial/ImageS/CharaterS/{characterCode}";
                return $"{folder}/{assetName}.{assetName}";
            })
            .ToList();
    }

    private static List<string> GetVoiceObjectPaths(
        string characterCode,
        IEnumerable<VoiceMaterialSection> sections,
        VoiceMaterialKind kind)
    {
        return sections
            .Where(section => section.Spec.Kind == kind)
            .SelectMany(section => section.Items)
            .OrderBy(item => item.Index)
            .Select(item => BuildVoiceObjectPath(characterCode, item))
            .ToList();
    }

    private static string BuildVoiceObjectPath(string characterCode, VoiceMaterialItem item)
    {
        var category = VoiceMaterialService.GetSpec(item.Kind).FolderName;
        var assetName = SanitizeUnrealName(Path.GetFileNameWithoutExtension(item.FilePath));
        return $"/Game/GameActor2D/{characterCode}/Sound/{category}/{assetName}.{assetName}";
    }

    private static string SanitizeUnrealName(string value) =>
        new string((value ?? string.Empty)
            .Select(character => char.IsLetterOrDigit(character) || character is '_' or '-' ? character : '_')
            .ToArray())
            .Trim('_', '-');

    private static string ResolveEditorCommandPath(string editorPath)
    {
        var folder = Path.GetDirectoryName(editorPath);
        var commandPath = string.IsNullOrWhiteSpace(folder)
            ? editorPath
            : Path.Combine(folder, "UnrealEditor-Cmd.exe");
        return File.Exists(commandPath) ? commandPath : editorPath;
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }
}
