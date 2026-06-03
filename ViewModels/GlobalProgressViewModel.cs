using System;

namespace CrossingVoidZDTool.ViewModels;

internal sealed class GlobalProgressViewModel : ObservableObject
{
    private bool _isVisible;
    private string _operationTitle = string.Empty;
    private string _title = string.Empty;
    private string _detail = string.Empty;
    private string _elapsedText = "00:00";
    private string _percentText = "0%";
    private double _percent;
    private double _lastPercent;
    private bool _isIndeterminate = true;

    public bool IsVisible
    {
        get => _isVisible;
        private set => SetProperty(ref _isVisible, value);
    }

    public string OperationTitle
    {
        get => _operationTitle;
        private set => SetProperty(ref _operationTitle, value);
    }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string Detail
    {
        get => _detail;
        private set => SetProperty(ref _detail, value);
    }

    public string ElapsedText
    {
        get => _elapsedText;
        private set => SetProperty(ref _elapsedText, value);
    }

    public string PercentText
    {
        get => _percentText;
        private set => SetProperty(ref _percentText, value);
    }

    public double Percent
    {
        get => _percent;
        private set => SetProperty(ref _percent, value);
    }

    public double LastPercent
    {
        get => _lastPercent;
        private set => SetProperty(ref _lastPercent, value);
    }

    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        private set => SetProperty(ref _isIndeterminate, value);
    }

    public void Start(string operationTitle, string detail)
    {
        OperationTitle = operationTitle;
        Title = detail;
        Detail = operationTitle;
        Percent = 0;
        LastPercent = 0;
        PercentText = "0%";
        IsIndeterminate = true;
        ElapsedText = FormatElapsedTime(TimeSpan.Zero);
        IsVisible = true;
    }

    public void Update(string message, double percent, string? detail = null, bool isIndeterminate = false)
    {
        var clampedPercent = Math.Clamp(percent, 0, 100);
        Title = message;
        Detail = string.IsNullOrWhiteSpace(detail)
            ? OperationTitle
            : detail.Replace('\n', ' ');
        IsIndeterminate = isIndeterminate;
        Percent = isIndeterminate ? Percent : clampedPercent;
        LastPercent = clampedPercent;
        PercentText = $"{clampedPercent:0}%";
    }

    public void Complete(string message, string? detail = null)
    {
        IsIndeterminate = false;
        Percent = 100;
        LastPercent = 100;
        PercentText = "100%";
        Title = message;
        Detail = string.IsNullOrWhiteSpace(detail) ? OperationTitle : detail;
    }

    public void Hide()
    {
        IsVisible = false;
    }

    public void UpdateElapsed(TimeSpan elapsed)
    {
        ElapsedText = FormatElapsedTime(elapsed);
    }

    private static string FormatElapsedTime(TimeSpan elapsed)
    {
        return elapsed.TotalHours >= 1
            ? elapsed.ToString(@"h\:mm\:ss")
            : elapsed.ToString(@"mm\:ss");
    }
}
