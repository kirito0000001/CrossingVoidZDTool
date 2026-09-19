using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;

namespace CrossingVoidZDTool.ViewModels;

/// <summary>
/// 备份 / 还原 / 删除要界面提供的东西（C4）。
/// 对话框和进度都在壳里；流程只负责「按什么顺序问、拿到结果之后做什么」。
/// </summary>
internal interface ICharacterBackupHost
{
    /// <summary>备份备注输入框；返回 null 表示用户取消。</summary>
    Task<string?> ShowBackupNoteDialogAsync(CharacterCard character);

    Task<CharacterBackupEntry> ShowBackupProgressAsync(CharacterCard character, string note);

    /// <summary>挑一份备份；返回 null 表示用户取消。</summary>
    Task<CharacterBackupEntry?> ShowRestoreDialogAsync(
        CharacterCard character,
        IReadOnlyList<CharacterBackupEntry> backups);

    Task<CharacterCard> ShowRestoreProgressAsync(CharacterCard character, CharacterBackupEntry backup);

    /// <summary>删除前的确认；返回 false 表示用户取消。</summary>
    Task<bool> ConfirmDeleteAsync(CharacterCard character);

    void PersistCurrentCharacterSelection();

    void RefreshProductionStatusWithFeedback();

    void ShowFloatingTip(NotifySeverity severity, string title, string message);

    void AppendLog(LogKind kind, string message, Exception? error = null);
}

/// <summary>
/// 角色卡的三条危险操作：备份、还原、删除（角色台右键菜单）。
///
/// 它们原来是三个 async void 处理器，各自「问对话框 → 跑进度 → 落提示与日志」，
/// 中间夹着取消与失败分支。搬到一起是因为三者的形状完全一样，
/// 而且共同守着同一条底线：**先问清楚再动角色目录**。
/// 以前这条底线只有真的点一次右键才能验；现在每一步问什么、按什么顺序问、
/// 用户说不的时候到底动没动数据，都能用假壳断言。
/// </summary>
internal sealed class CharacterBackupController(
    ICharacterBackupHost host,
    CharacterDeskViewModel desk)
{
    private readonly ICharacterBackupHost _host = host;
    private readonly CharacterDeskViewModel _desk = desk;

    /// <summary>备份角色卡（右键菜单）。</summary>
    public async Task BackupAsync(CharacterCard? character)
    {
        if (character is null)
        {
            return;
        }

        var note = await _host.ShowBackupNoteDialogAsync(character);
        if (note is null)
        {
            return;
        }

        try
        {
            var backup = await _host.ShowBackupProgressAsync(character, note);
            _host.ShowFloatingTip(NotifySeverity.Success, "角色卡已备份", backup.DisplayName);
            _host.AppendLog(
                LogKind.User,
                $"备份角色卡：{character.Name} / {character.Code} -> {backup.Path}");
        }
        catch (OperationCanceledException)
        {
            _host.ShowFloatingTip(NotifySeverity.Warning, "备份已取消", character.Name);
        }
        catch (Exception ex)
        {
            _host.ShowFloatingTip(NotifySeverity.Error, "角色卡备份失败", ex.Message);
            _host.AppendLog(LogKind.Error, "角色卡备份失败。", ex);
        }
    }

    /// <summary>还原角色卡：先列出备份让人挑，挑完才动手。</summary>
    public async Task RestoreAsync(CharacterCard? character)
    {
        if (character is null)
        {
            return;
        }

        try
        {
            var backups = await _desk.LoadCharacterBackupsAsync(character);
            var backup = await _host.ShowRestoreDialogAsync(character, backups);
            if (backup is null)
            {
                return;
            }

            var restored = await _host.ShowRestoreProgressAsync(character, backup);
            _host.PersistCurrentCharacterSelection();
            _host.RefreshProductionStatusWithFeedback();
            _host.ShowFloatingTip(NotifySeverity.Success, "角色卡已还原", restored.EffectiveDisplayName);
            _host.AppendLog(
                LogKind.User,
                $"还原角色卡：{character.Name} / {character.Code} <- {backup.Path}");
        }
        catch (OperationCanceledException)
        {
            _host.ShowFloatingTip(NotifySeverity.Warning, "还原已取消", character.Name);
        }
        catch (Exception ex)
        {
            _host.ShowFloatingTip(NotifySeverity.Error, "角色卡还原失败", ex.Message);
            _host.AppendLog(LogKind.Error, "角色卡还原失败。", ex);
        }
    }

    /// <summary>删除角色卡：默认按钮是「删除」，所以确认这一步绝不能省。</summary>
    public async Task DeleteAsync(CharacterCard? character)
    {
        if (character is null)
        {
            return;
        }

        if (!await _host.ConfirmDeleteAsync(character))
        {
            return;
        }

        try
        {
            await _desk.DeleteCharacterAsync(character);
            _host.PersistCurrentCharacterSelection();
            _host.RefreshProductionStatusWithFeedback();
            _host.ShowFloatingTip(NotifySeverity.Success, "角色卡已删除", $"{character.Name} / {character.Code}");
            _host.AppendLog(LogKind.User, $"删除角色卡：{character.Name} / {character.Code}");
        }
        catch (Exception ex)
        {
            _host.ShowFloatingTip(NotifySeverity.Error, "角色卡删除失败", ex.Message);
            _host.AppendLog(LogKind.Error, "角色卡删除失败。", ex);
        }
    }
}
