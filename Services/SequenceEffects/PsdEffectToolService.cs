using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 工具集《特效PSD》的服务（晓桀 2026-09-28）：一份多图层 PSD → 一张张按名字规范好的特效帧。
///
/// 读法完全复用「导入特效帧 → 从 PSD 导入…」那一套（<see cref="SequenceEffectPsdImportService"/>）：
/// 一个图层组 = 一帧，组里名字以「原本帧」开头的那层跳过，帧号按组名末尾的数字认。
///
/// 和导入那条的差别只有落点和文件名：那边写进工作区里的角色素材目录（还要清层、写规范名），
/// 这边写进用户自己挑的目录（默认 <c>&lt;工作区&gt;\Tools\PSDEffect</c>）**下面一个以名字命名的子目录**，
/// 文件名是 <c>&lt;名字&gt;_0001.png</c> —— **不碰工作区里的任何角色素材**。
///
/// 帧号按**图层顺序**数（第 1 个组 = 第 1 帧），不看组名里的号：工具这边攒的 PSD 没有
/// 底板导出那套组名规矩（晓桀 2026-09-28）。
/// </summary>
internal sealed class PsdEffectToolService
{
    /// <summary>工作区里放这个工具输出的目录名。</summary>
    public const string OutputFolderName = "PSDEffect";

    /// <summary>工作区里放各种工具输出的那一层目录名。</summary>
    public const string ToolsFolderName = "Tools";

    /// <summary>
    /// 默认落点 = <c>&lt;工作区&gt;\Tools\PSDEffect</c>。工作区路径由壳给
    /// （<c>Settings.ProjectRootPath</c>），服务自己不去猜盘上的位置。
    /// </summary>
    public static string ResolveDefaultOutputFolder(string projectRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRootPath);
        return Path.Combine(projectRootPath, ToolsFolderName, OutputFolderName);
    }

    /// <summary>
    /// 名字要直接当文件名用，非法字符先拦下来 ——
    /// 不然要等到落盘那一步才炸，报的还是"路径里有非法字符"这种看不懂的话。
    /// </summary>
    public static void EnsureUsableName(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new InvalidOperationException(
                "名字不能空着 —— 它既是输出文件夹名，也是文件名的前缀"
                + "（填 Ko_Effect 就出 PSDEffect\\Ko_Effect\\Ko_Effect_0001.png）。");
        }

        var invalid = Path.GetInvalidFileNameChars().Where(character => character >= ' ').ToArray();
        if (trimmed.IndexOfAny(invalid) >= 0)
        {
            throw new InvalidOperationException(
                $"名字里不能有这些字符：{string.Join(' ', invalid)}{Environment.NewLine}"
                + "它会直接当文件名用（例如 Ko_Effect → Ko_Effect_0001.png）。");
        }
    }

    /// <summary>
    /// 跑一趟：按图层组逐帧合并，落到 <c>&lt;输出目录&gt;\&lt;名字&gt;\</c>，
    /// 文件名规范成 <c>&lt;名字&gt;_0001.png</c>（晓桀 2026-09-28：按名字分一个文件夹，
    /// 好几种特效的帧就不会混在同一个目录里）。
    ///
    /// 那个名字目录里**只清这个名字的旧帧**（<c>&lt;名字&gt;_*.png</c>）——
    /// 它自己就是这一套的目录，别的名字各有各的目录，互不影响。
    /// </summary>
    public PsdEffectToolResult Export(
        PsdEffectToolRequest request,
        IProgress<SequenceEffectImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureUsableName(request.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputDirectory);

        var name = request.Name.Trim();
        // 名字目录：一套特效一个目录，输出目录里再乱也波及不到别处。
        var targetFolder = Path.Combine(request.OutputDirectory, name);
        Directory.CreateDirectory(targetFolder);

        var frames = new SequenceEffectPsdImportService().StageGroups(
            request.PsdPath,
            targetFolder,
            name,
            progress,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return new PsdEffectToolResult(
            targetFolder,
            frames.Count,
            frames.Count == 0 ? string.Empty : Path.GetFileName(frames[0].FilePath),
            frames.Count == 0 ? 0 : frames[^1].Ordinal);
    }
}
