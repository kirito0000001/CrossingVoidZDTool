using Microsoft.UI.Xaml.Controls;

namespace CrossingVoidZDTool.Controls;

/// <summary>工具集面板（可复用排版）。它只负责显示与绑定，流程在 <c>AtlasToolViewModel</c> 里。</summary>
public sealed partial class AtlasToolPanel : UserControl
{
    public AtlasToolPanel()
    {
        InitializeComponent();
    }
}
