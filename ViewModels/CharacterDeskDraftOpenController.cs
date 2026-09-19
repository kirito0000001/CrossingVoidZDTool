using System;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;

namespace CrossingVoidZDTool.ViewModels;

/// <summary>
/// 「打开草稿」流程要界面提供的东西（C2）。
/// 全是壳：提示、日志、引导浮层、把当前角色选择落盘、记最后编辑模块、页面入场动画。
/// </summary>
internal interface ICharacterDeskDraftOpenHost
{
    void ShowFloatingTip(NotifySeverity severity, string title, string message);

    void AppendLog(LogKind kind, string message, Exception? error = null);

    /// <summary>「当前未选择角色」那种一次性引导浮层。</summary>
    void ShowTextGuideOverlay(string title, string message);

    void PersistCurrentCharacterSelection();

    void MarkLastEditedModule(string moduleTag);

    void TryPlayDraftPageEntrance();
}

/// <summary>
/// 点角色卡 → 打开草稿 → 进 St1 这条流程（角色台 / St1-设计理念）。
///
/// 以前它长在 <c>MainWindow.CharacterDesk.cs</c> 里，用字段 <c>_isOpeningDraftCharacterCard</c>
/// 防重入，分两步走（先选中角色、再打开草稿），每步外面套一层 <c>RunDraftOpenStep</c>
/// 打点日志。问题是这一整套只有把界面跑起来、真的点一张卡才能验证——而它恰恰是
/// 「点一下没反应」「卡在草稿卡上」这类问题的现场。
///
/// 搬过来之后：防重入标志、两步顺序、每一步的成功/失败日志、以及
/// 「选中之后居然没有当前角色」这条兜底分支，全都能用假 Host 直接断言。
/// 行为与搬之前逐行对齐（日志文案保持不变，方便和旧日志对照）。
/// </summary>
internal sealed class CharacterDeskDraftOpenController(
    ICharacterDeskDraftOpenHost host,
    CharacterDeskViewModel desk)
{
    private readonly ICharacterDeskDraftOpenHost _host = host;
    private readonly CharacterDeskViewModel _desk = desk;
    private bool _isOpening;

    public async Task OpenAsync(CharacterCard? character)
    {
        if (_isOpening)
        {
            return;
        }

        _isOpening = true;
        try
        {
            if (character is null)
            {
                _host.ShowFloatingTip(NotifySeverity.Warning, "草稿卡打开失败", "没有识别到被点击的角色卡。");
                _host.AppendLog(LogKind.Warning, "DraftCard open failed. CharacterFromEvent=<null>.");
                return;
            }

            _host.AppendLog(
                LogKind.User,
                $"DraftCard tapped. Character={character.Code} Name={character.EffectiveDisplayName} Path={character.FolderPath}");

            await RunStepAsync("SetCurrentCharacter", () => _desk.SetCurrentCharacterAsync(character));
            _host.AppendLog(
                LogKind.User,
                $"DraftCard SetCurrentCharacter completed. Current={_desk.CurrentCharacter?.Code ?? "<null>"}");
            _host.PersistCurrentCharacterSelection();

            if (_desk.CurrentCharacter is null)
            {
                _host.ShowTextGuideOverlay("当前未选择角色", "当前未选择角色，请点击草稿卡进行选择。");
                return;
            }

            _host.AppendLog(
                LogKind.User,
                $"DraftCard OpenCurrentCharacterDraft starting. Character={_desk.CurrentCharacter.Code}");
            await RunStepAsync("OpenCurrentCharacterDraft", () => _desk.OpenCurrentCharacterDraftAsync());
            _host.AppendLog(
                LogKind.User,
                $"DraftCard OpenCurrentCharacterDraft completed. IsDraftOpen={_desk.IsDraftOpen}");

            // 进场动画失败不能影响「草稿已经打开」这个事实，所以它自己吞异常。
            _host.TryPlayDraftPageEntrance();
            _host.MarkLastEditedModule("ActionFrames");
            _host.PersistCurrentCharacterSelection();
            _host.ShowFloatingTip(NotifySeverity.Success, "已进入草稿", _desk.CurrentCharacterName);
            _host.AppendLog(LogKind.User, $"打开 St1 草稿：{_desk.CurrentCharacterName}");
        }
        catch (Exception ex)
        {
            _host.ShowFloatingTip(NotifySeverity.Error, "草稿卡打开失败", ExceptionText.ForTip(ex));
            _host.AppendLog(LogKind.Error, "DraftCard open failed.", ex);
        }
        finally
        {
            _isOpening = false;
        }
    }

    /// <summary>每一步外面套一层，把「卡在哪一步」写进日志——失败时它是唯一的线索。</summary>
    private async Task RunStepAsync(string stepName, Func<Task> action)
    {
        try
        {
            _host.AppendLog(LogKind.User, $"DraftCard step begin: {stepName}");
            await action();
            _host.AppendLog(LogKind.User, $"DraftCard step end: {stepName}");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"DraftCard step failed: {stepName}", ex);
        }
    }
}
