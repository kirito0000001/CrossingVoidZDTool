using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CrossingVoidZDTool.Services;

public enum DialogResultKind
{
    None,
    Primary,
    Secondary,
    Cancel
}

public enum DialogSoundIntent
{
    None,
    Positive,
    Negative,
    Selection
}

public sealed record ContentDialogRequest(
    string Title,
    UIElement Content,
    string PrimaryButtonText = "OK",
    string CloseButtonText = "Cancel",
    string? SecondaryButtonText = null,
    ContentDialogButton DefaultButton = ContentDialogButton.Primary,
    DialogSoundIntent PrimarySound = DialogSoundIntent.Positive,
    DialogSoundIntent CloseSound = DialogSoundIntent.Negative,
    Style? PrimaryButtonStyle = null,
    Action<ContentDialog>? ConfigureDialog = null);
