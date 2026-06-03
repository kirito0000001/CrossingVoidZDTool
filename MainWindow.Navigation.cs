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
                ShowPlaceholderPage(ActionFramesPage);
                return;
            }

            if (string.Equals(tag, "LineArt", StringComparison.Ordinal))
            {
                ShowPlaceholderPage(LineArtPage);
                return;
            }

            if (string.Equals(tag, "UnrealSync", StringComparison.Ordinal))
            {
                ShowPlaceholderPage(UnrealSyncPage);
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
                SettingsPage
            ];

            foreach (var page in pages)
            {
                page.Visibility = ReferenceEquals(page, visiblePage)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            PlayPageEntrance(visiblePage);
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
