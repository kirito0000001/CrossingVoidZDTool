using System;
using System.Collections.Generic;
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
        /// 按角色 + 动作算出路径直接读。读的是**图层组**（一帧一组、帧号认组名），见
        /// <see cref="SequenceEffectPsdImportService"/>。
        ///
        /// 组数不必和帧数相等：少的算空帧、多的忽略（组里画几层都行，随便返工）。
        /// 唯一会停下报错的是"这个文件里根本没有图层组"（拿旧版一帧一层的底板画的）。
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
            // 读回来的特效帧**就放在底板目录里**（`<动作>-2x/Effect/`）：和对照底板 PNG、
            // 那份 PSD 挨在一起，一眼就能看到自己画了什么（晓桀 2026-09-25）。
            // 这个子目录**不受"重新导出底板时清空目录"的影响**（见 BasePlateExportService）。
            var effectFolder = BasePlateExportPlanner.ResolveEffectFolderPath(plan.OutputDirectory);

            try
            {
                var staged = new SequenceEffectPsdImportService().StageFrames(
                    psdPath,
                    expectedFrameCount,
                    plan.CanvasWidth,
                    plan.CanvasHeight,
                    effectFolder);
                var result = new SequenceEffectService().ImportStaged(
                    character,
                    section.Action,
                    staged,
                    expectedFrameCount);
                ReloadEffectLayer(character, section);
                ReportEffectImport(section, result, expectedFrameCount, Path.GetFileName(psdPath));
                AppendLog(LogKind.Info, $"[Effect PSD] 读回的特效帧已写到：{effectFolder}");
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
            // 注意：这里**不再删**那个目录 —— 它现在就是给用户看结果的落点（以前是 `.from-psd` 暂存）。
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
            // 清完立刻补一次预加载 —— 否则播放中重载特效层（改帧/换动作）后缓存是空的，
            // 接下来每一帧都命中不了（晓桀 2026-09-25：那会一路闪到最后全看不见）。
            _ = PreloadSequenceEffectBitmapsAsync();
            _sequenceEffectShownFrameOrdinal = -1;
            _sequenceEffectSubFrame = 0;
            _sequenceEffectLastSubFrame = -1;
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
        /// <summary>
        /// 特效层的**逐格序列**：第 i 格（1 起）该显示哪一帧，缺号的是 null。
        ///
        /// 为什么要"建"而不是"算"：以前每个 tick 现算序号 `startOrdinal + subFrame`，再去
        /// `layer.Frames` 里**线性查** `Ordinal` 相等的那一帧。而 `Frames` 里**只有导进来的那些帧**
        /// —— 缺号的根本不在列表里；预加载覆盖的又是"池子里的帧"，跟播放要的"逐格的帧"不是同一个集合。
        /// 于是池子里缺哪一号，播放就解析成空、白一帧：看上去就是"**有些图片闪一下**"
        /// （晓桀 2026-09-25 报的，特效本来就快，一闪就没了）。
        ///
        /// 建成序列之后：**缺号在"建"的时候一次定下来**（预加载照着它一次多带一张），
        /// 播放只按下标取，不再每 tick 查一遍。
        /// 重建的触发就是下面那把 key：换动作 / 改倍数 / 重新导入 / 帧数变了。
        /// </summary>
        private List<SequenceEffectFrame?> _sequenceEffectSlots = [];
        private string _sequenceEffectSlotsKey = string.Empty;

        private void RebuildSequenceEffectSlots()
        {
            var frames = _applicationViewModel.SequenceFrames.PreviewFrames;
            var layer = _applicationViewModel.SequenceFrames.EffectLayer;
            var key = string.Join(
                "|",
                layer?.LayerName ?? string.Empty,
                layer?.Multiplier ?? 0,
                layer?.ImportedAt?.Ticks ?? 0,
                layer?.Frames.Count ?? 0,
                frames.Count,
                frames.Sum(frame => Math.Max(1, frame.DurationFrames)));
            if (string.Equals(key, _sequenceEffectSlotsKey, StringComparison.Ordinal))
            {
                return;
            }

            _sequenceEffectSlotsKey = key;
            _sequenceEffectSlots = [];
            if (layer is null || !layer.HasFrames)
            {
                return;
            }

            var byOrdinal = layer.Frames
                .GroupBy(frame => frame.Ordinal)
                .ToDictionary(group => group.Key, group => group.First());
            var multiplier = Math.Max(1, layer.Multiplier);
            foreach (var frame in frames)
            {
                var slotCount = Math.Max(1, frame.DurationFrames) * multiplier;
                for (var slot = 0; slot < slotCount; slot++)
                {
                    _sequenceEffectSlots.Add(
                        byOrdinal.TryGetValue(_sequenceEffectSlots.Count + 1, out var effectFrame)
                            ? effectFrame
                            : null);
                }
            }
        }

        /// <summary>按**格号**取这一格该显示哪张图（1 起；越界 / 缺号 / 空帧 → null）。</summary>
        private ImageSource? ResolveSequenceEffectSourceAt(int ordinal)
        {
            if (ordinal < 1 || ordinal > _sequenceEffectSlots.Count)
            {
                return null;
            }

            var effectFrame = _sequenceEffectSlots[ordinal - 1];
            if (effectFrame is null || effectFrame.IsEmpty)
            {
                return null;
            }

            if (_sequenceEffectPreviewCache.TryGet(effectFrame.FilePath, out var cached))
            {
                _sequenceEffectLastSource = cached;
                return cached;
            }

            // **播放中绝不现解**。
            //
            // 以前这里同步 `LoadFile` 一张：一张 PNG 解在 UI 线程上要十几毫秒，特效层按"倍数"
            // 播（最狠 40fps，每帧只有 25ms）—— 解不过来就整条都跟不上：**每一帧都闪、最后全看不见**
            // （晓桀 2026-09-25 报的）。现在改成：命中不了就**沿用上一张**（画面冻一下也比闪强），
            // 同时把这个文件丢给预加载去补 —— 补上了下一 tick 就是命中。
            QueueSequenceEffectPreload(effectFrame.FilePath, effectFrame.FileName);
            return _sequenceEffectLastSource;
        }

        private ImageSource? _sequenceEffectLastSource;
        private int _sequenceEffectLastSubFrame = -1;

        /// <summary>
        /// 播放中发现没预加载到的图：**异步**解一张放回缓存，不阻塞这一 tick。
        /// 同一个文件同时只会排一次（用一张"排队中"的表挡重复）。
        /// </summary>
        private readonly HashSet<string> _sequenceEffectPreloadQueue = new(StringComparer.OrdinalIgnoreCase);

        private void QueueSequenceEffectPreload(string filePath, string displayName)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !_sequenceEffectPreloadQueue.Add(filePath))
            {
                return;
            }

            _ = Task.Run(() =>
            {
                // ⚠️ 后台线程**只能解码**：`WriteableBitmap` 是 WinRT 对象，在这里建会抛
                // `0x8001010E`（RPC_E_WRONG_THREAD）—— 上一版就是这么错的（晓桀 2026-09-25 贴的日志）。
                // 解出来的字节回到 UI 线程再装成 bitmap。
                SequencePreviewBitmapCache.DecodedBitmap decoded;
                try
                {
                    decoded = SequencePreviewBitmapCache.DecodeToPixels(filePath);
                }
                catch (Exception ex)
                {
                    _sequenceEffectPreloadQueue.Remove(filePath);
                    DispatcherQueue.TryEnqueue(() =>
                        AppendLog(LogKind.Warning, $"特效帧解码不出来：{displayName}", ex));
                    return;
                }

                DispatcherQueue.TryEnqueue(() =>
                {
                    _sequenceEffectPreloadQueue.Remove(filePath);
                    try
                    {
                        var loaded = SequencePreviewBitmapCache.CreateFromPixels(decoded);
                        _sequenceEffectPreviewCache.Store(filePath, loaded);
                        _sequenceEffectLastSource = loaded;
                    }
                    catch (Exception ex)
                    {
                        AppendLog(LogKind.Warning, $"特效帧装不进画面：{displayName}", ex);
                    }
                });
            });
        }

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

            RebuildSequenceEffectSlots();

            // 这一帧在特效序列里的起点：前面所有帧的格数 × 倍数，再加本帧内的子帧号。
            var startOrdinal = 1;
            for (var index = 0; index < currentIndex; index++)
            {
                startOrdinal += Math.Max(1, frames[index].DurationFrames) * Math.Max(1, layer.Multiplier);
            }

            // ── 诊断（只在两个事件上打，不刷屏）─────────────────────────────
            // "来回播"只有两个可能：① **当前帧**在被来回改（角色/时间轴那一侧）；
            // ② 当前帧没变、但**子帧自己绕回**了（`% slotCount` 到了头）。
            // 这两条日志分开报，跑一次就能看出是哪一侧。
            var slotCount = Math.Max(1, current.DurationFrames) * Math.Max(1, layer.Multiplier);
            if (_sequenceEffectShownFrameOrdinal != currentIndex)
            {
                AppendDiagnosticLog(
                    LogKind.Info,
                    $"[Effect Cursor] 切帧：{_sequenceEffectShownFrameOrdinal} → {currentIndex}" +
                    $"（子帧清零，原 counter={_sequenceEffectSubFrame}，本帧格数={slotCount}）");
                _sequenceEffectShownFrameOrdinal = currentIndex;
                _sequenceEffectSubFrame = 0;
            }

            var subFrame = ResolveSequenceEffectSubFrameOfCurrentFrame(current);
            if (_sequenceEffectLastSubFrame >= 0 && subFrame < _sequenceEffectLastSubFrame)
            {
                AppendDiagnosticLog(
                    LogKind.Info,
                    $"[Effect Cursor] 子帧绕回：frame={currentIndex} {_sequenceEffectLastSubFrame}→{subFrame}" +
                    $"（counter={_sequenceEffectSubFrame}，本帧格数={slotCount}）");
            }

            _sequenceEffectLastSubFrame = subFrame;

            var ordinal = startOrdinal + subFrame;
            ShowSequenceEffectSource(ResolveSequenceEffectSourceAt(ordinal));

            // **提前把下一格准备到暗的那张上**（同角色层的 `PrepareNextSequencePreviewSource`）：
            // 下一 tick 的 `Show` 就只剩"翻一下可见性"。
            PrepareSequenceEffectSource(ResolveSequenceEffectSourceAt(ordinal + 1));
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
        /// 把某张图**先铺到暗的那张上**（不翻页）。下一 tick 的
        /// <see cref="ShowSequenceEffectSource"/> 于是只是切换可见性，
        /// 不会出现"已经翻过去了、图还没铺好"的那一帧空窗。
        /// </summary>
        private void PrepareSequenceEffectSource(ImageSource? source)
        {
            SequencePreviewEffectPresenter.Prepare(source);
            SequenceEditorPreviewEffectPresenter.Prepare(source);
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

            // 照着**逐格序列**加载（一次多带一张）：播放要的是"逐格的帧"，
            // 以前加载的是"池子里的帧" —— 两个集合不是一回事（缺号的格子到时候才现解，卡一下）。
            RebuildSequenceEffectSlots();
            // 每次开播都重新试一遍（失败不该是永久的）。
            _sequenceEffectPreviewCache.ResetFailures();
            var failures = await _sequenceEffectPreviewCache.PreloadPathsAsync(
                _sequenceEffectSlots
                    .Where(frame => frame is { IsEmpty: false } && !string.IsNullOrWhiteSpace(frame.FilePath))
                    .Select(frame => (frame!.FilePath, frame.FileName))
                    .Distinct());
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
