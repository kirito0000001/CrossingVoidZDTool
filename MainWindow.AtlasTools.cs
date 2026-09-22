using System;
using System.IO;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services.Atlas;
using CrossingVoidZDTool.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace CrossingVoidZDTool
{
    public sealed partial class MainWindow : IAtlasToolHost
    {
        /// <summary>工具集页面：左边工具清单、右边参数与运行。</summary>
        private void ShowAtlasToolsPage()
        {
            ShowOnlyPage(AtlasToolsPage);
            SelectShellNavigationItem(AtlasToolsNavItem);
        }

        /// <summary>给界面冒烟用：直接切到工具集页（和 St5 的那个入口同一形状）。</summary>
        internal void UiSmokeShowAtlasToolsPage() => ShowAtlasToolsPage();

        /// <summary>跑「创建图集」：任意目录的 PNG → 图集 + 坐标 json，落到用户选的目录。</summary>
        async Task<AtlasCreateResult?> IAtlasToolHost.RunCreateAsync(AtlasCreateRequest request)
        {
            ShowGlobalProgress("创建图集", request.AtlasName);
            try
            {
                var progress = new Progress<AtlasPackProgress>(update =>
                    UpdateGlobalProgress(update.Message, 0, request.AtlasName, update.Stage != AtlasPackStage.Finished));
                var result = await new AtlasFolderPackService().PackAsync(
                    request,
                    Settings.AtlasPythonPath,
                    progress,
                    GetGlobalProgressCancellationToken());

                CompleteGlobalProgress($"图集已生成：{result.FrameCount} 帧", result.OutputDirectory);
                await HideGlobalProgressAfterDelayAsync(900);
                AppendLog(LogKind.User,
                    $"创建图集：{Path.GetFileName(result.AtlasImagePath)} ← {request.SourceFolderPath}"
                    + $"（{result.FrameCount} 帧，{result.SizeText}）→ {result.OutputDirectory}");
                try
                {
                    OpenFolderInExplorer(result.OutputDirectory);
                }
                catch (Exception openError)
                {
                    AppendLog(LogKind.Warning, "图集已生成，但没能自动打开输出目录。", openError);
                }

                return result;
            }
            catch (OperationCanceledException)
            {
                CompleteGlobalProgress("创建图集已取消", request.AtlasName);
                await HideGlobalProgressAfterDelayAsync();
                return null;
            }
            catch (Exception ex)
            {
                CompleteGlobalProgress("创建图集失败", ex.Message);
                await HideGlobalProgressAfterDelayAsync();
                ShowFloatingTip(InfoBarSeverity.Error, "创建图集失败", ex.Message);
                AppendLog(LogKind.Error, "创建图集失败。", ex);
                return null;
            }
        }

        /// <summary>跑「拆分图集」：图集 + 坐标 json → 一张张 PNG。</summary>
        async Task<AtlasExtractResult?> IAtlasToolHost.RunExtractAsync(AtlasExtractRequest request)
        {
            ShowGlobalProgress("拆分图集", Path.GetFileName(request.AtlasImagePath));
            try
            {
                var result = await Task.Run(() => new AtlasExtractService().Extract(request));
                CompleteGlobalProgress($"已拆出 {result.Frames.Count} 张", result.OutputDirectory);
                await HideGlobalProgressAfterDelayAsync(900);
                AppendLog(LogKind.User,
                    $"拆分图集：{Path.GetFileName(request.AtlasImagePath)} → {result.Frames.Count} 张"
                    + (result.PaddedToCanvasCount > 0 ? $"（{result.PaddedToCanvasCount} 张贴回原画布）" : string.Empty)
                    + $"，{result.OutputDirectory}");
                try
                {
                    OpenFolderInExplorer(result.OutputDirectory);
                }
                catch (Exception openError)
                {
                    AppendLog(LogKind.Warning, "图集已拆出，但没能自动打开输出目录。", openError);
                }

                return result;
            }
            catch (Exception ex)
            {
                CompleteGlobalProgress("拆分图集失败", ex.Message);
                await HideGlobalProgressAfterDelayAsync();
                ShowFloatingTip(InfoBarSeverity.Error, "拆分图集失败", ex.Message);
                AppendLog(LogKind.Error, "拆分图集失败。", ex);
                return null;
            }
        }

        Task<string?> IAtlasToolHost.PickFolderAsync(string title) =>
            _filePickerService.PickFolderAsync(PickerLocationId.ComputerFolder);

        Task<string?> IAtlasToolHost.PickFileAsync(string title, string fileTypeFilter) =>
            _filePickerService.PickSingleFileAsync(PickerLocationId.ComputerFolder, fileTypeFilter);

        void IAtlasToolHost.OpenFolder(string folderPath) => OpenFolderInExplorer(folderPath);
    }
}
