using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;
using CrossingVoidZDTool.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Input;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CrossingVoidZDTool
{
    /// <summary>第二步「规整素材」：确认 Unreal 旧素材与工具箱规范素材的对应关系。</summary>
    public sealed partial class MainWindow
    {
        // ── 「打开素材规整」和「重新加载规整素材」两个入口都删了 ──
        // 合并后第 2 步既是规整又是素材同步：规整项由这一步**自己的检测**（同一次导出）
        // 重建，不再需要单独的入口，也不再需要单独跑一次导出。
        // 两个处理器都已经没有 XAML 按钮接着，留着只会让人以为还有这条路。

        private async void ChooseUnrealNormalizationRedirectButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: UnrealAssetNormalizationItem item })
            {
                return;
            }

            var usedCandidateIds = _applicationViewModel.UnrealProjectSync.NormalizationItems
                .Where(other => !ReferenceEquals(other, item))
                .Select(other => other.SelectedCandidate?.StableId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var availableCandidates = item.Candidates
                .Where(candidate => !usedCandidateIds.Contains(candidate.StableId) ||
                    string.Equals(candidate.StableId, item.SelectedCandidate?.StableId, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (availableCandidates.Length == 0)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "没有可用的工具箱素材", "请先在工具箱中补充该类型的合规素材，或标记为不需要重定向。");
                return;
            }

            var list = new ListView
            {
                ItemsSource = availableCandidates,
                SelectionMode = ListViewSelectionMode.Single,
                MaxHeight = 420
            };
            list.ItemTemplate = (DataTemplate)XamlReader.Load($@"
                <DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
                    <Grid MinHeight='82' Padding='6' ColumnSpacing='10'>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width='72'/>
                            <ColumnDefinition Width='*'/>
                        </Grid.ColumnDefinitions>
                        <Border Width='64' Height='64' Background='{{ThemeResource CardBackgroundFillColorSecondaryBrush}}'>
                            <Image Source='{{Binding FileUri}}' Stretch='Uniform'/>
                        </Border>
                        <StackPanel Grid.Column='1' Spacing='2' VerticalAlignment='Center'>
                            <TextBlock FontWeight='SemiBold' Text='{{Binding DisplayName}}' TextTrimming='CharacterEllipsis'/>
                            <TextBlock FontSize='12' Style='{{StaticResource SubtleTextStyle}}' Text='{{Binding RelativePath}}' TextTrimming='CharacterEllipsis'/>
                        </StackPanel>
                    </Grid>
                </DataTemplate>");
            if (item.SelectedCandidate is not null)
            {
                list.SelectedItem = item.SelectedCandidate;
            }

            var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                $"选择 {item.UnrealAssetName} 的重定向素材",
                list,
                PrimaryButtonText: "使用所选素材",
                CloseButtonText: "取消",
                ConfigureDialog: dialog =>
                {
                    dialog.IsPrimaryButtonEnabled = list.SelectedItem is not null;
                    list.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = list.SelectedItem is not null;
                }));
            if (result == DialogResultKind.Primary && list.SelectedItem is UnrealAssetNormalizationCandidate candidate)
            {
                _applicationViewModel.UnrealProjectSync.SelectNormalizationRedirect(item, candidate);
            }
        }

        private void SkipUnrealNormalizationButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: UnrealAssetNormalizationItem item })
            {
                _applicationViewModel.UnrealProjectSync.MarkNormalizationNotRequired(item);
            }
        }

        private void ClearUnrealNormalizationRedirectButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: UnrealAssetNormalizationItem item })
            {
                _applicationViewModel.UnrealProjectSync.ClearNormalizationRedirect(item);
            }
        }

        private void PreviewUnrealNormalizationAudioButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: UnrealAssetNormalizationItem { IsAudio: true, HasPreview: true } item } button)
            {
                PlayVoiceFile(item.PreviewFilePath, button);
            }
        }
    }
}
