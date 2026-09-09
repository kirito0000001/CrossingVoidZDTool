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
            _logLines.Clear();
            LogItemsControl.Items.Clear();
            AppendLog(LogKind.User, "已清空输出日志。");
        }

        private void CopyAllLogButton_Click(object sender, RoutedEventArgs e)
        {
            var text = string.Join(Environment.NewLine + Environment.NewLine, _logLines.Select(item => item.CopyText));
            CopyTextToClipboard(text);
            ShowFloatingTip(InfoBarSeverity.Success, "已复制全部日志", $"{_logLines.Count} 条记录");
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

        private void LogUserOperation(string action)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                return;
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
                    Directory.CreateDirectory(Path.GetDirectoryName(_runtimeLogPath)!);
                    File.AppendAllText(
                        _runtimeLogPath,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {line}{Environment.NewLine}");
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

            AppendRuntimeLog($"[{DateTime.Now:HH:mm:ss}] LogZDTool: {GetLogKindLabel(kind)}: {message}");
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

        private void AppendLog(LogKind kind, string message, Exception? exception = null)
        {
            if (!ShouldWriteLog(kind))
            {
                return;
            }

            var header = $"[{DateTime.Now:HH:mm:ss}] LogZDTool: {GetLogKindLabel(kind)}: {message}";
            var displayText = header;
            var copyText = header;
            if (exception is not null)
            {
                displayText += $"{Environment.NewLine}{FormatExceptionForLog(exception)}";
                copyText += $"{Environment.NewLine}{FormatExceptionForLog(exception, stackTraceLineLimit: 8)}";
            }

            AppendRuntimeLog(displayText.Replace(Environment.NewLine, " | "));
            _logLines.Enqueue((kind, displayText, copyText));

            var removedCount = 0;
            while (_logLines.Count > MaxUiLogCount)
            {
                _logLines.Dequeue();
                removedCount++;
            }

            AppendLogItem(kind, displayText, copyText, removedCount);
        }

        /// <summary>
        /// 只追加新的一条、并摘掉溢出的旧条目。
        /// 以前每写一行日志都要 Items.Clear() 再重建满 300 个 Border，外加一次同步排版，
        /// 单行实测 400ms；一次第五步检测有上千行日志，界面就会整个僵住十几分钟。
        /// </summary>
        private void AppendLogItem(LogKind kind, string displayText, string copyText, int removedCount)
        {
            if (LogItemsControl is null || LogScrollViewer is null)
            {
                return;
            }

            for (var index = 0; index < removedCount && LogItemsControl.Items.Count > 0; index++)
            {
                DetachLogBlock(LogItemsControl.Items[0]);
                LogItemsControl.Items.RemoveAt(0);
            }

            LogItemsControl.Items.Add(CreateLogBlock(kind, displayText, copyText));
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
            foreach (var (kind, displayText, copyText) in _logLines)
            {
                LogItemsControl.Items.Add(CreateLogBlock(kind, displayText, copyText));
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

        private Border CreateLogBlock(LogKind kind, string displayText, string copyText)
        {
            var block = new TextBlock
            {
                Text = displayText,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Consolas"),
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

    }
}
