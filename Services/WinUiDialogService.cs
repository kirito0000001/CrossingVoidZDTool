using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CrossingVoidZDTool.Services;

internal sealed class WinUiDialogService
{
    private readonly Func<XamlRoot> _getXamlRoot;

    public WinUiDialogService(Func<XamlRoot> getXamlRoot)
    {
        _getXamlRoot = getXamlRoot;
    }

    public async Task<DialogResultKind> ShowContentAsync(ContentDialogRequest request, CancellationToken cancellationToken = default)
    {
        var xamlRoot = _getXamlRoot();
        var dialog = new ContentDialog
        {
            Title = request.Title,
            Content = request.Content,
            PrimaryButtonText = request.PrimaryButtonText,
            SecondaryButtonText = request.SecondaryButtonText ?? string.Empty,
            CloseButtonText = request.CloseButtonText,
            DefaultButton = request.DefaultButton,
            PrimaryButtonStyle = request.PrimaryButtonStyle,
            XamlRoot = xamlRoot,
            RequestedTheme = (xamlRoot.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default
        };

        request.ConfigureDialog?.Invoke(dialog);
        AttachCancelShortcuts(dialog);
        return MapResult(await ShowDialogAsync(dialog, cancellationToken));
    }

    private static void AttachCancelShortcuts(ContentDialog dialog)
    {
        dialog.RightTapped += (_, args) =>
        {
            dialog.Hide();
            args.Handled = true;
        };
        dialog.KeyDown += (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Escape)
            {
                dialog.Hide();
                args.Handled = true;
            }
        };
    }

    private static async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog, CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(dialog.Hide);
        return await dialog.ShowAsync();
    }

    private static DialogResultKind MapResult(ContentDialogResult result)
    {
        return result switch
        {
            ContentDialogResult.Primary => DialogResultKind.Primary,
            ContentDialogResult.Secondary => DialogResultKind.Secondary,
            ContentDialogResult.None => DialogResultKind.None,
            _ => DialogResultKind.Cancel
        };
    }
}
