# 特效 Niagara：面片 + 序列帧同步（设计与实施）

日期：2026-09-23 ｜ 状态：**口径已定稿（第三次修正），先改文档再动手**（用户："你先修改一下文档吧"）

## 零、最终口径（按这个来，前面写过的都以此为准）

| 项 | 决定 | 出处 |
|---|---|---|
| 第几步 | **新开第七步「特效同步」**；第五步回到原职责，一点不加重 | 用户："我最早说的是在第七步同步，不要给第五步压得太重" |
| 出什么 | **只出特效**：网格 sheet + 材质实例（MI） | 用户："同步特效的话，那当然是只出特效" |
| 共享资产放哪 | **插件 Content** `/ZDBridge/FX/…`（面片 SM / SubUV 母材质 / 共享粒子系统） | 用户："插件不是能自带Content吗？放在那边吧，模型也是" |
| 粒子系统 | **一份共享**，后面所有特效都用它 | 用户："就可以只做一份放在插件里面，后面的发射器都用这个就行了吧？" |
| 帧号 | `User.FrameIndex`（角色序列时间驱动）；**UV 由材质按参数算** | 「怎么和序列一致」；参数化之后系统才真共享 |
| Paper2D 那份 | **从第七步去掉**；工程里已存在的特效紧凑图集/精灵/Flipbook 当历史资产清一次 | 同上"只出特效" |

**作废的旧写法**（本文件前面/历史版本里出现过，别再照着做）：

- ❌ "共享资产建在 `/Game/GameActor2D/_Shared/`" —— 改成插件 Content。
- ❌ "每个动作一套 Niagara 系统"（`CreateEffectNiagaraSystem` 每动作一份 + 一份材质）—— 改成"一份共享系统 + 每动作一个 MI"。
- ❌ "特效层并进第五步" —— 改成第七步。
- ❌ "网格 sheet 和 Paper2D 图集共用一张" —— Niagara 那条只用网格 sheet，Paper2D 那条从第七步拿掉了。

### 资产布局（最终）

| 资产 | 落点 | 谁建 | 频率 |
|---|---|---|---|
| 面片网格 `FXDefault` | `/ZDBridge/FX/FXDefault` | 助手导入（FBX 源文件已在插件里） | 一次性 |
| SubUV 母材质 `M_FXSheet` | `/ZDBridge/FX/M_FXSheet` | 助手建（参数化，不含具体贴图） | 一次性 |
| 共享粒子系统 `NS_FXSheet` | `/ZDBridge/FX/NS_FXSheet` | 助手建（1 发射器 / 1 粒子 / 面片渲染器） | 一次性 |
| 网格 sheet `<角色>_<动作>_Effect_Sheet` | 动作自己的 `Material/<动作>/` | 第七步打图集时产出 | 每动作 |
| 材质实例 `MI_<动作>_Effect` | 同上 | 第七步 | 每动作 |

### 玩法侧怎么用（写在文档里，免得以后忘）

1. `SpawnSystemAttached(NS_FXSheet, …)`；
2. 把这一个动作的 MI 传给系统的用户参数 `User.EffectMaterial`（面片渲染器的材质就是绑到这个参数的）；
3. 每 tick 按角色序列时间写 `User.FrameIndex = floor(AnimTime × 动作fps × 2)`。

因为**贴图和网格参数都在 MI 里、UV 由材质算**，系统本身对所有特效都是一样的 —— 这就是"一份共享"成立的前提。

### 插件侧要改的两处（否则内容进不了包）

1. `ZDBridge.uplugin` 加 `"CanContainContent": true`（引擎靠这个标记挂 `/ZDBridge` 挂载点）；
2. 现在插件**只有一个 Editor 模块** —— 编辑器插件在打包后的游戏里不加载，它 Content 里的资产就没人引用、进不了包。
   要加一个很薄的 **Runtime 模块**（如 `ZDBridgeRuntime`，可以只有空实现），插件的 Content 才是运行时可用。
   **这一条要实测验证**（打包目标里插件内容确实可用），不能只凭文档说法就宣布完成。

## 一、要做成什么（原始描述，留档）

每个有特效的动作，播一条**跟着角色序列走**的序列帧特效，渲染用 **Mesh 面片**（不是公告板精灵 —— 用户要能改方向）。

## 二、已拍板的决定

| 项 | 决定 | 依据 |
|---|---|---|
| 渲染方式 | **Mesh 渲染器 + 面片** | 用户："我需要 mesh 渲染，因为需要改变方向" |
| 面片来源 | **内置进插件**，助手首次使用时导入 | 用户："你可以把这个内置进插件" |
| 帧号来源 | **角色序列时间**（PaperZD） | 用户问"怎么保持和序列一致"，方案见第五节 |
| 新开一步？ | **新开第七步「特效同步」**（2026-09-23 用户纠正） | 用户："我最早说的是在第七步同步，不要给第五步压得太重" |

> **架构修正（必须按这个来）**：特效**不再挂在第五步**。第五步回到它原来的职责（角色序列：图集/精灵/
> Flipbook/序列/AnimMaps），特效那套（特效图、网格 sheet、SubUV 材质、Niagara 面片系统）整体挪到
> **第七步**：自己的检测、自己的差异列表、自己的同步按钮。
>
> 已经做完的**资产层**（ZDBridge 三个助手、sheet 打包、计划字段、`_sync_effect_niagara`）全部可复用，
> 要动的是**编排层**：
>
> 1. 工作流步骤表加第 7 项（现在 1 底层检测 / 2 规整素材 / 3 同步素材 / 4 基础配置 / 5 序列同步 / 6 蓝图置入）
>    —— 第 7 项「特效同步」，含自己的"已完成/进行中"状态与缓存。
> 2. 第五步的差异树里**不再追加特效层那一行**（`AppendEffectAction` 移出第五步的计划生成）。
> 3. 第七步自己的差异/同步：以动作为单位列出"这个动作的特效有几张、网格几×几、要不要重建"，
>    同步时只做特效那三件事（sheet / 材质 / Niagara 系统）+ 可选的 Paper2D 特效产物。
> 4. 页面与日志沿用 Sync 台的既有样式（右侧流程列表里多一条，日志前缀用 `[St7]`）。

## 三、面片（FXDefault.fbx）实测

> 出处：`D:\FXDefault.fbx`（用户提供）
> 我们的验证：解析 FBX 二进制节点树（`Geometry/Vertices`，12 个数 = 4 顶点；`PolygonVertexIndex` 4 个 = 1 个四边形）：
> **20:59 版（当前）**：`X: -0.464 .. 0.464 (0.928)`、`Y: 0`、`Z: 0.000 .. 0.640 (0.640)`
> （20:51 的旧版是 Z 居中 `-0.320 .. 0.320`，用户后来把**原点挪到了底部**，理由：和角色一样"初始就在底下"）

结论：**四点一个四边形，落在 XZ 平面、法线沿 ±Y，宽 0.928 × 高 0.640，左右居中、底边在 Z=0** —— 正好是 928:640 的画布比例（1 单位 ≈ 1000 px）。
面片不自带朝向，方向由 Niagara 里的旋转/对齐控制，这正是用 mesh 的意义。
**底部对齐这点很重要**：生成 NS 时不能再额外补 pivot 偏移（`PivotOffset` 保持 0），否则特效会浮起来或沉下去。

## 四、沿用第五步已有的口径（不要另立一套）

- 特效帧 = **总格数 × 2**（和导出底板一一对应），顺序就是 `<动作>_Effect_FrameNN_Sprite` 的 NN。
- 特效自己的帧率 = **动作 fps × 2**（`SequenceEffectSyncService` / `BasePlateExportPlanner.Multiplier`）。
- 网格：`Sub Image Size = (cols, rows)`，第 N 帧（0 起）= `列 = N % cols`、`行 = N / cols`。
  **空帧照样占格**（全透明格），不能跳号 —— 跳号会让帧序整体错位。
- 同一张网格图集**两边共用**：Paper2D 切精灵、Niagara 用 SubUV（见调研文档第三节）。

> 现状提醒：现在的特效图集是 `pack + trim`（`AtlasManifestWriter` 写 `Mode="pack"`，`SequenceAtlasPackService.PackEffectAtlasAsync` 用 `Trim:true`），
> 只是因为"帧同尺寸、未被裁"才碰巧堆成了网格。**要长期可用，必须把特效图集改成显式网格并写上行列数**（本次一并做）。

## 五、播放怎么和序列一致（关键设计）

**不让 Niagara 自己按寿命推进**，而是把"第几帧"从角色序列的时间推过来：

```
FrameIndex = floor(AnimTime × 动作fps × 2)
```

（`AnimTime` = 角色当前动作序列的播放时间，秒；`× 2` = 特效帧率倍数。）

数据来源都是 PaperZD 现成的 BlueprintPure 节点：

| 要什么 | 节点 | 位置 |
|---|---|---|
| 当前播放时间（秒） | `Get Current Playback Time` | `PaperZDAnimPlayer.h:149` |
| 播放进度（0~1） | `Get Playback Progress` | `PaperZDAnimPlayer.h:158` |
| AnimBP 里的当前时间／比例 | `Current Time` / `Current Time (ratio)` | `PaperZDAnimInstance.h:213 / 217` |
| 总时长 / 帧率 | `GetTotalDuration` / `GetFramesPerSecond` | `PaperZDAnimSequence.h:163 / 167` |

Niagara 侧的接法：

1. 系统暴露一个用户参数 **`User.FrameIndex`（Float）**；
2. 面片渲染器的 **Sub Image Index** 绑到它（`FNiagaraVariableAttributeBinding::Setup`，见 `NiagaraCommon.h:1391`）；
3. 角色蓝图（`/Game/GameActor2D/<角色>/<角色>`）或 AnimBP（`<角色>_AnimBP`）在动作推进时写入这个参数。

这样角色动画**暂停 / 倒放 / 混合 / 循环**，特效都停在"当前那一格"，永远不会和序列错位。
反过来"开始给一次初值、后面让 Niagara 自己跑"在动画有混合或变速时必然漂 —— 不采用。

## 六、ZDBridge 的两个 C++ 助手（本次要写的）

### 1）`EnsureEffectPlaneMesh`

```
static FString EnsureEffectPlaneMesh(
    const FString& SourceFbxPath,      // 默认取插件内置：<Plugin>/Content/FX/FXDefault.fbx
    const FString& PackagePath,        // 默认 /Game/GameActor2D/_Shared
    const FString& AssetName);         // 默认 FXDefault
```

行为：目标资产已存在 → 直接返回它；否则导入 FBX 成 `UStaticMesh` 并保存。
返回 JSON：`{ ok, action:"created"|"reused", meshPath, error }`。

> 为什么导入到**工程内容**而不是插件内容：插件是 `Type: Editor`，插件内容在打包时进不了游戏；
> 面片是运行时资产，必须落在 `/Game/...`。**源 FBX 内置在插件里**，资产按需导入。

### 2）`CreateEffectNiagaraSystem`

```
static FString CreateEffectNiagaraSystem(
    const FString& PackagePath,        // /Game/GameActor2D/<角色>/Material/<动作>
    const FString& AssetName,          // <动作>_Effect
    UStaticMesh* PlaneMesh,
    UMaterialInterface* Material,
    int32 Columns, int32 Rows, int32 Frames,
    float FramesPerSecond,
    float PlaneScale,
    bool  bLoop);
```

行为（v1）：

1. 建 `UNiagaraSystem` 资产（`UNiagaraSystemFactoryNew`）；
2. 拿引擎模板 emitter（`/Niagara/DefaultAssets/Templates/Emitters/SimpleSpriteBurst`）复制一份进同目录，`AddEmitterHandle` 挂进系统；
3. 把它的精灵渲染器**换成 Mesh 渲染器**（`UNiagaraMeshRendererProperties`）：面片网格、材质、`Sub Image Size=(cols,rows)`、`Sub Image Index` 绑 `User.FrameIndex`、`Sub Image Blending`；
4. 系统加用户参数 `User.FrameIndex`（Float，默认 0）；
5. 保存并返回 JSON 诊断：`{ ok, systemPath, emitterPath, rendererClass, subImageSize, frameIndexBinding, meshPath, materialPath, warnings[], error }`。

**v1 刻意不做**（留到下一轮，需要动 Niagara 模块栈，编译往返更多）：模块输入的精确调参
（寿命、播放速率、一次性/循环的 emitter 侧开关）。v1 用模板默认值 + 由驱动方在动作结束时停组件；
`bLoop` 先只记在 JSON 里，等栈编辑接上再落。

### 需要动的插件文件

| 文件 | 改动 |
|---|---|
| `ZDBridge.uplugin` | 不用改（面片内容不进插件包） |
| `ZDBridge.Build.cs` | Private 依赖加 `Niagara`、`NiagaraCore`、`NiagaraEditor` |
| `Source/ZDBridge/Public/ZDBridgeLibrary.h` | 两个新 UFUNCTION 声明 |
| `Source/ZDBridge/Private/ZDBridgeLibrary.cpp` | 实现 |
| `Content/FX/FXDefault.fbx`（新增） | 面片源文件（用户提供） |

## 七、工具箱侧接入（下一阶段）

1. 打特效图集时改成**显式网格**（`AtlasFolderPackService` 支持 grid + columns；空帧占格），把 `cols/rows` 写进图集结果；
2. 同步计划（`UnrealBridgeSequencePublishService`）在特效层那条上带 `columns/rows/frames/outputFps`；
3. `Tools/UnrealBridge/sync_character_sequences.py` 的 `_sync_effect_layer` 末尾加 `_sync_effect_niagara`：
   调 `EnsureEffectPlaneMesh` + `CreateEffectNiagaraSystem`；材质用 `MaterialEditingLibrary` 生成/复用一个 SubUV 母材质；
4. 差异/清理名单把 `NS_<动作>_Effect` 算进"特效层规范产物"（`IsEffectLayerAssetName`），否则会被当成待删。

## 八、验证计划

1. **编译**：ZDBridge 编译通过（预计 2~4 轮往返；NiagaraEditor 的 API 编译器说了算）。
2. **单点生成**：用 Misaka / Click 的 `Misaka_Click_Effect`（930×1926 = 1×3 网格）跑一次助手，
   读 JSON 诊断：系统 / emitter / 渲染器 / `Sub Image Size` / 绑定 / 面片路径都对。
3. **肉眼**：打开生成的 NS，面片显示第 0 帧；手改 `User.FrameIndex` 能换帧。
4. **同步**：在 AnimBP 里用 `Current Time × fps × 2` 驱动，检查特效和角色动作逐格对齐（含暂停 / 循环）。

### 实测结果（2026-09-23，编译 + 真机跑通）

编译：`Build.bat CrossingVoidEditor Win64 Development` —— **已通过**。过程中修掉两处：

1. 助手里的 `MakeError(...)` 撞上了 UE 5.8 `TValueOrError` 的同名模板（模板在重载里胜出，返回的是 error proxy）→ 改名 `MakeErrorJson`；
2. `IAssetTools::DuplicateAsset` 在 5.8 只接受 **UObject\***（旧的"路径"重载没了）→ 先 `LoadObject` 模板 emitter 再复制；
3. 链接期还差 `IPluginManager` 所在的 `Projects` 模块 → 补进 `PrivateDependencyModuleNames`。

真机（Python 通过 `run_remote_unreal_job.py` 调助手，编辑器在线执行）：

```json
{"plane": {"ok": true, "action": "created",
           "meshPath": "/Game/GameActor2D/_Shared/FXDefault.FXDefault",
           "sourceFbx": "C:/CrossingVoid/Plugins/ZDBridge/Content/FX/FXDefault.fbx",
           "bounds": "X=46.400 Y=0.000 Z=32.000"},
 "system": {"ok": true, "action": "created",
            "systemPath": "/Game/GameActor2D/Misaka/Material/Click/Click_Effect.Click_Effect",
            "emitterPath": ".../Click_Effect_Emitter.Click_Effect_Emitter",
            "rendererClass": "NiagaraMeshRendererProperties",
            "subImageSize": "1x3", "frameIndexBinding": "User.FrameIndex",
            "meshPath": "/Game/GameActor2D/_Shared/FXDefault.FXDefault"}}
```

**量出来的第一手数据：**

| 项 | 值 | 意味着什么 |
|---|---|---|
| 面片导入后的包围盒 | `X=46.4 Y=0 Z=32`（半长）→ **92.8 × 64 cm** | FBX 里的 0.928/0.640 被当成**米**；和 928 px 宽的画布不是一个量级 |
| 角色一格的像素 | `Click_Frame0_Sprite.source_dimension = 141 × 360`（裁过透明边；画布仍是 928×640） | Paper2D 的 PPU 在 Python 里读不到，**面片缩放要按视觉对齐定**（≈ ×10 才对得上 928×640 cm 的画布） |
| Flipbook | `fps = 10`，4 帧 | 和"动作 fps × 2"口径对照用 |

**这次在工程里新建的资产（测试用）**：`/Game/GameActor2D/_Shared/FXDefault`、`/Game/GameActor2D/Misaka/Material/Click/Click_Effect`（+ 同名 `_Emitter`）。
注意 Click 这个动作**还没有同步过特效层**（`Misaka_Click_Effect` 贴图在工程里不存在），所以这次测试没挂材质 —— 材质和真图集要等工具箱侧接上。

### 第二轮：SubUV 材质 + 带材质的面片系统（也跑通了）

新增第三个助手 `EnsureEffectSubUVSheetMaterial(PackagePath, AssetName, Sheet, bAdditive)`：
建/复用一个母材质（`MaterialExpressionTextureSampleParameterSubUV` + Emissive/Opacity + `bUsedWithNiagaraMeshParticles` /
`bUsedWithNiagaraSprites` / `bUsedWithParticleSprites`，Unlit + 加法或半透）。**放在 C++ 里做**是因为这三个"用作"标记
漏一个就只是一个不显示的面片，而且和生成系统在同一条链上，不会出现"系统建好了但没材质"的半成品。

真机结果（先 `PurgeAssets` 清掉上一轮的测试系统再重建）：

```json
{"plane":    {"ok": true, "action": "reused",  "meshPath": ".../FXDefault.FXDefault"},
 "material": {"ok": true, "action": "created", "materialPath": ".../_Shared/M_Effect_SubUV",
              "blendMode": "additive"},
 "system":   {"ok": true, "action": "created", "systemPath": ".../Click/Click_Effect",
              "rendererClass": "NiagaraMeshRendererProperties", "subImageSize": "1x3",
              "frameIndexBinding": "User.FrameIndex",
              "materialPath": ".../M_Effect_SubUV", "meshPath": ".../FXDefault"}}
```

顺手记两个踩到的点：

1. **Python 绑定名不是照抄 C++ 名**：UHT 把 `EnsureEffectSubUVSheetMaterial` 转成 `ensure_effect_sub_uv_sheet_material`
   （`SubUV` → `sub_uv`）。工具箱/py 调的时候按这个写。
2. `UMaterialEditingLibrary` 在 **MaterialEditor** 模块里，`IPluginManager` 在 **Projects** 里 —— 两个都要进 `ZDBridge.Build.cs`。

### 工具箱侧（已接上，2026-09-23）

1. **Niagara 用单独的网格 sheet**：`SequenceAtlasPackService.PackEffectSheetAsync` 打第二张图 ——
   一个输出帧一格、**空帧用 928×640 全透明图占位**（临时目录，收尾删）、`GridMode + Columns + Padding:0 + Trim:false`
   （网格必须能被行列数整除，加 padding 或裁边都会让 Sub UV 切偏）。
   **原 Paper2D 那张紧凑图集一点都不动** —— 它已经跑通了，没必要为 Niagara 冒风险。
2. 行列数由 `SequenceEffectSyncService.ResolveGrid(帧数)` 决定：列 = ⌈√帧数⌉，行 = ⌈帧数/列⌉。
3. 命名（一处定，Python 不重拼）：`<角色>_<动作>_Effect_Sheet` / `<动作>_Effect_Material` / `NS_<动作>_Effect`。
4. 计划（`UnrealBridgeSequenceSyncAction`）新增：`effectSheetName/effectSheetImagePath/effectColumns/effectRows/
   effectMaterialName/effectNiagaraSystemName/effectFps`。
5. `sync_character_sequences.py` 新增 `_sync_effect_niagara`：导 sheet → `ensure_effect_plane_mesh`（插件内置 FBX）
   → `ensure_effect_sub_uv_sheet_material` → `create_effect_niagara_system`；产物路径并进 `new_paths`，清理不会误删。

回归：`特效` 12/12、`图集` 14/14 通过（其中两条断言从"规范产物 4 个"改成 8 个 —— Paper2D 三样 + Niagara 四样）。

**还没做的**：用 DefAtk 跑一次端到端（工具箱第五步检测 → 同步），确认 sheet 落盘、材质与系统在工程里生成、
面片显示正确。这一步要走工具箱的界面流程。

## 十一、"只出特效"的原子重构步骤（下一轮一次做完，别拆开）

**为什么必须一次做完**：特效层现在同时产出 Paper2D 那份（紧凑图集 / 精灵 / `_Effect_Flipbook`）和
Niagara 那份（网格 sheet / MI）。规范产物名单（`CanonicalAssetObjectPaths`，进计划当
`StaleAssetObjectPaths`）是**"先清后建"的权威依据** —— 名单和生产必须同时改：
只删名单里的一项会留下清不掉的历史资产（这个坑踩过一次：归一化大小写导致清理认不出自己刚建的），
只停生产不改名单则下一轮同步会把自己刚建的东西当待删。

顺序（每一步都要能编译）：

1. `SequenceEffectSyncService.SequenceEffectSyncLayout`：产物收敛成 **sheet + MI** 两项；
   `CanonicalAssetObjectPaths` 只留这两条（去掉图集/Flipbook/精灵，以及上一版留下的每动作 NS / emitter）。
2. `SequenceAtlasPackService`：**不再调 `PackEffectAtlasAsync`**（特效不再打紧凑图集），只保留
   `PackEffectSheetAsync`（网格 sheet）。`PackEffectAtlasAsync` 连同它的差异口径一起留档、不再接线。
3. `UnrealBridgeSequencePublishService.AppendEffectAction`：计划里的特效项去掉 `Atlas` / `SourceImages` /
   `Frames[].SourceImageIndex`（那些是给精灵用的）；保留 `frames[]`（只有"第几帧是空的"这一个用途，
   sheet 打包和 frame_count 要用）、`effectSheet*` / `effectColumns|Rows` / `effectMaterialName` / `effectFps`。
4. `sync_character_sequences.py`：`isEffectLayer` 的项**只调** `_sync_effect_niagara`（已按"共享三样 + sheet + MI"
   写好），不再走 `_sync_effect_layer` 的图集/精灵/Flipbook 那段；`new_paths` 就是 sheet + MI。
5. 计划生成的接线：`AppendEffectAction` 从第五步（`BuildSequenceSyncPlan`）里摘掉，接到第七步自己的入口
   （`BuildEffectSyncPlan`），第五步回到纯角色序列。
6. 回归：`特效` / `图集` 两个批次重跑；把"规范产物 8 项"的断言改成新的项数（2 项）。
7. 清测试残留（`/Game/GameActor2D/_Shared/*`、`Misaka/Material/Click/Click_Effect` + emitter、
   以及历史遗留的特效紧凑图集/精灵/Flipbook）。

第 5 步（第七步的界面：流程列表项 + 页面）在做完 1–4 之后单独一轮，因为 XAML 有静默崩溃的前科，**一轮只碰那一块**。

## 十二、进度快照（2026-09-23，交给下一位接手的人）

**已经做完并且验证过的：**

1. 插件（`Plugins/ZDBridge`）：`ensure_effect_plane_mesh` / `ensure_effect_sheet_material` /
   `create_effect_sheet_material_instance` / `ensure_effect_niagara_template` 四个助手；
   共享三样（面片网格 `/ZDBridge/FX/FXDefault`、参数化母材质 `M_FXSheet`、共享粒子系统 `NS_FXSheet`）
   已实测建出并读回；`CanContainContent: true` + `ZDBridgeRuntime` 模块已加（否则编辑器插件的内容进不了包）。
2. 母材质**自己算 UV**（`Columns/Rows/FrameIndex`），渲染器 `Sub Image Size` 固定 (1,1) —— 这是"系统只需要一份"的前提。
3. 工具箱："只出特效"落地 —— 特效产物只有网格 sheet + MI；不再打 Paper2D 紧凑图集；计划里没有图集/精灵/Flipbook 字段；
   `sync_character_sequences.py` 的 `isEffectLayer` 直接走 `_sync_effect_niagara`。
4. **第五步与第七步分家**：`BuildSequenceSyncPlan` 的 `includeEffectLayers` 默认 `false`（第五步只出角色序列）；
   新增 `BuildEffectSyncPlan`（只出 `IsEffectLayer` 的项）。
5. 步骤上限 `UnrealSyncWorkflow.MaxStep = 7`；`MainWindow.xaml` 右侧流程列表加了第 7 行「特效同步」；
   `WorkflowStep7StatusText` 已接。
6. 回归：`特效 12/12`、`图集 14/14`、`指纹 1/1`；主/测试工程 0 错；`/Game` 里的测试残留已清。

**只剩下三步（顺序固定）：**

1. **第七步页面 + 处理器**：照第六步「蓝图置入」那块抄（`MainWindow.xaml` 约 2800→3030 行是流程列表与页面，
   `MainWindow.UnrealSync.BlueprintSetup.cs` 是处理器样板）。检测走 `BuildEffectSyncPlan`，
   差异列表按动作列出"特效几张 / 网格几×几 / 要不要重建"，同步走同一条发布链路，日志前缀 `[St7]`。
   **一轮只碰 XAML，改完立刻编译**（这个项目有 XamlCompiler 返回 1、零诊断的先例）。
2. 把第 1 条接上之后，跑一次 DefAtk 真同步。
3. spawn 一眼确认**材质真的收到 `FrameIndex`** —— 全链路唯一还没验证过的环节
   （`Renderer.DynamicMaterialBinding` 把帧号送给母材质的 `FrameIndex` 参数这条线，只验证到"绑上了"，没验证"生效了"）。

## 九、风险与未定

| 风险 | 说明 |
|---|---|
| NiagaraEditor API 版本差异 | 模块栈 / 渲染器属性的写法在 5.8 与旧版本不同，编译报错就按报错改 |
| 材质要勾 "Used with Niagara Meshes" | 面片材质必须勾上，否则运行时不可见（引擎源码注释里明确写了） |
| 面片默认朝向 | 面片在 XZ 平面（法线 ±Y）。若 2D 相机不是沿 Y 看，需要在 NS 里给初始旋转 —— 生成参数里留 `PlaneScale`，旋转走 `User.*` 或粒子旋转绑定 |
| 图集网格化会改口径 | 特效图集从 pack 改成 grid 后，旧图集的矩形口径作废一次（会重新打一次图集，和"布局口径变化重建一次"同类） |

## 十、助手口径调整（第三次修正后，替代第六节）

第六节写的"每个动作一套系统 + 每动作一份材质"**已作废**。按"一份共享放在插件里"重排如下。

### 共享三样（一次性，落在插件 Content）

```
static FString EnsureEffectPlaneMesh(SourceFbxPath, PackagePath, AssetName);
    // 默认落 /ZDBridge/FX/FXDefault；FBX 源文件在 <Plugin>/Content/FX/FXDefault.fbx，已内置

static FString EnsureEffectSheetMaterial(PackagePath, AssetName);
    // 默认落 /ZDBridge/FX/M_FXSheet。**不含具体贴图**，全部走参数：
    //   Sheet(TextureParameter) / Columns / Rows / Frames  ← MI 覆盖
    //   FrameIndex(标量，动态参数)  ← 由粒子属性/用户参数传进来
    // UV 由材质自己算：列 = idx % Columns、行 = idx / Columns，再除以 (Columns, Rows)

static FString EnsureEffectNiagaraTemplate(PackagePath, AssetName);
    // 默认落 /ZDBridge/FX/NS_FXSheet。1 个发射器 / 1 个粒子 / Mesh 渲染器；
    // 面片渲染器的材质绑到 **用户参数** User.EffectMaterial（不是写死某一份材质）；
    // 帧号走 User.FrameIndex。不再复制引擎的 SimpleSpriteBurst 模板（那会带一堆用不上的模块，
    // 也就是用户看到的"空白粒子系统 + 主发射器"）。
```

### 每动作（第七步）

```
static FString CreateEffectSheetMaterialInstance(
    const FString& PackagePath, const FString& AssetName,   // <角色>/Material/<动作>, MI_<动作>_Effect
    UTexture2D* Sheet, int32 Columns, int32 Rows, int32 Frames);
```

第七步每个动作只产出两样：**网格 sheet**（贴图）+ **MI**（参数：Sheet / Columns / Rows / Frames）。
不再产出每动作的 Niagara 系统、emitter、母材质。

### 已经建出来要清理的（我的测试残留）

- `/Game/GameActor2D/_Shared/FXDefault`、`/Game/GameActor2D/_Shared/M_Effect_SubUV`（测试建的，迁到插件后删）
- `/Game/GameActor2D/Misaka/Material/Click/Click_Effect` + `Click_Effect_Emitter`（每动作一套的旧口径）
- 工程里历史遗留的特效紧凑图集/精灵/`_Effect_Flipbook`（Paper2D 口径，第七步不再产出）
