using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CrossingVoidZDTool.Services;

/// <summary>PSD 里的一层：名字 + 它在画布上的矩形 + 自己的 RGBA 像素（紧密排列）。</summary>
internal sealed record PsdLayerBitmap(
    string Name,
    int Left,
    int Top,
    int Width,
    int Height,
    byte[] Rgba)
{
    /// <summary>
    /// 这是一条<b>分组壳层</b>（PSD 里用 <c>lsct</c> 块标记的组开始/结束），不是画出来的图层：
    /// 它没有像素、矩形是 0×0。导入时要跳过它，否则帧序会整体错一位。
    /// </summary>
    public bool IsSectionDivider { get; init; }
}

/// <summary>读出来的 PSD：画布尺寸 + 按**文件顺序**列的图层。</summary>
internal sealed record PsdDocument(int Width, int Height, IReadOnlyList<PsdLayerBitmap> Layers);

/// <summary>
/// 读多图层 PSD 的图层 —— <see cref="PsdWriter"/> 的**读回来那一半**。
///
/// 为什么要自己写：底板导出的 PSD 要拿到 PS / 画世界里画特效、再导回来，
/// 而 <c>System.Drawing</c> 根本不认 PSD，为一个入口引一个第三方解析库也不划算。
/// 用得上的只是 PSD 最基础的那半边结构（8 位 RGB、RAW / RLE 通道、纯像素图层），
/// 和写那一半对称，两百多行写得对。
///
/// **三件必须写对的事**（2026-09-23 拿两份真实 PSD 验过，别改成"更自然"的写法）：
/// <list type="number">
/// <item><b>通道按 ID 取，不能按位置取。</b>我们写出去的层是 <c>R,G,B,A</c>，
/// 而 PS / 画世界重新存过之后会变成 <c>A,R,G,B</c>（实测两份文件正是这两种顺序）。
/// 按位置读会把 alpha 当成 R —— 颜色整个错掉，而且**一声不响**。
/// （Pillow 的 PSD 图层支持就是这么错的，所以它不能拿来当基准。）</item>
/// <item><b>图层记录的顺序就是帧顺序。</b><c>Layers[i]</c> 对应导出底板时的第 <c>i+1</c> 张，
/// 所以读回来按数组下标编号即可，**不看图层名**。
/// （拿未被改动的底板 PSD 和源 PNG 逐像素比过：16/16 完全一致。）</item>
/// <item><b>图层落在画布哪儿由它自己的矩形决定。</b>我们导出时每层都贴左上角、
/// 尺寸就是整张画布，但人在 PS 里画的特效层是**子矩形**（实测 <c>339,201,509,323</c> 这种），
/// 按"层的尺寸"摆放会整体错位。</item>
/// </list>
///
/// 不支持的一律报错、不猜：PSB、16/32 位、非 RGB 模式、ZIP 压缩的通道。
/// 底板这条链路不会产生这几种。
/// </summary>
internal static class PsdReader
{
    private const uint SignaturePsd = 0x38425053; // '8BPS'
    private const ushort ColorModeRgb = 3;
    private const short ChannelAlpha = -1;
    private const int MaxChannelsPerLayer = 64;

    public static PsdDocument Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Parse(File.ReadAllBytes(path));
    }

    private static PsdDocument Parse(byte[] bytes)
    {
        var reader = new SeekReader(bytes);

        // ── 文件头 ────────────────────────────────────────────────────────
        if (reader.ReadUInt32() != SignaturePsd)
        {
            throw new InvalidDataException("这不是一份 PSD 文件。");
        }

        var version = reader.ReadUInt16();
        if (version != 1)
        {
            throw new InvalidDataException($"只认 PSD（版本 1），这份是版本 {version}——PSB 不支持。");
        }

        reader.Skip(6);                          // 保留段
        reader.Skip(2);                          // 文档通道数（拼 RGBA 用不上它）
        var documentHeight = reader.ReadInt32();
        var documentWidth = reader.ReadInt32();
        var depth = reader.ReadUInt16();
        if (depth != 8)
        {
            throw new InvalidDataException($"只认 8 位/通道的 PSD，这份是 {depth} 位。");
        }

        var colorMode = reader.ReadUInt16();
        if (colorMode != ColorModeRgb)
        {
            throw new InvalidDataException($"只认 RGB 模式的 PSD，这份是模式 {colorMode}。");
        }

        // 颜色模式数据段 / 图像资源段：底板这条链路用不到，整段跳过。
        reader.SkipBlock();
        reader.SkipBlock();

        // ── 图层与蒙版信息 ────────────────────────────────────────────────
        // ⚠️ 这两个长度**只能读掉，不能整段跳过**：图层记录和通道数据就在「图层信息」
        // 这一段**里面**，跳过去等于把要读的东西一起跳掉，游标落到段尾之后再读就是垃圾
        // （实测症状：第 1 层的"通道数"读出 61434）。这里只吃掉长度本身。
        _ = reader.ReadUInt32();                 // 「图层与蒙版信息」段总长
        // 「图层信息」子段：**长度字段的 4 字节不算在它自己的长度里** ——
        // 段尾 = 长度字段位置 + 4 + 长度。少算这 4，读完就正好差 -4，
        // 于是每次读自己的底板 PSD 都会报「图层结构没读齐（差 -4 字节）」。
        var layerInfoStart = reader.Position;
        var layerInfoLength = reader.ReadUInt32();
        var layerInfoEnd = layerInfoStart + 4 + (int)layerInfoLength;

        var recordCount = reader.ReadInt16();
        var layerCount = Math.Abs((int)recordCount);
        if (layerCount == 0)
        {
            return new PsdDocument(documentWidth, documentHeight, Array.Empty<PsdLayerBitmap>());
        }

        var rects = new (int Left, int Top, int Width, int Height)[layerCount];
        var names = new string[layerCount];
        var dividers = new bool[layerCount];
        var channelSpans = new List<(short Id, int Length)>[layerCount];

        for (var layer = 0; layer < layerCount; layer++)
        {
            var top = reader.ReadInt32();
            var left = reader.ReadInt32();
            var bottom = reader.ReadInt32();
            var right = reader.ReadInt32();
            rects[layer] = (left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));

            var channelCount = reader.ReadUInt16();
            if (channelCount > MaxChannelsPerLayer)
            {
                throw new InvalidDataException($"第 {layer + 1} 层的通道数不正常（{channelCount}），这份 PSD 的结构不认识。");
            }

            var spans = new List<(short Id, int Length)>(channelCount);
            for (var channel = 0; channel < channelCount; channel++)
            {
                var id = reader.ReadInt16();
                var length = reader.ReadInt32();
                if (length < 0)
                {
                    throw new InvalidDataException($"第 {layer + 1} 层的通道长度是负数，这份 PSD 的结构不认识。");
                }

                spans.Add((id, length));
            }

            channelSpans[layer] = spans;

            reader.Skip(8);                      // 混合签名 + 混合模式
            reader.Skip(4);                      // 不透明度 / 剪贴标志 / 标志位 / 填充
            var extraEnd = reader.ReadUInt32BlockStart();
            reader.SkipBlock();                  // 图层蒙版数据
            reader.SkipBlock();                  // 混合范围
            var nameLength = reader.ReadByte();
            var nameBytes = reader.ReadBytes(nameLength);
            names[layer] = DecodeLayerName(nameBytes);
            // 附加块里只关心一个：`lsct`（分组的开始/结束壳层）。其余（图层样式、缩略图等）
            // 一律跳过，但块是**要一个个走**的 —— 只有这样才能看见 `lsct`。
            dividers[layer] = ScanForSectionDivider(reader, extraEnd);
            reader.Position = extraEnd;
        }

        // ── 通道数据：紧跟在所有图层记录之后，顺序和记录里写的通道顺序一致 ──
        var layers = new List<PsdLayerBitmap>(layerCount);
        for (var layer = 0; layer < layerCount; layer++)
        {
            var (left, top, width, height) = rects[layer];
            var rgba = width > 0 && height > 0 ? new byte[width * height * 4] : Array.Empty<byte>();
            var hasAlpha = false;
            foreach (var (id, length) in channelSpans[layer])
            {
                var dataStart = reader.Position;
                // 长度是记录里给的权威值，用它对齐到下一通道 —— 就算某种写法有额外的
                // 填充，也不会把后面几层一起读歪。
                reader.Position = dataStart + length;
                if (rgba.Length == 0)
                {
                    continue;
                }

                var plane = DecodePlane(bytes, dataStart, length, width, height);
                if (id == ChannelAlpha)
                {
                    hasAlpha = true;
                    CopyPlane(plane, rgba, 3);
                }
                else if (id is >= 0 and <= 2)
                {
                    CopyPlane(plane, rgba, id);
                }
            }

            if (rgba.Length > 0 && !hasAlpha)
            {
                // 没有 alpha 通道的层就是不透明的。
                for (var index = 3; index < rgba.Length; index += 4)
                {
                    rgba[index] = 255;
                }
            }

            layers.Add(new PsdLayerBitmap(names[layer], left, top, width, height, rgba)
            {
                IsSectionDivider = dividers[layer]
            });
        }

        // 收尾自检：所有通道数据读完，游标应该落在「图层信息」段尾（或它的填充里）。
        //
        // 为什么允许几字节余量：这段的长度按规范是"向上取整到偶数"，而 Photoshop 实际
        // 会补齐到 **4 字节**——实测 PS 存过的文件正好多 3 个 0 字节。要求严丝合缝的话，
        // 凡是经 PS 转手过的文件都会被拒（报"差 3 字节"），而它们是完全正常的。
        // 但余量只认**全零**：读歪了留下的可不是整齐的 0。
        var remaining = layerInfoEnd - reader.Position;
        if (remaining < 0 || remaining > 3 || !IsAllZero(bytes, reader.Position, remaining))
        {
            throw new InvalidDataException(
                "PSD 的图层结构没读齐（差 "
                + $"{remaining} 字节），这份文件的结构不认识。");
        }

        return new PsdDocument(documentWidth, documentHeight, layers);
    }

    private static bool IsAllZero(byte[] bytes, int start, int count)
    {
        for (var index = 0; index < count; index++)
        {
            if (bytes[start + index] != 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 图层名的字节 → 字符串。
    ///
    /// PSD 的图层名是 Pascal 串（长度前缀），但两件事要处理：
    /// <list type="bullet">
    /// <item>画世界/PS 会在这个串里**再带一个结尾 0**（实测 <c>0001\0</c>）→ 削掉，
    /// 否则界面里显示的名字会多一个看不见的字符，比较也不相等；</item>
    /// <item>中文名是 **GBK** 字节（实测画世界导出的层叫 <c>背景</c>），用 Latin1 读会变成乱码 ——
    /// 虽然认帧不看名字，但"忽略底部背景层"这条规则和日志里显示的名字都要认得它。</item>
    /// </list>
    /// </summary>
    private static string DecodeLayerName(byte[] nameBytes)
    {
        if (nameBytes.Length == 0)
        {
            return string.Empty;
        }

        var name = PsdLayerNameEncoding.Decode(nameBytes);
        return name.TrimEnd('\0', ' ');
    }

    /// <summary>
    /// 在图层的附加块里找 <c>lsct</c>（分组的开始/结束壳层）。找到即返回真。
    /// 块是「签名(4) + 键(4) + 长度(4) + 数据（奇数长度再补 1 字节）」。
    /// </summary>
    private static bool ScanForSectionDivider(SeekReader reader, int extraEnd)
    {
        var isDivider = false;
        while (reader.Position + 12 <= extraEnd)
        {
            reader.Skip(4);                       // 签名 '8BIM'（有的老文件是 '8B64'，不细究）
            var key = reader.ReadBytes(4);
            var length = (int)reader.ReadUInt32();
            if (length < 0 || reader.Position + length > extraEnd)
            {
                break;
            }

            if (key[0] == 'l' && key[1] == 's' && key[2] == 'c' && key[3] == 't')
            {
                isDivider = true;
            }

            reader.Skip(length + (length % 2));
        }

        return isDivider;
    }

    /// <summary>把一个通道的像素按 4 字节步长铺进 RGBA 缓冲区的指定分量。</summary>
    private static void CopyPlane(byte[] plane, byte[] rgba, int component)
    {
        var pixels = Math.Min(plane.Length, rgba.Length / 4);
        for (var index = 0; index < pixels; index++)
        {
            rgba[index * 4 + component] = plane[index];
        }
    }

    /// <summary>解一条通道：0 = RAW 原样，1 = 逐行 PackBits。</summary>
    private static byte[] DecodePlane(byte[] bytes, int dataStart, int dataLength, int width, int height)
    {
        var expected = width * height;
        if (dataLength <= 2)
        {
            return new byte[expected];
        }

        var reader = new SeekReader(bytes, dataStart);
        var limit = Math.Min(dataStart + dataLength, bytes.Length);
        var compression = reader.ReadUInt16();
        var plane = new byte[expected];
        switch (compression)
        {
            case 0:
                var rawLength = Math.Min(expected, Math.Max(0, limit - reader.Position));
                Array.Copy(bytes, reader.Position, plane, 0, rawLength);
                break;
            case 1:
                var rowLengths = new int[height];
                for (var row = 0; row < height; row++)
                {
                    rowLengths[row] = reader.ReadUInt16();
                }

                for (var row = 0; row < height; row++)
                {
                    var rowStart = reader.Position;
                    var rowEnd = Math.Min(rowStart + rowLengths[row], limit);
                    var decoded = DecodePackBits(bytes, rowStart, rowEnd, width);
                    Array.Copy(decoded, 0, plane, row * width, Math.Min(decoded.Length, width));
                    reader.Position = rowStart + rowLengths[row];
                }

                break;
            default:
                throw new InvalidDataException(
                    $"图层的压缩方式不支持（{compression}）——底板这条链路只产生 RAW 或 RLE。");
        }

        return plane;
    }

    /// <summary>PSD 的逐行 PackBits：0..127 是字面量，-127..-1 是重复，-128 是空操作。</summary>
    private static byte[] DecodePackBits(byte[] bytes, int start, int end, int expected)
    {
        var output = new byte[expected];
        var written = 0;
        var position = start;
        while (position < end && written < expected)
        {
            var header = (sbyte)bytes[position++];
            if (header >= 0)
            {
                var count = Math.Min(header + 1, expected - written);
                for (var index = 0; index < count && position < end; index++)
                {
                    output[written++] = bytes[position++];
                }
            }
            else if (header != -128)
            {
                var count = Math.Min(1 - header, expected - written);
                if (position >= end)
                {
                    break;
                }

                var value = bytes[position++];
                for (var index = 0; index < count; index++)
                {
                    output[written++] = value;
                }
            }
        }

        return output;
    }

    /// <summary>大端读盘的小工具（和写那一半的扩展方法对称）。</summary>
    private sealed class SeekReader(byte[] bytes, int position = 0)
    {
        private readonly byte[] _bytes = bytes;

        public int Position { get; set; } = position;

        public ushort ReadUInt16()
        {
            var value = (ushort)((_bytes[Position] << 8) | _bytes[Position + 1]);
            Position += 2;
            return value;
        }

        public short ReadInt16() => unchecked((short)ReadUInt16());

        public int ReadInt32() => unchecked((int)ReadUInt32());

        public uint ReadUInt32()
        {
            var value = ((uint)_bytes[Position] << 24)
                | ((uint)_bytes[Position + 1] << 16)
                | ((uint)_bytes[Position + 2] << 8)
                | _bytes[Position + 3];
            Position += 4;
            return value;
        }

        public byte ReadByte() => _bytes[Position++];

        public byte[] ReadBytes(int count)
        {
            var result = new byte[count];
            Array.Copy(_bytes, Position, result, 0, count);
            Position += count;
            return result;
        }

        public void Skip(int count) => Position += count;

        /// <summary>跳过「4 字节长度 + 内容」的一段。</summary>
        public void SkipBlock()
        {
            var length = ReadUInt32();
            Position += (int)length;
        }

        /// <summary>读「4 字节长度」，返回内容**结束**的位置（调用方要跳到那儿）。</summary>
        public int ReadUInt32BlockStart()
        {
            var length = ReadUInt32();
            return Position + (int)length;
        }
    }
}
