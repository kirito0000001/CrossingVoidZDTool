using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;
using CrossingVoidZDTool.ViewModels;
using CrossingVoidZDTool.Views;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.System;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Microsoft.UI.Dispatching;
using WinRT.Interop;
using Windows.UI;
using PathFigure = Microsoft.UI.Xaml.Media.PathFigure;

namespace CrossingVoidZDTool
{
    public sealed partial class MainWindow
    {
        private void RegisterSettingsShortcuts()
        {
            var undoSettingsAccelerator = new KeyboardAccelerator
            {
                Key = VirtualKey.Z,
                Modifiers = VirtualKeyModifiers.Control,
                IsEnabled = true
            };
            undoSettingsAccelerator.ScopeOwner = RootGrid;
            undoSettingsAccelerator.Invoked += SettingsUndoKeyboardAccelerator_Invoked;
            RootGrid.KeyboardAccelerators.Add(undoSettingsAccelerator);

            var openShortcutGuideAccelerator = new KeyboardAccelerator
            {
                Key = VirtualKey.F1
            };
            openShortcutGuideAccelerator.ScopeOwner = RootGrid;
            openShortcutGuideAccelerator.Invoked += OpenShortcutGuideKeyboardAccelerator_Invoked;
            RootGrid.KeyboardAccelerators.Add(openShortcutGuideAccelerator);

            var openDraftAccelerator = new KeyboardAccelerator
            {
                Key = VirtualKey.F2
            };
            openDraftAccelerator.ScopeOwner = RootGrid;
            openDraftAccelerator.Invoked += OpenDraftKeyboardAccelerator_Invoked;
            RootGrid.KeyboardAccelerators.Add(openDraftAccelerator);

            var openSkillValueGuideAccelerator = new KeyboardAccelerator
            {
                Key = VirtualKey.F3
            };
            openSkillValueGuideAccelerator.ScopeOwner = RootGrid;
            openSkillValueGuideAccelerator.Invoked += OpenSkillValueGuideKeyboardAccelerator_Invoked;
            RootGrid.KeyboardAccelerators.Add(openSkillValueGuideAccelerator);

            var openPageRulesAccelerator = new KeyboardAccelerator
            {
                Key = VirtualKey.F4
            };
            openPageRulesAccelerator.ScopeOwner = RootGrid;
            openPageRulesAccelerator.Invoked += OpenPageRulesKeyboardAccelerator_Invoked;
            RootGrid.KeyboardAccelerators.Add(openPageRulesAccelerator);

            var closeDraftAccelerator = new KeyboardAccelerator
            {
                Key = VirtualKey.Escape
            };
            closeDraftAccelerator.ScopeOwner = RootGrid;
            closeDraftAccelerator.Invoked += CloseDraftKeyboardAccelerator_Invoked;
            RootGrid.KeyboardAccelerators.Add(closeDraftAccelerator);
        }

        private void CloseDraftKeyboardAccelerator_Invoked(
            KeyboardAccelerator sender,
            KeyboardAcceleratorInvokedEventArgs args)
        {
            if (SequenceFrameDuplicateResolverHost.Visibility == Visibility.Visible)
            {
                HideSequenceFrameDuplicateResolver();
                args.Handled = true;
                return;
            }

            if (SequenceFramesCollectionHost.Visibility == Visibility.Visible)
            {
                if (_isSequenceFrameCollectionMultiSelecting)
                {
                    CancelSequenceFrameCollectionMultiSelection();
                }
                else
                {
                    HideSequenceFrameCollection();
                }

                args.Handled = true;
                return;
            }

            if (SequenceFramesManagerHost.Visibility == Visibility.Visible)
            {
                if (_isSelectingSequenceFrameCopyTarget)
                {
                    CancelSequenceFrameCopyTargetSelection();
                }
                else
                {
                    HideSequenceFrameManager();
                }

                args.Handled = true;
                return;
            }

            if (ActionFramesPage.Visibility != Visibility.Visible ||
                !CharacterDesk.IsDraftOpen)
            {
                return;
            }

            ExitDraftToPortraitEntry();
            args.Handled = true;
        }

        private void OpenShortcutGuideKeyboardAccelerator_Invoked(
            KeyboardAccelerator sender,
            KeyboardAcceleratorInvokedEventArgs args)
        {
            ShowShortcutGuideOverlay();
            AppendLog(LogKind.User, SettingsPage.Visibility == Visibility.Visible
                ? "已通过 F1 打开设置页 Tips 合集。"
                : "已通过 F1 打开快捷键大全。");
            args.Handled = true;
        }

        private async void OpenDraftKeyboardAccelerator_Invoked(
            KeyboardAccelerator sender,
            KeyboardAcceleratorInvokedEventArgs args)
        {
            try
            {
                await ShowDraftOverlayAsync();
                AppendLog(LogKind.User, CharacterDesk.HasCurrentCharacter
                    ? "已通过 F2 打开当前角色草稿本。"
                    : "已通过 F2 打开未选择角色提示。");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "草稿参考层打开失败", ex.Message);
                AppendLog(LogKind.Error, "草稿参考层打开失败。", ex);
            }

            args.Handled = true;
        }

        private void OpenSkillValueGuideKeyboardAccelerator_Invoked(
            KeyboardAccelerator sender,
            KeyboardAcceleratorInvokedEventArgs args)
        {
            ShowSkillValueGuideOverlay();
            AppendLog(LogKind.User, "已通过 F3 打开技能数值倍率规范。");
            args.Handled = true;
        }

        private void OpenPageRulesKeyboardAccelerator_Invoked(
            KeyboardAccelerator sender,
            KeyboardAcceleratorInvokedEventArgs args)
        {
            ShowCurrentPageRulesOverlay();
            if (UnrealProjectSyncPage.Visibility == Visibility.Visible &&
                _applicationViewModel.UnrealProjectSync.IsEngineToToolbox)
            {
                CopyUnrealProjectSyncNamingMapToClipboard();
                ShowFloatingTip(InfoBarSeverity.Success, "命名对照已复制", "已复制虚幻基础素材图片类型对照表。");
            }

            AppendLog(LogKind.User, "已通过 F4 打开当前页面填写法则。");
            args.Handled = true;
        }

        private static void CopyUnrealProjectSyncNamingMapToClipboard()
        {
            CopyTextToClipboard(
                """
                道具-ItemIcon
                技能-SkillIcon-
                对局头像-BattleAvatar-
                完整立绘-FullMorphPortrait-
                立绘-MorphPortrait-
                背景-Background
                护援特写-SupportCutIn-
                头像-Icon-
                """);
        }

        private void SettingsUndoKeyboardAccelerator_Invoked(
            KeyboardAccelerator sender,
            KeyboardAcceleratorInvokedEventArgs args)
        {
            if (_applicationViewModel.UserOperations.CanUndoLastOperation)
            {
                _ = UndoLastUserOperationAsync();
                args.Handled = true;
                return;
            }

            if (SkillsPage.Visibility == Visibility.Visible &&
                _applicationViewModel.Skills.CanUndoStageChange)
            {
                args.Handled = UndoLastSkillStageChange();
                return;
            }

            if (SettingsPage.Visibility != Visibility.Visible ||
                !Settings.UndoLastSettingCommand.CanExecute(null))
            {
                return;
            }

            Settings.UndoLastSettingCommand.Execute(null);
            UpdateLogOptionEnabledState();
            UpdateAuxiliaryDisplayVisibility();
            AppendLog(LogKind.User, "已通过 Ctrl+Z 撤回上一次设置修改。");
            args.Handled = true;
        }

        private async Task UndoLastUserOperationAsync()
        {
            try
            {
                if (await _applicationViewModel.UserOperations.UndoLastOperationAsync())
                {
                    PersistCurrentCharacterSelection();
                }
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "撤销失败", ex.Message);
                AppendLog(LogKind.Error, "撤销用户操作失败。", ex);
            }
        }

        private void UpdateLogOptionEnabledState()
        {
            var enabled = Settings.LogEnabled;
            LogUserOperationsCheckBox.IsEnabled = enabled;
            LogWarningCheckBox.IsEnabled = enabled;
            LogErrorCheckBox.IsEnabled = enabled;
        }

        private void UpdateAuxiliaryDisplayVisibility()
        {
            WorkspaceStatusBorder.Visibility = Settings.ShowWorkspacePath
                ? Visibility.Visible
                : Visibility.Collapsed;
            LogPanelBorder.Visibility = Settings.LogEnabled
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void LogSettingControl_Changed(object sender, RoutedEventArgs e)
        {
            UpdateLogOptionEnabledState();
            UpdateAuxiliaryDisplayVisibility();
        }

        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            _logPanel.Clear();
            LogItemsControl.Items.Clear();
            AppendLog(LogKind.User, "已清空输出日志。");
        }

        private void CopyAllLogButton_Click(object sender, RoutedEventArgs e)
        {
            CopyLogEntries(_logPanel.Entries, "已复制全部日志");
        }

        /// <summary>
        /// 复制**本次流程**的全部日志。
        ///
        /// 面板只留 300 条，跑完第五步时第一到第四步的日志早就被挤掉了，
        /// 「复制全部」复制的是面板里残下来的那点。这一条按当前 RunId
        /// 从 runtime.log 里捞全量行——它才是「一次同步的完整日志」的入口。
        /// </summary>
        private void CopyCurrentRunLogButton_Click(object sender, RoutedEventArgs e)
        {
            var runId = CurrentLogRunId;
            if (runId == RuntimeLogFormat.NoRunId)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Informational,
                    "还没有本次流程",
                    "先跑一次检测或同步，日志里就会开出一批新的批次号。");
                return;
            }

            IReadOnlyList<string> lines;
            try
            {
                lines = RuntimeLogFile.ReadRunLines(_runtimeLogPath, runId);
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "读取本次流程日志失败", ex.Message);
                AppendLog(LogKind.Error, "读取本次流程日志失败。", ex);
                return;
            }

            if (lines.Count == 0)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Informational,
                    "本次流程还没有写进文件",
                    $"{runId} 在 runtime.log 里一行都没有。");
                return;
            }

            CopyTextToClipboard(string.Join(Environment.NewLine, lines));
            ShowFloatingTip(
                InfoBarSeverity.Success,
                "已复制本次流程日志",
                $"{runId} · {lines.Count} 行（含面板装不下的明细）");
        }

        private void CopyLogEntries(IReadOnlyList<LogEntry> entries, string tipTitle)
        {
            var text = string.Join(Environment.NewLine + Environment.NewLine, entries.Select(item => item.CopyText));
            CopyTextToClipboard(text);
            ShowFloatingTip(InfoBarSeverity.Success, tipTitle, $"{entries.Count} 条记录");
        }

        private void ScrollLogToBottomButton_Click(object sender, RoutedEventArgs e)
        {
            ScrollLogToBottom();
        }

        private void LogScrollViewer_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var delta = e.GetCurrentPoint(LogScrollViewer).Properties.MouseWheelDelta;
            if (delta == 0)
            {
                return;
            }

            const double logWheelScrollPixelsPerNotch = 150;
            var notchCount = delta / 120d;
            var targetOffset = Math.Clamp(
                LogScrollViewer.VerticalOffset - notchCount * logWheelScrollPixelsPerNotch,
                0,
                LogScrollViewer.ScrollableHeight);
            LogScrollViewer.ChangeView(null, targetOffset, null, disableAnimation: false);
            e.Handled = true;
        }

        private bool ShouldWriteLog(LogKind kind)
        {
            return Settings.ShouldWriteLog(kind);
        }

        /// <summary>
        /// 记一次用户操作。
        ///
        /// <paramref name="startsRun"/> 为真表示这是一次「跑流程」的动作
        /// （检测差异、同步素材、刷新序列、导入、应用配置…），会开一批新日志；
        /// 选引擎、选项目、全选这类沿用当前批次，免得每点一下就切一次批次号。
        /// </summary>
        private void LogUserOperation(string action, bool startsRun = false)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                return;
            }

            if (startsRun)
            {
                BeginLogRun(action);
            }

            _recentOperations.Enqueue((DateTime.Now, action));
            while (_recentOperations.Count > MaxRecentOperationCount)
            {
                _recentOperations.Dequeue();
            }

            AppendLog(LogKind.User, $"[操作] {action}");
        }

        private void AppendRuntimeLog(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            try
            {
                lock (_runtimeLogLock)
                {
                    // 轮转发生在写入之前：先归档旧文件，再在新文件里留一行说明。
                    // 这是「翻日志要翻一个无限长大的文件」和「跨天找不到上下文」的唯一出口。
                    var now = DateTimeOffset.Now;
                    var archived = RuntimeLogFile.RotateIfNeeded(_runtimeLogPath, now);
                    if (archived is not null)
                    {
                        RuntimeLogFile.AppendLine(
                            _runtimeLogPath,
                            RuntimeLogFormat.FormatFileLine(
                                now, CurrentLogScope, $"上一份日志已归档为 {archived}。"));
                    }

                    RuntimeLogFile.AppendLine(_runtimeLogPath, line);
                }
            }
            catch
            {
                // 运行日志写入失败不能影响同步主流程。
            }
        }

        /// <summary>
        /// 逐条明细只落 runtime.log，不进日志面板。
        /// 面板只留 300 条，上千条明细写进去也是当场被挤掉，
        /// 白白让每一条都去建 XAML 元素、排一次版。
        /// </summary>
        private void AppendDiagnosticLog(LogKind kind, string message)
        {
            if (!ShouldWriteLog(kind))
            {
                return;
            }

            AppendRuntimeLog(RuntimeLogFormat.FormatFileLine(
                DateTimeOffset.Now,
                CurrentLogScope,
                $"LogZDTool: {GetLogKindLabel(kind)}: {message}"));
        }

        /// <summary>
        /// 把 Service 层的日志接到底部日志面板。
        /// Services 是纯业务层、不认识 WinUI，所以走 IToolboxLogSink 这层薄映射。
        /// </summary>
        private sealed class ToolboxLogBridge(MainWindow owner) : IToolboxLogSink
        {
            public void Write(ToolboxLogLevel level, string message, Exception? error)
            {
                var kind = level switch
                {
                    ToolboxLogLevel.Error => LogKind.Error,
                    ToolboxLogLevel.Warning => LogKind.Warning,
                    _ => LogKind.Info,
                };
                owner.RunOnUiThread(() => owner.AppendLog(kind, message, error));
            }
        }

        /// <summary>
        /// 写一条日志。<paramref name="sticky"/> 为真的条目不参与面板淘汰——
        /// 步骤标题行和批次首行靠它才能在一次上千行的检测之后还留在面板里。
        /// </summary>
        private void AppendLog(
            LogKind kind,
            string message,
            Exception? exception = null,
            bool sticky = false,
            int? stepOverride = null)
        {
            if (!ShouldWriteLog(kind))
            {
                return;
            }

            var now = DateTimeOffset.Now;
            var scope = ScopeFor(stepOverride);
            var kindLabel = GetLogKindLabel(kind);
            var body = $"{kindLabel}: {message}";
            var displayText = RuntimeLogFormat.FormatPanelLine(now, scope, body);
            var copyText = displayText;
            if (exception is not null)
            {
                displayText += $"{Environment.NewLine}{FormatExceptionForLog(exception)}";
                copyText += $"{Environment.NewLine}{FormatExceptionForLog(exception, stackTraceLineLimit: 8)}";
            }

            var fileText = RuntimeLogFormat.FormatFileLine(now, scope, $"LogZDTool: {body}");
            if (exception is not null)
            {
                // 文件里异常另起一行，不再把 [HH:mm:ss] 嵌在正文中间。
                fileText += $"{Environment.NewLine}{FormatExceptionForLog(exception)}";
            }

            AppendRuntimeLog(fileText.Replace(Environment.NewLine, " | "));

            var removedIndex = _logPanel.Append(ToLogEntryKind(kind), displayText, copyText, sticky);
            AppendLogItem(kind, displayText, copyText, sticky, removedIndex);
        }

        /// <summary>
        /// 只追加新的一条、并摘掉溢出的旧条目。
        /// 以前每写一行日志都要 Items.Clear() 再重建满 300 个 Border，外加一次同步排版，
        /// 单行实测 400ms；一次第五步检测有上千行日志，界面就会整个僵住十几分钟。
        /// </summary>
        private void AppendLogItem(
            LogKind kind,
            string displayText,
            string copyText,
            bool sticky,
            int removedIndex)
        {
            if (LogItemsControl is null || LogScrollViewer is null)
            {
                return;
            }

            // 淘汰可能发生在中间（要跳过 Sticky 行），所以按 buffer 给的下标删，
            // 两边才不会错位。
            if (removedIndex >= 0 && removedIndex < LogItemsControl.Items.Count)
            {
                DetachLogBlock(LogItemsControl.Items[removedIndex]);
                LogItemsControl.Items.RemoveAt(removedIndex);
            }

            LogItemsControl.Items.Add(CreateLogBlock(kind, displayText, copyText, sticky));
            RequestLogScrollToBottom();
        }

        /// <summary>整棵重建，只用于清空、切换过滤这种一次性场景。</summary>
        private void RenderLogItems()
        {
            if (LogItemsControl is null || LogScrollViewer is null)
            {
                return;
            }

            foreach (var item in LogItemsControl.Items)
            {
                DetachLogBlock(item);
            }

            LogItemsControl.Items.Clear();
            foreach (var entry in _logPanel.Entries)
            {
                LogItemsControl.Items.Add(CreateLogBlock(
                    ToLogKind(entry.Kind), entry.DisplayText, entry.CopyText, entry.Sticky));
            }

            RequestLogScrollToBottom();
        }

        private void DetachLogBlock(object? item)
        {
            if (item is Border border)
            {
                // 不摘事件，滚出窗口的旧条目会一直被 Tapped 委托拉住不放。
                border.Tapped -= LogBlock_Tapped;
            }
        }

        /// <summary>
        /// 把「滚到底」合并成一次低优先级派发。ScrollViewer.UpdateLayout() 是同步整树排版，
        /// 日志刷屏时逐行调用会独占 UI 线程；低优先级则保证排版完成后才滚。
        /// </summary>
        private void RequestLogScrollToBottom()
        {
            if (_logScrollToBottomPending || LogScrollViewer is null)
            {
                return;
            }

            _logScrollToBottomPending = true;
            if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, ScrollLogToBottom))
            {
                _logScrollToBottomPending = false;
            }
        }

        private void ScrollLogToBottom()
        {
            _logScrollToBottomPending = false;
            LogScrollViewer?.ChangeView(null, LogScrollViewer.ScrollableHeight, null, disableAnimation: true);
        }

        private Border CreateLogBlock(LogKind kind, string displayText, string copyText, bool sticky)
        {
            var block = new TextBlock
            {
                Text = displayText,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Consolas"),
                // Sticky 行（批次首行、步骤标题行）加粗，一眼能从明细里挑出来。
                FontWeight = sticky ? FontWeights.SemiBold : FontWeights.Normal,
                IsTextSelectionEnabled = false,
                Style = GetLogTextStyle(kind)
            };

            var border = new Border
            {
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(8, 6, 8, 6),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                Child = block,
                Tag = copyText,
                Style = GetLogBlockStyle(kind)
            };
            border.Tapped += LogBlock_Tapped;
            return border;
        }

        private void LogBlock_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (sender is Border { Tag: string text })
            {
                var clipboardText = TrimLogTextForClipboard(text);
                CopyTextToClipboard(clipboardText);
                ShowFloatingTip(InfoBarSeverity.Success, "已复制日志", "已复制这一条日志记录。");
                e.Handled = true;
            }
        }

        private static string TrimLogTextForClipboard(string text)
        {
            const int maxClipboardLogLength = 12000;
            if (text.Length <= maxClipboardLogLength)
            {
                return text;
            }

            return text[..maxClipboardLogLength] +
                $"{Environment.NewLine}... log trimmed for clipboard ({text.Length - maxClipboardLogLength} more characters in UI)";
        }

        private static void CopyTextToClipboard(string text)
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        }

        private void ShowFloatingTip(InfoBarSeverity severity, string title, string message)
        {
            var tip = new InfoBar
            {
                Severity = severity,
                Title = title,
                Message = message,
                IsOpen = true,
                IsClosable = true,
                RenderTransform = new TranslateTransform { Y = -18 },
                Opacity = 0
            };

            FloatingTipsPanel.Children.Add(tip);
            PlayFloatingTipEntrance(tip);

            var timer = DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromSeconds(severity == InfoBarSeverity.Error ? 5 : 2.6);
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                _floatingTipTimers.Remove(tip);
                FloatingTipsPanel.Children.Remove(tip);
            };
            _floatingTipTimers[tip] = timer;
            timer.Start();
        }

        private void PlayFloatingTipEntrance(InfoBar tip)
        {
            var steps = 0;
            var timer = DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(16);
            timer.Tick += (_, _) =>
            {
                steps++;
                var progress = Math.Min(1, steps / 12.0);
                var eased = 1 - Math.Pow(1 - progress, 3);
                tip.Opacity = eased;
                if (tip.RenderTransform is TranslateTransform transform)
                {
                    transform.Y = -18 + 18 * eased;
                }

                if (progress >= 1)
                {
                    timer.Stop();
                }
            };
            timer.Start();
        }

        private static string GetLogKindLabel(LogKind kind)
        {
            return kind switch
            {
                LogKind.User => "User",
                LogKind.Warning => "Warning",
                LogKind.Error => "Error",
                _ => "Log"
            };
        }

        private static string FormatExceptionForLog(Exception exception, int? stackTraceLineLimit = null)
        {
            try
            {
                var lines = new List<string>();
                for (Exception? current = exception; current is not null; current = current.InnerException)
                {
                    var typeName = current.GetType().FullName ?? current.GetType().Name;
                    var message = string.IsNullOrWhiteSpace(current.Message) ? "<empty message>" : current.Message;
                    var hResult = current.HResult == 0 ? string.Empty : $" HRESULT=0x{current.HResult:X8}";
                    lines.Add($"    Exception={typeName}{hResult}");
                    foreach (var messageLine in message.Split(["\r\n", "\n", "\r"], StringSplitOptions.None))
                    {
                        lines.Add($"    Message={messageLine}");
                    }

                    if (current is COMException comException)
                    {
                        lines.Add($"    COMErrorCode=0x{comException.ErrorCode:X8}");
                    }

                    try
                    {
                        foreach (var key in current.Data.Keys)
                        {
                            lines.Add($"    Data[{key}]={current.Data[key]}");
                        }
                    }
                    catch (Exception dataException)
                    {
                        lines.Add($"    DataReadError={dataException.GetType().Name}: {dataException.Message}");
                    }
                }

                if (!string.IsNullOrWhiteSpace(exception.StackTrace))
                {
                    lines.Add("    StackTrace:");
                    var stackTraceLines = exception.StackTrace
                        .Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries)
                        .Select(line => $"        {line.Trim()}")
                        .ToList();
                    var emittedStackTraceLines = stackTraceLineLimit is null
                        ? stackTraceLines
                        : stackTraceLines.Take(stackTraceLineLimit.Value).ToList();
                    lines.AddRange(emittedStackTraceLines);
                    if (stackTraceLineLimit is not null && stackTraceLines.Count > stackTraceLineLimit.Value)
                    {
                        lines.Add($"        ... stack trace trimmed for clipboard ({stackTraceLines.Count - stackTraceLineLimit.Value} more lines in UI)");
                    }
                }

                return string.Join(Environment.NewLine, lines);
            }
            catch (Exception formattingException)
            {
                // 日志格式化不能遮蔽原始异常；任何异常对象都至少要留下类型和消息。
                var typeName = exception.GetType().FullName ?? exception.GetType().Name;
                var message = exception.Message ?? "<empty message>";
                return $"    Exception={typeName}\n    Message={message}\n" +
                    $"    LogFormattingError={formattingException.GetType().Name}: {formattingException.Message}";
            }
        }

        private static Style GetLogTextStyle(LogKind kind)
        {
            return Application.Current.Resources[kind switch
            {
                LogKind.User => "LogUserTextStyle",
                LogKind.Warning => "LogWarningTextStyle",
                LogKind.Error => "LogErrorTextStyle",
                _ => "LogDefaultTextStyle"
            }] as Style ?? throw new InvalidOperationException("日志文字样式资源不可用。");
        }

        private static Style GetLogBlockStyle(LogKind kind)
        {
            return Application.Current.Resources[kind switch
            {
                LogKind.User => "LogUserBlockStyle",
                LogKind.Warning => "LogWarningBlockStyle",
                LogKind.Error => "LogErrorBlockStyle",
                _ => "LogDefaultBlockStyle"
            }] as Style ?? throw new InvalidOperationException("日志容器样式资源不可用。");
        }

        /// <summary>当前日志批次号；还没开过批次时是 <c>-</c>。</summary>
        private string CurrentLogRunId => _currentLogRunId ?? RuntimeLogFormat.NoRunId;

        /// <summary>
        /// 这一行日志属于哪一次流程、哪一步。
        /// 导入方向没有分步流程，步骤记 0（面板上也就不显示 [StN]）。
        /// </summary>
        private RuntimeLogScope CurrentLogScope => new(CurrentLogRunId, CurrentWorkflowStepForLog());

        /// <summary>
        /// 这一行归哪一段。步骤标题行说的是**它指的那一步**，
        /// 不是「写这条日志时当前停在哪一步」——离开第三步时当前步已经是第四步了。
        /// </summary>
        private RuntimeLogScope ScopeFor(int? stepOverride) =>
            stepOverride is { } step ? new RuntimeLogScope(CurrentLogRunId, step) : CurrentLogScope;

        private int CurrentWorkflowStepForLog()
        {
            var sync = _applicationViewModel.UnrealProjectSync;
            return sync.IsEngineToToolbox ? 0 : Math.Clamp(sync.WorkflowStep, 0, UnrealSyncWorkflow.MaxStep);
        }

        /// <summary>
        /// 开一批新日志。批首是一条 Sticky 行，记下这次是哪个角色、哪个方向——
        /// 以后再翻 runtime.log，一个 RunId 就能把这批上下文凑齐。
        /// </summary>
        private void BeginLogRun(string trigger)
        {
            var sync = _applicationViewModel.UnrealProjectSync;
            _currentLogRunId = RuntimeLogFormat.CreateRunId(DateTimeOffset.Now);
            var character = sync.SelectedSource?.DraftCharacter;
            var characterText = character is null ? "未选择" : $"{character.Name}（{character.Code}）";
            AppendLog(
                LogKind.Info,
                RuntimeLogFormat.FormatRunHeader(_currentLogRunId, characterText, sync.DirectionTitle),
                sticky: true);
            AppendDiagnosticLog(LogKind.Info, $"[Run] 批次开始：{trigger}");
        }

        /// <summary>
        /// 盯着当前步骤的变化，进/出各写一条标题行。
        ///
        /// 这一步是「接不上」的正解：中间的明细会被面板挤掉，但标题行是 Sticky，
        /// 一次跑完从面板上仍能顺着 ▶1 ■1 ▶2 ■2 … 看完整条路径。
        /// </summary>
        private void AttachWorkflowStepLog()
        {
            var sync = _applicationViewModel.UnrealProjectSync;
            _lastLoggedWorkflowStep = 0;
            sync.PropertyChanged += (_, e) =>
            {
                if (string.Equals(
                        e.PropertyName,
                        nameof(UnrealProjectSyncViewModel.IsEngineToToolbox),
                        StringComparison.Ordinal))
                {
                    // 换方向等于换了一套东西：清掉上一条记录，换成新方向的第一步。
                    _lastLoggedWorkflowStep = 0;
                    LogWorkflowStepTransition(sync.WorkflowStep);
                    return;
                }

                if (!string.Equals(
                        e.PropertyName,
                        nameof(UnrealProjectSyncViewModel.WorkflowStep),
                        StringComparison.Ordinal))
                {
                    return;
                }

                LogWorkflowStepTransition(sync.WorkflowStep);
            };

            // 启动时先补一条当前步的开始行，否则第一段路只有「离开时」的记录。
            LogWorkflowStepTransition(sync.WorkflowStep);
        }

        private void LogWorkflowStepTransition(int step)
        {
            if (step == _lastLoggedWorkflowStep)
            {
                return;
            }

            var sync = _applicationViewModel.UnrealProjectSync;
            var previous = _lastLoggedWorkflowStep;
            _lastLoggedWorkflowStep = step;

            // 导入方向没有分步流程，写「第 N 步」只会让人以为走的是六步发布流程。
            if (sync.IsEngineToToolbox)
            {
                return;
            }

            if (previous is >= UnrealSyncWorkflow.MinStep and <= UnrealSyncWorkflow.MaxStep)
            {
                AppendLog(
                    LogKind.Info,
                    RuntimeLogFormat.FormatStepEnd(previous, sync.WorkflowStepConclusionText(previous)),
                    sticky: true,
                    stepOverride: previous);
            }

            if (step is >= UnrealSyncWorkflow.MinStep and <= UnrealSyncWorkflow.MaxStep)
            {
                AppendLog(
                    LogKind.Info,
                    RuntimeLogFormat.FormatStepStart(step, sync.WorkflowStepNameFor(step)),
                    sticky: true,
                    stepOverride: step);
            }
        }

        /// <summary>
        /// 某一批流程走完时补一条结束行。
        ///
        /// 前五步的结束行在「离开这一步」时就写了；第六步是最后一步，没有下一步可走，
        /// 所以它的收尾动作（蓝图置入写入成功）要自己报一次。
        /// </summary>
        private void LogWorkflowStepFinished(int step)
        {
            var sync = _applicationViewModel.UnrealProjectSync;
            AppendLog(
                LogKind.Info,
                RuntimeLogFormat.FormatStepEnd(step, sync.WorkflowStepConclusionText(step)),
                sticky: true,
                stepOverride: step);
        }

        private static LogEntryKind ToLogEntryKind(LogKind kind) => kind switch
        {
            LogKind.User => LogEntryKind.User,
            LogKind.Warning => LogEntryKind.Warning,
            LogKind.Error => LogEntryKind.Error,
            _ => LogEntryKind.Info,
        };

        private static LogKind ToLogKind(LogEntryKind kind) => kind switch
        {
            LogEntryKind.User => LogKind.User,
            LogEntryKind.Warning => LogKind.Warning,
            LogEntryKind.Error => LogKind.Error,
            _ => LogKind.Info,
        };

    }
}
