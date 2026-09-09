using System.Collections.Generic;

namespace CrossingVoidZDTool.Controls;

/// <summary>
/// 全屏遮罩层必须能关掉——这份清单是给回归用例比对用的。
///
/// 工具箱里有十来个自建的全屏遮罩层（角色详情、BUFF 编辑、序列帧管理器……），
/// 它们不是 <c>ContentDialog</c>，而是铺满窗口的 <c>Grid</c>，
/// 所以关闭手势得自己接。新增一个遮罩层却忘了接，用户就只能重启程序——
/// 而这种事没人会去逐个点一遍。回归用例拿这份清单和 MainWindow.xaml 比对，
/// 漏了会直接红。
///
/// **这里刻意不提供「一把梭挂上全部手势」的辅助方法。**
/// 曾经想把那四十多个重复的处理器收敛掉，做的时候才发现各遮罩层的手势
/// 本来就不一样，而且差异是有道理的：
/// <list type="bullet">
/// <item>草稿层点外面只吞掉事件、不关闭——正在写字，误触不该把它关了</item>
/// <item>裁切器和图片查看器只认右键关</item>
/// <item>序列帧管理器只认左键点外面关</item>
/// <item>创建角色对话框、去重解决器的 Esc 还要兼顾 Enter；
///       序列帧管理器的 Esc 是两段式（先取消子模式）</item>
/// </list>
/// 统一成一套手势会悄悄改掉这些行为，而它们一条 UI 测试都没有。
/// 去重的收益盖不过这个风险，所以只留下这份清单当护栏。
/// 等以后这些手势有了测试覆盖，再谈收敛。
/// </summary>
internal static class OverlayDismiss
{
    /// <summary>
    /// 必须支持某种关闭手势（左键点外面 / 右键 / Esc 至少有一种）的遮罩层。
    ///
    /// <c>GlobalProgressHost</c> 不在其中：进度遮罩不能靠点一下关掉，
    /// 它得等任务结束或者用户按取消。
    /// </summary>
    public static readonly IReadOnlyList<string> DismissibleOverlayNames =
    [
        "BaseMaterialCropHost",
        "BuffEditorHost",
        "CharacterCreateDialogHost",
        "CharacterDetailHost",
        "DraftOverlayHost",
        "ReferenceImageViewerHost",
        "SequenceFrameDuplicateResolverHost",
        "SequenceFramesCollectionHost",
        "SequenceFramesManagerHost",
        "SkillIconPickerHost",
        "SkillValueGuideOverlayHost",
        "VoiceMaterialManagerHost",
    ];

    /// <summary>不需要关闭手势的遮罩层，以及为什么。</summary>
    public static readonly IReadOnlyDictionary<string, string> NonDismissibleOverlays =
        new Dictionary<string, string>
        {
            ["GlobalProgressHost"] = "进度遮罩要等任务结束或用户按取消，不能点一下就关",
        };
}
