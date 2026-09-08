using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 第六步「蓝图置入」：把工具箱这边的角色数据整理成 Unreal 侧的目标值，
/// 交给桥接脚本扫描比对，再按勾选写回。
///
/// 目标值全部由工具箱推导，Unreal 只负责比对和写入——这样「差异」永远是
/// 「工具箱说应该是什么」和「项目里现在是什么」之差，不会掺进任何一方的猜测。
/// </summary>
internal sealed class UnrealBlueprintSetupService
{
    public const int SupportedProtocolVersion = 1;
    public const string ScriptRelativePath = @"Tools\UnrealBridge\apply_blueprint_setup.py";

    /// <summary>角色蓝图和数据表都按角色代号定位在这些固定位置。</summary>
    public const string CharacterActorRoot = "/Game/GameActor2D";
    public const string CharacterImageRoot = "/Game/AssetMaterial/ImageS/CharaterS";

    public string GetScriptPath() => Path.Combine(AppContext.BaseDirectory, ScriptRelativePath);

    public UnrealBlueprintSetupRequest BuildRequest(
        CharacterCard character,
        CharacterInfoData info,
        CharacterSkillsData skills,
        IReadOnlyList<BaseMaterialSection> materialSections,
        IReadOnlyCollection<string>? selectedStableIds = null)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(materialSections);

        var code = character.Code;
        var formCount = Math.Max(1, info.FormLimit);
        var avatars = GetBattleAvatarObjectPaths(code, materialSections);

        var request = new UnrealBlueprintSetupRequest
        {
            Mode = "Scan",
            CharacterCode = code,
            CharacterName = string.IsNullOrWhiteSpace(info.Name) ? character.Name : info.Name,
            FormCount = formCount,
            Anti = info.Anti,
            Icon1PObjectPath = avatars.GetValueOrDefault(1, string.Empty),
            Support1PObjectPath = avatars.GetValueOrDefault(2, string.Empty),
            Icon2PObjectPath = avatars.GetValueOrDefault(3, string.Empty),
            Support2PObjectPath = avatars.GetValueOrDefault(4, string.Empty),
            AnimInstanceClassObjectPath = $"{CharacterActorRoot}/{code}/{code}_AnimBP.{code}_AnimBP_C",
            IdleFlipbookObjectPath = BuildIdleFlipbookObjectPath(code),
            Sequences = BuildSequenceBindings(code, formCount),
            Skills = BuildSkillPayloads(code, formCount, skills),
        };

        if (selectedStableIds is not null)
        {
            request.SelectedStableIds = new HashSet<string>(selectedStableIds, StringComparer.OrdinalIgnoreCase);
        }

        return request;
    }

    /// <summary>
    /// 对局内头像的四个固定槽位：1=1P 主战、2=1P 护援、3=2P 主战、4=2P 护援。
    /// 主战两张写角色蓝图的 Icon1P/Icon2P，护援两张写 SupImage 表。
    /// </summary>
    private static Dictionary<int, string> GetBattleAvatarObjectPaths(
        string characterCode,
        IReadOnlyList<BaseMaterialSection> sections)
    {
        var result = new Dictionary<int, string>();
        var section = sections.FirstOrDefault(item => item.Spec.Kind == BaseMaterialKind.BattleAvatar);
        if (section is null)
        {
            return result;
        }

        foreach (var item in section.Items)
        {
            if (item.Index is < 1 or > 4)
            {
                continue;
            }

            var objectPath = BuildImageObjectPath(characterCode, item.FilePath);
            if (!string.IsNullOrEmpty(objectPath))
            {
                result[item.Index] = objectPath;
            }
        }

        return result;
    }

    /// <summary>
    /// 工具箱里的图片同步到 Unreal 后固定落在
    /// <c>/Game/AssetMaterial/ImageS/CharaterS/&lt;角色&gt;/&lt;文件名&gt;</c>，
    /// 与第三步发布使用的是同一条规则。
    /// </summary>
    public static string BuildImageObjectPath(string characterCode, string filePath)
    {
        if (string.IsNullOrWhiteSpace(characterCode) || string.IsNullOrWhiteSpace(filePath))
        {
            return string.Empty;
        }

        var name = Path.GetFileNameWithoutExtension(filePath);
        return string.IsNullOrWhiteSpace(name)
            ? string.Empty
            : $"{CharacterImageRoot}/{characterCode}/{name}.{name}";
    }

    /// <summary>
    /// 站街 Flipbook：由序列同步在 <c>Material/Idle</c> 下生成，规范名不带角色前缀
    /// （老角色遗留的 <c>&lt;角色&gt;_Idle</c> 会被判成差异，这是对的）。
    /// </summary>
    public static string BuildIdleFlipbookObjectPath(string characterCode)
    {
        var definition = SequenceActionCatalog.Definitions.First(item => item.Code == "Idle");
        var folder = SequenceActionCatalog.GetMaterialFolderName(definition, 1);
        var flipbook = SequenceActionCatalog.GetFlipbookName(definition, 1);
        return $"{CharacterActorRoot}/{characterCode}/Material/{folder}/{flipbook}.{flipbook}";
    }

    /// <summary>
    /// 角色蓝图上的序列数组。代号表里 <c>BlueprintSequenceArrayProperty</c> 非空的动作
    /// 才由蓝图引用，其余靠 AnimMaps 绑定，不归第六步管。
    /// </summary>
    public static List<UnrealBlueprintSequenceBinding> BuildSequenceBindings(string characterCode, int formCount)
    {
        var bindings = new List<UnrealBlueprintSequenceBinding>();
        foreach (var definition in SequenceActionCatalog.Definitions
                     .Where(item => !string.IsNullOrEmpty(item.BlueprintSequenceArrayProperty))
                     .OrderBy(item => item.Code, StringComparer.Ordinal))
        {
            var paths = new List<string>();
            for (var formIndex = 1; formIndex <= Math.Max(1, formCount); formIndex++)
            {
                var name = SequenceActionCatalog.GetAnimSequenceName(definition, formIndex);
                paths.Add($"{CharacterActorRoot}/{characterCode}/AnimSequences/{name}.{name}");
            }

            bindings.Add(new UnrealBlueprintSequenceBinding
            {
                PropertyName = definition.BlueprintSequenceArrayProperty,
                DisplayName = definition.DisplayName,
                ObjectPaths = paths,
            });
        }

        return bindings;
    }

    private static List<UnrealBlueprintSkillPayload> BuildSkillPayloads(
        string characterCode,
        int formCount,
        CharacterSkillsData skills)
    {
        var payloads = new List<UnrealBlueprintSkillPayload>
        {
            BuildSkillPayload(characterCode, formCount, "SkillSlot1", "一技能", skills.FirstSkill),
            BuildSkillPayload(characterCode, formCount, "SkillSlot2", "二技能", skills.SecondSkill),
            BuildSkillPayload(characterCode, formCount, "SkillSlot3", "终结技", skills.UltimateSkill),
            BuildSkillPayload(characterCode, formCount, "SubSkill", "护援技", skills.SupportSkill),
        };

        // 连携技按搭档分组：数据表里一个角色对每个搭档各存一条。
        foreach (var group in skills.ComboSkills
                     .GroupBy(entry => (entry.ComboCharacterName ?? string.Empty).Trim())
                     .Where(group => !string.IsNullOrEmpty(group.Key))
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var payload = BuildSkillPayload(
                characterCode, formCount, "Combo", $"连携技 · {group.Key}", group.ToList());
            payload.PartnerName = group.Key;
            payloads.Add(payload);
        }

        return payloads;
    }

    private static UnrealBlueprintSkillPayload BuildSkillPayload(
        string characterCode,
        int formCount,
        string slotKey,
        string displayName,
        IReadOnlyList<CharacterSkillEntry> entries)
    {
        var payload = new UnrealBlueprintSkillPayload
        {
            SlotKey = slotKey,
            DisplayName = displayName,
        };

        // 技能条目本来就是按形态排列的；护援技和连携技在工具箱里也可能只填一条，
        // 那就照实只下发一条，不去替用户补齐形态。
        foreach (var entry in entries)
        {
            payload.Names.Add(entry.PositionName?.Trim() ?? string.Empty);
            payload.SkillNames.Add(entry.TrueName?.Trim() ?? string.Empty);
            payload.Descriptions.Add(entry.Description?.Trim() ?? string.Empty);
            payload.IconObjectPaths.Add(BuildImageObjectPath(characterCode, entry.IconPath));
            payload.PointCosts.Add(ParseInt(entry.PtCost));
            payload.AttackCapacities.Add(ParseInt(entry.AttackCapacity));
            payload.AutoPriorities.Add(ParseInt(entry.AutoPriority));
            payload.SkillStates.Add(MapSkillStateToUnreal(entry.SkillState));
            payload.PreformTypes.Add(MapGuardStateToUnreal(entry.GuardState));
            payload.PreSkillValues.Add(ParseDouble(entry.GuardValue));
            payload.SkillRates.Add(BuildSkillRate(entry.LevelMultipliers));
        }

        return payload;
    }

    private static UnrealBlueprintSkillRate BuildSkillRate(IReadOnlyList<SkillMultiplierLevel> levels)
    {
        var rate = new UnrealBlueprintSkillRate();
        foreach (var level in levels)
        {
            var key = level.Level.ToString(CultureInfo.InvariantCulture);
            rate.Physical[key] = ParseDouble(level.PhysicalMultiplier);
            rate.Energy[key] = ParseDouble(level.EnergyMultiplier);
        }

        return rate;
    }

    /// <summary>工具箱的中文选项 -> Unreal 的 E2DSkillType 名称。</summary>
    public static string MapSkillStateToUnreal(string? value) => (value ?? string.Empty).Trim() switch
    {
        "常态" => "Normal",
        "禁用" => "Disable",
        "舍弃" => "Abandon",
        _ => "Air",
    };

    /// <summary>工具箱的中文选项 -> Unreal 的 EPreformType 名称。</summary>
    public static string MapGuardStateToUnreal(string? value) => (value ?? string.Empty).Trim() switch
    {
        "防御" => "Defense",
        "反击" => "Attack",
        "闪避" => "Dodge",
        _ => "Air",
    };

    private static int ParseInt(string? value) =>
        int.TryParse((value ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0;

    private static double ParseDouble(string? value) =>
        double.TryParse((value ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0d;

    public void SaveRequest(string path, UnrealBlueprintSetupRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(request);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(request, UnrealBlueprintSetupJsonContext.Default.UnrealBlueprintSetupRequest),
            new UTF8Encoding(false));
    }

    public ProcessStartInfo BuildProcessStartInfo(
        string editorPath,
        string projectPath,
        string requestPath,
        string resultPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveEditorCommandPath(editorPath),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("-run=pythonscript");
        startInfo.ArgumentList.Add($"-script={GetScriptPath()}");
        startInfo.ArgumentList.Add("-unattended");
        startInfo.ArgumentList.Add("-nosplash");
        startInfo.ArgumentList.Add("-nosound");
        startInfo.ArgumentList.Add("-nop4");
        // 在线执行时这两个变量由远程作业转发，所以必须带 ZD_ 前缀。
        startInfo.EnvironmentVariables["ZD_BLUEPRINT_SETUP_REQUEST"] = requestPath;
        startInfo.EnvironmentVariables["ZD_BLUEPRINT_SETUP_RESULT"] = resultPath;
        return startInfo;
    }

    /// <summary>
    /// 结果只认结果文件，不看退出码：编辑器只要在任何地方记过一条 error
    /// （比如项目里某个无关蓝图坏了）退出码就非零，第五步已经被这件事坑过。
    /// </summary>
    public async Task<UnrealBlueprintSetupResult> ExecuteAsync(
        ProcessStartInfo startInfo,
        string resultPath,
        CancellationToken cancellationToken = default)
    {
        TryDelete(resultPath);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 Unreal 蓝图置入进程。");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var startedAt = DateTime.UtcNow;
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
                throw new TimeoutException("Unreal 蓝图置入超过 20 分钟，已终止命令进程。");
            }

            await Task.Delay(500, cancellationToken);
        }

        var output = await outputTask + await errorTask;
        return ReadResult(resultPath, output, process.ExitCode);
    }

    public static UnrealBlueprintSetupResult ReadResult(string resultPath, string processOutput, int exitCode)
    {
        if (!File.Exists(resultPath))
        {
            throw new InvalidOperationException(
                $"Unreal 蓝图置入没有生成结果文件。退出码：{exitCode}。{Environment.NewLine}{processOutput}");
        }

        try
        {
            var result = JsonSerializer.Deserialize(
                File.ReadAllText(resultPath, Encoding.UTF8),
                UnrealBlueprintSetupJsonContext.Default.UnrealBlueprintSetupResult)
                ?? throw new InvalidDataException("Unreal 蓝图置入结果为空。");
            if (result.ProtocolVersion != SupportedProtocolVersion)
            {
                throw new InvalidDataException($"不支持的蓝图置入结果协议：{result.ProtocolVersion}。");
            }
            if (result.Items is null || result.AppliedStableIds is null || result.SavedAssets is null)
            {
                throw new InvalidDataException("Unreal 蓝图置入结果缺少必要集合。");
            }
            if (!result.Succeeded && !string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                throw new InvalidDataException($"Unreal 蓝图置入失败：{result.ErrorMessage}");
            }

            return result;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Unreal 蓝图置入结果无法解析：{resultPath}", ex);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string ResolveEditorCommandPath(string editorPath)
    {
        if (string.IsNullOrWhiteSpace(editorPath))
        {
            return editorPath;
        }

        var directory = Path.GetDirectoryName(editorPath);
        var fileName = Path.GetFileNameWithoutExtension(editorPath);
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName) || fileName.EndsWith("-Cmd", StringComparison.OrdinalIgnoreCase))
        {
            return editorPath;
        }

        var commandPath = Path.Combine(directory, fileName + "-Cmd" + Path.GetExtension(editorPath));
        return File.Exists(commandPath) ? commandPath : editorPath;
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
        }
    }
}
