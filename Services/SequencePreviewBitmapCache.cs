using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace CrossingVoidZDTool.Services;

internal sealed class SequencePreviewBitmapCache
{
    private readonly Dictionary<string, ImageSource> _bitmaps = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _failedPaths = new(StringComparer.OrdinalIgnoreCase);

    public bool IsFailed(string path) => _failedPaths.Contains(path);

    public bool TryGet(string cacheKey, out ImageSource bitmap) => _bitmaps.TryGetValue(cacheKey, out bitmap!);

    public void MarkFailed(string path) => _failedPaths.Add(path);

    /// <summary>
    /// 把"读不出来"的名单清掉 —— 每次预加载前调。
    ///
    /// 以前失败是**永久**的：一张图在预加载时正好被别的进程占着（PS 开着、图集工具刚写完），
    /// 就被记进 `_failedPaths`，以后每次都跳过它 —— 于是"播到后面整个特效都不见了"
    /// （晓桀 2026-09-25 报的）。图是可再生的，每次开播都该重新试一次。
    /// </summary>
    public void ResetFailures() => _failedPaths.Clear();

    public void Store(string cacheKey, ImageSource bitmap)
    {
        if (!string.IsNullOrWhiteSpace(cacheKey))
        {
            _bitmaps[cacheKey] = bitmap;
        }
    }

    /// <summary>
    /// 直接按文件路径加载一张图（特效层用：它的缓存键就是路径本身）。
    /// 失败会抛，调用方自己决定"这一张画不出来"怎么处理。
    /// ⚠️ **只能在 UI 线程调** —— 它内部要建 <see cref="WriteableBitmap"/>（WinRT 对象，跨线程会
    /// 抛 `RPC_E_WRONG_THREAD / 0x8001010E`）。要在后台线程解码就先 <see cref="DecodeToPixels"/>，
    /// 再回到 UI 线程 <see cref="CreateFromPixels"/>。
    /// </summary>
    public static ImageSource LoadFile(string filePath) => CreateFromPixels(DecodeToPixels(filePath));

    /// <summary>解好的一张图（GDI+ 产物，纯内存字节）—— 这一步**可以**在后台线程做。</summary>
    public readonly record struct DecodedBitmap(int Width, int Height, byte[] Pixels);

    /// <summary>
    /// 把 PNG 解成 RGBA 字节（预乘）。**不碰任何 WinRT 类型**，所以能在后台线程跑。
    ///
    /// 拆出这一步就是因为 `WriteableBitmap` 只能在 UI 线程建：以前
    /// `QueueSequenceEffectPreload` 把整条 `LoadFile` 丢进 `Task.Run`，结果每张都抛
    /// `0x8001010E`（晓桀 2026-09-25 贴的日志）。现在后台只解码、UI 线程只建 bitmap。
    /// </summary>
    public static DecodedBitmap DecodeToPixels(string filePath)
    {
        using var source = new Bitmap(filePath);
        using var converted = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(converted))
        {
            graphics.Clear(Color.Transparent);
            graphics.DrawImage(source, 0, 0, source.Width, source.Height);
        }

        var rectangle = new Rectangle(0, 0, converted.Width, converted.Height);
        var bitmapData = converted.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            var rowBytes = converted.Width * 4;
            var stride = Math.Abs(bitmapData.Stride);
            var buffer = new byte[stride * converted.Height];
            Marshal.Copy(bitmapData.Scan0, buffer, 0, buffer.Length);
            if (bitmapData.Stride == rowBytes)
            {
                return new DecodedBitmap(converted.Width, converted.Height, buffer);
            }

            // 行有 padding：按行搬到紧凑缓冲里。
            var compact = new byte[rowBytes * converted.Height];
            for (var y = 0; y < converted.Height; y++)
            {
                Array.Copy(buffer, y * stride, compact, y * rowBytes, rowBytes);
            }

            return new DecodedBitmap(converted.Width, converted.Height, compact);
        }
        finally
        {
            converted.UnlockBits(bitmapData);
        }
    }

    /// <summary>
    /// 把解好的字节装成 <see cref="WriteableBitmap"/>。
    /// ⚠️ **只能在 UI 线程调**（WinRT 对象）。
    /// </summary>
    public static ImageSource CreateFromPixels(DecodedBitmap decoded)
    {
        var writeableBitmap = new WriteableBitmap(decoded.Width, decoded.Height);
        using var pixelStream = writeableBitmap.PixelBuffer.AsStream();
        pixelStream.Write(decoded.Pixels, 0, decoded.Pixels.Length);
        writeableBitmap.Invalidate();
        return writeableBitmap;
    }

    public async Task<IReadOnlyList<SequencePreviewBitmapLoadFailure>> PreloadAsync(IEnumerable<SequenceFrameItem> frames)
    {
        var failures = new List<SequencePreviewBitmapLoadFailure>();
        foreach (var frame in frames.Where(frame => !frame.IsBlank))
        {
            if (_bitmaps.ContainsKey(frame.CacheKey) || _failedPaths.Contains(frame.FilePath))
            {
                continue;
            }

            if (!File.Exists(frame.FilePath))
            {
                _failedPaths.Add(frame.FilePath);
                failures.Add(new SequencePreviewBitmapLoadFailure(
                    frame.FileName,
                    frame.FilePath,
                    "文件不存在",
                    null));
                continue;
            }

            try
            {
                _bitmaps[frame.CacheKey] = LoadImageSource(frame);
                await Task.Yield();
            }
            catch (Exception ex)
            {
                _failedPaths.Add(frame.FilePath);
                failures.Add(new SequencePreviewBitmapLoadFailure(
                    frame.FileName,
                    frame.FilePath,
                    ex.Message,
                    ex.GetType().Name));
            }
        }

        return failures;
    }

    public Task<IReadOnlyList<SequencePreviewBitmapLoadFailure>> PreloadDecodedAsync(IEnumerable<SequenceFrameItem> frames)
    {
        return PreloadAsync(frames);
    }

    /// <summary>
    /// 按**文件路径**预加载 —— 特效层用（它的缓存键就是路径本身，帧对象也不是
    /// <see cref="SequenceFrameItem"/>）。
    ///
    /// **为什么必须有这一条**：角色层开播前会 <see cref="PreloadAsync"/> 把整条序列解好；
    /// 特效层以前没有对应的一步，是**播放中现用现解** —— `UpdateSequenceEffectLayerSource()`
    /// 在 UI 线程的定时器回调里同步 <c>LoadFile</c> 一张 PNG。特效层比角色层快"倍数"倍
    /// （每帧都要换图），于是播放起来一闪一闪、卡得看不清（晓桀 2026-09-25 报的）。
    ///
    /// 空路径直接跳过（特效的空帧本来就没有文件）；解不出来的记进 `_failedPaths`，
    /// 失败清单交回调用方去提示 —— 和角色层那条路同一个规矩。
    /// </summary>
    public async Task<IReadOnlyList<SequencePreviewBitmapLoadFailure>> PreloadPathsAsync(
        IEnumerable<(string FilePath, string DisplayName)> files)
    {
        var failures = new List<SequencePreviewBitmapLoadFailure>();
        foreach (var (filePath, displayName) in files)
        {
            if (string.IsNullOrWhiteSpace(filePath) ||
                _bitmaps.ContainsKey(filePath) ||
                _failedPaths.Contains(filePath))
            {
                continue;
            }

            if (!File.Exists(filePath))
            {
                _failedPaths.Add(filePath);
                failures.Add(new SequencePreviewBitmapLoadFailure(
                    displayName, filePath, "文件不存在", null));
                continue;
            }

            try
            {
                _bitmaps[filePath] = LoadFile(filePath);
                // 和上面那条一样：每张之间让出一次，别把 UI 线程占满。
                await Task.Yield();
            }
            catch (Exception ex)
            {
                _failedPaths.Add(filePath);
                failures.Add(new SequencePreviewBitmapLoadFailure(
                    displayName, filePath, ex.Message, ex.GetType().Name));
            }
        }

        return failures;
    }

    /// <summary>
    /// 按**叠层缓存键**预加载一张"已经叠好的"图：把这一格参与的各层解出来，
    /// 用 <see cref="SequenceEffectFrameComposer"/> 叠成一张，再装成 bitmap 存进缓存。
    ///
    /// 为什么要合成、而不是"一格多显示几张"：见 <see cref="SequenceEffectFrameComposer"/> ——
    /// 预览那边一格只有一张图，图层一多互相盖就是那档闪烁问题的温床
    /// （<c>Docs/特效层预览闪烁-问题档-2026-09-25.md</c>）。
    ///
    /// 解不出来 / 尺寸和第一层对不上的层会被跳过并进失败清单；
    /// **一层都没解出来**就不建缓存（下次播放还会再试一次，和单文件那条一个规矩）。
    /// </summary>
    public async Task<IReadOnlyList<SequencePreviewBitmapLoadFailure>> PreloadStackAsync(
        string stackKey,
        IEnumerable<(string FilePath, string DisplayName)> files,
        string displayName)
    {
        var failures = new List<SequencePreviewBitmapLoadFailure>();
        if (string.IsNullOrWhiteSpace(stackKey) || _bitmaps.ContainsKey(stackKey))
        {
            return failures;
        }

        var decoded = new List<DecodedBitmap>();
        foreach (var (filePath, fileDisplayName) in files)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                continue;
            }

            if (!File.Exists(filePath))
            {
                _failedPaths.Add(filePath);
                failures.Add(new SequencePreviewBitmapLoadFailure(
                    fileDisplayName, filePath, "文件不存在", null));
                continue;
            }

            try
            {
                decoded.Add(DecodeToPixels(filePath));
            }
            catch (Exception ex)
            {
                _failedPaths.Add(filePath);
                failures.Add(new SequencePreviewBitmapLoadFailure(
                    fileDisplayName, filePath, ex.Message, ex.GetType().Name));
            }
        }

        if (decoded.Count == 0)
        {
            return failures;
        }

        // 尺寸取第一层（正常都是同一块画布）；对不上的层直接丢掉，免得合成越界。
        var width = decoded[0].Width;
        var height = decoded[0].Height;
        var pixels = decoded
            .Where(bitmap => bitmap.Width == width && bitmap.Height == height)
            .Select(bitmap => bitmap.Pixels)
            .ToArray();
        try
        {
            _bitmaps[stackKey] = CreateFromPixels(new DecodedBitmap(
                width,
                height,
                SequenceEffectFrameComposer.Compose(width, height, pixels)));
            // 和单文件那条一样：让出一次，别把 UI 线程占满。
            await Task.Yield();
        }
        catch (Exception ex)
        {
            failures.Add(new SequencePreviewBitmapLoadFailure(
                displayName, stackKey, ex.Message, ex.GetType().Name));
        }

        return failures;
    }

    private static ImageSource LoadImageSource(SequenceFrameItem frame)
    {
        try
        {
            return LoadFile(frame.FilePath);
        }
        catch
        {
            if (string.IsNullOrWhiteSpace(frame.FileUri))
            {
                throw;
            }

            return new BitmapImage(new Uri(frame.FileUri, UriKind.Absolute));
        }
    }

    public void Clear()
    {
        _bitmaps.Clear();
        _failedPaths.Clear();
    }
}

internal sealed record SequencePreviewBitmapLoadFailure(
    string FileName,
    string FilePath,
    string Message,
    string? ExceptionType);
