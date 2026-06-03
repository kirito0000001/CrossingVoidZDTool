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

页面布局优先顺序：

```text
标题和当前状态
主要工具栏
核心工作区
属性/详情区
底部状态和进度
```

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

- 走底部进度
- 可取消
- 不用阻塞式进度对话框
- 显示当前对象、数量/百分比、输出位置

反馈：

- 成功：轻量状态文本
- 警告：可恢复问题说明
- 失败：说明哪个文件、什么原因、完成了多少、结果是否保留
- 需要用户决策时才用对话框

## 快捷键规则

默认支持：

- `Enter`：确认
- `Esc`：取消 / 关闭 / 退出预览
- `Left` / `Right`：上一帧 / 下一帧
- `A` / `D`：动作帧预览中的上一帧 / 下一帧备用键

右键：

- 有上下文动作时打开菜单
- 轻量预览层没有右键动作时可关闭

## 角色制作业务规则

数据层级：

```text
角色 -> 动作 -> 帧
```

原则：

- 原始截图永远保留
- 线稿、遮罩、裁切图、导出图都是衍生文件
- AI / 自动线稿只是辅助层
- 不允许 AI 默认改动作、服装、武器、帧时序
- 编号稳定，显示名可改
- Unreal 引用依赖稳定编号，不依赖中文显示名

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
