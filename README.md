# 零境交错：ZD 工具箱

角色制作工具箱。把角色从设计草稿一路做到可用素材，再同步进 Unreal 工程。

WinUI 3 桌面应用（.NET 8，unpackaged self-contained），外加一批在 Unreal Editor 进程内运行的 Python 脚本。

---

## 它解决什么问题

角色素材从制作到进游戏，中间有一堆容易出错的手工步骤：图片要按规范命名和编号、语音要按分类放对目录、序列帧要绑定到 PaperZD 的动画资产、技能和 BUFF 数据要写进蓝图和数据表。任何一步拼错一个字母，游戏里就是资源丢失。

工具箱把这些规则固化下来，并且**在同步前先把两侧的差异摆出来给人确认**，而不是直接改 Unreal 工程。

---

## 工作区结构

工具箱自己的工作区（和 Unreal 工程是两回事）：

```
<工作区>/
├─ Draft/<角色代号>/         制作中的角色
├─ Completed/<角色代号>/     已完成的角色
└─ Export/<角色代号>/        导出的角色包
```

每个角色目录：

```
<角色代号>/
├─ tool/                     工具箱自己的数据
│   ├─ character.json            角色卡：代号、显示名、完成状态、最近编辑时间
│   ├─ ZDToolboxData.json        用户编辑的全部内容（草稿、角色信息、技能、序列帧设置、BUFF）
│   ├─ ReferenceImages/          草稿参考图
│   └─ UnrealSync/               同步基线、身份表、分步缓存
├─ AssetMaterial/            基础图片
├─ ZDMaterial/<动作>/        序列帧（Frames/ + sequence.json）
├─ Sound/<分类>/             语音，按分类分目录
├─ ExAsset/                  附加资产
└─ BUFF/                     BUFF 图标与数据
```

**角色目录内的路径一律存成 `$char/` 开头的相对写法**，绝对路径只用于工作区外面的东西（Unreal 引擎、Unreal 工程）。这样角色目录搬到哪儿、在 Draft 和 Completed 之间怎么挪，路径都不会指丢。

---

## 虚幻同步台的六步

同步是分步的，每一步单独检测、单独确认、单独执行，各自有自己的缓存：

| 步 | 名称 | 做什么 |
|---|---|---|
| 1 | 底层检测 | 确认引擎、工程、目标目录、桥接脚本都在 |
| 2 | 规整素材 | 把 Unreal 侧位置不规范的资产列出来，给出重定向建议 |
| 3 | 同步素材 | 角色图片与语音的双向差异，逐条勾选执行 |
| 4 | 基础配置 | Item 白名单字段、入队语音、受击 MetaSound、语音并发 |
| 5 | 序列同步 | 序列帧、Sprite、Flipbook 与 PaperZD 动画资产 |
| 6 | 蓝图置入 | 角色蓝图与数据表：对局设置、动作序列、技能、护援与连携 |

两条执行通道，用同一份任务脚本和结果协议：

- **在线**：目标 Unreal Editor 已打开时走 Python Remote Execution，快约十倍
- **离线**：Editor 没开时用 `UnrealEditor-Cmd -run=pythonscript`

> ⚠️ **commandlet 的退出码不可靠**——编辑器在任何无关的地方报过错都会让它非零。所以导出成没成功以**产物**为准（清单是否存在、是否是这一轮写出来的），不以退出码为准。

---

## 构建与测试

必须指定平台，否则 WinUI 的 XAML 编译器会报错：

```bash
dotnet build CrossingVoidZDTool.csproj -p:Platform=x64
```

回归测试是一个控制台程序（不是 xunit），**必须在仓库根目录运行**——有用例按相对路径读源码和 XAML：

```bash
dotnet run --project Tests/CrossingVoidZDTool.RegressionTests -p:Platform=x64
```

Python 侧的自检不需要 Unreal（`unreal` 模块用桩顶替）：

```bash
python Tools/UnrealBridge/tests/check_blueprint_setup.py
```

打包走 `Pakout.ps1`。注意它会显式把 `PublishTrimmed` 覆盖成 `false`——界面用的是反射型 `{Binding}`，裁剪会让绑定静默失效。**不要绕过这个脚本直接 `dotnet publish -c Release`。**

---

## 代码结构

```
Models/       数据结构、序列化模型、结果对象
Services/     文件、图片、编号、导入导出、Unreal 同步等业务逻辑
  ├─ Infrastructure/   横切设施：原子写、进程编排、日志出口、目录布局
ViewModels/   页面状态、命令、校验、选择项
Views/        对话框内容工厂
Controls/     可复用控件
Styles/       共享样式
Tools/        在 Unreal 进程内运行的 Python 脚本
Tests/        回归测试（控制台程序 + 手写 runner）
Plan/         重构方案与缺陷清单
Docs/         历史设计文档
```

### 几条踩过坑才立起来的规矩

**Service 层不许静默吞异常。** 每个 `catch` 要么向上抛，要么调 `ToolboxLog` 写一行说明为什么吞。这一层曾经一万六千行、九十处 `catch`、零个日志出口，于是「显示成功但实际没做成」出现过六次。

**资产路径按大小写不敏感比较。** Unreal 的资产路径本来就不区分大小写，而 C# 的字符串比较区分。这个坑踩过四次（第六步引用比较、第五步清理误删刚生成的资产、资产规整判定、导出脚本），每次表现都不一样。

**写文件走 `AtomicFileWriter`。** 别再手写「临时文件 + 移动」——曾经有一处写成了「先删目标再移过去」，中途崩溃就是角色数据整份消失。

**起 Unreal 进程走 `UnrealProcessRunner`。** 这套编排曾经在四个服务里各抄一份，轮询间隔各不相同，而且共享同一个坑：取消时进程杀不掉，变成孤儿继续占着工程锁。

**测试断言行为，不断言源码文本。** 仓库里还剩一批 `File.ReadAllText("Xxx.cs") + Contains` 的用例，它们查的是变量名和换行位置，改个命名就假报警却拦不住逻辑写错。回归里有一条「技术债只许降不许升」的棘轮用例盯着这个数字，**只许往下调**。

**长任务要能取消、要报进度。** 走 `GlobalProgressViewModel`，不要用阻塞式进度对话框。

---

## 相关工程

Unreal 侧需要 `ZDBridge` 插件（提供数据表按行读写、资产清理、序列解绑等蓝图库函数）。工具箱的角色代号必须和 Unreal 的角色目录名完全一致，拼写不同直接报错，不做兼容映射。
