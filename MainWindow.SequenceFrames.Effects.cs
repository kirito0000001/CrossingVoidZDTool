using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;
using CrossingVoidZDTool.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;

namespace CrossingVoidZDTool
{
    public sealed partial class MainWindow
    {
        private readonly SequencePreviewBitmapCache _sequenceEffectPreviewCache = new();
        private DispatcherQueueTimer? _sequenceEffectSubFrameTimer;
        private int _sequenceEffectSubFrame;
        private int _sequenceEffectShownFrameOrdinal = -1;

        // ── 导入 / 打开目录 / 清空 ────────────────────────────────────────────

        /// <summary>
        /// 「导入特效帧」：把画好的特效帧（就是"导出底板"那批图，画完的样子）导进当前动作的特效层。
        ///
        /// 选**文件夹**而不是选文件：底板是一整批平铺在一个目录里的，选目录一下就对上了。
        /// 帧号取文件名里最后一段数字，所以"导出 → 画 → 导回"不需要改名。
        /// </summary>
        async Task ISequenceFramesCommandHost.ImportEffectFramesAsync()
        {
            if (CharacterDesk.CurrentCharacter is not { } character)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择角色", "请先在角色台选择当前制作角色。");
                return;
            }

            var section = _applicationViewModel.SequenceFrames.SelectedSection;
            if (section is null)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择动作", "先打开一个动作的序列编辑器，再导入特效。");
                return;
            }

            var expectedFrameCount = ResolveEffectFrameCount(character, section);
            if (expectedFrameCount <= 0)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "没有素材帧", "这个动作还没有帧，先导入帧素材。");
                return;
            }

            var folderPath = await _filePickerService.PickFolderAsync(PickerLocationId.ComputerFolder);
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                return;
            }

            var sourceFiles = Directory
                .EnumerateFiles(folderPath, "*.png", SearchOption.TopDirectoryOnly)
                .ToArray();
            if (sourceFiles.Length == 0)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Warning,
                    "这个文件夹里没有 PNG",
                    $"特效帧要从导出的底板改出来，期望 {expectedFrameCount} 张：{folderPath}");
                return;
            }

            try
            {
                var service = new SequenceEffectService();
                var result = service.Import(character, section.Action, sourceFiles, expectedFrameCount);
                ReloadEffectLayer(character, section);
                AppendLog(LogKind.User,
                    $"导入特效帧：{section.Action.DisplayName} → {result.ImportedFrames}/{expectedFrameCount} 张"
                    + $"，空帧 {result.EmptyFrames}"
                    + (result.IgnoredFrames > 0 ? $"，忽略越界 {result.IgnoredFrames} 张" : string.Empty)
                    + (result.ClearedFrames > 0 ? $"，先清掉旧帧 {result.ClearedFrames} 张" : string.Empty));
                ShowFloatingTip(
                    InfoBarSeverity.Success,
                    $"特效已导入 {result.ImportedFrames} 张",
                    result.EmptyFrames > 0
                        ? $"另有 {result.EmptyFrames} 帧没有内容（空帧），同步到虚幻时是空关键帧。"
                        : "全部帧都有内容。");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "导入特效失败", ex.Message);
                AppendLog(LogKind.Error, "导入特效帧失败。", ex);
            }
        }

        void ISequenceFramesCommandHost.OpenEffectFolder()
        {
            if (CharacterDesk.CurrentCharacter is not { } character ||
                _applicationViewModel.SequenceFrames.SelectedSection is not { } section)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择动作", "先打开一个动作的序列编辑器。");
                return;
            }

            OpenFolderInExplorer(SequenceEffectService.GetLayerFramesFolderPath(character, section.Action));
        }

        void ISequenceFramesCommandHost.ClearEffectLayer()
        {
            if (CharacterDesk.CurrentCharacter is not { } character ||
                _applicationViewModel.SequenceFrames.SelectedSection is not { } section)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择动作", "先打开一个动作的序列编辑器。");
                return;
            }

            try
            {
                var removed = new SequenceEffectService().ClearLayer(character, section.Action);
                ReloadEffectLayer(character, section);
                AppendLog(LogKind.User, $"清空特效层：{section.Action.DisplayName}（删了 {removed} 张）");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "清空特效失败", ex.Message);
                AppendLog(LogKind.Error, "清空特效层失败。", ex);
            }
        }

        /// <summary>把这一层的现状读进 ViewModel，并立刻刷新预览上的特效层。</summary>
        private void ReloadEffectLayer(CharacterCard character, SequenceFrameSection section)
        {
            var layer = new SequenceEffectService().Load(
                character,
                section.Action,
                expectedFrameCount: ResolveEffectFrameCount(character, section));
            _sequenceEffectPreviewCache.Clear();
            _sequenceEffectShownFrameOrdinal = -1;
            _sequenceEffectSubFrame = 0;
            _applicationViewModel.SequenceFrames.SetEffectLayer(layer.HasFrames ? layer : null);
            UpdateSequenceEffectLayerSource();
        }

        /// <summary>这个动作的特效该有多少张 = 导出底板会出多少张（总格数 × 倍数）。</summary>
        private int ResolveEffectFrameCount(CharacterCard character, SequenceFrameSection section)
        {
            if (section.Frames.Count == 0)
            {
                return 0;
            }

            return BasePlateExportPlanner
                .Build(
                    Settings.ProjectRootPath,
                    character.Code,
                    section.Action.Code,
                    section.Frames,
                    _applicationViewModel.SequenceFrames.PreviewFps)
                .Frames.Count;
        }

        // ── 两层预览 ────────────────────────────────────────────────────────

        /// <summary>
        /// 把当前时间点该显示的特效帧贴到两层预览上。
        ///
        /// 时序：一个角色帧占 <c>格数 × 倍数</c> 张特效帧（倍数就是导出底板那个 2），
        /// 所以特效层在自己的小定时器上推进，角色层照旧按素材帧推进 —— 两层合起来
        /// 正好是"角色 10fps、特效 20fps"的样子。
        /// </summary>
        private void UpdateSequenceEffectLayerSource()
        {
            var frames = _applicationViewModel.SequenceFrames.PreviewFrames;
            var current = _applicationViewModel.SequenceFrames.CurrentPreviewFrame;
            var layer = _applicationViewModel.SequenceFrames.EffectLayer;
            if (frames.Count == 0 || current is null || layer is null || !layer.HasFrames)
            {
                ShowSequenceEffectSource(null);
                return;
            }

            var currentIndex = frames.IndexOf(current);
            if (currentIndex < 0)
            {
                ShowSequenceEffectSource(null);
                return;
            }

            var multiplier = Math.Max(1, layer.Multiplier);
            var durationFrames = Math.Max(1, current.DurationFrames);
            // 这一帧在特效序列里的起点：前面所有帧的格数 × 倍数，再加本帧内的子帧号。
            var startOrdinal = 1;
            for (var index = 0; index < currentIndex; index++)
            {
                startOrdinal += Math.Max(1, frames[index].DurationFrames) * multiplier;
            }

            if (_sequenceEffectShownFrameOrdinal != currentIndex)
            {
                _sequenceEffectShownFrameOrdinal = currentIndex;
                _sequenceEffectSubFrame = 0;
            }

            var subFrame = _sequenceEffectSubFrame % (durationFrames * multiplier);
            var ordinal = startOrdinal + subFrame;
            var effectFrame = layer.Frames.FirstOrDefault(frame => frame.Ordinal == ordinal);
            if (effectFrame is null || effectFrame.IsEmpty)
            {
                ShowSequenceEffectSource(null);
                return;
            }

            if (!_sequenceEffectPreviewCache.TryGet(effectFrame.FilePath, out var source))
            {
                try
                {
                    source = SequencePreviewBitmapCache.LoadFile(effectFrame.FilePath);
                    _sequenceEffectPreviewCache.Store(effectFrame.FilePath, source);
                }
                catch (Exception ex)
                {
                    _sequenceEffectPreviewCache.MarkFailed(effectFrame.FilePath);
                    AppendLog(LogKind.Warning, $"特效帧读不出来：{effectFrame.FileName}", ex);
                    ShowSequenceEffectSource(null);
                    return;
                }
            }

            ShowSequenceEffectSource(source);
        }

        private void ShowSequenceEffectSource(ImageSource? source)
        {
            SequencePreviewEffectPresenter.Show(source);
            SequenceEditorPreviewEffectPresenter.Show(source);
        }

        /// <summary>特效层自己的节拍（比角色层快"倍数"倍）；跟着预览的播放/暂停一起开关。</summary>
        private void StartSequenceEffectSubFrameTimer()
        {
            _sequenceEffectSubFrameTimer ??= CreateSequenceEffectSubFrameTimer();
            _sequenceEffectSubFrameTimer.Interval = ResolveSequenceEffectSubFrameInterval();
            _sequenceEffectSubFrameTimer.Start();
        }

        private void StopSequenceEffectSubFrameTimer() => _sequenceEffectSubFrameTimer?.Stop();

        private DispatcherQueueTimer CreateSequenceEffectSubFrameTimer()
        {
            var timer = DispatcherQueue.CreateTimer();
            timer.Tick += (_, _) =>
            {
                _sequenceEffectSubFrame++;
                UpdateSequenceEffectLayerSource();
            };
            return timer;
        }

        private TimeSpan ResolveSequenceEffectSubFrameInterval()
        {
            var fps = Math.Clamp(_applicationViewModel.SequenceFrames.PreviewFps, 1, 60);
            var multiplier = Math.Max(1, _applicationViewModel.SequenceFrames.EffectLayer?.Multiplier ?? 1);
            return TimeSpan.FromMilliseconds(1000d / (fps * multiplier));
        }
    }
}
