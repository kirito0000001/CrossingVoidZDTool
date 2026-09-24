using System;
using System.Collections.Generic;
using System.Linq;
using CrossingVoidZDTool.Services;

namespace CrossingVoidZDTool
{
    /// <summary>
    /// 第 4 步「序列同步」自己的壳侧那一块。
    ///
    /// 这一步和第 2 步（同步素材）、第 6 步（特效同步）**共用**同一条检测/发布链路
    /// （`MainWindow.UnrealSync.Publish.cs` + `UnrealSyncPublishController`）—— 那是刻意的：
    /// 同一次导出、同一棵差异树、只是范围和计划不同。所以**共用的编排留在那儿**，
    /// 而**只服务序列帧**的东西（比如这条日志）收到这里，别让共享文件越养越胖。
    /// </summary>
    public sealed partial class MainWindow
    {
        private void LogSequenceChanges(string prefix, IEnumerable<UnrealBridgeChange> changes)
        {
            var count = 0;
            foreach (var change in changes.Where(item => item.Module == UnrealBridgeModule.SequenceFrames))
            {
                var canExecute = UnrealBridgePublishSupportPolicy.CanExecute(change);
                var toolboxValue = FormatSyncLogValue(change.ToolboxItem?.PayloadJson);
                var unrealValue = FormatSyncLogValue(change.UnrealItem?.PayloadJson);
                AppendDiagnosticLog(LogKind.Info,
                    $"{prefix} stableId={change.StableId} kind={change.Kind} selected={change.IsSelected} canExecute={canExecute} group={FormatSyncLogValue(change.SequenceGroupKey)} display={FormatSyncLogValue(change.DisplayName)} toolbox={toolboxValue} unreal={unrealValue}");
                count++;
            }

            if (count > 0)
            {
                AppendLog(LogKind.Info, $"{prefix} 共 {count} 条明细，已写入 runtime.log。");
            }
        }
    }
}
