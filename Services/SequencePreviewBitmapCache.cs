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
