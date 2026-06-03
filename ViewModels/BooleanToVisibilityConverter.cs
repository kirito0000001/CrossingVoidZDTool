using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace CrossingVoidZDTool.ViewModels;

internal sealed class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var isVisible = value is bool flag && flag;
        if (parameter is string text && string.Equals(text, "Invert", StringComparison.OrdinalIgnoreCase))
        {
            isVisible = !isVisible;
        }

        return isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        var isVisible = value is Visibility.Visible;
        if (parameter is string text && string.Equals(text, "Invert", StringComparison.OrdinalIgnoreCase))
        {
            isVisible = !isVisible;
        }

        return isVisible;
    }
}
