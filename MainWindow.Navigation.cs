using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;
using CrossingVoidZDTool.ViewModels;
using CrossingVoidZDTool.Views;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
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
        private void ShellNavigation_Loaded(object sender, RoutedEventArgs e)
        {
            var menuScrollViewer = FindNamedDescendant<ScrollViewer>(
                ShellNavigation,
                "MenuItemsScrollViewer");
            if (menuScrollViewer is null)
            {
                return;
            }

            menuScrollViewer.VerticalScrollMode = ScrollMode.Enabled;
            menuScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            menuScrollViewer.IsVerticalRailEnabled = true;
        }

        private static T? FindNamedDescendant<T>(DependencyObject root, string name)
            where T : FrameworkElement
        {
            var childCount = VisualTreeHelper.GetChildrenCount(root);
            for (var index = 0; index < childCount; index++)
            {
                var child = VisualTreeHelper.GetChild(root, index);
                if (child is T element && string.Equals(element.Name, name, StringComparison.Ordinal))
                {
                    return element;
                }

                var match = FindNamedDescendant<T>(child, name);
                if (match is not null)
                {
                    return match;
                }
            }

            return null;
        }

        private void ShellNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag)
            {
                return;
            }

            if (_isChangingShellSelectionInternally)
            {
                return;
            }

            if (string.Equals(tag, "Settings", StringComparison.Ordinal))
            {
                ShowSettingsPage();
                return;
            }

            if (string.Equals(tag, "ActionFrames", StringComparison.Ordinal))
            {
                ShowSt1DesignPage();
                return;
            }

            if (string.Equals(tag, "LineArt", StringComparison.Ordinal))
            {
                ShowSt2MaterialPage();
                return;
            }

            if (string.Equals(tag, "UnrealSync", StringComparison.Ordinal))
            {
                ShowSt3CharacterInfoPage();
                return;
            }

            if (string.Equals(tag, "Skills", StringComparison.Ordinal))
            {
                ShowSt4SkillsPage();
                return;
            }

            if (string.Equals(tag, "SequenceFrames", StringComparison.Ordinal))
            {
                ShowSt5SequenceFramesPage();
                return;
            }

            if (string.Equals(tag, "Buffs", StringComparison.Ordinal))
            {
                ShowSt6BuffsPage();
                return;
            }

            if (string.Equals(tag, "UnrealProjectSync", StringComparison.Ordinal))
            {
                ShowUnrealProjectSyncPage();
                return;
            }

            ShowCharacterDeskPage();
        }

        private void ShowCharacterDeskPage()
        {
            CharacterDesk.SetViewOnly(false);
            ShowOnlyPage(CharacterDeskPage);
            SelectShellNavigationItem(CharacterDeskNavItem);
        }

        private void ShowSettingsPage()
        {
            ShowOnlyPage(SettingsPage);
            SelectShellNavigationItem(GlobalSettingsNavItem);
        }

        private void ShowSt1DesignPage()
        {
            ShowOnlyPage(ActionFramesPage);
            SelectShellNavigationItem(St1DesignNavItem);
        }

        private void ShowSt2MaterialPage()
        {
            if (!TryEnterCharacterEditingPage())
            {
                return;
            }

            ShowOnlyPage(LineArtPage);
            SelectShellNavigationItem(St2MaterialNavItem);
            StartBaseMaterialWatcher();
            _ = RestorePageScrollPositionAfterAsync(LineArtPage, RefreshBaseMaterialsWithFeedbackAsync());
        }

        private void ShowSt3CharacterInfoPage()
        {
            if (!TryEnterCharacterEditingPage())
            {
                return;
            }

            ShowOnlyPage(UnrealSyncPage);
            SelectShellNavigationItem(St3CharacterInfoNavItem);
            _ = RestorePageScrollPositionAfterAsync(UnrealSyncPage, RefreshCharacterInfoWithFeedbackAsync());
        }

        private void ShowSt4SkillsPage()
        {
            if (!TryEnterCharacterEditingPage())
            {
                return;
            }

            ShowOnlyPage(SkillsPage);
            SelectShellNavigationItem(St4SkillsNavItem);
            _ = RestorePageScrollPositionAfterAsync(SkillsPage, RefreshSkillsWithFeedbackAsync());
        }

        private void ShowSt5SequenceFramesPage()
        {
            if (!TryEnterCharacterEditingPage())
            {
                return;
            }

            ShowOnlyPage(SequenceFramesPage);
            SelectShellNavigationItem(St5SequenceFramesNavItem);
            MarkLastEditedModule("SequenceFrames");
            _ = RestorePageScrollPositionAfterAsync(SequenceFramesPage, RefreshSequenceFramesWithFeedbackAsync());
        }

        private void ShowSt6BuffsPage()
        {
            if (!TryEnterCharacterEditingPage())
            {
                return;
            }

            ShowOnlyPage(BuffsPage);
            SelectShellNavigationItem(St6BuffsNavItem);
            _ = RestorePageScrollPositionAfterAsync(BuffsPage, RefreshBuffsWithFeedbackAsync());
            RefreshProductionStatusWithFeedback();
        }

        private bool TryEnterCharacterEditingPage()
        {
            if (CharacterDesk.IsViewOnly)
            {
                return true;
            }

            if (CharacterDesk.CurrentCharacter?.IsCompleted != true)
            {
                return true;
            }

            // 已完成角色默认进入只读查看，不再用锁定提示阻断浏览。
            CharacterDesk.SetViewOnly(true);
            AppendLog(LogKind.Info, $"已进入完成角色只读查看：{CharacterDesk.CurrentCharacter.Name} / {CharacterDesk.CurrentCharacter.Code}");
            return true;
        }

        private void ShowUnrealProjectSyncPage()
        {
            ShowOnlyPage(UnrealProjectSyncPage);
            SelectShellNavigationItem(UnrealProjectSyncNavItem);
            _applicationViewModel.UnrealProjectSync.Load(Settings.UnrealEnginePath, Settings.UnrealProjectPath);
            var preferredCode = CharacterDesk.CurrentCharacter?.IsCompleted == true
                ? CharacterDesk.CurrentCharacter.Code
                : CharacterDesk.LastEditedCharacter?.IsCompleted == true
                    ? CharacterDesk.LastEditedCharacter.Code
                    : null;
            var restoreResult = _applicationViewModel.UnrealProjectSync.RefreshDraftSources(
                CharacterDesk.CompletedCharacters,
                preferredCode);
            if (restoreResult.Status == UnrealSyncSessionCacheLoadStatus.Invalid)
            {
                var exception = new InvalidDataException(restoreResult.ErrorMessage);
                AppendLog(LogKind.Error, "无法恢复虚幻同步进度，将忽略旧状态并返回底层检测。", exception);
                ShowFloatingTip(InfoBarSeverity.Warning, "无法恢复同步进度", restoreResult.ErrorMessage);
            }

            // 恢复页面只恢复缓存，不在启动阶段触发任何 Unreal 检测；进入新步骤或点击
            // 该步骤自己的重新加载按钮时，才执行对应范围的检测。
            AppendLog(LogKind.Info, "已恢复虚幻同步台状态；进入步骤或手动重新加载时才执行检测。");
        }

        private string? GetLastEditedModuleTag()
        {
            return _applicationViewModel.UserOperations.LastOperationModuleTag ?? Settings.LastEditedModuleTag;
        }

        private void ShowLastEditedPage(string? tag = null)
        {
            tag ??= GetLastEditedModuleTag();
            if (string.Equals(tag, "LineArt", StringComparison.Ordinal))
            {
                ShowSt2MaterialPage();
                return;
            }

            if (string.Equals(tag, "UnrealSync", StringComparison.Ordinal))
            {
                ShowSt3CharacterInfoPage();
                return;
            }

            if (string.Equals(tag, "Skills", StringComparison.Ordinal))
            {
                ShowSt4SkillsPage();
                return;
            }

            if (string.Equals(tag, "SequenceFrames", StringComparison.Ordinal))
            {
                ShowSt5SequenceFramesPage();
                return;
            }

            if (string.Equals(tag, "Buffs", StringComparison.Ordinal))
            {
                ShowSt6BuffsPage();
                return;
            }

            if (string.Equals(tag, "UnrealProjectSync", StringComparison.Ordinal))
            {
                ShowUnrealProjectSyncPage();
                return;
            }

            if (string.Equals(tag, "CharacterDesk", StringComparison.Ordinal))
            {
                ShowCharacterDeskPage();
                return;
            }

            ShowSt1DesignPage();
        }

        private void ShowPlaceholderPage(FrameworkElement page)
        {
            ShowOnlyPage(page);
        }

        private void ShowOnlyPage(FrameworkElement visiblePage)
        {
            if (!ReferenceEquals(visiblePage, LineArtPage))
            {
                StopVoicePlayback();
            }

            FrameworkElement[] pages =
            [
                CharacterDeskPage,
                ActionFramesPage,
                LineArtPage,
                UnrealSyncPage,
                SkillsPage,
                SequenceFramesPage,
                BuffsPage,
                UnrealProjectSyncPage,
                SettingsPage
            ];

            SaveVisiblePageScrollPosition(pages);

            foreach (var page in pages)
            {
                page.Visibility = ReferenceEquals(page, visiblePage)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            ProductionStatusBorder.Visibility = ReferenceEquals(visiblePage, BuffsPage)
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (!ReferenceEquals(visiblePage, LineArtPage))
            {
                StopBaseMaterialWatcher();
            }

            PlayPageEntrance(visiblePage);
            QueuePageScrollPositionRestore(visiblePage);
        }

        private void SaveVisiblePageScrollPosition(IReadOnlyList<FrameworkElement> pages)
        {
            var visiblePage = pages.FirstOrDefault(page => page.Visibility == Visibility.Visible);
            var scrollContext = visiblePage is null ? null : GetPageScrollContext(visiblePage);
            if (scrollContext is not null)
            {
                _pageScrollPositions.Save(scrollContext.Value.PageKey, scrollContext.Value.ScrollViewer.VerticalOffset);
            }
        }

        private async Task RestorePageScrollPositionAfterAsync(FrameworkElement page, Task refreshTask)
        {
            await refreshTask;
            if (page.Visibility == Visibility.Visible)
            {
                QueuePageScrollPositionRestore(page);
            }
        }

        private async Task RunWithPageScrollPositionPreservedAsync(FrameworkElement page, Func<Task> operation)
        {
            var scrollContext = GetPageScrollContext(page);
            if (scrollContext is null)
            {
                await operation();
                return;
            }

            _pageScrollPositions.Save(
                scrollContext.Value.PageKey,
                scrollContext.Value.ScrollViewer.VerticalOffset);

            try
            {
                await operation();
            }
            finally
            {
                if (page.Visibility == Visibility.Visible)
                {
                    QueuePageScrollPositionRestore(page);
                }
            }
        }

        private void QueuePageScrollPositionRestore(FrameworkElement page)
        {
            var scrollContext = GetPageScrollContext(page);
            if (scrollContext is null)
            {
                return;
            }

            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (page.Visibility != Visibility.Visible)
                {
                    return;
                }

                var context = GetPageScrollContext(page);
                if (context is null)
                {
                    return;
                }

                context.Value.ScrollViewer.UpdateLayout();
                var targetOffset = Math.Min(
                    _pageScrollPositions.Get(context.Value.PageKey),
                    context.Value.ScrollViewer.ScrollableHeight);
                context.Value.ScrollViewer.ChangeView(null, targetOffset, null, disableAnimation: true);
            });
        }

        private (string PageKey, ScrollViewer ScrollViewer)? GetPageScrollContext(FrameworkElement page)
        {
            if (ReferenceEquals(page, LineArtPage))
            {
                return ("LineArt", LineArtScrollViewer);
            }

            if (ReferenceEquals(page, UnrealSyncPage))
            {
                return ("UnrealSync", UnrealSyncScrollViewer);
            }

            if (ReferenceEquals(page, SkillsPage))
            {
                return ("Skills", SkillsScrollViewer);
            }

            if (ReferenceEquals(page, SequenceFramesPage))
            {
                return ("SequenceFrames", SequenceFramesScrollViewer);
            }

            if (ReferenceEquals(page, BuffsPage))
            {
                return ("Buffs", BuffsScrollViewer);
            }

            if (ReferenceEquals(page, UnrealProjectSyncPage))
            {
                return ("UnrealProjectSync", UnrealProjectSyncPage);
            }

            if (ReferenceEquals(page, SettingsPage))
            {
                return ("Settings", SettingsPage);
            }

            return null;
        }

        private void PageBlankArea_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if (!IsBlankAreaRightTap(sender, e.OriginalSource))
            {
                return;
            }

            if (CloseTopTransientLayer())
            {
                e.Handled = true;
                return;
            }

            if (ActionFramesPage.Visibility == Visibility.Visible && CharacterDesk.IsDraftOpen)
            {
                ExitDraftToPortraitEntry();
                e.Handled = true;
                return;
            }
        }

        private bool CloseTopTransientLayer()
        {
            if (CharacterDetailHost.Visibility == Visibility.Visible)
            {
                HideCharacterDetail();
                return true;
            }

            if (BaseMaterialCropHost.Visibility == Visibility.Visible)
            {
                CompleteBaseMaterialCrop(null);
                return true;
            }

            if (ReferenceImageViewerHost.Visibility == Visibility.Visible)
            {
                CloseReferenceImageViewer();
                return true;
            }

            if (SkillIconPickerHost.Visibility == Visibility.Visible)
            {
                HideSkillIconPicker();
                return true;
            }

            if (DraftOverlayHost.Visibility == Visibility.Visible)
            {
                HideDraftOverlay();
                return true;
            }

            if (BuffEditorHost.Visibility == Visibility.Visible)
            {
                CloseBuffEditor();
                return true;
            }

            return false;
        }

        private static bool IsBlankAreaRightTap(object sender, object originalSource)
        {
            if (sender is not DependencyObject pageRoot || originalSource is not DependencyObject source)
            {
                return false;
            }

            for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
            {
                if (ReferenceEquals(current, pageRoot))
                {
                    return true;
                }

                if (IsInteractiveRightTapSource(current))
                {
                    return false;
                }
            }

            return false;
        }

        private static bool IsInteractiveRightTapSource(DependencyObject source)
        {
            return source is ButtonBase or TextBox or NumberBox or PasswordBox or RichEditBox or ComboBox or ToggleSwitch or CheckBox or RadioButton or Slider or ListViewBase or GridViewItem or ListViewItem or SelectorItem or MenuFlyoutItem or AppBarButton or HyperlinkButton or Expander or InfoBar or Image or ScrollBar;
        }

        private void SelectShellNavigationItem(NavigationViewItem item)
        {
            _isChangingShellSelectionInternally = true;
            try
            {
                ShellNavigation.SelectedItem = item;
                item.IsSelected = true;
            }
            finally
            {
                _isChangingShellSelectionInternally = false;
            }
        }

        private static void PlayPageEntrance(FrameworkElement page)
        {
            if (page.Visibility != Visibility.Visible)
            {
                return;
            }

            page.Transitions = null;
            page.Resources["PageEntranceStoryboard"] = null;

            if (page.RenderTransform is not TranslateTransform transform)
            {
                transform = new TranslateTransform();
                page.RenderTransform = transform;
            }

            transform.X = PageEntranceOffsetX;
            transform.Y = 0;
            page.Opacity = 0;

            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            var slideAnimation = new DoubleAnimation
            {
                From = PageEntranceOffsetX,
                To = 0,
                Duration = PageEntranceDuration,
                EasingFunction = easing
            };
            Storyboard.SetTarget(slideAnimation, transform);
            Storyboard.SetTargetProperty(slideAnimation, nameof(TranslateTransform.X));

            var fadeAnimation = new DoubleAnimation
            {
                From = 0.82,
                To = 1,
                Duration = PageEntranceDuration,
                EasingFunction = easing
            };
            Storyboard.SetTarget(fadeAnimation, page);
            Storyboard.SetTargetProperty(fadeAnimation, nameof(UIElement.Opacity));

            var storyboard = new Storyboard();
            storyboard.Children.Add(slideAnimation);
            storyboard.Children.Add(fadeAnimation);
            storyboard.Completed += (_, _) =>
            {
                transform.X = 0;
                transform.Y = 0;
                page.Opacity = 1;
                page.Resources.Remove("PageEntranceStoryboard");
            };
            page.Resources["PageEntranceStoryboard"] = storyboard;
            storyboard.Begin();
        }

    }
}
