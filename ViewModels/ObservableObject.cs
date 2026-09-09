namespace CrossingVoidZDTool.ViewModels;

/// <summary>
/// 全项目 ViewModel 的基类。
///
/// 原来这是一份三十行的手写实现（<c>SetProperty</c> + <c>OnPropertyChanged</c>）。
/// 现在改成继承 CommunityToolkit.Mvvm 的同名基类——两边的
/// <c>SetProperty&lt;T&gt;(ref T, T, [CallerMemberName] string?)</c> 和
/// <c>OnPropertyChanged([CallerMemberName] string?)</c> 签名一致，所以是平滑替换，
/// 现有几百处调用一行都不用改。
///
/// 换过来是为了拿到 <c>[ObservableProperty]</c> 和 <c>[NotifyPropertyChangedFor]</c>
/// 这两个源生成器。真正想要的是后者：它把「哪个派生属性依赖哪个字段」
/// 从注释变成**编译期声明**。
///
/// 这个项目已经因为「某条路径忘了通知」踩过两次——中栏一片空白、
/// 第三五步的勾选计数不刷新。手工列举通知是守不住的：
/// <c>UnrealProjectSyncViewModel</c> 里一个 setter 曾经要手写三十多条
/// <c>OnPropertyChanged</c>，其中一条重复、两行缩进错位，正是维护疲劳的痕迹。
///
/// **但目前一处都还没用上。** 换基类本身是零行为变化的，几百处
/// <c>SetProperty</c> / <c>OnPropertyChanged</c> 调用一行没改；
/// 源生成器留着给后面增量采用——最该先用的是
/// <c>UnrealProjectSyncViewModel.WorkflowStep</c> 那个三十二条通知的 setter，
/// 但它的 setter 里还夹着钳位和落盘，不是照搬属性就能换的，
/// 所以没有在这次重构的收尾阶段动它。
///
/// 只用源生成器这一半；<c>Ioc</c> 和 <c>Messenger</c> 一律不碰——
/// 单人维护的工具不需要，引进来只会多一层间接。
/// </summary>
public abstract class ObservableObject : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
}
