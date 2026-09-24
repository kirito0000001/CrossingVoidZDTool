using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CrossingVoidZDTool.Services.Atlas;

/// <summary>
/// 同步序列之前，给勾选的动作各打一张图集。
///
/// 这一步的存在理由很直接：Unreal 侧要的是「一张贴图切 N 个精灵」，
/// 而装箱是图集工具做的 —— 框在哪只有它说了算。所以同步前必须先打一遍，
/// 把每格的矩形拿到手，再写进同步计划。
///
/// 三条边界：
/// <list type="bullet">
/// <item><b>只打勾选的动作。</b>没勾的不打 —— 打包是要起进程的，不该为不动的动作付钱。</item>
/// <item><b>落可删缓存。</b><see cref="AtlasDestination.Cache"/>，每轮先清再打。
/// 素材可能已经改过，而图集产物无法自证对应的是哪一版素材，复用上一轮就是拿旧框切新图。</item>
/// <item><b>没有帧的动作跳过。</b>计划生成那边同样会跳过，这里不替它报错。</item>
/// </list>
/// </summary>
internal sealed class SequenceAtlasPackService
{
    /// <summary>尺寸上限的起步值。低于它的搜索空间没必要开。</summary>
    private const int MinimumMaxSize = 2048;

    /// <summary>尺寸上限的封顶。再大就超出常见贴图规格了，宁可失败让人看见。</summary>
    private const int MaximumMaxSize = 8192;

    public async Task<IReadOnlyDictionary<string, UnrealBridgeSequenceAtlasInput>> PackAsync(
        CharacterCard character,
        IReadOnlyList<UnrealBridgeChange> selectedChanges,
        string? configuredPythonPath = null,
        IProgress<AtlasPackProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(selectedChanges);

        var result = new Dictionary<string, UnrealBridgeSequenceAtlasInput>(StringComparer.OrdinalIgnoreCase);
        // 勾选的动作 + 借用素材的来源动作：借用方的精灵指向来源图集，来源必须一起打。
        var actions = UnrealBridgeSequencePublishService.ResolveActionsWithSourceOwners(character, selectedChanges);
        if (actions.Count == 0)
        {
            return result;
        }

        // 动作代号解析成动作卡，是为了拿素材目录 —— 目录名用的是动作卡上的代号，
        // 不是规范代号，形态变体尤其不能想当然。
        // 这一份清单在这一段里要问两次（查来源动作、查帧数），原来调了两遍 `LoadSections`，
        // 每遍都把整角色的序列帧从磁盘读一次。读一次、传下去（局部去重，不动对外签名）。
        var sections = new SequenceFrameService()
            .LoadSections(character, new CharacterSkillsService().Load(character));
        var sectionByAction = sections
            .Select(section => (section, Resolved: Resolve(section.Action.Code)))
            .Where(pair => pair.Resolved is not null)
            .GroupBy(pair => (pair.Resolved!.Value.Definition.Code, pair.Resolved!.Value.FormIndex))
            .ToDictionary(group => group.Key, group => group.First().section);

        var packer = new AtlasPackService();
        foreach (var (definition, formIndex) in actions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 没有帧的动作会被计划生成跳过，不必为它打一张空图集 ——
            // 空图集本身也会被打包器当场拒绝。
            if (!sectionByAction.TryGetValue((definition.Code, formIndex), out var section) ||
                section.Frames.Count == 0)
            {
                continue;
            }

            var variantCode = SequenceActionCatalog.GetVariantCode(definition, formIndex);

            // 特效只打**网格 sheet**（Niagara 面片用）。
            //
            // 以前这里还打一张 Paper2D 口径的紧凑图集（pack + trim，配精灵和 `_Effect_Flipbook`）；
            // 「只出特效」之后那条线整个不做了 —— 特效只用 sheet + 材质实例两样。
            // `PackEffectAtlasAsync` 的实现留着（不接线），万一将来又要 Paper2D 口径可以直接接回来。
            await PackEffectSheetAsync(
                character,
                section,
                definition,
                formIndex,
                configuredPythonPath,
                progress,
                cancellationToken,
                result);

            var outputDirectory = AtlasPackService.ResolveOutputDirectory(
                AtlasDestination.Cache,
                projectRootPath: string.Empty,
                character.FolderPath,
                character.Code,
                variantCode);
            AtlasPackService.ClearCache(outputDirectory);

            // 图集只收**这个动作自己的**图。借来的图已经在来源动作的图集里了，
            // 再打一份就是同一张图存两遍（Misaka 实测这样的重复有 23 格）。
            var sourceImages = SequenceActionFolderLayout
                .ResolveSourceImagePlan(character, section.Action, section.Frames)
                .Where(image => image.IsOwn)
                .Select(image => image.FilePath)
                .ToArray();
            if (sourceImages.Length == 0)
            {
                // 一张自己的图都没有：整条都在复用别人的素材，这个动作**没有**自己的图集。
                // 计划生成那边按「图集归属」去找来源图集，不需要它。
                // 顺手把上一轮留下的缓存清掉：那是对应「这个动作曾经有自己的图」的产物，
                // 留着只会让人以为它还有自己的图集。
                ClearStaleCacheEntry(character, variantCode);
                continue;
            }

            var packedAt = DateTime.UtcNow;
            var packed = await packer
                .PackAsync(
                    character.Code,
                    sourceImages,
                    definition,
                    formIndex,
                    outputDirectory,
                    configuredPythonPath,
                    progress,
                    cancellationToken,
                    ResolveMaxSize(sourceImages.Length))
                .ConfigureAwait(false);

            var manifest = AtlasSequenceManifestReader.Read(
                AtlasSequenceManifestReader.GetManifestPath(packed.OutputDirectory, packed.AtlasName),
                packedAt);

            result[UnrealBridgeSequencePublishService.AtlasKey(definition.Code, formIndex)] =
                new UnrealBridgeSequenceAtlasInput
                {
                    AtlasName = packed.AtlasName,
                    ImagePath = packed.ImagePath,
                    Width = packed.Width,
                    Height = packed.Height,
                    FramesByOrdinal = AtlasSequenceManifestReader.IndexByOrdinal(manifest),
                };
        }

        return result;
    }

    /// <summary>
    /// 打这一层的特效图集。没有特效图（或全是空帧）就跳过 —— 计划生成那边同样会跳过，
    /// 不替它报错。
    ///
    /// 和角色序列**各打一张**（<c>&lt;角色&gt;_&lt;动作&gt;_Effect</c>）：特效帧率和张数都不一样，
    /// 混进同一张只会让两边都算不清。
    /// </summary>
    private static async Task PackEffectAtlasAsync(
        AtlasPackService packer,
        CharacterCard character,
        SequenceFrameSection section,
        SequenceActionDefinition definition,
        int formIndex,
        string? configuredPythonPath,
        IProgress<AtlasPackProgress>? progress,
        CancellationToken cancellationToken,
        Dictionary<string, UnrealBridgeSequenceAtlasInput> result)
    {
        var layer = new SequenceEffectService().Load(character, section.Action);
        var layout = SequenceEffectSyncService.TryBuildLayout(
            character,
            section,
            definition,
            formIndex,
            layer,
            new SequenceFrameService().GetActionFps(character, section.Action));
        if (layout is null)
        {
            return;
        }

        var sourceImages = layout.FilledFrames.Select(frame => frame.FilePath).ToArray();
        if (sourceImages.Length == 0)
        {
            return;
        }

        var outputDirectory = AtlasPackService.ResolveOutputDirectory(
            AtlasDestination.Cache,
            projectRootPath: string.Empty,
            character.FolderPath,
            character.Code,
            layout.LayerCode);
        AtlasPackService.ClearCache(outputDirectory);
        var packedAt = DateTime.UtcNow;
        // 特效图集走**通用**打包入口（能显式指定图集名和精灵名）：
        // 名字必须和计划里写的一致 —— 计划说 `<动作>_Effect_FrameNN_Sprite`，这里就得切出同名精灵。
        var packed = await new AtlasFolderPackService()
            .PackAsync(
                new AtlasCreateRequest(
                    Path.GetDirectoryName(sourceImages[0]) ?? string.Empty,
                    outputDirectory,
                    layout.AtlasName,
                    AtlasFolderPackService.PackMode,
                    Columns: 0,
                    Padding: 2,
                    Trim: true,
                    MaxSize: ResolveMaxSize(sourceImages.Length)),
                configuredPythonPath,
                progress,
                cancellationToken,
                spriteNameForIndex: position => layout.FilledFrames[position - 1].SpriteAssetName,
                sourceImages: sourceImages)
            .ConfigureAwait(false);

        var manifest = AtlasSequenceManifestReader.Read(
            AtlasSequenceManifestReader.GetManifestPath(packed.OutputDirectory, layout.AtlasName),
            packedAt);
        var atlasSize = AtlasFolderPackService.ReadPngSize(packed.AtlasImagePath);
        result[UnrealBridgeSequencePublishService.AtlasKey(layout.LayerCode, formIndex)] =
            new UnrealBridgeSequenceAtlasInput
            {
                AtlasName = layout.AtlasName,
                ImagePath = packed.AtlasImagePath,
                Width = atlasSize.Width,
                Height = atlasSize.Height,
                FramesByOrdinal = AtlasSequenceManifestReader.IndexByOrdinal(manifest),
            };
    }

    /// <summary>
    /// **只打特效网格 sheet**，不需要"勾选的变化"。
    ///
    /// 第六步「特效同步」专用：特效该有几张、网格几×几全部来自工作区的特效帧目录，
    /// 所以这一步不必先把整条序列的帧从 Unreal 导出一遍（用户明确要求省掉那一步）。
    /// 有特效层的动作才打得出 sheet；一张特效图都没有的动作会被 `TryBuildLayout` 跳过。
    /// </summary>
    public async Task<IReadOnlyDictionary<string, UnrealBridgeSequenceAtlasInput>> PackEffectSheetsAsync(
        CharacterCard character,
        string? configuredPythonPath = null,
        IProgress<AtlasPackProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(character);
        var result = new Dictionary<string, UnrealBridgeSequenceAtlasInput>(StringComparer.OrdinalIgnoreCase);
        var sections = new SequenceFrameService()
            .LoadSections(character, new CharacterSkillsService().Load(character));
        foreach (var section in sections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Resolve(section.Action.Code) is not { } resolved)
            {
                continue;
            }

            await PackEffectSheetAsync(
                character,
                section,
                resolved.Definition,
                resolved.FormIndex,
                configuredPythonPath,
                progress,
                cancellationToken,
                result)
                .ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>
    /// 打 Niagara 用的**网格 sheet**：一个输出帧一格，空帧用全透明图占位。
    ///
    /// 和 Paper2D 那张图集分开打（那张是 pack + trim，每格矩形不同），因为 Sub UV 只认等分网格。
    /// **空帧必须占格**：跳过一格，后面所有帧的 SubUV 索引就整体错位，而且错得很像"本来就该这样"。
    /// </summary>
    private static async Task PackEffectSheetAsync(
        CharacterCard character,
        SequenceFrameSection section,
        SequenceActionDefinition definition,
        int formIndex,
        string? configuredPythonPath,
        IProgress<AtlasPackProgress>? progress,
        CancellationToken cancellationToken,
        Dictionary<string, UnrealBridgeSequenceAtlasInput> result)
    {
        var layer = new SequenceEffectService().Load(character, section.Action);
        var layout = SequenceEffectSyncService.TryBuildLayout(
            character,
            section,
            definition,
            formIndex,
            layer,
            new SequenceFrameService().GetActionFps(character, section.Action));
        if (layout is null || layout.Frames.Count == 0)
        {
            return;
        }

        var reference = layout.Frames.FirstOrDefault(frame => !frame.IsEmpty);
        if (reference is null)
        {
            return; // 整层都是空帧：没有可画的东西，也就不需要 sheet
        }

        var referenceSize = AtlasFolderPackService.ReadPngSize(reference.FilePath);
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "zd-effect-sheet-" + Guid.NewGuid().ToString("N"));
        try
        {
            var sourceImages = new List<string>(layout.Frames.Count);
            foreach (var frame in layout.Frames)
            {
                if (!frame.IsEmpty)
                {
                    sourceImages.Add(frame.FilePath);
                    continue;
                }

                Directory.CreateDirectory(temporaryRoot);
                var placeholder = Path.Combine(temporaryRoot, $"Cell{frame.OutputOrdinal:0000}.png");
                WriteTransparentPng(placeholder, referenceSize.Width, referenceSize.Height);
                sourceImages.Add(placeholder);
            }

            var outputDirectory = AtlasPackService.ResolveOutputDirectory(
                AtlasDestination.Cache,
                projectRootPath: string.Empty,
                character.FolderPath,
                character.Code,
                layout.LayerCode);
            var packedAt = DateTime.UtcNow;
            var packed = await new AtlasFolderPackService()
                .PackAsync(
                    new AtlasCreateRequest(
                        Directory.Exists(temporaryRoot)
                            ? temporaryRoot
                            : Path.GetDirectoryName(reference.FilePath) ?? string.Empty,
                        outputDirectory,
                        layout.SheetName,
                        AtlasFolderPackService.GridMode,
                        Columns: layout.Columns,
                        // 网格要能被行列数整除：**不加 padding、不裁边**，否则 Sub UV 切出来会偏。
                        Padding: 0,
                        Trim: false,
                        MaxSize: ResolveMaxSize(sourceImages.Count)),
                    configuredPythonPath,
                    progress,
                    cancellationToken,
                    spriteNameForIndex: position =>
                        string.IsNullOrWhiteSpace(layout.Frames[position - 1].SpriteAssetName)
                            ? $"{layout.SheetName}_Cell{position - 1:00}"
                            : layout.Frames[position - 1].SpriteAssetName,
                    sourceImages: sourceImages)
                .ConfigureAwait(false);

            var manifest = AtlasSequenceManifestReader.Read(
                AtlasSequenceManifestReader.GetManifestPath(packed.OutputDirectory, layout.SheetName),
                packedAt);
            var sheetSize = AtlasFolderPackService.ReadPngSize(packed.AtlasImagePath);
            result[UnrealBridgeSequencePublishService.AtlasKey(layout.SheetName, formIndex)] =
                new UnrealBridgeSequenceAtlasInput
                {
                    AtlasName = layout.SheetName,
                    ImagePath = packed.AtlasImagePath,
                    Width = sheetSize.Width,
                    Height = sheetSize.Height,
                    FramesByOrdinal = AtlasSequenceManifestReader.IndexByOrdinal(manifest)
                };
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
            {
                try
                {
                    Directory.Delete(temporaryRoot, recursive: true);
                }
                catch (IOException)
                {
                    // 临时目录删不掉不影响这一轮结果，系统清理会收走。
                }
            }
        }
    }

    /// <summary>写一张全透明 PNG（空帧占位用）。</summary>
    private static void WriteTransparentPng(string path, int width, int height)
    {
        using var bitmap = new System.Drawing.Bitmap(
            Math.Max(1, width),
            Math.Max(1, height),
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    /// <summary>
    /// 算这次的尺寸上限。
    ///
    /// 用「张数 × 帧规格」的面积开方，再向上取到 2 的幂 —— 这算的是**不裁剪时**的下界，
    /// 而实际内容通常比规格小得多（Sk2 的 17 帧裁完只占 1114×1018），
    /// 所以这个上限只会偏大，不会算小了让人装不下。
    ///
    /// 它只约束搜索范围：真正用多大由打包器挑，挑完从 report 回读。
    /// </summary>
    private static int ResolveMaxSize(int frameCount)
    {
        if (frameCount <= 0)
        {
            return MinimumMaxSize;
        }

        var area = (double)frameCount * SequenceFrameSpec.RequiredWidth * SequenceFrameSpec.RequiredHeight;
        var required = Math.Ceiling(Math.Sqrt(area));
        var size = MinimumMaxSize;
        while (size < required && size < MaximumMaxSize)
        {
            size *= 2;
        }

        return Math.Min(size, MaximumMaxSize);
    }

    private static (SequenceActionDefinition Definition, int FormIndex)? Resolve(string? rawCode) =>
        SequenceActionCatalog.TryResolve(rawCode, out var definition, out var formIndex)
            ? (definition, formIndex)
            : null;

    /// <summary>
    /// 这个动作已经不需要自己的图集了（整条都在借用别人的素材）——
    /// 把上一轮留在缓存里的那张清掉。留着只会让人以为它还有自己的图集。
    /// 缓存目录按变体代号命名，路径能直接算出来。
    /// </summary>
    private static void ClearStaleCacheEntry(CharacterCard character, string variantCode)
    {
        var outputDirectory = AtlasPackService.ResolveOutputDirectory(
            AtlasDestination.Cache,
            projectRootPath: string.Empty,
            character.FolderPath,
            character.Code,
            variantCode);
        if (!Directory.Exists(outputDirectory))
        {
            return;
        }

        try
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ToolboxLog.Warn($"旧图集缓存没有删掉：{outputDirectory}", error);
        }
    }
}
