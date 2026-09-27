using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// PSD 里的一层：图层名 + 它自己那张图（尺寸就是这一层的矩形）。
/// <see cref="ImagePath"/> 为空 = 一张**全透明的空层**（组里给画特效留的那张）；
/// <see cref="SolidColor"/> 给了就是一张**纯色不透明的层**（底板 PSD 垫在最下面的「背景」，
/// 免得为了一个底色还先写一张 PNG 出来再读回来）。
/// </summary>
internal sealed record PsdLayerSource(
    string Name,
    string ImagePath,
    (byte Red, byte Green, byte Blue)? SolidColor = null)
{
    /// <summary>一张纯色不透明的层（铺满整张画布）。</summary>
    public static PsdLayerSource Solid(string name, byte red, byte green, byte blue) =>
        new(name, string.Empty, (red, green, blue));
}

/// <summary>
/// PSD 里的一个**图层组**：组名 + 组里的图层（第一个在最下面）。
///
/// 底板 PSD 现在按"一帧一组"写：组里是空的（或只有一张占位空层），
/// 画特效的人可以往里加任意多层、返工随便改；读回时**把整组合并成一张**就是那一帧。
/// </summary>
/// <param name="Hidden">
/// 这一组是不是**关掉可视性**（眼睛灭掉）。底板导出默认只留第 1 组可见、后面的组全关掉 ——
/// 66 组一起亮着叠出来的是一张糊图，画的时候还得手动一个个点灭（晓桀 2026-09-27）。
/// </param>
/// <param name="Collapsed">
/// 这一组是不是**折叠**（面板里收起来）。跟可视性是两件事，但导出时一起给上：
/// 晓桀存回来的那份 PSD 里，被关掉的组同时也都是折叠的，照它写最稳。
/// </param>
internal sealed record PsdLayerGroup(
    string Name,
    IReadOnlyList<PsdLayerSource> Layers,
    bool Hidden = false,
    bool Collapsed = false);

/// <summary>
/// 把若干张 PNG 写成一份**多图层 PSD**，给画世界 / Photoshop 直接导入。
///
/// 为什么自己写：底板导出来的是一张张 PNG，而画世界一张张导入又麻烦、又分不清先后
/// 顺序；PSD 能把「N 个图层、按序、带透明」一次带过去（画世界 Pro 支持导入 PSD）。
/// 这里用到的只是 PSD 最基础的那半边结构，两百多行就能写对，不值得再引一个依赖。
///
/// 写法**照着真实的 Photoshop 文件抄**（拿 `角色ZD.psd` / `赤瞳.psd` 对过）：
/// <list type="bullet">
/// <item>8 位、RGB；文档 **4 通道**（合并图带 alpha），图层每层 4 通道；</item>
/// <item>每层的通道顺序是 <c>R, G, B, alpha</c> —— 和**画世界自己导出的文件**一致
/// （Photoshop 是 <c>alpha, R, G, B</c>，两种都合法，但对着要读它的程序写更稳）；</item>
/// <item>图层记录**从下往上**：<c>layers[0]</c> 在最下面 —— 第 1 帧在底、最后一帧在顶；</item>
/// <item>图层数是**负数**（= 合并图的 alpha 是透明数据），画世界和 Photoshop 都这么写；</item>
/// <item>图层标志位：<c>0x00</c> = 可见、<c>0x02</c> = 这一层被关掉（PSD 的惯例是
/// <c>可见 = !(标志位 &amp; 0x02)</c>，实测晓桀存回来的那份里可见记录正是 <c>0x00</c>、
/// 被点灭的记录是 <c>0x02</c>）；</item>
/// <item>带一个最小图像资源段（分辨率 72dpi）——Photoshop 总会写，省掉它等于逼对方处理一个空段；</item>
/// <item>每层带 40 字节的混合范围 <c>0000ffff × 10</c>，以及 Photoshop 每层都会带的那串
/// 附加块。这里只写画世界自己会写的那个 <c>luni</c>（真正的 Unicode 图层名）；</item>
/// <item>通道数据用逐行 RLE（PackBits）压缩：底板大片透明，压完只有几十 KB。</item>
/// </list>
/// 刻意**不写**的：图层蒙版、图层样式（<c>PlLd</c> / <c>SoLd</c> / <c>fxrp</c>…）——
/// 纯像素图层用不上。
/// </summary>
internal static class PsdWriter
{
    private const int BlendSignature = 0x3842494D; // '8BIM'
    private const int BlendModeNormal = 0x6E6F726D; // 'norm'
    private const uint SignaturePsd = 0x38425053;   // '8BPS'

    // 分组壳层的种类：读那一半（PsdReader）和这里共用同一份数字。
    private const int GroupStartKind = PsdSectionDividerKinds.GroupStartOpen;
    private const int GroupStartCollapsedKind = PsdSectionDividerKinds.GroupStartClosed;
    private const int GroupEndKind = PsdSectionDividerKinds.GroupEnd;

    /// <summary>标志位第 1 位 = 这一层（或这一组）被关掉了。可见时写 <c>0x00</c>。</summary>
    private const byte HiddenFlags = 0x02;

    /// <summary>
    /// 写一份 PSD。图层顺序 = 传进来的顺序（第一个在最下面）。
    /// 返回写出的字节数。
    /// </summary>
    public static long Write(string path, int canvasWidth, int canvasHeight, IReadOnlyList<PsdLayerSource> layers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(layers);
        if (layers.Count == 0)
        {
            throw new InvalidOperationException("没有图层，写不出 PSD。");
        }

        return WriteCore(
            path,
            canvasWidth,
            canvasHeight,
            layers.Select(PsdLayer.Read).ToArray(),
            PsdLayer.ReadComposite(layers[0], canvasWidth, canvasHeight));
    }

    /// <summary>
    /// 按**图层组**写一份 PSD：每个组 = 一帧。
    ///
    /// 组的记录顺序（从下往上）照画世界 / Photoshop 的写法：
    /// <c>[边界标记 3] → 组内各图层 → [组开始 1/2（组名在这个记录上）]</c>。
    /// 读回那一侧（`SequenceEffectPsdImportService`）就靠这个把图层归到组里，
    /// 再把**整组合并**成一张 —— 所以组里加多少层都不影响帧数，
    /// 画的人不用再"把特效合并成一层"才能用，也就还能返工。
    /// </summary>
    /// <param name="onProgress">
    /// 进度回调：<c>(已完成单位数, 总单位数)</c>。单位 = 编组数 + 图层记录条数
    /// （记录条数动手之前就能算出来，所以一开始分母就是对的）。
    /// **这一个方法是整次导出最慢的一段**（每组都要解码一张 PNG、每条记录都要逐行 RLE），
    /// 界面上的进度条靠它动起来 —— 以前一条都不报，界面上就是"卡住不动"。
    /// 这里用裸回调而不是 <c>IProgress</c>：进度文案属于导出那一侧，写盘这层只管报数。
    /// </param>
    /// <param name="cancellationToken">取消令牌：每编完一条图层记录查一次。</param>
    public static long WriteGrouped(
        string path,
        int canvasWidth,
        int canvasHeight,
        IReadOnlyList<PsdLayerSource> topLevelLayers,
        IReadOnlyList<PsdLayerGroup> groups,
        Action<int, int>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(topLevelLayers);
        ArgumentNullException.ThrowIfNull(groups);
        if (groups.Count == 0)
        {
            throw new InvalidOperationException("没有图层组，写不出 PSD。");
        }

        // 进度分母：一个组算一个单位（解码组里那些 PNG），一条图层记录再算一个
        // （RLE 那一遍在 WriteCore 里，长度要写在通道数据之前，躲不掉）。
        // 分母不动像素就能算出来，所以进度条一出现就是对的。
        var recordCount = topLevelLayers.Count + groups.Sum(group => group.Layers.Count + 2);
        var totalUnits = groups.Count + recordCount;
        var completedUnits = 0;

        // 记录从下往上排：**顶层图层在最下面**（对照底图就是这些），
        // 然后每个组摊成 [边界 → 组内各层 → 组名]，组与组之间保持传进来的先后。
        var records = new List<PsdLayer>();
        foreach (var layer in topLevelLayers)
        {
            records.Add(ReadLayer(layer, canvasWidth, canvasHeight));
        }

        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentException.ThrowIfNullOrWhiteSpace(group.Name);
            records.Add(PsdLayer.Divider(GroupEndKind, "</Layer group>"));
            foreach (var layer in group.Layers)
            {
                records.Add(ReadLayer(layer, canvasWidth, canvasHeight));
            }

            // 组名写在**组开始**那条记录上，组关没关也记在它身上（画世界就是这么存的）。
            records.Add(PsdLayer.Divider(
                group.Collapsed ? GroupStartCollapsedKind : GroupStartKind,
                group.Name,
                hidden: group.Hidden));

            completedUnits++;
            onProgress?.Invoke(completedUnits, totalUnits);
        }

        // 合并图（忽略图层时看到的那张）：先铺**纯色背景**（底板 PSD 垫在最下面的「背景」），
        // 再把画面图层 source-over 上去 —— 只拿纯色层当合并图，缩略图会是一整张纯灰；
        // 只拿角色那张，又少了底色。
        // 没有纯色层时保持老规矩：最底下那张顶层图层，没有就退到第一组的第一层。
        var underlaySource = topLevelLayers.FirstOrDefault(layer => layer.SolidColor is not null);
        var overlaySource = topLevelLayers.FirstOrDefault(layer => layer.SolidColor is null) ??
            (groups[0].Layers.Count > 0
                ? groups[0].Layers[0]
                : throw new InvalidOperationException("既没有顶层图层、第一个组也是空的，写不出合并图。"));
        var composite = underlaySource is null
            ? PsdLayer.ReadComposite(overlaySource, canvasWidth, canvasHeight)
            : PsdLayer.ReadComposite(underlaySource, overlaySource, canvasWidth, canvasHeight);

        // 剩下那些单位（逐条记录的 RLE）在 WriteCore 里消耗，把计数接着报上去，
        // 进度条才会一路走到最后一条记录，而不是提前停在 100%。
        var encodedRecords = 0;
        return WriteCore(
            path,
            canvasWidth,
            canvasHeight,
            records,
            composite,
            onRecordEncoded: () =>
            {
                encodedRecords++;
                onProgress?.Invoke(completedUnits + encodedRecords, totalUnits);
            },
            cancellationToken: cancellationToken);
    }

    /// <summary>一层来源 → 编码后的一层；纯色层铺满画布，空路径就是一张全透明的空层。</summary>
    private static PsdLayer ReadLayer(PsdLayerSource source, int canvasWidth, int canvasHeight) =>
        source.SolidColor is { } color
            ? PsdLayer.Solid(source.Name, canvasWidth, canvasHeight, color)
            : string.IsNullOrWhiteSpace(source.ImagePath)
                ? PsdLayer.Empty(source.Name, canvasWidth, canvasHeight)
                : PsdLayer.Read(source);

    private static long WriteCore(
        string path,
        int canvasWidth,
        int canvasHeight,
        IReadOnlyList<PsdLayer> encoded,
        PsdLayer composite,
        Action? onRecordEncoded = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(canvasWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(canvasHeight, 1);

        // 先把每层的四个通道编成 RLE。图层记录里的「通道数据长度」必须写在通道数据之前，
        // 所以这一遍躲不掉；编完每层就只留下压缩后的字节（几 KB），原始像素当场丢掉。
        // 合并图（忽略图层时看到的那张）由调用方按画布尺寸铺好 —— 这里只负责写下去。

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.WriteUInt32(SignaturePsd);
            writer.WriteUInt16(1);                 // 版本 1 = PSD
            writer.Write(new byte[6]);             // 保留
            writer.WriteUInt16(4);                 // 文档通道数：RGB + alpha
            writer.WriteUInt32((uint)canvasHeight);
            writer.WriteUInt32((uint)canvasWidth);
            writer.WriteUInt16(8);                 // 每通道 8 位
            writer.WriteUInt16(3);                 // RGB
            writer.WriteUInt32(0);                 // 颜色模式数据
            // 图像资源整段照搬画世界自己导出的那一份（见 PsdTemplateResources 的说明）。
            writer.Write(PsdTemplateResources.Bytes);

            var layerInfo = BuildLayerInfo(encoded, onRecordEncoded, cancellationToken);
            writer.WriteUInt32((uint)(layerInfo.Length + 4)); // 「图层与蒙版信息」段总长（含末尾那段空的全局蒙版）
            writer.Write(layerInfo);
            writer.WriteUInt32(0);                 // 全局图层蒙版信息：没有

            writer.WriteUInt16(1);                 // 图像数据：RLE
            // 合并图按 R, G, B, A 写（通道数取自文档头）。**注意这里的 RLE 布局和图层通道不一样**：
            // 行长度表要把「高度 × 通道数」条**集中写在前面**，然后才是各通道的数据；
            // 写成「每通道 表+数据」（图层通道那种）读的人就会一步步读歪 —— 实测画世界正是
            // 在这里把我的像素数据当成了"行长度"，报 NegativeArraySizeException。
            writer.WriteMergedPlanes(composite.FileOrderPlanes);
        }

        var bytes = stream.ToArray();
        AtomicFileWriter.WriteAllBytes(path, bytes);
        return bytes.Length;
    }

    /// <summary>
    /// 图层记录 + 各层通道数据，合成 PSD 的「图层信息」段。
    /// 通道数据这一遍是逐行 RLE，整次导出里最慢的一段，所以每条记录编完都报一次进度。
    /// </summary>
    private static byte[] BuildLayerInfo(
        IReadOnlyList<PsdLayer> layers,
        Action? onRecordEncoded = null,
        CancellationToken cancellationToken = default)
    {
        using var channelStream = new MemoryStream();
        using var channelWriter = new BinaryWriter(channelStream, Encoding.UTF8, leaveOpen: true);
        foreach (var layer in layers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 通道数据必须**和图层记录里的通道 ID 同序**（都是 R, G, B, alpha）。
            // 顺序对不上、长度表按另一套顺序给，读的人就会一位一位读歪，
            // 最后在 RLE 数据里读到一个"长度"（画世界报的就是这种
            // NegativeArraySizeException；psd-tools 会报 "33024 is not a valid Compression"）。
            foreach (var plane in layer.FileOrderPlanes)
            {
                channelWriter.WriteUInt16(1); // 压缩方式：RLE
                channelWriter.WritePlane(plane);
            }

            onRecordEncoded?.Invoke();
        }

        using var recordStream = new MemoryStream();
        using (var recordWriter = new BinaryWriter(recordStream, Encoding.UTF8, leaveOpen: true))
        {
            // 负数 = 合并图的 alpha 是透明数据（画世界和 Photoshop 都这么写）。
            recordWriter.WriteInt16(unchecked((short)-layers.Count));
            foreach (var layer in layers)
            {
                WriteLayerRecord(recordWriter, layer);
            }
        }

        var recordBytes = recordStream.ToArray();
        var channelBytes = channelStream.ToArray();
        using var info = new MemoryStream();
        using var infoWriter = new BinaryWriter(info, Encoding.UTF8, leaveOpen: true);
        infoWriter.WriteUInt32((uint)(recordBytes.Length + channelBytes.Length));
        infoWriter.Write(recordBytes);
        infoWriter.Write(channelBytes);
        return info.ToArray();
    }

    private static void WriteLayerRecord(BinaryWriter writer, PsdLayer layer)
    {
        writer.WriteInt32(0);                    // top：贴画布左上角
        writer.WriteInt32(0);                    // left
        writer.WriteInt32(layer.Height);         // bottom
        writer.WriteInt32(layer.Width);          // right
        // 通道数：**壳层也写 4**（0×0 矩形，所以每个通道只有 2 字节的 RLE 头）。
        writer.WriteUInt16(4);
        // 通道 ID：0/1/2 是 R/G/B，-1 是 alpha；顺序照画世界导出的文件（RGB 在前）。
        var planes = layer.FileOrderPlanes;
        for (var index = 0; index < planes.Count; index++)
        {
            writer.WriteInt16((short)(index == 3 ? -1 : index));
            writer.WriteUInt32((uint)ChannelDataLength(planes[index]));
        }

        writer.WriteInt32(BlendSignature);
        writer.WriteInt32(BlendModeNormal);
        writer.Write((byte)255);                 // 不透明度
        writer.Write((byte)0);                   // 不当作剪贴层
        // 标志位：0x00 = 可见、不锁透明；0x02 = 这一层（或这一组）被关掉。
        writer.Write(layer.Hidden ? HiddenFlags : (byte)0x00);
        writer.Write((byte)0);                   // 填充

        // 附加数据 = 图层蒙版(空) + 混合范围(40 字节) + 图层名（Pascal 串，补齐到 4 字节）+ 附加块。
        // 名字**带一个结尾 0**（画世界导出的文件就是这样）；中文名按 GBK 写
        // —— 按 ASCII 写会变成 `??`，信息直接丢掉（读回来也认不出"背景"这种层）。
        var name = PsdLayerNameEncoding.Encode(layer.Name + "\0");
        var nameFieldLength = (name.Length + 1 + 3) / 4 * 4;
        const int rangesLength = 40;
        var blocks = BuildLayerBlocks(layer.Name, layer.SectionDividerKind);
        writer.WriteUInt32((uint)(8 + rangesLength + nameFieldLength + blocks.Length));
        writer.WriteUInt32(0);                   // 没有图层蒙版
        writer.WriteUInt32(rangesLength);
        for (var index = 0; index < rangesLength / 4; index++)
        {
            writer.WriteUInt16(0);
            writer.WriteUInt16(0xFFFF);
        }

        writer.Write((byte)name.Length);
        writer.Write(name);
        for (var index = name.Length + 1; index < nameFieldLength; index++)
        {
            writer.Write((byte)0);
        }

        writer.Write(blocks);
    }

    /// <summary>
    /// 图层记录尾巴上的附加块。这里只写**画世界自己导出时会写的那一个** <c>luni</c>
    /// （4 字节字符数 + UTF-16BE 名字 + 结尾 0）。
    ///
    /// Photoshop 还会写 <c>lnsr</c>/<c>lyid</c>/<c>clbl</c>/<c>infx</c>/<c>knko</c>/<c>lspf</c>/
    /// <c>lclr</c>/<c>shmd</c> 那一串，纯像素图层用不上；而画世界的解析器只要这一个就够
    /// （它自己导出的文件就是这样）。
    /// </summary>
    private static byte[] BuildLayerBlocks(string name, int sectionDividerKind = 0)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        var unicode = Encoding.BigEndianUnicode.GetBytes(name);
        writer.WriteInt32(BlendSignature);
        WriteTag(writer, "luni");
        writer.WriteUInt32((uint)(4 + unicode.Length + 2));
        writer.WriteUInt32((uint)name.Length);
        writer.Write(unicode);
        writer.WriteUInt16(0);

        // 分组壳层再加一个 `lsct`：数据**只有 4 字节**（种类），没有混合模式/子类型那两段。
        //
        // 2026-09-27 实测：一开始照规范写 12 字节（种类 + 混合模式 + 子类型），
        // 画世界导入报「图层读取错误 -53」；它自己导出的组样本里这个块长度就是 **4**。
        // 块顺序也照它来：`luni` 在前、`lsct` 在后（我原来写反了）。
        if (sectionDividerKind != 0)
        {
            writer.WriteInt32(BlendSignature);
            WriteTag(writer, "lsct");
            writer.WriteUInt32(4);
            writer.WriteInt32(sectionDividerKind);
        }

        return stream.ToArray();
    }

    /// <summary>四字符的块键，写成 ASCII 字节（不是长度前缀的串）。</summary>
    private static void WriteTag(BinaryWriter writer, string key) =>
        writer.Write(Encoding.ASCII.GetBytes(key));

    /// <summary>一条通道写进文件的字节数：2 字节压缩标志 + 每行 2 字节的长度表 + 各行数据。</summary>
    private static int ChannelDataLength(IReadOnlyList<byte[]> rows) =>
        2 + rows.Count * 2 + rows.Sum(row => row.Length);

    /// <summary>一层编码完的样子：名字、自己的矩形、四个通道（alpha / R / G / B）的逐行 RLE。</summary>
    private sealed class PsdLayer
    {
        private PsdLayer(
            string name,
            int width,
            int height,
            List<byte[]>[] planes,
            int sectionDividerKind = 0,
            bool hidden = false)
        {
            Name = name;
            Width = width;
            Height = height;
            Planes = planes;
            SectionDividerKind = sectionDividerKind;
            Hidden = hidden;
        }

        public string Name { get; }

        public int Width { get; }

        public int Height { get; }

        /// <summary>0 = 正常图层；1/2 = 组开始（组名在这个记录上）；3 = 组的边界标记。</summary>
        public int SectionDividerKind { get; }

        /// <summary>
        /// 这一条记录是不是**关掉可视性**（标志位写 <c>0x02</c>）。
        /// 组的可视性记在「组开始」那条记录上 —— 也就是组名那条（2026-09-27 实测：
        /// 晓桀在画世界里把后面的组一个个点灭、存回来，被点灭的就是组名那条记录）。
        /// </summary>
        public bool Hidden { get; }

        public bool IsSectionDivider => SectionDividerKind != 0;

        /// <summary>[0]=alpha [1]=R [2]=G [3]=B，每项是逐行的 RLE 数据。</summary>
        public IReadOnlyList<byte[]>[] Planes { get; }

        /// <summary>
        /// 写进文件时的通道顺序（PSD 里是 <c>R, G, B, alpha</c>）。
        /// **通道 ID 和通道数据必须按同一个顺序写** —— 分成两套顺序就是"读的人会读歪"的根源。
        /// </summary>
        public IReadOnlyList<IReadOnlyList<byte[]>> FileOrderPlanes =>
            [Planes[1], Planes[2], Planes[3], Planes[0]];

        public static PsdLayer Read(PsdLayerSource source) =>
            ReadCore(source.Name, new Bitmap(source.ImagePath));

        /// <summary>一张**全透明的空层**（组里给画特效留的那张）。</summary>
        public static PsdLayer Empty(string name, int width, int height) =>
            FromPixels(name, new byte[width * height * 4], width, height);

        /// <summary>
        /// 一张**纯色不透明**的层（底板 PSD 垫在最下面的「背景」）。
        /// 四个通道各自都是同一个值，RLE 一压只有几 KB。
        /// </summary>
        public static PsdLayer Solid(string name, int width, int height, (byte Red, byte Green, byte Blue) color) =>
            FromPixels(name, CreateSolidPixels(color, width, height), width, height);

        /// <summary>
        /// 一条**分组壳层**记录：没有通道数据、矩形 0×0，靠附加块里的 <c>lsct</c>
        /// 表明自己是组的开始还是边界。
        ///
        /// ⚠️ **照样要给 4 个空通道**（每个 0 行 → 数据是 2 字节的 RLE 头）。
        /// 2026-09-27 实测踩到：一开始写成 0 个通道，画世界导入报「图层读取错误 -53」——
        /// 它导出的组样本里每个壳层也是 4 个通道（0×0 矩形，所以通道数据只有 2 字节）。
        /// </summary>
        public static PsdLayer Divider(int kind, string name, bool hidden = false) =>
            new(name, 0, 0, [[], [], [], []], kind, hidden);

        /// <summary>合并图那四条通道：这一层按画布尺寸铺好（画布比它大就补透明 / 铺满纯色）。</summary>
        public static PsdLayer ReadComposite(PsdLayerSource source, int canvasWidth, int canvasHeight) =>
            source.SolidColor is { } color
                ? FromPixels(
                    string.Empty,
                    CreateSolidPixels(color, canvasWidth, canvasHeight),
                    canvasWidth,
                    canvasHeight)
                : string.IsNullOrWhiteSpace(source.ImagePath)
                    ? FromPixels(
                        string.Empty,
                        new byte[canvasWidth * canvasHeight * 4],
                        canvasWidth,
                        canvasHeight)
                    : ReadCore(string.Empty, new Bitmap(source.ImagePath), canvasWidth, canvasHeight);

        /// <summary>
        /// 合并图（图层全关掉时看到的那张）：先铺**纯色底**，再把画面图层 <c>source-over</c> 叠上去。
        ///
        /// 底板 PSD 里就是「#6B6B6B 的背景 + 第 1 帧的对照底图」：只拿纯色层当合并图，
        /// 缩略图会是一整张纯灰；只拿角色那张，又少了底色。
        /// </summary>
        public static PsdLayer ReadComposite(
            PsdLayerSource underlaySource,
            PsdLayerSource overlaySource,
            int canvasWidth,
            int canvasHeight)
        {
            var pixels = underlaySource.SolidColor is { } color
                ? CreateSolidPixels(color, canvasWidth, canvasHeight)
                : new byte[canvasWidth * canvasHeight * 4];
            using var bitmap = new Bitmap(overlaySource.ImagePath);
            BlitOver(pixels, canvasWidth, canvasHeight, ReadRgba(bitmap), bitmap.Width, bitmap.Height);
            return FromPixels(string.Empty, pixels, canvasWidth, canvasHeight);
        }

        /// <summary>画布大小的纯色像素（不透明）：R, G, B, 255 一路排下去。</summary>
        private static byte[] CreateSolidPixels((byte Red, byte Green, byte Blue) color, int width, int height)
        {
            var pixels = new byte[width * height * 4];
            for (var index = 0; index < pixels.Length; index += 4)
            {
                pixels[index] = color.Red;
                pixels[index + 1] = color.Green;
                pixels[index + 2] = color.Blue;
                pixels[index + 3] = 255;
            }

            return pixels;
        }

        /// <summary>紧密 RGBA 像素 → 一层：通道顺序和 <c>Planes</c> 一致（alpha / R / G / B）。</summary>
        private static PsdLayer FromPixels(string name, byte[] rgba, int width, int height) =>
            new(
                name,
                width,
                height,
                [
                    EncodePlane(rgba, channelOffset: 3, width, height, pixelStride: 4),
                    EncodePlane(rgba, channelOffset: 0, width, height, pixelStride: 4),
                    EncodePlane(rgba, channelOffset: 1, width, height, pixelStride: 4),
                    EncodePlane(rgba, channelOffset: 2, width, height, pixelStride: 4)
                ]);

        /// <summary>
        /// 把一张紧密 RGBA 图 <c>source-over</c> 叠到画布左上角（逐像素 alpha 混合）：
        /// 底下已经铺了底色，角色 PNG 的透明像素得让底色透出来，不能直接覆盖成透明。
        /// </summary>
        private static void BlitOver(
            byte[] canvas,
            int canvasWidth,
            int canvasHeight,
            byte[] overlay,
            int width,
            int height)
        {
            var rows = Math.Min(height, canvasHeight);
            var columns = Math.Min(width, canvasWidth);
            for (var y = 0; y < rows; y++)
            {
                for (var x = 0; x < columns; x++)
                {
                    var source = (y * width + x) * 4;
                    var alpha = overlay[source + 3];
                    if (alpha == 0)
                    {
                        continue;
                    }

                    var target = (y * canvasWidth + x) * 4;
                    if (alpha == 255)
                    {
                        canvas[target] = overlay[source];
                        canvas[target + 1] = overlay[source + 1];
                        canvas[target + 2] = overlay[source + 2];
                    }
                    else
                    {
                        canvas[target] = BlendOver(overlay[source], canvas[target], alpha);
                        canvas[target + 1] = BlendOver(overlay[source + 1], canvas[target + 1], alpha);
                        canvas[target + 2] = BlendOver(overlay[source + 2], canvas[target + 2], alpha);
                    }

                    canvas[target + 3] = 255; // 底下是纯色不透明，混合完还是不透明
                }
            }
        }

        /// <summary>source-over 的单通道混合：<c>src×alpha + dst×(255-alpha)</c>。</summary>
        private static byte BlendOver(byte source, byte destination, byte alpha) =>
            (byte)((source * alpha + destination * (255 - alpha) + 127) / 255);

        private static PsdLayer ReadCore(
            string name,
            Bitmap bitmap,
            int canvasWidth = 0,
            int canvasHeight = 0)
        {
            using (bitmap)
            {
                var pixels = ReadRgba(bitmap);
                var width = bitmap.Width;
                var height = bitmap.Height;
                if (canvasWidth > 0 && canvasHeight > 0 && (width != canvasWidth || height != canvasHeight))
                {
                    pixels = BlitToCanvas(pixels, width, height, canvasWidth, canvasHeight);
                    width = canvasWidth;
                    height = canvasHeight;
                }

                // 图层通道：读出来的像素是紧密排列的 RGBA，所以 alpha=3、R=0、G=1、B=2。
                return FromPixels(name, pixels, width, height);
            }
        }

        /// <summary>取 RGBA 平面：统一转成 32bppArgb 再按 R,G,B,A 紧密排开。</summary>
        private static byte[] ReadRgba(Bitmap bitmap)
        {
            var normalized = bitmap.PixelFormat == PixelFormat.Format32bppArgb
                ? null
                : new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb);
            var source = normalized ?? bitmap;
            try
            {
                if (normalized is not null)
                {
                    using var graphics = Graphics.FromImage(normalized);
                    graphics.DrawImageUnscaled(bitmap, 0, 0);
                }

                var data = source.LockBits(
                    new Rectangle(0, 0, source.Width, source.Height),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format32bppArgb);
                try
                {
                    var rgba = new byte[source.Width * source.Height * 4];
                    var row = new byte[source.Width * 4];
                    for (var y = 0; y < source.Height; y++)
                    {
                        System.Runtime.InteropServices.Marshal.Copy(
                            data.Scan0 + y * data.Stride, row, 0, row.Length);
                        for (var x = 0; x < source.Width; x++)
                        {
                            var target = (y * source.Width + x) * 4;
                            rgba[target] = row[x * 4 + 2];     // R
                            rgba[target + 1] = row[x * 4 + 1]; // G
                            rgba[target + 2] = row[x * 4];     // B
                            rgba[target + 3] = row[x * 4 + 3]; // A
                        }
                    }

                    return rgba;
                }
                finally
                {
                    source.UnlockBits(data);
                }
            }
            finally
            {
                normalized?.Dispose();
            }
        }

        private static byte[] BlitToCanvas(byte[] rgba, int width, int height, int canvasWidth, int canvasHeight)
        {
            var canvas = new byte[canvasWidth * canvasHeight * 4];
            var rows = Math.Min(height, canvasHeight);
            var columns = Math.Min(width, canvasWidth);
            for (var y = 0; y < rows; y++)
            {
                Buffer.BlockCopy(rgba, y * width * 4, canvas, y * canvasWidth * 4, columns * 4);
            }

            return canvas;
        }

        /// <summary>
        /// 一个通道逐行的 PackBits（PSD 的 RLE）：同一行里连续 3 个以上相同值才用「重复」编码，
        /// 其余按字面量写出去，一段字面量最多 128 字节。行与行之间不跨边界。
        /// </summary>
        private static List<byte[]> EncodePlane(
            byte[] pixels,
            int channelOffset,
            int width,
            int height,
            int pixelStride)
        {
            var rows = new List<byte[]>(height);
            for (var y = 0; y < height; y++)
            {
                var rowStart = y * width * pixelStride;
                var output = new List<byte>(width / 8 + 8);
                var index = 0;
                while (index < width)
                {
                    var run = 1;
                    while (run < 128 && index + run < width &&
                           pixels[rowStart + (index + run) * pixelStride + channelOffset] ==
                           pixels[rowStart + index * pixelStride + channelOffset])
                    {
                        run++;
                    }

                    if (run >= 3)
                    {
                        output.Add((byte)(257 - run));
                        output.Add(pixels[rowStart + index * pixelStride + channelOffset]);
                        index += run;
                        continue;
                    }

                    var literalStart = index;
                    var literalLength = 0;
                    while (index < width && literalLength < 128)
                    {
                        var peek = 1;
                        while (peek < 3 && index + peek < width &&
                               pixels[rowStart + (index + peek) * pixelStride + channelOffset] ==
                               pixels[rowStart + index * pixelStride + channelOffset])
                        {
                            peek++;
                        }

                        if (peek >= 3)
                        {
                            break;
                        }

                        index++;
                        literalLength++;
                    }

                    output.Add((byte)(literalLength - 1));
                    for (var step = 0; step < literalLength; step++)
                    {
                        output.Add(pixels[rowStart + (literalStart + step) * pixelStride + channelOffset]);
                    }
                }

                rows.Add(output.ToArray());
            }

            return rows;
        }
    }
}

/// <summary>大端写盘的小工具：PSD 里每个整数都是大端，而 <see cref="BinaryWriter"/> 只会小端。</summary>
internal static class PsdBinaryWriterExtensions
{
    public static void WriteUInt16(this BinaryWriter writer, ushort value) =>
        writer.Write(new[] { (byte)(value >> 8), (byte)value });

    public static void WriteInt16(this BinaryWriter writer, short value) =>
        writer.WriteUInt16(unchecked((ushort)value));

    public static void WriteUInt32(this BinaryWriter writer, uint value) =>
        writer.Write(new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value });

    public static void WriteInt32(this BinaryWriter writer, int value) =>
        writer.WriteUInt32(unchecked((uint)value));

    /// <summary>写一整条 RLE 通道：先是每行的 2 字节长度表，再是各行的 RLE 数据。</summary>
    public static void WritePlane(this BinaryWriter writer, IReadOnlyList<byte[]> rows)
    {
        foreach (var row in rows)
        {
            writer.WriteUInt16((ushort)row.Length);
        }

        foreach (var row in rows)
        {
            writer.Write(row);
        }
    }

    /// <summary>
    /// 写「合并图」：**所有通道的行长度表集中写在前面**，然后才是各通道的行数据。
    ///
    /// 这一点和图层通道相反（图层是每个通道各自 表+数据），而且不是可选的口味问题：
    /// 标准解码器（psd_tools / Adobe 口径）读合并图时，是按
    /// <c>行数 = 高度 × 通道数</c> 先把整张表读完的 —— 表和数据交错写，读的人就会
    /// 一步步读歪，最终把像素数据当成长度。
    /// </summary>
    public static void WriteMergedPlanes(this BinaryWriter writer, IReadOnlyList<IReadOnlyList<byte[]>> planes)
    {
        foreach (var plane in planes)
        {
            foreach (var row in plane)
            {
                writer.WriteUInt16((ushort)row.Length);
            }
        }

        foreach (var plane in planes)
        {
            foreach (var row in plane)
            {
                writer.Write(row);
            }
        }
    }
}
