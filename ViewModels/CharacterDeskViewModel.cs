namespace CrossingVoidZDTool.ViewModels;

internal sealed class CharacterDeskViewModel : ObservableObject
{
    private string _statusText = "就绪：等待接入第一个角色制作模块。";

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }
}
