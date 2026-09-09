using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;

namespace CrossingVoidZDTool.Services;

internal sealed class UnrealProjectSyncService
{
    public const string TargetBaseMaterialContentPath = "/Game/AssetMaterial/ImageS/CharaterS";
    public const string TargetSharedBuffIconContentPath = "/Game/AssetMaterial/ImageS/BUFF";
    public const string TargetZdContentPath = "/Game/GameActor2D";
    public const string TargetCharacterItemContentPath = "/Game/ITems/CharItemS";
    public const string TeamSelectContentPath = "/Game/UIWidget/2DPvpUI";
    public const string TeamSelectObjectPath = "/Game/UIWidget/2DPvpUI/UI_TeamSelect.UI_TeamSelect";
    public const string LinkSkillLibraryObjectPath = "/Game/BaseC/ExCordLibrary/LB_Fucs.LB_Fucs";
    public const string ExportFolderName = "ZDToolboxExport";
    public const string ExportManifestFileName = "characters.json";
    internal const string SequenceExportManifestFileName = "characters-sequences.json";
    public const string ExportScriptRelativePath = @"Tools\Unreal\export_zd_assets.py";

    private static readonly string[] ExportTargetContentPaths =
    [
        TargetBaseMaterialContentPath,
        TargetSharedBuffIconContentPath,
        TargetZdContentPath,
        TeamSelectContentPath
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
        var (exportManifestPath, exportManifest) = UnrealExportManifestReader.ResolveExportManifest(exportDirectoryPath);
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
            UnrealExportManifestReader.ParseGeneratedAt(exportManifest?.GeneratedAt),
            UnrealExportManifestReader.BuildPreviewAssets(exportManifest),
            UnrealExportManifestReader.BuildCharacterCandidates(exportManifest, contentPath),
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

    public void ValidateSequenceCharacterFolders(string projectPath, string characterCode)
    {
        static string ContentRelative(string unrealPath) =>
            unrealPath.TrimStart('/').StartsWith("Game/", StringComparison.OrdinalIgnoreCase)
                ? unrealPath.TrimStart('/')[5..]
                : unrealPath.TrimStart('/');

        var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        var root = Path.Combine(projectDirectory, "Content", ContentRelative(TargetZdContentPath), characterCode);
        var missing = new List<string>();
        foreach (var path in new[] { root, Path.Combine(root, "AnimSequences"), Path.Combine(root, "Material") })
        {
            if (!Directory.Exists(path)) missing.Add(path);
        }
        var item = Path.Combine(projectDirectory, "Content", ContentRelative(TargetCharacterItemContentPath), $"Item_{characterCode}.uasset");
        if (!File.Exists(item)) missing.Add(item);
        if (missing.Count > 0) throw new InvalidOperationException("序列同步底层检查未通过：\n- " + string.Join("\n- ", missing));
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
        // 同样要走合并解析：分步导出不写 characters.json，只读它会拿不到资产类型。
        var manifest = requireAssetTypes
            ? UnrealExportManifestReader.ResolveExportManifest(GetExportDirectoryPath(projectPath)).Manifest
            : null;
        var checks = new List<UnrealPublishFoundationCheckItem>
        {
            CheckExactAsset(contentPath, CombineContentPath(contentPath, TargetCharacterItemContentPath), TargetCharacterItemContentPath,
                $"Item_{characterCode}", "角色 Item", "Blueprint", manifest, requireAssetTypes),
            CheckExactAsset(contentPath, CombineContentPath(contentPath, TeamSelectContentPath), TeamSelectContentPath,
                "UI_TeamSelect", "角色入队语音映射 UI", "WidgetBlueprint", manifest, requireAssetTypes),
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
        if (requireAssetTypes)
        {
            var itemIndex = checks.FindIndex(item => item.DisplayName == "角色 Item");
            var exportedItem = manifest?.CharacterItems.FirstOrDefault(item =>
                string.Equals(item.ObjectPath, itemObjectPath, StringComparison.OrdinalIgnoreCase));
            var parentClass = exportedItem?.ParentClass ?? string.Empty;
            var itemStructureMatches = exportedItem?.HasItemData == true &&
                exportedItem?.ItemData.HasCharData == true &&
                parentClass.Contains("InventoryBaseItem", StringComparison.OrdinalIgnoreCase);
            if (itemIndex >= 0 && checks[itemIndex].IsCompliant && !itemStructureMatches)
            {
                checks[itemIndex] = checks[itemIndex] with
                {
                    IsCompliant = false,
                    Problem = exportedItem?.HasItemData != true || exportedItem?.ItemData.HasCharData != true
                        ? "缺少 ItemData/CharData"
                        : "父类错误",
                    ActualType = string.IsNullOrWhiteSpace(parentClass)
                        ? checks[itemIndex].ActualType
                        : $"{checks[itemIndex].ActualType}；父类 {parentClass}"
                };
            }

            var index = checks.FindIndex(item => item.DisplayName == "角色入队语音映射 UI");
            if (index >= 0 && checks[index].IsCompliant && manifest?.TeamSelect.HasCharVoice != true)
            {
                var readMessage = manifest?.TeamSelect.ReadMessage ?? string.Empty;
                checks[index] = checks[index] with
                {
                    IsCompliant = false,
                    Problem = "缺少 CharVoice",
                    ActualType = string.IsNullOrWhiteSpace(readMessage)
                        ? checks[index].ActualType
                        : $"{checks[index].ActualType}；{readMessage}"
                };
            }
        }

        return checks;
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
        var editorCommandPath = ResolveEditorCommandPath(normalizedEnginePath);
        var startInfo = new ProcessStartInfo
        {
            FileName = editorCommandPath,
            Arguments = $"{Quote(normalizedProjectPath)} -run=pythonscript -script={Quote(scriptPath)} -unattended -nop4",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(editorCommandPath) ?? string.Empty
        };
        foreach (var (key, value) in BuildExportEnvironment(normalizedProjectPath, scope, selectedCharacterCodes))
        {
            startInfo.Environment[key] = value;
        }

        return startInfo;
    }

    /// <summary>
    /// 导出脚本认的那批环境变量。
    ///
    /// 抽出来是因为桥接同步要在同一个编辑器会话里顺手把复扫导出做掉：
    /// 一次同步原本要开三次编辑器，而实测每次会话 13-15 秒里约 9 秒是纯启动开销。
    /// 两边必须用同一套变量，否则复扫会写到别的清单上。
    /// </summary>
    public IReadOnlyDictionary<string, string> BuildExportEnvironment(
        string projectPath,
        UnrealProjectSyncExportScope scope,
        IReadOnlyCollection<string>? selectedCharacterCodes)
    {
        var normalizedProjectPath = NormalizePath(projectPath);
        var manifestPath = GetExportManifestPath(normalizedProjectPath, scope);
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ZD_TOOLBOX_PROJECT_PATH"] = normalizedProjectPath,
            ["ZD_TOOLBOX_EXPORT_MANIFEST"] = manifestPath,
            ["ZD_TOOLBOX_EXPORT_PROGRESS"] = GetExportProgressPath(manifestPath),
            ["ZD_TOOLBOX_TARGET_PATHS"] = $"[{string.Join(", ", ExportTargetContentPaths.Select(ToJsonStringLiteral))}]",
            ["ZD_TOOLBOX_EXPORT_SCOPE"] = scope.ToString(),
        };
        if (selectedCharacterCodes is { Count: > 0 })
        {
            values["ZD_TOOLBOX_SELECTED_CHARACTERS"] =
                $"[{string.Join(", ", selectedCharacterCodes.Select(ToJsonStringLiteral))}]";
        }

        return values;
    }

    /// <summary>某个导出范围对应的清单文件路径。</summary>
    public string GetExportManifestPath(string projectPath, UnrealProjectSyncExportScope scope) =>
        Path.Combine(
            GetExportDirectoryPath(NormalizePath(projectPath)),
            GetExportManifestFileName(scope));

    private static string GetExportManifestFileName(UnrealProjectSyncExportScope scope) =>
        scope switch
        {
            UnrealProjectSyncExportScope.CharacterSequences => SequenceExportManifestFileName,
            UnrealProjectSyncExportScope.CharacterMaterials => "characters-materials.json",
            UnrealProjectSyncExportScope.Normalization => "characters-normalization.json",
            _ => ExportManifestFileName
        };

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
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            // 读不出来只能当「没有这份清单」——.modules 缺失在纯蓝图项目里本来就正常，
            // 不该因此挡住导入。但要留一行：返回 null 会让 ValidateProjectModuleBuildIds
            // 那条「引擎与项目二进制版本不一致」的守卫整个失效，用户拿着旧二进制一路走下去，
            // 最后在 Unreal 里撞上一堆看不懂的报错。
            ToolboxLog.Warn($"读不了 Unreal 模块清单，已跳过引擎版本一致性检查：{path}", error);
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
        var manifestPath = Path.Combine(GetExportDirectoryPath(normalizedProjectPath), GetExportManifestFileName(scope));
        var progressPath = GetExportProgressPath(manifestPath);
        var taskExecutionService = new UnrealPythonTaskExecutionService();
        var useRunningEditor = taskExecutionService.ShouldUseRunningEditor(enginePath, normalizedProjectPath);
        var offlineStartInfo = BuildExportProcessStartInfo(
            enginePath,
            projectPath,
            selectedCodes,
            validateProjectModules: !useRunningEditor,
            scope: scope);
        offlineStartInfo.RedirectStandardOutput = true;
        offlineStartInfo.RedirectStandardError = true;
        // 子进程写的是 UTF-8（在线执行那条路径还显式设了 PYTHONUTF8=1）；不指定编码时
        // 父进程按 OEM 代码页解码，中文 Windows 是 936，这段日志必然乱码——
        // 而它恰恰只在导出失败时才会被人翻出来看。
        offlineStartInfo.StandardOutputEncoding = Encoding.UTF8;
        offlineStartInfo.StandardErrorEncoding = Encoding.UTF8;
        var launch = taskExecutionService.BuildLaunch(
            NormalizePath(enginePath),
            normalizedProjectPath,
            GetExportScriptPath(),
            Path.Combine(Path.GetDirectoryName(manifestPath)!, "export.remote-job.json"),
            offlineStartInfo,
            useRunningEditor);
        var startInfo = launch.StartInfo;
        UnrealProcessRunner.TryClearStaleFile(progressPath);
        progress?.Report(new ProgressUpdate("正在准备导出目录...", 18, manifestPath));
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);

        progress?.Report(new ProgressUpdate(
            launch.UsesRunningEditor ? "正在连接已打开的 Unreal Editor..." : "正在启动 Unreal Editor 命令进程...",
            35,
            startInfo.FileName));
        progress?.Report(new ProgressUpdate(
            launch.UsesRunningEditor
                ? "已连接 Unreal Editor，正在执行导出脚本..."
                : "Unreal 已启动，正在加载项目并执行导出脚本...",
            45,
            selectedCodes.Count == 0
                ? "首次加载项目可能需要较长时间。"
                : $"本次只获取 {selectedCodes.Count} 个选中角色：{string.Join("、", selectedCodes)}。"));

        // 这里的进度转发不能用 Runner 那个「文件没变就不推」的通用转发器：
        // 详情里带着「已等待 mm:ss」，脚本静默不写进度时也得每两秒刷一次，
        // 不然界面看起来就是卡死了。所以只借 Runner 的轮询节拍，策略留在本地。
        var lastProgressReportAt = DateTime.MinValue;
        UnrealProjectExportManifest? manifest = null;
        var run = await UnrealProcessRunner.RunAsync(
            startInfo,
            TimeSpan.FromMinutes(10),
            "无法启动 Unreal Python 任务进程。",
            "Unreal Editor 扫描超过 10 分钟，已终止进程，避免工具箱长时间无响应。",
            onPoll: elapsed =>
            {
                if ((DateTime.UtcNow - lastProgressReportAt).TotalSeconds < 2)
                {
                    return;
                }

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
            },
            // 导出成没成功，以清单为准，不以退出码为准。
            // commandlet 只要编辑器在别处报过错就返回非 0——实测工程里有个蓝图编译不过，
            // 于是每次检测都被判成「导出失败」，而日志里明写着 Python script executed successfully、
            // 清单也照常写出来了。清单缺失或不是这一轮写的，才是真失败。
            //
            // 这条规则只对导出成立，所以它是传进去的回调、不是 Runner 里的通用逻辑：
            // 别的链路（同步执行、蓝图置入、基础配置）判的是各自的结果文件，规则并不一样。
            verdict: completed =>
            {
                // 校验就在这个回调里做，读盘前先把进度推到 94%，免得界面停在 90 干等。
                progress?.Report(new ProgressUpdate("正在读取 Unreal 导出结果...", 94, manifestPath));
                manifest = UnrealExportManifestReader.LoadExportManifest(manifestPath);
                var runStartedAtUtc = completed.StartedAtUtc;
                var manifestIsFresh = manifest is not null && UnrealProcessRunner.TryGetLastWriteUtc(manifestPath) > runStartedAtUtc;
                if (manifest is null || !manifestIsFresh)
                {
                    return completed.ExitCode != 0
                        ? $"Unreal 角色数据导出失败，退出码 {completed.ExitCode}。\n{DescribeProcessOutput(completed.Output)}"
                        : $"Unreal 导出进程已结束，但没有生成有效清单：{manifestPath}";
                }

                return null;
            },
            cancellationToken: cancellationToken);

        var output = run.Output;
        var exportWarning = run.ExitCode == 0
            ? string.Empty
            : $"Unreal 退出码为 {run.ExitCode}，但导出清单已正常写出；退出码多半来自与导出无关的编辑器报错。\n{DescribeProcessOutput(output)}";

        var assetCount = manifest?.Assets.Count ?? 0;
        var characterItemCount = manifest?.CharacterItems.Count ?? 0;
        var characterSequenceCount = manifest?.CharacterSequences.Count ?? 0;
        var totalCount = assetCount + characterItemCount + characterSequenceCount;
        progress?.Report(new ProgressUpdate(
            "项目角色导出完成。",
            100,
            $"导出资产 {assetCount} 个，角色物品 {characterItemCount} 个，序列预览 {characterSequenceCount} 个，BUFF {(manifest?.CharacterBuffs.Count ?? 0)} 组。"));
        return new UnrealProjectSyncExportRunResult(run.ExitCode, manifestPath, totalCount, output, exportWarning);
    }

    /// <summary>
    /// 报错里带的进程输出：截尾保留最后 4000 字。Unreal 的日志前面全是启动噪声，
    /// 真正的失败原因总在末尾。
    /// </summary>
    private static string DescribeProcessOutput(string output)
    {
        var detail = string.IsNullOrWhiteSpace(output)
            ? "Unreal 未返回标准输出；请查看项目 Saved/Logs 下的最新日志。"
            : output.Trim();
        return detail.Length > 4000 ? detail[^4000..] : detail;
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
            // 这里**故意不写日志**：进度文件每两秒读一次，撞上 Python 侧写到一半是常态
            // 而不是故障（UnrealProgressFileWatcher.Poll 是同一个判断），记下来只会刷满
            // 日志面板、盖掉真正的失败。后果也只是这一轮退回通用文案，下一轮就好了。
            return null;
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
        catch (Exception error)
        {
            // 路径写得不成样子（非法字符、盘符不存在……）时原样退回用户填的文本，
            // 让后面的 File.Exists 去报「找不到」——那句报错比这里抛出来更好懂。
            // 但要留一行：此后所有以这个路径拼出来的判断都建立在一个没规范化的字符串上，
            // 出问题时得知道源头在这儿。
            ToolboxLog.Warn($"路径无法规范化，按原样使用：{path}", error);
            return path.Trim().Trim('"');
        }
    }

    internal static string CombineContentPath(string contentPath, string unrealContentPath)
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

    internal static string NormalizeObjectPath(string value)
    {
        return value.Trim().Replace("\\", "/", StringComparison.OrdinalIgnoreCase);
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
