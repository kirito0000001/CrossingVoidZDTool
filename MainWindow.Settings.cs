using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;
using CrossingVoidZDTool.ViewModels;
using CrossingVoidZDTool.Views;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Microsoft.UI.Dispatching;
using WinRT.Interop;
using PathFigure = Microsoft.UI.Xaml.Media.PathFigure;

namespace CrossingVoidZDTool
{
    public sealed partial class MainWindow
    {
        private async void ChooseProjectRootButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.ComputerFolder
            };
            picker.FileTypeFilter.Add("*");

            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            var selectedFolder = await picker.PickSingleFolderAsync();
            if (selectedFolder is null)
            {
                return;
            }

            AppendLog(LogKind.User, $"选择新的整体项目父目录：{selectedFolder.Path}");
            var newProjectRootPath = Settings.BuildProjectRootPathFromParent(selectedFolder.Path);
            var oldProjectRootPath = Path.GetFullPath(Settings.ProjectRootPath);

            if (Settings.IsCurrentProjectRoot(newProjectRootPath))
            {
                Settings.SetProjectRootStatus(InfoBarSeverity.Informational, "目录未变化", $"当前已经在使用：{newProjectRootPath}");
                return;
            }

            if (Settings.IsCandidateInsideCurrentRoot(newProjectRootPath))
            {
                Settings.SetProjectRootStatus(InfoBarSeverity.Error, "无法迁移目录", "新位置不能放在旧项目总目录里面，否则迁移完成后删除旧目录时会连新目录一起删除。");
                return;
            }

            try
            {
                Settings.SetProjectRootStatus(InfoBarSeverity.Informational, "正在迁移目录", $"{oldProjectRootPath} -> {newProjectRootPath}");
                AppendLog(LogKind.Info, $"开始迁移整体项目目录：{oldProjectRootPath} -> {newProjectRootPath}");
                ShowGlobalProgress("迁移整体项目目录", newProjectRootPath);
                UpdateGlobalProgress("正在复制和校验项目文件...", 5, $"{oldProjectRootPath} -> {newProjectRootPath}");
                var progress = new Progress<ProgressUpdate>(update =>
                    UpdateGlobalProgress(update.Message, update.Percent, update.Detail, update.IsIndeterminate));
                var result = await Settings.ChangeProjectRootAsync(
                    newProjectRootPath,
                    progress,
                    GetGlobalProgressCancellationToken());

                CompleteGlobalProgress("目录迁移完成", $"已迁移 {result.FileCount} 个文件、{result.DirectoryCount} 个文件夹");
                await HideGlobalProgressAfterDelayAsync();
                Settings.SetProjectRootStatus(InfoBarSeverity.Success, "目录迁移完成", $"已迁移并校验 {result.FileCount} 个文件、{result.DirectoryCount} 个文件夹。旧目录已删除：{oldProjectRootPath}");
                _applicationViewModel.CharacterDesk.StatusText = Settings.WorkspaceStatusText;
                await LoadCharacterCardsAsync();
                AppendLog(LogKind.User, $"整体项目目录迁移完成：{newProjectRootPath}");
            }
            catch (Exception ex)
            {
                CompleteGlobalProgress(ex is OperationCanceledException ? "目录迁移已取消" : "目录迁移失败", ex.Message);
                await HideGlobalProgressAfterDelayAsync();
                Settings.EnsureCurrentProjectRoot();
                Settings.SetProjectRootStatus(InfoBarSeverity.Error, "目录迁移失败", $"已保留原目录和设置，未删除旧目录。错误：{ex.Message}");
                _applicationViewModel.CharacterDesk.StatusText = Settings.WorkspaceStatusText;
                await LoadCharacterCardsAsync();
                AppendLog(LogKind.Error, "整体项目目录迁移失败。", ex);
            }
        }

        private void ApplyThemeSettings()
        {
            RootGrid.RequestedTheme = Settings.NightModeEnabled
                ? ElementTheme.Dark
                : ElementTheme.Light;
        }

    }
}
