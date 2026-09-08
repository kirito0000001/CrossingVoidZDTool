using System;

namespace CrossingVoidZDTool;

/// <summary>
/// 同步台中栏当前该显示什么。
///
/// 以前中栏由九个各自独立的可见性绑定拼出来，谁都不知道别人在不在显示，
/// 于是「还没检测」这种没人认领的组合就是一片空白——第四步和第六步都撞过。
/// 收敛成一个状态之后，空白态在结构上不可能出现：枚举必然命中一个模板。
/// </summary>
internal enum UnrealSyncWorkspaceState
{
    /// <summary>还没选角色。</summary>
    NoCharacter,

    /// <summary>选了角色，但这一步还没检测过。</summary>
    NotDetected,

    /// <summary>正在检测或正在执行。</summary>
    Busy,

    /// <summary>有内容要处理，显示这一步自己的列表。</summary>
    HasContent,

    /// <summary>检测过了，没有需要处理的内容。</summary>
    NoChanges,

    /// <summary>这一步失败了。</summary>
    Failed,
}

/// <summary>中栏占位面板的文案。内容态不用它——那时显示的是各步自己的列表。</summary>
internal readonly record struct UnrealSyncWorkspacePlaceholder(
    string Glyph,
    string Title,
    string Description,
    bool IsError)
{
    public static UnrealSyncWorkspacePlaceholder For(
        UnrealSyncWorkspaceState state,
        int workflowStep,
        string stepName,
        string detail)
    {
        return state switch
        {
            UnrealSyncWorkspaceState.NoCharacter => new(
                "",
                "尚未选择角色",
                "请在左侧选择一个已完成角色，再开始同步。",
                false),
            UnrealSyncWorkspaceState.NotDetected => new(
                "",
                $"尚未检测{stepName}",
                string.IsNullOrWhiteSpace(detail)
                    ? $"点击右侧的「重新加载」开始检测第 {workflowStep} 步。"
                    : detail,
                false),
            UnrealSyncWorkspaceState.Busy => new(
                "",
                $"正在检测{stepName}",
                string.IsNullOrWhiteSpace(detail) ? "正在读取 Unreal 项目，请稍候。" : detail,
                false),
            UnrealSyncWorkspaceState.NoChanges => new(
                "",
                $"{stepName}没有差异",
                string.IsNullOrWhiteSpace(detail) ? "当前没有需要处理的内容。" : detail,
                false),
            UnrealSyncWorkspaceState.Failed => new(
                "",
                $"{stepName}检测失败",
                string.IsNullOrWhiteSpace(detail) ? "请查看下方输出日志。" : detail,
                true),
            _ => new(string.Empty, string.Empty, string.Empty, false),
        };
    }
}
