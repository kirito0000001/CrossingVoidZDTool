using System;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;

namespace CrossingVoidZDTool.ViewModels;

/// <summary>
/// 刷新角色台要界面提供的东西（C6a）。
/// 三条设置里存着「上次选的角色 / 上次编辑的角色」，加载完还要把当前选择落盘。
/// </summary>
internal interface ICharacterDeskReloadHost
{
    string ProjectRootPath { get; }

    string CurrentCharacterCode { get; }

    string LastEditedCharacterCode { get; }

    void PersistCurrentCharacterSelection();

    void AppendLog(LogKind kind, string message, Exception? error = null);
}

/// <summary>
/// 刷新角色台（角色台右上角那个刷新图标），启动时也走它。
///
/// 原来是一个两处调用的私有方法：成功记一条「已加载 N 张」，失败把原因写进状态栏
/// 与错误日志。搬出来的价值是失败那条路——它以前只有真的把项目目录弄坏一次才能看到，
/// 而「角色卡一张都不显示」这种投诉，十有八九就是它悄悄失败的那一次。
/// </summary>
internal sealed class CharacterDeskReloadController(
    ICharacterDeskReloadHost host,
    CharacterDeskViewModel desk)
{
    private readonly ICharacterDeskReloadHost _host = host;
    private readonly CharacterDeskViewModel _desk = desk;

    public async Task ReloadAsync()
    {
        try
        {
            await _desk.LoadCharactersAsync(
                _host.ProjectRootPath,
                _host.CurrentCharacterCode,
                _host.LastEditedCharacterCode);
            _host.PersistCurrentCharacterSelection();
            _host.AppendLog(LogKind.Info, $"已加载角色卡：{_desk.Characters.Count} 张。");
        }
        catch (Exception ex)
        {
            // 加载失败必须留痕：状态栏给用户看，日志给排查用。
            _desk.StatusText = $"角色卡加载失败：{ex.Message}";
            _host.AppendLog(LogKind.Error, "角色卡加载失败。", ex);
        }
    }
}
