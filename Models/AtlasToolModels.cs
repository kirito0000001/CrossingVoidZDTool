using System.Collections.Generic;

namespace CrossingVoidZDTool;

/// <summary>工具集里的几个工具。</summary>
internal enum AtlasToolKind
{
    /// <summary>创建图集：一目录 PNG → 一张图集 + 坐标 json。</summary>
    Create,

    /// <summary>拆分图集：一张图集 + 坐标 json → 一张张 PNG。</summary>
    Extract,

    /// <summary>特效PSD：一份多图层 PSD → 一张张按名字规范好的特效帧。</summary>
    PsdEffect
}

/// <summary>
/// 工具卡（清单里的一条）。纯数据，回归可以直接断言"现在有哪些工具"。
///
/// 文案分两段：<paramref name="Summary"/> 是左栏列表里的一行短句（只够说"这工具干什么"），
/// <paramref name="Description"/> 是选中之后右栏标题下面那份完整说明。
/// 之前两处绑的是同一个字段，左栏被挤成三行、右栏又和左栏重复。
/// </summary>
internal sealed record AtlasToolCard(
    AtlasToolKind Kind,
    string Title,
    string Summary,
    string Description,
    string Glyph);

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
            "一个目录的 PNG → 一张图集。",
            "选一个装 PNG 的目录，打成一张图集 + 坐标 json，输出到你指定的位置。",
            "\uE8B7"),
        new(
            AtlasToolKind.Extract,
            "拆分图集",
            "一张图集 → 一张张 PNG。",
            "拿图集 + 它的坐标 json 拆回一张张 PNG；可贴回原始画布尺寸，改完再打回去。",
            "\uE7C4"),
        new(
            AtlasToolKind.PsdEffect,
            "特效PSD",
            "一份 PSD → 一张张特效帧。",
            "读一份多图层 PSD，一个图层组合并成一张特效帧（组里名字以「原本帧」开头的那层跳过），"
            + "按图层顺序排帧（最下面那个组是第 1 帧），再按你写的名字规范文件名叫 <名字>_0001.png，"
            + "落到 <你选的目录>\\<名字>\\ 里 —— 一套特效一个文件夹。"
            + "和「导入特效帧 → 从 PSD 导入…」是同一套读法，只是不碰工作区里的角色素材。",
            "\uE945")
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

/// <summary>
/// 「特效PSD」的输入参数。
///
/// <paramref name="Name"/> 既是**输出文件夹名**、也是**文件名前缀**：填 <c>Ko_Effect</c> 就出
/// <c>&lt;输出目录&gt;\Ko_Effect\Ko_Effect_0001.png</c>。号按**图层顺序**数（最下面那个组是第 1 帧），
/// 不看组名 —— 工具这边攒的 PSD 没有底板导出那套组名规矩。
/// </summary>
internal sealed record PsdEffectToolRequest(
    string PsdPath,
    string Name,
    string OutputDirectory);

/// <summary>
/// 「特效PSD」跑完的结果。<paramref name="OutputDirectory"/> 是**实际落点**
/// （= 你挑的输出目录下面那个以名字命名的子目录），所以跑完打开的就是这一套的目录。
/// </summary>
internal sealed record PsdEffectToolResult(
    string OutputDirectory,
    int FileCount,
    string FirstFileName,
    int LastOrdinal);
