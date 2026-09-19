using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;

namespace CrossingVoidZDTool.ViewModels;

/// <summary>检测重复要界面提供的东西（S2）。</summary>
internal interface ISequenceFrameDuplicateDetectionHost
{
    CharacterCard? CurrentCharacter { get; }

    /// <summary>重算「检测 / 一键处理」两个按钮的可用性。</summary>
    void UpdateDuplicateActionButtons();

    void ShowGlobalProgress(string title, string detail);

    void UpdateGlobalProgress(string message, double percent, string? detail = null, bool isIndeterminate = false);

    void CompleteGlobalProgress(string message, string? detail = null);

    Task HideGlobalProgressAfterDelayAsync(int delayMilliseconds = 1400);

    CancellationToken GetGlobalProgressCancellationToken();

    void ShowFloatingTip(NotifySeverity severity, string title, string message);

    void AppendLog(LogKind kind, string message, Exception? error = null);

    /// <summary>帧合集里正在被替换的目标帧；没有多选目标时为 null。</summary>
    SequenceFrameItem? CollectionSelectionTarget { get; }

    /// <summary>多选顺序（按点选先后，界面要拿它当替换源）。</summary>
    IReadOnlyList<SequenceFrameCollectionItem> OrderedCollectionSelection { get; }

    /// <summary>把编辑器那一帧替换成这些素材（返回是否成功）。</summary>
    Task<bool> ReplaceEditorFrameWithSourcesAsync(
        SequenceFrameItem target,
        IReadOnlyList<string> sourcePaths);

    void HideSequenceFrameCollection();

    /// <summary>帧合集里还有没有重复项（一键处理的前置条件）。</summary>
    bool HasDuplicateCollectionItems { get; }
}

/// <summary>
/// 「检测帧合集里的重复内容」（S2）。
///
/// 原来是一个 40 行的 async void 处理器，里面三件事混在一起：
/// 防重入（`_isDetectingSequenceFrameDuplicates`）、进度（显示/更新/完成/收起）、
/// 以及取消与失败两条分支。搬出来之后**防重入标志属于流程**——
/// 这正是 S0 那条教训（标志属于流程，不属于窗口），也让「跑完一定要放开标志」
/// 这件事能被断言（以前漏放就是按钮永久变灰）。
/// </summary>
internal sealed class SequenceFrameDuplicateDetectionController(
    ISequenceFrameDuplicateDetectionHost host,
    SequenceFramesViewModel viewModel)
{
    private readonly ISequenceFrameDuplicateDetectionHost _host = host;
    private readonly SequenceFramesViewModel _viewModel = viewModel;

    /// <summary>正在检测。界面按钮的可用性由它决定（壳会读这个值）。</summary>
    public bool IsDetecting { get; private set; }

    /// <summary>正在一键处理。界面按钮的可用性由它决定（壳会读这个值）。</summary>
    public bool IsResolvingAll { get; private set; }

    public async Task DetectAsync()
    {
        if (_host.CurrentCharacter is not { } character || IsDetecting)
        {
            return;
        }

        IsDetecting = true;
        _host.UpdateDuplicateActionButtons();
        try
        {
            _host.ShowGlobalProgress("检测重复帧", character.StatusDisplayText);
            _host.UpdateGlobalProgress("正在检测帧内容重复...", 2, character.Code);
            var progress = new Progress<ProgressUpdate>(update =>
                _host.UpdateGlobalProgress(update.Message, update.Percent, update.Detail, update.IsIndeterminate));
            await _viewModel.DetectCollectionDuplicatesAsync(progress, _host.GetGlobalProgressCancellationToken());
            _host.CompleteGlobalProgress("重复检测完成", _viewModel.CollectionSummaryText);
            await _host.HideGlobalProgressAfterDelayAsync(600);
            _host.AppendLog(LogKind.User, "检测帧合集重复内容。");
        }
        catch (OperationCanceledException)
        {
            _host.CompleteGlobalProgress("重复检测已取消", character.Code);
            await _host.HideGlobalProgressAfterDelayAsync();
        }
        catch (Exception ex)
        {
            _host.CompleteGlobalProgress("重复检测失败", ex.Message);
            await _host.HideGlobalProgressAfterDelayAsync();
            _host.ShowFloatingTip(NotifySeverity.Error, "重复检测失败", ex.Message);
            _host.AppendLog(LogKind.Error, "检测帧合集重复内容失败。", ex);
        }
        finally
        {
            // 标志一定要放开：漏放的话两个按钮会永久变灰（以前就有这类投诉）。
            IsDetecting = false;
            _host.UpdateDuplicateActionButtons();
        }
    }

    /// <summary>
    /// 「确认批量替换」（S2）：把多选到的素材当成替换源，换掉目标那一帧。
    ///
    /// 两条守卫（有目标、选了东西）与一条顺序（**替换成功才收浮层**）是这条流程的全部；
    /// 失败时浮层要留着，让人能改选——以前这条只有真点一次才知道会不会提前收掉。
    /// </summary>
    public async Task ConfirmCollectionSelectionAsync()
    {
        if (_host.CollectionSelectionTarget is not { } target)
        {
            return;
        }

        var sourcePaths = _host.OrderedCollectionSelection.Select(item => item.FilePath).ToList();
        if (sourcePaths.Count == 0)
        {
            return;
        }

        if (await _host.ReplaceEditorFrameWithSourcesAsync(target, sourcePaths))
        {
            _host.HideSequenceFrameCollection();
        }
    }

    /// <summary>
    /// 「一键处理重复帧」（S2 最后一条）：把重复资源组里多余的引用重定向到保留项。
    ///
    /// 与 <see cref="DetectAsync"/> 同一个形状：三条件守卫 → 防重入 → 进度 →
    /// 成功提示（带"重定向了几个引用"）→ 取消/失败分支 → **finally 放开标志**。
    /// 标志同样属于流程：漏放就是按钮永久变灰。
    /// </summary>
    public async Task ResolveAllAsync()
    {
        if (_host.CurrentCharacter is not { } character || IsResolvingAll || !_host.HasDuplicateCollectionItems)
        {
            return;
        }

        IsResolvingAll = true;
        _host.UpdateDuplicateActionButtons();
        try
        {
            _host.ShowGlobalProgress("一键处理重复帧", character.StatusDisplayText);
            _host.UpdateGlobalProgress("正在准备重复资源组...", 2, character.Code);
            var progress = new Progress<ProgressUpdate>(update =>
                _host.UpdateGlobalProgress(update.Message, update.Percent, update.Detail, update.IsIndeterminate));
            var updatedReferenceCount = await _viewModel.ResolveAllDuplicateFramesAsync(
                character, progress, _host.GetGlobalProgressCancellationToken());
            _host.CompleteGlobalProgress(
                "重复资源处理完成",
                $"已重定向 {updatedReferenceCount} 个帧引用，重复检测结果已更新。");
            await _host.HideGlobalProgressAfterDelayAsync(700);
            _host.ShowFloatingTip(
                NotifySeverity.Success,
                "一键处理完成",
                $"已重定向 {updatedReferenceCount} 个帧引用。剩余资源已重新检测。");
            _host.AppendLog(LogKind.User, $"一键处理帧合集重复资源：重定向 {updatedReferenceCount} 个引用。");
        }
        catch (OperationCanceledException)
        {
            _host.CompleteGlobalProgress("一键处理已取消", character.Code);
            await _host.HideGlobalProgressAfterDelayAsync();
        }
        catch (Exception ex)
        {
            _host.CompleteGlobalProgress("一键处理失败", ex.Message);
            await _host.HideGlobalProgressAfterDelayAsync();
            _host.ShowFloatingTip(NotifySeverity.Error, "一键处理失败", ex.Message);
            _host.AppendLog(LogKind.Error, "一键处理帧合集重复资源失败。", ex);
        }
        finally
        {
            IsResolvingAll = false;
            _host.UpdateDuplicateActionButtons();
        }
    }
}
