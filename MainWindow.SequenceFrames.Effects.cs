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
        /// <summary>
        /// 时间轴上被点住的那一格（帧序号 + 第几张特效帧）。开着「预览叠特效」时时间轴
        /// 按特效帧展开，点哪一格就把那一张**固定**在预览里；播放时清空，让定时器接管。
        /// </summary>
        private (int FrameIndex, int SubIndex)? _sequenceEffectPinnedSlot;

        // ── 导入 / 打开目录 / 清空 ────────────────────────────────────────────

        Task ISequenceFramesCommandHost.ImportEffectFramesFromPsdAsync()
        {
            // 读 PSD 没有对话框，一路同步；帧数不多（几十层），和特效层读盘同一量级。
            ImportEffectFramesFromBasePlatePsd();
            return Task.CompletedTask;
        }

        Task ISequenceFramesCommandHost.ImportEffectFramesFromFolderAsync() =>
            ImportEffectFramesFromPickedFolderAsync();

        /// <summary>
        /// 「导入特效帧 → 从底板 PSD 读回」（菜单里的第一条，也就是默认那条）。
        ///
        /// 前提是"导出底板 → 在 PS / 画世界里画 → 存回原处"：
        /// 那份 PSD 就在底板目录里（和底板 PNG 同一个落点），所以这里不用弹框，
        /// 按角色 + 动作算出路径直接读。**图层顺序就是帧顺序**，见
        /// <see cref="SequenceEffectPsdImportService"/>。
        ///
        /// 图层数对不上会**先停下报数**，不猜：多一层少一层都会让整条特效时序错位，
        /// 而且看起来很像"画的时候就是这样的"。
        /// </summary>
        private void ImportEffectFramesFromBasePlatePsd()
        {
            if (!TryResolveEffectImportTarget(out var character, out var section, out var expectedFrameCount))
            {
                return;
            }

            // 底板落点要靠动作代号解析出来（"OnDamage" 与 "Ondm" 指向同一份底板），
            // 认不出来就找不到那份 PSD，不如直接说清楚。
            if (!SequenceActionCatalog.TryResolve(section.Action.Code, out var definition, out var parsedForm))
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "读不回特效帧",
                    $"不认识的动作代号：{section.Action.Code}——认不出底板落在哪个目录。");
                return;
            }

            var plan = BuildBasePlatePlan(character, section, definition, parsedForm);
            var psdPath = SequenceEffectPsdImportService.ResolveBasePlatePsdPath(plan);
            var stagingFolder = SequenceEffectPsdImportService.GetStagingFolderPath(
                character,
                section.Action,
                SequenceEffectService.DefaultLayerName);

            try
            {
                var staged = new SequenceEffectPsdImportService().StageFrames(
                    psdPath,
                    expectedFrameCount,
                    plan.CanvasWidth,
                    plan.CanvasHeight,
                    stagingFolder);
                var result = new SequenceEffectService().ImportInOrder(
                    character,
                    section.Action,
                    staged,
                    expectedFrameCount);
                ReloadEffectLayer(character, section);
                ReportEffectImport(section, result, expectedFrameCount, Path.GetFileName(psdPath));
            }
            catch (InvalidOperationException ex)
            {
                // 层数 / 画布对不上属于"画的时候要对齐一下"，不是工具坏了。
                ShowFloatingTip(InfoBarSeverity.Warning, "读不回特效帧", ex.Message);
                AppendLog(LogKind.Warning, $"从底板 PSD 读回特效帧：{ex.Message}");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "读回特效失败", ex.Message);
                AppendLog(LogKind.Error, $"从底板 PSD 读回特效帧失败：{psdPath}", ex);
            }
            finally
            {
                // 暂存目录只是中转，成败都不留在盘上。
                DeleteStagingFolder(stagingFolder);
            }
        }

        /// <summary>
        /// 「导入特效帧 → 选择文件夹…」：挑一个装满 PNG 的目录，帧号取文件名末尾那段数字。
        ///
        /// 导出底板那批 PNG 和这套编号同名（<c>&lt;角色&gt;_&lt;动作&gt;_0001.png</c>），
        /// 所以"导出 → 画 → 选目录"不用改名。之所以还留着这条路：改图、从别处凑素材、
        /// 或者 PSD 那条走不通时，它不依赖任何别的东西。
        /// </summary>
        private async Task ImportEffectFramesFromPickedFolderAsync()
        {
            if (!TryResolveEffectImportTarget(out var character, out var section, out var expectedFrameCount))
            {
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
                var result = new SequenceEffectService().Import(
                    character,
                    section.Action,
                    sourceFiles,
                    expectedFrameCount);
                ReloadEffectLayer(character, section);
                ReportEffectImport(section, result, expectedFrameCount, Path.GetFileName(folderPath));
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "导入特效失败", ex.Message);
                AppendLog(LogKind.Error, "导入特效帧失败。", ex);
            }
        }

        /// <summary>
        /// 两个导入入口共用的前置检查：有没有选角色、有没有打开动作、这个动作该有几张特效。
        /// 任何一条不满足都只提示、不动盘（返回 false）。
        /// </summary>
        private bool TryResolveEffectImportTarget(
            out CharacterCard character,
            out SequenceFrameSection section,
            out int expectedFrameCount)
        {
            character = null!;
            section = null!;
            expectedFrameCount = 0;

            if (CharacterDesk.CurrentCharacter is not { } currentCharacter)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择角色", "请先在角色台选择当前制作角色。");
                return false;
            }

            character = currentCharacter;
            if (_applicationViewModel.SequenceFrames.SelectedSection is not { } currentSection)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "未选择动作", "先打开一个动作的序列编辑器，再导入特效。");
                return false;
            }

            section = currentSection;
            expectedFrameCount = ResolveEffectFrameCount(character, section);
            if (expectedFrameCount <= 0)
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "没有素材帧", "这个动作还没有帧，先导入帧素材。");
                return false;
            }

            return true;
        }

        /// <summary>导入成功后那一套提示：写日志 + 报"进来几张、空几张"。</summary>
        private void ReportEffectImport(
            SequenceFrameSection section,
            SequenceEffectImportResult result,
            int expectedFrameCount,
            string sourceName)
        {
            AppendLog(LogKind.User,
                $"导入特效帧：{section.Action.DisplayName} ← {sourceName} → "
                + $"{result.ImportedFrames}/{expectedFrameCount} 张"
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

        private static void DeleteStagingFolder(string stagingFolder)
        {
            try
            {
                if (Directory.Exists(stagingFolder))
                {
                    Directory.Delete(stagingFolder, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 暂存目录没删掉只是留了点垃圾，不该把一次成功的导入报成失败。
                ToolboxLog.Warn($"特效导入的暂存目录没清掉：{stagingFolder}", ex);
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

        /// <summary>
        /// 把「导入特效帧」按钮的菜单装上：清单来自 <see cref="SequenceEffectImportMenu"/>（纯函数），
        /// 命令在 ViewModel 里配一次。
        ///
        /// **菜单本体也是在这儿建的**：`MainWindow.xaml` 的行数已经顶在棘轮上限上，
        /// 而 XAML 那边只需要给按钮挂一个 `x:Name`（零行开销）。代价是看 XAML 看不出
        /// 这个按钮有菜单 —— 所以这条注释得留着。
        /// </summary>
        private void BuildSequenceEffectImportMenu()
        {
            var flyout = new MenuFlyout();
            foreach (var (item, command) in _applicationViewModel.SequenceFrames.EffectImportMenuActions)
            {
                var menuItem = new MenuFlyoutItem
                {
                    Text = item.Text,
                    Command = command
                };
                ToolTipService.SetToolTip(menuItem, item.ToolTip);
                flyout.Items.Add(menuItem);
            }

            SequenceEffectImportButton.Flyout = flyout;
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
            _sequenceEffectPinnedSlot = null;
            _applicationViewModel.SequenceFrames.SetEffectLayer(layer.HasFrames ? layer : null);
            UpdateSequenceEffectLayerSource();
        }

        /// <summary>
        /// 帧清单被改过之后（增删 / 替换 / 改帧率）重读一次特效层。
        ///
        /// 特效层是"按动作算出来的"：一个动作帧占几格变了，特效该有多少张、每一格对应哪一张
        /// 都会跟着变（时间轴按特效帧摊开的那份列表也一样）。编辑器没开着就什么都不用做。
        /// </summary>
        private void RefreshSequenceEffectLayerIfEditorOpen()
        {
            if (SequenceFramesManagerHost.Visibility != Visibility.Visible)
            {
                return;
            }

            if (CharacterDesk.CurrentCharacter is not { } character ||
                _applicationViewModel.SequenceFrames.SelectedSection is not { } section)
            {
                return;
            }

            ReloadEffectLayer(character, section);
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

            // 这一帧在特效序列里的起点：前面所有帧的格数 × 倍数，再加本帧内的子帧号。
            var startOrdinal = 1;
            for (var index = 0; index < currentIndex; index++)
            {
                startOrdinal += Math.Max(1, frames[index].DurationFrames) * Math.Max(1, layer.Multiplier);
            }

            if (_sequenceEffectShownFrameOrdinal != currentIndex)
            {
                _sequenceEffectShownFrameOrdinal = currentIndex;
                _sequenceEffectSubFrame = 0;
            }

            var ordinal = startOrdinal + ResolveSequenceEffectSubFrameOfCurrentFrame(current);
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

        /// <summary>
        /// 当前这一帧现在该显示第几张特效帧（0 起）—— 一个动作帧占"格数 × 倍数"张。
        ///
        /// 点住某一格时看钉住的那张（不跟定时器走）；否则按特效层自己的节拍走。
        /// **预览显示的那一张和时间轴上高亮的那一格共用这一个来源**，
        /// 所以两边不会各说各话（播放时高亮就是一格一格往前走的）。
        /// </summary>
        private int ResolveSequenceEffectSubFrameOfCurrentFrame(SequenceFrameItem frame)
        {
            var multiplier = Math.Max(
                1,
                _applicationViewModel.SequenceFrames.EffectLayer?.Multiplier ?? BasePlateExportPlanner.Multiplier);
            var slotCount = Math.Max(1, frame.DurationFrames) * multiplier;
            if (_sequenceEffectPinnedSlot is { } pinned && pinned.FrameIndex == frame.Index)
            {
                return Math.Min(pinned.SubIndex, slotCount - 1);
            }

            return _sequenceEffectSubFrame % slotCount;
        }

        private void ShowSequenceEffectSource(ImageSource? source)
        {
            SequencePreviewEffectPresenter.Show(source);
            SequenceEditorPreviewEffectPresenter.Show(source);
        }

        /// <summary>
        /// 特效层**先解码好再播** —— 和角色层那条 `PreloadSequencePreviewBitmapsAsync` 同一个道理。
        ///
        /// 以前只有角色层预加载，特效层是**播放中现用现解**（`UpdateSequenceEffectLayerSource()`
        /// 在 UI 线程的定时器回调里同步 `LoadFile`）。特效层比角色层快"倍数"倍、每帧都要换图，
        /// 于是播放一闪一闪、卡到看不清（晓桀 2026-09-25 报的）。
        /// 现在开播前把所有非空的特效帧一口气解完，播放路径只查缓存。
        ///
        /// 空帧不进预加载（它本来就没有文件）；解不出来的给一条提示 + 一行日志，
        /// 和角色层保持一致 —— 不静默。
        /// </summary>
        private async Task PreloadSequenceEffectBitmapsAsync()
        {
            var layer = _applicationViewModel.SequenceFrames.EffectLayer;
            if (layer is null || !layer.HasFrames)
            {
                return;
            }

            var failures = await _sequenceEffectPreviewCache.PreloadPathsAsync(
                layer.Frames
                    .Where(frame => !frame.IsEmpty && !string.IsNullOrWhiteSpace(frame.FilePath))
                    .Select(frame => (frame.FilePath, frame.FileName)));
            if (failures.Count == 0)
            {
                return;
            }

            ShowFloatingTip(
                InfoBarSeverity.Warning,
                "部分特效帧读取失败",
                failures.Count == 1
                    ? failures[0].FileName
                    : $"{failures[0].FileName} 等 {failures.Count} 张特效帧无法读取。");
            AppendLog(
                LogKind.Warning,
                "St5 特效层预加载部分失败：" + string.Join(
                    Environment.NewLine,
                    failures
                        .Take(8)
                        .Select(failure =>
                            $"{failure.FileName} | {failure.ExceptionType ?? "LoadError"} | {failure.Message} | {failure.FilePath}"))
                    + (failures.Count > 8 ? $"{Environment.NewLine}... 还有 {failures.Count - 8} 张失败。" : string.Empty));
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
                // **高亮必须跟着特效层一起走。**
                //
                // 时间轴展开之后，一个动作帧占"格数 × 倍数"格；真正一格一格往前的是特效层
                // （它有自己的、快"倍数"倍的节拍）。以前高亮只挂在**角色层**那次 tick 上，
                // 于是它永远停在每帧的第一格，中间那些展开出来的特效帧格在时间轴上
                // **一次都不会亮** —— 看着就像"还在按原来的帧格播"（晓桀 2026-09-25 报的）。
                SynchronizeSequenceTimelineSelectionToCurrentFrame();
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
