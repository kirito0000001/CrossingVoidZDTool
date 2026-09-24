using System.IO;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 同步台各步自己的小缓存落在哪 —— 全项目**唯一**一处算这个路径。
///
/// 以前它住在 `UnrealSyncSessionCacheService`（那份"所有步骤共用的一大坨"）里，
/// 六个小文件都得绕过去调它。现在那一坨拆掉了，路径算法搬出来独立（2026-09-24）。
/// 落点：`&lt;角色&gt;/&lt;工具目录&gt;/UnrealSync/`。
/// </summary>
internal static class UnrealSyncCacheFolder
{
    public const string FolderName = "UnrealSync";

    /// <summary>角色自己的进度目录；没有本地角色（例如只在虚幻侧的候选）时返回空。</summary>
    public static string GetCacheFolderPath(CharacterCard? character) =>
        character is null || string.IsNullOrWhiteSpace(character.ToolFolderPath)
            ? string.Empty
            : Path.Combine(character.ToolFolderPath, FolderName);
}
