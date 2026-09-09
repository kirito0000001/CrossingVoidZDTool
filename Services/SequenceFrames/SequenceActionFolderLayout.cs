using System;
using System.IO;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 动作目录的布局与路径推导：一个动作在磁盘上长什么样，以及绝对路径和清单里那份
/// 相对路径之间怎么互转。
///
/// 从 <see cref="SequenceFrameService"/> 里搬出来的。留在那个类里时，这些两三行的
/// 小函数散落在一千多行中间，改一处目录规则要先把整个服务通读一遍才敢下手。
///
/// 这里最要紧的一条约定：<b>清单里的帧路径是相对动作目录的，语音路径是相对角色目录的</b>。
/// 角色目录要能在 Draft/Completed 之间整体搬家、也要能跟着工作区根目录迁移，
/// 一旦写成绝对路径，搬完之后所有帧和语音绑定就全断了。
/// 任何时候都不要把 <see cref="ResolveManifestPath"/> 的结果写回清单。
/// </summary>
internal static class SequenceActionFolderLayout
{
    /// <summary>每个动作目录下的清单文件名。</summary>
    public const string ManifestFileName = "sequence.json";

    /// <summary>动作目录下存放实际帧图的子目录名。</summary>
    public const string FramesFolderName = CharacterFolderLayout.Frames;

    private const string ZdMaterialFolderName = CharacterFolderLayout.ZdMaterial;

    /// <summary>角色名下所有动作目录的父目录。</summary>
    public static string GetMaterialRootPath(CharacterCard character)
    {
        return Path.Combine(character.FolderPath, ZdMaterialFolderName);
    }

    public static string GetActionFolderPath(CharacterCard character, SequenceFrameAction action)
    {
        return Path.Combine(character.FolderPath, ZdMaterialFolderName, action.Code);
    }

    public static string GetFramesFolderPath(CharacterCard character, SequenceFrameAction action)
    {
        return Path.Combine(GetActionFolderPath(character, action), FramesFolderName);
    }

    public static string GetManifestPath(CharacterCard character, SequenceFrameAction action)
    {
        return Path.Combine(GetActionFolderPath(character, action), ManifestFileName);
    }

    /// <summary>把清单里的相对路径还原成绝对路径。只用于读盘，不要写回清单。</summary>
    public static string ResolveManifestPath(CharacterCard character, SequenceFrameAction action, string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        return Path.GetFullPath(Path.Combine(GetActionFolderPath(character, action), normalized));
    }

    /// <summary>把一个帧文件按「帧池里的同名文件」推回相对路径，用于按路径反查清单条目。</summary>
    public static string ToPoolRelativePath(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        return NormalizeRelativePath(Path.Combine(FramesFolderName, fileName));
    }

    /// <summary>语音绑定存的是相对角色目录的路径。</summary>
    public static string ToCharacterRelativePath(CharacterCard character, string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return string.Empty;
        }

        return NormalizeRelativePath(Path.GetRelativePath(character.FolderPath, Path.GetFullPath(filePath)));
    }

    public static string ResolveVoicePath(CharacterCard character, string? relativePath)
    {
        return string.IsNullOrWhiteSpace(relativePath)
            ? string.Empty
            : Path.GetFullPath(Path.Combine(character.FolderPath, NormalizeRelativePath(relativePath)));
    }

    /// <summary>清单里一律用 '/' 分隔，这样同一份角色目录在不同机器上拷来拷去也不会变形。</summary>
    public static string NormalizeRelativePath(string value)
    {
        return value.Replace('\\', '/').TrimStart('/');
    }

    /// <summary>
    /// 历史上这几个动作的目录名拼写不统一（Ondm/OnDM、Defense、Flydown……）。
    /// 规范目录不存在、而某个旧拼写目录还在时，整目录改名过来——不能新建一个空目录，
    /// 那会让用户原来的帧凭空「消失」。
    /// </summary>
    public static void MigrateLegacyActionFolderPath(
        CharacterCard character,
        SequenceFrameAction action,
        string canonicalFolderPath)
    {
        if (Directory.Exists(canonicalFolderPath)) return;
        var aliases = action.Code switch
        {
            "OnDamage" => new[] { "Ondm", "OnDM" },
            "Defence" => new[] { "Defense" },
            "FlyDown" => new[] { "Flydown" },
            "FlyStart" => new[] { "Flystart" },
            "StandUP" => new[] { "Standup" },
            _ => Array.Empty<string>()
        };
        foreach (var alias in aliases)
        {
            var legacyPath = Path.Combine(character.FolderPath, ZdMaterialFolderName, alias);
            if (!Directory.Exists(legacyPath)) continue;
            Directory.Move(legacyPath, canonicalFolderPath);
            return;
        }
    }
}
