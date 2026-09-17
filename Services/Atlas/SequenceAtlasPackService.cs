using System;
using System.Collections.Generic;
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
        var actions = UnrealBridgeSequencePublishService.ResolveSelectedActions(selectedChanges);
        if (actions.Count == 0)
        {
            return result;
        }

        // 动作代号解析成动作卡，是为了拿素材目录 —— 目录名用的是动作卡上的代号，
        // 不是规范代号，形态变体尤其不能想当然。
        var sectionByAction = new SequenceFrameService()
            .LoadSections(character, new CharacterSkillsService().Load(character))
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
            var framesFolder = SequenceActionFolderLayout.GetFramesFolderPath(character, section.Action);
            var outputDirectory = AtlasPackService.ResolveOutputDirectory(
                AtlasDestination.Cache,
                projectRootPath: string.Empty,
                character.FolderPath,
                character.Code,
                variantCode);
            AtlasPackService.ClearCache(outputDirectory);

            var sourceImages = AtlasManifestWriter.EnumerateSourceImages(framesFolder);
            var packedAt = DateTime.UtcNow;
            var packed = await packer
                .PackAsync(
                    character.Code,
                    framesFolder,
                    definition,
                    formIndex,
                    outputDirectory,
                    configuredPythonPath,
                    progress,
                    cancellationToken,
                    ResolveMaxSize(sourceImages.Count))
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
}
