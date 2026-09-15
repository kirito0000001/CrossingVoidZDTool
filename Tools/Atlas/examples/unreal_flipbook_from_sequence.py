# -*- coding: utf-8 -*-
"""
unreal_flipbook_from_sequence.py —— 按 _sequence.json 的**序号**建 Paper2D Flipbook

这是「图集工具 → ZD 工具箱 Unreal 侧」的参考消费实现。

为什么要专门做这件事：
    UE 从 sprite sheet 建 Flipbook 时，是按 **sprite 名排序**决定帧序的。
    源帧一旦是哈希名（Misaka-Sk2-5ae23a0f…），顺序就丢在导入这一步了。
    图集工具走清单时，序号是工具箱给的权威值，已经固化在 `<atlas>_sequence.json` 里，
    这个脚本就照它精确建 Flipbook，不去猜名字排序。

⚠️ 这是**参考骨架，没有在编辑器里跑过**。它依赖两处 UE Python API，不同 UE 版本签名可能不同：
      1) 拿 Flipbook 工厂：`unreal.PaperFlipbookFactory()`
      2) 写帧：        `factory.set_editor_property("key_frames", ...)`
    接进 ZD 工具箱的 UnrealBridge 时，按你们既有脚本（Tools/Unreal/*.py）的约定调整：
    路径解析、ToolboxLog、错误处理、是否走 Remote Execution 还是 -run=pythonscript。

用法（编辑器 Python）：
    import unreal
    py = r"D:/图集/ue_atlas_tool/examples/unreal_flipbook_from_sequence.py"
    exec(open(py, encoding="utf-8").read())
    make_flipbook(
        sequence_json = r"D:/out/Misaka_Sk2_sequence.json",
        sprite_folder = "/Game/GameActor2D/Misaka/ZDMaterial/Sk2",  # 导入 sprite 后所在目录
        dest_folder   = "/Game/GameActor2D/Misaka/ZDMaterial/Sk2",
        fps           = 12.0,
    )
"""

import json
import os

import unreal


def _load_sprite(folder: str, name: str):
    """按 `目录/精灵名` 取已导入的 Sprite 资产。"""
    path = "{}/{}".format(folder.rstrip("/"), name)
    sprite = unreal.load_asset(path)
    if sprite is None:
        # 有的导入流程会给 sprite 名带前缀/后缀，退回按名字模糊找一遍
        candidates = unreal.EditorAssetLibrary.list_assets(folder, recursive=False, include_folder=False)
        hit = [c for c in candidates if c.split("/")[-1].split(".")[0] == name]
        if hit:
            sprite = unreal.load_asset(hit[0])
    return sprite


def make_flipbook(sequence_json: str, sprite_folder: str, dest_folder: str,
                  fps: float = 12.0, asset_name: str = None):
    """读 _sequence.json，按 index 升序建一个 Flipbook。

    返回创建的 Flipbook 资产的完整路径。
    """
    with open(sequence_json, "r", encoding="utf-8") as f:
        data = json.load(f)

    atlas = data.get("atlas") or os.path.splitext(os.path.basename(sequence_json))[0]
    asset_name = asset_name or atlas
    frames = sorted(data["frames"], key=lambda e: (e["index"], e["name"]))

    key_frames = []
    missing = []
    for entry in frames:
        sprite = _load_sprite(sprite_folder, entry["name"])
        if sprite is None:
            missing.append(entry["name"])
            continue
        kf = unreal.PaperFlipbookKeyFrame()
        kf.set_editor_property("sprite", sprite)
        kf.set_editor_property("frame_run", 1)      # 一帧显示几帧时长（默认 1）
        key_frames.append(kf)

    if missing:
        # 缺帧直接停下比起一张顺序错的 Flipbook 安全
        raise RuntimeError("以下 Sprite 没找到，先确认图集已导入 {}：{}".format(
            sprite_folder, ", ".join(missing[:8])))

    factory = unreal.PaperFlipbookFactory()
    factory.set_editor_property("key_frames", key_frames)

    tools = unreal.AssetToolsHelpers.get_asset_tools()
    flipbook = tools.create_asset(asset_name, dest_folder, unreal.PaperFlipbook, factory)
    if flipbook is None:
        raise RuntimeError("创建 Flipbook 失败：{}/{}".format(dest_folder, asset_name))

    # 默认时长 = 每帧 1 帧，所以 fps 就是「每秒几帧」
    flipbook.set_editor_property("frames_per_second", float(fps))
    unreal.EditorAssetLibrary.save_loaded_asset(flipbook)

    unreal.log("[atlas] Flipbook {} 建好：{} 帧，{:.1f} fps（顺序取自 _sequence.json 的 index）".format(
        flipbook.get_path_name(), len(key_frames), fps))
    return flipbook.get_path_name()


def describe(sequence_json: str):
    """不建资产，只把 _sequence.json 的帧序打出来，方便先核对。"""
    with open(sequence_json, "r", encoding="utf-8") as f:
        data = json.load(f)
    out = []
    for e in sorted(data["frames"], key=lambda x: (x["index"], x["name"])):
        f = e["frame"]
        out.append("  #{:<3} {:<24} frame=({},{},{},{}){}".format(
            e["index"], e["name"], f["x"], f["y"], f["w"], f["h"],
            " rotated" if e.get("rotated") else ""))
    text = "{}  共 {} 帧  贴图 {}x{}".format(
        data.get("atlas"), data.get("frameCount"),
        data.get("texture", {}).get("w"), data.get("texture", {}).get("h")) + "\n" + "\n".join(out)
    unreal.log(text)
    return text


if __name__ == "__main__":
    unreal.log("这是给编辑器 Python 用的参考模块，请在 UE Python 里 import / exec 后调 make_flipbook()。")
