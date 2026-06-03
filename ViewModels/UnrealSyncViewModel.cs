namespace CrossingVoidZDTool.ViewModels;

internal sealed class UnrealSyncViewModel : ObservableObject
{
    private bool _isSyncRunning;
    private string _statusText = "就绪：等待配置 Unreal 同步目标。";

    public bool IsSyncRunning
    {
        get => _isSyncRunning;
        set => SetProperty(ref _isSyncRunning, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }
}
