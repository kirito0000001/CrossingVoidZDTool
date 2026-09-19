using System;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;

namespace CrossingVoidZDTool.ViewModels;

/// <summary>
/// 角色详情里的动作要界面提供的东西（C6b）。
/// 收浮层、跳页面都是壳的活；流程只决定「先干什么、失败怎么留痕」。
/// </summary>
internal interface ICharacterDetailActionHost
{
    /// <summary>详情浮层里当前展示的角色卡；没开详情时为 null。</summary>
    CharacterCard? DetailCharacter { get; }

    void HideCharacterDetail();

    /// <summary>跳到 St1-设计理念页。</summary>
    void ShowDesignPage();

    /// <summary>跳到虚幻同步台页。</summary>
    void ShowUnrealSyncPage();

    /// <summary>恢复成草稿之后，刷新详情里持有的那张卡（否则详情还显示旧状态）。</summary>
    void ReplaceDetailCharacter(CharacterCard character);

    void LogUserOperation(string action);

    void PersistCurrentCharacterSelection();

    /// <summary>跳到上次编辑的页面（继续编辑用）。</summary>
    void ShowLastEditedPage(string? moduleTag);

    /// <summary>跳到 St2-基础素材页（没有可跳的模块时的兜底）。</summary>
    void ShowMaterialPage();

    /// <summary>「上次编辑的模块」标签，决定继续编辑跳哪一页。</summary>
    string? LastEditedModuleTag { get; }

    /// <summary>把角色目录交给系统资源管理器打开。</summary>
    void OpenInFileExplorer(string folderPath);

    /// <summary>
    /// 导出这个角色。
    ///
    /// 导出的流程本身在 `CharacterExportController`（C3），这里只**转发**：
    /// 详情弹窗的按钮命令够不到"当前详情里的角色"——那是壳的状态——
    /// 所以借这一个出口把角色递过去，而不是让命令层去猜。
    /// </summary>
    Task ExportDetailCharacterAsync(CharacterCard character);

    void ShowFloatingTip(NotifySeverity severity, string title, string message);

    void AppendLog(LogKind kind, string message, Exception? error = null);
}

/// <summary>
/// 角色详情里的「查看角色」（只读查看已完成角色）。
///
/// 搬出来的理由和前面几条一样：它先有守卫（只有**已完成**角色才给看）、
/// 再有顺序（先进入只读查看 → 收浮层 → 跳 St1），最后还有失败留痕。
/// 这条顺序错了的症状是「点了一下没反应」或「跳过去了但还能改」，
/// 以前只有真点一次才知道；现在可以用假壳断言。
/// </summary>
internal sealed class CharacterDetailActionController(
    ICharacterDetailActionHost host,
    CharacterDeskViewModel desk)
{
    private readonly ICharacterDetailActionHost _host = host;
    private readonly CharacterDeskViewModel _desk = desk;

    public async Task ViewCharacterAsync()
    {
        // 只有已完成角色能「只读查看」；草稿点它不该有任何动作。
        if (_host.DetailCharacter is not { IsCompleted: true } character)
        {
            return;
        }

        try
        {
            await _desk.OpenCompletedCharacterViewAsync(character);
            _host.HideCharacterDetail();
            _host.ShowDesignPage();
            _host.AppendLog(LogKind.User, $"只读查看角色：{character.Name} / {character.Code}");
        }
        catch (Exception ex)
        {
            _host.ShowFloatingTip(NotifySeverity.Error, "打开角色查看失败", ex.Message);
            _host.AppendLog(LogKind.Error, "打开角色只读查看失败。", ex);
        }
    }

    /// <summary>
    /// 「继续编辑」：把只读查看切回可编辑，然后跳到该角色上次编辑的那一页。
    ///
    /// 分支看着多，其实只有两种：**已完成**角色要先恢复成草稿（写盘，可能失败），
    /// 草稿角色只要切过去。两种都在失败时原地返回——绝不能带着半截状态去跳页。
    /// </summary>
    public async Task ContinueEditingAsync()
    {
        if (_host.DetailCharacter is not { } character)
        {
            return;
        }

        _desk.SetViewOnly(false);

        if (character.IsCompleted)
        {
            try
            {
                character = await _desk.ReopenCompletedCharacterAsync(character);
                _host.ReplaceDetailCharacter(character);
                _host.PersistCurrentCharacterSelection();
                _host.LogUserOperation($"恢复角色草稿：{character.Name} / {character.Code}");
            }
            catch (Exception ex)
            {
                _host.ShowFloatingTip(NotifySeverity.Error, "恢复草稿失败", ex.Message);
                _host.AppendLog(LogKind.Error, "恢复已完成角色为草稿失败。", ex);
                return;
            }
        }
        else
        {
            try
            {
                await _desk.SetCurrentCharacterAsync(character);
                _host.PersistCurrentCharacterSelection();
                _host.LogUserOperation($"继续编辑角色：{character.Name} / {character.Code}");
            }
            catch (Exception ex)
            {
                _host.ShowFloatingTip(NotifySeverity.Error, "选择角色失败", ex.Message);
                _host.AppendLog(LogKind.Error, "继续编辑时选择角色失败。", ex);
                return;
            }
        }

        _host.HideCharacterDetail();
        var lastEditedModuleTag = _host.LastEditedModuleTag;
        if (string.Equals(lastEditedModuleTag, "CharacterDesk", StringComparison.Ordinal) ||
            string.Equals(lastEditedModuleTag, "Settings", StringComparison.Ordinal))
        {
            // 角色台/设置不算「编辑过哪个模块」，回到素材页是原来的兜底。
            _host.ShowMaterialPage();
            return;
        }

        _host.ShowLastEditedPage(lastEditedModuleTag);
    }

    /// <summary>「打开角色目录」：交给资源管理器（目录不存在就先建）。</summary>
    public void OpenCharacterFolder()
    {
        if (_host.DetailCharacter is not { } character)
        {
            return;
        }

        try
        {
            _host.OpenInFileExplorer(character.FolderPath);
            _host.AppendLog(LogKind.User, $"打开角色目录：{character.FolderPath}");
        }
        catch (Exception ex)
        {
            _host.ShowFloatingTip(NotifySeverity.Error, "角色目录打开失败", ex.Message);
            _host.AppendLog(LogKind.Error, "角色目录打开失败。", ex);
        }
    }

    /// <summary>「导出角色」：真正的导出在 `CharacterExportController`，这里只把角色递出去。</summary>
    public async Task ExportCharacterAsync()
    {
        if (_host.DetailCharacter is not { } character)
        {
            return;
        }

        await _host.ExportDetailCharacterAsync(character);
    }

    /// <summary>「前往虚幻同步台」：先把这个角色选上，再跳同步台。</summary>
    public async Task GoToUnrealSyncAsync()
    {
        if (_host.DetailCharacter is not { } character)
        {
            return;
        }

        try
        {
            await _desk.SetCurrentCharacterAsync(character);
            _host.PersistCurrentCharacterSelection();
        }
        catch (Exception ex)
        {
            _host.ShowFloatingTip(NotifySeverity.Error, "选择角色失败", ex.Message);
            _host.AppendLog(LogKind.Error, "前往虚幻同步台时选择角色失败。", ex);
            return;
        }

        _host.HideCharacterDetail();
        _host.ShowUnrealSyncPage();
    }
}
