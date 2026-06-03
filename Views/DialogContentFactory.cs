using CrossingVoidZDTool.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CrossingVoidZDTool.Views;

internal static class DialogContentFactory
{
    public static ScrollViewer CreateProjectRootHelpContent()
    {
        var panel = CreateHelpPanel();
        panel.Children.Add(CreateHelpHeading("整体项目位置"));
        panel.Children.Add(CreateHelpParagraph("这里设置的是所有零境 ZD 角色项目的总存放目录。程序启动时会检查这个目录，不存在就自动创建。"));
        panel.Children.Add(CreateHelpHeading("选择位置"));
        panel.Children.Add(CreateHelpParagraph($"点击“选择位置”时，你选择的是父目录。程序会自动在该目录下追加 {AppSettingsService.ProjectRootFolderName} 文件夹名。"));
        panel.Children.Add(CreateHelpCodeBlock($"""
            示例：
            选择：E:\ZDWork
            实际使用：E:\ZDWork\{AppSettingsService.ProjectRootFolderName}
            """));
        panel.Children.Add(CreateHelpHeading("迁移规则"));
        panel.Children.Add(CreateHelpParagraph("如果你更换了目录，程序会把旧目录里的所有文件复制到新目录，逐个校验文件大小和 SHA-256 内容哈希。全部确认无误后，才会保存新路径并删除旧目录。"));
        panel.Children.Add(CreateHelpHeading("安全限制"));
        panel.Children.Add(CreateHelpParagraph("新目录不能放在旧目录里面。这样可以避免迁移后删除旧目录时，把新目录也一起删除。"));
        panel.Children.Add(CreateHelpHeading("快捷键"));
        panel.Children.Add(CreateHelpParagraph("说明弹窗可以按 Esc 关闭，也可以在弹窗空白区域右键关闭。需要确认的小页面默认用 Enter 确认，Esc 取消。"));

        return CreateHelpScrollViewer(panel);
    }

    private static ScrollViewer CreateHelpScrollViewer(UIElement content)
    {
        return new ScrollViewer
        {
            Width = 440,
            MaxHeight = 420,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Auto,
            Content = content
        };
    }

    private static StackPanel CreateHelpPanel()
    {
        return new StackPanel
        {
            Spacing = 12,
            Width = 420,
            HorizontalAlignment = HorizontalAlignment.Left
        };
    }

    private static TextBlock CreateHelpHeading(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
    }

    private static TextBlock CreateHelpParagraph(string text)
    {
        return new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Width = 420,
            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush
        };
    }

    private static Border CreateHelpCodeBlock(string text)
    {
        return new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Background = Application.Current.Resources["LayerFillColorAltBrush"] as Brush,
            Child = new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily("Consolas"),
                Width = 396,
                TextWrapping = TextWrapping.Wrap
            }
        };
    }
}
