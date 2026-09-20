using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 特效层：导入、读盘、清空。
///
/// 磁盘形状（跟着动作目录走，和序列帧、图集缓存同一个父目录）：
/// <code>
/// &lt;角色&gt;/ZDMaterial/&lt;动作&gt;/Effects/&lt;层名&gt;/Frames/&lt;角色&gt;_&lt;动作&gt;_0001.png …
/// </code>
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
    /// <summary>默认层名。以后一个动作要多条特效，就再加 <c>EffectB</c> 这种层名。</summary>
    public const string DefaultLayerName = "Effect";

    private static readonly Regex TrailingNumberPattern = new(@"(\d+)(?!.*\d)", RegexOptions.Compiled);

    public static string GetEffectsRootPath(CharacterCard character, SequenceFrameAction action) =>
        Path.Combine(SequenceActionFolderLayout.GetActionFolderPath(character, action), "Effects");

    public static string GetLayerFolderPath(
        CharacterCard character,
        SequenceFrameAction action,
        string layerName = DefaultLayerName) =>
        Path.Combine(GetEffectsRootPath(character, action), layerName);

    public static string GetLayerFramesFolderPath(
        CharacterCard character,
        SequenceFrameAction action,
        string layerName = DefaultLayerName) =>
        Path.Combine(GetLayerFolderPath(character, action, layerName), SequenceActionFolderLayout.FramesFolderName);

    /// <summary>层名 → 资产名前缀：<c>Effect</c> → <c>Sk2_Effect</c>（和角色序列同构）。</summary>
    public static string BuildAssetPrefix(string actionCode, string layerName = DefaultLayerName) =>
        $"{actionCode}_{layerName}";

    /// <summary>
    /// 读这一层现在有什么。目录不存在 / 一张图都没有，都返回一个空层（不抛异常）：
    /// "还没画特效"是常态，不是错误。
    /// </summary>
    public SequenceEffectLayer Load(
        CharacterCard character,
        SequenceFrameAction action,
        int multiplier = BasePlateExportPlanner.Multiplier,
        string layerName = DefaultLayerName,
        int expectedFrameCount = 0)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(action);
        var assetPrefix = BuildAssetPrefix(action.Code, layerName);
        var framesFolder = GetLayerFramesFolderPath(character, action, layerName);
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
    /// 把画好的特效帧导进来。
    /// <paramref name="expectedFrameCount"/> 由调用方按动作算（总格数 × 倍数）。
    /// </summary>
    public SequenceEffectImportResult Import(
        CharacterCard character,
        SequenceFrameAction action,
        IReadOnlyList<string> sourceFiles,
        int expectedFrameCount,
        int multiplier = BasePlateExportPlanner.Multiplier,
        string layerName = DefaultLayerName)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(sourceFiles);
        if (expectedFrameCount <= 0)
        {
            throw new InvalidOperationException("这个动作没有帧，算不出特效该有多少张。");
        }

        var framesFolder = GetLayerFramesFolderPath(character, action, layerName);
        var cleared = ClearLayer(character, action, layerName);
        // 清空只删文件，目录本身第一次可能还不存在。
        Directory.CreateDirectory(framesFolder);

        var imported = 0;
        var ignored = 0;
        foreach (var (file, ordinal) in ResolveSourceOrdinals(sourceFiles))
        {
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
        }

        var emptyFrames = expectedFrameCount - imported;
        return new SequenceEffectImportResult(imported, emptyFrames, ignored, cleared, framesFolder);
    }

    /// <summary>清空这一层（只删层目录里的帧文件；层目录本身留着）。返回删掉几张。</summary>
    public int ClearLayer(
        CharacterCard character,
        SequenceFrameAction action,
        string layerName = DefaultLayerName)
    {
        var framesFolder = GetLayerFramesFolderPath(character, action, layerName);
        if (!Directory.Exists(framesFolder))
        {
            return 0;
        }

        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(framesFolder))
        {
            if (AtomicFileWriter.TryDelete(file))
            {
                removed++;
            }
        }

        return removed;
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
