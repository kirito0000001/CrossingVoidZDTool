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
            ShowOnlyPage(LineArtPage);
            SelectShellNavigationItem(St2MaterialNavItem);
            StartBaseMaterialWatcher();
            _ = RefreshBaseMaterialsWithFeedbackAsync();
        }

        private void ShowSt3CharacterInfoPage()
        {
            ShowOnlyPage(UnrealSyncPage);
            SelectShellNavigationItem(St3CharacterInfoNavItem);
            _ = RefreshCharacterInfoWithFeedbackAsync();
        }

        private void ShowSt4SkillsPage()
        {
            ShowOnlyPage(SkillsPage);
            SelectShellNavigationItem(St4SkillsNavItem);
            _ = RefreshSkillsWithFeedbackAsync();
        }

        private void ShowSt5SequenceFramesPage()
        {
            ShowOnlyPage(SequenceFramesPage);
            SelectShellNavigationItem(St5SequenceFramesNavItem);
            MarkLastEditedModule("SequenceFrames");
            _ = RefreshSequenceFramesWithFeedbackAsync();
        }

        private void ShowSt6BuffsPage()
        {
            ShowOnlyPage(BuffsPage);
            SelectShellNavigationItem(St6BuffsNavItem);
            _ = RefreshBuffsWithFeedbackAsync();
            RefreshProductionStatusWithFeedback();
        }

        private void ShowUnrealProjectSyncPage()
        {
            ShowOnlyPage(UnrealProjectSyncPage);
            SelectShellNavigationItem(UnrealProjectSyncNavItem);
            _applicationViewModel.UnrealProjectSync.Load(Settings.UnrealEnginePath, Settings.UnrealProjectPath);
            AppendLog(LogKind.Info, "已检测虚幻同步台关联状态。");
        }

        private void ShowLastEditedPage()
        {
            var tag = _applicationViewModel.UserOperations.LastOperationModuleTag ?? Settings.LastEditedModuleTag;
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
