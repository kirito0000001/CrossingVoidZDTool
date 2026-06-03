namespace CrossingVoidZDTool.ViewModels;

internal sealed class ActionFramesViewModel : ObservableObject
{
    private string _statusText = "就绪：等待导入动作截图序列。";

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }
}
