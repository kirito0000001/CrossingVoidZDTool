using System.Collections.Generic;

namespace CrossingVoidZDTool;

/// <summary>图集工具集里的两个工具。</summary>
internal enum AtlasToolKind
{
    /// <summary>创建图集：一目录 PNG → 一张图集 + 坐标 json。</summary>
    Create,

    /// <summary>拆分图集：一张图集 + 坐标 json → 一张张 PNG。</summary>
    Extract
}

/// <summary>工具卡（清单里的一条）。纯数据，回归可以直接断言"现在有哪些工具"。</summary>
internal sealed record AtlasToolCard(AtlasToolKind Kind, string Title, string Description, string Glyph);

/// <summary>
/// 工具集里当前有哪些工具。
///
/// **加新工具就三步**：这里加一张卡、XAML 加一个参数面板（绑 <c>IsXxxToolSelected</c>）、
/// 宿主加一个"跑"的方法。清单是纯函数，所以"现在有哪几个工具"在回归里能直接断言。
/// </summary>
internal static class AtlasToolCatalog
{
    public static IReadOnlyList<AtlasToolCard> Build() =>
    [
        new(
            AtlasToolKind.Create,
            "创建图集",
            "选一个装 PNG 的目录，打成一张图集 + 坐标 json，输出到你指定的位置。",
            "\uE8B7"),
        new(
            AtlasToolKind.Extract,
            "拆分图集",
            "拿图集 + 它的坐标 json 拆回一张张 PNG；可贴回原始画布尺寸，改完再打回去。",
            "\uE7C4")
    ];
}

/// <summary>创建图集的输入参数。</summary>
internal sealed record AtlasCreateRequest(
    string SourceFolderPath,
    string OutputDirectory,
    string AtlasName,
    string Mode,
    int Columns,
    int Padding,
    bool Trim,
    int MaxSize);

internal sealed record AtlasCreateResult(
    string OutputDirectory,
    string AtlasImagePath,
    string DataFilePath,
    int FrameCount,
    string SizeText);

/// <summary>拆分图集的输入参数。</summary>
internal sealed record AtlasExtractRequest(
    string AtlasImagePath,
    string DataFilePath,
    string OutputDirectory,
    bool PasteBackToCanvas);

internal sealed record AtlasExtractFrame(
    string SpriteName,
    string OutputFilePath,
    string? OriginalSourcePath,
    int Width,
    int Height);

internal sealed record AtlasExtractResult(
    string OutputDirectory,
    IReadOnlyList<AtlasExtractFrame> Frames,
    int PaddedToCanvasCount,
    string? ReportPath);
