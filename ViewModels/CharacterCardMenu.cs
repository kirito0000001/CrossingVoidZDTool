using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace CrossingVoidZDTool.ViewModels;

/// <summary>角色卡右键菜单里的动作（C6c）。</summary>
internal enum CharacterCardMenuAction
{
    Backup,
    Restore,
    Delete
}

/// <summary>
/// 一条菜单项：文案 + 干什么 + 前面要不要分隔线。
///
/// 它**只是数据**，不碰任何 UI 类型——所以可以在回归里直接断言"这个角色该有哪些菜单项、
/// 顺序对不对"，而不是靠把界面跑起来右键一次。
/// </summary>
internal sealed record CharacterCardMenuItem(
    string Text,
    CharacterCardMenuAction Action,
    bool IsSeparatorBefore = false);

/// <summary>
/// 角色台右键菜单的内容（C6c）。
///
/// 以前这份清单**只存在于壳里**：`ShowCharacterCardMenu` 里手写三个 `MenuFlyoutItem`
/// 再 `Click +=` 三个处理器。想改文案要去 XAML 之外的 C# 里翻，想验证只能真右键。
/// 现在清单是这里的一个纯函数，壳只负责把数据变成控件。
/// </summary>
internal static class CharacterCardMenu
{
    public static IReadOnlyList<CharacterCardMenuItem> Build() =>
    [
        new("手动备份", CharacterCardMenuAction.Backup),
        new("还原", CharacterCardMenuAction.Restore),
        new("删除", CharacterCardMenuAction.Delete, IsSeparatorBefore: true)
    ];
}

/// <summary>参考图右键菜单里的动作（C6c，和角色卡那张同一个形状）。</summary>
internal enum CharacterReferenceImageMenuAction
{
    Rename,
    Delete
}

/// <summary>
/// 参考图右键菜单的内容：文案 + 动作。
///
/// 和 <see cref="CharacterCardMenu"/> 一样只描述"有哪些项"，
/// 这样"参考图上能做什么"这件事在回归里可以直接读，不用真右键一次。
/// </summary>
internal sealed record CharacterReferenceImageMenuItem(string Text, CharacterReferenceImageMenuAction Action);

internal static class CharacterReferenceImageMenu
{
    public static IReadOnlyList<CharacterReferenceImageMenuItem> Build() =>
    [
        new("重命名", CharacterReferenceImageMenuAction.Rename),
        new("删除", CharacterReferenceImageMenuAction.Delete)
    ];
}

/// <summary>
/// 角色卡菜单要界面提供的东西。三条流程都在 `CharacterBackupController` 里，
/// 所以这里只需要把控制器交进来。
/// </summary>
internal sealed class CharacterCardMenuCommands(CharacterBackupController backup)
{
    public static CharacterCardMenuCommands? Current { get; private set; }

    public static CharacterCardMenuCommands Attach(CharacterBackupController value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Current = new CharacterCardMenuCommands(value);
        return Current;
    }

    public AsyncRelayCommand BackupCommand { get; } = new(
        (Func<object?, Task>)(parameter => parameter is CharacterCard card
            ? backup.BackupAsync(card)
            : Task.CompletedTask));

    public AsyncRelayCommand RestoreCommand { get; } = new(
        (Func<object?, Task>)(parameter => parameter is CharacterCard card
            ? backup.RestoreAsync(card)
            : Task.CompletedTask));

    public AsyncRelayCommand DeleteCommand { get; } = new(
        (Func<object?, Task>)(parameter => parameter is CharacterCard card
            ? backup.DeleteAsync(card)
            : Task.CompletedTask));
}
