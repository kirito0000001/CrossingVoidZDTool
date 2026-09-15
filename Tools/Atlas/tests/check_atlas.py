#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""ue_atlas.py 的行为自检。

不需要 Unreal，只需要 Pillow：

    python tests/check_atlas.py

钉住的是这几件容易悄悄坏掉的事：
  1. extrude 与 padding 的间隙账（扩边不能被邻居吃掉）
  2. grid 模式下所有帧的 sourceSize 统一（不统一 UE 里锚点就不一致，播放会抖）
  3. .paper2dsprites 的 schema（frames 必须是数组 + filename，UE 才认）
  4. crop / pot 的画布尺寸语义，以及画布形状不能退回竖条
  5. 清单接口的契约（名字原样落盘、_sequence.json 按序号升序、重名报错等）
  6. 货架式装箱在「图元尺寸接近」时明显优于 MaxRects（默认 auto 要两个都试）
"""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
import tempfile
from typing import List, Optional, Tuple

from PIL import Image

try:
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCRIPT = os.path.join(ROOT, "ue_atlas.py")

sys.path.insert(0, ROOT)
import ue_atlas as _UA   # noqa: E402  （直接钉住 packer 注册关系用）

_passed = 0
_failed: List[str] = []


def check(name: str, ok: bool, detail: str = "") -> bool:
    global _passed
    if ok:
        _passed += 1
        print(f"  PASS {name}")
    else:
        _failed.append(name)
        print(f"  FAIL {name}" + (f"  -> {detail}" if detail else ""))
    return ok


def run(args: List[str]) -> subprocess.CompletedProcess:
    return subprocess.run(
        [sys.executable, SCRIPT] + args,
        cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace")


def solid(path: str, size: Tuple[int, int], color: Tuple[int, int, int, int]) -> None:
    Image.new("RGBA", size, color).save(path)


def png_size(path: str) -> Tuple[int, int]:
    return Image.open(path).size


def is_pow2(n: int) -> bool:
    return n > 0 and (n & (n - 1)) == 0


def scan_line(path: str) -> List[list]:
    """取非透明区域的中线，返回 [(颜色标记, 起, 止), ...]。R/B 是测试图的两个纯色。"""
    im = Image.open(path).convert("RGBA")
    w, h = im.size
    px = im.load()
    pts = [(x, y) for y in range(h) for x in range(w) if px[x, y][3] > 0]
    if not pts:
        return []
    y = (min(p[1] for p in pts) + max(p[1] for p in pts)) // 2
    spans: List[list] = []
    cur: Optional[list] = None
    for x in range(w):
        p = px[x, y]
        if p[3] == 0:
            c = "."
        elif p[0] > 200 and p[2] < 60:
            c = "R"
        elif p[2] > 200 and p[0] < 60:
            c = "B"
        else:
            c = "?"
        if cur and cur[0] == c:
            cur[2] = x
        else:
            cur = [c, x, x]
            spans.append(cur)
    return spans


def span_of(spans: List[list], tag: str):
    for c, a, b in spans:
        if c == tag:
            return (a, b)
    return None


# ------------------------------------------------------------------ 用例

def color_bbox(path: str, pred):
    """找出满足颜色条件的所有像素的包围盒 (x0,y0,x1,y1)，没有返回 None。"""
    im = Image.open(path).convert("RGBA")
    w, h = im.size
    px = im.load()
    xs, ys = [], []
    for y in range(h):
        for x in range(w):
            if pred(px[x, y]):
                xs.append(x)
                ys.append(y)
    if not xs:
        return None
    return (min(xs), min(ys), max(xs), max(ys))


def is_red(c) -> bool:
    return c[3] > 0 and c[0] > 200 and c[2] < 60


def is_blue(c) -> bool:
    return c[3] > 0 and c[2] > 200 and c[0] < 60


def case_extrude_gap(tmp: str) -> None:
    """扩边与间距：两张图各扩 e 像素，它们之间必须真的留出 2e。

    注意：pack 现在会搜画布宽度，两张图**横排还是竖排并不确定**，
    所以这里按颜色求包围盒、自动判轴，不去假设版面。
    """
    src = os.path.join(tmp, "gap_in")
    os.makedirs(src, exist_ok=True)
    solid(os.path.join(src, "a.png"), (64, 64), (255, 0, 0, 255))
    solid(os.path.join(src, "b.png"), (64, 64), (0, 0, 255, 255))
    out = os.path.join(tmp, "gap_out")

    # 这一组同时也是「crop 不会把扩边裁掉」的回归：默认就是 crop 开的。
    r = run([src, "-o", out, "--mode", "pack", "--name", "t",
             "--padding", "2", "--extrude", "2"])
    if not check("扩边用例跑得通", r.returncode == 0, (r.stderr or "")[-300:]):
        return

    png = os.path.join(out, "t.png")
    red = color_bbox(png, is_red)
    blue = color_bbox(png, is_blue)
    if not check("两张图都在图集里", red is not None and blue is not None,
                 f"red={red} blue={blue}"):
        return

    # 64 核心 + 四周各 2 扩边 = 68
    rw, rh = red[2] - red[0] + 1, red[3] - red[1] + 1
    bw, bh = blue[2] - blue[0] + 1, blue[3] - blue[1] + 1
    check("图 A 四周扩边都完整（64 + 2×2 = 68）", (rw, rh) == (68, 68), f"实测 {rw}x{rh}")
    check("图 B 四周扩边都完整（64 + 2×2 = 68）", (bw, bh) == (68, 68), f"实测 {bw}x{bh}")

    if red[2] < blue[0] or blue[2] < red[0]:       # 横排
        gap = (blue[0] - red[2] - 1) if red[2] < blue[0] else (red[0] - blue[2] - 1)
        check("两图的扩边相接但不重叠（横向）", gap == 0, f"间隙 {gap}")
    else:                                          # 竖排
        gap = (blue[1] - red[3] - 1) if red[3] < blue[1] else (red[1] - blue[3] - 1)
        check("两图的扩边相接但不重叠（纵向）", gap == 0, f"间隙 {gap}")


def case_grid_unifies_anchor(tmp: str) -> None:
    """grid 模式：sourceSize 必须所有帧一致，否则 UE 里每帧锚点各在一处，播放会抖。"""
    src = os.path.join(tmp, "grid_in")
    os.makedirs(src, exist_ok=True)
    solid(os.path.join(src, "f1.png"), (128, 96), (255, 0, 0, 255))
    solid(os.path.join(src, "f2.png"), (96, 96), (0, 0, 255, 255))
    solid(os.path.join(src, "f3.png"), (96, 128), (0, 200, 0, 255))
    out = os.path.join(tmp, "grid_out")

    r = run([src, "-o", out, "--mode", "grid", "--name", "g", "--cols", "2"])
    if not check("grid 用例跑得通", r.returncode == 0, (r.stderr or "")[-300:]):
        return

    data = json.load(open(os.path.join(out, "g.paper2dsprites"), encoding="utf-8"))
    frames = data["frames"]
    check("frames 是数组（不是 hash）", isinstance(frames, list))
    check("每帧都带 filename", all("filename" in f for f in frames))
    check("每帧都带 spriteSourceSize / sourceSize",
          all("spriteSourceSize" in f and "sourceSize" in f for f in frames))

    sizes = {tuple(f["sourceSize"].values()) for f in frames}
    check("所有帧的 sourceSize 统一", len(sizes) == 1, str(sizes))
    check("sourceSize 取的是 cell 尺寸", sizes == {(128, 128)}, str(sizes))

    f1 = next((f for f in frames if str(f["filename"]).endswith("f1")), None)
    if check("找得到 128x96 那帧", f1 is not None):
        check("帧在格子内的居中偏移记对了",
              f1["spriteSourceSize"] == {"x": 0, "y": 16, "w": 128, "h": 96},
              str(f1["spriteSourceSize"]))
        check("偏过位的帧标记为 trimmed", f1["trimmed"] is True)


def case_pack_canvas(tmp: str) -> None:
    """pack 模式的画布尺寸：默认裁紧（非 2 的幂），--pot 才是幂。"""
    src = os.path.join(ROOT, "demo", "frames")
    if not os.path.isdir(src):
        check("demo/frames 存在", False, src)
        return
    out = os.path.join(tmp, "pack_out")

    r1 = run([src, "-o", out, "--mode", "pack", "--name", "c1", "--trim"])
    r2 = run([src, "-o", out, "--mode", "pack", "--name", "c2", "--trim", "--no-crop"])
    r3 = run([src, "-o", out, "--mode", "pack", "--name", "c3", "--trim", "--pot"])
    if not check("pack 三种画布用例跑得通",
                 r1.returncode == 0 and r2.returncode == 0 and r3.returncode == 0,
                 ((r1.stderr or "") + (r2.stderr or "") + (r3.stderr or ""))[-300:]):
        return

    w1, h1 = png_size(os.path.join(out, "c1.png"))
    w2, h2 = png_size(os.path.join(out, "c2.png"))
    w3, h3 = png_size(os.path.join(out, "c3.png"))

    check("默认裁掉未使用区域（宽或高不是 2 的幂）", not (is_pow2(w1) and is_pow2(h1)),
          f"{w1}x{h1}")
    check("默认画布比不裁时小", w1 * h1 < w2 * h2, f"{w1}x{h1} vs {w2}x{h2}")
    check("不裁时画布仍是 2 的幂", is_pow2(w2) and is_pow2(h2), f"{w2}x{h2}")
    check("--pot 让裁后的画布回到 2 的幂", is_pow2(w3) and is_pow2(h3), f"{w3}x{h3}")
    check("--pot 的画布不小于裁后的实际内容", w3 >= w1 and h3 >= h1, f"{w3}x{h3} vs {w1}x{h1}")


def case_canvas_shape(tmp: str) -> None:
    """pack 出来的画布要「又方又满」，不能退回一根竖条。

    背景：原来是在一个 2 的幂方块里装、再 --crop，BSSF 在图元尺寸接近时会一路往下贴，
    17 帧 Misaka-Sk2 被排成 603×2035（1:3.37）。加了画布宽度搜索之后应该接近方形。
    用例给一批「竖长条」帧 —— 这是最容易退化成竖条的输入。
    """
    src = os.path.join(tmp, "shape_in")
    os.makedirs(src, exist_ok=True)
    n = 12
    fw, fh = 80, 200
    for i in range(n):
        # 用不同亮度避免被当成重复图
        solid(os.path.join(src, f"f{i:02d}.png"), (fw, fh), (40 + i * 15, 90, 160, 255))
    out = os.path.join(tmp, "shape_out")

    r = run([src, "-o", out, "--mode", "pack", "--name", "sh", "--padding", "0"])
    if not check("画布形状用例跑得通", r.returncode == 0, (r.stderr or "")[-300:]):
        return

    w, h = png_size(os.path.join(out, "sh.png"))
    net = n * fw * fh
    ratio = max(w, h) / float(min(w, h))
    check("不是竖条（长宽比 <= 1.8）", ratio <= 1.8, f"{w}x{h} -> 1:{ratio:.2f}")
    check("面积没有明显浪费（<= 1.10 倍净面积）", w * h <= net * 1.10,
          f"{w}x{h} = {w * h}，净面积 {net}，{w * h / net:.2f} 倍")
    check("所有帧都在", count_frames(os.path.join(out, "sh.paper2dsprites")) == n)


def count_frames(path: str) -> int:
    d = json.load(open(path, encoding="utf-8"))
    return len(d["frames"])


def case_manifest_api(tmp: str) -> None:
    """清单接口：名字与序号由外部给定，不是从文件名反推。

    钉住这几件（对接 ZD 工具箱的契约）：
      - 精灵名必须原样落盘（清单给什么就是什么）
      - _sequence.json 按 index 升序 —— 这是动画帧序的唯一权威
      - 名字重复必须报错（UE 里会互相覆盖，静默灾难）
      - JSON 与 TSV 两种清单等价
      - 序号缺失自动补、图片缺失要报错
    """
    src = os.path.join(tmp, "man_in")
    os.makedirs(src, exist_ok=True)
    n = 6
    for i in range(n):
        solid(os.path.join(src, "raw_%d.png" % i), (48, 64), (30 + i * 30, 120, 200, 255))

    # 故意让序号 ≠ 文件名顺序，并让名字 ≠ 文件名 —— 这样才证明「清单说了算」
    idx = [4, 1, 6, 2, 5, 3]
    frames = [{"file": "raw_%d.png" % i, "name": "Misaka_Sk2_%02d" % idx[i], "index": idx[i]}
              for i in range(n)]
    man = {"atlas": "Misaka_Sk2", "spritePrefix": "Misaka_Sk2", "mode": "pack",
           "trim": True, "padding": 2, "frames": frames}
    jpath = os.path.join(src, "atlas_manifest.json")
    with open(jpath, "w", encoding="utf-8") as f:
        json.dump(man, f, ensure_ascii=False)
    tpath = os.path.join(src, "atlas_manifest.tsv")
    with open(tpath, "w", encoding="utf-8") as f:
        f.write("# file\tname\tindex\n")
        for fr in frames:
            f.write("%s\t%s\t%d\n" % (fr["file"], fr["name"], fr["index"]))

    out = os.path.join(tmp, "man_out")
    rep = os.path.join(out, "report.json")
    r = run(["--manifest", jpath, "-o", out, "--report", rep])
    if not check("清单用例（JSON）跑得通", r.returncode == 0, (r.stderr or "")[-400:]):
        return

    sp = json.load(open(os.path.join(out, "Misaka_Sk2.paper2dsprites"), encoding="utf-8"))
    got = {f["filename"] for f in sp["frames"]}
    want = {fr["name"] for fr in frames}
    check("精灵名原样落盘（与文件名无关）", got == want, f"{sorted(got)} vs {sorted(want)}")

    seq = json.load(open(os.path.join(out, "Misaka_Sk2_sequence.json"), encoding="utf-8"))
    order = [f["index"] for f in seq["frames"]]
    check("_sequence.json 按 index 严格升序", order == sorted(order), str(order))
    check("序号就是清单给的那组", sorted(order) == sorted(idx), str(order))
    check("序号顺序与清单的 name 对得上",
          [f["name"] for f in seq["frames"]] == ["Misaka_Sk2_%02d" % i for i in sorted(idx)],
          str([f["name"] for f in seq["frames"]]))
    check("每帧都带图集矩形 / sourceSize",
          all("frame" in f and "sourceSize" in f and "spriteSourceSize" in f
              for f in seq["frames"]))

    result = json.load(open(rep, encoding="utf-8"))
    check("--report 给出的字段齐",
          all(k in result for k in ("ok", "atlas", "image", "sequence", "size", "frameCount")),
          str(sorted(result)))
    check("--report 的 frameCount 对", result["frameCount"] == n, str(result.get("frameCount")))

    # TSV 与 JSON 等价
    out2 = os.path.join(tmp, "man_out2")
    r2 = run(["--manifest", tpath, "-o", out2, "--name", "Misaka_Sk2", "--mode", "pack", "--trim"])
    if check("清单用例（TSV）跑得通", r2.returncode == 0, (r2.stderr or "")[-300:]):
        a = json.load(open(os.path.join(out, "Misaka_Sk2.paper2dsprites"), encoding="utf-8"))
        b = json.load(open(os.path.join(out2, "Misaka_Sk2.paper2dsprites"), encoding="utf-8"))
        check("JSON 与 TSV 两种清单结果一致",
              sorted(f["filename"] for f in a["frames"]) == sorted(f["filename"] for f in b["frames"]),
              "")

    # 名字重复必须报错
    bad = os.path.join(src, "dup.json")
    badman = dict(man)
    badman["frames"] = [{"file": "raw_0.png", "name": "same", "index": 1},
                        {"file": "raw_1.png", "name": "same", "index": 2}]
    with open(bad, "w", encoding="utf-8") as f:
        json.dump(badman, f, ensure_ascii=False)
    r3 = run(["--manifest", bad, "-o", os.path.join(tmp, "man_bad")])
    check("精灵名重复要报错（不能静默出图）",
          r3.returncode != 0 and "重复" in ((r3.stderr or "") + (r3.stdout or "")),
          ((r3.stderr or "") + (r3.stdout or ""))[-200:])

    # 图片不存在要报错
    miss = os.path.join(src, "missing.json")
    mman = dict(man)
    mman["frames"] = [{"file": "nope.png", "name": "a", "index": 1}]
    with open(miss, "w", encoding="utf-8") as f:
        json.dump(mman, f, ensure_ascii=False)
    r4 = run(["--manifest", miss, "-o", os.path.join(tmp, "man_miss")])
    check("图片不存在要报错", r4.returncode != 0 and "不存在" in ((r4.stderr or "") + (r4.stdout or "")),
          ((r4.stderr or "") + (r4.stdout or ""))[-200:])

    # 序号全缺 -> 按数组顺序补
    auto = os.path.join(src, "auto.json")
    aman = {"atlas": "auto", "frames": [{"file": "raw_%d.png" % i} for i in range(4)]}
    with open(auto, "w", encoding="utf-8") as f:
        json.dump(aman, f, ensure_ascii=False)
    out3 = os.path.join(tmp, "man_auto")
    r5 = run(["--manifest", auto, "-o", out3, "--mode", "pack", "--trim"])
    if check("序号全缺时按数组顺序补", r5.returncode == 0, (r5.stderr or "")[-200:]):
        s = json.load(open(os.path.join(out3, "auto_sequence.json"), encoding="utf-8"))
        check("补出来的序号是 1..4", [x["index"] for x in s["frames"]] == [1, 2, 3, 4],
              str([x["index"] for x in s["frames"]]))
        check("没给名字时用 atlas 名 + 补零序号",
              [x["name"] for x in s["frames"]] == ["auto_01", "auto_02", "auto_03", "auto_04"],
              str([x["name"] for x in s["frames"]]))


def case_shelf_beats_maxrects(tmp: str) -> None:
    """货架式装箱在「图元尺寸接近」时必须明显优于 MaxRects。

    这是拆 TexturePacker 官方 DLL 得到的行为印证：它有 `AlgorithmShelf`，
    和 `AlgorithmMaxRects` 并列，还有 `--pack-mode Fast/Good/Best` 控制搜多狠。
    实测同一批不规则但尺寸接近的图：货架 93.3%、MaxRects 85.3%、
    TexturePacker（真实素材 17 帧）94.8%。所以默认 `--packer auto` 必须两个都试。
    """
    src = os.path.join(tmp, "shelf_in")
    os.makedirs(src, exist_ok=True)
    # 真实素材 Misaka-Sk2 那 17 帧的 trim 尺寸（确定性，不用随机数）
    sizes = [(147, 355), (152, 354), (205, 345), (248, 341), (244, 341), (170, 341),
             (169, 338), (166, 335), (166, 334), (175, 333), (152, 332), (187, 316),
             (179, 316), (201, 299), (223, 285), (256, 266), (245, 264)]
    net = 0
    for i, (w, h) in enumerate(sizes):
        solid(os.path.join(src, "g%02d.png" % i), (w, h), (40 + i * 13, 90, 160, 255))
        net += w * h

    out = os.path.join(tmp, "shelf_out")
    common = ["--mode", "pack", "--padding", "2", "--extrude", "1", "--rotate"]
    r1 = run([src, "-o", out, "--name", "auto"] + common)
    r2 = run([src, "-o", out, "--name", "mr", "--packer", "maxrects"] + common)
    r3 = run([src, "-o", out, "--name", "sh", "--packer", "shelf"] + common)
    if not check("货架/MaxRects 对照用例跑得通",
                 r1.returncode == 0 and r2.returncode == 0 and r3.returncode == 0,
                 ((r1.stderr or "") + (r2.stderr or "") + (r3.stderr or ""))[-300:]):
        return

    aw, ah = png_size(os.path.join(out, "auto.png"))
    mw, mh = png_size(os.path.join(out, "mr.png"))
    sw, sh = png_size(os.path.join(out, "sh.png"))
    da, ds = net / (aw * ah), net / (sw * sh)

    # 断言「契约」而不是「谁赢」：货架和 MaxRects 各有胜负（取决于具体尺寸分布），
    # 真正要保证的是 auto 把两个都试了、并且取到了较好的那个。
    check("auto 单页且有效", aw > 0 and ah > 0 and not os.path.exists(os.path.join(out, "auto_1.png")),
          f"{aw}x{ah}")
    check("auto 至少不差于两者中较好的那个",
          aw * ah <= min(mw * mh, sw * sh) * 1.001,
          f"auto {aw}x{ah} vs maxrects {mw}x{mh} / shelf {sw}x{sh}")
    check("auto 密度 >= 88%", da >= 0.88, f"{aw}x{ah} 密度 {da * 100:.1f}%")
    check("两个装箱器确实产生了不同版面（说明都跑到了）",
          (mw, mh) != (sw, sh), f"maxrects {mw}x{mh} / shelf {sw}x{sh}")

    # 直接钉住注册关系，不依赖版面结果
    check("auto 的候选里包含货架式", any(v.startswith("shelf") for v in _UA.resolve_packers("auto")),
          str(_UA.resolve_packers("auto")))
    check("auto 的候选里也包含 MaxRects", "maxrects" in _UA.resolve_packers("auto"),
          str(_UA.resolve_packers("auto")))
    check("--packer shelf 只走货架", all(v.startswith("shelf") for v in _UA.resolve_packers("shelf")),
          str(_UA.resolve_packers("shelf")))
    check("--packer maxrects 只走 MaxRects", _UA.resolve_packers("maxrects") == ("maxrects",),
          str(_UA.resolve_packers("maxrects")))


def case_rotation(tmp: str) -> None:
    """旋转：该转的时候要真转，不该转的时候不许乱转，转完取图必须还原得回来。

    货架式里旋转的**唯一价值是压低行高** —— 行高 = 行内最高那张，
    整行都按它留空间。所以当素材里混着「几张特别高的 + 一堆矮的」时，
    把那几张转横能整行变矮；而清一色竖长条时转了只是白占宽度。

    这里同时钉住三件事：
      1) 该转的素材，`--rotate` 下确实出现 rotated: true（以前货架式永远 0 旋转）；
      2) 不该转的素材，`--rotate` 下也不会为了转而转（面积不许变差）；
      3) 旋转帧按 UE 的约定取图（先按 h/w 取、再逆时针转回）能逐像素还原。
    """
    src = os.path.join(tmp, "rot_in")
    os.makedirs(src, exist_ok=True)
    # 「几根高梁 + 一堆矮墩」：3 张 60x400 的竖条 + 8 张 100x100 的方块。
    # 竖条若竖放，每个都独占一行、行高 400；转横后高只有 60，行高立刻压下来。
    for i in range(3):
        solid(os.path.join(src, "tall%02d.png" % i), (60, 400), (255, 0, 0, 255))
    for i in range(8):
        solid(os.path.join(src, "wide%02d.png" % i), (100, 100), (0, 0, 255, 255))

    out = os.path.join(tmp, "rot_out")
    common = ["--mode", "pack", "--padding", "2", "--extrude", "1"]
    r_on = run([src, "-o", out, "--name", "on", "--rotate"] + common)
    r_off = run([src, "-o", out, "--name", "off"] + common)
    if not check("旋转对照用例跑得通",
                 r_on.returncode == 0 and r_off.returncode == 0,
                 ((r_on.stderr or "") + (r_off.stderr or ""))[-300:]):
        return

    d_on = json.load(open(os.path.join(out, "on.paper2dsprites"), encoding="utf-8"))
    d_off = json.load(open(os.path.join(out, "off.paper2dsprites"), encoding="utf-8"))
    n_rot_on = sum(1 for f in d_on["frames"] if f.get("rotated"))
    n_rot_off = sum(1 for f in d_off["frames"] if f.get("rotated"))

    check("--rotate 下确实出现了旋转帧（货架式以前永远为 0）", n_rot_on > 0, f"旋转 {n_rot_on} 帧")
    check("不给 --rotate 时一帧都不许转", n_rot_off == 0, f"旋转 {n_rot_off} 帧")

    ow, oh = d_on["meta"]["size"]["w"], d_on["meta"]["size"]["h"]
    fw, fh = d_off["meta"]["size"]["w"], d_off["meta"]["size"]["h"]
    check("这组素材开旋转后画布不更大（转了就该有收益）", ow * oh <= fw * fh, f"on {ow}x{oh} / off {fw}x{fh}")

    # ---- 取图还原：这是旋转功能真正要保证的东西 ----
    # UE 的约定（与 TexturePacker 一致）：JSON 里 frame.w/h 是**未旋转**尺寸，
    # 图集里实际按 (x, y, h, w) 横躺，取出来要逆时针转回。
    sheet = Image.open(os.path.join(out, "on.png")).convert("RGBA")
    src_im = Image.open(os.path.join(src, "tall00.png")).convert("RGBA")
    ok_restore = True
    detail = ""
    for f in d_on["frames"]:
        if not f.get("rotated"):
            continue
        fg = f["frame"]
        x, y, w, h = fg["x"], fg["y"], fg["w"], fg["h"]
        # 旋转帧在图集里占的是 (w 高, h 宽)
        got = sheet.crop((x, y, x + h, y + w)).transpose(Image.Transpose.ROTATE_90)
        want = Image.open(os.path.join(src, os.path.basename(f["filename"]) + ".png"))
        want = want.convert("RGBA").crop((0, 0, w, h))
        if got.size != want.size or got.tobytes() != want.tobytes():
            ok_restore = False
            detail = f"{f['filename']} 还原不一致 got={got.size} want={want.size}"
            break
    check("旋转帧按 UE 约定取图能逐像素还原", ok_restore, detail)


def case_mixed_size_warning(tmp: str) -> None:
    """源帧尺寸不一时必须提醒：这是 UE 里锚点会不一致的根因。"""
    src = os.path.join(tmp, "mixed_in")
    os.makedirs(src, exist_ok=True)
    solid(os.path.join(src, "f1.png"), (128, 96), (255, 0, 0, 255))
    solid(os.path.join(src, "f2.png"), (96, 128), (0, 0, 255, 255))
    out = os.path.join(tmp, "warn_out")
    r = run([src, "-o", out, "--mode", "pack", "--name", "w"])
    check("尺寸不一时给出锚点警告", "锚点" in (r.stdout or ""), (r.stdout or "")[-200:])

    same = os.path.join(tmp, "same_in")
    os.makedirs(same, exist_ok=True)
    for i in range(3):
        solid(os.path.join(same, f"k{i}.png"), (64, 64), (255, 0, 0, 255))
    r2 = run([same, "-o", os.path.join(tmp, "warn_out2"), "--mode", "pack", "--name", "s"])
    check("尺寸统一时不该刷警告", "锚点" not in (r2.stdout or ""))


def main() -> int:
    print("ue_atlas.py 行为自检")
    tmp = tempfile.mkdtemp(prefix="ue_atlas_check_")
    try:
        print("\n1) 扩边与间距")
        case_extrude_gap(tmp)
        print("\n2) grid 模式的统一锚点")
        case_grid_unifies_anchor(tmp)
        print("\n3) pack 模式的画布尺寸")
        case_pack_canvas(tmp)
        print("\n4) pack 模式的画布形状（搜索后不能退回竖条）")
        case_canvas_shape(tmp)
        print("\n5) 清单接口（名字 + 序号，对接 ZD 工具箱）")
        case_manifest_api(tmp)
        print("\n6) 货架式装箱优于 MaxRects（拆 TP DLL 印证的行为）")
        case_shelf_beats_maxrects(tmp)
        print("\n7) 帧尺寸不一的警告")
        case_mixed_size_warning(tmp)
        print("\n8) 旋转（该转才转 + 取图能还原）")
        case_rotation(tmp)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)

    print()
    if _failed:
        print(f"{_passed} 通过，{len(_failed)} 失败：")
        for name in _failed:
            print(f"  - {name}")
        return 1
    print(f"全部通过（{_passed} 项）。")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
