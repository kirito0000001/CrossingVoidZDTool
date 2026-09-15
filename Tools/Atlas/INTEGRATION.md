# free-tex-packer 整合参考

面向「要把这个开源项目融进别的项目」的技术参考。所有数据均从 `free-tex-packer-core@0.3.9` 源码实测提取。

---

## 1. 项目拆解：该用哪部分

| 仓库 | 状态 | 建议 |
|------|------|------|
| `odrick/free-tex-packer` | **已死**（最后提交 2021-04-28） | ❌ 不要用。Electron 壳子，依赖全是 2021 年的 |
| `odrick/free-tex-packer-core` | **活着**（npm 0.3.9，2026-07-24） | ✅ **只用这个**。纯 Node 库，无 GUI 依赖 |
| `free-tex-packer-cli` | 半死（2023-06） | ⚠️ 可参考，但不如自己包一层 |

**结论：只依赖 `free-tex-packer-core`，自己写薄薄一层 I/O。**

```bash
npm install free-tex-packer-core
```

---

## 2. 最小可用代码

```js
const { packAsync } = require('free-tex-packer-core');

const images = [
  { path: 'a.png', contents: fs.readFileSync('./a.png') },
  { path: 'b.png', contents: fs.readFileSync('./b.png') },
];

const files = await packAsync(images, {
  textureName: 'atlas',
  exporter: 'JsonArray',
});

// files: [{ name: 'atlas.png', buffer }, { name: 'atlas.json', buffer }]
for (const f of files) fs.writeFileSync(f.name, f.buffer);
```

回调式也有：`pack(images, options, (files, error) => {...})`

---

## 3. 完整 Options 表

core 源码 `index.js` 里的默认值（`options.X === undefined ? 默认 : options.X`）：

| option | core 默认 | 说明 | **推荐（UE）** |
|--------|----------|------|--------------|
| `textureName` | `"pack-result"` | 输出文件名 | 必填 |
| `width` / `height` | `2048` | 画布上限 | `2048`（大图集 4096） |
| `fixedSize` | `false` | 是否固定画布尺寸 | `false`（自动增长） |
| `powerOfTwo` | `false` | 尺寸取 2 的幂 | **`false`**（UE5 Paper2D 不强求，省空间） |
| `padding` | `0` | 图元间距 | `2` |
| `extrude` | `0` | 边缘像素外扩 | `2`（防 mip 漏色） |
| `allowTrim` | `true` | 裁透明边 | **`true`** |
| `trimMode` | `"trim"` | `"trim"` / `"crop"` | `"trim"` |
| `alphaThreshold` | `0` | trim 的 alpha 阈值 | `1` |
| `allowRotation` | `true` | 允许 90° 旋转 | `false`（UE 兼容差） |
| `detectIdentical` | `true` | 检测重复图 | `true` |
| `removeFileExtension` | `false` | sprite 名去扩展名 | **`true`** |
| `prependFolderName` | `true` | sprite 名带目录前缀 | `false`（自己控制） |
| `packer` | `"MaxRectsBin"` | 装箱器 | `"MaxRectsBin"` |
| `packerMethod` | 各 packer 的 defaultMethod | 启发式 | `"BestShortSideFit"` |
| `exporter` | `"JsonHash"` | 导出格式 | **`"JsonArray"`**（UE） |
| `textureFormat` | `"png"` | png / jpg / jpeg / webp | `"png"` |
| `filter` | `"none"` | none / mask / grayscale | `"none"` |
| `scale` | `1` | 缩放 | `1` |
| `scaleMethod` | `"BILINEAR"` | 缩放算法 | 默认 |
| `base64Export` | `false` | 内嵌 base64 | `false` |
| `suffix` / `suffixInitialValue` | `"-"` / `0` | 多页图集后缀 | 默认 |
| `tinify` / `tinifyKey` | `false` / `""` | TinyPNG 压缩 | 不用 |
| `appInfo` | package.json | 写进 meta 的应用信息 | 自定义 |

> ⚠️ `trimMode: "crop"` 和 `"trim"` 区别：
> `"trim"` → 保留 `spriteSourceSize` 偏移（UE Sprite 能还原原位）
> `"crop"` → 把 `trimmed` 强制写 `false`，`sourceSize` 改成裁剪后尺寸（**丢信息**）
> **UE 用 `"trim"`，不要用 `"crop"`。**

---

## 4. Packer 与启发式方法

| packer | defaultMethod | 全部 methods |
|--------|--------------|-------------|
| **`MaxRectsBin`** | `BestShortSideFit` | `BestShortSideFit` / `BestLongSideFit` / `BestAreaFit` / `BottomLeftRule` / `ContactPointRule` |
| `MaxRectsPacker` | `Smart` | `Smart` / `SmartArea` / `Square` / `SquareArea` |
| `OptimalPacker` | `Automatic` | `Automatic`（内部跑所有 packer×method 组合，挑最优，慢但最密） |

- 常规用 `MaxRectsBin` + `BestShortSideFit`
- 想最密且不在乎耗时 → `OptimalPacker`

> ⚠️ `packer: "MaxRectsPacker"` + `packerMethod: "BestShortSideFit"` 会抛
> `Unknown packer method BestShortSideFit` —— 老的 `MaxRectsPacker` 没有这个方法。

---

## 5. 导出器全表（18 个）

取自 `exporters/list.json`：

| type | 扩展名 | 允许 trim | 允许 rotate | 用途 |
|------|--------|----------|------------|------|
| **`JsonArray`** | `.json` | ✓ | ✓ | **数组 + filename ← UE Paper2D 用这个** |
| `JsonHash` | `.json` | ✓ | ✓ | 通用 hash |
| `Unreal` | `.paper2dsprites` | ✓ | ✓ | 官方 UE 模板，**hash 形式，实测 UE 兼容性差** |
| `XML` | `.xml` | ✓ | ✓ | 通用 XML |
| `Css` / `OldCss` | `.css` | ✓ / ✗ | ✓ / ✗ | 网页雪碧图 |
| `Pixi` | `.json` | ✓ | ✓ | PixiJS |
| `PhaserHash` / `PhaserArray` / `Phaser3` | `.json` | ✓ | ✓ | Phaser 2/3 |
| `Cocos2d` | `.plist` | ✓ | ✓ | Cocos |
| `Spine` | `.atlas` | ✓ | ✓ | Spine |
| `Starling` | `.xml` | ✓ | ✓ | Starling |
| `GodotAtlas` / `GodotTileset` | `.tpsheet` / `.tpset` | ✓ | ✓ | Godot |
| `Unity3D` | `.tpsheet` | ✓ | ✗ | Unity |
| `UIKit` | `.plist` | ✓ | ✗ | iOS UIKit |
| `Egret2D` | `.json` | ✗ | ✗ | Egret |

> ⚠️ 注意：选了 `allowTrim: false` 或 `allowRotation: false` 的 exporter，
> core 会**自动把对应 option 强制关掉**（`index.js` 第 83-84 行）：
> ```js
> if(!exporter.allowRotation) options.allowRotation = false;
> if(!exporter.allowTrim) options.allowTrim = false;
> ```

---

## 6. 自定义导出模板

`options.exporter` 可以传对象而不仅是字符串：

```js
{
  template: '/abs/path/to/my.mst',  // mustache 模板绝对路径
  fileExt: 'myext',
  allowTrim: true,
  allowRotation: true,
  // content: '...'  // 可选，直接给模板内容，跳过读文件
}
```

（若 `predefined: true`，`template` 按 `exporters/` 目录下的文件名解析）

### mustache 可用变量

```
rects[]              精灵数组
  .name              精灵名
  .frame             {x, y, w, h, hw, hh}   hw/hh 是半宽半高
  .spriteSourceSize  {x, y, w, h}   trim 后在原图中的位置
  .sourceSize        {w, h}         原图尺寸
  .rotated / .trimmed
  .index / .first / .last

config              当前配置
  .imageWidth / .imageHeight / .scale / .format
  .imageName / .imageFile
  .base64Export / .base64Prefix / .imageData

appInfo             {displayName, version, url}
```

### mustache formatter（`@jvitela/mustache-wax`）

`add` `subtract` `multiply` `divide` `offsetLeft` `offsetRight` `mirror` `escapeName`

模板写法示例（`{{#rects}} ... {{/rects}}` 循环，变量用 `{{name}}`，不转义用 `{{{name}}}`）。

---

## 7. UE Paper2D 专用：schema 与坑

### 能用的 schema（实测）

```jsonc
{
  "frames": [                      // ← 必须是「数组」！不是 hash
    {
      "filename": "frame_01",      // ← 必须有 filename 字段
      "frame": { "x": 4, "y": 4, "w": 128, "h": 98 },
      "rotated": false,
      "trimmed": true,
      "spriteSourceSize": { "x": 6, "y": 6, "w": 128, "h": 98 },
      "sourceSize": { "w": 140, "h": 110 },
      "pivot": { "x": 0.5, "y": 0.5 }   // 归一化 0~1
    }
  ],
  "meta": {
    "app": "...", "version": "1.0",
    "image": "atlas.png",          // 只写文件名，UE 按同目录解析
    "format": "RGBA8888",
    "size": { "w": 700, "h": 304 },
    "scale": 1
  }
}
```

### 三条实测结论

1. **`frames` 必须是数组 + `filename`**。官方 `Unreal.mst` 吐的是 hash（`{"frames": {"name": {...}}}`），UE 导入不稳定。**用 `JsonArray` 的内容 + `.paper2dsprites` 扩展名。**
2. **导入器靠扩展名 `.paper2dsprites` 分派**，不是靠 `meta.target`。早期我以为 `target: "paper2d"` 是必需字段，**实测不需要**（`JsonArray` 模板没有这个字段，照样能导入）。
3. **锚点是 UE 从 `sourceSize` / `spriteSourceSize` 反算出来的**，不是靠 `pivot` 字段。
   TexturePacker 的 paper2d 输出里**根本没有 `pivot`**，导入后照样不抖——因为它给所有帧写同一个
   `sourceSize`（= 原始画布尺寸），于是每帧锚点落在同一个坐标系里。
   反过来，各帧 `sourceSize` 不一致时，即使写了 `pivot: 0.5/0.5`，播放时每帧锚点也各在一处、会上下跳。
   → **要保证的是 `sourceSize` 统一**，`pivot` 写不写都行。

### 导入步骤

1. Edit → Plugins → 启用 **Paper2D**
2. 拖 `.paper2dsprites` 进 Content Browser → 自动生成 Texture + Sprite Sheet + 各 Sprite
3. 右键 Sprite Sheet → **Create Flipbooks**（按命名数字自动分组）
4. Flipbook 细节面板改 **Frames Per Second**（UE 默认 30fps，风格化序列帧实际 11-13fps）

---

## 8. 已知坑清单

| # | 坑 | 影响 | 解法 |
|---|---|------|------|
| 1 | core 内部按 `Object.keys(images).sort()` **字典序**处理 rects | MaxRects 拿到最差顺序，布局散、空洞多 | 调用前预排 `height desc → width desc → name asc`，再把 `path` 改成 `0000_原名` 让内部排序跟着走。**注意：这个序号会原样写进产出的 sprite 名**（见 #12），落盘前要剔掉 |
| 2 | `packer: "MaxRectsPacker"` 配 `BestShortSideFit` | 抛 `Unknown packer method` | 用 `MaxRectsBin` |
| 3 | `Unreal` 导出器是 hash 形式 | UE 导入不稳定 | 用 `JsonArray` |
| 4 | 默认 `trim=false`（我封装前） | 每个精灵占满源图（含透明边），pack 松散 | 默认 `trim=true` |
| 5 | 默认 `pot=true`（我封装前） | 画布向上取整到 2 的幂，大片空白 | 默认 `pot=false` |
| 6 | `trimMode: "crop"` | 丢 `spriteSourceSize` 偏移信息 | 用 `"trim"` |
| 7 | exporter 的 `allowTrim/allowRotation: false` | 会静默覆盖你传的 option | 检查 list.json |
| 8 | `prependFolderName` 默认 `true` | sprite 名带目录前缀，命名变丑 | 不想要就设 `false`。**但别把 `path` 退化成纯 basename**（见 #13）—— 那样这个开关会静默失效 |
| 9 | 自己实现 extrude 时把 `padding` 当成「扩边之外还要的空间」 | 相邻精灵的扩边互相覆盖，防 mip 漏色失效 | 间隙要给到 `2 × extrude`。core 内部已经算对了，自己写装箱器时按这个来 |
| 10 | 各帧 `sourceSize` 不一致 | UE 里每帧锚点各在一处，Flipbook 播放会跳 | 让所有帧的 `sourceSize` 指向同一个逻辑画布（TexturePacker 的做法：写原始画布尺寸） |
| 11 | 数组版 `.paper2dsprites` 用了和官方模板相同的文件名 | 静默覆盖，官方模板版拿不到了 | 数组版另起 `_array` 后缀，两份都留着 |
| 12 | 排序用的 `0000_` 序号泄漏进 sprite 名（`exporters/index.js`: `let name = item.name;` 直接拿 path 当名字） | 产出变成 `0013_Misaka-Sk2-d8361887…`，比哈希名还难看 | 落盘前剔除。用**精确字符串 + 单次替换**，不要用 `\d{4}_` 正则 —— 会把本来就叫 `0001_idle.png` 的源图误伤，还会 A→B 又匹配 B 的级联 |
| 13 | `path` 只给 basename | `prependFolderName` 靠路径里的 `/` 拼前缀，没 `/` 就什么都不做，**静默失效** | `path` 传「相对输入根目录的路径」，用 `/` 分隔 |
| 14 | `rotated:true` 时按 `frame.w/h` 去裁图 | 裁错，旋转帧内容全乱 | `rotated:true` 时图集里的贴图是 `(x, y, h, w)` 横向摆放，`frame.w/h` 写的是**未旋转**的逻辑尺寸；先按 `h/w` 取再转回来。TexturePacker 同一约定 |
| 15 | **只用一个装箱算法** | 图元尺寸接近时 MaxRects 反而差：同一批 17 帧，MaxRects（5 个启发式全试）密度 **86.1%**，朴素货架式 **94.3%**，TexturePacker **94.8%**。调 `packerMethod` 没用（BestAreaFit 反而更差 1.81×） | **几种算法 × 几种排序 × 一串候选尺寸全试一遍，按面积挑最优**（TexturePacker 就是这么干的：它有 `AlgorithmMaxRects` + `AlgorithmShelf`，还有 `Slow/Good/Best` 三档搜索力度、"Trying %u different sets of parameters"）。core 的 `packers/index.js` 把 list 导出了，push 一个自定义装箱器进去就会被 `getPackerByType` 和 OptimalPacker 自动认（见本仓库的 `shelf_packer.js`） |
| 15b | **排序键依赖传入顺序** | 货架对排序极其敏感：同一批同样的宽度，次级排序从「按宽度」换成「按名字」就 3 行变 4 行、面积差 8% | 排序键要**确定性**（破平一律用 name），不要依赖「谁先传进来」；否则同一个素材在两个语言实现里会给出差 5% 的结果。排序键也要一起搜 |
| 15c | core 的 padding 记账比 ue_atlas 多 | core 膨胀 `padding*2 + extrude*2`、内缩 `padding+extrude` → 两张图**核心内容**之间实际间隙 = `2*(padding+extrude)`；ue_atlas 是 `max(padding, 2*extrude)`。`padding=2,extrude=1` 时 core 给 6px，多留 4px/张 | 想要两边一致就得换算：core 的 padding 取 `(max(padding, 2*extrude) - 2*extrude) / 2` |
| 15d | 装箱器内部搜宽度 vs 外层反复调 `packAsync` | `packAsync` 每调一次都重渲染一张贴图，几百个候选宽度根本跑不动 | **把宽度搜索放进装箱器内部**（纯计算），core 只渲染最终那一次。装箱器只要保证放置结果落在 bin 内就行，放哪儿是它的自由 |
| 15e | 装箱器返回空数组 | PackProcessor 的 `while (rects.length)` 会**死循环**（它靠 removeRect 推进）。MaxRectsBin 放不下时也会返回 [] 并留下这个隐患 | 自定义装箱器必须保证「只要还有 rect 且它放得进 bin，就至少放下一张」，兜底路径一定要写 |
| 16 | core 只按你给的 `width/height` 建箱体，**自己不会挑形状** | 丢一个 2048×2048 方块箱子进去，BSSF 会排出一根 747×2044 的竖条，右边空一大块 | 别指望参数，要自己遍历 `width` 调 `packAsync` 多次取最优（见 #15）。每次调用都会重渲染一张贴图，所以帧数上百时把候选宽度砍到 5 个左右 |

---

## 8-B. 清单接口：把「名字 + 序号」交给调用方

上游已经有「精灵名 ↔ 帧序」对照表时（ZD 工具箱就是这种情况），不要让图集工具从文件名去猜。
一帧一条的清单就是为此设计的：

```jsonc
// atlas.json —— 路径相对清单文件所在目录
{ "atlas": "Misaka_Sk2", "spritePrefix": "Misaka_Sk2",
  "mode": "pack", "trim": true, "extrude": 1, "padding": 2, "rotate": true,
  "frames": [ { "file": "a.png", "name": "Misaka_Sk2_01", "index": 1 }, ... ] }
```

```bash
python ue_atlas.py --manifest atlas.json -o ./out --report result.json
node   pack_ue.js  --manifest atlas.json -o ./out --format unreal --report result.json
```

要点（自己封装时要注意的）：

| 点 | 说明 |
|---|---|
| 名字必须**原样落盘** | core 的 `removeFileExtension`（按 `.` 截断）和 `prependFolderName`（按 `/` 拼前缀）都会改名字，走清单时必须都关掉 |
| 内部的排序前缀不能泄漏 | core 按名字字典序处理 rects，所以要挂 `0000_` 序号；而它是拿 `path` 当 sprite 名的，落盘前必须精确剔掉 |
| **`_sequence.json` 才是帧序权威** | UE 建 Flipbook 按 sprite 名排序，名字无序时顺序会丢。序号固化在这个文件里，下游照它建 |
| 名字重复要**报错** | UE 里同名 sprite 会互相覆盖，是静默灾难，不能只警告 |
| 参数优先级 | 命令行显式给的 > 清单里的 > API 默认值（API 默认 `mode=pack`+`trim=true`，和命令行扫目录模式不同） |

完整字段表 / 校验规则 / `result.json` 结构 → `MANIFEST.md`。
Python 侧也可以不走进程：`from ue_atlas import run_atlas`。

## 9. 融进项目的几种形态

### A. 直接 npm 依赖（最简单）
```bash
npm install free-tex-packer-core
```
适合 Node 后端 / 构建脚本 / Electron 工具。

### B. Vendored 进仓库
core 是纯 JS（依赖 `sharp` / `jimp` / `mustache` 等 80 个包，约 61MB `node_modules`）。
如果要避免 npm 依赖，可以把 `node_modules/free-tex-packer-core` 整个目录拷进项目 —— 但它依赖 sharp（原生模块），**跨平台分发会有二进制问题**，不推荐。

### C. 前端 / 浏览器
core 依赖 `sharp`（Node 原生），**不能直接在浏览器跑**。
官方的 Web 版 `free-tex-packer.com/app` 是另一套（`odrick/free-tex-packer` 里的 `web build`）。
浏览器场景建议：自己用 canvas 实现，或调后端接口。

### D. 构建管线集成
`packAsync` 是 Promise，天然适合塞进 webpack / gulp / vite 插件：
```js
// vite 插件伪代码
async function atlasPlugin() {
  return {
    name: 'atlas',
    async buildEnd() {
      const images = await glob('src/sprites/**/*.png');
      const files = await packAsync(images, opts);
      // emit 到产物
    }
  };
}
```
官方已有 `gulp-free-tex-packer` / `grunt-free-tex-packer` / `webpack-free-tex-packer`（都是 2021 年的，可参考思路）。

---

## 10. 本目录两个封装的定位

| | `ue_atlas.py` | `pack_ue.js` |
|---|---|---|
| 引擎 | 自写 MaxRects（200 行） | `free-tex-packer-core` |
| 依赖 | Pillow | Node + core |
| **均匀网格** | ✅ `--mode grid`（Niagara SubUV 必需） | ❌ core 没这功能 |
| 多格式导出 | paper2dsprites + TP JSON | 18 种 |
| 适合 | 序列帧特效、要塞进 Python 管线 | 要多格式、要成熟引擎 |

**融合建议**：
- 只需要 UE + 序列帧 → 抄 `ue_atlas.py` 的 grid 逻辑（纯 Pillow，无外部依赖，最好移植）
- 要多引擎支持 → 包一层 `pack_ue.js`，核心就 100 行 I/O + 预排序
- 两套的**预排序**和**数组+filename 输出**是共通经验，移植时别漏

---

## 11. 版本与兼容性

- 当前实测版本：`free-tex-packer-core@0.3.9`（2026-07-24 发布）
- Node 要求：core 用 ESM/CJS 混合，Node 18+ 稳
- 贴图格式：`png` / `jpg` / `jpeg` / `webp`（`utils/imageFormats.js` 里定死，没有 tga/dds）
- 已知近半年更新：alpha 通道 bug 修复、旋转 bug 修复、WebP 支持、sharp/jimp 升级
