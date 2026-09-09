namespace CrossingVoidZDTool.Services;

/// <summary>
/// 序列帧的硬性规格：成品分辨率、默认帧率、单帧最长停留。
///
/// 单独放一份，是因为拆分之后清单读写、帧格组装、快照恢复都要用这几个数字。
/// 它们原本挂在 <see cref="SequenceFrameService"/> 上，那样每个下层小类都要反过来
/// 依赖门面，等于把刚拆开的方向又粘回去。
///
/// <see cref="SequenceFrameService"/> 仍然按原名把它们转发出去——回归用例和四个
/// Unreal 侧服务都是按 <c>SequenceFrameService.RequiredWidth</c> 这样的名字取值的。
/// </summary>
internal static class SequenceFrameSpec
{
    /// <summary>成品帧的唯一合规尺寸。不符合的帧不拦截导入，只在界面上标红。</summary>
    public const int RequiredWidth = 928;

    public const int RequiredHeight = 640;

    public const int DefaultFps = 12;

    /// <summary>单帧最多能停留多少个帧位。</summary>
    public const int MaxFrameDuration = 600;
}
