using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;
using Microsoft.UI.Xaml.Controls;

namespace CrossingVoidZDTool.ViewModels;

/// <summary>
/// 「同步素材到虚幻」这条编排需要界面那侧提供的东西（B4）。
///
/// 目的与 <see cref="IUnrealSyncWorkflowHost"/> 一样：让编排本身不依赖 MainWindow
/// 就能跑。这张表是**量出来的**——把 <c>PublishCurrentCharacterAssetsToUnrealAsync</c>
/// 的方法体扫一遍，列出它真正调用到的壳成员；方法体本身现在还在 MainWindow 里，
/// 这一批只把接口定下来并让 MainWindow 实现，**流程一行没动**。
///
/// 注意几件容易漏的事：
/// - <c>Settings</c>（`BackupBeforeUnrealSync`）是**属性**，不在「字段 + 方法」的扫描结果里；
/// - <c>ValidatePublishCharacterFolders</c> / <c>ValidateSequenceCharacterFolders</c>
///   名字很像壳成员，其实是 ViewModel 自己的方法，**不属于这张表**；
/// - 接口里只放壳能力，VM 的方法（`Detect`、`SetLoadedPublishStep` 之类）留在编排里直接调。
/// </summary>
internal interface IUnrealSyncPublishHost
{
    SettingsViewModel Settings { get; }

    bool IsPublishRunning { get; set; }

    /// <summary>第三/五步里「这棵树归谁」的目标步号，检测流程会读它。</summary>
    int WorkflowStepAfterPublishDetection { get; set; }

    void AppendLog(LogKind kind, string message, Exception? exception = null, bool sticky = false, int? stepOverride = null);

    void AppendDiagnosticLog(LogKind kind, string message);

    void AppendRuntimeLog(string line);

    void LogUserOperation(string action, bool startsRun = false);

    /// <summary>把日志里的值压成一行短文本（静态纯函数，壳上唯一的那一份）。</summary>
    string FormatSyncLogValue(string? value, int maxLength = 180);

    void LogExportWarning(UnrealProjectSyncExportRunResult result);

    void LogSequenceChanges(string prefix, IEnumerable<UnrealBridgeChange> changes);

    void LogLightConfigurationPreflight(string characterCode, UnrealLightConfigurationResult result);

    void ShowFloatingTip(InfoBarSeverity severity, string title, string message);

    void ShowGlobalProgress(string title, string detail);

    void UpdateGlobalProgress(string message, double percent, string? detail = null, bool isIndeterminate = false);

    void CompleteGlobalProgress(string message, string? detail = null);

    Task HideGlobalProgressAfterDelayAsync(int delayMilliseconds = 1400);

    CancellationToken GetGlobalProgressCancellationToken();

    bool TryBeginUnrealWorkflowOperation();

    void EndUnrealWorkflowOperation();

    Task DetectUnrealPublishChangesAsync();

    Task BackupUnrealProjectIfRequestedAsync(
        string enginePath,
        string projectPath,
        string characterCode,
        bool planTouchesExistingAssets,
        WorkflowProgressBand band = default);

    Task<UnrealLightConfigurationResult> ExecuteUnrealLightConfigurationAsync(
        CharacterCard character,
        bool apply,
        IReadOnlyCollection<string> selectedStableIds,
        WorkflowProgressPlan? progressPlan = null);

    bool TrySkipRescanExport(string manifestPath, DateTime syncStartedAtUtc);
}
