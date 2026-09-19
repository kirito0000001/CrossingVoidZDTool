using System;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;

namespace CrossingVoidZDTool.ViewModels;

/// <summary>
/// 新建角色卡要界面提供的东西（C5）。
/// 对话框本体（浮层 + 卡片缩放动画 + 等待用户输入）留在壳里，流程只拿到最终的名字。
/// </summary>
internal interface ICharacterDeskCreateHost
{
    /// <summary>弹出「新建角色卡」浮层并等用户输入；返回 null / 空白表示取消。</summary>
    Task<string?> ShowCreateDialogAsync();

    void PersistCurrentCharacterSelection();

    void ShowFloatingTip(NotifySeverity severity, string title, string message);

    void AppendLog(LogKind kind, string message, Exception? error = null);
}

/// <summary>
/// 新建角色卡（角色台的「新建角色」按钮）。
///
/// 这是四条流程里唯一带 UI 动画的一条，但动画全在壳里——流程只看名字有没有、
/// 创建成不成、失败有没有留下痕迹。搬出来的价值在于那条容易漏的路：
/// 名字空白要**什么都不做**（不要建出一个空角色），创建失败要把原因写进状态栏与日志，
/// 而不是只在界面上闪一下。这两条以前只有真点一次才知道。
/// </summary>
internal sealed class CharacterCreateController(
    ICharacterDeskCreateHost host,
    CharacterDeskViewModel desk)
{
    private readonly ICharacterDeskCreateHost _host = host;
    private readonly CharacterDeskViewModel _desk = desk;

    public async Task CreateAsync()
    {
        var characterName = await _host.ShowCreateDialogAsync();
        if (string.IsNullOrWhiteSpace(characterName))
        {
            return;
        }

        try
        {
            var character = await _desk.CreateCharacterAsync(characterName);
            _host.PersistCurrentCharacterSelection();
            _host.ShowFloatingTip(
                NotifySeverity.Success,
                "角色卡已创建",
                $"{character.Name} / {character.Code}");
            _host.AppendLog(LogKind.User, $"创建角色卡：{character.Name} / {character.Code}");
        }
        catch (Exception ex)
        {
            // 失败只改状态栏 + 落错误日志（与搬之前一致）：建角色失败必须留痕。
            _desk.StatusText = $"创建角色失败：{ex.Message}";
            _host.AppendLog(LogKind.Error, "创建角色卡失败。", ex);
        }
    }
}
