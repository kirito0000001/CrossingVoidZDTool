using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;

namespace CrossingVoidZDTool.Services;

internal sealed class UnrealProjectSyncService
{
    private const int CurrentExportSchemaVersion = 2;
    private static readonly UnicodeEncoding StrictUnicodeEncoding = new(
        bigEndian: false,
        byteOrderMark: false,
        throwOnInvalidBytes: true);

    public const string TargetBaseMaterialContentPath = "/Game/AssetMaterial/ImageS/CharaterS";
    public const string TargetSharedBuffIconContentPath = "/Game/AssetMaterial/ImageS/BUFF";
    public const string TargetZdContentPath = "/Game/GameActor2D";
    public const string TargetCharacterItemContentPath = "/Game/ITems/CharItemS";
    public const string LinkSkillLibraryObjectPath = "/Game/BaseC/ExCordLibrary/LB_Fucs.LB_Fucs";
    public const string ExportFolderName = "ZDToolboxExport";
    public const string ExportManifestFileName = "characters.json";
    public const string ExportScriptRelativePath = @"Tools\Unreal\export_zd_assets.py";

    private static readonly string[] ExportTargetContentPaths =
    [
        TargetBaseMaterialContentPath,
        TargetSharedBuffIconContentPath,
        TargetZdContentPath
    ];

    private static readonly UnrealSequenceActionDefinition[] StandardSequenceActions =
    [
        new("Click", "点击", "base", 0),
        new("Death", "死亡", "base", 1),
        new("DefAtk", "守备反击", "base", 2),
        new("Defeat", "失败", "base", 3),
        new("Defence", "守备防御", "base", 4),
        new("Dodge", "守备闪避", "base", 5),
        new("FlyDown", "坠落", "base", 6),
        new("Flying", "飞行", "base", 7),
        new("FlyStart", "击飞", "base", 8),
        new("Idle", "站街", "base", 9),
        new("Land", "落地", "base", 10),
        new("Move", "移动", "base", 11),
        new("OnDamage", "受击", "base", 12),
        new("StandUP", "站起", "base", 13),
        new("Victory", "胜利", "base", 14),
        new("Sk1", "一技能", "skill", 100),
        new("Sk2", "二技能", "skill", 101),
        new("KO", "终结技", "skill", 102),
        new("Sub", "护援技", "skill", 103)
    ];

    public UnrealProjectSyncCheckResult Check(string? enginePath, string? projectPath)
    {
        var items = new List<UnrealProjectSyncCheckItem>();
        var normalizedEnginePath = NormalizePath(enginePath);
        var normalizedProjectPath = NormalizePath(projectPath);
        var engineExists = File.Exists(normalizedEnginePath);
        var projectExists = File.Exists(normalizedProjectPath) &&
            string.Equals(Path.GetExtension(normalizedProjectPath), ".uproject", StringComparison.OrdinalIgnoreCase);
        items.Add(new UnrealProjectSyncCheckItem(
            "虚幻引擎",
            normalizedEnginePath,
            engineExists,
            engineExists ? "已找到 UnrealEditor.exe。" : "请选择 Engine/Binaries/Win64/UnrealEditor.exe。"));
        items.Add(new UnrealProjectSyncCheckItem(
            "虚幻项目",
            normalizedProjectPath,
            projectExists,
            projectExists ? "已找到 .uproject 文件。" : "请选择目标 Unreal 项目的 .uproject 文件。"));

        var contentPath = projectExists
            ? Path.Combine(Path.GetDirectoryName(normalizedProjectPath)!, "Content")
            : string.Empty;
        var contentExists = Directory.Exists(contentPath);
        items.Add(new UnrealProjectSyncCheckItem(
            "Content 文件夹",
            contentPath,
            contentExists,
            contentExists ? "项目 Content 目录存在。" : "未找到项目 Content 目录。"));

        var baseMaterialDiskPath = contentExists
            ? CombineContentPath(contentPath, TargetBaseMaterialContentPath)
            : string.Empty;
        var zdDiskPath = contentExists
            ? CombineContentPath(contentPath, TargetZdContentPath)
            : string.Empty;
        var characterItemDiskPath = contentExists
            ? CombineContentPath(contentPath, TargetCharacterItemContentPath)
            : string.Empty;
        var linkSkillLibraryDiskPath = contentExists
            ? CombineObjectPath(contentPath, LinkSkillLibraryObjectPath)
            : string.Empty;
        var baseMaterialExists = Directory.Exists(baseMaterialDiskPath);
        var zdExists = Directory.Exists(zdDiskPath);
        var characterItemExists = Directory.Exists(characterItemDiskPath);
        var linkSkillLibraryExists = File.Exists(linkSkillLibraryDiskPath);
        items.Add(new UnrealProjectSyncCheckItem(
            "目标基础素材文件夹",
            baseMaterialDiskPath,
            baseMaterialExists,
            baseMaterialExists ? "基础素材目标目录存在。" : "未找到基础素材目标目录。"));
        items.Add(new UnrealProjectSyncCheckItem(
            "目标 ZD 文件夹",
            zdDiskPath,
            zdExists,
            zdExists ? "ZD 目标目录存在。" : "未找到 ZD 目标目录。"));
        items.Add(new UnrealProjectSyncCheckItem(
            "目标角色道具文件夹",
            characterItemDiskPath,
            characterItemExists,
            characterItemExists ? "角色道具目录存在。" : "未找到角色道具目录，St3 角色信息无法读取。"));
        items.Add(new UnrealProjectSyncCheckItem(
            "连携函数库",
            linkSkillLibraryDiskPath,
            linkSkillLibraryExists,
            linkSkillLibraryExists ? "已找到 LB_Fucs.uasset。" : "未找到 LB_Fucs.uasset，连携技无法读取。"));

        var exportDirectoryPath = projectExists
            ? GetExportDirectoryPath(normalizedProjectPath)
            : string.Empty;
        var exportScriptPath = string.IsNullOrWhiteSpace(exportDirectoryPath)
            ? string.Empty
            : GetExportScriptPath();
        var exportScriptExists = File.Exists(exportScriptPath);
        var exportManifestPath = string.IsNullOrWhiteSpace(exportDirectoryPath)
            ? string.Empty
            : Path.Combine(exportDirectoryPath, ExportManifestFileName);
        var exportManifest = LoadExportManifest(exportManifestPath);
        var exportManifestExists = exportManifest is not null;
        items.Add(new UnrealProjectSyncCheckItem(
            "工具箱导出脚本",
            exportScriptPath,
            exportScriptExists,
            exportScriptExists ? "工具箱内置 Unreal 导出脚本存在。" : "工具箱内置 Unreal 导出脚本缺失。"));
        items.Add(new UnrealProjectSyncCheckItem(
            "导出清单",
            exportManifestPath,
            exportManifestExists,
            exportManifestExists
                ? $"已找到 Unreal 导出的角色资产清单，共 {exportManifest!.Assets.Count} 个资产，角色物品 {exportManifest.CharacterItems.Count} 个。"
                : "尚未导出 characters.json；请先运行“从 Unreal 导出清单”。"));

        var canSync = engineExists &&
            projectExists &&
            contentExists &&
            baseMaterialExists &&
            zdExists &&
            characterItemExists &&
            linkSkillLibraryExists &&
            exportScriptExists;
        return new UnrealProjectSyncCheckResult(
            canSync ? InfoBarSeverity.Success : InfoBarSeverity.Warning,
            canSync ? "关联检测通过" : "关联未完整",
            canSync
                ? "虚幻引擎、项目文件和两个目标内容目录均存在。"
                : "请补全缺失路径后重新检测；当前不会执行同步。",
            canSync,
            contentPath,
            TargetBaseMaterialContentPath,
            TargetZdContentPath,
            TargetCharacterItemContentPath,
            LinkSkillLibraryObjectPath,
            baseMaterialDiskPath,
            zdDiskPath,
            characterItemDiskPath,
            linkSkillLibraryDiskPath,
            exportDirectoryPath,
            exportScriptPath,
            exportManifestPath,
            exportManifestExists,
            (exportManifest?.Assets.Count ?? 0) + (exportManifest?.CharacterItems.Count ?? 0) + (exportManifest?.CharacterSequences.Count ?? 0),
            ParseGeneratedAt(exportManifest?.GeneratedAt),
            BuildPreviewAssets(exportManifest),
            BuildCharacterCandidates(exportManifest, contentPath),
            items);
    }

    public void ValidatePublishCharacterFolders(string projectPath, string characterCode, bool requireAssetTypes = false)
    {
        var invalid = CheckPublishCharacterFolders(projectPath, characterCode, requireAssetTypes)
            .Where(item => !item.IsCompliant)
            .ToArray();
        if (invalid.Length == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            "底层检测未通过，请先在 Unreal 内容浏览器中整理以下目录或资产：\n" +
            string.Join("\n", invalid.Select(item =>
                $"- {item.DisplayName}：应为 {item.ExpectedPath}" +
                (string.IsNullOrWhiteSpace(item.ExpectedType) ? string.Empty : $"；类型应为 {item.ExpectedType}") +
                (string.IsNullOrWhiteSpace(item.ActualPath) ? string.Empty : $"；当前为 {item.ActualPath}") +
                (string.IsNullOrWhiteSpace(item.ActualType) ? string.Empty : $"；当前类型 {item.ActualType}"))));
    }

    public IReadOnlyList<UnrealPublishFoundationCheckItem> CheckPublishCharacterFolders(
        string projectPath,
        string characterCode,
        bool requireAssetTypes = false)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || string.IsNullOrWhiteSpace(characterCode))
        {
            throw new InvalidOperationException("检测角色目录前必须选择 Unreal 项目和已完成角色。");
        }

        var contentPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath))!, "Content");
        var baseFolder = CheckExactCharacterFolder(contentPath, TargetBaseMaterialContentPath, characterCode, "角色图片素材");
        var actorFolder = CheckExactCharacterFolder(contentPath, TargetZdContentPath, characterCode, "角色战斗素材");
        var actorRootPath = CombineContentPath(contentPath, $"{TargetZdContentPath}/{characterCode}");
        var actorObjectPath = $"{TargetZdContentPath}/{characterCode}";
        var soundRootPath = Path.Combine(actorRootPath, "Sound");
        var soundObjectPath = $"{actorObjectPath}/Sound";
        var itemObjectPath = $"{TargetCharacterItemContentPath}/Item_{characterCode}.Item_{characterCode}";
        var manifest = requireAssetTypes
            ? LoadExportManifest(Path.Combine(GetExportDirectoryPath(projectPath), ExportManifestFileName))
            : null;
        return new List<UnrealPublishFoundationCheckItem>
        {
            CheckExactAsset(contentPath, CombineContentPath(contentPath, TargetCharacterItemContentPath), TargetCharacterItemContentPath,
                $"Item_{characterCode}", "角色 Item", "Blueprint", manifest, requireAssetTypes),
            baseFolder,
            actorFolder,
            CheckExactAsset(contentPath, actorRootPath, actorObjectPath, characterCode, "角色蓝图", "Blueprint", manifest, requireAssetTypes, name =>
                !name.EndsWith("_AnimBP", StringComparison.OrdinalIgnoreCase) &&
                !name.EndsWith("_AnimMaps", StringComparison.OrdinalIgnoreCase)),
            CheckExactAsset(contentPath, actorRootPath, actorObjectPath, $"{characterCode}_AnimBP", "动画蓝图", "PaperZDAnimBP", manifest, requireAssetTypes,
                name => name.EndsWith("_AnimBP", StringComparison.OrdinalIgnoreCase)),
            CheckExactAsset(contentPath, actorRootPath, actorObjectPath, $"{characterCode}_AnimMaps", "动画库", "PaperZDAnimationSource_Flipbook", manifest, requireAssetTypes,
                name => name.EndsWith("_AnimMaps", StringComparison.OrdinalIgnoreCase)),
            CheckChildFolder(actorRootPath, actorObjectPath, "BUFF", "个人 BUFF 素材"),
            CheckChildFolder(actorRootPath, actorObjectPath, "Material", "序列帧素材"),
            CheckChildFolder(actorRootPath, actorObjectPath, "Sound", "角色声音"),
            CheckChildFolder(soundRootPath, soundObjectPath, "Other", "待分配语音目录"),
            CheckExactAsset(contentPath, soundRootPath, soundObjectPath, $"{characterCode}_OnDM", "Meta 受击音", "MetaSoundSource", manifest, requireAssetTypes,
                name => name.EndsWith("_OnDM", StringComparison.OrdinalIgnoreCase)),
            CheckExactAsset(contentPath, soundRootPath, soundObjectPath, $"{characterCode}_Con_Talk", "语音并发", "SoundConcurrency", manifest, requireAssetTypes,
                name => name.EndsWith("_Con_Talk", StringComparison.OrdinalIgnoreCase)),
            CheckExactAsset(contentPath, soundRootPath, soundObjectPath, $"{characterCode}_Con_Ondm", "受击并发", "SoundConcurrency", manifest, requireAssetTypes,
                name => name.EndsWith("_Con_Ondm", StringComparison.OrdinalIgnoreCase)),
            CheckChildFolder(actorRootPath, actorObjectPath, "AnimSequences", "动画序列"),
            CheckChildFolder(actorRootPath, actorObjectPath, "ExAsset", "其他素材")
        };
    }

    private static UnrealPublishFoundationCheckItem CheckExactCharacterFolder(
        string contentPath,
        string contentRoot,
        string characterCode,
        string displayName)
    {
        var rootPath = CombineContentPath(contentPath, contentRoot);
        var exactFolder = Directory.Exists(rootPath)
            ? Directory.EnumerateDirectories(rootPath)
                .FirstOrDefault(path => string.Equals(Path.GetFileName(path), characterCode, StringComparison.Ordinal))
            : null;
        if (exactFolder is not null)
        {
            return new(displayName, $"{contentRoot}/{characterCode}", $"{contentRoot}/{characterCode}", true);
        }

        var legacyFolder = Directory.Exists(rootPath)
            ? Directory.EnumerateDirectories(rootPath)
                .FirstOrDefault(path =>
                    string.Equals(Path.GetFileName(path), characterCode, StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(path).EndsWith($"_{characterCode}", StringComparison.OrdinalIgnoreCase))
            : null;
        return new(
            displayName,
            $"{contentRoot}/{characterCode}",
            legacyFolder is null ? string.Empty : ToGameContentPath(contentPath, legacyFolder),
            false);
    }

    private static UnrealPublishFoundationCheckItem CheckChildFolder(
        string actorRootPath,
        string actorObjectPath,
        string folderName,
        string displayName)
    {
        var expectedObjectPath = $"{actorObjectPath}/{folderName}";
        var exists = Directory.Exists(Path.Combine(actorRootPath, folderName));
        return new(displayName, expectedObjectPath, exists ? expectedObjectPath : string.Empty, exists);
    }

    private static UnrealPublishFoundationCheckItem CheckExactAsset(
        string contentPath,
        string diskFolder,
        string objectFolder,
        string expectedAssetName,
        string displayName,
        string expectedType,
        UnrealProjectExportManifest? manifest,
        bool requireAssetType,
        Func<string, bool>? legacyPredicate = null)
    {
        var expectedFile = Path.Combine(diskFolder, $"{expectedAssetName}.uasset");
        var expectedObjectPath = $"{objectFolder}/{expectedAssetName}.{expectedAssetName}";
        if (!File.Exists(expectedFile))
        {
            var legacyFile = Directory.Exists(diskFolder) && legacyPredicate is not null
            ? Directory.EnumerateFiles(diskFolder, "*.uasset", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(path => legacyPredicate(Path.GetFileNameWithoutExtension(path)))
            : null;
            return new(
                displayName,
                expectedObjectPath,
                legacyFile is null ? string.Empty : ToGameObjectPath(contentPath, legacyFile),
                false,
                legacyFile is null ? "缺失" : "命名不规范",
                expectedType);
        }

        if (!requireAssetType)
        {
            return new(displayName, expectedObjectPath, expectedObjectPath, true, ExpectedType: expectedType, ActualType: "等待 Unreal 类型复检");
        }

        var rawAssetClass = manifest?.Assets.FirstOrDefault(item =>
                string.Equals(item.ObjectPath, expectedObjectPath, StringComparison.OrdinalIgnoreCase))?.AssetClass ??
            manifest?.CharacterItems.FirstOrDefault(item =>
                string.Equals(item.ObjectPath, expectedObjectPath, StringComparison.OrdinalIgnoreCase))?.AssetClass;
        var actualType = GetAssetClassName(rawAssetClass);
        var typeMatches = !string.IsNullOrWhiteSpace(actualType) && string.Equals(actualType, expectedType, StringComparison.OrdinalIgnoreCase);
        return new(
            displayName,
            expectedObjectPath,
            expectedObjectPath,
            typeMatches,
            typeMatches ? string.Empty : string.IsNullOrWhiteSpace(actualType) ? "未读取类型" : "类型错误",
            expectedType,
            actualType);
    }

    private static string GetAssetClassName(string? assetClass)
    {
        if (string.IsNullOrWhiteSpace(assetClass)) return string.Empty;
        const string marker = "asset_name: \"";
        var start = assetClass.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return assetClass.Trim();
        start += marker.Length;
        var end = assetClass.IndexOf('"', start);
        return end > start ? assetClass[start..end] : assetClass.Trim();
    }

    private static string ToGameContentPath(string contentPath, string diskPath)
    {
        var relative = Path.GetRelativePath(contentPath, diskPath).Replace('\\', '/');
        return $"/Game/{relative}";
    }

    private static string ToGameObjectPath(string contentPath, string diskPath)
    {
        var packagePath = ToGameContentPath(contentPath, Path.ChangeExtension(diskPath, null));
        var assetName = Path.GetFileNameWithoutExtension(diskPath);
        return $"{packagePath}.{assetName}";
    }

    public string GetExportScriptPath()
    {
        return Path.Combine(AppContext.BaseDirectory, ExportScriptRelativePath);
    }

    public ProcessStartInfo BuildExportProcessStartInfo(
        string? enginePath,
        string? projectPath,
        IReadOnlyCollection<string>? selectedCharacterCodes = null,
        bool validateProjectModules = true,
        UnrealProjectSyncExportScope scope = UnrealProjectSyncExportScope.Full)
    {
        var normalizedEnginePath = NormalizePath(enginePath);
        var normalizedProjectPath = NormalizePath(projectPath);
        if (!File.Exists(normalizedEnginePath))
        {
            throw new InvalidOperationException("请先选择有效的 UnrealEditor.exe。");
        }

        if (!File.Exists(normalizedProjectPath) ||
            !string.Equals(Path.GetExtension(normalizedProjectPath), ".uproject", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("请先选择有效的 Unreal .uproject 文件。");
        }

        if (validateProjectModules)
        {
            ValidateProjectModuleBuildIds(normalizedEnginePath, normalizedProjectPath);
        }

        var scriptPath = GetExportScriptPath();
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException("工具箱内置 Unreal 导出脚本不存在。", scriptPath);
        }

        var exportDirectoryPath = GetExportDirectoryPath(normalizedProjectPath);
        Directory.CreateDirectory(exportDirectoryPath);
        var manifestPath = Path.Combine(exportDirectoryPath, ExportManifestFileName);
        var editorCommandPath = ResolveEditorCommandPath(normalizedEnginePath);
        var startInfo = new ProcessStartInfo
        {
            FileName = editorCommandPath,
            Arguments = $"{Quote(normalizedProjectPath)} -run=pythonscript -script={Quote(scriptPath)} -unattended -nop4",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(editorCommandPath) ?? string.Empty
        };
        startInfo.Environment["ZD_TOOLBOX_PROJECT_PATH"] = normalizedProjectPath;
        startInfo.Environment["ZD_TOOLBOX_EXPORT_MANIFEST"] = manifestPath;
        startInfo.Environment["ZD_TOOLBOX_EXPORT_PROGRESS"] = GetExportProgressPath(manifestPath);
        startInfo.Environment["ZD_TOOLBOX_TARGET_PATHS"] = $"[{string.Join(", ", ExportTargetContentPaths.Select(ToJsonStringLiteral))}]";
        startInfo.Environment["ZD_TOOLBOX_EXPORT_SCOPE"] = scope.ToString();
        if (selectedCharacterCodes is { Count: > 0 })
        {
            startInfo.Environment["ZD_TOOLBOX_SELECTED_CHARACTERS"] =
                $"[{string.Join(", ", selectedCharacterCodes.Select(ToJsonStringLiteral))}]";
        }
        return startInfo;
    }

    private static void ValidateProjectModuleBuildIds(string editorPath, string projectPath)
    {
        var editorDirectory = Path.GetDirectoryName(editorPath);
        var projectDirectory = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrWhiteSpace(editorDirectory) || string.IsNullOrWhiteSpace(projectDirectory))
        {
            return;
        }

        var engineManifest = TryReadModuleManifest(Path.Combine(editorDirectory, "UnrealEditor.modules"));
        if (engineManifest is null || string.IsNullOrWhiteSpace(engineManifest.BuildId))
        {
            return;
        }

        var manifestPaths = new List<string>();
        var projectManifestPath = Path.Combine(projectDirectory, "Binaries", "Win64", "UnrealEditor.modules");
        if (File.Exists(projectManifestPath))
        {
            manifestPaths.Add(projectManifestPath);
        }

        var pluginsPath = Path.Combine(projectDirectory, "Plugins");
        if (Directory.Exists(pluginsPath))
        {
            manifestPaths.AddRange(Directory.EnumerateFiles(
                pluginsPath,
                "UnrealEditor.modules",
                SearchOption.AllDirectories));
        }

        var mismatches = manifestPaths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => (Path: path, Manifest: TryReadModuleManifest(path)))
            .Where(item =>
                item.Manifest is not null &&
                !string.IsNullOrWhiteSpace(item.Manifest.BuildId) &&
                !string.Equals(item.Manifest.BuildId, engineManifest.BuildId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (mismatches.Length == 0)
        {
            return;
        }

        var moduleNames = mismatches
            .SelectMany(item => item.Manifest!.Modules)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var projectBuildIds = mismatches
            .Select(item => item.Manifest!.BuildId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(buildId => buildId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var engineDirectory = Path.GetFullPath(Path.Combine(editorDirectory, "..", ".."));
        var buildScriptPath = Path.Combine(engineDirectory, "Build", "BatchFiles", "Build.bat");
        var targetName = $"{Path.GetFileNameWithoutExtension(projectPath)}Editor";
        var buildCommand = $"{Quote(buildScriptPath)} {targetName} Win64 Development -Project={Quote(projectPath)} -WaitMutex -FromMSBuild";

        throw new InvalidOperationException(
            "当前 Unreal 引擎与项目现有 C++/插件二进制版本不一致，完整导入尚未执行。\n" +
            $"当前引擎 BuildId：{engineManifest.BuildId}\n" +
            $"项目二进制 BuildId：{string.Join("、", projectBuildIds)}\n" +
            $"需要重新编译的模块：{string.Join("、", moduleNames)}\n" +
            "请先关闭 Unreal Editor，并使用当前引擎重新编译项目：\n" +
            buildCommand);
    }

    private static UnrealModuleManifest? TryReadModuleManifest(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            var root = document.RootElement;
            if (!root.TryGetProperty("BuildId", out var buildIdElement))
            {
                return null;
            }

            var buildId = buildIdElement.GetString() ?? string.Empty;
            var modules = root.TryGetProperty("Modules", out var modulesElement) &&
                modulesElement.ValueKind == JsonValueKind.Object
                ? modulesElement.EnumerateObject().Select(property => property.Name).ToArray()
                : [];
            return new UnrealModuleManifest(buildId, modules);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private sealed record UnrealModuleManifest(string BuildId, IReadOnlyList<string> Modules);

    public async Task<UnrealProjectSyncExportRunResult> ExportProjectCharactersAsync(
        string? enginePath,
        string? projectPath,
        IReadOnlyCollection<string>? selectedCharacterCodes = null,
        IProgress<ProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default,
        UnrealProjectSyncExportScope scope = UnrealProjectSyncExportScope.Full)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new ProgressUpdate("正在校验虚幻同步定位...", 5, "检查引擎、项目和工具箱内置导出脚本。"));
        var selectedCodes = NormalizeSelectedCharacterCodes(selectedCharacterCodes);
        var normalizedProjectPath = NormalizePath(projectPath);
        var manifestPath = Path.Combine(GetExportDirectoryPath(normalizedProjectPath), ExportManifestFileName);
        var progressPath = GetExportProgressPath(manifestPath);
        var taskExecutionService = new UnrealPythonTaskExecutionService();
        var useRunningEditor = taskExecutionService.ShouldUseRunningEditor();
        var offlineStartInfo = BuildExportProcessStartInfo(
            enginePath,
            projectPath,
            selectedCodes,
            validateProjectModules: !useRunningEditor,
            scope: scope);
        offlineStartInfo.RedirectStandardOutput = true;
        offlineStartInfo.RedirectStandardError = true;
        var launch = taskExecutionService.BuildLaunch(
            NormalizePath(enginePath),
            normalizedProjectPath,
            GetExportScriptPath(),
            Path.Combine(Path.GetDirectoryName(manifestPath)!, "export.remote-job.json"),
            offlineStartInfo,
            useRunningEditor);
        var startInfo = launch.StartInfo;
        TryDeleteFile(progressPath);
        progress?.Report(new ProgressUpdate("正在准备导出目录...", 18, manifestPath));
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);

        progress?.Report(new ProgressUpdate(
            launch.UsesRunningEditor ? "正在连接已打开的 Unreal Editor..." : "正在启动 Unreal Editor 命令进程...",
            35,
            startInfo.FileName));
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 Unreal Python 任务进程。");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        progress?.Report(new ProgressUpdate(
            launch.UsesRunningEditor
                ? "已连接 Unreal Editor，正在执行导出脚本..."
                : "Unreal 已启动，正在加载项目并执行导出脚本...",
            45,
            selectedCodes.Count == 0
                ? "首次加载项目可能需要较长时间。"
                : $"本次只获取 {selectedCodes.Count} 个选中角色：{string.Join("、", selectedCodes)}。"));

        var waitStartedAt = DateTime.UtcNow;
        var lastProgressReportAt = DateTime.MinValue;
        while (!process.HasExited)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                KillProcessTree(process);
                cancellationToken.ThrowIfCancellationRequested();
            }

            var elapsed = DateTime.UtcNow - waitStartedAt;
            if (elapsed >= TimeSpan.FromMinutes(30))
            {
                KillProcessTree(process);
                throw new TimeoutException("Unreal Editor 导出超过 30 分钟，已终止进程。");
            }

            if ((DateTime.UtcNow - lastProgressReportAt).TotalSeconds >= 2)
            {
                lastProgressReportAt = DateTime.UtcNow;
                var percent = Math.Min(90, 45 + elapsed.TotalSeconds / 180d * 40d);
                var scriptProgress = TryReadExportProgress(progressPath);
                progress?.Report(scriptProgress is null
                    ? new ProgressUpdate(
                        "Unreal 正在扫描角色资源并写入导出清单...",
                        percent,
                        $"已等待 {FormatElapsed(elapsed)}")
                    : new ProgressUpdate(
                        scriptProgress.Message,
                        Math.Clamp(scriptProgress.Percent, 45, 93),
                        $"{scriptProgress.Detail}  |  已等待 {FormatElapsed(elapsed)}",
                        scriptProgress.IsIndeterminate));
            }

            await Task.Delay(1000, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new ProgressUpdate("正在读取 Unreal 导出结果...", 94, manifestPath));
        var output = await outputTask + await errorTask;
        if (process.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(output)
                ? "Unreal 未返回标准输出；请查看项目 Saved/Logs 下的最新日志。"
                : output.Trim();
            if (detail.Length > 4000)
            {
                detail = detail[^4000..];
            }

            throw new InvalidOperationException(
                $"Unreal 角色数据导出失败，退出码 {process.ExitCode}。\n{detail}");
        }

        var manifest = LoadExportManifest(manifestPath);
        if (manifest is null)
        {
            throw new InvalidOperationException($"Unreal 导出进程已结束，但没有生成有效清单：{manifestPath}");
        }

        var assetCount = manifest?.Assets.Count ?? 0;
        var characterItemCount = manifest?.CharacterItems.Count ?? 0;
        var characterSequenceCount = manifest?.CharacterSequences.Count ?? 0;
        var totalCount = assetCount + characterItemCount + characterSequenceCount;
        progress?.Report(new ProgressUpdate(
            "项目角色导出完成。",
            100,
            $"导出资产 {assetCount} 个，角色物品 {characterItemCount} 个，序列预览 {characterSequenceCount} 个，BUFF {(manifest?.CharacterBuffs.Count ?? 0)} 组。"));
        return new UnrealProjectSyncExportRunResult(process.ExitCode, manifestPath, totalCount, output);
    }

    public string GetExportDirectoryPath(string projectPath)
    {
        return Path.Combine(Path.GetDirectoryName(projectPath)!, "Intermediate", ExportFolderName);
    }

    private static string GetExportProgressPath(string manifestPath)
    {
        return Path.Combine(Path.GetDirectoryName(manifestPath)!, "characters.progress.json");
    }

    private static UnrealExportProgressState? TryReadExportProgress(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(
                File.ReadAllText(path, Encoding.UTF8),
                AppJsonSerializerContext.Default.UnrealExportProgressState);
        }
        catch
        {
            return null;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(path.Trim().Trim('"'));
        }
        catch
        {
            return path.Trim().Trim('"');
        }
    }

    private static string CombineContentPath(string contentPath, string unrealContentPath)
    {
        var relativePath = unrealContentPath.TrimStart('/');
        const string gameRootPrefix = "Game/";
        if (relativePath.StartsWith(gameRootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            relativePath = relativePath[gameRootPrefix.Length..];
        }

        return Path.Combine(
            contentPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string CombineObjectPath(string contentPath, string unrealObjectPath)
    {
        var packagePath = unrealObjectPath.TrimStart('/');
        if (packagePath.StartsWith("Game/", StringComparison.OrdinalIgnoreCase))
        {
            packagePath = packagePath["Game/".Length..];
        }

        var dotIndex = packagePath.IndexOf('.', StringComparison.Ordinal);
        if (dotIndex >= 0)
        {
            packagePath = packagePath[..dotIndex];
        }

        return Path.Combine(
            contentPath,
            packagePath.Replace('/', Path.DirectorySeparatorChar) + ".uasset");
    }

    private static UnrealProjectExportManifest? LoadExportManifest(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(
                File.ReadAllText(path, Encoding.UTF8),
                AppJsonSerializerContext.Default.UnrealProjectExportManifest);
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<UnrealProjectSyncExportAssetView> BuildPreviewAssets(UnrealProjectExportManifest? manifest)
    {
        if (manifest is null)
        {
            return [];
        }

        return manifest.Assets
            .OrderBy(asset => asset.SourceRoot, StringComparer.OrdinalIgnoreCase)
            .ThenBy(asset => asset.PackagePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(asset => asset.AssetName, StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .Select(asset => new UnrealProjectSyncExportAssetView(
                asset.AssetName,
                asset.AssetClass,
                asset.PackagePath,
                asset.ObjectPath,
                asset.SourceRoot,
                asset.ExportedFilePath))
            .ToArray();
    }

    private static IReadOnlyCollection<string> NormalizeSelectedCharacterCodes(IReadOnlyCollection<string>? selectedCharacterCodes)
    {
        if (selectedCharacterCodes is null || selectedCharacterCodes.Count == 0)
        {
            return [];
        }

        return selectedCharacterCodes
            .Select(code => code.Trim())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<UnrealProjectSyncCharacterCandidate> BuildCharacterCandidates(
        UnrealProjectExportManifest? manifest,
        string contentPath)
    {
        var manifestGeneratedAt = ParseGeneratedAt(manifest?.GeneratedAt);
        var detailedCandidates = BuildManifestCharacterCandidates(manifest)
            .ToDictionary(candidate => candidate.Code, StringComparer.OrdinalIgnoreCase);
        var summaryNames = (manifest?.CharacterSummaries ?? [])
            .Where(summary => !string.IsNullOrWhiteSpace(summary.Code) && !string.IsNullOrWhiteSpace(summary.DisplayName))
            .GroupBy(summary => summary.Code.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().DisplayName.Trim(),
                StringComparer.OrdinalIgnoreCase);
        foreach (var item in ReadCharacterItemDisplayNames(contentPath))
        {
            summaryNames[item.Key] = item.Value;
        }
        var scannedCandidates = BuildScannedCharacterCandidates(contentPath)
            .ToDictionary(candidate => candidate.Code, StringComparer.OrdinalIgnoreCase);

        if (scannedCandidates.Count == 0)
        {
            return detailedCandidates.Values
                .OrderBy(candidate => candidate.Code, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var candidateCodes = scannedCandidates.Keys
            .Union(detailedCandidates.Keys, StringComparer.OrdinalIgnoreCase)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return candidateCodes
            .Select(code =>
            {
                if (!scannedCandidates.TryGetValue(code, out var candidate))
                {
                    return detailedCandidates[code];
                }

                if (!detailedCandidates.TryGetValue(code, out var detailed))
                {
                    return summaryNames.TryGetValue(code, out var scannedDisplayName)
                        ? CloneCharacterCandidateWithLatestData(candidate, false, scannedDisplayName)
                        : candidate;
                }

                var displayName = summaryNames.TryGetValue(code, out var summaryDisplayName)
                    ? summaryDisplayName
                    : detailed.DisplayName;

                if (manifestGeneratedAt is null || HasCharacterFolderChangedAfterExport(contentPath, code, manifestGeneratedAt.Value))
                {
                    return CloneCharacterCandidateWithLatestData(detailed, false, displayName);
                }

                return string.Equals(displayName, detailed.DisplayName, StringComparison.Ordinal)
                    ? detailed
                    : CloneCharacterCandidateWithLatestData(detailed, true, displayName);
            })
            .OrderBy(candidate => candidate.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<UnrealProjectSyncCharacterCandidate> BuildScannedCharacterCandidates(string contentPath)
    {
        if (string.IsNullOrWhiteSpace(contentPath) || !Directory.Exists(contentPath))
        {
            return [];
        }

        var baseMaterialDiskPath = CombineContentPath(contentPath, TargetBaseMaterialContentPath);
        var zdDiskPath = CombineContentPath(contentPath, TargetZdContentPath);
        if (!Directory.Exists(baseMaterialDiskPath) && !Directory.Exists(zdDiskPath))
        {
            return [];
        }

        var baseFolders = Directory.Exists(baseMaterialDiskPath)
            ? Directory.EnumerateDirectories(baseMaterialDiskPath)
                .Select(path => (Code: Path.GetFileName(path), Path: path))
                .Where(item => !string.IsNullOrWhiteSpace(item.Code))
                .ToDictionary(item => item.Code, item => item.Path, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var zdFolders = Directory.Exists(zdDiskPath)
            ? Directory.EnumerateDirectories(zdDiskPath)
                .Select(path => (Code: Path.GetFileName(path), Path: path))
                .Where(item => !string.IsNullOrWhiteSpace(item.Code))
                .ToDictionary(item => item.Code, item => item.Path, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return baseFolders.Keys
            .Union(zdFolders.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .Select(code =>
            {
                var baseAssetCount = baseFolders.TryGetValue(code, out var baseFolder)
                    ? CountUassetFiles(baseFolder)
                    : 0;
                var zdAssetCount = zdFolders.TryGetValue(code, out var zdFolder)
                    ? CountUassetFiles(Path.Combine(zdFolder, "Material"))
                    : 0;
                return new UnrealProjectSyncCharacterCandidate(
                    code,
                    code,
                    $"{TargetBaseMaterialContentPath}/{code}",
                    $"{TargetZdContentPath}/{code}",
                    baseAssetCount,
                    zdAssetCount,
                    BuildCharacterInfoPreview(null, null),
                    BuildSkillsPreview(null, null, null, null, new Dictionary<string, UnrealProjectExportAsset>(StringComparer.OrdinalIgnoreCase)),
                    BuildSequenceFramesPreview(null),
                    BuildBuffsPreview(null),
                    [],
                    hasLatestData: false);
            })
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string> ReadCharacterItemDisplayNames(string contentPath)
    {
        if (string.IsNullOrWhiteSpace(contentPath) || !Directory.Exists(contentPath))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var itemFolderPath = CombineContentPath(contentPath, TargetCharacterItemContentPath);
        if (!Directory.Exists(itemFolderPath))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var filePath in Directory.EnumerateFiles(itemFolderPath, "Item_*.uasset", SearchOption.TopDirectoryOnly))
        {
            var assetName = Path.GetFileNameWithoutExtension(filePath);
            if (assetName.Length <= "Item_".Length)
            {
                continue;
            }

            try
            {
                var displayName = ReadFirstLocalizedText(File.ReadAllBytes(filePath));
                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    names[assetName["Item_".Length..]] = displayName;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return names;
    }

    private static string ReadFirstLocalizedText(ReadOnlySpan<byte> bytes)
    {
        for (var offset = 0; offset <= bytes.Length - 8; offset++)
        {
            var serializedLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset, sizeof(int)));
            if (serializedLength is > -2 or < -128)
            {
                continue;
            }

            var characterCount = -serializedLength;
            var byteCount = characterCount * sizeof(char);
            var valueOffset = offset + sizeof(int);
            if (valueOffset + byteCount > bytes.Length ||
                bytes[valueOffset + byteCount - 2] != 0 ||
                bytes[valueOffset + byteCount - 1] != 0)
            {
                continue;
            }

            try
            {
                var value = StrictUnicodeEncoding.GetString(bytes.Slice(valueOffset, byteCount - sizeof(char))).Trim();
                if (IsUsableCharacterDisplayName(value))
                {
                    return value;
                }
            }
            catch (DecoderFallbackException)
            {
            }
        }

        return string.Empty;
    }

    private static bool IsUsableCharacterDisplayName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64 || value.Any(char.IsControl))
        {
            return false;
        }

        return value.Any(character =>
            character is >= '\u3400' and <= '\u4DBF' or
            >= '\u4E00' and <= '\u9FFF' or
            >= '\uF900' and <= '\uFAFF' or
            >= '\u3040' and <= '\u30FF');
    }

    private static UnrealProjectSyncCharacterCandidate CloneCharacterCandidateWithLatestData(
        UnrealProjectSyncCharacterCandidate candidate,
        bool hasLatestData,
        string? displayName = null)
    {
        return new UnrealProjectSyncCharacterCandidate(
            candidate.Code,
            string.IsNullOrWhiteSpace(displayName) ? candidate.DisplayName : displayName,
            candidate.BaseMaterialPath,
            candidate.ZdPath,
            candidate.BaseMaterialAssetCount,
            candidate.ZdAssetCount,
            candidate.CharacterInfo,
            candidate.SkillsPreview,
            candidate.SequenceFramesPreview,
            candidate.BuffsPreview,
            candidate.MaterialBuckets,
            hasLatestData,
            candidate.VoiceBuckets);
    }

    private static bool HasCharacterFolderChangedAfterExport(string contentPath, string code, DateTime exportGeneratedAt)
    {
        if (string.IsNullOrWhiteSpace(contentPath) || string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var exportUtc = exportGeneratedAt.Kind == DateTimeKind.Utc
            ? exportGeneratedAt
            : exportGeneratedAt.ToUniversalTime();
        var roots = new[]
        {
            Path.Combine(CombineContentPath(contentPath, TargetBaseMaterialContentPath), code),
            Path.Combine(CombineContentPath(contentPath, TargetZdContentPath), code)
        };

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            if (Directory.EnumerateFiles(root, "*.uasset", SearchOption.AllDirectories)
                .Any(path => File.GetLastWriteTimeUtc(path) > exportUtc))
            {
                return true;
            }
        }

        return false;
    }

    private static int CountUassetFiles(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return 0;
        }

        return Directory.EnumerateFiles(path, "*.uasset", SearchOption.AllDirectories).Count();
    }

    private static IReadOnlyList<UnrealProjectSyncCharacterCandidate> BuildManifestCharacterCandidates(UnrealProjectExportManifest? manifest)
    {
        if (manifest is null)
        {
            return [];
        }

        var assets = manifest.Assets
            .Where(asset => !string.IsNullOrWhiteSpace(asset.PackagePath))
            .ToArray();
        var assetLookup = BuildExportAssetLookup(assets);
        var baseGroups = GroupByCharacterFolder(assets, TargetBaseMaterialContentPath);
        var zdGroups = GroupByCharacterFolder(assets, TargetZdContentPath);
        var actorMap = manifest.CharacterActors
            .GroupBy(actor => actor.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var sequenceMap = manifest.CharacterSequences
            .GroupBy(sequence => sequence.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var buffMap = manifest.CharacterBuffs
            .GroupBy(buffSet => buffSet.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var candidateCodes = baseGroups.Keys
            .Union(zdGroups.Keys, StringComparer.OrdinalIgnoreCase)
            .Union(manifest.CharacterItems.Select(item => item.Code), StringComparer.OrdinalIgnoreCase)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return candidateCodes
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .Select(code =>
            {
                var baseAssets = baseGroups.TryGetValue(code, out var matchedBaseAssets) ? matchedBaseAssets : [];
                var zdAssets = zdGroups.TryGetValue(code, out var matchedZdAssets) ? matchedZdAssets : [];
                var personalBuffRoot = $"{TargetZdContentPath}/{code}/BUFF";
                var materialAssets = baseAssets
                    .Concat(zdAssets.Where(asset =>
                        IsTextureAsset(asset) &&
                        (string.Equals(asset.PackagePath, personalBuffRoot, StringComparison.OrdinalIgnoreCase) ||
                         asset.PackagePath.StartsWith(personalBuffRoot + "/", StringComparison.OrdinalIgnoreCase))))
                    .ToArray();
                var zdMaterialTextureCount = CountZdMaterialTextures(zdAssets, code);
                var characterItem = ResolveCharacterItem(code, baseAssets, zdAssets, manifest.CharacterItems);
                actorMap.TryGetValue(code, out var characterActor);
                sequenceMap.TryGetValue(code, out var characterSequence);
                buffMap.TryGetValue(code, out var characterBuffs);
                var sequencePreview = BuildSequenceFramesPreview(characterSequence);
                return new UnrealProjectSyncCharacterCandidate(
                    code,
                    ResolveDisplayName(code, characterItem),
                    $"{TargetBaseMaterialContentPath}/{code}",
                    $"{TargetZdContentPath}/{code}",
                    baseAssets.Count,
                    zdMaterialTextureCount,
                    BuildCharacterInfoPreview(characterItem, characterActor),
                    BuildSkillsPreview(characterActor, characterItem, manifest.LinkSkillLibrary, manifest.SupportSkillLibrary, assetLookup),
                    sequencePreview,
                    BuildBuffsPreview(characterBuffs),
                    BuildMaterialBuckets(materialAssets),
                    hasLatestData: manifest.SchemaVersion >= CurrentExportSchemaVersion &&
                        HasDetailedCharacterData(characterItem, characterActor, characterSequence, characterBuffs),
                    voiceBuckets: BuildVoiceBuckets(zdAssets, sequencePreview));
            })
            .ToArray();
    }

    private static bool HasDetailedCharacterData(
        UnrealProjectExportCharacterItem? characterItem,
        UnrealProjectExportCharacterActor? characterActor,
        UnrealProjectExportCharacterSequence? characterSequence,
        UnrealProjectExportCharacterBuffSet? characterBuffs)
    {
        return characterItem is not null ||
            characterActor is not null ||
            characterSequence is not null ||
            characterBuffs is not null;
    }

    private static UnrealProjectExportCharacterItem? ResolveCharacterItem(
        string code,
        IReadOnlyList<UnrealProjectExportAsset> baseAssets,
        IReadOnlyList<UnrealProjectExportAsset> zdAssets,
        IReadOnlyList<UnrealProjectExportCharacterItem> items)
    {
        if (items.Count == 0)
        {
            return null;
        }

        var assetNames = baseAssets
            .Concat(zdAssets)
            .Select(asset => asset.AssetName)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return items
            .Select(item => new
            {
                Item = item,
                Score = ScoreCharacterItemMatch(code, assetNames, item)
            })
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Item.Code, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Item)
            .FirstOrDefault();
    }

    private static int ScoreCharacterItemMatch(
        string code,
        IReadOnlyList<string> assetNames,
        UnrealProjectExportCharacterItem item)
    {
        var score = 0;
        var normalizedCode = NormalizeCharacterIdentity(code);
        var itemCode = NormalizeCharacterIdentity(item.Code);
        var itemAssetName = NormalizeCharacterIdentity(item.AssetName);
        var itemNameTokens = BuildIdentityTokens(item.Code)
            .Concat(BuildIdentityTokens(item.AssetName))
            .Concat(BuildIdentityTokens(item.ItemData.Name))
            .Concat(item.ItemData.Keywords.SelectMany(BuildIdentityTokens))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!string.IsNullOrWhiteSpace(normalizedCode))
        {
            if (string.Equals(normalizedCode, itemCode, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedCode, itemAssetName, StringComparison.OrdinalIgnoreCase))
            {
                score += 1000;
            }

            var folderTokens = BuildIdentityTokens(code).ToArray();
            if (folderTokens.Length > 0 && folderTokens.All(token => itemNameTokens.Contains(token, StringComparer.OrdinalIgnoreCase)))
            {
                score += 260;
            }

            var sharedFolderTokens = folderTokens.Count(token => itemNameTokens.Contains(token, StringComparer.OrdinalIgnoreCase));
            score += sharedFolderTokens * 80;

            var distance = LevenshteinDistance(normalizedCode, itemCode);
            if (distance > 0 && distance <= 3)
            {
                score += 180 - distance * 35;
            }
        }

        foreach (var assetName in assetNames.Take(80))
        {
            var normalizedAssetName = NormalizeCharacterIdentity(assetName);
            if (string.IsNullOrWhiteSpace(normalizedAssetName))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(itemCode) &&
                normalizedAssetName.Contains(itemCode, StringComparison.OrdinalIgnoreCase))
            {
                score += 220;
            }

            var assetTokens = BuildIdentityTokens(assetName).ToArray();
            var sharedAssetTokens = assetTokens.Count(token => itemNameTokens.Contains(token, StringComparer.OrdinalIgnoreCase));
            score += sharedAssetTokens * 45;
        }

        return score;
    }

    private static IEnumerable<string> BuildIdentityTokens(string? value)
    {
        var text = value ?? string.Empty;
        foreach (Match match in Regex.Matches(text, @"[A-Za-z0-9]+|[\u4e00-\u9fff]+"))
        {
            var token = match.Value.Trim().ToLowerInvariant();
            if (token.Length >= 2 && token != "item" && token != "image")
            {
                yield return token;
            }
        }
    }

    private static string NormalizeCharacterIdentity(string? value)
    {
        var tokens = BuildIdentityTokens(value).ToArray();
        return tokens.Length == 0 ? string.Empty : string.Concat(tokens);
    }

    private static int LevenshteinDistance(string left, string right)
    {
        if (string.IsNullOrEmpty(left))
        {
            return right.Length;
        }

        if (string.IsNullOrEmpty(right))
        {
            return left.Length;
        }

        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = char.ToLowerInvariant(left[i - 1]) == char.ToLowerInvariant(right[j - 1]) ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private static string ResolveDisplayName(string code, UnrealProjectExportCharacterItem? item)
    {
        var name = item?.ItemData?.Name;
        return string.IsNullOrWhiteSpace(name) ? code : name.Trim();
    }

    private static UnrealProjectSyncCharacterInfoPreview BuildCharacterInfoPreview(
        UnrealProjectExportCharacterItem? item,
        UnrealProjectExportCharacterActor? actor)
    {
        if (item is null)
        {
            return new UnrealProjectSyncCharacterInfoPreview(
                string.Empty,
                string.Empty,
                false,
                string.Empty,
                string.Empty,
                string.Empty,
                [],
                [],
                1,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0);
        }

        var data = item.ItemData;
        var charData = data.CharData;
        var formLimit = ResolveFormLimitFromSkillSlots(actor);
        if (formLimit <= 0)
        {
            formLimit = charData.CharShapeHas.Count > 0
                ? Math.Max(1, charData.CharShapeHas.Count(value => value))
                : charData.CharShapeNow + 1;
        }

        formLimit = Math.Max(1, formLimit);
        var skillCount = Math.Max(charData.SkillDataCount, charData.SkillHave.Count);
        return new UnrealProjectSyncCharacterInfoPreview(
            item.AssetName,
            item.ObjectPath,
            item.HasItemData,
            item.ReadMessage,
            data.Name,
            data.Description,
            data.Keywords,
            charData.SkillDescription,
            formLimit,
            skillCount,
            charData.Speed,
            charData.Health,
            charData.Attack,
            charData.PhyDefense,
            charData.MagDefense,
            charData.Critical,
            charData.CriticalC,
            charData.Synchronize,
            charData.Anti);
    }

    private static int ResolveFormLimitFromSkillSlots(UnrealProjectExportCharacterActor? actor)
    {
        if (actor is null)
        {
            return 0;
        }

        return new[] { "SkillSlot1", "SkillSlot2", "SkillSlot3" }
            .Select(slotKey => actor.SkillSlots.TryGetValue(slotKey, out var slot) ? GetSkillSlotStageCount(slot) : 0)
            .DefaultIfEmpty(0)
            .Max();
    }

    private static int GetSkillSlotStageCount(UnrealProjectExportSkillSlot slot)
    {
        return new[]
        {
            slot.Icons.Count,
            slot.Names.Count,
            slot.Descriptions.Count,
            slot.PointCosts.Count,
            slot.SkillRates.Count,
            slot.AutoPriorities.Count,
            slot.SkillStates.Count,
            slot.PreformTypes.Count,
            slot.PreSkillValues.Count,
            slot.SkillNames.Count,
            slot.AttackCapacities.Count
        }.Max();
    }

    private static UnrealProjectSyncSkillsPreview BuildSkillsPreview(
        UnrealProjectExportCharacterActor? actor,
        UnrealProjectExportCharacterItem? item,
        UnrealProjectExportLinkSkillLibrary? linkLibrary,
        UnrealProjectExportSupportSkillLibrary? supportLibrary,
        IReadOnlyDictionary<string, UnrealProjectExportAsset> assetLookup)
    {
        var coreSlots = new[]
        {
            BuildSkillSlotPreview(actor, "SkillSlot1", "一技能", assetLookup),
            BuildSkillSlotPreview(actor, "SkillSlot2", "二技能", assetLookup),
            BuildSkillSlotPreview(actor, "SkillSlot3", "终结技", assetLookup)
        };

        var characterName = item?.ItemData?.Name?.Trim() ?? string.Empty;
        var supportEntry = ResolveSupportSkillEntry(supportLibrary, item);
        var supportSkillSlot = supportEntry is null
            ? new UnrealProjectSyncSkillSlotPreview("SkillSlot4", "护援技", true, false, ResolveSupportSkillStatus(supportLibrary, item), [])
            : BuildSkillSlotPreview(supportEntry.SkillSlot4Data, "SkillSlot4", "护援技", assetLookup, isDynamic: true);

        var linkEntries = linkLibrary?.Entries.AsEnumerable() ?? [];
        if (!string.IsNullOrWhiteSpace(characterName))
        {
            linkEntries = linkEntries.Where(pair =>
                string.IsNullOrWhiteSpace(pair.Value.MainCharacterName) ||
                string.Equals(pair.Value.MainCharacterName, characterName, StringComparison.CurrentCultureIgnoreCase));
        }

        var linkSkills = linkEntries
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new UnrealProjectSyncLinkSkillPreview(
                pair.Value.MainCharacterName,
                pair.Value.SupportCharacterCode,
                pair.Value.Skill12Index,
                BuildSkillSlotPreview(pair.Value.SkillSlot5Data, "SkillSlot5Data", "连携技", assetLookup, isDynamic: true)))
            .ToArray() ?? [];

        var sourceText = actor is null
            ? "未找到角色蓝图"
            : $"{actor.AssetName}  {actor.ObjectPath}";
        var actorStatus = actor is null
            ? "未找到角色蓝图技能数据"
            : actor.HasActorData
                ? $"已读取角色蓝图技能槽，目标形态 {Math.Max(1, actor.TargetShape)}"
                : $"已找到角色蓝图，但未读出 SkillSlot1-4：{actor.ReadMessage}";
        var linkStatus = linkLibrary is null || string.IsNullOrWhiteSpace(linkLibrary.ObjectPath)
            ? "未找到连携/护援函数库导出数据"
            : linkLibrary.HasData || supportLibrary?.HasData == true
                ? $"已读取函数库：{linkLibrary.ObjectPath}，护援技 {supportSkillSlot.Stages.Count} 个阶段，连携技 {linkSkills.Length} 个"
                : $"函数库未读出护援/连携数据：{CombineStatusMessages(supportLibrary?.ReadMessage, linkLibrary.ReadMessage)}";

        return new UnrealProjectSyncSkillsPreview(
            actor?.HasActorData == true,
            sourceText,
            actorStatus,
            linkStatus,
            coreSlots,
            supportSkillSlot,
            linkSkills);
    }

    private static UnrealProjectExportSupportSkillEntry? ResolveSupportSkillEntry(
        UnrealProjectExportSupportSkillLibrary? supportLibrary,
        UnrealProjectExportCharacterItem? item)
    {
        if (supportLibrary is null ||
            !supportLibrary.HasData)
        {
            return null;
        }

        var matchKeys = BuildSupportMatchKeys(item);
        if (matchKeys.Count == 0)
        {
            return null;
        }

        return supportLibrary.Entries.Values
            .Where(entry => SupportEntryMatches(entry, matchKeys))
            .OrderBy(entry => entry.SourceIndex)
            .FirstOrDefault();
    }

    private static bool SupportEntryMatches(UnrealProjectExportSupportSkillEntry entry, ISet<string> matchKeys)
    {
        foreach (var value in EnumerateSupportEntryKeys(entry))
        {
            if (matchKeys.Contains(NormalizeSupportMatchKey(value)))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> EnumerateSupportEntryKeys(UnrealProjectExportSupportSkillEntry entry)
    {
        yield return entry.SupportCharacterName;
        yield return entry.SupportCharacterCode;
        yield return StripBracketSuffix(entry.SupportCharacterName);
    }

    private static HashSet<string> BuildSupportMatchKeys(UnrealProjectExportCharacterItem? item)
    {
        var values = new[]
        {
            item?.ItemData?.Name,
            StripBracketSuffix(item?.ItemData?.Name),
            item?.Code,
            item?.AssetName,
            item?.AssetName?.StartsWith("Item_", StringComparison.OrdinalIgnoreCase) == true
                ? item.AssetName[5..]
                : string.Empty
        };

        return values
            .Select(NormalizeSupportMatchKey)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeSupportMatchKey(string? value)
    {
        return value?.Trim().Replace("？", "?", StringComparison.OrdinalIgnoreCase) ?? string.Empty;
    }

    private static string StripBracketSuffix(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var bracketIndex = text.LastIndexOf('[');
        return bracketIndex > 0 && text.EndsWith(']')
            ? text[..bracketIndex].Trim()
            : text;
    }

    private static string ResolveSupportSkillStatus(UnrealProjectExportSupportSkillLibrary? supportLibrary, UnrealProjectExportCharacterItem? item)
    {
        if (supportLibrary is null || string.IsNullOrWhiteSpace(supportLibrary.ObjectPath))
        {
            return "未找到护援函数库导出数据。";
        }

        if (!supportLibrary.HasData)
        {
            return $"函数库未读出护援技：{supportLibrary.ReadMessage}";
        }

        var characterName = item?.ItemData?.Name?.Trim() ?? string.Empty;
        var characterCode = item?.Code?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(characterName)
            ? "当前角色未读取到中文名，无法匹配护援技。"
            : $"函数库未找到 {characterName}（{characterCode}）的护援技。";
    }

    private static string CombineStatusMessages(params string?[] messages)
    {
        var values = messages
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Select(message => message!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return values.Length == 0 ? "无详细信息" : string.Join("；", values);
    }

    private static UnrealProjectSyncSkillSlotPreview BuildSkillSlotPreview(
        UnrealProjectExportCharacterActor? actor,
        string slotKey,
        string displayName,
        IReadOnlyDictionary<string, UnrealProjectExportAsset> assetLookup,
        bool isDynamic = false)
    {
        if (actor is null || !actor.SkillSlots.TryGetValue(slotKey, out var slot))
        {
            return new UnrealProjectSyncSkillSlotPreview(slotKey, displayName, isDynamic, false, "未读取到该技能槽。", []);
        }

        return BuildSkillSlotPreview(slot, slotKey, displayName, assetLookup, isDynamic);
    }

    private static UnrealProjectSyncSkillSlotPreview BuildSkillSlotPreview(
        UnrealProjectExportSkillSlot slot,
        string slotKey,
        string displayName,
        IReadOnlyDictionary<string, UnrealProjectExportAsset> assetLookup,
        bool isDynamic = false)
    {
        var stageCount = GetSkillSlotStageCount(slot);

        var stages = Enumerable.Range(0, stageCount)
            .Select(index => BuildSkillStagePreview(slot, index, assetLookup))
            .ToArray();
        var hasData = stages.Length > 0;
        return new UnrealProjectSyncSkillSlotPreview(
            slotKey,
            string.IsNullOrWhiteSpace(slot.DisplayName) ? displayName : slot.DisplayName,
            isDynamic,
            hasData,
            hasData ? "已读取" : "该技能槽没有形态数据。",
            stages);
    }

    private static UnrealProjectSyncSkillStagePreview BuildSkillStagePreview(
        UnrealProjectExportSkillSlot slot,
        int index,
        IReadOnlyDictionary<string, UnrealProjectExportAsset> assetLookup)
    {
        var iconObjectPath = GetAt(slot.Icons, index);
        var iconExportedFilePath = ResolveExportedAssetPath(assetLookup, iconObjectPath ?? string.Empty);
        return new UnrealProjectSyncSkillStagePreview(
            index + 1,
            GetTextAt(slot.Names, index),
            GetTextAt(slot.SkillNames, index),
            GetTextAt(slot.Descriptions, index),
            GetAt(slot.PointCosts, index).ToString(),
            GetAt(slot.AttackCapacities, index).ToString(),
            GetAt(slot.AutoPriorities, index).ToString(),
            MapSkillState(GetAt(slot.SkillStates, index)),
            MapGuardState(GetAt(slot.PreformTypes, index)),
            FormatDouble(GetAt(slot.PreSkillValues, index)),
            iconObjectPath ?? string.Empty,
            ExtractAssetName(iconObjectPath ?? string.Empty),
            iconExportedFilePath,
            BuildMultiplierPreview(GetAt(slot.SkillRates, index)));
    }

    private static IReadOnlyList<UnrealProjectSyncSkillMultiplierPreview> BuildMultiplierPreview(UnrealProjectExportSkillRate? rate)
    {
        if (rate is null)
        {
            return [];
        }

        return Enumerable.Range(1, 5)
            .Select(level => new UnrealProjectSyncSkillMultiplierPreview(
                level,
                FormatDouble(GetRate(rate.Physical, level)),
                FormatDouble(GetRate(rate.Energy, level))))
            .ToArray();
    }

    private static UnrealProjectSyncSequenceFramesPreview BuildSequenceFramesPreview(UnrealProjectExportCharacterSequence? sequence)
    {
        if (sequence is null)
        {
            return new UnrealProjectSyncSequenceFramesPreview(
                false,
                false,
                string.Empty,
                "未找到 AnimMaps/AnimSequences 导出数据；请先重新获取项目角色。",
                [],
                [],
                [],
                []);
        }

        var actions = BuildSequenceActionPreviews(sequence.Actions);
        var baseActions = actions
            .Where(action => IsSequenceCategory(action, "base"))
            .ToArray();
        var skillActions = actions
            .Where(action => IsSequenceCategory(action, "skill"))
            .ToArray();
        var linkActions = actions
            .Where(action => IsSequenceCategory(action, "link"))
            .ToArray();
        var otherActions = actions
            .Where(action => IsSequenceCategory(action, "other"))
            .ToArray();
        var status = sequence.HasData
            ? sequence.HasAnimMaps
                ? $"已找到 AnimMaps：{sequence.AnimMapsObjectPath}"
                : $"序列素材已读取，但 AnimMaps 未找到：{sequence.ReadMessage}"
            : string.IsNullOrWhiteSpace(sequence.ReadMessage)
                ? "未读取到 AnimMaps、AnimSequences 或 Material 帧素材。"
                : sequence.ReadMessage;
        return new UnrealProjectSyncSequenceFramesPreview(
            sequence.HasData,
            sequence.HasAnimMaps,
            sequence.AnimMapsObjectPath,
            status,
            baseActions,
            skillActions,
            linkActions,
            otherActions);
    }

    private static UnrealProjectSyncBuffsPreview BuildBuffsPreview(UnrealProjectExportCharacterBuffSet? buffSet)
    {
        if (buffSet is null)
        {
            return new UnrealProjectSyncBuffsPreview(
                false,
                "未找到 BUFF 导出数据；请先重新获取项目角色。",
                []);
        }

        var buffs = buffSet.Buffs
            .OrderBy(buff => ExtractTrailingNumber(buff.AssetName))
            .ThenBy(buff => buff.AssetName, StringComparer.OrdinalIgnoreCase)
            .Select(buff => new UnrealProjectSyncBuffPreview(
                buff.AssetName,
                buff.ObjectPath,
                buff.HasReadableData,
                string.IsNullOrWhiteSpace(buff.ReadMessage)
                    ? buff.HasReadableData ? "已读取 DreamTask BUFF" : "未读取到 DreamTask 数据"
                    : buff.ReadMessage,
                buff.TaskName,
                buff.DisplayName,
                buff.Description,
                MapBuffDamageType(buff.DamageType),
                MapBuffGainType(buff.GainType),
                MapBuffTaskPriority(buff.TaskPriority),
                buff.Count,
                buff.CompleteCount,
                buff.Power,
                buff.CompletePower,
                buff.TriggerTiming,
                buff.ConditionSummary,
                buff.IconObjectPath,
                string.IsNullOrWhiteSpace(buff.IconAssetName) ? ExtractAssetName(buff.IconObjectPath) : buff.IconAssetName,
                buff.IconExportedFilePath))
            .ToArray();
        var status = buffSet.HasData
            ? $"已读取 BUFF 文件夹：{buffSet.BuffFolderObjectPath}"
            : string.IsNullOrWhiteSpace(buffSet.ReadMessage)
                ? "BUFF 文件夹存在，但未读取到 DreamTask BUFF。"
                : buffSet.ReadMessage;
        return new UnrealProjectSyncBuffsPreview(buffSet.HasData, status, buffs);
    }

    private static IReadOnlyList<UnrealProjectSyncSequenceActionPreview> BuildSequenceActionPreviews(
        IReadOnlyList<UnrealProjectExportSequenceAction> exportActions)
    {
        var actions = exportActions
            .SelectMany(SplitSequenceActionByForm)
            .ToList();
        var formLimit = Math.Max(
            1,
            actions
                .Where(action => !action.Category.Equals("link", StringComparison.OrdinalIgnoreCase) &&
                    !action.Category.Equals("other", StringComparison.OrdinalIgnoreCase))
                .Select(action => action.FormIndex)
                .DefaultIfEmpty(1)
                .Max());
        foreach (var definition in StandardSequenceActions)
        {
            for (var formIndex = 1; formIndex <= formLimit; formIndex++)
            {
                if (actions.Any(action =>
                        string.Equals(action.ActionCode, definition.ActionCode, StringComparison.OrdinalIgnoreCase) &&
                        action.FormIndex == formIndex))
                {
                    continue;
                }

                actions.Add(CreateMissingSequenceActionPreview(definition, formIndex));
            }
        }

        return actions
            .OrderBy(GetSequenceActionSortOrder)
            .ThenBy(action => action.FormIndex)
            .ThenBy(action => action.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<UnrealProjectSyncSequenceActionPreview> SplitSequenceActionByForm(UnrealProjectExportSequenceAction action)
    {
        var formIndexes = action.FormIndexes.Count == 0
            ? new[] { 1 }
            : action.FormIndexes.Distinct().Order().ToArray();
        if (formIndexes.Length <= 1)
        {
            yield return BuildSequenceActionPreview(action);
            yield break;
        }

        foreach (var formIndex in formIndexes)
        {
            yield return BuildSequenceActionPreview(CloneSequenceActionForForm(action, formIndex));
        }
    }

    private static UnrealProjectExportSequenceAction CloneSequenceActionForForm(UnrealProjectExportSequenceAction action, int formIndex)
    {
        var animSequences = FilterSequenceAssetsByForm(action.AnimSequences, action.ActionCode, formIndex).ToList();
        var sequencePaths = animSequences
            .Select(asset => NormalizeObjectPath(asset.ObjectPath))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new UnrealProjectExportSequenceAction
        {
            ActionCode = action.ActionCode,
            DisplayName = action.DisplayName,
            SourceProperty = action.SourceProperty,
            Category = action.Category,
            Target = action.Target,
            HasData = action.HasData,
            FormIndexes = [formIndex],
            ReferencedSequences = action.ReferencedSequences,
            AnimSequences = animSequences,
            TextureCount = action.TextureCount,
            SpriteCount = action.SpriteCount,
            FlipbookCount = action.FlipbookCount,
            FramesPerSecond = action.FramesPerSecond,
            OrderedFrames = FilterSequenceAssetsByForm(action.OrderedFrames, action.ActionCode, formIndex, fallbackToAllWhenSingleForm: true).ToList(),
            PreviewFrames = FilterSequenceAssetsByForm(action.PreviewFrames, action.ActionCode, formIndex, fallbackToAllWhenSingleForm: true).ToList(),
            SoundNotifies = sequencePaths.Count == 0
                ? action.SoundNotifies
                : action.SoundNotifies
                    .Where(notify => sequencePaths.Contains(NormalizeObjectPath(notify.SequenceObjectPath)))
                    .ToList()
        };
    }

    private static IEnumerable<UnrealProjectExportSequenceAsset> FilterSequenceAssetsByForm(
        IReadOnlyList<UnrealProjectExportSequenceAsset> assets,
        string actionCode,
        int formIndex,
        bool fallbackToAllWhenSingleForm = false)
    {
        var matched = assets
            .Where(asset => GetSequenceAssetFormIndex(asset.AssetName, actionCode) == formIndex)
            .ToList();
        if (matched.Count == 0 && fallbackToAllWhenSingleForm && formIndex == 1)
        {
            return assets;
        }

        return matched;
    }

    private static int GetSequenceAssetFormIndex(string assetName, string actionCode)
    {
        var name = assetName ?? string.Empty;
        if (Regex.IsMatch(name, @"(?:^|[_-])Shape0*([2-9]\d*)", RegexOptions.IgnoreCase) is true)
        {
            var match = Regex.Match(name, @"(?:^|[_-])Shape0*([2-9]\d*)", RegexOptions.IgnoreCase);
            return int.TryParse(match.Groups[1].Value, out var shapeIndex) ? shapeIndex : 1;
        }

        var normalizedCode = Regex.Replace(actionCode ?? string.Empty, "[^a-z0-9]", string.Empty).ToLowerInvariant();
        var normalizedName = Regex.Replace(name, "[^a-z0-9]", string.Empty).ToLowerInvariant();
        if (!string.IsNullOrEmpty(normalizedCode) &&
            normalizedName.Contains(normalizedCode + "2", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 1;
    }

    private static UnrealProjectSyncSequenceActionPreview CreateMissingSequenceActionPreview(UnrealSequenceActionDefinition definition, int formIndex)
    {
        return new UnrealProjectSyncSequenceActionPreview(
            definition.ActionCode,
            definition.DisplayName,
            string.Empty,
            definition.Category,
            string.Empty,
            false,
            [formIndex],
            0,
            0,
            0,
            0,
            0,
            0,
            [],
            []);
    }

    private static int GetSequenceActionSortOrder(UnrealProjectSyncSequenceActionPreview action)
    {
        var definition = StandardSequenceActions.FirstOrDefault(item =>
            string.Equals(item.ActionCode, action.ActionCode, StringComparison.OrdinalIgnoreCase));
        if (definition is not null)
        {
            return definition.SortOrder;
        }

        return action.Category.Equals("link", StringComparison.OrdinalIgnoreCase)
            ? 200
            : 300;
    }

    private static UnrealProjectSyncSequenceActionPreview BuildSequenceActionPreview(UnrealProjectExportSequenceAction action)
    {
        var orderedFrames = action.OrderedFrames
            .Select(BuildExportAssetView)
            .ToArray();
        var previewFrames = action.PreviewFrames
            .Select(BuildExportAssetView)
            .ToArray();
        return new UnrealProjectSyncSequenceActionPreview(
            action.ActionCode,
            string.IsNullOrWhiteSpace(action.DisplayName) ? action.ActionCode : action.DisplayName,
            action.SourceProperty,
            action.Category,
            action.Target,
            action.HasData,
            action.FormIndexes,
            action.ReferencedSequences.Count,
            action.AnimSequences.Count,
            action.TextureCount,
            action.SpriteCount,
            action.FlipbookCount,
            action.FramesPerSecond,
            orderedFrames,
            previewFrames,
            action.SoundNotifies.Select(notify => new UnrealProjectSyncSequenceSoundNotifyPreview(
                notify.FrameIndex,
                notify.TimeSeconds,
                notify.TrackIndex,
                notify.SoundObjectPath,
                notify.SoundAssetName,
                notify.SoundAssetClass,
                notify.ExportedFilePath,
                notify.IsCharacterVoice,
                notify.SequenceObjectPath)).ToArray());
    }

    private static UnrealProjectSyncExportAssetView BuildExportAssetView(UnrealProjectExportSequenceAsset asset)
    {
        return new UnrealProjectSyncExportAssetView(
            asset.AssetName,
            asset.AssetClass,
            asset.PackagePath,
            asset.ObjectPath,
            string.Empty,
            asset.ExportedFilePath);
    }

    private sealed record UnrealSequenceActionDefinition(
        string ActionCode,
        string DisplayName,
        string Category,
        int SortOrder);

    private static bool IsSequenceCategory(UnrealProjectSyncSequenceActionPreview action, string category)
    {
        return string.Equals(action.Category, category, StringComparison.OrdinalIgnoreCase);
    }

    private static T? GetAt<T>(IReadOnlyList<T> values, int index)
    {
        return index >= 0 && index < values.Count ? values[index] : default;
    }

    private static string GetTextAt(IReadOnlyList<string> values, int index)
    {
        return index >= 0 && index < values.Count ? values[index] : string.Empty;
    }

    private static double GetRate(Dictionary<string, double> values, int level)
    {
        return values.TryGetValue(level.ToString(), out var value) ? value : 0;
    }

    private static string FormatDouble(double value)
    {
        return Math.Abs(value) < 0.000001 ? string.Empty : value.ToString("0.###");
    }

    private static string FormatDouble(double? value)
    {
        return value.HasValue ? FormatDouble(value.Value) : string.Empty;
    }

    private static string MapSkillState(string? value)
    {
        return NormalizeUnrealEnumValue(value) switch
        {
            "normal" => "常态",
            "disable" => "禁用",
            "abandon" => "舍弃",
            "air" => "空",
            "" or null => "空",
            var other => value?.Trim() ?? other
        };
    }

    private static string MapGuardState(string? value)
    {
        return NormalizeUnrealEnumValue(value) switch
        {
            "defense" => "防御",
            "attack" => "反击",
            "dodge" => "闪避",
            "air" => "空",
            "" or null => "空",
            var other => value?.Trim() ?? other
        };
    }

    private static string MapBuffDamageType(string? value)
    {
        return NormalizeUnrealEnumValue(value) switch
        {
            "sendattr" or "senderattr" or "ownerattr" or "attribute" or "attr" or "property" or "battackerattr" => "发送方-属性",
            "sendfinal" or "senderfinal" or "ownerfinal" or "final" or "battackerfinal" => "发送方-最终",
            "receiveattr" or "receiverattr" or "targetattr" or "injuredattr" => "接收方-属性",
            "receivefinal" or "receiverfinal" or "targetfinal" or "injuredfinal" => "接收方-最终",
            "发送方-属性" => "发送方-属性",
            "发送方-最终" => "发送方-最终",
            "接收方-属性" => "接收方-属性",
            "接收方-最终" => "接收方-最终",
            _ => string.IsNullOrWhiteSpace(value) ? "发送方-属性" : value!.Trim()
        };
    }

    private static string MapBuffGainType(string? value)
    {
        var normalized = NormalizeUnrealEnumValue(value);
        if (normalized.Contains("debuff", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("weak", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("削弱", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("decrease", StringComparison.OrdinalIgnoreCase))
        {
            return "削弱";
        }

        return "增益";
    }

    private static string MapBuffTaskPriority(string? value)
    {
        return NormalizeUnrealEnumValue(value) switch
        {
            "low" or "低" => "低",
            "high" or "高" => "高",
            "urgent" or "紧急" => "紧急",
            _ => "正常"
        };
    }

    private static int ExtractTrailingNumber(string value)
    {
        var match = Regex.Match(value ?? string.Empty, @"(\d+)(?!.*\d)");
        return match.Success && int.TryParse(match.Groups[1].Value, out var number)
            ? number
            : int.MaxValue;
    }

    private static string NormalizeUnrealEnumValue(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var colonIndex = text.IndexOf(':');
        if (colonIndex > 0)
        {
            text = text[..colonIndex];
        }

        var dotIndex = text.LastIndexOf('.');
        if (dotIndex >= 0 && dotIndex < text.Length - 1)
        {
            text = text[(dotIndex + 1)..];
        }

        text = text.Trim().Trim('<', '>', ' ');
        return text.ToLowerInvariant();
    }

    private static string ExtractAssetName(string objectPath)
    {
        if (string.IsNullOrWhiteSpace(objectPath))
        {
            return string.Empty;
        }

        var dotIndex = objectPath.LastIndexOf('.');
        if (dotIndex >= 0 && dotIndex < objectPath.Length - 1)
        {
            return objectPath[(dotIndex + 1)..];
        }

        var slashIndex = objectPath.LastIndexOf('/');
        return slashIndex >= 0 && slashIndex < objectPath.Length - 1
            ? objectPath[(slashIndex + 1)..]
            : objectPath;
    }

    private static IReadOnlyDictionary<string, UnrealProjectExportAsset> BuildExportAssetLookup(
        IReadOnlyList<UnrealProjectExportAsset> assets)
    {
        var lookup = new Dictionary<string, UnrealProjectExportAsset>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in assets)
        {
            AddExportAssetLookupValue(lookup, asset.ObjectPath, asset);
            AddExportAssetLookupValue(lookup, asset.AssetName, asset);
            AddExportAssetLookupValue(lookup, ExtractAssetName(asset.ObjectPath), asset);
        }

        return lookup;
    }

    private static void AddExportAssetLookupValue(
        IDictionary<string, UnrealProjectExportAsset> lookup,
        string key,
        UnrealProjectExportAsset asset)
    {
        var normalized = NormalizeObjectPath(key);
        if (string.IsNullOrWhiteSpace(normalized) ||
            lookup.ContainsKey(normalized))
        {
            return;
        }

        lookup[normalized] = asset;
    }

    private static string ResolveExportedAssetPath(
        IReadOnlyDictionary<string, UnrealProjectExportAsset> assetLookup,
        string objectPath)
    {
        if (string.IsNullOrWhiteSpace(objectPath))
        {
            return string.Empty;
        }

        var lookupKeys = new[]
        {
            objectPath,
            ExtractAssetName(objectPath)
        };

        foreach (var key in lookupKeys)
        {
            if (assetLookup.TryGetValue(NormalizeObjectPath(key), out var asset) &&
                File.Exists(asset.ExportedFilePath))
            {
                return asset.ExportedFilePath;
            }
        }

        return string.Empty;
    }

    public int SyncMaterialBucketToToolbox(CharacterCard character, UnrealProjectSyncMaterialBucket bucket)
    {
        if (!Enum.TryParse<BaseMaterialKind>(bucket.Kind, ignoreCase: true, out var kind))
        {
            kind = BaseMaterialKind.OtherImage;
        }

        var service = new BaseMaterialService();
        var sourcePaths = bucket.Assets
            .Where(asset => asset.HasPreview)
            .Select(asset => asset.ExportedFilePath)
            .ToArray();
        return service.ReplaceWithImages(character, kind, sourcePaths);
    }

    public int SyncAllMaterialBucketsToToolbox(CharacterCard character, UnrealProjectSyncCharacterCandidate candidate)
    {
        var importedCount = 0;
        foreach (var bucket in candidate.MaterialBuckets)
        {
            importedCount += SyncMaterialBucketToToolbox(character, bucket);
        }

        return importedCount;
    }

    public void SyncCharacterInfoToToolbox(CharacterCard character, UnrealProjectSyncCharacterCandidate candidate)
    {
        if (!candidate.CharacterInfo.HasItemData)
        {
            throw new InvalidOperationException("当前角色物品蓝图还没有成功读取 ItemData.CharData，不能同步到 St3。");
        }

        var info = candidate.CharacterInfo;
        var characterInfoService = new CharacterInfoService();
        var existingData = characterInfoService.Load(character);
        var data = new CharacterInfoData
        {
            Code = character.Code,
            Name = string.IsNullOrWhiteSpace(info.Name) ? candidate.Code : info.Name.Trim(),
            Description = info.Description,
            KeywordTagGroups = existingData.KeywordTagGroups,
            FormLimit = Math.Max(1, info.FormLimit),
            Speed = Math.Max(0, info.Speed),
            Health = Math.Max(0, info.Health),
            Attack = Math.Max(0, info.Attack),
            PhysicalDefense = Math.Max(0, info.PhysicalDefense),
            EnergyDefense = Math.Max(0, info.EnergyDefense),
            CriticalRate = Math.Max(0, info.CriticalRate),
            CriticalDamage = Math.Max(0, info.CriticalDamage),
            Synchronize = Math.Max(0, info.Synchronize),
            Anti = info.Anti,
            UpdatedAt = DateTime.Now
        };

        foreach (var tag in info.KeywordTags
                     .Select(tag => tag.Trim())
                     .Where(tag => !string.IsNullOrWhiteSpace(tag))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            data.KeywordTags.Add(tag);
        }

        foreach (var passive in info.PassiveSkills
                     .Select(passive => passive.Trim())
                     .Where(passive => !string.IsNullOrWhiteSpace(passive)))
        {
            data.PassiveSkills.Add(passive);
        }

        characterInfoService.Save(character, data);
    }

    public int SyncAllSkillsToToolbox(CharacterCard character, UnrealProjectSyncCharacterCandidate candidate)
    {
        using var entryNotifications = CharacterSkillEntry.SuppressEditNotifications();
        using var multiplierNotifications = SkillMultiplierLevel.SuppressEditNotifications();

        var data = new CharacterSkillsService().Load(character);
        var count = 0;
        foreach (var slot in candidate.SkillsPreview.CoreSlots)
        {
            count += ApplySkillSlot(data, slot, candidate);
        }

        count += ApplySkillSlot(data, candidate.SkillsPreview.SupportSkillSlot, candidate);
        count += ApplyLinkSkills(data, candidate.SkillsPreview.LinkSkills, candidate);
        new CharacterSkillsService().Save(character, data);
        return count;
    }

    public int SyncSkillSlotToToolbox(
        CharacterCard character,
        UnrealProjectSyncCharacterCandidate candidate,
        UnrealProjectSyncSkillSlotPreview slot)
    {
        using var entryNotifications = CharacterSkillEntry.SuppressEditNotifications();
        using var multiplierNotifications = SkillMultiplierLevel.SuppressEditNotifications();

        var data = new CharacterSkillsService().Load(character);
        var count = ApplySkillSlot(data, slot, candidate);
        new CharacterSkillsService().Save(character, data);
        return count;
    }

    public int SyncSkillStageToToolbox(
        CharacterCard character,
        UnrealProjectSyncCharacterCandidate candidate,
        UnrealProjectSyncSkillSlotPreview slot,
        int stageIndex)
    {
        if (stageIndex < 0 || stageIndex >= slot.Stages.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(stageIndex));
        }

        using var entryNotifications = CharacterSkillEntry.SuppressEditNotifications();
        using var multiplierNotifications = SkillMultiplierLevel.SuppressEditNotifications();
        var data = new CharacterSkillsService().Load(character);
        var target = ResolveTargetSkillCollection(data, slot.SlotKey);
        var entry = ToCharacterSkillEntry(slot.Stages[stageIndex], candidate, linkSkill: null);
        if (target is null || entry is null)
        {
            return 0;
        }

        while (target.Count < stageIndex)
        {
            target.Add(CharacterSkillsService.CreateEntry());
        }

        entry.SyncId = UnrealBridgeSemanticSnapshotService.CreateOriginIdentity(
            $"{candidate.Code}|skill|{slot.SlotKey}||{stageIndex}");
        if (target.Count == stageIndex)
        {
            target.Add(entry);
        }
        else
        {
            target[stageIndex] = entry;
        }

        new CharacterSkillsService().Save(character, data);
        return 1;
    }

    public int SyncLinkSkillToToolbox(
        CharacterCard character,
        UnrealProjectSyncCharacterCandidate candidate,
        UnrealProjectSyncLinkSkillPreview linkSkill)
    {
        using var entryNotifications = CharacterSkillEntry.SuppressEditNotifications();
        using var multiplierNotifications = SkillMultiplierLevel.SuppressEditNotifications();

        var data = new CharacterSkillsService().Load(character);
        var entry = ToCharacterSkillEntry(linkSkill.SkillSlot.Stages.FirstOrDefault(), candidate, linkSkill);
        if (entry is null)
        {
            return 0;
        }

        var existingIndex = data.ComboSkills
            .Select((value, index) => new { value, index })
            .FirstOrDefault(item =>
                string.Equals(item.value.ComboCharacterCode, linkSkill.SupportCharacterCode, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.value.TrueName, entry.TrueName, StringComparison.CurrentCultureIgnoreCase))
            ?.index;
        if (existingIndex is int index)
        {
            data.ComboSkills[index] = entry;
        }
        else
        {
            data.ComboSkills.Add(entry);
        }

        new CharacterSkillsService().Save(character, data);
        return 1;
    }

    public int SyncAllSequenceFramesToToolbox(CharacterCard character, UnrealProjectSyncCharacterCandidate candidate)
    {
        var skills = new CharacterSkillsService().Load(character);
        var actions = SequenceFrameService.BuildActions(skills, new CharacterFormService().GetFormLimit(character));
        var service = new SequenceFrameService();
        var syncedCount = 0;
        foreach (var action in candidate.SequenceFramesPreview.Actions.Where(action => action.HasFramePreview))
        {
            syncedCount += SyncSequenceActionToToolbox(character, action, service, actions);
        }

        return syncedCount;
    }

    public int SyncSequenceActionToToolbox(
        CharacterCard character,
        UnrealProjectSyncSequenceActionPreview action)
    {
        var skills = new CharacterSkillsService().Load(character);
        var actions = SequenceFrameService.BuildActions(skills, new CharacterFormService().GetFormLimit(character));
        return SyncSequenceActionToToolbox(character, action, new SequenceFrameService(), actions);
    }

    public int SyncAllBuffsToToolbox(CharacterCard character, UnrealProjectSyncCharacterCandidate candidate)
    {
        var buffService = new BuffService();
        var data = buffService.Load(character);
        data.Buffs.Clear();
        foreach (var buff in candidate.BuffsPreview.Buffs.Where(buff => buff.HasReadableData))
        {
            data.Buffs.Add(ToBuffEntry(buff, character, buffService));
        }

        buffService.Save(character, data);
        return data.Buffs.Count;
    }

    public int SyncBuffToToolbox(
        CharacterCard character,
        UnrealProjectSyncBuffPreview buff)
    {
        if (!buff.HasReadableData)
        {
            throw new InvalidOperationException($"BUFF 未读取成功，不能同步：{buff.AssetName}。{buff.ReadStatusText}");
        }

        var buffService = new BuffService();
        var data = buffService.Load(character);
        var entry = ToBuffEntry(buff, character, buffService);
        var existingIndex = data.Buffs
            .Select((value, index) => new { value, index })
            .FirstOrDefault(item =>
                string.Equals(item.value.SourceAssetPath, buff.ObjectPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.value.UserCode, buff.AssetName, StringComparison.OrdinalIgnoreCase))
            ?.index;
        if (existingIndex is int index)
        {
            data.Buffs[index] = entry;
        }
        else
        {
            data.Buffs.Add(entry);
        }

        buffService.Save(character, data);
        return 1;
    }

    private static int ApplySkillSlot(
        CharacterSkillsData data,
        UnrealProjectSyncSkillSlotPreview slot,
        UnrealProjectSyncCharacterCandidate candidate)
    {
        var target = ResolveTargetSkillCollection(data, slot.SlotKey);
        if (target is null)
        {
            return 0;
        }

        var entries = slot.Stages
            .Select(stage => ToCharacterSkillEntry(stage, candidate, linkSkill: null))
            .Where(entry => entry is not null)
            .Cast<CharacterSkillEntry>()
            .ToList();
        if (entries.Count == 0)
        {
            return 0;
        }

        target.Clear();
        foreach (var entry in entries)
        {
            target.Add(entry);
        }

        return entries.Count;
    }

    private static int ApplyLinkSkills(
        CharacterSkillsData data,
        IReadOnlyList<UnrealProjectSyncLinkSkillPreview> linkSkills,
        UnrealProjectSyncCharacterCandidate candidate)
    {
        data.ComboSkills.Clear();
        foreach (var linkSkill in linkSkills)
        {
            var entry = ToCharacterSkillEntry(linkSkill.SkillSlot.Stages.FirstOrDefault(), candidate, linkSkill);
            if (entry is not null)
            {
                data.ComboSkills.Add(entry);
            }
        }

        return data.ComboSkills.Count;
    }

    private static ObservableCollection<CharacterSkillEntry>? ResolveTargetSkillCollection(
        CharacterSkillsData data,
        string slotKey)
    {
        return slotKey switch
        {
            "SkillSlot1" => data.FirstSkill,
            "SkillSlot2" => data.SecondSkill,
            "SkillSlot3" => data.UltimateSkill,
            "SkillSlot4" => data.SupportSkill,
            "SkillSlot5Data" => data.ComboSkills,
            _ => null
        };
    }

    private static CharacterSkillEntry? ToCharacterSkillEntry(
        UnrealProjectSyncSkillStagePreview? stage,
        UnrealProjectSyncCharacterCandidate candidate,
        UnrealProjectSyncLinkSkillPreview? linkSkill)
    {
        if (stage is null)
        {
            return null;
        }

        var entry = CharacterSkillsService.CreateEntry();
        entry.PositionName = string.IsNullOrWhiteSpace(stage.PositionName)
            ? stage.TrueName
            : stage.PositionName;
        entry.TrueName = stage.TrueName;
        entry.Description = stage.Description;
        entry.PtCost = stage.PtCost;
        entry.AttackCapacity = stage.AttackCapacity;
        entry.AutoPriority = stage.AutoPriority;
        entry.SkillState = string.IsNullOrWhiteSpace(stage.SkillState) ? "空" : stage.SkillState;
        entry.GuardState = string.IsNullOrWhiteSpace(stage.GuardState) ? "空" : stage.GuardState;
        entry.GuardValue = stage.GuardValue;
        entry.IconPath = File.Exists(stage.IconExportedFilePath)
            ? stage.IconExportedFilePath
            : ResolveLocalPreviewPath(candidate, stage.IconObjectPath);
        if (File.Exists(entry.IconPath))
        {
            entry.IconUri = new Uri(entry.IconPath).AbsoluteUri;
        }

        for (var index = 0; index < Math.Min(entry.LevelMultipliers.Count, stage.Multipliers.Count); index++)
        {
            entry.LevelMultipliers[index].PhysicalMultiplier = stage.Multipliers[index].PhysicalMultiplier;
            entry.LevelMultipliers[index].EnergyMultiplier = stage.Multipliers[index].EnergyMultiplier;
        }

        if (linkSkill is not null)
        {
            entry.ComboCharacterCode = linkSkill.SupportCharacterCode;
            entry.ComboCharacterName = linkSkill.SupportCharacterCode;
        }

        return entry;
    }

    private static string ResolveLocalPreviewPath(UnrealProjectSyncCharacterCandidate candidate, string iconObjectPath)
    {
        if (string.IsNullOrWhiteSpace(iconObjectPath))
        {
            return string.Empty;
        }

        var normalizedIconPath = NormalizeObjectPath(iconObjectPath);
        var allAssets = candidate.MaterialBuckets
            .SelectMany(bucket => bucket.Assets)
            .ToArray();
        var matched = allAssets.FirstOrDefault(asset =>
            string.Equals(NormalizeObjectPath(asset.ObjectPath), normalizedIconPath, StringComparison.OrdinalIgnoreCase));
        if (matched is not null && File.Exists(matched.ExportedFilePath))
        {
            return matched.ExportedFilePath;
        }

        var iconAssetName = ExtractAssetName(iconObjectPath);
        matched = allAssets.FirstOrDefault(asset =>
            string.Equals(asset.AssetName, iconAssetName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ExtractAssetName(asset.ObjectPath), iconAssetName, StringComparison.OrdinalIgnoreCase));
        return matched is not null && File.Exists(matched.ExportedFilePath)
            ? matched.ExportedFilePath
            : string.Empty;
    }

    private static int SyncSequenceActionToToolbox(
        CharacterCard character,
        UnrealProjectSyncSequenceActionPreview action,
        SequenceFrameService service,
        IReadOnlyList<SequenceFrameAction> toolboxActions)
    {
        var targetAction = ResolveToolboxSequenceAction(action, toolboxActions);
        if (targetAction is null)
        {
            throw new InvalidOperationException($"没有找到可写入的 St5 序列项：{action.Title}。请先同步 St4 技能/连携信息。");
        }

        var sourceFrames = (action.OrderedFrames.Count > 0 ? action.OrderedFrames : action.PreviewFrames)
            .Select(frame => new ExternalSequenceFrameSource(frame.ExportedFilePath, frame.IsBlank))
            .ToArray();
        var imported = service.ImportExternalFrames(
            character,
            targetAction,
            sourceFrames,
            (int)Math.Round(action.FramesPerSecond <= 0 ? SequenceFrameService.DefaultFps : action.FramesPerSecond));
        return imported.Count;
    }

    private static BuffEntry ToBuffEntry(
        UnrealProjectSyncBuffPreview buff,
        CharacterCard character,
        BuffService buffService)
    {
        var entry = BuffService.CreateBuff();
        entry.UserCode = SanitizeBuffUserCode(buff.AssetName);
        entry.Name = buff.Title;
        entry.Description = buff.Description;
        entry.DamageType = buff.DamageType;
        entry.GainType = buff.GainType;
        entry.TaskPriority = buff.TaskPriority;
        entry.Stacks = Math.Max(0, buff.Count);
        entry.CompleteStacks = Math.Max(1, buff.CompleteCount);
        entry.Strength = Math.Max(0, buff.Power);
        entry.CompleteStrength = Math.Max(1, buff.CompletePower);
        entry.TriggerTiming = buff.TriggerTiming;
        entry.ConditionSummary = buff.ConditionSummary;
        entry.SourceAssetPath = buff.ObjectPath;
        entry.ReadStatus = buff.HasReadableData ? "Unreal 同步" : buff.ReadStatusText;
        entry.SetOwnerTextFromToolbox(character.EffectiveDisplayName);
        BuffService.RefreshNaming(character, [entry]);

        if (File.Exists(buff.IconExportedFilePath))
        {
            buffService.ImportIcon(character, entry, buff.IconExportedFilePath);
        }
        else
        {
            buffService.EnsureDefaultIcon(character, entry);
        }

        return entry;
    }

    private static string SanitizeBuffUserCode(string value)
    {
        var text = Regex.Replace(value ?? string.Empty, @"[^A-Za-z0-9_-]+", "_").Trim('_');
        if (text.Length > 40)
        {
            text = text[^40..];
        }

        return text;
    }

    private static SequenceFrameAction? ResolveToolboxSequenceAction(
        UnrealProjectSyncSequenceActionPreview action,
        IReadOnlyList<SequenceFrameAction> toolboxActions)
    {
        if (action.Category.Equals("link", StringComparison.OrdinalIgnoreCase))
        {
            var target = NormalizeSequenceToken(action.Target);
            var title = NormalizeSequenceToken(action.Title);
            var candidates = toolboxActions.Where(item => item.IsCombo).ToArray();
            if (candidates.Length == 0)
            {
                return null;
            }

            var matched = candidates.FirstOrDefault(item =>
                NormalizeSequenceToken(item.DisplayName).Contains(target, StringComparison.OrdinalIgnoreCase) ||
                NormalizeSequenceToken(item.Code).Contains(target, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(target) && title.Contains(target, StringComparison.OrdinalIgnoreCase)));
            return matched ?? candidates.FirstOrDefault();
        }

        var code = NormalizeToolboxSequenceActionCode(action.ActionCode, action.FormIndex);
        return toolboxActions.FirstOrDefault(item =>
            string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeToolboxSequenceActionCode(string unrealActionCode, int formIndex)
    {
        var code = unrealActionCode.Trim();
        code = code switch
        {
            "Defence" => "Defense",
            "OnDamage" => "Ondm",
            "KO" => "Ko",
            "FlyDown" => "Flydown",
            "FlyStart" => "Flystart",
            "StandUP" => "Standup",
            _ => code
        };

        if (formIndex > 1 && !Regex.IsMatch(code, @"\d{2}$", RegexOptions.CultureInvariant))
        {
            code += formIndex.ToString("00");
        }

        return code;
    }

    private static string NormalizeSequenceToken(string value)
    {
        return Regex.Replace(value ?? string.Empty, "[^a-z0-9]", string.Empty, RegexOptions.IgnoreCase).ToLowerInvariant();
    }

    private static string NormalizeObjectPath(string value)
    {
        return value.Trim().Replace("\\", "/", StringComparison.OrdinalIgnoreCase);
    }

    private static int CountZdMaterialTextures(IReadOnlyList<UnrealProjectExportAsset> zdAssets, string code)
    {
        var materialRoot = $"{TargetZdContentPath}/{code}/Material";
        return zdAssets.Count(asset =>
            asset.PackagePath.StartsWith(materialRoot, StringComparison.OrdinalIgnoreCase) &&
            IsTextureAsset(asset));
    }

    private static bool IsTextureAsset(UnrealProjectExportAsset asset)
    {
        return asset.AssetClass.Contains("Texture", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, List<UnrealProjectExportAsset>> GroupByCharacterFolder(
        IReadOnlyList<UnrealProjectExportAsset> assets,
        string targetRoot)
    {
        var result = new Dictionary<string, List<UnrealProjectExportAsset>>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in assets)
        {
            if (!TryGetCharacterFolder(asset.PackagePath, targetRoot, out var code))
            {
                continue;
            }

            if (!result.TryGetValue(code, out var list))
            {
                list = [];
                result[code] = list;
            }

            list.Add(asset);
        }

        return result;
    }

    private static bool TryGetCharacterFolder(string packagePath, string targetRoot, out string code)
    {
        code = string.Empty;
        var normalizedPackagePath = packagePath.TrimEnd('/');
        var normalizedTargetRoot = targetRoot.TrimEnd('/');
        if (!normalizedPackagePath.StartsWith($"{normalizedTargetRoot}/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relative = normalizedPackagePath[(normalizedTargetRoot.Length + 1)..];
        var firstSlash = relative.IndexOf('/');
        code = firstSlash >= 0 ? relative[..firstSlash] : relative;
        return !string.IsNullOrWhiteSpace(code);
    }

    private static IReadOnlyList<UnrealProjectSyncMaterialBucket> BuildMaterialBuckets(
        IReadOnlyList<UnrealProjectExportAsset> assets)
    {
        return assets
            .Where(asset => !IsObjectRedirector(asset))
            .Select(asset => (Kind: ClassifyMaterial(asset.AssetName), Asset: asset))
            .GroupBy(item => item.Kind.Key, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key == "OtherImage" ? 1 : 0)
            .ThenBy(group => group.First().Kind.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var kind = group.First().Kind;
                var views = group
                    .Select(item => item.Asset)
                    .OrderBy(asset => asset.AssetName, StringComparer.OrdinalIgnoreCase)
                    .Select(asset => new UnrealProjectSyncExportAssetView(
                        asset.AssetName,
                        asset.AssetClass,
                        asset.PackagePath,
                        asset.ObjectPath,
                        asset.SourceRoot,
                        asset.ExportedFilePath))
                    .ToArray();
                return new UnrealProjectSyncMaterialBucket(kind.DisplayName, kind.Key, views.Length, views);
            })
            .ToArray();
    }

    private static IReadOnlyList<UnrealProjectSyncVoiceBucket> BuildVoiceBuckets(
        IReadOnlyList<UnrealProjectExportAsset> assets,
        UnrealProjectSyncSequenceFramesPreview sequencePreview)
    {
        var sequenceKinds = new Dictionary<string, VoiceMaterialKind>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in sequencePreview.Actions)
        {
            var kind = UnrealBridgeVoiceClassification.ClassifySequenceAction(action);
            foreach (var notify in action.SequenceSounds.Where(notify => notify.IsCharacterVoice))
            {
                var objectPath = NormalizeObjectPath(notify.SoundObjectPath);
                if (!string.IsNullOrWhiteSpace(objectPath) &&
                    (!sequenceKinds.TryGetValue(objectPath, out var existing) || existing == VoiceMaterialKind.Other))
                {
                    sequenceKinds[objectPath] = kind;
                }
            }
        }

        return assets
            .Where(asset => asset.AssetClass.Contains("SoundWave", StringComparison.OrdinalIgnoreCase))
            .Select(asset => (
                Kind: sequenceKinds.TryGetValue(NormalizeObjectPath(asset.ObjectPath), out var sequenceKind)
                    ? sequenceKind
                    : UnrealBridgeVoiceClassification.Classify(asset.PackagePath, asset.AssetName),
                Asset: asset))
            .GroupBy(item => item.Kind)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var spec = VoiceMaterialService.GetSpec(group.Key);
                var views = group
                    .Select(item => item.Asset)
                    .OrderBy(asset => asset.AssetName, StringComparer.OrdinalIgnoreCase)
                    .Select(asset => new UnrealProjectSyncExportAssetView(
                        asset.AssetName,
                        asset.AssetClass,
                        asset.PackagePath,
                        asset.ObjectPath,
                        asset.SourceRoot,
                        asset.ExportedFilePath))
                    .ToArray();
                return new UnrealProjectSyncVoiceBucket(
                    spec.DisplayName,
                    group.Key.ToString(),
                    views.Length,
                    views);
            })
            .ToArray();
    }

    private static bool IsObjectRedirector(UnrealProjectExportAsset asset) =>
        asset.AssetClass.Contains("ObjectRedirector", StringComparison.OrdinalIgnoreCase);

    private static (string Key, string DisplayName) ClassifyMaterial(string assetName)
    {
        if (TryClassifyStandardMaterialName(assetName, out var standardKind, out var isStandardName))
        {
            return standardKind;
        }

        if (isStandardName)
        {
            return ("OtherImage", "其他图片");
        }

        var name = assetName.Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase);
        if (name.Contains("itemicon", StringComparison.OrdinalIgnoreCase))
        {
            return ("ItemIcon", "图标-道具");
        }

        if (name.Contains("skillicon", StringComparison.OrdinalIgnoreCase))
        {
            return ("SkillIcon", "技能图标");
        }

        if (name.Contains("battleavatar", StringComparison.OrdinalIgnoreCase))
        {
            return ("BattleAvatar", "对局内头像");
        }

        if (name.Contains("fullmorphportrait", StringComparison.OrdinalIgnoreCase))
        {
            return ("FullMorphPortrait", "幻形完整立绘");
        }

        if (name.Contains("morphportrait", StringComparison.OrdinalIgnoreCase))
        {
            return ("MorphPortrait", "幻形立绘");
        }

        if (name.Contains("supportcutin", StringComparison.OrdinalIgnoreCase))
        {
            return ("SupportCutIn", "护援特写");
        }

        if (name.Contains("item", StringComparison.OrdinalIgnoreCase))
        {
            return ("ItemIcon", "图标-道具");
        }

        if (name.Contains("skill", StringComparison.OrdinalIgnoreCase))
        {
            return ("SkillIcon", "技能图标");
        }

        if (name.Contains("battle", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("avatar", StringComparison.OrdinalIgnoreCase))
        {
            return ("BattleAvatar", "对局内头像");
        }

        if (name.Contains("fullmorph", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("fullportrait", StringComparison.OrdinalIgnoreCase))
        {
            return ("FullMorphPortrait", "幻形完整立绘");
        }

        if (name.Contains("morph", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("portrait", StringComparison.OrdinalIgnoreCase))
        {
            return ("MorphPortrait", "幻形立绘");
        }

        if (name.Contains("background", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("bg", StringComparison.OrdinalIgnoreCase))
        {
            return ("Background", "背景图");
        }

        if (name.Contains("support", StringComparison.OrdinalIgnoreCase))
        {
            return ("SupportCutIn", "护援特写");
        }

        if (name.Contains("icon", StringComparison.OrdinalIgnoreCase))
        {
            return ("Icon", "头像");
        }

        return ("OtherImage", "其他图片");
    }

    private static bool TryClassifyStandardMaterialName(
        string assetName,
        out (string Key, string DisplayName) kind,
        out bool isStandardName)
    {
        kind = default;
        isStandardName = false;
        var parts = assetName
            .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        isStandardName = true;
        if (TryResolveStandardMaterialKind(parts[^1], out kind))
        {
            return true;
        }

        if (parts.Length >= 3 && TryResolveStandardMaterialKind(parts[^2], out kind))
        {
            return true;
        }

        return false;
    }

    private static bool TryResolveStandardMaterialKind(
        string type,
        out (string Key, string DisplayName) kind)
    {
        kind = type.ToLowerInvariant() switch
        {
            "itemicon" => ("ItemIcon", "图标-道具"),
            "skillicon" => ("SkillIcon", "技能图标"),
            "battleavatar" => ("BattleAvatar", "对局内头像"),
            "fullmorphportrait" => ("FullMorphPortrait", "幻形完整立绘"),
            "morphportrait" => ("MorphPortrait", "幻形立绘"),
            "background" => ("Background", "背景图"),
            "supportcutin" => ("SupportCutIn", "护援特写"),
            "icon" => ("Icon", "头像"),
            "otherimage" => ("OtherImage", "其他图片"),
            _ => default
        };

        return kind.Key is not null;
    }

    private static DateTime? ParseGeneratedAt(string? generatedAt)
    {
        return DateTime.TryParse(generatedAt, out var value)
            ? value
            : null;
    }

    private static string ResolveEditorCommandPath(string enginePath)
    {
        var folderPath = Path.GetDirectoryName(enginePath);
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return enginePath;
        }

        var commandPath = Path.Combine(folderPath, "UnrealEditor-Cmd.exe");
        return File.Exists(commandPath) ? commandPath : enginePath;
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
        catch
        {
            // Cancellation cleanup should not hide the original cancellation signal.
        }
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        return elapsed.TotalHours >= 1
            ? elapsed.ToString(@"h\:mm\:ss")
            : elapsed.ToString(@"mm\:ss");
    }

    private static string Quote(string value)
    {
        return $"\"{value.Replace("\"", "\\\"")}\"";
    }

    private static string ToJsonStringLiteral(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var character in value)
        {
            builder.Append(character switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\r' => "\\r",
                '\n' => "\\n",
                '\t' => "\\t",
                _ => character.ToString()
            });
        }

        builder.Append('"');
        return builder.ToString();
    }
}
