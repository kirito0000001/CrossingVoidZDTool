# UE 图集打包工具集

TexturePacker 的开源替代方案，面向 Unreal Engine。

> 📘 **要把这套东西融进别的项目？看 [`INTEGRATION.md`](./INTEGRATION.md)**
> 完整 options 表 / 18 个导出器 / mustache 模板变量 / 16 条已知坑 / 融合形态建议。
>
> 🔌 **要和 ZD 工具箱对接（名字 + 序号）？看 [`MANIFEST.md`](./MANIFEST.md)** —— 清单接口契约。
>
> 两套实现，挑一套用：

| | `ue_atlas.py` | `pack_ue.js` |
|---|---|---|
| 依赖 | Python + Pillow | Node + `free-tex-packer-core` |
| 打包引擎 | 自写 MaxRects | **官方仍在维护的核心**（2026-07 还在更新） |
| 导出格式 | paper2dsprites + TP JSON | 13 种（Unreal / JsonArray / JsonHash / XML / Spine / Phaser3 / Cocos2d / Unity3D / Godot / Egret / Starling / UIKit / CSS） |
| 均匀网格(Niagara SubUV) | ✓ 有 grid 模式 | ✗ 只有紧密装箱 |
| 边缘外扩 extrude | ✓ | ✓ |
| 透明裁切 trim | ✓ | ✓ |
| 画布形状优化（又方又满） | ✓ 搜候选宽度挑最优 | ✓ 装箱器内部自搜行宽 |
| **装箱算法** | MaxRects + **货架式**，两个都试 | core 的 MaxRects + **挂上货架式**，两个都试 |
| **清单接口（名字 + 序号）** | ✓ `--manifest` / `run_atlas()` | ✓ `--manifest` |
| 播放不抖（锚点一致） | ✓ grid 自动统一 sourceSize | 源帧来自同一画布才行 |
| 推荐场景 | 序列帧特效、要塞进构建管线 | 想用成熟引擎、要多格式导出 |

---

## 一、安装

### Python 版
```bash
pip install pillow
python ue_atlas.py --help
```

### Node 版
```bash
npm install free-tex-packer-core
node pack_ue.js --help
```

---

## 二、Python 版 `ue_atlas.py`

### 序列帧特效 → Niagara SubUV（主力）

```bash
python ue_atlas.py ./fx_hit_frames -o ./out --mode grid --cols 4 --padding 2 --name fx_hit
```

输出 `fx_hit.png` + `fx_hit_grid.json`（含 NumFramesX/NumFramesY）。  
`fx_hit.paper2dsprites` 也照样生成，以后想用 Paper2D 也能直接拖进 UE。

### 紧密图集 → Paper2D Sprite / Flipbook

```bash
python ue_atlas.py ./hero_idle -o ./out --mode pack --trim --padding 2 --extrude 2 --crop --name hero_idle
```

### 每个子目录一张图集

```
vfx/
├── skill_hit/     (frame_01.png, frame_02.png, ...)
├── skill_spark/
└── buff_aura/
```
```bash
python ue_atlas.py ./vfx -o ./out --mode grid --each-subdir
```

### 参数

| 参数 | 默认 | 说明 |
|------|------|------|
| `--mode` | grid | grid / pack |
| `--padding` | 2 | 图元间距。UE Paper2D 图集推荐 0，带 mip 的特效/UI 开 2-4 |
| `--extrude` | 0 | 边缘像素外扩，防 mip 漏色。开了会把间距自动提到 **2 × extrude**（相邻两图各扩一份，只提 1 倍会互相覆盖） |
| `--trim` | 关 | pack 模式裁透明边 |
| `--rotate` | 关 | 允许 90° 旋转。**不预设旋转一定有好处** —— 开了之后是把「不转 / 按需转 / 全转横放」三种姿态都排一遍，按最终面积挑。清一色竖长条素材通常仍会选不转（如 Misaka-Sk2 实测：转了反而大 4.6%）；「几根高梁混一堆矮墩」时才真受益。部分 UE 流程对 `rotated:true` 兼容不好，介意就别开 |
| `--max-size` | 2048 | pack 最大边长 |
| `--pot` | 关 | 画布取 2 的幂。**作用于裁切之后**：默认给的是裁紧的非幂画布，开了才回到幂尺寸。Niagara 无所谓 |
| `--packer` | auto | `auto`=MaxRects 与货架式都试、挑最优；`shelf`=只走货架式（图元尺寸接近时更省）；`shelf-rot`=货架式 + 旋转姿态全试；`maxrects`=只走紧密装箱 |
| `--crop` | **开** | pack 模式裁掉未使用区域（`--no-crop` 关闭）。**这才是 pack 省空间的关键开关** |
| `--cols` | 自动 | grid 列数；0 = sqrt(n) |
| `--anchor` | center | grid 每帧在格子内对齐：center / topleft / bottomleft |

---

## 三、Node 版 `pack_ue.js`

```bash
# UE Paper2D
node pack_ue.js ./sprites -o ./out --name hero_idle --trim --rotate --padding 2 --extrude 2

# 换导出格式
node pack_ue.js ./sprites -o ./out --format json-array

# 每个子目录一张
node pack_ue.js ./vfx -o ./out --each-subdir

# 走清单：名字与序号由清单给定（对接 ZD 工具箱）
node pack_ue.js --manifest atlas.json -o ./out --format unreal --report result.json
```

`--format` 可选：`unreal`（默认）/ `json-array` / `json-hash` / `xml` / `css` / `phaser3` / `cocos2d` / `spine` / `starling` / `unity3d` / `godot` / `egret` / `uikit`

**注意**：`unreal` 格式会同时产出两份——
- `xxx.paper2dsprites`：官方 Unreal.mst 模板（frames 是 **hash/dict**）
- `xxx_array.paper2dsprites`：数组 + `filename` 形式

UE 的 Paper2D 导入器对两种形式的支持历史上出过差异，**两个都拖一次，用能正常出 Sprite 的那个**。

---

## 三-B、清单接口：名字 + 序号（对接 ZD 工具箱）

精灵名和帧序原来只能从**源文件名**推。但对 ZD 工具箱来说，名字和序号在工具box那边
**本来就有对照表**，不该让图集工具去猜。所以有了一帧一条的清单：

```jsonc
{
  "atlas": "Misaka_Sk2",
  "spritePrefix": "Misaka_Sk2",
  "mode": "pack", "trim": true, "padding": 2, "extrude": 1, "rotate": true,
  "frames": [
    { "file": "Misaka-Sk2-a1b2.png", "name": "Misaka_Sk2_01", "index": 1 },
    { "file": "Misaka-Sk2-e5f6.png", "name": "Misaka_Sk2_02", "index": 2 }
  ]
}
```

TSV 也一样（一行一帧，`文件<TAB>名字<TAB>序号`）。调用：

```bash
python ue_atlas.py --manifest atlas.json -o ./out --report result.json
node   pack_ue.js  --manifest atlas.json -o ./out --format unreal --report result.json
```

产出里多一个 **`<atlas>_sequence.json`** —— 按 `index` 升序排好的帧表（含每帧在图集里的矩形）。
**它才是动画帧序的唯一权威**：UE 建 Flipbook 是按 sprite 名排序的，名字一旦无序顺序就丢了，
而这个序号是工具箱给的。Unreal 侧照它建 Flipbook 就精确了，
参考实现见 `examples/unreal_flipbook_from_sequence.py`。

Python 侧还能当库直接调：`from ue_atlas import run_atlas`。
完整字段表、校验规则、`result.json` 结构 → **[`MANIFEST.md`](./MANIFEST.md)**。

---

## 四、UE 端导入步骤

1. 启用 Paper2D 插件（Edit → Plugins → Paper2D）
2. 把 `.paper2dsprites` 拖进 Content Browser —— 自动创建 Texture + Sprite Sheet + 每个 Sprite
3. 右键 Sprite Sheet → **Create Flipbooks**（按命名数字自动分组）
4. 选中 Flipbook，细节面板改 **Frames Per Second**（UE 默认 30fps；91Act 那类曲线驱动的序列帧实际约 11-13fps，要手动降）

---

## 五、坑 & 实战经验

### A. `fx_core_array.paper2dsprites`（数组 + filename）才是能用的那份

UE Paper2D 导入器实测只对**数组 + `filename` 形式**稳定。官方 Unreal.mst 模板吐的是 **hash/dict** 形式，导入有概率不识别。
脚本现在两种都产 —— **优先拖 `_array.paper2dsprites`**。

### B. core 的默认排序是字典序，浪费空间

`free-tex-packer-core` 内部按文件名 `Object.keys(images).sort()` 处理 rects。MaxRects 想要好布局必须**最大先装**。
`pack_ue.js` 内部已经预排为 `height desc -> width desc -> name asc`（和 TexturePacker 一致），如果不预排就直接用 core，画面会变成"小空块+大散块"的丑陋布局。

### C. 播放抖不抖，看的是 `sourceSize` 统不统一

UE Paper2D 导入时靠两个字段反算每帧的 pivot：

```jsonc
"spriteSourceSize": {"x": 356, "y": 242, "w": 168, "h": 336},   // 帧内容在原画布里的位置
"sourceSize":       {"w": 928, "h": 640}                        // 原画布尺寸
```

- 所有帧 `sourceSize` 一致 → 每帧锚点落在同一个坐标系 → Flipbook 播放稳
- 各帧 `sourceSize` 不同 → 每帧锚点各在一处 → 播放时上下跳

TexturePacker 的做法就是给所有帧写同一个 `sourceSize`（= 原始画布尺寸），所以它导入后不抖，
**而且它根本不输出 `pivot` 字段** —— 锚点是 UE 从上面两个字段算出来的，不是靠某个"锚点"字段。

本工具的两种情况：

- **`grid` 模式**：所有帧的 `sourceSize` 统一写成 cell 尺寸，帧在 cell 里的对齐偏移记进 `spriteSourceSize`
  → 天然一致，不管源帧尺寸多乱。
- **`pack` 模式**：`sourceSize` 就是各帧的原始尺寸。**只要输入是「同一张画布上、带完整透明边」的帧，
  它就一致**（和 TexturePacker 一样，因为工具自己会 trim 并记下偏移）；尺寸不一时会打印警告。

> ⚠️ 帧要是已经被裁过，它在原画布里的位置信息就已经没了，谁都补不回来。
> 所以**把带完整画布的帧交给工具裁（`--trim`），别自己先裁**。

### D. `pack_ue.js` 默认值就是紧凑版（trim=on, pot=off）

之前默认 `trim=false` + `pot=true` 是踩坑点：
- 不 trim → 每个精灵占满源图大小（含透明边），pack 出来松散
- 强制 POT → 画布向上取整到 2 的幂，下面/右边留大片空白

现已改默认 `trim=true` + `pot=false`（UE5 Paper2D 不强求 POT，更省空间），如上 12 帧 demo 现在能装到 **700×304** 而不是 1024×512。

特殊场景再覆盖：
- 想做带 mip 的 3D 贴图（位图字体、远景贴图）：开 `--pot`
- 想保留完整源图方便查找（不建议）：`--no-trim`

### E. 91Act / 边狱巴士风格化项目 → grid 模式优先

你之前逆向笔记里写的"图集以 2×2 / 3×3 为主"，Niagara SubUV 要的就是**均匀网格**，用 `pack` 模式反而害处：
- 网格不规则，SubUV 的 `NumFramesX/Y` 不能写死成常量，得逐 Sprite 表单独算
- MaxRects 把大小不一的帧拆得到处都是，材质采样 UV 计算容易错位

→ **特效/UI 全部用 `ue_atlas.py --mode grid`**，只有角色大动作才用 `pack` 模式（`pack_ue.js`）。

**前提是这些帧来自同一张画布、尺寸一致。** 帧尺寸不一时，grid 会按最大帧定格子、把小帧居中塞进去，
每格内容占比不一样 —— SubUV 的 UV 不会采错位，但画面上特效会忽大忽小，当 Flipbook 用还会跳
（见上面 C 节）。这种情况工具会打印警告，两条出路：先把帧统一到同一画布，或者改用 `pack` 模式。

### F. 上游生态现状（2026-09 实测）

查了 `odrick/free-tex-packer` 的全部 239 个 fork：

| 仓库 | 最后提交 | 状态 |
|------|---------|------|
| `odrick/free-tex-packer`（GUI） | **2021-04-28** | **死了**。README 顶上作者自己写"I don't have time to improve this app anymore" |
| `odrick/free-tex-packer-core`（引擎） | **2026-07-24** | **活着**。npm 0.3.9，2026-06 修了 alpha 通道 bug、旋转 bug，加了 WebP |
| `KenneyNL/free-texture-packer` | 2026-06-28 | 只删了那句"我不维护了"的声明，代码一行没动。Kenney 可能打算接手，**持续观察** |
| `kitzsh/tex-packer-ex` | 2026-04-05 | 真有改动：更新 libs、react 去 deprecated API。但 **0 个 release**，要自己 build，且默认导出改成 Sparrow/Starling（为 FNF modding 做的） |
| `dcrawl/free-tex-packer` | 2026-06-28 | 只加了 Apple Silicon 支持 |
| `blus007/free-tex-packer` | 2026-05-05 | 加了 Unity3D SmartUpdate + 排序改进，但 "Local build only" |
| `terraKote/free-tex-packer` | 2026-04-05 | 修 import errors |
| `zl3388/FreeAtlasPro` | 2024-07-14 | 名字听着唬人，代码是 2021 年原样，纯改名 |

**结论**：GUI 层面没人真正接手。但**引擎核心一直在维护**，所以最实用的路径是直接调 core（就是 `pack_ue.js` 做的事），不必趟 Electron 老壳子的坑。

---

## 六、已知坑

- **`.paper2dsprites` 就是 JSON**，改扩展名不影响内容。UE 靠扩展名分派 `PaperSpriteSheetImportFactory`。
- **锚点是算出来的，不是写出来的**：UE 用 `sourceSize` / `spriteSourceSize` 反算每帧 pivot。
  各帧 `sourceSize` 不一致，导入后播放就会抖（见第五节 C）。
- **`--extrude` 会把 `--padding` 提到 2 倍**：相邻两图各向外扩一份，间距不够就是互相覆盖，
  防 mip 漏色的作用直接没了。想自己控间距，就把 `--padding` 给到 `--extrude` 的两倍以上。
- **pack 模式的画布默认是裁紧的非幂尺寸**：`--no-crop` 才保留整幅，`--pot` 才回到 2 的幂。
- `--rotate` 谨慎开。`rotated:true` 在部分第三方 Pipeline 插件下不识别。
  **副作用**：允许旋转后装箱器会把竖长条横过来放，通常能明显提密度。实测同一批 17 帧
  「不旋转 1179×1273（1.43 倍净面积）」vs「旋转 1057×1177（1.18 倍）」。
  TexturePacker 的 paper2d 输出里也有 `rotated:true`，所以 UE 侧这条是通的。
- **`rotated:true` 时 `frame.w/h` 写的是「未旋转」的逻辑尺寸**，图集里的贴图实际按 `(x, y, h, w)` 横向摆放。
  读的时候必须先按 `h/w` 取、再转回来。TexturePacker 同一约定，照着做才对齐。
- **sprite 名 = 源文件名（去扩展名）**（扫目录时），两套实现都是。名字乱是源文件名乱，不是打包环节的问题。
  而 **sprite 名是帧顺序的唯一载体**（UE 建 Flipbook 按名字排序），所以想顺序正确，
  要么把源帧命名成 `frame_01`…`frame_N`，要么**走清单把名字和序号显式给进来**（见第三节 B）——
  后者还会额外产出 `_sequence.json`，让 Unreal 侧照序号精确建 Flipbook，不依赖名字排序。
- 91Act / 边狱巴士那类风格化 2D 项目，图集以 2×2 / 3×3 均匀网格为主 → **优先用 grid 模式**，别用 MaxRects 紧密装箱，Niagara SubUV 要的就是均匀网格。
- **`pack_ue.js` 的两条（都已修，别再踩）**：
  - 为了让引擎按「高度降序」处理图元，内部会给文件名挂 `0000_` 序号；而引擎直接拿这个当 sprite 名
    （`exporters/index.js`: `let name = item.name;`），所以**序号会原样写进产出**。
    修法是落盘前用「精确字符串 + 单次替换」剔掉前缀 —— 不能用 `\d{4}_` 正则，
    否则会把本来就叫 `0001_idle.png` 的源图误伤，还会 A→B 之后又匹配到 B 规则的级联。
  - `--prepend-folder` 依赖路径里有 `/`。原来只把 basename 交给引擎，结果这个开关是**静默空操作**；
    现在传的是「相对输入根目录的路径」，实测才真的拼出 `Sk2_a/xxx`。
- **`ue_atlas.py` 的 `extrude_img()` 底边扩边原来写到了画布外**（paste 的 y 写成 `e+h+e`，
  而画布只有 `h+2e` 高）→ 上/左/右有扩边、**下没有**，防漏色只做了一半。
  自检原来只查了横向宽度所以没抓到，现在按颜色求包围盒、两个方向都查。
- **装箱算法本身也要换着试：MaxRects 在图元尺寸接近时反而差。**
  同一批 17 帧 Misaka-Sk2，MaxRects（5 个启发式全试过）密度 **86.1%**，
  一个朴素的**货架式（shelf）**装箱 **94.3%**，TexturePacker **94.8%**。
  差 8% 全在算法选择上，调 MaxRects 的参数一点用没有（BestAreaFit 反而更差）。
  现在两套实现都是「MaxRects + 货架 × 几种排序」全试一遍按面积挑最优，默认就是。
  另外货架对**排序键**也极其敏感 —— 同一批同样的宽度，只把次级排序从「按宽度」换成「按名字」，
  就从 3 行变 4 行、面积差 8%。所以排序键也必须一起搜，而且定义要**确定性**的
  （破平用 name，不能依赖「谁先传进来」，否则两个工具会给出不同结果）。
- **两套实现的画布形状现在都是搜出来的**，不再是「扔个方块箱子听天由命」。
  背景：MaxRects 的 BSSF 启发式在图元尺寸接近时会一路往下贴 —— 17 帧 Misaka-Sk2
  原来被排成 `603×2035`（1:3.37）和 `747×2044`（1:2.74），右边空一大块；
  TexturePacker 给的是 `1132×996`（1:1.14）。
  修法就是把**箱体宽度当变量搜一遍**（固定宽度、给足高度，看它自己缩出来的紧凑尺寸），
  打分 `面积 × 长宽比^0.4`。
  实测同一批素材：

  | 实现 | 最初 | 只加画布搜索 | **加上货架式后** | 密度 |
  |---|---|---|---|---|
  | ue_atlas.py `pack` | 603×2035 (1:3.37) | 1057×1177 | **1115×1019** | **94.1%** |
  | pack_ue.js `unreal` | 747×2044 (1:2.74) | 1020×1218 | **1114×1018** | **94.3%** |
  | *TexturePacker（参照）* | — | — | *1132×996* | *94.8%* |

  密度从 86% 一路到 94.3%，**距 TexturePacker 只剩 0.6%**。
  想关掉搜索用 `--no-search`（pack_ue.js），ue_atlas.py 里是 `search_canvas()`；
  指定算法用 `--packer auto|shelf|shelf-rot|maxrects`。
- **旋转不是免费午餐，别默认开。** 货架式里旋转的唯一价值是**压低行高**（行高 = 行内最高那张，
  整行都按它留空间）：塞进已有行时行高已定，转了只多占宽度，纯亏；只有开新行时才可能受益。
  所以实现是**三种姿态全试一遍按面积挑**（`never` / `auto` 按需 / `all` 全横放），
  而不是「开了就一定转」。实测 Misaka-Sk2 那 17 帧清一色竖长条（147×355 … 256×266）：

  | 姿态 | 画布 | 面积 | 旋转帧 |
  |---|---|---|---|
  | `never`（最优） | **1115×1019** | **1,136,185** | 0 |
  | `all` 全横放 | 358×3320 | 1,188,560 | 17 |
  | `maxrects` + 旋转 | 1135×1136 | 1,289,360 | 16 |

  横放反而大 4.6% —— 因为转横后每张宽到 264~355，一行只放得下一两张，行数反而变多。
  **理论上限（Σ 裁剪后可见面积）= 1,051,461**，即 1025×1025；
  当前 1,136,185 比它多 8.1%，TexturePacker 的 1,127,472 多 7.2% —— 差在装箱启发式本身，不在形状。
- **Node 侧旋转曾经永远不生效**（同一类 bug）：core 的 `PackProcessor.js:198` 只传 3 个参数
  （`new packerClass(width, height, combo.allowRotation)`），靠 `combo.allowRotation` 把
  「不转 / 允许转」当两个组合各跑一遍。如果自定义装箱器把姿态写死在构造函数第 4 个参数上，
  `allowRotation=true` 那一轮会被默认值吃掉。**自定义 `Packer` 子类必须从第 3 个参数推导姿态。**
- **`pack_ue.js` 的 `--packer-method`** 可以换启发式，但**换参数没有免费午餐**（实测同一批 17 帧）：
  `BestShortSideFit` 747×2044 / `BestAreaFit` 937×2035（反而更差）/ `BestLongSideFit` 较方但面积多一成 /
  `BottomLeftRule` 面积最小(1.22×)但贴着 2048 上限、加帧就分页。
  所以默认仍是 BSSF，靠外面的宽度搜索改善形状。
- 改完工具跑一下：
  - `python tests/check_atlas.py`（55 项）—— 钉住扩边间隙（两个方向都查）、grid 的锚点统一、
    `paper2dsprites` 的 schema、crop/pot 画布语义、**画布不能退回竖条**、
    **清单接口契约**（名字原样落盘、`_sequence.json` 按序号升序、重名/缺文件报错）、
    **装箱器注册与 `auto` 契约**（auto 必须不差于两个单算法里较好的那个）、
    **旋转该转才转且取图能逐像素还原**。
  - `node tests/check_pack_ue.js`（33 项）—— 钉住排序前缀不泄漏、前缀剔除不误伤、`--prepend-folder` 生效、
    `_array` 与官方模板版两份都留、`sourceSize` 保留原画布、**清单接口契约**、
    **货架装箱器真的生效**（版面必须与 MaxRects 不同）、
    **`allowRotation` 不被姿态默认值吃掉**。

---

## 七、TexturePacker 8.2.2 拆包记录（2026-09-10）

把官方 MSI 拆开看了一遍。`msiexec /a` 要管理员权限（报 1314），最后是
**Bandizip 的 CLI（`bz.exe`）**一次解开的：`bz x` 先解 MSI 的 OLE 流拿到 `software.cab`，
再 `bz x` 解 CAB 拿到全部 311 个文件。

- 安装包是标准 OLE2 复合文档，里面一个 **28.5 MB 的 LZX 压缩 CAB**，311 个文件。
- **打包引擎是独立的 `TexturePackerLib.dll`，23 MB。** UI 是 Qt6
  （Qt6Core/Gui/Widgets/Quick/Qml…），另带 PVRTexLib（PVRTC 压缩）、HQX 放大、Qt6 的网络与 TLS。
- 导出器不是代码，是一堆 **Grantlee 模板 + 描述文件**：
  `resources_exporters_<平台>_<名字>_<模板>.<扩展名>`，覆盖 egret / gamemaker / godot /
  phaser / orx / uikit / noesisgui / playcanvas / solar2d / kwiksher2 / spritestudio / panda / unity / plain …
  —— 和我们「引擎 + 模板」的分工是同一个形状。
- 文件清单用 Python 读 CAB 目录就能拿到，**LZX 数据本体没有解**（`extrac32` 会先把整个
  folder 解一遍，84 MB 的那个 folder 十分钟没跑完，`tar`(bsdtar) 直接报 Invalid CAB header）。
### 字符串分析拿到的算法事实（这些是真正有用的）

不反编译代码，只看字符串/符号名，就拿到了它算法的骨架：

| 字符串 | 说明 |
|---|---|
| `AlgorithmMaxRects` / `AlgorithmShelf` / `AlgorithmBasic` / `AlgorithmPolygon` | **四种装箱算法**，货架式与 MaxRects 并列 |
| `BestShortSideFit` / `BestLongSideFit` / `BestAreaFit` / `BottomLeftRule` / `ContactPointRule` | MaxRects 的 5 个启发式，和 jylänki 那套公开的 MaxRects 完全一致 |
| `--pack-mode <mode>` `Optimization mode: Fast, Good, Best` | **搜索力度三档** |
| `Search for the minimum fitting power of 2 size` | 它会搜最小能装下的尺寸 |
| `Searches for the minimum size but aborts after some time.` | = Good 档 |
| `Searches intensively for the minimum size. Might take some time....` | = Best 档 |
| `Best result: %1x%2. Optimizing...` | 找到一个结果后还会**继续收缩优化** |
| `Trying %u different sets of parameters` | **多参数集全试** |
| `Sorts sprites by their area (width*height)` | 排序键可选 |
| `.?AVAlgorithmMaxRectsSettings@@` / `Heuristic` | MaxRects 的设置类里有 Heuristic 字段 |

**结论：它不是靠某个秘方，而是「几种算法 × 几种排序 × 一串候选尺寸」全试一遍挑最优。**
这也解释了为什么它 94.8% 而我们原来的单算法只有 86%。

照着这个结论做的改动就在上面第六节：加了货架式装箱器、把排序键和宽度都纳入搜索，
密度从 86.1% 提到 94.3%，**距它只剩 0.6%**。

> 反编译它的核心代码去"照抄"既不合适、性价比也极低（23MB 去掉符号的 C++）。
> 真正有用的信息全在上面这张表里了，而且字符串+行为反推的路子完全够用。

---

## 八、等价性验证（换素材可复跑）

不看"两张图集长得像不像"，而是**模拟 UE 取图**：

```
取图 = 从图集裁出 frame{x,y,w,h}
     → 若 rotated 则读作 (x,y,h,w) 再逆时针转回
     → 贴回一张 sourceSize 大小的空画布，位置 = spriteSourceSize{x,y}
比对 = 上面这张 vs 源 PNG，逐像素比 RGBA
```

脚本在 `../compare_atlas.py` + `../make_visual.py`（相对本仓库目录）。
17 帧 Misaka-Sk2 实测：TexturePacker / ue_atlas.py / pack_ue.js 的每一份产出
**重建后与原图都是 0 像素差异**，`sourceSize` 全部统一为 928×640。
