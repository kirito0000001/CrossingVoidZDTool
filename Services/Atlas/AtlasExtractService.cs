using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace CrossingVoidZDTool.Services.Atlas;

/// <summary>
/// 「拆分图集」这个工具：一张图集 + 它的坐标 json → 一张张 PNG。
///
/// 支持的坐标文件就是**装箱器写出来的那两种**（都是 TexturePacker 的格式，
/// 所以别人用 TexturePacker 打的图集也能拆）：
/// <list type="bullet">
/// <item>Hash：<c>frames: { "名字": { frame{x,y,w,h}, rotated, trimmed, spriteSourceSize{x,y,w,h}, sourceSize{w,h} } }</c>
///       —— 我们自己的 <c>&lt;图集名&gt;.json</c> 就是这种；</item>
/// <item>Array：<c>frames: [ { filename, frame{...}, ... } ]</c> —— 我们另外写的 <c>.paper2dsprites</c> 是这种。</item>
/// </list>
///
/// **为什么要"贴回原画布"这个开关**：打包时默认裁掉了透明边（<c>trimmed</c>），
/// 直接按格子切出来的图会变小、位置也不对。开这个开关就按 <c>spriteSourceSize</c>
/// 把图贴回 <c>sourceSize</c> 尺寸的透明画布上，拆出来的图能和原始帧逐像素对上。
/// </summary>
internal sealed class AtlasExtractService
{
    private const string ReportFileName = "_extract_report.json";

    /// <summary>找同名的坐标文件：<c>X.png</c> → <c>X.json</c> / <c>X.paper2dsprites</c>。</summary>
    public static string? ResolveDataFilePath(string atlasImagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(atlasImagePath);
        var stem = Path.GetFileNameWithoutExtension(atlasImagePath);
        var directory = Path.GetDirectoryName(Path.GetFullPath(atlasImagePath))!;
        foreach (var candidate in new[] { $"{stem}.json", $"{stem}.paper2dsprites" })
        {
            var path = Path.Combine(directory, candidate);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    public AtlasExtractResult Extract(AtlasExtractRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!File.Exists(request.AtlasImagePath))
        {
            throw new FileNotFoundException("找不到图集图片。", request.AtlasImagePath);
        }

        if (!File.Exists(request.DataFilePath))
        {
            throw new FileNotFoundException(
                "找不到坐标文件，拆不出来（需要打包时一起生成的 json；只有一张 PNG 没法还原每格的位置）。",
                request.DataFilePath);
        }

        var entries = ReadEntries(request.DataFilePath);
        if (entries.Count == 0)
        {
            throw new InvalidOperationException($"坐标文件里没有可拆的格子：{request.DataFilePath}");
        }

        var rotated = entries.Where(entry => entry.Rotated).Select(entry => entry.Name).ToArray();
        if (rotated.Length > 0)
        {
            throw new InvalidOperationException(
                "这份图集里有旋转过的格子（rotated=true），拆分暂不支持："
                + string.Join("、", rotated.Take(5)));
        }

        var outputDirectory = Path.GetFullPath(request.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);
        // 只删"上一次拆分写出来的那些文件"（报告里记着），不动用户放进来的东西。
        ClearPreviousOutputs(outputDirectory);

        var frames = new List<AtlasExtractFrame>();
        var paddedCount = 0;
        using var sheet = new Bitmap(request.AtlasImagePath);
        foreach (var entry in entries)
        {
            var rectangle = entry.Rectangle;
            if (rectangle.Width <= 0 || rectangle.Height <= 0 ||
                rectangle.Right > sheet.Width || rectangle.Bottom > sheet.Height)
            {
                throw new InvalidOperationException(
                    $"坐标文件里的格子越界：{entry.Name}（{rectangle} 超出图集 {sheet.Width}×{sheet.Height}）。");
            }

            using var cropped = Crop(sheet, rectangle);
            Bitmap output;
            if (request.PasteBackToCanvas && entry.SourceSize.Width > 0 && entry.SourceSize.Height > 0)
            {
                output = PasteBack(cropped, entry);
                if (output.Width != cropped.Width || output.Height != cropped.Height)
                {
                    paddedCount++;
                }
            }
            else
            {
                output = new Bitmap(cropped);
            }

            using (output)
            {
                var fileName = $"{SanitizeFileName(entry.Name)}.png";
                var targetPath = Path.Combine(outputDirectory, fileName);
                output.Save(targetPath, ImageFormat.Png);
                frames.Add(new AtlasExtractFrame(entry.Name, targetPath, null, output.Width, output.Height));
            }
        }

        var reportPath = WriteReport(request, outputDirectory, frames, paddedCount);
        return new AtlasExtractResult(outputDirectory, frames, paddedCount, reportPath);
    }

    /// <summary>读坐标文件（两种格式都认）。</summary>
    public static IReadOnlyList<AtlasExtractEntry> ReadEntries(string dataFilePath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(dataFilePath, Encoding.UTF8));
        var root = document.RootElement;
        if (!root.TryGetProperty("frames", out var frames))
        {
            throw new InvalidOperationException(
                $"这份 json 里没有 frames 段，不像是图集的坐标文件：{dataFilePath}");
        }

        var entries = new List<AtlasExtractEntry>();
        if (frames.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in frames.EnumerateObject())
            {
                entries.Add(ParseEntry(property.Name, property.Value, dataFilePath));
            }
        }
        else if (frames.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in frames.EnumerateArray())
            {
                var name = item.TryGetProperty("filename", out var file) ? file.GetString() : null;
                entries.Add(ParseEntry(name ?? $"sprite_{entries.Count + 1:0000}", item, dataFilePath));
            }
        }
        else
        {
            throw new InvalidOperationException($"frames 既不是对象也不是数组：{dataFilePath}");
        }

        return entries;
    }

    private static AtlasExtractEntry ParseEntry(string name, JsonElement element, string dataFilePath)
    {
        if (!element.TryGetProperty("frame", out var frame))
        {
            throw new InvalidOperationException($"坐标文件里的 {name} 没有 frame 段：{dataFilePath}");
        }

        var rectangle = new Rectangle(
            frame.GetProperty("x").GetInt32(),
            frame.GetProperty("y").GetInt32(),
            frame.GetProperty("w").GetInt32(),
            frame.GetProperty("h").GetInt32());
        var rotated = element.TryGetProperty("rotated", out var rotatedValue) &&
            rotatedValue.ValueKind == JsonValueKind.True;

        var sourceSize = Size.Empty;
        if (element.TryGetProperty("sourceSize", out var sizeElement))
        {
            sourceSize = new Size(
                sizeElement.GetProperty("w").GetInt32(),
                sizeElement.GetProperty("h").GetInt32());
        }

        var offset = Point.Empty;
        if (element.TryGetProperty("spriteSourceSize", out var sourceRectangle))
        {
            offset = new Point(
                sourceRectangle.GetProperty("x").GetInt32(),
                sourceRectangle.GetProperty("y").GetInt32());
        }

        return new AtlasExtractEntry(name, rectangle, rotated, sourceSize, offset);
    }

    private static Bitmap Crop(Bitmap sheet, Rectangle rectangle)
    {
        var cropped = new Bitmap(rectangle.Width, rectangle.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(cropped);
        graphics.DrawImage(
            sheet,
            new Rectangle(0, 0, rectangle.Width, rectangle.Height),
            rectangle,
            GraphicsUnit.Pixel);
        return cropped;
    }

    /// <summary>把裁过的图贴回原始画布（尺寸与位置都按坐标文件来）。</summary>
    private static Bitmap PasteBack(Bitmap cropped, AtlasExtractEntry entry)
    {
        var canvas = new Bitmap(entry.SourceSize.Width, entry.SourceSize.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(canvas);
        graphics.Clear(Color.Transparent);
        graphics.DrawImage(cropped, entry.Offset.X, entry.Offset.Y);
        return canvas;
    }

    /// <summary>删掉上一次拆分写出的文件（读上次的报告，只删它列出来的那些）。</summary>
    private static void ClearPreviousOutputs(string outputDirectory)
    {
        var reportPath = Path.Combine(outputDirectory, ReportFileName);
        if (!File.Exists(reportPath))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(reportPath, Encoding.UTF8));
            if (!document.RootElement.TryGetProperty("frames", out var frames) ||
                frames.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var item in frames.EnumerateArray())
            {
                var file = item.TryGetProperty("output", out var output) ? output.GetString() : null;
                if (!string.IsNullOrWhiteSpace(file) && File.Exists(file))
                {
                    AtomicFileWriter.TryDelete(file);
                }
            }
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            // 清不掉不该让拆分失败：同名文件后面会被覆盖。
            ToolboxLog.Warn($"上次拆分的结果没清干净：{outputDirectory}", error);
        }
    }

    private static string WriteReport(
        AtlasExtractRequest request,
        string outputDirectory,
        IReadOnlyList<AtlasExtractFrame> frames,
        int paddedCount)
    {
        var payload = new Dictionary<string, object?>
        {
            ["atlas"] = Path.GetFileNameWithoutExtension(request.AtlasImagePath),
            ["sheet"] = Path.GetFullPath(request.AtlasImagePath),
            ["data"] = Path.GetFullPath(request.DataFilePath),
            ["pastedBackToCanvas"] = request.PasteBackToCanvas,
            ["paddedFrameCount"] = paddedCount,
            ["frameCount"] = frames.Count,
            ["frames"] = frames.Select(frame => new Dictionary<string, object?>
            {
                ["name"] = frame.SpriteName,
                ["output"] = frame.OutputFilePath,
                ["width"] = frame.Width,
                ["height"] = frame.Height
            }).ToArray()
        };
        var reportPath = Path.Combine(outputDirectory, ReportFileName);
        AtomicFileWriter.WriteAllText(
            reportPath,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return reportPath;
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "sprite" : cleaned;
    }
}

/// <summary>坐标文件里的一格。</summary>
internal sealed record AtlasExtractEntry(
    string Name,
    Rectangle Rectangle,
    bool Rotated,
    Size SourceSize,
    Point Offset);
