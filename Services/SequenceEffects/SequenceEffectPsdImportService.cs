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
/// **一帧 = 一个图层组**（2026-09-25 改，晓桀：单图层逼着人把特效合并成一层，没法返工）：
///
/// <list type="bullet">
/// <item>导出时一帧一组（组名 <c>帧0001</c>…），组里先是那层 <c>原本帧</c>（对照底图），
/// 再是一张空的 <c>特效</c> 层（见 <see cref="BasePlateExportService"/>）；</item>
/// <item>人在组里随便加图层画特效 —— 加多少层都行，两帧之间互不影响，随时能返工；</item>
/// <item>读回时**把整组合并成一张**（组内从下往上叠）就是那一帧；</item>
/// <item><b>组里那层 <c>原本帧</c> 会被跳过</b>（<see cref="BasePlateExportPlanner.BasePlateLayerName"/>）——
/// 它只是给人对着画的，不跳过就会把角色烘进特效帧；</item>
/// <item>顶层那些不属于任何组的图层（画世界自己加的 <c>背景</c>）天然被忽略。</item>
/// </list>
///
/// 帧号**按组名认**（`帧0003` → 第 3 帧）：名字是导出时定的、画的时候基本不会动，
/// 所以不用再猜"第几层就是第几帧"。组名认不出来（被人改名了）才退回按顺序排，
/// 并且**只认名字全都能认出来**那一档，半认半猜最容易错位。
///
/// **组数和帧数不用完全对得上**（晓桀 2026-09-25）：少的那几帧算空帧，
/// 多出来的组会被忽略（帧号超出范围）—— 只把读到的东西如实报出来。
/// </summary>
internal sealed class SequenceEffectPsdImportService
{
    /// <summary>读回来的一帧：落盘的 PNG + 它是第几帧（1 起）。</summary>
    internal sealed record StagedEffectFrame(string FilePath, int Ordinal);

    /// <summary>底板导出的那份 PSD 落在哪（和底板 PNG 同一个目录，同一个文件名规则）。</summary>
    public static string ResolveBasePlatePsdPath(BasePlateExportPlan plan) =>
        Path.Combine(plan.OutputDirectory, BasePlateExportPlanner.FormatPsdFileName(plan));

    /// <summary>
    /// 读 PSD 里的每个图层组、合并成整画布 PNG，落到 <paramref name="outputFolder"/>
    /// （= 底板目录下的 <c>Effect/</c>，晓桀要"一眼能看到"）。
    ///
    /// 返回按帧号排好的结果。画布对不上 / 一个图层组都没有，抛
    /// <see cref="InvalidOperationException"/>，消息是直接给用户看的。
    /// </summary>
    public IReadOnlyList<StagedEffectFrame> StageFrames(
        string psdPath,
        int expectedFrameCount,
        int expectedCanvasWidth,
        int expectedCanvasHeight,
        string outputFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(psdPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFolder);
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

        var groups = CollectGroups(document.Layers);
        if (groups.Count == 0)
        {
            throw new InvalidOperationException(
                "这份 PSD 里没有图层组，读不出特效帧。"
                + Environment.NewLine
                + "底板 PSD 现在是「一帧一个图层组」，特效画在组里 —— "
                + "用「导出 ▾ → 导出底板」重出一份，别在旧版（一帧一层）的文件上画。");
        }

        var ordinals = ResolveGroupOrdinals(groups);
        if (groups.Count != expectedFrameCount)
        {
            ToolboxLog.Info(
                $"从底板 PSD 读回特效帧：读到 {groups.Count} 个图层组，这个动作有 {expectedFrameCount} 帧 —— "
                + "缺的那些算空帧，多出来的会被忽略。");
        }

        if (Directory.Exists(outputFolder))
        {
            ClearFolder(outputFolder);
        }

        Directory.CreateDirectory(outputFolder);

        var staged = new List<StagedEffectFrame>(groups.Count);
        var skippedBasePlateLayers = 0;
        for (var index = 0; index < groups.Count; index++)
        {
            var canvas = ComposeGroup(
                groups[index].Layers, document.Width, document.Height, ref skippedBasePlateLayers);
            var ordinal = ordinals[index];
            var path = Path.Combine(
                outputFolder, $"{ordinal.ToString("0000", CultureInfo.InvariantCulture)}.png");
            WritePng(path, canvas, document.Width, document.Height);
            staged.Add(new StagedEffectFrame(path, ordinal));
        }

        if (skippedBasePlateLayers > 0)
        {
            ToolboxLog.Info(
                $"从底板 PSD 读回特效帧：跳过了 {skippedBasePlateLayers} 层「{BasePlateExportPlanner.BasePlateLayerName}」"
                + "（那是给人对着画的对照底图，不算特效内容）。");
        }

        return staged.OrderBy(frame => frame.Ordinal).ToArray();
    }

    /// <summary>
    /// 把图层记录归到各自的组里。
    ///
    /// 记录顺序是从下往上，一个组的形状是
    /// <c>[边界 3] → 组内各层 → [组开始 1/2（组名在这条上）]</c> ——
    /// 和画世界 / Photoshop 导出的写法一致（拿晓桀给的 5 图层组样本对过）。
    /// **组外面的图层**（对照底图、画世界的 背景）直接不进任何组。
    /// </summary>
    private static List<PsdGroup> CollectGroups(IReadOnlyList<PsdLayerBitmap> layers)
    {
        var groups = new List<PsdGroup>();
        PsdGroup? open = null;
        foreach (var layer in layers)
        {
            switch (layer.SectionDividerKind)
            {
                case PsdSectionDividerKinds.GroupEnd:
                    open = new PsdGroup();
                    groups.Add(open);
                    break;
                case PsdSectionDividerKinds.GroupStartOpen:
                case PsdSectionDividerKinds.GroupStartClosed:
                    if (open is not null)
                    {
                        open.Name = layer.Name;
                        open = null;
                    }

                    break;
                default:
                    open?.Layers.Add(layer);
                    break;
            }
        }

        return groups;
    }

    /// <summary>
    /// 每个组的**帧号**：优先全部按组名末尾的数字认（<c>帧0003</c> → 3）。
    /// 只要有任何一个组认不出来、或者两个组撞到同一个号，就**整批**退回按顺序 1..N
    /// —— 半认半猜最容易让帧错位，而且错得很像"本来就该这样"。
    /// </summary>
    private static int[] ResolveGroupOrdinals(IReadOnlyList<PsdGroup> groups)
    {
        var fromName = new int[groups.Count];
        var seen = new HashSet<int>();
        var allResolved = true;
        for (var index = 0; index < groups.Count; index++)
        {
            var ordinal = SequenceEffectService.TryResolveOrdinal(groups[index].Name);
            if (ordinal <= 0 || !seen.Add(ordinal))
            {
                allResolved = false;
                break;
            }

            fromName[index] = ordinal;
        }

        if (allResolved)
        {
            return fromName;
        }

        ToolboxLog.Info(
            "从底板 PSD 读回特效帧：组名认不出帧号（被人改过名？），这一轮按**组的先后**排帧。");
        var positional = new int[groups.Count];
        for (var index = 0; index < groups.Count; index++)
        {
            positional[index] = index + 1;
        }

        return positional;
    }

    /// <summary>
    /// 把一个组里的图层从下往上 source-over 叠成整画布 RGBA。
    /// <b>跳过 <c>原本帧</c> 那层</b> —— 它只是对照底图，合进去就会让特效里多一个角色。
    /// </summary>
    private static byte[] ComposeGroup(
        IReadOnlyList<PsdLayerBitmap> layers,
        int canvasWidth,
        int canvasHeight,
        ref int skippedBasePlateLayers)
    {
        var canvas = new byte[canvasWidth * canvasHeight * 4];
        foreach (var layer in layers)
        {
            // 名字以「原本帧」开头就算对照层（PS 会起「原本帧 副本」这种名字，所以用前缀）。
            if (layer.Name.TrimStart().StartsWith(
                    BasePlateExportPlanner.BasePlateLayerName,
                    StringComparison.Ordinal))
            {
                skippedBasePlateLayers++;
                continue;
            }

            BlendOnto(canvas, layer, canvasWidth, canvasHeight);
        }

        return canvas;
    }

    /// <summary>
    /// 一层按它自己的矩形贴上去（超出的裁掉），**带 alpha 合成**。
    ///
    /// 以前是"整层覆盖"（一帧就一层，覆盖没差别）；现在一个组里可能好几层，
    /// 直接覆盖会把下面那层的像素连同透明区一起抹掉。
    /// </summary>
    private static void BlendOnto(
        byte[] canvas,
        PsdLayerBitmap layer,
        int canvasWidth,
        int canvasHeight)
    {
        if (layer.Width <= 0 || layer.Height <= 0)
        {
            return;
        }

        var startX = Math.Max(0, layer.Left);
        var startY = Math.Max(0, layer.Top);
        var endX = Math.Min(canvasWidth, layer.Left + layer.Width);
        var endY = Math.Min(canvasHeight, layer.Top + layer.Height);
        for (var y = startY; y < endY; y++)
        {
            for (var x = startX; x < endX; x++)
            {
                var source = ((y - layer.Top) * layer.Width + (x - layer.Left)) * 4;
                var sourceAlpha = layer.Rgba[source + 3];
                if (sourceAlpha == 0)
                {
                    continue;
                }

                var target = (y * canvasWidth + x) * 4;
                if (sourceAlpha == 255)
                {
                    canvas[target] = layer.Rgba[source];
                    canvas[target + 1] = layer.Rgba[source + 1];
                    canvas[target + 2] = layer.Rgba[source + 2];
                    canvas[target + 3] = 255;
                    continue;
                }

                var targetAlpha = canvas[target + 3];
                var outAlpha = sourceAlpha + targetAlpha * (255 - sourceAlpha) / 255;
                canvas[target] = BlendChannel(layer.Rgba[source], sourceAlpha, canvas[target], targetAlpha, outAlpha);
                canvas[target + 1] = BlendChannel(layer.Rgba[source + 1], sourceAlpha, canvas[target + 1], targetAlpha, outAlpha);
                canvas[target + 2] = BlendChannel(layer.Rgba[source + 2], sourceAlpha, canvas[target + 2], targetAlpha, outAlpha);
                canvas[target + 3] = (byte)outAlpha;
            }
        }
    }

    /// <summary>source-over 的一个颜色分量（直通 alpha，0..255 整数）。</summary>
    private static byte BlendChannel(
        byte sourceChannel,
        int sourceAlpha,
        byte targetChannel,
        int targetAlpha,
        int outAlpha)
    {
        if (outAlpha <= 0)
        {
            return 0;
        }

        var weighted = sourceChannel * sourceAlpha
            + targetChannel * targetAlpha * (255 - sourceAlpha) / 255;
        return (byte)Math.Clamp((weighted + outAlpha / 2) / outAlpha, 0, 255);
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

    private sealed class PsdGroup
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>组里的图层，数组顺序 = 文件顺序 = **从下往上**。</summary>
        public List<PsdLayerBitmap> Layers { get; } = [];
    }
}
