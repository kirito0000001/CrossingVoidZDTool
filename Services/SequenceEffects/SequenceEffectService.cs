using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 特效层：导入、读盘、清空。
///
/// 磁盘形状（跟着动作目录走，和序列帧、图集缓存同一个父目录）：
/// <code>
/// &lt;角色&gt;/ZDMaterial/&lt;动作&gt;/Effects/&lt;动作&gt;_&lt;层名&gt;_0001.png …（例：DefAtk_Effect_0001.png）
/// &lt;角色&gt;/ZDMaterial/&lt;动作&gt;/Effects2/&lt;动作&gt;_&lt;层名&gt;_0001.png …（例：DefAtk_Effect2_0001.png）
/// </code>
///
/// **一个动作可以有好几层特效**（晓桀 2026-09-27：「从 PSD 导入新增一个层 … 为了制作分层特效用」）：
/// 层号从 1 起，第 1 层是 <c>Effects\</c> + 文件名里的 <c>Effect</c>，第 2 层是 <c>Effects2\</c> + <c>Effect2</c>，
/// 一层一个目录、一层一套文件名，互不干扰。层号是**目录名**算出来的，不另存清单。
///
/// 不写额外的清单文件：**文件名里的编号就是序号**，空帧 = 那个编号没有文件。
/// 少一个清单就少一处会和实际文件对不上的地方。
///
/// 导入的时候：
/// <list type="bullet">
/// <item>目标帧数按动作算（总格数 × 倍数），超出的编号忽略并回报；缺的编号就是空帧；</item>
/// <item>整张全透明的图也算空帧（用底板画的时候没动那几页，最容易是这种）；</item>
/// <item>层目录**只留这一次导入的结果**，所以导入前先清空它。</item>
/// </list>
/// </summary>
internal sealed class SequenceEffectService
{
    /// <summary>默认层名（第 1 层）。第 N 层是 <c>Effect2</c>、<c>Effect3</c>…（见 <see cref="BuildLayerName"/>）。</summary>
    public const string DefaultLayerName = "Effect";

    /// <summary>默认层号。层号从 1 起；0 和负数一律当第 1 层。</summary>
    public const int DefaultLayerIndex = 1;

    /// <summary>
    /// 层号上限。防手滑建出 <c>Effects99</c> 这种，也防「导入到第几层」的菜单被盘上的怪目录撑爆。
    /// </summary>
    public const int MaxLayerIndex = 9;

    /// <summary>第 1 层那个目录的名字（<c>Effects</c>）；第 N 层是 <c>Effects2</c>…</summary>
    private const string EffectsFolderName = "Effects";

    private static readonly Regex TrailingNumberPattern = new(@"(\d+)(?!.*\d)", RegexOptions.Compiled);

    /// <summary>层号 → 层名：1 → <c>Effect</c>，2 → <c>Effect2</c>（层名写在每个文件名里）。</summary>
    public static string BuildLayerName(int layerIndex) =>
        layerIndex <= DefaultLayerIndex ? DefaultLayerName : $"{DefaultLayerName}{layerIndex}";

    /// <summary>层号 → 目录名：1 → <c>Effects</c>，2 → <c>Effects2</c>。</summary>
    public static string BuildLayerFolderName(int layerIndex) =>
        layerIndex <= DefaultLayerIndex ? EffectsFolderName : $"{EffectsFolderName}{layerIndex}";

    /// <summary>
    /// 层名 → 层号：<c>Effect</c> → 1、<c>Effect2</c> → 2（大小写不敏感）。
    /// 认不出来（空、<c>EffectB</c> 这种、后缀不是数字）返回 0，调用方自己决定当几层。
    /// </summary>
    public static int ResolveLayerIndex(string? layerName)
    {
        var name = (layerName ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return 0;
        }

        if (string.Equals(name, DefaultLayerName, StringComparison.OrdinalIgnoreCase))
        {
            return DefaultLayerIndex;
        }

        if (!name.StartsWith(DefaultLayerName, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var suffix = name[DefaultLayerName.Length..];
        return int.TryParse(suffix, out var index) ? Math.Max(DefaultLayerIndex, index) : 0;
    }

    /// <summary>第 1 层的目录（=<c>&lt;动作&gt;\Effects</c>）。历史上只有这一层，名字留着给老调用点用。</summary>
    public static string GetEffectsRootPath(CharacterCard character, SequenceFrameAction action) =>
        GetLayerFolderPath(character, action);

    /// <summary>
    /// 这一层的帧**直接放在层目录里**（不再套 <c>&lt;层名&gt;\Frames\</c>）。
    ///
    /// 晓桀 2026-09-27：「特效多套了两层路径」—— 原来落点是
    /// <c>&lt;动作&gt;\Effects\Effect\Frames\</c>，<c>Effect</c> 和 <c>Frames</c> 两层都是多余的：
    /// 层名本来就写在每个文件名里（<c>Misaka_Sk2_Effect_0001.png</c>），
    /// 而 <c>Effects\</c> 本身已经说明这是特效。现在就是 <c>&lt;动作&gt;\Effects\&lt;帧&gt;.png</c>。
    ///
    /// 第 2 层起同理，只是目录换成了 <c>Effects2\</c>（层号进目录名，层名进文件名）。
    ///
    /// 旧落点由 <see cref="MigrateLegacyLayout"/> 一次性搬上来（人已经画好的东西不能丢）。
    /// </summary>
    public static string GetLayerFolderPath(
        CharacterCard character,
        SequenceFrameAction action,
        int layerIndex = DefaultLayerIndex) =>
        Path.Combine(SequenceActionFolderLayout.GetActionFolderPath(character, action), BuildLayerFolderName(layerIndex));

    public static string GetLayerFramesFolderPath(
        CharacterCard character,
        SequenceFrameAction action,
        int layerIndex = DefaultLayerIndex) =>
        GetLayerFolderPath(character, action, layerIndex);

    /// <summary>
    /// 这个动作盘上现有的层号（升序）。**目录在就算**，空的也算 —— 人可能先把 <c>Effects2\</c> 建好
    /// 再慢慢画，「导入到第几层」的清单得能把空目录列出来。
    /// 认不出的目录（<c>EffectsBak</c>、超过 <see cref="MaxLayerIndex"/> 的）不进清单。
    /// </summary>
    public static IReadOnlyList<int> FindLayerIndexes(CharacterCard character, SequenceFrameAction action)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(action);
        var actionFolder = SequenceActionFolderLayout.GetActionFolderPath(character, action);
        if (!Directory.Exists(actionFolder))
        {
            return Array.Empty<int>();
        }

        var indexes = new List<int>();
        foreach (var folder in Directory.EnumerateDirectories(actionFolder))
        {
            var name = Path.GetFileName(folder);
            if (!name.StartsWith(EffectsFolderName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var suffix = name[EffectsFolderName.Length..];
            var index = suffix.Length == 0
                ? DefaultLayerIndex
                : int.TryParse(suffix, out var parsed) ? parsed : 0;
            if (index >= DefaultLayerIndex && index <= MaxLayerIndex)
            {
                indexes.Add(index);
            }
        }

        return indexes.Distinct().OrderBy(index => index).ToArray();
    }

    /// <summary>
    /// 把这一层的目录建出来（切到某一层 / 挑「新建第 N 层」时先建好，
    /// 「打开特效目录」和导出落点才有个实在的地方）。已经存在就什么都不做。
    /// </summary>
    public static string EnsureLayerFolder(
        CharacterCard character,
        SequenceFrameAction action,
        int layerIndex = DefaultLayerIndex)
    {
        var folder = GetLayerFolderPath(character, action, layerIndex);
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>第 N 层的目录名（给日志和提示文案用）：<c>Effects\</c> / <c>Effects2\</c>。</summary>
    public static string FormatLayerFolderName(int layerIndex) => $"{BuildLayerFolderName(layerIndex)}\\";

    /// <summary>旧落点：<c>&lt;动作&gt;\Effects\&lt;层名&gt;\Frames\</c>（2026-09-27 之前）。</summary>
    private static string GetLegacyLayerFramesFolderPath(
        CharacterCard character,
        SequenceFrameAction action,
        int layerIndex) =>
        Path.Combine(
            GetEffectsRootPath(character, action),
            BuildLayerName(layerIndex),
            SequenceActionFolderLayout.FramesFolderName);

    /// <summary>
    /// 把「多套了两层」的旧落点搬成新落点（一次性；搬过就什么都不做）。
    ///
    /// 只在旧目录**有图**、而且新目录里**还没有这一层的图**时才搬 ——
    /// 两边都有就什么都不动（那是已经在用新落点了，别去搅和）。
    /// </summary>
    internal static void MigrateLegacyLayout(
        CharacterCard character,
        SequenceFrameAction action,
        int layerIndex = DefaultLayerIndex)
    {
        var layerName = BuildLayerName(layerIndex);
        var legacy = GetLegacyLayerFramesFolderPath(character, action, layerIndex);
        if (!Directory.Exists(legacy))
        {
            return;
        }

        var legacyFiles = Directory.EnumerateFiles(legacy, "*.png").ToArray();
        if (legacyFiles.Length == 0)
        {
            TryRemoveEmptyLegacyFolders(legacy);
            return;
        }

        var current = GetLayerFramesFolderPath(character, action, layerIndex);
        var alreadyThere = Directory.Exists(current) &&
            Directory.EnumerateFiles(current, "*.png")
                .Any(path => IsFrameOfLayer(Path.GetFileName(path), action.Code, layerName));
        if (alreadyThere)
        {
            return;
        }

        Directory.CreateDirectory(current);
        var moved = 0;
        foreach (var file in legacyFiles)
        {
            var target = Path.Combine(current, Path.GetFileName(file));
            try
            {
                File.Move(file, target, overwrite: true);
                moved++;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                ToolboxLog.Warn($"特效帧搬家失败（留在旧位置）：{file}", error);
            }
        }

        TryRemoveEmptyLegacyFolders(legacy);
        if (moved > 0)
        {
            ToolboxLog.Info(
                $"特效帧落点上移：{action.Code} 的 {moved} 张从 "
                + $"{Path.Combine(EffectsFolderName, layerName, SequenceActionFolderLayout.FramesFolderName)} 搬到了 "
                + $"{FormatLayerFolderName(layerIndex)}（去掉了多余的两层）。");
        }
    }

    private static void TryRemoveEmptyLegacyFolders(string legacyFramesFolder)
    {
        foreach (var folder in new[]
                 {
                     legacyFramesFolder,
                     Path.GetDirectoryName(legacyFramesFolder)
                 })
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                continue;
            }

            if (Directory.EnumerateFileSystemEntries(folder).Any())
            {
                continue;
            }

            try
            {
                Directory.Delete(folder);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // 删不掉就留着，空目录不碍事。
            }
        }
    }

    /// <summary>层名 → 资产名前缀：<c>Effect</c> → <c>Sk2_Effect</c>（和角色序列同构）。</summary>
    public static string BuildAssetPrefix(string actionCode, string layerName = DefaultLayerName) =>
        $"{actionCode}_{layerName}";

    /// <summary>
    /// 读这一层现在有什么。目录不存在 / 一张图都没有，都返回一个空层（不抛异常）：
    /// "还没画特效"是常态，不是错误。层号默认第 1 层，和以前的行为一模一样。
    /// </summary>
    public SequenceEffectLayer Load(
        CharacterCard character,
        SequenceFrameAction action,
        int multiplier = BasePlateExportPlanner.Multiplier,
        int layerIndex = DefaultLayerIndex,
        int expectedFrameCount = 0)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(action);
        // 旧落点（多套了两层）一次性搬到新落点，别让人已经画好的东西找不着。
        MigrateLegacyLayout(character, action, layerIndex);
        var layerName = BuildLayerName(layerIndex);
        var assetPrefix = BuildAssetPrefix(action.Code, layerName);
        var framesFolder = GetLayerFramesFolderPath(character, action, layerIndex);
        if (!Directory.Exists(framesFolder))
        {
            return SequenceEffectLayer.Empty(layerName, assetPrefix, multiplier);
        }

        var frames = Directory
            .EnumerateFiles(framesFolder, "*.png")
            .Select(path => (Path: path, Ordinal: TryResolveOrdinal(Path.GetFileName(path))))
            .Where(item => item.Ordinal > 0)
            .OrderBy(item => item.Ordinal)
            .Select(item => new SequenceEffectFrame(
                item.Ordinal,
                Path.GetFileName(item.Path),
                item.Path,
                IsEmpty: false))
            .ToArray();
        if (frames.Length == 0)
        {
            return SequenceEffectLayer.Empty(layerName, assetPrefix, multiplier);
        }

        var importedAt = Directory.GetLastWriteTime(framesFolder);
        return new SequenceEffectLayer(layerName, assetPrefix, multiplier, expectedFrameCount, frames, importedAt);
    }

    /// <summary>
    /// 层里的帧 → 「格号 → PNG 路径」，给「导出底板（带特效）」查用。
    ///
    /// 键就是 <see cref="SequenceEffectFrame.Ordinal"/>（= 文件名里最后一段数字，和底板导出的
    /// <c>帧NNNN</c> 同号），所以图层组 <c>帧0003</c> 直接拿 3 去查自己那一格的特效。
    /// 没导入过 / 空层都返回空表（调用方照旧写空层，不是错误）。
    /// </summary>
    public static IReadOnlyDictionary<int, string> BuildFramePathsByOrdinal(SequenceEffectLayer? layer)
    {
        if (layer is null || !layer.HasFrames)
        {
            return new Dictionary<int, string>();
        }

        var paths = new Dictionary<int, string>();
        foreach (var frame in layer.Frames)
        {
            if (frame.IsEmpty || string.IsNullOrWhiteSpace(frame.FilePath))
            {
                continue;
            }

            // 同一格有两张（人工往目录里塞的）时只认先来的那张，别让后面那张偷偷顶掉。
            paths.TryAdd(frame.Ordinal, frame.FilePath);
        }

        return paths;
    }

    /// <summary>
    /// 把画好的特效帧导进来。
    /// <paramref name="expectedFrameCount"/> 由调用方按动作算（总格数 × 倍数）。
    /// 帧号取**文件名里最后一段数字** —— 导出底板那批 PNG 和这套编号同名，画完不用改名。
    /// <paramref name="layerIndex"/> 是导进第几层（1 起；第 2 层落 <c>Effects2\</c>）。
    /// <paramref name="progress"/>：清层 72% → 逐张落盘 75~99%；<paramref name="cancellationToken"/>
    /// 每张之间查一次（清完层才发现不想导，也还来得及按下取消）。
    /// </summary>
    public SequenceEffectImportResult Import(
        CharacterCard character,
        SequenceFrameAction action,
        IReadOnlyList<string> sourceFiles,
        int expectedFrameCount,
        int multiplier = BasePlateExportPlanner.Multiplier,
        int layerIndex = DefaultLayerIndex,
        IProgress<SequenceEffectImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceFiles);
        return ImportResolved(
            character,
            action,
            ResolveSourceOrdinals(sourceFiles),
            expectedFrameCount,
            multiplier,
            layerIndex,
            progress,
            cancellationToken);
    }

    /// <summary>
    /// 按**给定的帧号**逐张收进特效层（第 N 帧落到第 N 格）。
    ///
    /// 这条给"从 PSD 导入"用。帧号由那一侧**按图层组名**算出来（`帧0003` → 第 3 帧），
    /// **不再假设"第几个文件就是第几帧"** —— 组少几个、缺的是哪几帧，落点都还对得上
    /// （晓桀 2026-09-25：「数量都可以不用完全对的上了」）。
    ///
    /// 进度 / 取消和 <see cref="Import"/> 是同一条。
    /// </summary>
    public SequenceEffectImportResult ImportStaged(
        CharacterCard character,
        SequenceFrameAction action,
        IReadOnlyList<SequenceEffectPsdImportService.StagedEffectFrame> stagedFrames,
        int expectedFrameCount,
        int multiplier = BasePlateExportPlanner.Multiplier,
        int layerIndex = DefaultLayerIndex,
        IProgress<SequenceEffectImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stagedFrames);
        return ImportResolved(
            character,
            action,
            stagedFrames.Select(frame => (File: frame.FilePath, Ordinal: frame.Ordinal)),
            expectedFrameCount,
            multiplier,
            layerIndex,
            progress,
            cancellationToken);
    }

    private SequenceEffectImportResult ImportResolved(
        CharacterCard character,
        SequenceFrameAction action,
        IEnumerable<(string File, int Ordinal)> resolvedSources,
        int expectedFrameCount,
        int multiplier,
        int layerIndex,
        IProgress<SequenceEffectImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(action);
        if (expectedFrameCount <= 0)
        {
            throw new InvalidOperationException("这个动作没有帧，算不出特效该有多少张。");
        }

        var layerName = BuildLayerName(layerIndex);
        var framesFolder = GetLayerFramesFolderPath(character, action, layerIndex);
        Report(progress, $"正在清空第 {layerIndex} 层…", 72);
        cancellationToken.ThrowIfCancellationRequested();
        var cleared = ClearLayer(character, action, layerIndex);
        // 清空只删文件，目录本身第一次可能还不存在。
        Directory.CreateDirectory(framesFolder);

        // 落成列表：下面要按序号报进度（`IEnumerable` 数不出总数）。
        var sources = resolvedSources as IReadOnlyList<(string File, int Ordinal)>
            ?? resolvedSources.ToList();
        // 保底 1，免得空批次除零。
        var total = Math.Max(1, sources.Count);
        var imported = 0;
        var ignored = 0;
        for (var index = 0; index < sources.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (file, ordinal) = sources[index];
            if (ordinal > expectedFrameCount)
            {
                ignored++;
                continue;
            }

            if (IsFullyTransparent(file))
            {
                // 画了但整张透明 = 这段没有特效：不落文件，读盘时自然就是空帧。
                continue;
            }

            var targetPath = Path.Combine(framesFolder, BuildFrameFileName(action.Code, layerName, ordinal));
            File.Copy(file, targetPath, overwrite: true);
            imported++;
            Report(
                progress,
                $"正在收进第 {layerIndex} 层 {index + 1}/{sources.Count}…",
                75 + (24.0 * (index + 1) / total));
        }

        var emptyFrames = expectedFrameCount - imported;
        return new SequenceEffectImportResult(imported, emptyFrames, ignored, cleared, framesFolder);
    }

    /// <summary>报一条进度（没接进度条时是空操作）；百分比夹到 0~100。</summary>
    private static void Report(
        IProgress<SequenceEffectImportProgress>? progress,
        string message,
        double percent) =>
        progress?.Report(new SequenceEffectImportProgress(message, Math.Clamp(percent, 0, 100)));

    /// <summary>清空这一层（只删这一层目录里的帧文件；层目录本身留着）。返回删掉几张。</summary>
    public int ClearLayer(
        CharacterCard character,
        SequenceFrameAction action,
        int layerIndex = DefaultLayerIndex)
    {
        var layerName = BuildLayerName(layerIndex);
        var framesFolder = GetLayerFramesFolderPath(character, action, layerIndex);
        if (!Directory.Exists(framesFolder))
        {
            return 0;
        }

        var removed = 0;
        // 一层一个目录（<c>Effects\</c> / <c>Effects2\</c>），但**只删这一层自己的帧**：
        // 目录里可能被人塞了别的层的文件，删 A 不能把 B 的一起删了。
        foreach (var file in Directory.EnumerateFiles(framesFolder, "*.png"))
        {
            if (!IsFrameOfLayer(Path.GetFileName(file), action.Code, layerName))
            {
                continue;
            }

            if (AtomicFileWriter.TryDelete(file))
            {
                removed++;
            }
        }

        return removed;
    }

    /// <summary>
    /// 这个文件名是不是**这一层**的帧。比对的是完整规范名
    /// （<c>&lt;动作&gt;_&lt;层名&gt;_0001.png</c>），不能只比前缀 ——
    /// `Click_EffectB_0001.png` 也以 `Click_Effect` 开头，比前缀会把别的层一起带走。
    /// </summary>
    private static bool IsFrameOfLayer(string fileName, string actionCode, string layerName)
    {
        var ordinal = TryResolveOrdinal(fileName);
        return ordinal > 0 &&
            string.Equals(
                fileName,
                BuildFrameFileName(actionCode, layerName, ordinal),
                StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>规范帧文件名：<c>&lt;角色&gt;_…</c> 由调用方给前缀，这里只负责编号。</summary>
    public static string BuildFrameFileName(string actionCode, string layerName, int ordinal) =>
        $"{BuildAssetPrefix(actionCode, layerName)}_{ordinal:0000}.png";

    /// <summary>
    /// 源文件 → 输出帧号。优先认**文件名里最后一段数字**（导出底板就是
    /// <c>&lt;角色&gt;_&lt;动作&gt;_0001.png</c>，导回来天然对得上）；
    /// 没有数字的文件按名字排序往后顺延，避免整批直接失败。
    /// </summary>
    private static IEnumerable<(string File, int Ordinal)> ResolveSourceOrdinals(IReadOnlyList<string> sourceFiles)
    {
        var used = new HashSet<int>();
        var withoutNumber = new List<string>();
        foreach (var file in sourceFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var ordinal = TryResolveOrdinal(Path.GetFileNameWithoutExtension(file));
            if (ordinal <= 0 || !used.Add(ordinal))
            {
                withoutNumber.Add(file);
                continue;
            }

            yield return (file, ordinal);
        }

        var next = 1;
        foreach (var file in withoutNumber)
        {
            while (!used.Add(next))
            {
                next++;
            }

            yield return (file, next);
        }
    }

    /// <summary>取名字里最后一段数字；取不到返回 0。</summary>
    public static int TryResolveOrdinal(string fileName)
    {
        var match = TrailingNumberPattern.Match(fileName);
        return match.Success && int.TryParse(match.Value, out var ordinal) ? ordinal : 0;
    }

    /// <summary>整张图是不是全透明（完全不透明的地方一个都没有）。</summary>
    public static bool IsFullyTransparent(string path)
    {
        using var bitmap = new Bitmap(path);
        var rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = Math.Abs(data.Stride) * bitmap.Height;
            var buffer = new byte[bytes];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buffer, 0, bytes);
            for (var index = 3; index < bytes; index += 4)
            {
                if (buffer[index] != 0)
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
