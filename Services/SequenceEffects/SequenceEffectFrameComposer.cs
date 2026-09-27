using System;
using System.Collections.Generic;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 把**同一格的几层特效**叠成一张（预乘 RGBA 的 source-over）。
///
/// 为什么要这么一层：特效是**多层叠加播放**的（第 1 层在最下、层号大的盖在上面 ——
/// 晓桀 2026-09-27：「我之前想要的是多层叠加播放」），而预览那边（
/// <c>SequencePreviewEffectPresenter</c>）每一格只有**一张**图。
/// 与其在 XAML 里塞 N 张图互相盖（预览闪烁那档子事就是图层一多就出问题，见
/// <c>Docs/特效层预览闪烁-问题档-2026-09-25.md</c>），不如**先在内存里叠好一张**再交给它 ——
/// 播放路径还是"一格翻一张可见性"，一点没变。
///
/// 输入是 <see cref="SequencePreviewBitmapCache.DecodeToPixels"/> 解出来的字节
/// （`Format32bppPArgb` = **预乘** alpha，所以每一路就是
/// <c>out = src + dst × (1 − srcA)</c> 直接相加，不用再乘一次 alpha）。
///
/// 这里**不碰任何 WinRT 类型**，所以能在后台线程跑，也能直接写回归用例。
/// </summary>
internal static class SequenceEffectFrameComposer
{
    /// <summary>
    /// 把若干层叠成一张（第 0 个在最下）。返回新数组，调用方给的字节不会被改。
    /// 尺寸对不上的层直接跳过（正常情况都是同一块画布）；一层都没剩下就抛
    /// <see cref="ArgumentException"/>。
    /// </summary>
    public static byte[] Compose(int width, int height, IReadOnlyList<byte[]> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "画布尺寸得是正数。");
        }

        var pixelBytes = checked(width * height * 4);
        var usable = new List<byte[]>(layers.Count);
        foreach (var layer in layers)
        {
            if (layer is not null && layer.Length >= pixelBytes)
            {
                usable.Add(layer);
            }
        }

        if (usable.Count == 0)
        {
            throw new ArgumentException("没有可用的图层字节（尺寸和画布对不上？）。", nameof(layers));
        }

        var canvas = new byte[pixelBytes];
        Buffer.BlockCopy(usable[0], 0, canvas, 0, pixelBytes);
        for (var index = 1; index < usable.Count; index++)
        {
            BlendOnto(canvas, usable[index], pixelBytes);
        }

        return canvas;
    }

    /// <summary>
    /// 把一层叠到画布上（画布**就地**改）。
    ///
    /// 预乘 alpha 的好处就在这里：`dst × (1 − srcA)` 不用再乘一次源 alpha，
    /// 而 `src` 自己已经是乘过的值，直接相加即可。`+127` 是四舍五入。
    /// </summary>
    private static void BlendOnto(byte[] canvas, byte[] source, int pixelBytes)
    {
        for (var offset = 0; offset < pixelBytes; offset += 4)
        {
            var sourceAlpha = source[offset + 3];
            if (sourceAlpha == 0)
            {
                // 这一层这个点没画：下面的原样留着。
                continue;
            }

            if (sourceAlpha == 255)
            {
                // 完全不透明：直接盖过去（省掉三次乘法）。
                canvas[offset] = source[offset];
                canvas[offset + 1] = source[offset + 1];
                canvas[offset + 2] = source[offset + 2];
                canvas[offset + 3] = 255;
                continue;
            }

            var inverse = 255 - sourceAlpha;
            canvas[offset] = (byte)Math.Min(
                255, source[offset] + ((canvas[offset] * inverse + 127) / 255));
            canvas[offset + 1] = (byte)Math.Min(
                255, source[offset + 1] + ((canvas[offset + 1] * inverse + 127) / 255));
            canvas[offset + 2] = (byte)Math.Min(
                255, source[offset + 2] + ((canvas[offset + 2] * inverse + 127) / 255));
            canvas[offset + 3] = (byte)Math.Min(
                255, sourceAlpha + ((canvas[offset + 3] * inverse + 127) / 255));
        }
    }
}
