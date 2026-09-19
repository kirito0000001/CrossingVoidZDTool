using System;
using System.IO;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;

namespace CrossingVoidZDTool.ViewModels;

/// <summary>
/// 「导出完整角色文件夹」这条流程要界面提供的东西（C3）。
///
/// 三个对话框（选位置、覆盖确认、导出进度）都在壳里——它们本来就是界面；
/// 流程只负责按顺序问它们、把结果拼起来、决定什么时候提示什么。
/// </summary>
internal interface ICharacterDeskExportHost
{
    /// <summary>导出位置的默认根（跟着工具箱的项目根目录走）。</summary>
    string ProjectRootPath { get; }

    Task<string?> ShowExportLocationDialogAsync(CharacterCard character, string defaultExportRoot);

    Task<bool> ConfirmOverwriteAsync(CharacterCard character, string targetPath);

    /// <summary>导出并返回落地路径；用户取消时抛 <see cref="OperationCanceledException"/>。</summary>
    Task<string> ShowExportProgressAsync(CharacterCard character, string exportRoot, bool overwrite);

    void ShowFloatingTip(NotifySeverity severity, string title, string message);

    void AppendLog(LogKind kind, string message, Exception? error = null);
}

/// <summary>
/// 导出完整角色文件夹（角色台详情的「导出」按钮）。
///
/// 从 MainWindow 里搬出来的是**分支顺序**：先问默认根 → 让用户选位置 →
/// 用「导出根 + 角色代号」算出目标 → 目标已存在才问覆盖 → 走导出进度。
/// 这条顺序以前只有把界面跑起来、一路点下去才能验证，而它恰好决定了
/// 「会不会悄悄覆盖上一次的导出」——所以它值得能单独断言。
///
/// 行为与搬之前逐行对齐：取消导出提示「导出已取消」，其它异常提示失败并落错误日志。
/// </summary>
internal sealed class CharacterExportController(
    ICharacterDeskExportHost host,
    CharacterDeskViewModel desk)
{
    private readonly ICharacterDeskExportHost _host = host;
    private readonly CharacterDeskViewModel _desk = desk;

    public async Task ExportAsync(CharacterCard? character)
    {
        if (character is null)
        {
            return;
        }

        var defaultExportRoot = _desk.GetDefaultExportRootPath(_host.ProjectRootPath);
        var exportRoot = await _host.ShowExportLocationDialogAsync(character, defaultExportRoot);
        if (string.IsNullOrWhiteSpace(exportRoot))
        {
            return;
        }

        var targetPath = Path.Combine(exportRoot, character.Code);
        var overwrite = Directory.Exists(targetPath);
        if (overwrite && !await _host.ConfirmOverwriteAsync(character, targetPath))
        {
            return;
        }

        try
        {
            var exportedPath = await _host.ShowExportProgressAsync(character, exportRoot, overwrite);
            _host.ShowFloatingTip(NotifySeverity.Success, "角色已导出", exportedPath);
            _host.AppendLog(
                LogKind.User,
                $"导出完整角色文件夹：{character.Name} / {character.Code} -> {exportedPath}");
        }
        catch (OperationCanceledException)
        {
            _host.ShowFloatingTip(NotifySeverity.Warning, "导出已取消", character.Name);
        }
        catch (Exception ex)
        {
            _host.ShowFloatingTip(NotifySeverity.Error, "导出角色失败", ex.Message);
            _host.AppendLog(LogKind.Error, "导出完整角色文件夹失败。", ex);
        }
    }
}
