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
    /// </summary>
    public static ImageSource LoadFile(string filePath) => LoadWriteableBitmap(filePath);

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

    private static ImageSource LoadImageSource(SequenceFrameItem frame)
    {
        try
        {
            return LoadWriteableBitmap(frame.FilePath);
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

    private static WriteableBitmap LoadWriteableBitmap(string filePath)
    {
        using var source = new Bitmap(filePath);
        using var converted = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(converted))
        {
            graphics.Clear(Color.Transparent);
            graphics.DrawImage(source, 0, 0, source.Width, source.Height);
        }

        var writeableBitmap = new WriteableBitmap(converted.Width, converted.Height);
        var rectangle = new Rectangle(0, 0, converted.Width, converted.Height);
        var bitmapData = converted.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            using var pixelStream = writeableBitmap.PixelBuffer.AsStream();
            var rowBytes = converted.Width * 4;
            var stride = Math.Abs(bitmapData.Stride);
            var buffer = new byte[stride * converted.Height];
            Marshal.Copy(bitmapData.Scan0, buffer, 0, buffer.Length);
            if (bitmapData.Stride == rowBytes)
            {
                pixelStream.Write(buffer, 0, buffer.Length);
            }
            else
            {
                for (var y = 0; y < converted.Height; y++)
                {
                    pixelStream.Write(buffer, y * stride, rowBytes);
                }
            }
        }
        finally
        {
            converted.UnlockBits(bitmapData);
        }

        writeableBitmap.Invalidate();
        return writeableBitmap;
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
