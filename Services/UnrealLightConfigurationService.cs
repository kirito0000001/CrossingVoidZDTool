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
        // 受击语音走角色自己的 OnDM MetaSound，不进这里。
        // 音效也排除：并发控制的语义是「同一时刻只响一条角色语音」，
        // 而音效本来就要能叠加。待分配语音仍然算语音，照常进并发。
        var talk = voices
            .Where(section => section.Spec.Kind is not (VoiceMaterialKind.Hurt or VoiceMaterialKind.SoundEffect))
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
            RedirectStandardError = true,
            // Unreal 的 Python 层写的是 UTF-8；不指定就按父进程 OEM 代码页解码
            // （中文 Windows 是 936），出错时那段日志会整段乱码，等于线索全丢。
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
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
        // 清场删除可能失败（编辑器/杀软占着），所以记下删没删掉，
        // 好在结果不新鲜时把「残留删不掉」这条线索写进报错。
        var staleResultRemoved = UnrealProcessRunner.TryClearStaleFile(resultPath);
        UnrealProcessRunner.TryClearStaleFile(progressPath);
        // 虚幻那一侧有十几秒的静默期：脚本会把阶段写进进度文件，
        // Runner 轮询转发出去，进度条才不会整段一动不动。
        await UnrealProcessRunner.RunAsync(
            startInfo,
            progressPath,
            progress,
            AppJsonSerializerContext.Default.UnrealExportProgressState,
            TimeSpan.FromMinutes(20),
            "无法启动 Unreal 基础配置进程。",
            "Unreal 基础配置超过 20 分钟，已终止命令进程。",
            verdict: completed => DescribeUnusableResult(resultPath, completed, staleResultRemoved),
            cancellationToken: cancellationToken);

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

    /// <summary>
    /// 结果文件必须是这一轮写出来的，只判 <c>File.Exists</c> 会踩坑：
    /// 结果路径是固定的，而开跑前的清场删除会被 IO 异常吞掉；删不掉时留在那儿的
    /// 一定是上一次运行的结果，于是这一轮什么都没写出来，却把上一轮的配置结果
    /// 当成本次结果读回去。
    /// </summary>
    private static string? DescribeUnusableResult(
        string resultPath,
        UnrealProcessResult run,
        bool staleResultRemoved)
    {
        if (UnrealProcessRunner.IsFreshOutput(resultPath, run.StartedAtUtc))
        {
            return null;
        }

        return File.Exists(resultPath) && !staleResultRemoved
            ? $"Unreal 基础配置没有写出本轮结果文件，只留下删不掉的上一轮残留：{resultPath}。" +
              $"退出码：{run.ExitCode}。{Environment.NewLine}{run.Output}"
            : $"Unreal 基础配置没有生成结果文件。退出码：{run.ExitCode}。{Environment.NewLine}{run.Output}";
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
}
