using System;

namespace CrossingVoidZDTool;

/// <summary>
/// 图集的落点。
///
/// 两个场景的产物寿命完全不同，所以分成两种目标：
/// <list type="bullet">
/// <item><see cref="Export"/> —— St5 手动导出，是**交付物**，用户会拿走，不自动清理。</item>
/// <item><see cref="Cache"/> —— 序列同步时顺带打，**一次性**，每轮同步前可以清掉。</item>
/// </list>
/// </summary>
internal enum AtlasDestination
{
    /// <summary><c>&lt;工作区&gt;/Export/&lt;角色&gt;/Atlas/&lt;动作&gt;/</c></summary>
    Export,

    /// <summary><c>&lt;角色&gt;/tool/AtlasCache/&lt;动作&gt;/</c></summary>
    Cache,
}

/// <summary>一次图集打包的结果。</summary>
/// <param name="AtlasName">图集名，形如 <c>Misaka_Sk2</c>。</param>
/// <param name="Width">图集宽（像素）。</param>
/// <param name="Height">图集高（像素）。</param>
/// <param name="ImagePath">产出的 PNG 绝对路径。</param>
/// <param name="FrameCount">实际打进图集的帧数（**不含**空白帧）。</param>
/// <param name="OutputDirectory">产物所在目录，界面上「打开目录」用这个。</param>
/// <param name="Duration">本次耗时，用于日志。</param>
internal sealed record AtlasPackResult(
    string AtlasName,
    int Width,
    int Height,
    string ImagePath,
    int FrameCount,
    string OutputDirectory,
    TimeSpan Duration)
{
    /// <summary>界面上的尺寸文案。</summary>
    public string SizeText => Width > 0 && Height > 0 ? $"{Width} × {Height}" : "尺寸未知";
}

/// <summary>
/// 图集工具写出来的 <c>--report</c> 文件。
///
/// **它是这条链路上唯一的返回值** —— 和 Unreal 侧同一个道理：退出码不可靠，
/// 判定成败一律看这个文件是不是**这一轮新写的**、且 <see cref="Ok"/> 为真。
/// 字段名走 camelCase（图集工具侧的约定），所以这个类型挂在
/// <c>AtlasJsonContext</c> 上而不是 <c>AppJsonSerializerContext</c>。
/// </summary>
internal sealed class AtlasReport
{
    public bool Ok { get; set; }

    public string? Atlas { get; set; }

    public string? Image { get; set; }

    public AtlasReportSize? Size { get; set; }

    public int FrameCount { get; set; }

    /// <summary>图集工具在失败时带的说明，成功时为空。</summary>
    public string? Error { get; set; }

    /// <summary>
    /// 图集工具带的警告列表（例如「源帧尺寸不统一，锚点会抖」）。
    ///
    /// ⚠️ 字段名是复数、类型是**数组** —— 这是实测 report.json 得到的，
    /// 不是照着文档猜的单数字符串。猜错的话这里永远是 null，
    /// 而「源帧尺寸不统一」正是最该被看见的那条警告，会静默丢掉。
    /// </summary>
    public System.Collections.Generic.List<string>? Warnings { get; set; }

    /// <summary>成功时也可能带警告。这类警告不该让整件事失败，但要进日志。</summary>
    public string? WarningText =>
        Warnings is { Count: > 0 } ? string.Join("；", Warnings) : null;
}

internal sealed class AtlasReportSize
{
    public int W { get; set; }

    public int H { get; set; }
}

/// <summary>
/// 写给图集工具的清单文件（<c>--manifest</c> 的输入）。
///
/// 契约见 <c>Tools/Atlas/MANIFEST.md</c>。这里刻意只放**协议字段**，
/// 不带任何工具箱内部概念（没有「形态」「复用」这些词）—— 图集工具不需要懂这些。
/// </summary>
internal sealed class AtlasManifest
{
    public string Atlas { get; set; } = string.Empty;

    public string Mode { get; set; } = "pack";

    public System.Collections.Generic.List<AtlasManifestFrame> Frames { get; set; } = [];
}

internal sealed class AtlasManifestFrame
{
    /// <summary>帧图绝对路径。</summary>
    public string File { get; set; } = string.Empty;

    /// <summary>精灵名。**必须**与 Unreal 侧同一套规则（见 <c>SequenceActionCatalog</c>）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>序号，从 1 起（清单接口的约定），等于「原始数组下标 + 1」。</summary>
    public int Index { get; set; }
}

/// <summary>
/// 图集工具写出来的 <c>&lt;图集名&gt;_sequence.json</c>。
///
/// 它回答的问题只有一个：**每张素材落在图集的哪个矩形里**。
/// 帧序不归它管（那是序列清单的事），素材目录里有几张 PNG 它就有几条。
///
/// 同步序列要的就是这个矩形 —— 有了它才能在一张贴图上切出 N 个 Sprite，
/// 而不是把 N 张 PNG 各导入成一个贴图。
/// </summary>
internal sealed class AtlasSequenceManifest
{
    public string Atlas { get; set; } = string.Empty;

    public string? Image { get; set; }

    public string? Sprites { get; set; }

    /// <summary>图集贴图的尺寸。</summary>
    public AtlasReportSize? Texture { get; set; }

    public int FrameCount { get; set; }

    public string? Order { get; set; }

    public System.Collections.Generic.List<AtlasSequenceFrame> Frames { get; set; } = [];
}

/// <summary>
/// 图集里的一格。字段名对齐图集工具的产物（camelCase），详见 <c>Tools/Atlas/MANIFEST.md</c>。
/// </summary>
internal sealed class AtlasSequenceFrame
{
    /// <summary>序号，从 1 起；等于写入清单时的下标 + 1。**这是与清单对齐的唯一可靠键。**</summary>
    public int Index { get; set; }

    /// <summary>精灵名。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>这一格在图集贴图里的矩形。</summary>
    public AtlasRect? Frame { get; set; }

    /// <summary>是否在图集里被转了 90°。</summary>
    public bool Rotated { get; set; }

    /// <summary>是否被裁掉了透明边。</summary>
    public bool Trimmed { get; set; }

    /// <summary>裁剪后那块内容在**原图**里的位置与尺寸。</summary>
    public AtlasRect? SpriteSourceSize { get; set; }

    /// <summary>裁剪前的原图尺寸。</summary>
    public AtlasReportSize? SourceSize { get; set; }
}

/// <summary>
/// 一个像素矩形。图集工具给的是 <c>{x, y, w, h}</c>，和 <see cref="AtlasReportSize"/>
/// 的 <c>{w, h}</c> 不是一个形状，所以单列一个类型，免得读串。
/// </summary>
internal sealed class AtlasRect
{
    public int X { get; set; }

    public int Y { get; set; }

    public int W { get; set; }

    public int H { get; set; }
}
