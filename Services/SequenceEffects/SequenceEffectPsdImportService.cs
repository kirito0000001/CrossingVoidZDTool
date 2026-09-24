using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 把「导出底板」那份多图层 PSD **读回来当特效帧**。
///
/// 流程是：底板 PSD 一帧一图层 → 人在 PS / 画世界里画特效（自带图层随便删，
/// 但**留下的层数必须还是那个数**，一层对一帧）→ 这里按图层顺序拼回整画布尺寸的 PNG。
///
/// 为什么按顺序、不看名字：画的时候会新建图层、名字由 PS 起（实测
/// <c>mikoto_dz1_lightning_05 副本 5</c> 这种），靠名字认帧认不出来；
/// 而导出时图层就是按帧顺序写的，人只要不调层序，位置本身就是帧号。
///
/// **层数不符就停下**（晓桀定的）：多一层少一层都会让所有帧整体错位，
/// 而且错得很像"画的时候就是这样的"，所以宁可先报数、不猜。
/// </summary>
internal sealed class SequenceEffectPsdImportService
{
    /// <summary>拼好的整画布 PNG 先落在这里，再交给 <see cref="SequenceEffectService"/> 归档。</summary>
    public const string StagingFolderName = ".from-psd";

    /// <summary>底板导出的那份 PSD 落在哪（和底板 PNG 同一个目录，同一个文件名规则）。</summary>
    public static string ResolveBasePlatePsdPath(BasePlateExportPlan plan) =>
        Path.Combine(plan.OutputDirectory, BasePlateExportPlanner.FormatPsdFileName(plan));

    public static string GetStagingFolderPath(CharacterCard character, SequenceFrameAction action, string layerName) =>
        Path.Combine(SequenceEffectService.GetLayerFolderPath(character, action, layerName), StagingFolderName);

    /// <summary>
    /// 读 PSD 的每一层、拼成整画布 PNG，按**图层顺序**返回文件路径（第 1 层 = 第 1 帧）。
    ///
    /// 层数或画布对不上就抛 <see cref="InvalidOperationException"/>，消息是直接给用户看的。
    /// </summary>
    public IReadOnlyList<string> StageFrames(
        string psdPath,
        int expectedFrameCount,
        int expectedCanvasWidth,
        int expectedCanvasHeight,
        string stagingFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(psdPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingFolder);
        if (!File.Exists(psdPath))
        {
            throw new InvalidOperationException(
                $"找不到底板 PSD：{psdPath}{Environment.NewLine}先「导出 ▾ → 导出底板」出一份，画完再来。");
        }

        var document = PsdReader.Read(psdPath);
        if (document.Width != expectedCanvasWidth || document.Height != expectedCanvasHeight)
        {
            throw new InvalidOperationException(
                $"这份 PSD 的画布是 {document.Width}×{document.Height}，"
                + $"底板应该是 {expectedCanvasWidth}×{expectedCanvasHeight}——画布尺寸被改过就对不上了。");
        }

        var layers = SelectFrameLayers(document.Layers, expectedFrameCount);
        if (layers.Count != expectedFrameCount)
        {
            throw new InvalidOperationException(
                $"图层数对不上：这份 PSD 有 {layers.Count} 层可以当帧，这个动作应该是 {expectedFrameCount} 层。"
                + Environment.NewLine
                + "底板是按「每格再切成两半」导的，所以层数 = 帧总长（总格数）× 2。"
                + Environment.NewLine
                + "多一层少一层都会让整条特效时序错位，而且错得很像\"本来就该这样\"，所以先停在这里。"
                + Environment.NewLine
                + "对一下：层数要正好，第 1 帧在最下面、一帧一往上叠。");
        }

        if (layers.Count == 0)
        {
            throw new InvalidOperationException("这份 PSD 里一层都没有。");
        }

        if (Directory.Exists(stagingFolder))
        {
            ClearFolder(stagingFolder);
        }

        Directory.CreateDirectory(stagingFolder);

        var staged = new List<string>(layers.Count);
        for (var index = 0; index < layers.Count; index++)
        {
            var layer = layers[index];
            var canvas = ComposeCanvas(layer, document.Width, document.Height);
            var path = Path.Combine(
                stagingFolder,
                $"layer-{(index + 1).ToString("0000", CultureInfo.InvariantCulture)}.png");
            WritePng(path, canvas, document.Width, document.Height);
            staged.Add(path);
        }

        return staged;
    }

    /// <summary>
    /// 从 PSD 的图层里挑出"哪些是帧"。
    ///
    /// 两件必须排除的，都是重新保存时**自己长出来**的、画的人没动过的东西：
    /// <list type="number">
    /// <item><b>分组壳层</b>（<c>lsct</c> 标记的组开始/结束）：没有像素，留着会让帧序整体错一位；</item>
    /// <item><b>最底下那个默认背景层</b>（画世界/PS 保存时自动加的 <c>背景</c> / <c>Background</c>，
    /// 整张画布全不透明）。<b>只在"去掉它层数就正好"时才丢</b>：这样既照顾了往返一次
    /// 必然多一层背景的现实，又不会在层数本来就对不上时替用户猜。</item>
    /// </list>
    /// </summary>
    private static List<PsdLayerBitmap> SelectFrameLayers(
        IReadOnlyList<PsdLayerBitmap> layers,
        int expectedFrameCount)
    {
        var candidates = layers.Where(layer => !layer.IsSectionDivider).ToList();
        if (candidates.Count > expectedFrameCount && candidates.Count - 1 == expectedFrameCount &&
            IsDefaultBackground(candidates[0]))
        {
            ToolboxLog.Info(
                $"从底板 PSD 读特效帧：忽略最底部的默认背景层「{candidates[0].Name}」"
                + $"（它不算一帧；剩下的 {expectedFrameCount} 层对得上）。");
            candidates.RemoveAt(0);
        }

        return candidates;
    }

    /// <summary>是不是画世界 / Photoshop 保存时自动加的那个默认背景层。</summary>
    private static bool IsDefaultBackground(PsdLayerBitmap layer) =>
        layer.Name.Trim() is var name &&
        (name.Equals("背景", StringComparison.Ordinal) ||
         name.Equals("Background", StringComparison.OrdinalIgnoreCase));

    /// <summary>把一层按它自己的矩形贴回整张画布（超出的部分裁掉）。</summary>
    private static byte[] ComposeCanvas(PsdLayerBitmap layer, int canvasWidth, int canvasHeight)
    {
        var canvas = new byte[canvasWidth * canvasHeight * 4];
        if (layer.Width <= 0 || layer.Height <= 0)
        {
            return canvas;
        }

        var startX = Math.Max(0, layer.Left);
        var startY = Math.Max(0, layer.Top);
        var endX = Math.Min(canvasWidth, layer.Left + layer.Width);
        var endY = Math.Min(canvasHeight, layer.Top + layer.Height);
        var columns = endX - startX;
        if (columns <= 0)
        {
            return canvas;
        }

        for (var y = startY; y < endY; y++)
        {
            var sourceOffset = ((y - layer.Top) * layer.Width + (startX - layer.Left)) * 4;
            var targetOffset = (y * canvasWidth + startX) * 4;
            Buffer.BlockCopy(layer.Rgba, sourceOffset, canvas, targetOffset, columns * 4);
        }

        return canvas;
    }

    /// <summary>RGBA 紧密缓冲 → PNG（GDI+ 在内存里是 BGRA，所以要换一次序）。</summary>
    private static void WritePng(string path, byte[] rgba, int width, int height)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[width * 4];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var source = (y * width + x) * 4;
                    row[x * 4] = rgba[source + 2];
                    row[x * 4 + 1] = rgba[source + 1];
                    row[x * 4 + 2] = rgba[source];
                    row[x * 4 + 3] = rgba[source + 3];
                }

                System.Runtime.InteropServices.Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, row.Length);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        bitmap.Save(path, ImageFormat.Png);
    }

    private static void ClearFolder(string folder)
    {
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            AtomicFileWriter.TryDelete(file);
        }
    }
}
