using Microsoft.UI.Xaml.Controls;

namespace CrossingVoidZDTool.ViewModels;

internal sealed class ProductionStatusViewModel : ObservableObject
{
    private InfoBarSeverity _severity = InfoBarSeverity.Informational;
    private string _title = "未选择角色";
    private string _message = "请先在零境角色台选择当前制作角色。";
    private bool _canComplete;
    private bool _isCompleted;
    private bool _isOpen = true;

    public InfoBarSeverity Severity
    {
        get => _severity;
        private set => SetProperty(ref _severity, value);
    }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public bool CanComplete
    {
        get => _canComplete;
        private set => SetProperty(ref _canComplete, value);
    }

    public bool IsCompleted
    {
        get => _isCompleted;
        private set => SetProperty(ref _isCompleted, value);
    }

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
    }

    public void Apply(ProductionStatus status)
    {
        Severity = status.Severity;
        Title = status.Title;
        Message = status.Message;
        CanComplete = status.CanComplete;
        IsCompleted = status.IsCompleted;
        IsOpen = true;
    }
}
