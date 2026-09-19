using System;
using System.IO;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 「同步前备份」那个整包 zip 该落在哪。
///
/// **它只认工作区根**——签名里根本没有 Unreal 工程路径。原来是在壳里拼
/// <c>Path.GetDirectoryName(projectPath) + "Saved" + "ZDToolboxBackups"</c>，
/// 于是备份被写进 <c>&lt;uproject&gt;\Saved\ZDToolboxBackups\&lt;代号&gt;-&lt;时间&gt;.zip</c>：
/// 用户在自己的 Unreal 工程里看到了一份 "Misaka 备份"。
///
/// 备份是**工具箱自己的东西**：工程目录只该被读、被改，不该被囤备份。
/// 放在工作区根下、而不是塞进角色目录，是因为它压的是整个工程（不是这个角色的数据），
/// 否则角色导出/角色备份会跟着带上几个 G。
///
/// 命名沿用原来的形状：<c>&lt;代号&gt;[-标签]-yyyyMMdd-HHmmss.zip</c>。
/// </summary>
internal static class UnrealProjectBackupLocator
{
    /// <summary>备份目录：<c>&lt;工作区&gt;\UnrealProjectBackups</c>（不存在时由备份服务建）。</summary>
    public static string ResolveBackupDirectory(string workspaceRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRootPath);
        return Path.Combine(Path.GetFullPath(workspaceRootPath), CharacterFolderLayout.UnrealProjectBackups);
    }

    /// <summary>
    /// 这一次备份的目标 zip 全路径。
    /// <paramref name="label"/> 是可选的中文标签（例如「基础配置」），插在代号和时间戳之间。
    /// </summary>
    public static string ResolveDestination(
        string workspaceRootPath,
        string characterCode,
        DateTime now,
        string? label = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(characterCode);
        var name = string.IsNullOrWhiteSpace(label)
            ? $"{characterCode}-{now:yyyyMMdd-HHmmss}.zip"
            : $"{characterCode}-{label}-{now:yyyyMMdd-HHmmss}.zip";
        return Path.Combine(ResolveBackupDirectory(workspaceRootPath), name);
    }
}
