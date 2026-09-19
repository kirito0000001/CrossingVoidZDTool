using System;

namespace CrossingVoidZDTool.ViewModels;

/// <summary>
/// 「六步各自有没有可用数据」的唯一真相（P5 收尾）。
///
/// 以前这几件事散在四个字段里：`_isNormalizationStepLoaded`、
/// `_isLightConfigurationLoaded`、`_isBlueprintSetupLoaded`、
/// 加上 `_hasImportDetection` + `_loadedPublishStep` 这一对。散着放有两个后果：
///
/// 1. 「第三步和第五步共用同一棵差异树、但同一时刻只能归一个」这条不变式
///    没有任何地方写下来，只有 `_hasImportDetection && _loadedPublishStep == 3` 这种
///    成对判断，散在 ViewModel 和 WorkspaceState 两处；
/// 2. 想回答「现在是哪一步有数据」得同时读四个字段，加一步就得再记一个字段。
///
/// 现在它们住在这里：按步号取，越界的步号直接抛（宁可当场炸，也不要悄悄当成"没加载"）。
/// </summary>
internal sealed class UnrealSyncStepLoadStore
{
    private readonly bool[] _loaded = new bool[UnrealSyncWorkflow.MaxStep + 1];
    private bool _hasPublishTree;
    private int _publishTreeOwnerStep;

    /// <summary>
    /// 这一步现在有没有可用数据。
    /// 第三、五步问的是同一棵树归谁；其余步看各自的标志。
    /// </summary>
    public bool IsLoaded(int step) => step is 3 or 5
        ? _hasPublishTree && _publishTreeOwnerStep == step
        : _loaded[ToKnownStep(step)];

    /// <summary>差异树本身跑过没有（导入方向也用它，那时树不归任何步）。</summary>
    public bool HasPublishTree => _hasPublishTree;

    /// <summary>这棵树归第几步；0 表示还没有归属（没检测过，或只用于导入方向）。</summary>
    public int PublishTreeOwnerStep => _publishTreeOwnerStep;

    /// <summary>第二步 / 第四步 / 第六步的加载标志。返回是否真的变了。</summary>
    public bool SetLoaded(int step, bool value)
    {
        if (step is not (2 or 4 or 6))
        {
            throw new ArgumentOutOfRangeException(
                nameof(step), step, "第一、三、五步的加载状态不是独立标志（第一步看内容、三五步看差异树）。");
        }

        if (_loaded[step] == value)
        {
            return false;
        }

        _loaded[step] = value;
        return true;
    }

    /// <summary>差异树存在，但归属不动（导入方向选完树、复扫结果回填）。</summary>
    public bool MarkPublishTree()
    {
        if (_hasPublishTree)
        {
            return false;
        }

        _hasPublishTree = true;
        return true;
    }

    /// <summary>差异树跑完了，并且归这一步（只可能是第三或第五步）。</summary>
    public bool ClaimPublishTree(int step)
    {
        if (step is not (3 or 5))
        {
            throw new ArgumentOutOfRangeException(
                nameof(step), step, "差异树只可能归第三或第五步——这两步共用同一棵，范围不同。");
        }

        if (_hasPublishTree && _publishTreeOwnerStep == step)
        {
            return false;
        }

        _hasPublishTree = true;
        _publishTreeOwnerStep = step;
        return true;
    }

    /// <summary>丢掉差异树：换角色、换方向、缓存作废时用。</summary>
    public bool ClearPublishTree()
    {
        if (!_hasPublishTree && _publishTreeOwnerStep == 0)
        {
            return false;
        }

        _hasPublishTree = false;
        _publishTreeOwnerStep = 0;
        return true;
    }

    public void Reset()
    {
        Array.Clear(_loaded);
        _hasPublishTree = false;
        _publishTreeOwnerStep = 0;
    }

    private static int ToKnownStep(int step) =>
        step is >= UnrealSyncWorkflow.MinStep and <= UnrealSyncWorkflow.MaxStep
            ? step
            : throw new ArgumentOutOfRangeException(nameof(step), step, "步号超出 1~6。");
}
