using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;

namespace CrossingVoidZDTool.ViewModels;

/// <summary>编辑器里「改帧」类操作要界面提供的东西（S3）。</summary>
internal interface ISequenceFrameEditorHost
{
    CharacterCard? CurrentCharacter { get; }

    SequenceFrameSection? SelectedSection { get; }

    void StopSequencePreview();

    /// <summary>标一下「这一页被改过」（用于离开时提示保存之类）。</summary>
    void MarkSequenceFramesEdited();

    void ClearSequencePreviewCache();

    /// <summary>把预览源清空（删除到一帧不剩时用）。</summary>
    void ClearSequencePreviewSource();

    void UpdateSequencePreviewImageSource();

    void UpdateSequencePreviewInterval();

    void HideSequenceFrameManager();

    /// <summary>记一次可撤销操作（**撤销/重做的唯一入口，不能漏**）。</summary>
    void RecordSequenceFrameOperation(
        string actionName,
        string description,
        SequenceFrameSection section,
        IReadOnlyList<string> snapshotFilePaths);

    void ShowFloatingTip(NotifySeverity severity, string title, string message);

    void AppendLog(LogKind kind, string message, Exception? error = null);

    /// <summary>时间轴上当前多选中的帧（批量删除的输入）。</summary>
    IReadOnlyList<SequenceFrameItem> SelectedTimelineFrames { get; }

    /// <summary>批量删除前的确认；返回 false 表示用户取消。</summary>
    Task<bool> ConfirmBatchDeleteAsync(int frameCount);

    /// <summary>在保持页面滚动位置的前提下跑一段导入（导入会重建整页，滚动位置会跳）。</summary>
    Task RunPreservingScrollAsync(Func<Task> action);

    void ResetSequencePreviewTransform();

    Task StartSequencePreviewAsync();
}

/// <summary>
/// 编辑器里「复制 / 删除」这两条改帧流程（S3）。
///
/// 两条的形状一样，而且都是**先抓快照、再改、最后记一笔可撤销操作**：
///
/// ```
/// 守卫（有角色 / 有当前动作）→ 抓快照 → 停预览（拷贝走 ClearCache，删除不拷）
///   → VM 改帧 → 刷新预览 → RecordSequenceFrameOperation → 提示 + 用户日志 → 失败留痕
/// ```
///
/// 搬出来的价值集中在两点：**顺序**（先停预览再改，否则预览还指着旧帧）和
/// **别忘了记撤销**——后者以前藏在 2200 行的分部文件里，没有任何用例或冒烟保护它。
/// 删除还带一个防重入标志，跟 S2 一样随流程走。
/// </summary>
internal sealed class SequenceFrameEditorController(
    ISequenceFrameEditorHost host,
    SequenceFramesViewModel viewModel)
{
    private readonly ISequenceFrameEditorHost _host = host;
    private readonly SequenceFramesViewModel _viewModel = viewModel;

    public bool IsDeleting { get; private set; }

    /// <summary>
    /// 用「帧合集」里选中的素材替换指定帧（S3 最后一条）。
    ///
    /// 与单文件替换同形，只是来源是多张；**返回值决定浮层收不收**，
    /// 所以失败时返回 false 而不是吞掉——调用方（确认批量替换）据此留着浮层让人改选。
    /// </summary>
    public async Task<bool> ReplaceFrameWithSourcesAsync(
        SequenceFrameItem frame,
        IReadOnlyList<string> sourcePaths)
    {
        if (_host.CurrentCharacter is not { } character || _host.SelectedSection is not { } section)
        {
            return false;
        }

        try
        {
            _host.MarkSequenceFramesEdited();
            _host.StopSequencePreview();
            _host.ClearSequencePreviewCache();
            await _viewModel.ReplaceFrameWithSourcesAsync(character, section, frame, sourcePaths);
            _host.UpdateSequencePreviewImageSource();
            _host.ShowFloatingTip(
                NotifySeverity.Success,
                "帧素材已选入",
                $"{section.Action.DisplayName} / {sourcePaths.Count} 张");
            _host.AppendLog(
                LogKind.User,
                $"从帧合集选入素材：{section.Action.DisplayName} / #{frame.Index} / {sourcePaths.Count} 张");
            return true;
        }
        catch (Exception ex)
        {
            _host.ShowFloatingTip(NotifySeverity.Error, "帧素材选入失败", ex.Message);
            _host.AppendLog(LogKind.Error, "从帧合集选入素材失败。", ex);
            return false;
        }
    }

    /// <summary>
    /// 导入帧素材到某个动作（S4）。路径由壳那边先选好（选文件是界面的事），
    /// 这里负责顺序：标记改动 → 停预览 → 清缓存 → 保持滚动位置导入 → 选中并起预览 → 提示/日志。
    ///
    /// 「保持滚动位置」这层包装不能省：导入会重建整页，不包的话用户会被弹回页面顶部。
    /// </summary>
    public async Task ImportAsync(SequenceFrameSection section, IReadOnlyList<string> paths)
    {
        if (_host.CurrentCharacter is not { } character)
        {
            _host.ShowFloatingTip(NotifySeverity.Warning, "未选择角色", "请先在角色台选择当前制作角色。");
            return;
        }

        try
        {
            _host.MarkSequenceFramesEdited();
            _host.StopSequencePreview();
            _host.ClearSequencePreviewCache();
            await _host.RunPreservingScrollAsync(() => _viewModel.ImportAsync(character, section, paths));
            if (_viewModel.TrySelectSection(section.Action.Code))
            {
                _host.ResetSequencePreviewTransform();
                _host.UpdateSequencePreviewImageSource();
                await _host.StartSequencePreviewAsync();
            }

            _host.ShowFloatingTip(
                NotifySeverity.Success,
                "序列帧已导入",
                $"{section.Action.DisplayName}：{paths.Count} 张。");
            _host.AppendLog(
                LogKind.User,
                $"导入序列帧：{section.Action.DisplayName} / {section.Action.Code}，{paths.Count} 张。");
        }
        catch (Exception ex)
        {
            _host.ShowFloatingTip(NotifySeverity.Error, "序列帧导入失败", ex.Message);
            _host.AppendLog(LogKind.Error, "序列帧导入失败。", ex);
        }
    }

    /// <summary>复制当前帧（原「复制」按钮 / 右键菜单）。</summary>
    public async Task CopyFrameAsync(SequenceFrameItem frame)
    {
        if (_host.CurrentCharacter is not { } character || _host.SelectedSection is not { } section)
        {
            return;
        }

        try
        {
            var snapshot = await _viewModel.CreateSectionSnapshotAsync(character, section);
            _host.StopSequencePreview();
            _host.ClearSequencePreviewCache();
            await _viewModel.DuplicateFrameAsync(character, section, frame);
            _host.RecordSequenceFrameOperation(
                "复制序列帧",
                $"{section.Action.DisplayName} / {frame.FileName}",
                section,
                snapshot);
            _host.ShowFloatingTip(NotifySeverity.Success, "序列帧已复制", frame.FileName);
            _host.AppendLog(LogKind.User, $"复制序列帧：{section.Action.DisplayName} / {frame.FileName}");
        }
        catch (Exception ex)
        {
            _host.ShowFloatingTip(NotifySeverity.Error, "序列帧复制失败", ex.Message);
            _host.AppendLog(LogKind.Error, "序列帧复制失败。", ex);
        }
    }

    /// <summary>删除指定帧（原右键菜单「删除」）。删空了要顺手收起管理器。</summary>
    /// <summary>
    /// 替换指定帧的素材（S3「替换」）。
    ///
    /// 顺序与复制/删除一致，只多一步 <see cref="ISequenceFrameEditorHost.MarkSequenceFramesEdited"/>——
    /// 换掉素材等于"这一页被改过"，漏掉它会让离开页面时的提示/保存判断失准。
    /// </summary>
    public async Task ReplaceFrameAsync(SequenceFrameItem frame, string sourcePath)
    {
        if (_host.CurrentCharacter is not { } character || _host.SelectedSection is not { } section)
        {
            return;
        }

        try
        {
            _host.MarkSequenceFramesEdited();
            _host.StopSequencePreview();
            _host.ClearSequencePreviewCache();
            await _viewModel.ReplaceFrameAsync(character, section, frame, sourcePath);
            _host.UpdateSequencePreviewImageSource();
            _host.ShowFloatingTip(
                NotifySeverity.Success,
                "帧素材已替换",
                $"{section.Action.DisplayName} / 第 {frame.Index} 帧");
            _host.AppendLog(LogKind.User, $"替换序列帧素材：{section.Action.DisplayName} / #{frame.Index}");
        }
        catch (Exception ex)
        {
            _host.ShowFloatingTip(NotifySeverity.Error, "帧素材替换失败", ex.Message);
            _host.AppendLog(LogKind.Error, "序列帧素材替换失败。", ex);
        }
    }

    public async Task DeleteFrameAsync(SequenceFrameItem frame, SequenceFrameSection section)
    {
        if (_host.CurrentCharacter is not { } character || IsDeleting)
        {
            return;
        }

        IsDeleting = true;
        try
        {
            var snapshot = await _viewModel.CreateSectionSnapshotAsync(character, section);
            _host.StopSequencePreview();
            await _viewModel.DeleteFrameAsync(character, section, frame);
            if (_viewModel.PreviewFrames.Count == 0)
            {
                // 一帧不剩：清预览源并收起管理器，否则界面会指着刚被删掉的帧。
                _host.ClearSequencePreviewSource();
                _host.HideSequenceFrameManager();
            }
            else
            {
                _host.UpdateSequencePreviewImageSource();
                _host.UpdateSequencePreviewInterval();
            }

            _host.RecordSequenceFrameOperation(
                "删除序列帧",
                $"{section.Action.DisplayName} / {frame.FileName}",
                section,
                snapshot);
            _host.ShowFloatingTip(NotifySeverity.Success, "序列帧已删除", frame.FileName);
            _host.AppendLog(LogKind.User, $"删除序列帧：{section.Action.DisplayName} / {frame.FileName}");
        }
        catch (Exception ex)
        {
            _host.ShowFloatingTip(NotifySeverity.Error, "序列帧删除失败", ex.Message);
            _host.AppendLog(LogKind.Error, "序列帧删除失败。", ex);
        }
        finally
        {
            IsDeleting = false;
        }
    }

    /// <summary>
    /// 批量删除选中的帧（S3 收尾）。与单帧删除**共用同一个防重入标志**——
    /// 以前那是壳里的一个字段，两条流程各自读它；现在都归 <see cref="IsDeleting"/>，
    /// 「同一时刻只允许一次删除」这条不变式只有一处定义。
    /// </summary>
    public async Task DeleteFramesAsync(IReadOnlyList<SequenceFrameItem> frames)
    {
        if (frames.Count < 2 ||
            _host.CurrentCharacter is not { } character ||
            _host.SelectedSection is not { } section ||
            IsDeleting)
        {
            return;
        }

        if (!await _host.ConfirmBatchDeleteAsync(frames.Count))
        {
            return;
        }

        IsDeleting = true;
        try
        {
            var snapshot = await _viewModel.CreateSectionSnapshotAsync(character, section);
            _host.StopSequencePreview();
            await _viewModel.DeleteFramesAsync(character, section, frames);
            if (_viewModel.PreviewFrames.Count == 0)
            {
                _host.ClearSequencePreviewSource();
                _host.HideSequenceFrameManager();
            }
            else
            {
                _host.UpdateSequencePreviewImageSource();
                _host.UpdateSequencePreviewInterval();
            }

            _host.RecordSequenceFrameOperation(
                "批量删除序列帧",
                $"{section.Action.DisplayName} / {frames.Count} 帧",
                section,
                snapshot);
            _host.ShowFloatingTip(NotifySeverity.Success, "序列帧已批量删除", $"已删除 {frames.Count} 帧。");
            _host.AppendLog(LogKind.User, $"批量删除序列帧：{section.Action.DisplayName} / {frames.Count} 帧");
        }
        catch (Exception ex)
        {
            _host.ShowFloatingTip(NotifySeverity.Error, "序列帧批量删除失败", ex.Message);
            _host.AppendLog(LogKind.Error, "序列帧批量删除失败。", ex);
        }
        finally
        {
            IsDeleting = false;
        }
    }
}
