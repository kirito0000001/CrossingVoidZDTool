#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
ue_atlas.py —— UE 图集打包工具（TexturePacker 的开源替代，面向 Unreal Engine）

两种模式：
  grid  均匀网格图集 —— 给 Niagara SubUV / 材质 SubUV 用（序列帧特效主力，
        不需要任何数据文件，UE 里只要填 NumFramesX / NumFramesY）。
        所有帧的 sourceSize 都写成同一个 cell 尺寸，UE 里每帧锚点落在同一坐标系，播放不抖。
  pack  MaxRects 紧密装箱 —— 输出 .paper2dsprites，可直接拖进 UE 内容浏览器，
        自动生成 Texture + Frames(Sprites) + Sprite Sheet，右键即可 Create Flipbooks。
        默认裁掉未使用区域（--no-crop 可保留整幅画布）。

关于「播放抖不抖」：UE Paper2D 靠 sourceSize / spriteSourceSize 反算每帧的 pivot。
只要所有帧的 sourceSize 一致，锚点就一致，Flipbook 播放才稳。grid 模式由工具保证统一；
pack 模式取决于输入帧是否来自同一张画布（TexturePacker 也是这样），尺寸不一时工具会警告。

输出：
  <name>.png              图集贴图
  <name>.paper2dsprites   UE Paper2D 导入文件（拖进 Content Browser 即可）
  <name>.json             TexturePacker JSON(hash) 通用格式
  <name>_grid.json        grid 模式的网格信息（cols/rows/cell/帧数），给 Niagara 用

依赖：pip install pillow
"""

from __future__ import annotations

import argparse
import json
import math
import os
import re
import sys
from collections import Counter
from dataclasses import dataclass
from typing import Dict, List, Optional, Tuple

from PIL import Image

try:
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass

IMG_EXTS = {".png", ".jpg", ".jpeg", ".bmp", ".tga", ".webp"}


# ---------------------------------------------------------------- 基础工具

def natural_key(s: str):
    """自然排序：frame_2 < frame_10"""
    return [int(t) if t.isdigit() else t.lower() for t in re.split(r"(\d+)", s)]


def pot_ceil(n: int) -> int:
    n = int(n)
    return 1 << max(0, (n - 1).bit_length())


def is_image(path: str) -> bool:
    return os.path.splitext(path)[1].lower() in IMG_EXTS


def collect_images(src: str, recursive: bool) -> List[str]:
    if os.path.isfile(src):
        return [src] if is_image(src) else []
    out = []
    if recursive:
        for root, _dirs, files in os.walk(src):
            for f in files:
                p = os.path.join(root, f)
                if is_image(p):
                    out.append(p)
    else:
        for f in os.listdir(src):
            p = os.path.join(src, f)
            if os.path.isfile(p) and is_image(p):
                out.append(p)
    out.sort(key=lambda p: natural_key(os.path.basename(p)))
    return out


def make_name(path: str, base: str, keep_ext: bool) -> str:
    rel = os.path.relpath(path, base)
    if not keep_ext:
        rel = os.path.splitext(rel)[0]
    return re.sub(r"[/\\.:]", "_", rel)


def alpha_bbox(img: Image.Image, threshold: int):
    if img.mode != "RGBA":
        img = img.convert("RGBA")
    a = img.split()[-1].point(lambda p: 255 if p >= threshold else 0)
    return a.getbbox()


def extrude_img(img: Image.Image, e: int) -> Image.Image:
    """把边缘像素向外扩展 e 像素，防止 mip / 双线性采样吃到邻居。

    四条边里**底边原来写错了**：paste 的 y 坐标写成 `e + h + e`，而画布高度是 `h + 2e`，
    合法行号只到 `h + 2e - 1` —— 等于把底边扩边整个画到了画布外，静默什么都没做。
    结果是上/左/右有扩边、下没有，防漏色只做了一半。（自检原来只查了横向宽度，所以没抓到。）
    """
    if e <= 0:
        return img
    w, h = img.size
    r = Image.Resampling.NEAREST   # 纯复制，不要插值
    out = Image.new("RGBA", (w + 2 * e, h + 2 * e), (0, 0, 0, 0))
    out.paste(img, (e, e))
    out.paste(img.crop((0, 0, 1, h)).resize((e, h), r), (0, e))                # 左
    out.paste(img.crop((w - 1, 0, w, h)).resize((e, h), r), (w + e, e))        # 右
    out.paste(out.crop((0, e, w + 2 * e, e + 1)).resize((w + 2 * e, e), r), (0, 0))          # 上
    out.paste(out.crop((0, e + h - 1, w + 2 * e, e + h)).resize((w + 2 * e, e), r), (0, h + e))  # 下
    return out


def resolve_padding(padding: int, extrude: int) -> int:
    """算出真正可用的图元间距。

    extrude 是把边缘像素向外扩 e 像素，相邻两张图各扩 e，所以它们之间**真正需要 2e 的间隙**。
    原来写的是 `if extrude > padding: padding = extrude`，只把间距提到 e——
    于是间隙 = padding − 2e = −e，后画的图把前一张的扩边吃掉，防 mip 漏色直接失效。
    而 `--padding 2 --extrude 2` 恰好是文档和所有示例推荐的组合，所以这个坑很容易踩满。
    """
    return max(padding, 2 * extrude)


# ---------------------------------------------------------------- 数据

@dataclass
class Item:
    name: str
    img: Image.Image      # trim 之后的图（未 extrude / 未旋转）
    src_w: int
    src_h: int
    off_x: int
    off_y: int
    trimmed: bool
    rotated: bool = False

    # 动画帧序号。只有「走清单」时才由外部给定（ZD 工具箱那边名字和序号都有对照）；
    # 直接扫目录时留 0，表示「序号未知」。0 的话不产出 _sequence.json。
    index: int = 0

    # 「统一逻辑画布」。grid 模式下所有帧都记成同一个 cell 尺寸，帧在里面的位置记在 off_canvas_*。
    #
    # 为什么要这个：UE Paper2D 导入时拿 sourceSize / spriteSourceSize 反算每帧的 pivot。
    # 各帧 sourceSize 不一样，锚点就各在一处，Flipbook 播放起来会上下跳
    # （TexturePacker 的做法就是给所有帧写同一个 sourceSize，即原始画布尺寸）。
    # 0 表示「用帧自己的尺寸与偏移」，pack 模式走这条 —— 那边帧本来就来自统一画布。
    canvas_w: int = 0
    canvas_h: int = 0
    off_canvas_x: int = 0
    off_canvas_y: int = 0


def _load_one_image(path: str, name: str, index: int, trim: bool,
                    trim_threshold: int) -> Item:
    im = Image.open(path).convert("RGBA")
    sw, sh = im.size
    ox = oy = 0
    trimmed = False
    if trim:
        bb = alpha_bbox(im, trim_threshold)
        if bb and (bb[0] > 0 or bb[1] > 0 or bb[2] < sw or bb[3] < sh):
            im = im.crop(bb)
            ox, oy = bb[0], bb[1]
            trimmed = True
    return Item(name=name, img=im, src_w=sw, src_h=sh,
                off_x=ox, off_y=oy, trimmed=trimmed, index=index)


def build_items(files: List[str], base: str, trim: bool, trim_threshold: int,
                keep_ext: bool) -> List[Item]:
    """扫目录的路径：精灵名按源文件名推导，序号未知（0）。"""
    return [_load_one_image(p, make_name(p, base, keep_ext), 0, trim, trim_threshold)
            for p in files]


# ---------------------------------------------------------------- 输入清单（对接 ZD 工具箱的接口）

MANIFEST_JSON_KEYS = ("atlas", "spritePrefix", "mode", "trim", "padding", "extrude",
                      "rotate", "cols", "columns", "anchor", "maxSize", "max_size",
                      "pot", "crop", "name")


def load_manifest(path: str) -> dict:
    """读「一帧一条」的输入清单。两种写法等价：

    JSON（推荐，C# 侧好生成）：
        {
          "atlas": "Misaka_Sk2",
          "spritePrefix": "Misaka_Sk2",
          "frames": [
            {"file": "Misaka-Sk2-a1b2.png", "name": "Misaka_Sk2_01", "index": 1},
            ...
          ]
        }

    TSV / CSV（一行一帧，好手工维护、也好 diff）：
        # file<TAB>name<TAB>index       # 这一行是注释
        Misaka-Sk2-a1b2.png<TAB>Misaka_Sk2_01<TAB>1

    `file` 是相对**清单文件所在目录**的路径。`name` 与 `index` 都可省：
    名字省了用 `spritePrefix` + 补零序号生成；序号省了按数组顺序补。
    """
    ext = os.path.splitext(path)[1].lower()
    base = os.path.dirname(os.path.abspath(path))
    meta: Dict[str, object] = {}

    if ext == ".json":
        with open(path, "r", encoding="utf-8-sig") as f:
            data = json.load(f)
        if isinstance(data, list):
            data = {"frames": data}
        if not isinstance(data, dict) or "frames" not in data:
            raise ValueError("清单 JSON 的顶层要么是数组，要么是含 frames 数组的对象")
        raw = data["frames"]
        if not isinstance(raw, list):
            raise ValueError("清单 JSON 的 frames 必须是数组")
        for k in MANIFEST_JSON_KEYS:
            if k in data and data[k] is not None:
                meta[k] = data[k]
        frames = []
        for i, fr in enumerate(raw):
            if isinstance(fr, str):
                fr = {"file": fr}
            if not isinstance(fr, dict) or not fr.get("file"):
                raise ValueError(f"清单第 {i + 1} 帧缺少 file")
            frames.append({"file": fr["file"], "name": fr.get("name"),
                           "index": fr.get("index")})
    else:
        frames = []
        with open(path, "r", encoding="utf-8-sig") as f:
            for line_no, line in enumerate(f, 1):
                s = line.rstrip("\n").rstrip("\r")
                if not s.strip() or s.lstrip().startswith("#"):
                    continue
                if "\t" in s:
                    parts = s.split("\t")            # TSV：空字段有意义，保留
                elif "," in s:
                    parts = s.split(",")
                else:
                    parts = [p for p in s.split() if p]   # 空格分隔：空字段无意义
                parts = [p.strip() for p in parts]
                file = parts[0] if parts else ""
                if not file:
                    continue
                name = parts[1] if len(parts) > 1 and parts[1] else None
                idx = None
                if len(parts) > 2 and parts[2]:
                    try:
                        idx = int(parts[2])
                    except ValueError:
                        raise ValueError(f"清单第 {line_no} 行序号不是整数：{parts[2]!r}")
                frames.append({"file": file, "name": name, "index": idx})

    if not frames:
        raise ValueError(f"清单里没有任何帧：{path}")

    # 解析相对路径 + 检查文件存在
    for i, fr in enumerate(frames):
        p = fr["file"]
        if not os.path.isabs(p):
            p = os.path.normpath(os.path.join(base, p))
        if not os.path.isfile(p):
            raise ValueError(f"清单第 {i + 1} 帧的图片不存在：{p}")
        fr["file"] = p

    meta["frames"] = frames
    meta.setdefault("atlas", os.path.splitext(os.path.basename(path))[0])
    return meta


def normalize_manifest(manifest: dict):
    """把清单补全成 [(path, sprite_name, index), ...]（按序号升序）+ 警告列表。

    规则：
      - 序号全缺 → 按数组顺序 1..N；部分缺 → 从已有最大值往后补。
      - 名字缺 → `spritePrefix`（缺则用 atlas 名）+ 补零序号，补零宽度按最大序号来。
      - 名字重复直接报错 —— UE 里同名 sprite 会互相覆盖，这是静默灾难。
      - 序号重复只警告（允许，但很可能不是本意）。
    """
    frames = manifest["frames"]
    n = len(frames)
    warnings: List[str] = []

    idxs: List[Optional[int]] = [fr.get("index") for fr in frames]
    if all(i is None for i in idxs):
        idxs = list(range(1, n + 1))
    else:
        used = [i for i in idxs if i is not None]
        nxt = (max(used) + 1) if used else 1
        for k in range(n):
            if idxs[k] is None:
                idxs[k] = nxt
                nxt += 1

    dup_idx = [i for i, c in Counter(idxs).items() if c > 1]
    if dup_idx:
        warnings.append("序号重复：" + ", ".join(str(i) for i in sorted(dup_idx)[:8]))

    prefix = str(manifest.get("spritePrefix") or manifest.get("atlas") or "sprite")
    width = max(2, len(str(max(idxs))))

    names: List[str] = []
    for k, fr in enumerate(frames):
        nm = fr.get("name")
        names.append(str(nm) if nm else f"{prefix}_{idxs[k]:0{width}d}")

    dup_name = [x for x, c in Counter(names).items() if c > 1]
    if dup_name:
        raise ValueError("精灵名重复，UE 里会互相覆盖：" + ", ".join(dup_name[:8]))

    # 文件存在性在这里统一查一遍 —— 走 dict / 列表直接调 run_atlas() 的那条路
    # 不经过 load_manifest，少了这一步就会在更晚的地方抛一个难懂的异常。
    for k, fr in enumerate(frames):
        if not os.path.isfile(fr["file"]):
            raise ValueError(f"第 {k + 1} 帧的图片不存在：{fr['file']}")

    entries = [(frames[k]["file"], names[k], int(idxs[k])) for k in range(n)]
    entries.sort(key=lambda e: e[2])          # 按序号升序 —— grid 的格子顺序就靠它
    return entries, warnings


def read_manifest(path: str):
    m = load_manifest(path)
    entries, warnings = normalize_manifest(m)
    return m, entries, warnings


# ---------------------------------------------------------------- MaxRects

class MaxRects:
    """MaxRects 装箱，BSSF（Best Short Side Fit）启发式"""

    def __init__(self, w: int, h: int, padding: int, allow_rotate: bool):
        self.w, self.h = w, h
        self.pad = padding
        self.allow_rotate = allow_rotate
        self.free = [(0, 0, w, h)]

    def _score(self, rw: int, rh: int, fw: int, fh: int):
        if rw > fw or rh > fh:
            return None
        return (min(fw - rw, fh - rh), max(fw - rw, fh - rh))

    def place(self, rw: int, rh: int):
        ew, eh = rw + self.pad, rh + self.pad
        best = None
        for (fx, fy, fw, fh) in self.free:
            s1 = self._score(ew, eh, fw, fh)
            if s1 is not None and (best is None or s1 < best[0]):
                best = (s1, fx, fy, ew, eh, False)
            if self.allow_rotate:
                s2 = self._score(eh, ew, fw, fh)
                if s2 is not None and (best is None or s2 < best[0]):
                    best = (s2, fx, fy, eh, ew, True)
        if best is None:
            return None
        _s, x, y, pw, ph, rotated = best
        self._split((x, y, pw, ph))
        self._prune()
        return (x, y, rw, rh, rotated)   # 返回"核心"矩形（不含 padding）

    def _split(self, r):
        px, py, pw, ph = r
        new_free = []
        for i in range(len(self.free) - 1, -1, -1):
            fx, fy, fw, fh = self.free[i]
            if px < fx + fw and fx < px + pw and py < fy + fh and fy < py + ph:
                if py > fy:
                    new_free.append((fx, fy, fw, py - fy))
                if py + ph < fy + fh:
                    new_free.append((fx, py + ph, fw, fy + fh - (py + ph)))
                if px > fx:
                    new_free.append((fx, fy, px - fx, fh))
                if px + pw < fx + fw:
                    new_free.append((px + pw, fy, fx + fw - (px + pw), fh))
                self.free.pop(i)
        self.free.extend(new_free)

    def _prune(self):
        out = []
        for i, a in enumerate(self.free):
            contained = False
            for j, b in enumerate(self.free):
                if i != j and a[0] >= b[0] and a[1] >= b[1] and \
                   a[0] + a[2] <= b[0] + b[2] and a[1] + a[3] <= b[1] + b[3]:
                    contained = True
                    break
            if not contained and a[2] > 0 and a[3] > 0:
                out.append(a)
        self.free = out


def _placed_wh(r):
    """算出一个已放置图元的**实际占位**宽高。

    `MaxRects.place()` 返回的是 `(x, y, rw, rh, rotated)`，其中 rw/rh 是**未旋转**的尺寸；
    旋转之后真正占的地方要转过来。原来裁紧画布时一律按 rw/rh 算，旋转帧会被裁掉一截
    （这次实测没触发纯属运气：那批素材里决定右边界的是个非旋转帧）。
    """
    _x, _y, rw, rh, rot = r
    return (rh, rw) if rot else (rw, rh)


def _tight_size(placed, padding, extrude) -> Tuple[int, int]:
    """一批占位在画布上的紧凑包围盒。

    + extrude 是因为像素要往外扩一圈（扩边不能被画布边界吃掉）；
    + padding 是给双线性采样留的余量。跟原来 `--crop` 的口径一致，只是现在转了旋转帧。
    """
    right = bottom = 0
    for r in placed:
        x, y = r[0], r[1]
        pw, ph = _placed_wh(r)
        right = max(right, x + pw)
        bottom = max(bottom, y + ph)
    return right + extrude + padding, bottom + extrude + padding


# ---------------------------------------------------------------- 货架式装箱
#
# 为什么要有它：**MaxRects 在图元尺寸接近时表现反而差。**
# 实测同一批 17 帧 Misaka-Sk2（尺寸都在 150~260 × 260~360）：
#     MaxRects(BSSF)          1020×1218  密度 86.1%
#     货架(高度降序 + FirstFit) 1122×1018  密度 93.6%   ← 好 8%
#     TexturePacker            1132× 996  密度 94.8%
# 拆官方 DLL 也印证了这条：它里面有 `AlgorithmShelf`，和 `AlgorithmMaxRects` 并列可选。
# 所以现在两个都留着，由画布搜索一起试、按面积挑最优（相当于 TexturePacker 的 Best 模式）。

# 货架式的旋转姿态。放在这里是因为 Shelf.__init__ 的默认值校验要用到。
ROTATE_MODES = ("never", "auto", "all")


class Shelf:
    """货架（行）式装箱：按某种顺序排一遍，每张图塞进第一个放得下的货架。

    同一行共用一个行高（= 行内最高那张），所以越整齐的图集越省。

    **排序方式要当参数搜。** 实测同一批 17 帧、同一个宽度，只把次级排序从
    `-高度 稳定` 换成 `(-高度, -宽度)`，就从 3 行变 4 行、面积差 8%。
    所以这里不写死排序，而是让搜索把几种排序都试一遍
    （TexturePacker 也是这个路子：它有 "Sorts sprites by their area"，
    还有 "Trying %u different sets of parameters"）。
    """

    # 排序键必须是**确定性**的：破平一律用 name，不要依赖「谁先传进来」。
    # 否则同一个素材在两个工具（Python / Node）里会因为输入顺序不同而排出差 5% 的结果。
    KEYS = {
        "h":       lambda it: (-it.img.size[1], it.name),
        "hw":      lambda it: (-it.img.size[1], -it.img.size[0]),
        "w":       lambda it: (-it.img.size[0], -it.img.size[1]),
        "area":    lambda it: (-it.img.size[0] * it.img.size[1], -it.img.size[1]),
        "maxside": lambda it: (-max(it.img.size), -min(it.img.size)),
    }

    def __init__(self, w: int, h: int, padding: int, allow_rotate: bool,
                 key: str = "h", best_fit: bool = False, rotate_mode: str = "never"):
        self.bin_w, self.bin_h = w, h
        self.pad = padding
        self.allow_rotate = allow_rotate
        self.key = key if key in self.KEYS else "h"
        self.best_fit = best_fit
        self.rotate_mode = rotate_mode if rotate_mode in ROTATE_MODES else "never"
        self.rows = []          # 每行 = [y, 行高, 已用宽]

    def pack_all(self, items: List[Item]):
        """一次排完所有图元。放不下返回 None。返回 [(item, (x, y, rw, rh, rotated)), ...]"""

        # 旋转（rotate_mode）—— 货架式里旋转的**唯一价值**是压低行高：
        #   行高 = 行内最高那张的高度，整行都要按它留空间。
        #   把最高那张转成横放（高变矮、宽变宽）能整行变矮，而变宽只是横向多占一点。
        #   已经开好的行里行高已定，此时旋转只增宽不降高 —— 纯亏，所以行内不转。
        #
        # 模式：
        #   "auto"  —— 开新行时，若「横放」能让这一行更矮**且**横向仍放得下，就横放。
        #              注意这是**逐帧的局部判断**，不保证全局更优（实测这批素材横放反而更差，
        #              所以搜索会把它和 "never" 一起比较，最终按面积挑）。默认值。
        #   "never" —— 从不旋转。多数「清一色竖长条」的素材这是最优解。
        #   "all"   —— 一律横放。给「全都又高又窄」的素材用。
        rm = self.rotate_mode
        rows = []
        placed = []
        order = sorted(items, key=self.KEYS[self.key])

        for it in order:
            w, h = it.img.size
            upright = (w, h, False)
            sideways = (h, w, True) if (self.allow_rotate and w != h) else None

            # ---- 1) 先试塞进已有行。此时**不旋转**：行高已定，转了只多占宽度。----
            pick = None
            for row in rows:
                cw, ch, rot = upright
                if ch <= row[1] and row[2] + cw + self.pad <= self.bin_w:
                    slack = self.bin_w - row[2] - cw - self.pad
                    if self.best_fit and pick is not None and slack >= pick[0]:
                        continue
                    pick = (slack, row, cw, ch, rot)
                    if not self.best_fit:
                        break

            # ---- 2) 开新行。这里是旋转真正起作用的地方。----
            if pick is None:
                # 换行也要留 padding —— 少了它，下一行顶部的**扩边**会压进上一行的声明区，
                # 重建时就会多出几个像素（实测 17 帧里冒出 61 个不一致像素）。
                y = (rows[-1][0] + rows[-1][1] + self.pad) if rows else 0
                cands = [upright]
                if sideways is not None:
                    fits = sideways[0] + self.pad <= self.bin_w
                    if rm == "all" and fits:
                        cands = [sideways, upright]
                    elif rm == "auto" and fits and sideways[1] < upright[1]:
                        # 横放确实更矮 —— 压行高，值得一试
                        cands = [sideways, upright]
                for cw, ch, rot in cands:
                    if cw + self.pad <= self.bin_w and y + ch + self.pad <= self.bin_h:
                        row = [y, ch, 0]
                        rows.append(row)
                        pick = (0, row, cw, ch, rot)
                        break
            if pick is None:
                return None
            _slack, row, cw, ch, rot = pick
            x = row[2]
            # 返回的仍是**未旋转**的 rw/rh（和 MaxRects.place 同一约定）
            placed.append((it, (x, row[0], w, h, rot)))
            row[2] = x + cw + self.pad
        self.rows = rows
        return placed


def _try_canvas_maxrects(items, bin_w: int, bin_h: int, padding: int, allow_rotate: bool):
    p = MaxRects(bin_w, bin_h, padding, allow_rotate)
    placed = []
    for it in sorted(items, key=lambda x: -max(x.img.size)):
        r = p.place(*it.img.size)
        if r is None:
            return None
        placed.append((it, r))
    return placed


def _try_canvas(items, bin_w: int, bin_h: int, padding: int, allow_rotate: bool,
                packer: str = "maxrects"):
    """在一个固定箱体里装一遍，装得下返回 [(item, (x,y,rw,rh,rot))]，装不下返回 None。

    `packer` 形如 `"maxrects"` / `"shelf"` / `"shelf:w"`（冒号后是排序键）。
    """
    if packer.startswith("shelf"):
        parts = packer.split(":")
        key = parts[1] if len(parts) > 1 else "h"
        bf = "bf" in parts[2:]
        rm = [p for p in parts[2:] if p in ROTATE_MODES]
        return Shelf(bin_w, bin_h, padding, allow_rotate, key=key, best_fit=bf,
                     rotate_mode=(rm[0] if rm else "never")).pack_all(items)
    return _try_canvas_maxrects(items, bin_w, bin_h, padding, allow_rotate)


# 要试哪些「算法 + 排序 + 旋转姿态」组合（顺序无所谓，最终按分数挑最优）
SHELF_SORTS = ("h", "hw", "w", "area", "maxside")

# **不预设旋转一定有好处** —— 三种姿态全都排一遍，按最终面积挑。
# 实测：清一色竖长条素材（如 Misaka-Sk2）横放反而更差，就该老老实实选 "never"；
# 而「几张特别高的混在矮图里」时 "auto" 压行高才见效。所以交给搜索判断。
AUTO_PACKERS = (
    tuple("shelf:%s" % k for k in SHELF_SORTS)                       # never（等价于 :never）
    + tuple("shelf:%s:auto" % k for k in SHELF_SORTS)
    + tuple("shelf:%s:all" % k for k in SHELF_SORTS)
    + ("maxrects",)
)

PACKER_CHOICES = {
    "auto": AUTO_PACKERS,               # 默认：全试一遍，挑最优（相当于 TP 的 Best 模式）
    "shelf": tuple("shelf:%s" % k for k in SHELF_SORTS),
    "shelf-rot": (tuple("shelf:%s:auto" % k for k in SHELF_SORTS)
                  + tuple("shelf:%s:all" % k for k in SHELF_SORTS)),
    "maxrects": ("maxrects",),
}


def resolve_packers(v) -> tuple:
    if v is None:
        return PACKER_CHOICES["auto"]
    if isinstance(v, (list, tuple)):
        return tuple(v)
    key = str(v).lower()
    if key not in PACKER_CHOICES:
        raise ValueError(f"未知 packer：{v}（可选 {', '.join(PACKER_CHOICES)}）")
    return PACKER_CHOICES[key]


def _candidate_widths(n: int, side: int, maxw: int, maxh: int, max_size: int) -> List[int]:
    """生成候选箱体宽度。

    **基准宽度附近必须细扫。** 货架式装箱对宽度极其敏感 —— 行宽差几个像素就换一种断行方式，
    面积能差 5% 以上（实测同一批 17 帧：W=1122 排到 1122×1018，而 W=1117 或 W=1148 都明显更差）。
    所以：

    * 基准（√净面积）±30% 这一段**按 1 像素扫**（n 太大时降成 2~4 像素，免得装箱次数爆炸）；
    * 再往外（细长形状）用粗梯子，那些形状本来也不受几个像素影响；
    * 另外补上「最宽那张图」「最高那张图」本身 —— 宽度放不下最宽的图就直接无解。
    """
    lo = max(maxw, int(side * 0.70))
    hi = min(max_size, int(side * 1.40))
    if n <= 200:
        step = 1
    elif n <= 500:
        step = 2
    elif n <= 1200:
        step = 4
    else:
        step = max(4, (hi - lo) // 60)

    widths = set()
    if hi >= lo:
        widths |= set(range(lo, hi + 1, step))
    for f in (0.45, 0.55, 0.65, 0.70, 1.40, 1.55, 1.75, 2.0, 2.4):
        w = int(side * f)
        if w > hi or w < lo:
            widths.add(w)
    widths |= {maxw, maxh, maxw * 2}
    return sorted(w for w in widths if maxw <= w <= max_size)


def search_canvas(items: List[Item], padding: int, allow_rotate: bool,
                  max_size: int, extrude: int, aspect_power: float = 0.4,
                  packers=None):
    """搜一个「又方又满」的画布，同时在多种装箱算法里挑最优。

    两件事一起做：

    **1) 画布尺寸要搜，不能让装箱器自己挑。** 原来是把一个 2 的幂方块丢给 MaxRects 再 --crop，
    而 BSSF 在图元尺寸接近时会一路往下贴，最后裁出 603×2035 这种 1:3.4 的竖条
    （面积只多 17% 不算亏，但形状对 mip 极不友好，帧数再多几张就越过 2048 被迫分页）。
    固定宽度、给足高度、装完看紧凑包围盒，就能拿到方正得多而且面积不亏的结果。

    **2) 算法要换着试。** MaxRects 在图元尺寸接近时**反而差**：同一批 17 帧，
    MaxRects 密度 86.1%，朴素货架 93.6%，TexturePacker 94.8%。
    （拆官方 DLL 印证过：它有 `AlgorithmShelf`，和 `AlgorithmMaxRects` 并列，
    还有 `--pack-mode Fast/Good/Best` 控制搜多狠、"Trying %u different sets of parameters"。）
    所以这里把两个算法 × 一串候选宽度全试一遍，按分数挑最优 —— 相当于它的 Best 模式。

    打分 = 面积 × 长宽比^aspect_power。0.4 是实测值：再高会为了方正多花一成面积
    （同一批素材 1.18x/1:1.19 会被换成 1.26x/1:1.03），再低则开始容忍长条。
    """
    if not items:
        return None
    if packers is None:
        packers = AUTO_PACKERS
    area = sum(it.img.size[0] * it.img.size[1] for it in items) or 1
    side = max(1, math.isqrt(int(area)))
    maxw = max(it.img.size[0] for it in items)
    maxh = max(it.img.size[1] for it in items)

    widths = _candidate_widths(len(items), side, maxw, maxh, max_size)

    best = None
    for packer in packers:
        for w in widths:
            placed = _try_canvas(items, w, max_size, padding, allow_rotate, packer)
            if placed is None:
                continue
            tw, th = _tight_size([r for _it, r in placed], padding, extrude)
            if tw > max_size or th > max_size:
                continue
            ratio = max(tw, th) / float(min(tw, th))
            score = tw * th * (ratio ** aspect_power)
            if best is None or score < best[0]:
                best = (score, tw, th, placed, w, packer)

    if best is None:
        return None
    _score, tw, th, placed, w, packer = best
    return tw, th, placed, w, packer


def pack(items: List[Item], max_size: int, padding: int, allow_rotate: bool,
         min_size: int = 64, extrude: int = 0,
         packers=None) -> List[Tuple[int, int, List[Tuple[Item, tuple]], str]]:
    """返回 [(canvas_w, canvas_h, [(item, (x,y,w,h,rotated)), ...], 用的算法), ...]

    `canvas_w/h` 是**紧凑内容尺寸**（已含扩边与采样余量），不是箱体尺寸。
    先试「候选宽度 × 多算法搜索」吃单页；单页装不下才退回贪心分页。
    """
    single = search_canvas(items, padding, allow_rotate, max_size, extrude, packers=packers)
    if single is not None:
        tw, th, placed, _w, used = single
        return [(tw, th, list(placed), used)]

    # ---- 兜底：单页装不下（图元太多/太大），退回贪心分页 ----
    used_packer = "maxrects"
    ordered = sorted(items, key=lambda it: -max(it.img.size))
    pages = []
    remaining = list(ordered)

    area = sum(it.img.size[0] * it.img.size[1] for it in ordered) or 1
    start = max(min_size, min(max_size, pot_ceil(int(math.sqrt(area) * 1.25))))

    while remaining:
        w = h = start
        result = None
        while True:
            p = MaxRects(w, h, padding, allow_rotate)
            res = []
            ok = True
            for it in remaining:
                iw, ih = it.img.size
                r = p.place(iw, ih)
                if r is None:
                    ok = False
                    break
                res.append((it, r))
            if ok:
                result = res
                break
            if w <= h and w * 2 <= max_size:
                w *= 2
            elif h * 2 <= max_size:
                h *= 2
            elif w * 2 <= max_size:
                w *= 2
            else:
                break
        if result:
            tw, th = _tight_size([r for _it, r in result], padding, extrude)
            pages.append((tw, th, result, used_packer))
            remaining = []
            break

        p = MaxRects(max_size, max_size, padding, allow_rotate)
        page, rest = [], []
        for it in remaining:
            iw, ih = it.img.size
            r = p.place(iw, ih)
            if r is None:
                rest.append(it)
            else:
                page.append((it, r))
        if not page:
            bad = remaining[0]
            raise RuntimeError(
                f"单张图 {bad.name} {bad.img.size} 超过 max-size={max_size}，无法装箱")
        tw, th = _tight_size([r for _it, r in page], padding, extrude)
        pages.append((min(tw, max_size), min(th, max_size), page, used_packer))
        remaining = rest

    return pages


# ---------------------------------------------------------------- 渲染 & 输出

def paste(canvas: Image.Image, item: Item, x: int, y: int, extrude: int):
    im = item.img
    if item.rotated:
        im = im.transpose(Image.Transpose.ROTATE_270)   # 顺时针 90°
    if extrude > 0:
        im = extrude_img(im, extrude)
        x -= extrude
        y -= extrude
    canvas.alpha_composite(im, (x, y))


def frame_dict(item: Item, x: int, y: int, w: int, h: int) -> dict:
    if item.canvas_w > 0 and item.canvas_h > 0:
        # 统一逻辑画布：sourceSize 写成画布尺寸（所有帧一致），spriteSourceSize 写成帧在画布里的位置。
        # UE 靠这两个字段算出「这帧的锚点落在画布坐标系的哪里」，因此每帧锚点一致 -> 播放不抖。
        return {
            "frame": {"x": x, "y": y, "w": w, "h": h},
            "rotated": bool(item.rotated),
            "trimmed": bool(item.off_canvas_x or item.off_canvas_y
                            or w != item.canvas_w or h != item.canvas_h),
            "spriteSourceSize": {"x": item.off_canvas_x, "y": item.off_canvas_y, "w": w, "h": h},
            "sourceSize": {"w": item.canvas_w, "h": item.canvas_h},
            "pivot": {"x": 0.5, "y": 0.5},
        }

    return {
        "frame": {"x": x, "y": y, "w": w, "h": h},
        "rotated": bool(item.rotated),
        "trimmed": bool(item.trimmed),
        "spriteSourceSize": {"x": item.off_x, "y": item.off_y,
                             "w": item.img.size[0], "h": item.img.size[1]},
        "sourceSize": {"w": item.src_w, "h": item.src_h},
        "pivot": {"x": 0.5, "y": 0.5},
    }


def write_outputs(outdir: str, name: str, canvas: Image.Image,
                  entries: List[Tuple[Item, int, int, int, int]],
                  grid_info: Optional[dict]):
    os.makedirs(outdir, exist_ok=True)
    png_path = os.path.join(outdir, name + ".png")
    canvas.save(png_path)

    frames_arr, frames_hash = [], {}
    for it, x, y, w, h in entries:
        d = frame_dict(it, x, y, w, h)
        frames_arr.append({"filename": it.name, **d})
        frames_hash[it.name] = d

    meta = {
        "app": "ue_atlas.py",
        "version": "1.0",
        "image": name + ".png",
        "format": "RGBA8888",
        "size": {"w": canvas.size[0], "h": canvas.size[1]},
        "scale": 1,
        "target": "paper2d",
    }

    with open(os.path.join(outdir, name + ".paper2dsprites"), "w", encoding="utf-8") as f:
        json.dump({"frames": frames_arr, "meta": meta}, f, indent=2, ensure_ascii=False)

    with open(os.path.join(outdir, name + ".json"), "w", encoding="utf-8") as f:
        json.dump({"frames": frames_hash, "meta": meta}, f, indent=2, ensure_ascii=False)

    if grid_info:
        with open(os.path.join(outdir, name + "_grid.json"), "w", encoding="utf-8") as f:
            json.dump(grid_info, f, indent=2, ensure_ascii=False)

    # _sequence.json：按「序号」排好的帧表，是动画帧序的**唯一权威**。
    #
    # 为什么需要它：UE 从 sprite sheet 建 Flipbook 是按 **sprite 名排序**决定帧序的，
    # 名字一旦不是有序的（比如源文件是哈希名），顺序就丢了。走清单时序号是外部给的权威值，
    # 这里把它连同每帧在图集里的矩形一起固化下来，Unreal 侧脚本可以照它精确建 Flipbook，
    # 不用去猜名字排序。只有序号已知（走清单）时才产出。
    seq_path = None
    if any(it.index for it, _x, _y, _w, _h in entries):
        seq = []
        for it, x, y, w, h in entries:
            d = frame_dict(it, x, y, w, h)
            seq.append({
                "index": it.index,
                "name": it.name,
                "frame": d["frame"],
                "rotated": d["rotated"],
                "trimmed": d["trimmed"],
                "spriteSourceSize": d["spriteSourceSize"],
                "sourceSize": d["sourceSize"],
            })
        seq.sort(key=lambda e: (e["index"], e["name"]))
        doc = {
            "atlas": name,
            "image": name + ".png",
            "sprites": name + ".paper2dsprites",
            "texture": {"w": canvas.size[0], "h": canvas.size[1]},
            "frameCount": len(seq),
            "order": "frames 已按 index 升序排列，这个顺序就是动画帧序（第 0 帧在前）",
            "frames": seq,
        }
        seq_path = os.path.join(outdir, name + "_sequence.json")
        with open(seq_path, "w", encoding="utf-8") as f:
            json.dump(doc, f, indent=2, ensure_ascii=False)

    return png_path, seq_path


def run_grid(items: List[Item], outdir: str, name: str, cols: Optional[int],
             padding: int, extrude: int, pot: bool, anchor: str) -> dict:
    padding = resolve_padding(padding, extrude)
    cw = max(it.img.size[0] for it in items)
    ch = max(it.img.size[1] for it in items)
    n = len(items)
    cs = cols if (cols and cols > 0) else max(1, int(math.ceil(math.sqrt(n))))
    rs = int(math.ceil(n / cs))

    pw, ph = cw + padding, ch + padding
    W, H = cs * pw + padding, rs * ph + padding
    if pot:
        W, H = pot_ceil(W), pot_ceil(H)

    canvas = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    entries = []
    for i, it in enumerate(items):
        c, r = i % cs, i // cs
        iw, ih = it.img.size
        if anchor == "topleft":
            dx, dy = 0, 0
        elif anchor == "bottomleft":
            dx, dy = 0, ch - ih
        else:
            dx, dy = (cw - iw) // 2, (ch - ih) // 2
        x = padding + c * pw + dx
        y = padding + r * ph + dy
        paste(canvas, it, x, y, extrude)
        # 统一逻辑画布 = cell 尺寸。帧尺寸不一时，对齐之后的偏移记进 spriteSourceSize，
        # 于是所有帧的 sourceSize 都是 (cw, ch)、锚点都落在同一个坐标系里，UE 播放不抖。
        it.canvas_w, it.canvas_h = cw, ch
        it.off_canvas_x, it.off_canvas_y = dx, dy
        entries.append((it, x, y, iw, ih))

    info = {
        "image": name + ".png",
        "size": {"w": W, "h": H},
        "cell": {"w": cw, "h": ch},
        "pitch": {"w": pw, "h": ph},
        "padding": padding,
        "cols": cs, "rows": rs,
        "frameCount": n,
        "niagara": {"NumFramesX": cs, "NumFramesY": rs},
        "order": "row-major, left->right, top->bottom",
    }
    # items 已经按序号升序（走清单时），所以格子顺序天然就是动画帧序
    png, seq = write_outputs(outdir, name, canvas, entries, info)
    print(f"  [grid] {png}  {W}x{H}  cell {cw}x{ch}  {cs}x{rs}  frames={n}")
    return {"mode": "grid", "image": png, "sequence": seq,
            "size": {"w": W, "h": H}, "cell": {"w": cw, "h": ch},
            "grid": {"cols": cs, "rows": rs}, "frameCount": n}


def run_pack(items: List[Item], outdir: str, name: str, max_size: int,
             padding: int, extrude: int, allow_rotate: bool, pot: bool, crop: bool,
             packers=None) -> dict:
    padding = resolve_padding(padding, extrude)
    pages = pack(items, max_size, padding, allow_rotate, extrude=extrude, packers=packers)
    multi = len(pages) > 1
    # 整体往外挪 extrude 那么多：不然最靠上/左的那几张，扩边会被画布边界吃掉。
    # （像素真正落在 frame 矩形再往外一圈，所以扩边本身不会进 UE 的取图范围，
    #   它的作用就是给双线性采样当垫片。）
    margin = extrude
    out = {"mode": "pack", "sheets": [], "sequence": None, "frameCount": len(items)}
    for pi, (W, H, res, used) in enumerate(pages):
        # pack() 现在直接给紧凑内容尺寸，不再需要在外面重算 bbox
        cw, ch = max(1, W), max(1, H)
        if pot or not crop:
            cw, ch = pot_ceil(cw), pot_ceil(ch)

        canvas = Image.new("RGBA", (cw, ch), (0, 0, 0, 0))
        entries = []
        used_area = 0
        for it, (x, y, w, h, rot) in res:
            it.rotated = rot
            paste(canvas, it, x + margin, y + margin, extrude)
            entries.append((it, x + margin, y + margin, w, h))
            used_area += (w + padding) * (h + padding)
        pname = f"{name}_{pi}" if multi else name
        png, seq = write_outputs(outdir, pname, canvas, entries, None)
        eff = used_area / float(cw * ch) * 100
        print(f"  [pack] {png}  {cw}x{ch}  sprites={len(res)}  密度 {eff:.1f}%  ({used})")
        out["sheets"].append({"name": pname, "image": png, "sequence": seq, "packer": used,
                              "size": {"w": cw, "h": ch}, "sprites": len(res),
                              "density": round(eff, 2)})
        if seq and out["sequence"] is None:
            out["sequence"] = seq
    if len(out["sheets"]) == 1:
        out["image"] = out["sheets"][0]["image"]
        out["size"] = out["sheets"][0]["size"]
        out["packer"] = out["sheets"][0]["packer"]
        out["density"] = out["sheets"][0]["density"]
    return out


# ---------------------------------------------------------------- CLI

def warn_mixed_source_sizes(name: str, items: List[Item]):
    """pack 模式下各帧原始尺寸不一样时给出警告。

    UE Paper2D 拿 sourceSize / spriteSourceSize 反算每帧锚点，各帧 sourceSize 不同，
    锚点就各在一处，Flipbook 播放时会上下跳。这一点工具补不回来：只有当输入是
    「同一张画布上、带完整透明边」的帧时 sourceSize 才会一致（TexturePacker 就是这么工作的）。
    帧要是已经被裁过，它在原画布中的位置信息就已经丢了。
    """
    sizes = sorted({(it.src_w, it.src_h) for it in items})
    if len(sizes) < 2:
        return
    shown = ", ".join(f"{w}x{h}" for w, h in sizes[:4])
    if len(sizes) > 4:
        shown += f" …共 {len(sizes)} 种"
    print(f"  ! {name}: 源帧尺寸不统一（{shown}）")
    print( "     -> UE 里每帧锚点会各在一处，Flipbook 播放会跳。")
    print( "     -> 改用 --mode grid（格子统一，锚点自然一致），")
    print( "        或拿未裁剪的整幅画布帧重新导出（那样 sourceSize 才一致）。")


def run_on_items(items: List[Item], outdir: str, name: str, args) -> dict:
    """核心调度：拿已经建好的 Item 列表跑一次，返回机器可读的结果。"""
    if args.mode == "grid":
        return run_grid(items, outdir, name, args.cols, args.padding, args.extrude,
                        args.pot, args.anchor)
    warn_mixed_source_sizes(name, items)
    return run_pack(items, outdir, name, args.max_size, args.padding, args.extrude,
                    args.rotate, args.pot, args.crop,
                    packers=resolve_packers(getattr(args, "packer", None)))


def process_one(files: List[str], base: str, outdir: str, name: str, args):
    items = build_items(files, base, args.trim, args.trim_threshold, args.keep_ext)
    if not items:
        print(f"  ! {name}: 没有找到图片，跳过")
        return None
    return run_on_items(items, outdir, name, args)


# ---------------------------------------------------------------- 给外部程序用的入口
#
# ZD 工具箱那边名字和序号本来就有对照表，所以这里把「一帧一条」当成正式接口：
#   每帧 = (图片路径, 精灵名, 序号)
# 精灵名直接写进图集（不再从文件名反推），序号决定动画帧序并固化成 _sequence.json。
#
# 两种用法等价：
#   1) 命令行 + 清单文件（工具箱生成清单、起进程，最省事，和现有 export_zd_assets.py 一个套路）
#        python ue_atlas.py --manifest atlas.json -o ./out
#        python ue_atlas.py --manifest atlas.tsv  -o ./out --mode grid --report result.json
#   2) 当库直接调（以后想内嵌到 Python 侧流程时）
#        from ue_atlas import run_atlas
#        run_atlas([("a.png", "Misaka_Sk2_01", 1), ("b.png", "Misaka_Sk2_02", 2)], "./out")

# 清单里能覆盖的参数（键名两种写法都认：maxSize / max_size）
_MANIFEST_OPT_KEYS = {
    "mode": "mode", "padding": "padding", "extrude": "extrude",
    "rotate": "rotate", "trim": "trim", "cols": "cols", "columns": "cols",
    "anchor": "anchor", "maxSize": "max_size", "maxsize": "max_size",
    "max_size": "max_size", "pot": "pot", "crop": "crop", "packer": "packer",
}

DEFAULT_OPTIONS = dict(mode="pack", cols=0, anchor="center", padding=2, extrude=0,
                       trim=True, trim_threshold=1, rotate=False, max_size=2048,
                       pot=False, crop=True, keep_ext=False, packer="auto")


def _options(**overrides) -> argparse.Namespace:
    ns = argparse.Namespace(**DEFAULT_OPTIONS)
    for k, v in overrides.items():
        if k in (None, "name", "out", "input", "manifest", "report", "each_subdir", "recursive"):
            continue
        setattr(ns, k, v)
    return ns


def _as_manifest_dict(manifest) -> Tuple[dict, List[str]]:
    if isinstance(manifest, (str, os.PathLike)):
        m, entries, warnings = read_manifest(str(manifest))
        return {"_entries": entries, "_warnings": warnings, **m}, warnings

    if isinstance(manifest, dict):
        man = dict(manifest)
        man.setdefault("atlas", "atlas")
        entries, warnings = normalize_manifest(man)
        return {"_entries": entries, "_warnings": warnings, **man}, warnings

    if isinstance(manifest, (list, tuple)):
        frames = []
        for item in manifest:
            if isinstance(item, dict):
                frames.append({"file": item.get("file"), "name": item.get("name"),
                               "index": item.get("index")})
            elif isinstance(item, (list, tuple)):
                p = item[0] if len(item) > 0 else None
                nm = item[1] if len(item) > 1 else None
                ix = item[2] if len(item) > 2 else None
                frames.append({"file": p, "name": nm, "index": ix})
            else:
                frames.append({"file": item, "name": None, "index": None})
        man = {"atlas": "atlas", "frames": frames}
        entries, warnings = normalize_manifest(man)
        return {"_entries": entries, "_warnings": warnings, **man}, warnings

    raise TypeError("manifest 需要是清单文件路径 / 清单 dict / [(图片, 名字, 序号), ...]")


def run_atlas(manifest, outdir: str, name: Optional[str] = None, **overrides) -> dict:
    """给外部程序用的函数式入口（ZD 工具箱对接点）。

    参数：
      manifest  清单文件路径（.json/.tsv/.csv/.txt），或清单 dict，或 [(图片, 名字, 序号), ...]
      outdir    输出目录
      name      图集文件名（不含扩展名）；不给就用清单里的 atlas
      overrides 覆盖 mode / padding / extrude / rotate / trim / cols / anchor /
                max_size / pot / crop —— 优先级高于清单里的同名项

    返回：
      {
        "ok": true,
        "atlas": "Misaka_Sk2", "outdir": "...",
        "mode": "pack", "image": "...png", "sequence": "..._sequence.json",
        "size": {"w":..,"h":..}, "frameCount": 17,
        "pages": [{"image":"..","size":{"w":..,"h":..},"sequence":"..","sprites":17}],
        "frames": [{"index":1,"name":"Misaka_Sk2_01"}, ...],   # 已按序号升序
        "warnings": ["..."]
      }
    """
    man, warnings = _as_manifest_dict(manifest)
    entries = man.pop("_entries")
    man.pop("_warnings", None)

    args = _options(**overrides)
    # 清单里写的默认值生效（overrides 优先）
    for mk, ak in _MANIFEST_OPT_KEYS.items():
        if mk in man and man[mk] is not None and ak not in overrides:
            setattr(args, ak, man[mk])

    atlas_name = name or overrides.get("name") or man.get("atlas") or "atlas"
    if "spritePrefix" not in man:
        man["spritePrefix"] = atlas_name
    outdir = os.path.abspath(outdir)

    items = [_load_one_image(p, nm, ix, args.trim, args.trim_threshold)
             for p, nm, ix in entries]
    if not items:
        raise ValueError("清单里没有任何帧")

    result = run_on_items(items, outdir, atlas_name, args)
    result.update({
        "ok": True,
        "atlas": atlas_name,
        "outdir": outdir,
        "warnings": warnings,
        "frameCount": len(items),
        "frames": [{"index": it.index, "name": it.name}
                   for it in sorted(items, key=lambda x: x.index)],
    })
    if "sheets" in result and "pages" not in result:
        result["pages"] = result.pop("sheets")
    return result


# ---------------------------------------------------------------- CLI


def main(argv=None):
    ap = argparse.ArgumentParser(
        description="UE 图集打包工具（TexturePacker 开源替代）",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""示例:
  # 序列帧特效 -> Niagara SubUV 均匀网格
  python ue_atlas.py ./frames -o ./out --mode grid --cols 4 --padding 2 --name fx_hit

  # 紧密装箱 -> 生成 .paper2dsprites，直接拖进 UE
  python ue_atlas.py ./sprites -o ./out --mode pack --max-size 2048 --trim --name hero_idle

  # 每个子目录单独打一张图集（每个特效/每个动作一张）
  python ue_atlas.py ./vfx -o ./out --mode grid --each-subdir

  # 走清单（对接 ZD 工具箱）：名字与序号由清单给定
  python ue_atlas.py --manifest atlas.json -o ./out --mode pack --trim --report result.json
""")
    ap.add_argument("input", nargs="?", help="图片目录（或单张图片）。走 --manifest 时可省")
    ap.add_argument("--manifest", help="输入清单（.json / .tsv / .csv）：一帧一条，"
                                       "给「图片、精灵名、序号」。详见 MANIFEST.md")
    ap.add_argument("--report", help="把机器可读的结果 JSON 写到这个路径（给调用方解析用）")
    ap.add_argument("-o", "--out", default="./out", help="输出目录，默认 ./out")
    ap.add_argument("--mode", choices=["grid", "pack"], default=None,
                    help="grid=均匀网格(Niagara SubUV)；pack=MaxRects 紧密装箱(Paper2D)")
    ap.add_argument("--name", default=None, help="输出文件名（不含扩展名）")
    ap.add_argument("--each-subdir", action="store_true",
                    help="把 input 的每个子目录分别打成一张图集，各自以目录名命名")
    ap.add_argument("--recursive", action="store_true", help="递归扫描子目录中的图片")
    ap.add_argument("--cols", type=int, default=0,
                    help="grid 模式列数，0=自动(接近正方形)，行数自动推算")
    ap.add_argument("--anchor", choices=["center", "topleft", "bottomleft"],
                    default="center", help="grid 模式每帧在格子内的对齐方式")
    ap.add_argument("--padding", type=int, default=2, help="图元间距，默认 2")
    ap.add_argument("--extrude", type=int, default=0,
                    help="边缘像素外扩，防 mip 漏色；开了会把间距自动提到 2 倍 extrude")
    ap.add_argument("--trim", action="store_true", help="裁剪透明边（pack 模式常用）")
    ap.add_argument("--trim-threshold", type=int, default=1,
                    help="trim 的 alpha 阈值 0-255，默认 1")
    ap.add_argument("--rotate", action="store_true",
                    help="pack 模式允许旋转 90°（更省空间，但部分流程不友好）")
    ap.add_argument("--max-size", type=int, default=2048, help="pack 模式最大边长，默认 2048")
    ap.add_argument("--pot", action="store_true",
                    help="画布尺寸向上取整到 2 的幂（pack 模式下作用于裁切之后）")
    ap.add_argument("--crop", action="store_true", default=True,
                    help="pack 模式裁掉未使用区域（默认开）")
    ap.add_argument("--no-crop", dest="crop", action="store_false",
                    help="pack 模式保留完整画布，不裁未使用区域")
    ap.add_argument("--packer", choices=["auto", "shelf", "maxrects"], default=None,
                    help="pack 模式的装箱算法：auto=两个都试挑最优(默认)；"
                         "shelf=货架式(图元尺寸接近时更省)；maxrects=紧密装箱")
    ap.add_argument("--keep-ext", action="store_true", help="sprite 名保留文件扩展名")
    args = ap.parse_args(argv)

    outdir = os.path.abspath(args.out)

    # ---------------- 清单模式：名字与序号由清单给定 ----------------
    if args.manifest:
        try:
            m, entries, warnings = read_manifest(args.manifest)
        except (ValueError, OSError, json.JSONDecodeError) as e:
            ap.error(f"清单读取失败：{e}")
        # 命令行显式给的参数优先于清单里的默认值
        cli_over = {}
        if args.mode is not None:
            cli_over["mode"] = args.mode
        for k in ("padding", "extrude", "trim", "rotate", "cols", "anchor",
                  "max_size", "pot", "crop", "packer"):
            src_v = getattr(args, k)
            if src_v is not None and src_v != ap.get_default(k):
                cli_over[k] = src_v
        atlas_name = args.name or m.get("atlas") or "atlas"
        for w in warnings:
            print(f"  ! {w}")
        print(f"清单：{len(entries)} 帧 -> 图集 {atlas_name}")
        try:
            result = run_atlas(m, outdir, name=atlas_name, **cli_over)
        except (ValueError, OSError) as e:
            ap.error(f"打包失败：{e}")
        if args.report:
            with open(args.report, "w", encoding="utf-8") as f:
                json.dump(result, f, indent=2, ensure_ascii=False)
            print(f"结果 JSON -> {args.report}")
        print(f"\n完成 -> {outdir}")
        return 0

    # ---------------- 扫目录模式（原有行为） ----------------
    if not args.input:
        ap.error("要么给 input（图片目录），要么给 --manifest")
    if args.mode is None:
        args.mode = "grid"
    src = os.path.abspath(args.input)
    if not os.path.exists(src):
        ap.error(f"输入路径不存在: {src}")

    results = []
    if args.each_subdir:
        subs = sorted([d for d in os.listdir(src) if os.path.isdir(os.path.join(src, d))],
                      key=natural_key)
        if not subs:
            ap.error("--each-subdir 但输入目录下没有子目录")
        print(f"共 {len(subs)} 个子目录")
        for d in subs:
            files = collect_images(os.path.join(src, d), True)
            r = process_one(files, os.path.join(src, d), outdir, d, args)
            if r:
                results.append(r)
    else:
        files = collect_images(src, args.recursive)
        if not files:
            ap.error(f"没有找到图片: {src}")
        r = process_one(files, src, outdir, args.name or "atlas", args)
        if r:
            results.append(r)

    if args.report:
        report = {"ok": True, "outdir": outdir, "atlasses": results}
        with open(args.report, "w", encoding="utf-8") as f:
            json.dump(report, f, indent=2, ensure_ascii=False)
        print(f"结果 JSON -> {args.report}")

    print(f"\n完成 -> {outdir}")
    print("UE 导入：把 .paper2dsprites 拖进 Content Browser（需启用 Paper2D 插件）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
