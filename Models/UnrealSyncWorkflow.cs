namespace CrossingVoidZDTool;

/// <summary>
/// 虚幻同步台的流程步数。加新步骤时只改这一处。
///
/// 步号在好几个地方会被夹到合法区间，之前这个上限散落着写死成 5：
/// 「蓝图置入」刚接上时，点「下一步」会被静默夹回上一步，
/// 界面停在原地却已经开始跑虚幻检测，看着就像按钮直接执行了操作。
/// </summary>
internal static class UnrealSyncWorkflow
{
    public const int MinStep = 1;

    /// <summary>
    /// 第六步要不要露在流程里。
    ///
    /// 晓桀 2026-09-25：「先把特效这一步隐藏起来吧，可能之后不会用了」。
    /// **隐藏 ≠ 删掉**：代码、各自的缓存文件（`step6-effect-sync.json`）、中栏面板、
    /// 桥接脚本、`smoke --effect-sync` 全都留着，改回 `true` 就整条回来
    /// —— 别为了隐藏去拆那些东西。
    /// </summary>
    public const bool IncludesEffectSyncStep = false;

    /// <summary>
    /// 1 底层检测、**2 同步素材**（= 旧的「规整素材」+「同步素材」合并，2026-09-24）、
    /// 3 基础配置、4 序列同步、5 蓝图置入、**6 特效同步**。
    /// （合并后 4~7 整体前移成 3~6，2026-09-24 收口。）
    ///
    /// 第六步是**独立的一步**，不是第四步的一部分：第四步只管角色序列（图集/精灵/Flipbook/序列/AnimMaps），
    /// 特效那套（网格 sheet + SubUV 材质实例 + 共享 Niagara 面片系统）走第六步自己的检测与同步。
    /// 用户当时的话（步号按当时的编号）："我最早说的是在第七步同步，不要给第五步压得太重"。
    /// </summary>
    public const int MaxStep = 6;

    /// <summary>
    /// **界面上**的最后一步：导航（下一步 / 上一步 / 依次检测）、「下一步」按钮的置灰，
    /// 以及"是不是最后一步"的通知，认的都是它。
    ///
    /// 和 <see cref="MaxStep"/> 分开是有意的：`MaxStep` 是**代码认识的步号范围**（钳位用），
    /// 隐藏的步骤仍然落在里面；这一条才是"用户看得到几步"。
    /// 隐藏特效时它就是 5（蓝图置入）—— 走到那儿就是流程结束。
    /// </summary>
    public const int LastVisibleStep = IncludesEffectSyncStep ? MaxStep : 5;
}
