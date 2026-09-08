using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;
using CrossingVoidZDTool.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Input;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CrossingVoidZDTool
{
    /// <summary>第一步「底层检测」：检查 Unreal 目录和角色 Item 是否符合规范。</summary>
    public sealed partial class MainWindow
    {
        private void UnrealFoundationCheckItem_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: UnrealPublishFoundationCheckItem item } ||
                string.IsNullOrWhiteSpace(item.CorrectName))
            {
                return;
            }

            CopyTextToClipboard(item.CorrectName);
            ShowFloatingTip(InfoBarSeverity.Success, "已复制正确名称", item.CorrectName);
        }
    }
}
