# 清单（Manifest）接口

给外部程序对接用：**一帧一条，显式给「图片 + 精灵名 + 序号」**。

为什么要这个：精灵名和帧序本来只有两个来源——源文件名。但对接 ZD 工具箱时，
名字和序号在工具箱那边**本来就有对照表**（角色代号、技能名、动作帧号都是工具box的领域知识），
不该让图集工具去猜文件名。所以把「名字 + 序号」提升成正式输入。

---

## 一、最小示例

一个 JSON 清单：

```jsonc
{
  "atlas": "Misaka_Sk2",              // 输出文件名（不含扩展名）
  "spritePrefix": "Misaka_Sk2",       // 可选：帧没给 name 时用它 + 补零序号
  "mode": "pack",                     // 可选：grid | pack
  "trim": true,
  "padding": 2,
  "extrude": 1,
  "rotate": true,
  "frames": [
    { "file": "Misaka-Sk2-a1b2c3d4.png", "name": "Misaka_Sk2_01", "index": 1 },
    { "file": "Misaka-Sk2-e5f6a7b8.png", "name": "Misaka_Sk2_02", "index": 2 }
  ]
}
```

同一个清单的 TSV 写法（好手工维护、好 diff）：

```
# file	TAB name	TAB index        ← 这一行是注释
Misaka-Sk2-a1b2c3d4.png	Misaka_Sk2_01	1
Misaka-Sk2-e5f6a7b8.png	Misaka_Sk2_02	2
```

两种写法完全等价，`.tsv` / `.csv` / `.txt` 都按「一行一帧」解析（制表符优先，其次逗号，最后空格）。
`file` 是**相对清单文件所在目录**的路径，绝对路径也行。

调用：

```bash
# Python 版
python ue_atlas.py --manifest atlas.json -o ./out --report result.json

# Node 版
node pack_ue.js --manifest atlas.json -o ./out --format unreal --report result.json
```

---

## 二、字段

| 字段 | 必填 | 说明 |
|---|---|---|
| `frames[].file` | ✅ | 图片路径。相对路径按**清单文件所在目录**解析 |
| `frames[].name` | 可选 | 写进图集的**精灵名**。省了就用 `spritePrefix` + 补零序号 |
| `frames[].index` | 可选 | **动画帧序号**（整数）。全缺就按数组顺序 1..N；部分缺就从现有最大值往后补 |
| `atlas` | 可选 | 输出文件名（不含扩展名）。省了用清单文件名 |
| `spritePrefix` | 可选 | 帧没给 `name` 时的前缀。省了用 `atlas` |
| `mode` | 可选 | `grid` / `pack` |
| `trim` / `padding` / `extrude` / `rotate` / `cols`(=`columns`) / `anchor` / `maxSize`(=`max_size`) / `pot` / `crop` | 可选 | 打包参数，含义同命令行。**命令行显式给的优先于清单里的** |

> **API 默认值和命令行扫目录模式不同**：清单（以及 Python 的 `run_atlas()`）默认
> `mode=pack` + `trim=true`——因为它就是给「把序列帧打成精灵表」用的。
> 命令行扫目录模式默认是 `mode=grid` + 不裁边。

---

## 三、产出

| 文件 | 说明 |
|---|---|
| `<atlas>.png` | 图集贴图 |
| `<atlas>.paper2dsprites` | UE Paper2D 导入文件（`pack` 模式） |
| `<atlas>.json` | TexturePacker JSON(hash) 通用格式 |
| `<atlas>_grid.json` | `grid` 模式专用：cols/rows/cell/帧数，给 Niagara SubUV |
| **`<atlas>_sequence.json`** | **按序号排好的帧表 —— 动画帧序的唯一权威**（只有序号已知时才产出） |
| `result.json` | `--report` 指定的机器可读结果 |

### `<atlas>_sequence.json`

```jsonc
{
  "atlas": "Misaka_Sk2",
  "image": "Misaka_Sk2.png",
  "sprites": "Misaka_Sk2.paper2dsprites",
  "texture": { "w": 1020, "h": 1218 },
  "frameCount": 17,
  "order": "frames 已按 index 升序排列，这个顺序就是动画帧序（第 0 帧在前）",
  "frames": [
    {
      "index": 1,
      "name": "Misaka_Sk2_01",
      "frame": { "x": 344, "y": 405, "w": 166, "h": 335 },   // 图集里的矩形
      "rotated": true,                                        // 读图时按 (x,y,h,w) 横向取再转回
      "trimmed": true,
      "spriteSourceSize": { "x": 357, "y": 243, "w": 166, "h": 334 },
      "sourceSize": { "w": 928, "h": 640 }
    }
  ]
}
```

**为什么要有这个文件**：UE 从 sprite sheet 建 Flipbook 时是按 **sprite 名排序**决定帧序的。
名字一旦不是有序的（比如源帧是哈希名），顺序就丢在导入这一步了。
序号是工具箱给的权威值，这里把它连同每帧在图集里的矩形一起固化下来，
Unreal 侧脚本可以照它精确建 Flipbook，不用去猜名字排序。

> 反过来，如果希望「右键 Sprite Sheet → Create Flipbooks」这条路也能得到正确顺序，
> 那 `name` 就必须是**能正确排序**的（比如统一补零的 `Misaka_Sk2_01`…`_17`）。
> 两条路都覆盖了：名字管编辑器里手动建，`_sequence.json` 管脚本建。

### `result.json`（`--report`）

```jsonc
{
  "ok": true,
  "atlas": "Misaka_Sk2",
  "outdir": "D:/out",
  "mode": "pack",
  "image": "D:/out/Misaka_Sk2.png",
  "sequence": "D:/out/Misaka_Sk2_sequence.json",
  "size": { "w": 1020, "h": 1218 },
  "frameCount": 17,
  "pages": [ { "name": "Misaka_Sk2", "image": "...", "sequence": "...", "size": {...}, "sprites": 17 } ],
  "frames": [ { "index": 1, "name": "Misaka_Sk2_01" } ],
  "warnings": []
}
```

多页（图集装不下会分页）时 `pages` 有多项；单页时同时给出扁平的 `image` / `size`。

---

## 四、给 Python 侧当库直接调

不想起进程的话，`ue_atlas.py` 也能直接 import：

```python
from ue_atlas import run_atlas

result = run_atlas(
    [("D:/frames/a.png", "Misaka_Sk2_01", 1),
     ("D:/frames/b.png", "Misaka_Sk2_02", 2)],
    outdir="D:/out",
    name="Misaka_Sk2",
    mode="pack", trim=True, extrude=1, padding=2, rotate=True,
)
print(result["sequence"], result["frameCount"])
```

`manifest` 参数三种写法都收：清单文件路径 / 清单 dict / `[(图片, 名字, 序号), ...]`。
关键字参数会覆盖清单里的同名项。

---

## 五、校验规则（都会明确报错，不静默）

| 情况 | 处理 |
|---|---|
| `file` 缺失或文件不存在 | **报错**，指出是第几条 |
| 精灵名重复 | **报错** —— UE 里同名 sprite 会互相覆盖，是静默灾难 |
| 序号重复 | 警告（允许，但多半不是本意） |
| `index` 不是整数 | **报错**，指出是第几行 |
| 名字/序号缺省 | 按规则补齐（见上表） |

---

## 六、完整示例：从源帧目录生成清单

工具箱侧大致就是这么生成清单的（等价于把工具箱已有的「名字 ↔ 序号」对照表导出来）：

```python
import json, os

frames = []
for order, fname in enumerate(sorted(os.listdir(FRAMES_DIR)), start=1):
    if not fname.lower().endswith(".png"):
        continue
    frames.append({
        "file": fname,                       # 相对清单所在目录
        "name": f"Misaka_Sk2_{order:02d}",   # ← 工具箱的命名规范
        "index": order,                      # ← 工具箱认定的动画帧序
    })

json.dump({"atlas": "Misaka_Sk2", "spritePrefix": "Misaka_Sk2",
           "mode": "pack", "trim": True, "extrude": 1, "padding": 2, "rotate": True,
           "frames": frames},
          open(os.path.join(FRAMES_DIR, "atlas_manifest.json"), "w", encoding="utf-8"),
          ensure_ascii=False, indent=2)
```

---

## 七、Unreal 侧消费 `_sequence.json`

`examples/unreal_flipbook_from_sequence.py` 是一份参考实现（编辑器 Python），
读 `_sequence.json` 按序号建 Paper2D Flipbook。**它是参考代码，没有在编辑器里跑过** ——
接进 ZD 工具箱的 UnrealBridge 时按你们既有脚本的约定调整（路径解析、日志、错误处理）。
