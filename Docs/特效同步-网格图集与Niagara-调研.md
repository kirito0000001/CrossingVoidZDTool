# 特效同步：网格图集与 Niagara（调研 + 方案）

日期：2026-09-23 ｜ 状态：**调研完成，等拍板范围**（未动实现）

## 一、要回答的问题

1. 代码里现在有没有"特效导入"这部分？
2. `Paper2D Flipbook` 能不能直接喂给 Niagara？
3. 如果能，新一步该怎么做才不会白做？

## 二、结论（先说结果）

### 1. 代码里已经有了，而且就在第五步

特效帧（动作帧率 × 2）→ 图集贴图 + 每帧一个精灵 + 一个 Flipbook，**第五步同步时一起走**：

| 产物 | 位置 | 代码 |
|---|---|---|
| 这一层长什么样（命名、帧序、图集名、规范资产名单） | — | `Services/SequenceEffects/SequenceEffectSyncService.cs` |
| 打特效图集 | `<角色>/tool/AtlasCache/<动作>_Effect/` | `Services/Atlas/SequenceAtlasPackService.cs` → `PackEffectAtlasAsync` |
| 导入 UE：贴图 + 切精灵 + 建 Flipbook | `/Game/GameActor2D/<角色>/Material/<动作>/` | `Tools/UnrealBridge/sync_character_sequences.py` → `_sync_effect_layer` |

资产命名（已实装）：

```
<动作>_Effect_FrameNN_Sprite     ← 每个有图的输出帧一只精灵（NN 是输出位置，从 00 起）
<动作>_Effect_Flipbook           ← 整层一个（fps = 动作 fps × 2；空帧 = 空关键帧，时间照占）
<角色>_<动作>_Effect             ← 这一层自己的图集贴图
```

**所以"特效导入"不需要另开一步**：缺的不是导入，是"给谁用"——现在这套产物只有 Paper2D 口径（Flipbook），没有 Niagara 口径。

### 2. Flipbook 不能直接用于 Niagara（这一条是硬结论）

> 出处：本机引擎源码 `D:\UnrealEngine-5.8.2\Engine\Plugins\FX\Niagara\Source\**`
> 原文（检索结果）：`rg -n "PaperFlipbook|PaperSprite"` → **0 处命中**
> 我们的验证：整个 Niagara 插件（运行时 + 编辑器 + shader）没有任何一处引用 Paper2D 的资产类型；
> Niagara 的精灵路径只认 **贴图 + 网格参数 + 帧号属性**，没有"播放某个 Flipbook"的入口。

Niagara 的精灵渲染器自己就带 Sub UV（这是官方能力，不是绕路）：

> 出处：`Engine\Plugins\FX\Niagara\Source\Niagara\Public\NiagaraSpriteRendererProperties.h`
> 原文（204–210 行）：`/** When using SubImage lookups for particles, this variable contains the number of columns in X and the number of rows in Y.*/ FVector2D SubImageSize = FVector2D(1.0f, 1.0f);`
> 原文（315–317 行）：`/** Which attribute should we use for sprite sub-image indexing when generating sprites?*/ FNiagaraVariableAttributeBinding SubImageIndexBinding;`
> 原文（208–210 行）：`Sub UV Blending Enabled`（用 SubImageIndex 的小数部分在相邻两帧间插值）

UV 是**渲染器算好再给材质**的，不用材质自己拼格子：

> 出处：`Engine\Plugins\FX\Niagara\Shaders\Private\NiagaraSpriteVertexFactory.ush:1058-1069`
> 原文：`float SubImageA = SubImageIndex - SubImageLerp + 0.5f; ... Intermediates.TexCoord.xy = (float2(SubImageAH, SubImageAV) + UVForTexturing) * NiagaraSpriteVF.SubImageSize.zw;`
> 我们的验证：渲染器把"第几格"折算成 UV；材质侧另有 `Particle.SubUVCoords`（`MaterialExpressionParticleSubUV.h`，TextureSample 子类 + `bBlend`）用于**相邻帧混合**。

### 3. 别人怎么做的（社区 + 现成实现）

> 出处：知乎站内搜索"Niagara 序列帧 特效"（22 篇内容 + AI 汇总），例：《004-Unreal-Niagara-粒子特效序列图-二次元爆炸》（小虫儿飞到花丛中）、《Niagara播放火焰动画》（Zeeture）、《虚幻5 Niagara 爆炸特效》（Matt 的UE探索站）
> 原文：「序列帧贴图需要按照行列网格排列多帧动画，常见规格有 6×6（36帧）、8×8（64帧）」「Sprite Renderer：指定使用 Particle SubUV 材质，并设置 SubImage Size 匹配贴图的行列数」「Sub UVAnimation 添加在 Particle Update 阶段……**该模块必须搭配 Particle State 模块才能正常播放**（否则停在第一帧）」

> 出处：GitHub `ssencho/flipbook2niagara`（"Command-line flipbook texture to Niagara system converter for UE 5.7"）
> 原文（README Notes）："One-shot mode sets Loop Behavior to Once on **both** the emitter and the system-level SystemState — Niagara's system loop defaults to Infinite and silently restarts completed emitters"
> 原文（脚本）：材质走 `MaterialExpressionTextureSampleParameterSubUV` + `used_with_niagara_sprites = True`；系统侧 `configure_subuv(columns, rows, blend=True, add_animation_module=True)` 后设 `SubUVAnimation.Number Of Frames`
> 我们的验证：该仓库是"贴图网格 → Niagara 系统"的完整现成实现，做法与上面的引擎源码一致；它的网格约定也是 `<cols>x<rows>_<frames>f`（**统一网格**）。

### 4. 但我们现在打出来的特效图集，Niagara 用不了

> 出处：`Services/Atlas/AtlasManifestWriter.cs`（`Mode = "pack"`）、`Services/Atlas/SequenceAtlasPackService.cs`（`Trim: true` + `PackMode`）
> 我们的验证（实跑产物）：`D:\NewData\CrossingVoidZDProject\Completed\Misaka\tool\AtlasCache\Click_Effect\Misaka_Click_Effect.png` = **930×1926**，是"裁掉透明边 + 紧凑堆放"的排布，**每格矩形都不一样**；Niagara Sub UV 只认**等分网格**（`行列数 + 序号`），这种排布喂不进去。

反过来说，特效这边**天然适合网格**：

> 我们的验证（实跑产物）：`...\Completed\Misaka\ZDMaterial\DefAtk\Effects\Effect\Frames\*.png` 共 6 张，尺寸**全是 928×640**（特效帧本来就是整张画布的图），单元格统一是免费的。

## 三、方案（三档，范围递增）

### 方案 A：特效图集改成"统一网格"（最小改动，两边都能用）

- 打特效图集时改成 grid：所有帧共用一个矩形（建议**先在工具箱侧算一次"所有帧内容的公共外框"**，再按这个尺寸铺网格），行列数写进同步计划。
- 格位映射必须显式带上：**空帧也占一格**（透明格），和第 5 步现在的空帧语义一致。
- 好处：
  - Niagara：`Sub Image Size = (cols, rows)`、`Sub Image Index` 一填就能跑；
  - Paper2D：精灵照旧按格子切，Flipbook 不变（还能顺带省掉"每帧一个矩形"的对齐计算）；
  - UI：精灵当 UMG 的 Image Brush 用（这一点和现在一样）。
- 代价：贴图比紧凑排布大（空帧也占格子）。Sk2 那种 16 帧 × 928×640 的量级要用公共外框裁一刀才划算。

### 方案 B：A + 生成 SubUV 材质（美术直接挂到自己的 Niagara 里）

- 一个通用母材质 `M_SubUV_Sprite`（`TextureSampleParameterSubUV`，Unlit + Additive/Translucent 各一份）+ 每个动作一个 `MI_<角色>_<动作>_Effect` 指定贴图。
- 勾 `Used with Niagara Sprites`（GitHub 那份实现就是这么做的）。
- 好处：美术做 Niagara 时不用自己搭材质，也不用担心采样口径不对。

### 方案 C：B + 生成整套 Niagara 系统 `NS_<角色>_<动作>_Effect`

- 一次性、单粒子、`Lifetime = 帧数 / (动作fps × 2)`，**emitter 和 SystemState 两处 Loop Behavior 都要设 Once**（否则会一直重播）。
- 风险（**没验证，别当成能做**）：ZDBridge 现在只有 Paper2D / MetaSound / DataTable 的封装，没有 Niagara；5.8 用 Python 建 Niagara 系统 + 改 renderer 的 `SubImageSize` / 绑定 `SubImageIndex` 需要先做一次可行性 spike（GitHub 那份实现是靠自己写的编辑器插件代理完成的，不是纯 Python）。

## 四、需要拍板的点

1. **特效最终由谁渲染**：
   - Niagara 粒子（推荐，可加叠加/扭曲/发光/多发）→ 走方案 A/B；
   - 还是 Paper2D 叠层（跟着角色一起画的整幅特效）→ 现在的 Flipbook 就够了，不必改图集口径。
2. **做到哪一档**：A（只出网格图集 + 计划带行列数）／B（+ 材质）／C（+ Niagara 系统，先验证可行性）。
3. **贴图尺寸口径**：特效图集要不要"公共外框裁一刀"（省显存，但精灵偏移变成每动作一个常数，需要重新算对齐）。

## 三·补、把"生成 Niagara"这条路走到什么程度（2026-09-23 实测补充）

### 实测 1：你给的那张 930×1926 —— **已经是网格，可以直接用**

> 出处：`D:\NewData\CrossingVoidZDProject\Completed\Misaka\tool\AtlasCache\Click_Effect\Misaka_Click_Effect_sequence.json`
> 原文：三帧的 `frame` 分别是 `{x:0,y:0,w:928,h:640}`、`{x:0,y:642,...}`、`{x:0,y:1284,...}`，`trimmed:false`，`packer: "shelf:h"`
> 我们的验证：它是**1 列 × 3 行**、每格 928×640（格间 2px 透明 padding），930×1926 正好能被 1×3 等分 —— Niagara 侧填 `Sub Image Size = (1, 3)` 就能用。
> 但这条**是碰出来的**（打包器把同尺寸、没被裁的帧堆成一列），不是我们规定的。要长期可靠，打包时必须显式写网格行列数。

### 实测 2：Python 能做什么、不能做什么（在运行中的 5.8.2 编辑器里探的）

渠道：`run_remote_unreal_job.py` + 只读探测脚本（不建资产）。

| 能力 | 结果 |
|---|---|
| `unreal.NiagaraSystemFactoryNew` / `NiagaraEmitterFactoryNew` / `NiagaraScriptFactoryNew` | 有 |
| `unreal.NiagaraSpriteRendererProperties` | 有，且 `sub_image_size` 可读（默认 (1,1)） |
| `unreal.NiagaraMeshRendererProperties` | **没有暴露** |
| `unreal.NiagaraEditorSubsystem` / `unreal.NiagaraEditorLibrary` | **都没有** |
| 从系统上拿 emitter/renderer（`emitter_handles`） | 读不到：`Failed to find property 'emitter_handles'` |
| `unreal.NiagaraSystem` 在 Python 里的成员 | 只有 2 个（`acquire_editor_element_handle`、`render_custom_depth`） |
| `unreal.MaterialEditingLibrary` + `MaterialExpressionTextureSampleParameterSubUV` / `ParticleSubUV` | 有 |

**结论：材质那半 Python 能做；"从零搭一个 Niagara 系统"Python 做不了**（这也解释了 GitHub 上那份 `flipbook2niagara` 为什么要自带一个编辑器插件代理）。

### 实测 3：ZDBridge 这条路需要的引擎 API 都在

| 要干的事 | API（本机 5.8.2 源码位置） |
|---|---|
| 往系统里加 emitter | `UNiagaraSystem::AddEmitterHandle(UNiagaraEmitter&, FName, FGuid)` — `Niagara\Classes\NiagaraSystem.h:332` |
| 往 emitter 上加渲染器 | `UNiagaraEmitter::AddRenderer(UNiagaraRendererProperties*, FGuid)` — `Niagara\Classes\NiagaraEmitter.h:983` |
| 拿 emitter 数据（含渲染器列表） | `UNiagaraEmitter::GetLatestEmitterData()` / `GetExposedVersion()` — 同文件 729/751 行 |
| 往模块栈里加模块 | `FNiagaraStackGraphUtilities::AddScriptModuleToStack(UNiagaraScript*, UNiagaraNodeOutput&, ...)` — `NiagaraEditor\...\NiagaraStackGraphUtilities.h:290` |
| 设模块输入 | 同文件 `GetOrCreateStackFunctionInputOverridePin` / `SetLinkedParameterValueForFunctionInput`（216/229 行） |
| 现成模板可以拿来当底 | `Engine\Plugins\FX\Niagara\Content\DefaultAssets\Templates\Emitters\SimpleSpriteBurst`、`SingleLoopingParticle`、`OmnidirectionalBurst`；系统模板 `SimpleExplosion`、`RadialBurst`… |

代价：这是 **NiagaraEditor 模块**的 API，ZDBridge 要加一条 editor-only 依赖（`NiagaraEditor`），并且模块栈编辑属于"编译一次才知道对不对"的那类代码 —— 预计要 2~4 轮编译往返。

## 五、复现命令（谁都能重跑一遍）

```powershell
# 1) Niagara 认不认识 Paper2D（期望 0 命中）
rg -n "PaperFlipbook|PaperSprite" "D:\UnrealEngine-5.8.2\Engine\Plugins\FX\Niagara\Source"

# 2) 精灵渲染器的 Sub UV 参数（期望看到 SubImageSize / SubImageIndexBinding）
rg -n "SubImageSize|SubImageIndexBinding" "D:\UnrealEngine-5.8.2\Engine\Plugins\FX\Niagara\Source\Niagara\Public\NiagaraSpriteRendererProperties.h"

# 3) 现有特效图集是不是网格（期望：紧凑排布，不是等分网格）
#    直接看 D:\NewData\CrossingVoidZDProject\Completed\Misaka\tool\AtlasCache\Click_Effect\Misaka_Click_Effect.png
#    （我们量到 930×1926；而特效源帧是 6 张 928×640）
```
