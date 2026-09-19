using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;
using Windows.Storage.Pickers;

namespace CrossingVoidZDTool.ViewModels;

/// <summary>
/// 参考图导入流程要界面提供的东西（C1）。
/// 四件事都是壳的：记「最后编辑的模块」、持久化当前角色选择、提示、写用户操作。
/// </summary>
internal interface ICharacterDeskReferenceImageHost
{
    void MarkLastEditedModule(string moduleTag);

    void PersistCurrentCharacterSelection();

    void ShowFloatingTip(NotifySeverity severity, string title, string message);

    void LogUserOperation(string action);
}

/// <summary>
/// 「导入草稿参考图」这条流程（角色台 / St1-设计理念）。
///
/// 从 MainWindow 里搬出来的是**两个入口共用的那一段**：按钮点击和拖放
/// 本来就是同一件事（选文件 vs 拿到路径），以前各自写一遍守卫、各自调私有的
/// <c>ImportReferenceImagesAsync</c>；现在守卫、成功提示、失败落日志都只有一处，
/// 也第一次变得能测——以前只有把界面跑起来、点一次导入或拖一次文件才能验证。
///
/// 行为与搬之前逐行对齐：只看「有没有当前角色 / 是不是只读」，空列表直接返回，
/// 失败只改状态栏并落一条错误日志（不弹提示）——保持原样，不在解耦批次里顺手改行为。
/// </summary>
internal sealed class CharacterDeskReferenceImageImportController(
    ICharacterDeskReferenceImageHost host,
    CharacterDeskViewModel desk,
    IFilePickerService filePicker)
{
    private readonly ICharacterDeskReferenceImageHost _host = host;
    private readonly CharacterDeskViewModel _desk = desk;
    private readonly IFilePickerService _filePicker = filePicker;

    /// <summary>参考图编辑区支持的图片格式（和 B1 的选文件服务组合使用）。</summary>
    private static readonly string[] ImageFilters = [".png", ".jpg", ".jpeg", ".webp", ".bmp"];

    /// <summary>按钮入口：让用户挑图片，然后交给 <see cref="ImportPathsAsync"/>。</summary>
    public async Task ImportPickedAsync()
    {
        if (!CanImport())
        {
            return;
        }

        var paths = (await _filePicker.PickMultipleFilesAsync(
                PickerLocationId.PicturesLibrary, ImageFilters))
            .Where(File.Exists)
            .ToList();
        await ImportPathsAsync(paths);
    }

    /// <summary>拖放入口：路径已经拿到，直接走同一条导入路径。</summary>
    public async Task ImportPathsAsync(IReadOnlyList<string> paths)
    {
        if (!CanImport() || paths.Count == 0)
        {
            return;
        }

        try
        {
            await _desk.ImportReferenceImagesAsync(paths);
            _host.MarkLastEditedModule("ActionFrames");
            _host.PersistCurrentCharacterSelection();
            _host.ShowFloatingTip(NotifySeverity.Success, "参考图已导入", $"{paths.Count} 个文件");
            _host.LogUserOperation($"导入草稿参考图：{paths.Count} 个文件。");
        }
        catch (Exception ex)
        {
            // 只读/无角色时不该走到这里；真失败了就把原因留在状态栏和日志里。
            _desk.StatusText = $"导入参考图失败：{ex.Message}";
            ToolboxLog.Error("导入草稿参考图失败。", ex);
        }
    }

    private bool CanImport() => _desk.CurrentCharacter is not null && !_desk.IsViewOnly;
}
