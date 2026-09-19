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
