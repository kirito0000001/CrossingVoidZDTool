namespace CrossingVoidZDTool.ViewModels;

internal sealed class LineArtViewModel : ObservableObject
{
    private string _statusText = "就绪：等待选择线稿处理流程。";

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }
}
