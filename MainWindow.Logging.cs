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
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.System;
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
        private void RegisterSettingsShortcuts()
        {
            var undoSettingsAccelerator = new KeyboardAccelerator
            {
                Key = VirtualKey.Z,
                Modifiers = VirtualKeyModifiers.Control
            };
            undoSettingsAccelerator.Invoked += SettingsUndoKeyboardAccelerator_Invoked;
            RootGrid.KeyboardAccelerators.Add(undoSettingsAccelerator);
        }

        private void SettingsUndoKeyboardAccelerator_Invoked(
            KeyboardAccelerator sender,
            KeyboardAcceleratorInvokedEventArgs args)
        {
            if (SettingsPage.Visibility != Visibility.Visible ||
                !Settings.UndoLastSettingCommand.CanExecute(null))
            {
                return;
            }

            Settings.UndoLastSettingCommand.Execute(null);
            UpdateLogOptionEnabledState();
            UpdateAuxiliaryDisplayVisibility();
            AppendLog(LogKind.User, "已通过 Ctrl+Z 撤回上一次设置修改。");
            args.Handled = true;
        }

        private void UpdateLogOptionEnabledState()
        {
            var enabled = Settings.LogEnabled;
            LogUserOperationsCheckBox.IsEnabled = enabled;
            LogWarningCheckBox.IsEnabled = enabled;
            LogErrorCheckBox.IsEnabled = enabled;
        }

        private void UpdateAuxiliaryDisplayVisibility()
        {
            WorkspaceStatusBorder.Visibility = Settings.ShowWorkspacePath
                ? Visibility.Visible
                : Visibility.Collapsed;
            LogPanelBorder.Visibility = Settings.LogEnabled
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void LogSettingControl_Changed(object sender, RoutedEventArgs e)
        {
            UpdateLogOptionEnabledState();
            UpdateAuxiliaryDisplayVisibility();
        }

        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            LogItemsControl.Items.Clear();
            AppendLog(LogKind.User, "已清空输出日志。");
        }

        private async void ShowLogHelpButton_Click(object sender, RoutedEventArgs e)
        {
            var content = new ScrollViewer
            {
                MaxHeight = 520,
                Content = new StackPanel
                {
                    Spacing = 14,
                    Children =
                    {
                        CreateHelpText("辅助显示", "这里控制底部工作区路径和输出日志是否显示。设置会立即保存，后续功能也要遵守这些开关。"),
                        CreateHelpText("输出日志", "log 用于记录用户操作、提示和错误。关闭 log 功能后，底部日志面板会隐藏，并停止写入新日志。"),
                        CreateHelpText("撤回设置", "在设置页按 Ctrl+Z 可撤回最近一次设置开关修改，例如误关了日志或工作区路径。它不用于目录迁移、文件导入、删除、同步等素材操作。"),
                        CreateHelpText("后续功能", "动作帧导入、线稿生成、批量导出和 Unreal 同步都要把关键步骤写入 log，并在长任务时走底部全局进度条。")
                    }
                }
            };

            await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "辅助显示说明",
                content,
                PrimaryButtonText: "关闭",
                CloseButtonText: string.Empty,
                DefaultButton: ContentDialogButton.Primary,
                ConfigureDialog: dialog =>
                {
                    dialog.MinWidth = 610;
                    dialog.MaxWidth = 610;
                }));
        }

        private static TextBlock CreateHelpText(string title, string message)
        {
            return new TextBlock
            {
                Text = $"{title}\n{message}",
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true
            };
        }

        private bool ShouldWriteLog(LogKind kind)
        {
            return Settings.ShouldWriteLog(kind);
        }

        private void AppendLog(LogKind kind, string message, Exception? exception = null)
        {
            if (!ShouldWriteLog(kind))
            {
                return;
            }

            var text = $"[{DateTime.Now:HH:mm:ss}] [{GetLogKindLabel(kind)}] {message}";
            if (exception is not null)
            {
                text += $" 原因：{exception.Message}";
            }

            LogItemsControl.Items.Add(new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Consolas"),
                Margin = new Thickness(0, 0, 0, 4),
                Foreground = GetLogBrush(kind)
            });

            const int maxLogCount = 300;
            while (LogItemsControl.Items.Count > maxLogCount)
            {
                LogItemsControl.Items.RemoveAt(0);
            }

            LogScrollViewer.UpdateLayout();
            LogScrollViewer.ChangeView(null, LogScrollViewer.ScrollableHeight, null);
        }

        private static string GetLogKindLabel(LogKind kind)
        {
            return kind switch
            {
                LogKind.User => "用户",
                LogKind.Warning => "提示",
                LogKind.Error => "错误",
                _ => "信息"
            };
        }

        private static Brush? GetLogBrush(LogKind kind)
        {
            return kind switch
            {
                LogKind.User => new SolidColorBrush(Colors.ForestGreen),
                LogKind.Warning => new SolidColorBrush(Colors.DarkOrange),
                LogKind.Error => new SolidColorBrush(Colors.Firebrick),
                _ => null
            };
        }
    }
}
