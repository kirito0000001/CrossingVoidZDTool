# Codex 开发速查规约

项目：`D:\UnrealMap\CrossingVoidZDTool`

这份文件给之后的 Codex / 维护者快速对齐用。进项目先读它，按这里的规则写。

## 一句话原则

这是一个长期维护的 WinUI 3 角色制作工具箱，不是一次性脚本。

默认目标：

- 少点按
- 强快捷键
- 清晰进度
- 可取消
- 可预览
- 不乱改用户素材
- 不把业务逻辑塞进 `MainWindow.xaml.cs`

## 必守分层

```text
Models      数据结构、序列化模型、结果对象
Services    文件、图片、线稿、编号、导入导出、Unreal 同步等业务逻辑
ViewModels  页面状态、命令、校验、选择项、调用 Service
Views       UI 工厂、卡片、对话框内容、可复用控件
Styles      共享 XAML 样式
Docs        给 Codex 和维护者看的规则
```

当前根框架：

- `ApplicationViewModel` 聚合 `SettingsViewModel`、`GlobalProgressViewModel`、`CharacterDeskViewModel`、`ActionFramesViewModel`、`LineArtViewModel`、`UnrealSyncViewModel`。
- `CharacterDeskViewModel` 管零境角色台和 `St1-设计理念` 共享状态：角色卡、当前制作角色、上次编辑角色、立绘入口、草稿文本、延迟保存状态和参考图列表。
- `MainWindow.xaml.cs` 是组合根，只保留启动连接、标题栏、图标、窗口放置和 ViewModel 暴露属性。
- `MainWindow.Navigation.cs` 管导航和页面动画。
- `MainWindow.Settings.cs` 管需要窗口句柄的文件夹选择器、项目根目录帮助弹窗和迁移进度桥接。
- `MainWindow.Progress.cs` 管底部全局进度条动画、取消、耗时和圆环几何。
- `MainWindow.Logging.cs` 管辅助显示刷新、输出日志面板、log 帮助弹窗和窗口层日志写入桥接。
- 新增功能如果需要新的状态，先加到对应模块 ViewModel；如果需要规则，先加到 Service。

`MainWindow.xaml.cs` 只做外壳和 WinUI 桥接：

- 可以做导航切换
- 可以连接 ViewModel / Service
- 可以处理 WinUI 只能在 code-behind 做的事
- 不写文件扫描、线稿生成、编号规则、导入导出、同步 Unreal 等业务逻辑

## MVVM 规则

- ViewModel 继承 `ObservableObject`。
- 同步命令用 `RelayCommand`。
- 访问磁盘、批处理、生成图片、导入导出、同步 Unreal 的命令用 `AsyncRelayCommand`。
- 异步命令默认不允许并发。
- 长任务必须支持 `CancellationToken`。
- ViewModel 暴露状态，不暴露控件。
- Service 返回结果对象，不直接操作 UI。
- 文件编号、路径推导、命名规则必须能脱离 UI 测试。

推荐调用链：

```text
XAML/Button -> ViewModel Command -> Service -> Result -> ViewModel State -> Binding
```

## UI 外壳规则

沿用 GalExcleTools 手感：

- 顶部 48 px 标题栏
- 左侧 compact `NavigationView`
- 主区域是实际工具，不做宣传首页
- 底部保留状态 / 进度区
- 高频功能直接露出
- 低频设置放设置页或折叠区

左侧导航顺序：

- 第一栏是 `零境角色台`，用于查看已创建角色卡和选择当前制作角色；这里不能新建角色卡。
- 第二栏是 `St1-设计理念`，用于新建角色卡、当前角色的立绘入口、设计草稿和参考图；进入 St1 时先看到立绘卡，点击立绘卡后才进入草稿页面。
- 后续步骤页都显示当前制作角色，但不负责新建角色卡。
- St1、St2 等步骤页一旦进入真实编辑状态，要记录最后编辑的步骤页；角色台里的“继续”入口先同步选中左侧对应大栏，再回到最后编辑的页面，不要再额外做第二个继续按钮。

页面布局优先顺序：

```text
标题和当前状态
主要工具栏
核心工作区
属性/详情区
底部状态和进度
```

虚幻同步台固定使用三栏工作区：

- 左栏直接显示来源搜索和角色列表，并使用自己的滚动区域。
- 中栏只承载检测结果与选择树，并使用自己的滚动区域。
- 右栏只承载项目关联、选择影响、执行操作和最近结果，并使用自己的滚动区域。
- 三栏内部滚动不能复用或改写其他步骤页的滚动位置。
- 从虚幻导入和同步到虚幻共用“检测、选择影响、执行、结果”四段状态，不再维护隐藏的旧预览页面。
- 项目通用素材在尚未实现时必须明确标记为待支持，不能表现为已经可执行的来源。

## 样式规则

优先复用 `Styles/ToolboxStyles.xaml`：

- `PanelBorderStyle`：页面面板
- `ToolbarBorderStyle`：工具栏、底部状态栏
- `PageTitleStyle`：页面标题
- `SectionTitleStyle`：分区标题
- `SubtleTextStyle`：次要说明
- `ToolCardStyle`：重复项目卡片
- `IconToolButtonStyle`：普通图标按钮
- `CompactIconButtonStyle`：紧凑图标按钮
- `HelpIconButtonStyle`：帮助按钮
- `PrimaryToolButtonStyle`：主要文字按钮

不要做：

- 卡片套卡片
- 页面装饰性浮动卡片
- 大圆角视觉玩具
- 一堆页面内临时重复样式
- 无 tooltip 的图标按钮

## 按钮规则

图标按钮用于：

- 新建
- 刷新
- 返回
- 删除
- 编辑
- 打开
- 保存
- 导出
- 缩放
- 帮助

所有图标按钮必须有：

```xml
ToolTipService.ToolTip="..."
```

文字按钮用于明确动作：

- 导入动作帧
- 批量生成线稿
- 导出序列帧
- 同步 Unreal

危险动作：

- 按钮文案要明确
- 必须确认
- 确认框说明会改什么

## 使用手感规则

高频操作：

- 少点按
- 不藏深层弹窗
- 不强迫用户离开预览区
- 有快捷键
- 有状态反馈

长任务：

- 走底部全局进度条，样式和剧情工具箱一致
- 可取消
- 不用阻塞式进度对话框
- 显示当前对象、数量/百分比、输出位置
- 底部进度状态归 `GlobalProgressViewModel`，窗口只负责动画和圆环绘制
- 如果已有程序实例正在运行并锁住 Release 输出目录，用独立输出目录构建验证，例如 `-o .\bin\verify\step-name`

整体项目目录迁移、批量导入、批量生成、批量导出、同步 Unreal 等操作，即使当前测试文件很少，也必须接入全局进度条，不能只在页面 `InfoBar` 里显示“正在处理”。

反馈：

- 成功：轻量状态文本
- 警告：可恢复问题说明
- 失败：说明哪个文件、什么原因、完成了多少、结果是否保留
- 需要用户决策时才用对话框
- 步骤页里的短提示交给右上角浮动 tips，参考剧情工具箱的 transient tips；不要把“已回到入口”“草稿已打开”这类提示放进底部常驻栏。

log 和撤回：

- 设置页必须保留“显示工作区路径”和 log 分类开关；“显示工作区路径”和“开启 log 功能”默认关闭，log 分类开关默认勾选但在 log 关闭时不可操作。
- 新功能的重要用户操作、警告和错误都要调用 `AppendLog(...)`，并遵守 `Settings.ShouldWriteLog(...)`。
- 输出日志只记录当前运行会话，不做素材持久化记录。
- 设置撤回只用于用户刚修改的设置开关，例如 log、工作区路径显示；入口是设置页底层快捷键 `Ctrl+Z`，不要做成可见按钮；不用于目录迁移、文件导入、删除、同步等素材操作。
- 增加新设置项时，要把它纳入 `AppSettings`、`SettingsViewModel` 和设置撤回栈。

## 快捷键和小页面规则

所有小页面、说明弹窗、确认弹窗、轻量编辑层都要有键盘路径，不能只靠鼠标点。

默认支持：

- `Enter`：确认
- `Esc`：取消 / 关闭 / 退出预览
- `Ctrl+Z`：设置页撤回最近一次设置开关修改
- `Left` / `Right`：上一帧 / 下一帧
- `A` / `D`：动作帧预览中的上一帧 / 下一帧备用键

右键：

- 有上下文动作时打开菜单
- 轻量预览层没有右键动作时可关闭

说明类 tips 不要塞进页面 `InfoBar` 里凑数；参考剧情工具箱，用 `ContentDialog + ScrollViewer`，支持 `Esc` 和右键关闭。

工具箱弹窗必须相对窗口居中显示，不能因为当前页面布局、左侧导航栏或局部容器导致新建/确认弹窗偏向左侧。

## 页面切换动画规则

进入不同主界面时必须有轻量动画，不要硬切。默认使用剧情工具箱同款：

- 当前页 `Visibility=Visible` 后调用 `PlayPageEntrance(...)`
- X 方向从 `-96` 滑入到 `0`
- Opacity 从 `0.82` 到 `1`
- 时长 `280ms`
- `CubicEase` / `EaseOut`

新增主页面时必须加入统一页面切换列表，不能单独写一套动画。

## 角色制作业务规则

数据层级：

```text
角色 -> 动作 -> 帧
```

St1 角色工作区目录方向：

```text
<CharacterCode>/
tool/
AssetMaterial/
ZDMaterial/
Sound/
ExAsset/
BUFF/
```

`tool` 下先保存：

- `character.json`：角色基础信息、英文代号和最近编辑时间。
- `ZDToolboxData.json`：工具箱用户编辑数据，包括 St1 草稿、St3 角色信息、St4 技能、St5 序列帧设置、St6 BUFF。
- `ReferenceImages/`：草稿参考图。

原则：

- 原始截图永远保留
- 线稿、遮罩、裁切图、导出图都是衍生文件
- AI / 自动线稿只是辅助层
- 不允许 AI 默认改动作、服装、武器、帧时序
- 编号稳定，显示名可改
- Unreal 引用依赖稳定编号，不依赖中文显示名
- 用户设置过的内容必须保存到角色自己的 JSON 文件或对应素材文件夹，不能只存在 ViewModel、窗口缓存或临时集合里。草稿本内容保存到 `tool/ZDToolboxData.json` 的 `Draft` 节点。
- 能通过文件获得的信息要优先从现有文件读取并同步到 ViewModel，例如 `character.json`、`ZDToolboxData.json`、参考图目录、动作/帧清单和各类素材目录；不要只依赖内存里的临时状态。
- 用户或外部工具在工作区文件夹内做了修改时，刷新/进入页面/执行相关操作前要重新读取对应文件状态，保证界面、缓存和磁盘内容正常同步。
- 当前制作角色可由 `零境角色台` 选择，其他步骤页常驻显示当前角色；只有 `St1-设计理念` 可以新建角色卡。
- St3 角色数值默认值：速度 `0`，生命值 `2000`，攻击力/异能防御/物理防御 `50`，暴击率/暴击伤害 `10`；任一数值为 `0` 时，St3 顶部通告栏必须提示用户确认。
- 角色卡/立绘卡比例按 `558:1200` 的竖卡视觉处理，默认立绘卡使用 `Assets/DefaultPortrait.png`，图片必须完整显示、居中，不裁切；卡片在普通步骤页使用紧凑尺寸，避免占用过多工作区；多个卡片加载要异步、分批让出 UI。
- 草稿本即时更改，但必须用短延迟自动保存，避免每输入一个字就写磁盘。
- `F1` 弹出快捷键大全；设置大页面下 `F1` 弹出当前设置 Tips 合集。`F2` 弹出当前角色草稿本，`F3` 弹出技能数值倍率规范，`F4` 弹出当前页面填写法则。
- St1 草稿详情页按 `Esc` 退出草稿，回到立绘入口。

推荐目录方向：

```text
Characters/<CharacterCode>/
Actions/<ActionCode>/
Frames/<ActionCode>_<FrameIndex>.png
LineArt/<ActionCode>_<FrameIndex>_line.png
Masks/<ActionCode>_<FrameIndex>_mask.png
Meta/action.meta.json
```

## 必走进度的操作

- 批量导入截图序列
- 批量裁切动作帧
- 批量生成线稿
- 批量重命名
- 批量导出
- 备份
- 恢复
- Unreal 同步

进度至少包含：

- 操作名
- 当前文件/对象
- 已完成数量或百分比
- 是否可取消
- 输出位置或结果摘要

## 文档规则

- 这份文件是 Codex 速查规约。
- 面向用户的说明以后放 `README.md`。
- 改变使用方式时，同步改文档。
- 新增共享样式或交互规则时，同步改本文件。
- 不写账号、密码、SMTP 授权码、私有密钥。

## 编码规则

- C# / XAML / Markdown 用 UTF-8。
- 中文 UI 文案可以直接写中文。
- 不用 PowerShell `>` / `>>` 重写源码或文档。
- 改中文后搜 `???` 和明显乱码。
- 发现历史乱码不要扩散。

## 验证规则

改 C# / XAML / csproj 后运行：

```powershell
dotnet build CrossingVoidZDTool.csproj `
  --configuration Release `
  --runtime win-x64 `
  -p:Platform=x64
```

每一次构建通过后都必须直接启动一次程序。启动新验证程序前，先关闭上一轮验证程序；最终保留最后一次启动的程序，方便用户直接查看和继续调试。

如果上一步启动的程序锁住 Release exe，下一步构建用独立输出目录：

```powershell
dotnet build CrossingVoidZDTool.csproj `
  --configuration Release `
  --runtime win-x64 `
  -p:Platform=x64 `
  -o .\bin\verify\<step-name>
```

每个有意义的阶段完成后发送 QQ 邮箱通告；构建完成并启动验证程序后，也要发送一次通告，说明构建结果和当前保留的验证程序状态：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File C:\Users\liuyu\Documents\CodexTools\notify-step.ps1 `
  -Project "CrossingVoidZDTool" `
  -Title "<阶段标题>" `
  -Summary "<阶段摘要>"
```

只改 Markdown 时不用 build。

## 新功能默认流程

1. 确定功能属于哪个角色制作流程。
2. 先补 `Model` / `Service` / `ViewModel`。
3. 再补 XAML 页面或卡片。
4. 高频动作补快捷键。
5. 长任务补进度和取消。
6. 更新文档。
7. C# / XAML / csproj 有变更就跑 Release build。

最后提醒：别把项目养成一个超大的 `MainWindow.xaml.cs`。业务逻辑往 Service 走，页面状态往 ViewModel 走。
