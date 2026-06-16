using Microsoft.UI.Xaml.Controls;

namespace CrossingVoidZDTool;

internal sealed record ProductionStatus(
    InfoBarSeverity Severity,
    string Title,
    string Message,
    bool CanComplete,
    bool IsCompleted);
