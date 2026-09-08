using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;

namespace CrossingVoidZDTool.ViewModels;

/// <summary>
/// 右栏的选择操作：全选、全不选、反选。
///
/// 六步的勾选目标不一样——第三、五步是差异树，第四、六步是字段列表，
/// 第一、二步压根没有勾选。以前每步各写一套（其实是只有第六步写了），
/// 这里统一成一组按钮，按当前步骤分派。
/// </summary>
internal sealed partial class UnrealProjectSyncViewModel
{
    /// <summary>这一步有没有可勾选的东西。没有就把整组按钮藏掉。</summary>
    public bool HasStepSelection => SelectableStepItemCount > 0;

    public Visibility StepSelectionVisibility =>
        !IsEngineToToolbox && HasStepSelection ? Visibility.Visible : Visibility.Collapsed;

    public bool CanChangeStepSelection => HasStepSelection && IsWorkflowOperationIdle;

    /// <summary>可勾选的条目数。差异树只算叶子里真正能执行的那些。</summary>
    public int SelectableStepItemCount => WorkflowStep switch
    {
        3 or 5 => SelectionTreeRoots
            .SelectMany(root => root.Children)
            .Count(item => item.IsSelectable && item.Change is not null && CanExecutePublishChange(item.Change)),
        4 => LightConfigurationItems.Count(item => item.IsSelectable),
        6 => BlueprintSetupItems.Count(item => item.IsSelectable),
        _ => 0,
    };

    public int SelectedStepItemCount => WorkflowStep switch
    {
        3 or 5 => SelectionTreeRoots
            .SelectMany(root => root.Children)
            .Count(item => item.IsChecked == true && item.Change is not null && CanExecutePublishChange(item.Change)),
        4 => LightConfigurationItems.Count(item => item.IsSelected),
        6 => BlueprintSetupItems.Count(item => item.IsSelected),
        _ => 0,
    };

    public string StepSelectionText => $"已选择 {SelectedStepItemCount} / {SelectableStepItemCount} 项";

    public bool AreAllStepItemsSelected =>
        SelectableStepItemCount > 0 && SelectedStepItemCount == SelectableStepItemCount;

    /// <summary>全选 / 全不选。</summary>
    public void SetStepSelection(bool selected)
    {
        ApplyStepSelection(_ => selected);
    }

    /// <summary>反选：勾上的取消、没勾的勾上。</summary>
    public void InvertStepSelection()
    {
        ApplyStepSelection(current => !current);
    }

    /// <summary>
    /// 批量改勾选。
    ///
    /// 逐条改会各自触发一次重算和一次会话缓存落盘，几十条就是几十次写盘，
    /// 所以先压住通知，改完再统一刷新一次。
    /// </summary>
    private void ApplyStepSelection(Func<bool, bool> next)
    {
        switch (WorkflowStep)
        {
            case 3:
            case 5:
            {
                using var scope = BeginBulkSelectionUpdate();
                foreach (var item in SelectionTreeRoots.SelectMany(root => root.Children)
                             .Where(item => item.IsSelectable &&
                                 item.Change is not null &&
                                 CanExecutePublishChange(item.Change)))
                {
                    item.IsChecked = next(item.IsChecked == true);
                }

                break;
            }
            case 4:
            {
                foreach (var item in LightConfigurationItems.Where(item => item.IsSelectable))
                {
                    item.IsSelected = next(item.IsSelected);
                }

                break;
            }
            case 6:
            {
                _isBulkBlueprintSetupSelection = true;
                try
                {
                    foreach (var item in BlueprintSetupItems.Where(item => item.IsSelectable))
                    {
                        item.IsSelected = next(item.IsSelected);
                    }
                }
                finally
                {
                    _isBulkBlueprintSetupSelection = false;
                }

                NotifyBlueprintSetupChanged();
                break;
            }
            default:
                return;
        }

        NotifyStepSelectionChanged();
        SaveSessionCache();
    }

    /// <summary>把当前这一步的差异整理成一段可粘贴的文本。</summary>
    public string BuildStepChangeReport()
    {
        var lines = new List<string>
        {
            $"# {SelectedSource?.DraftCharacter?.EffectiveDisplayName ?? "未选角色"} · 第 {WorkflowStep} 步 {WorkflowStepName}",
            string.Empty,
        };

        switch (WorkflowStep)
        {
            case 1:
                lines.Add(FoundationSummaryText);
                lines.AddRange(FoundationChecks.Select(item =>
                    $"- [{(item.IsCompliant ? "通过" : "不符")}] {item.DisplayName}：{item.DetailText}"));
                break;
            case 2:
                lines.Add(NormalizationSummaryText);
                lines.AddRange(NormalizationItems.Select(item =>
                    $"- [{(item.IsResolved ? "已处理" : "待处理")}] {item.UnrealAssetName}  {item.UnrealObjectPath}"));
                break;
            case 3:
            case 5:
                lines.Add(DetectionResultSummaryText);
                lines.AddRange(SelectionTreeRoots.SelectMany(root => root.Children)
                    .Where(item => item.Change is not null)
                    .Select(item => $"- [{item.Change!.Kind}] {item.DisplayName}"));
                break;
            case 4:
                lines.Add(LightConfigurationSummaryText);
                lines.AddRange(LightConfigurationItems.Select(item =>
                    $"- [{item.StatusText}] {item.GroupName} · {item.DisplayName}\n    现在：{item.Source.CurrentSummary}\n    目标：{item.Source.TargetSummary}"));
                break;
            case 6:
                lines.Add(BlueprintSetupSummaryText);
                lines.AddRange(BlueprintSetupItems.Select(item =>
                    $"- [{item.StatusText}] {item.GroupName} · {item.DisplayName}\n    现在：{item.Source.CurrentSummary}\n    目标：{item.Source.TargetSummary}"));
                break;
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// 当前步骤里被勾选的条目指向的 Unreal 资产路径，用于在编辑器里定位。
    /// 一个都没勾时退回这一步涉及的全部资产。
    /// </summary>
    public IReadOnlyList<string> GetStepObjectPathsToBrowse()
    {
        var selected = WorkflowStep switch
        {
            3 or 5 => SelectionTreeRoots.SelectMany(root => root.Children)
                .Where(item => item.IsChecked == true && item.Change is not null)
                .Select(item => item.Change!.UnrealItem?.SourceObjectPath ??
                    item.Change!.ToolboxItem?.SourceObjectPath ?? string.Empty),
            4 => LightConfigurationItems
                .Where(item => item.IsSelected)
                .Select(item => item.Source.TargetPath),
            6 => BlueprintSetupItems
                .Where(item => item.IsSelected)
                .Select(item => item.Source.TargetPath),
            _ => [],
        };

        var paths = selected
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizeBrowsePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (paths.Length > 0)
        {
            return paths;
        }

        var all = WorkflowStep switch
        {
            4 => LightConfigurationItems.Select(item => item.Source.TargetPath),
            6 => BlueprintSetupItems.Select(item => item.Source.TargetPath),
            _ => Enumerable.Empty<string>(),
        };
        return all
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizeBrowsePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>编辑器定位要的是包路径，去掉 <c>.资产名</c> 那一段。</summary>
    private static string NormalizeBrowsePath(string objectPath)
    {
        var path = (objectPath ?? string.Empty).Trim();
        var dot = path.IndexOf('.', StringComparison.Ordinal);
        return dot > 0 ? path[..dot] : path;
    }

    public void NotifyStepSelectionChanged()
    {
        OnPropertyChanged(nameof(HasStepSelection));
        OnPropertyChanged(nameof(StepSelectionVisibility));
        OnPropertyChanged(nameof(CanChangeStepSelection));
        OnPropertyChanged(nameof(SelectableStepItemCount));
        OnPropertyChanged(nameof(SelectedStepItemCount));
        OnPropertyChanged(nameof(StepSelectionText));
        OnPropertyChanged(nameof(AreAllStepItemsSelected));
    }
}
