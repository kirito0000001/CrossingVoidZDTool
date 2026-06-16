using CrossingVoidZDTool.Services;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CrossingVoidZDTool.Views;

internal static class DialogContentFactory
{
    public static CharacterNameInput CreateCharacterNameInput()
    {
        var nameTextBox = new TextBox
        {
            Header = "角色名字",
            PlaceholderText = "例如：Kirito",
            MaxLength = 60
        };
        var errorInfoBar = new InfoBar
        {
            IsOpen = false,
            Severity = InfoBarSeverity.Warning,
            Title = "无法创建"
        };
        var panel = new StackPanel
        {
            Spacing = 12,
            Width = 420,
            Children =
            {
                new TextBlock
                {
                    Text = "这里只填写角色名字。工具箱会自动生成角色英文代号和基础文件夹，后续可继续细化角色信息。",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush
                },
                nameTextBox,
                errorInfoBar
            }
        };

        return new CharacterNameInput(panel, nameTextBox, errorInfoBar);
    }

    public static ComboCharacterSelection CreateComboCharacterSelection(IReadOnlyList<CharacterCard> characters)
    {
        var listView = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            MaxHeight = 340,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        foreach (var character in characters)
        {
            listView.Items.Add(new ListViewItem
            {
                Tag = character,
                Content = new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = character.EffectiveDisplayName,
                            FontSize = 16,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                        },
                        new TextBlock
                        {
                            Text = $"{character.Code} / {(character.IsCompleted ? "已完成" : "草稿")}",
                            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush
                        }
                    }
                }
            });
        }

        if (listView.Items.Count > 0)
        {
            listView.SelectedIndex = 0;
        }

        var errorInfoBar = new InfoBar
        {
            IsOpen = false,
            Severity = InfoBarSeverity.Warning,
            Title = "请选择角色"
        };
        var panel = new StackPanel
        {
            Spacing = 12,
            Width = 460,
            Children =
            {
                new TextBlock
                {
                    Text = "连携技需要先绑定一个已有角色。这里会显示当前工程里已完成和草稿中的角色。",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush
                },
                listView,
                errorInfoBar
            }
        };

        return new ComboCharacterSelection(panel, listView, errorInfoBar);
    }

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

internal sealed record CharacterNameInput(StackPanel Content, TextBox NameTextBox, InfoBar ErrorInfoBar);

internal sealed record ComboCharacterSelection(StackPanel Content, ListView CharacterListView, InfoBar ErrorInfoBar);
