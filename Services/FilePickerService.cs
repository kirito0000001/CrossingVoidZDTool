using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 选文件 / 选文件夹的唯一入口（P4 的前置抽象之一）。
///
/// 以前「建 picker → 加过滤器 → <c>InitializeWithWindow.Initialize(picker, hwnd)</c>
/// → 取结果」这一套在九个地方各写一遍。漏掉 <c>Initialize</c> 那一行不会编译报错，
/// 只会在运行时直接抛异常——非打包 WinUI 窗口必须显式绑 HWND。
/// 收成一处之后，这类错误最多只会犯一次。
/// </summary>
internal interface IFilePickerService
{
    Task<string?> PickSingleFileAsync(PickerLocationId startLocation, params string[] fileTypeFilters);

    /// <summary>
    /// 挑一个文件，并且**从指定目录开**（<paramref name="startFolderPath"/>）。
    ///
    /// 上面那个重载只能给 <see cref="PickerLocationId"/> 这种"已知位置"（文档、图片 …），
    /// 而工具箱的落点全在工作区里的自定路径上（某个动作的底板目录、图集目录…）。
    /// 这里用 WinAppSDK 2.0 的 <c>Microsoft.Windows.Storage.Pickers.FileOpenPicker</c>：
    /// 它的 <c>SuggestedFolder</c> **每次打开都生效**，不会被"上次挑过的目录"顶掉
    /// —— 那个能记一次的 <c>SuggestedStartFolder</c> 不行，用户挑过一次目录它就失效了。
    /// </summary>
    Task<string?> PickSingleFileAsync(string startFolderPath, string title, params string[] fileTypeFilters);

    Task<IReadOnlyList<string>> PickMultipleFilesAsync(PickerLocationId startLocation, params string[] fileTypeFilters);

    Task<string?> PickFolderAsync(PickerLocationId startLocation = PickerLocationId.ComputerFolder);
}

/// <summary>WinUI 实现。窗口句柄用回调取，免得服务自己认识 MainWindow。</summary>
internal sealed class WinUiFilePickerService(Func<IntPtr> windowHandleProvider) : IFilePickerService
{
    private readonly Func<IntPtr> _windowHandleProvider = windowHandleProvider;

    public async Task<string?> PickSingleFileAsync(PickerLocationId startLocation, params string[] fileTypeFilters)
    {
        var picker = CreateOpenPicker(startLocation, fileTypeFilters);
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public async Task<string?> PickSingleFileAsync(
        string startFolderPath,
        string title,
        params string[] fileTypeFilters)
    {
        // 命名空间里有个同名的经典 FileOpenPicker（上面 using Windows.Storage.Pickers），
        // 所以新的这个得写全名 —— 两者并存不冲突，经典那个继续服务"已知位置"那些调用点。
        // 窗口绑定也从 InitializeWithWindow 换成了构造时传 WindowId。
        var picker = new Microsoft.Windows.Storage.Pickers.FileOpenPicker(
            Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_windowHandleProvider()));
        if (!string.IsNullOrWhiteSpace(startFolderPath))
        {
            picker.SuggestedFolder = startFolderPath;
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            picker.Title = title;
        }

        foreach (var filter in fileTypeFilters)
        {
            picker.FileTypeFilter.Add(filter);
        }

        // 取消 / 直接关掉选择框时回来的是 null，交调用方决定"什么都不做"。
        var result = await picker.PickSingleFileAsync();
        return result?.Path;
    }

    public async Task<IReadOnlyList<string>> PickMultipleFilesAsync(
        PickerLocationId startLocation,
        params string[] fileTypeFilters)
    {
        var picker = CreateOpenPicker(startLocation, fileTypeFilters);
        var files = await picker.PickMultipleFilesAsync();
        return files.Select(file => file.Path).ToArray();
    }

    public async Task<string?> PickFolderAsync(PickerLocationId startLocation = PickerLocationId.ComputerFolder)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = startLocation,
        };
        picker.FileTypeFilter.Add("*");
        AttachToWindow(picker);
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private FileOpenPicker CreateOpenPicker(PickerLocationId startLocation, string[] fileTypeFilters)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = startLocation,
        };
        foreach (var filter in fileTypeFilters)
        {
            picker.FileTypeFilter.Add(filter);
        }

        AttachToWindow(picker);
        return picker;
    }

    private void AttachToWindow(object picker) =>
        InitializeWithWindow.Initialize(picker, _windowHandleProvider());
}
