"""导出脚本里「路径/资产名按大小写比」这一族缺陷的自检。

    python Tools/UnrealBridge/tests/check_export_asset_paths.py

Unreal 的包路径本来就大小写不敏感（FName 比较就不敏感），磁盘上目录叫
Material 还是 material、蓝图叫 Misaka 还是 MISAKA，全看当初是谁建的。
脚本里凡是拿资产名/包路径做精确比较的地方，只要大小写对不上就静默丢资产：
清单照样写出、导出照样报成功，用户只看到「这个角色什么都没有」。

覆盖：
1. 帧贴图预览的目录判定（原来是 "/Material/" in package_path）——
   目录写成 material/ 时贴图掉进 else 返回 ""，exportedFilePath 为空，
   界面上该动作有帧、没预览图，也无法与本地素材比对；
2. 角色蓝图与文件夹同名的判定（原来是 asset_name != relative）——
   文件夹 Misaka/ 里蓝图叫 MISAKA 时整个角色不进 character_actors，
   下游序列导出、BUFF 导出一并跳过；
3. 顺带扫出来的同族写法：Item_ 前缀、/Game 前缀换算磁盘路径、
   object path 与清单资产的比对。

模块顶层会调用 _export()，所以把那一段摘掉再 exec；unreal 用桩顶上。
"""
import io
import os
import shutil
import sys
import tempfile
import types

# 中文要经管道回到回归套件里。标准输出的编码跟着代码页走，不是 936 的机器上
# print 一句中文就 UnicodeEncodeError，退出码 1——代码明明是对的，整套回归却红了。
try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    try:
        sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
        sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding="utf-8", errors="replace")
    except Exception:
        pass

REPO = os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.dirname(os.path.abspath(__file__)))))
P = os.path.join(REPO, "Tools", "Unreal", "export_zd_assets.py")
src = io.open(P, encoding="utf-8").read()
RUNNER = "\ntry:\n    _export()\n"
cut = src.find(RUNNER)
if cut < 0:
    raise SystemExit("没找到模块末尾的 _export() 入口，自检需要先摘掉它")
src = src[:cut]


class _FakeExportTask(object):
    """替身 AssetExportTask：只记住脚本往上面写了什么。"""

    def __init__(self):
        self.object = None
        self.filename = ""
        self.automated = False
        self.replace_identical = False
        self.prompt = True
        self.selected = True
        self.exporter = None


class _FakeExporter(object):
    @staticmethod
    def run_asset_export_task(task):
        # 真实导出会落一个 PNG；脚本随后要 os.path.exists 才认，所以这里真写一个空文件。
        with io.open(task.filename, "wb") as handle:
            handle.write(b"\x89PNG")
        return True


unreal = types.ModuleType("unreal")
unreal.log = lambda *a, **k: None
unreal.log_warning = lambda *a, **k: None
unreal.log_error = lambda *a, **k: None
unreal.load_asset = lambda path: object()
unreal.load_object = lambda outer, path: None
unreal.load_class = lambda outer, path: None
unreal.AssetExportTask = _FakeExportTask
unreal.TextureExporterPNG = _FakeExportTask
unreal.Exporter = _FakeExporter
sys.modules["unreal"] = unreal

module = types.ModuleType("zdexport")
module.__dict__["__name__"] = "zdexport"
exec(compile(src, P, "exec"), module.__dict__)

failed = []


def check(name, expected, actual):
    ok = expected == actual
    print(("  PASS " if ok else "  FAIL ") + name)
    if not ok:
        print("        期望 %r，实际 %r" % (expected, actual))
        failed.append(name)


class _FakeAssetData(object):
    """替身 AssetData：只提供导出用到的那几个字段。"""

    def __init__(self, package_path, asset_name, asset_class="Texture2D"):
        self.package_path = package_path
        self.asset_name = asset_name
        self.package_name = package_path + "/" + asset_name
        self.asset_class = asset_class

    def get_asset(self):
        return object()

    def get_soft_object_path(self):
        raise RuntimeError("soft object path is unavailable in this stub")


def exported_relative(package_path, asset_name="Frame0", asset_class="Texture2D"):
    """跑一趟贴图导出，返回相对于导出根目录的落地路径（没导出就是空串）。"""
    root = tempfile.mkdtemp(prefix="zd-export-")
    try:
        result = module._export_texture_png(
            _FakeAssetData(package_path, asset_name, asset_class), root)
        if not result:
            return ""
        return os.path.relpath(result, root).replace("\\", "/")
    finally:
        shutil.rmtree(root, ignore_errors=True)


print("1) 帧贴图预览：Material 目录的大小写不该决定有没有预览图")
check("规范拼写 Material 照常导出",
      "SequenceFrames/Misaka/Material/Click/Frame0.png",
      exported_relative("/Game/GameActor2D/Misaka/Material/Click"))
check("磁盘上是 material 也要导出",
      "SequenceFrames/Misaka/material/Click/Frame0.png",
      exported_relative("/Game/GameActor2D/Misaka/material/Click"))
check("全大写 MATERIAL 也要导出",
      "SequenceFrames/Misaka/MATERIAL/Click/Frame0.png",
      exported_relative("/Game/GameActor2D/Misaka/MATERIAL/Click"))
check("BUFF 图标仍走 BuffIcons",
      "BuffIcons/Misaka/BUFF/Icon0.png",
      exported_relative("/Game/GameActor2D/Misaka/BUFF", "Icon0"))
check("共享素材根仍走 Images",
      "Images/Misaka/Misaka-SkillIcon-1.png",
      exported_relative("/Game/AssetMaterial/ImageS/CharaterS/Misaka",
                        "Misaka-SkillIcon-1"))
check("角色 Sound 目录不该被当成帧素材", "",
      exported_relative("/Game/GameActor2D/Misaka/Sound", "Vo_Tone1"))
check("非贴图资产照旧不导", "",
      exported_relative("/Game/GameActor2D/Misaka/Material/Click", "Click",
                        "PaperFlipbook"))

print("2) 角色蓝图：文件夹 Misaka/ 里蓝图叫 MISAKA，不能整个角色都不见")


def actor_codes(entries):
    manifest = [{
        "packagePath": package_path,
        "assetName": asset_name,
        "objectPath": "%s/%s.%s" % (package_path, asset_name, asset_name),
    } for package_path, asset_name in entries]
    return [actor.get("code", "") for actor in module._export_character_actors(manifest)]


check("规范拼写照常认出", ["Misaka"],
      actor_codes([("/Game/GameActor2D/Misaka", "Misaka")]))
check("文件夹和蓝图都是全大写时照常认出（code 跟着文件夹走）", ["MISAKA"],
      actor_codes([("/Game/GameActor2D/MISAKA", "MISAKA")]))
# code 取的是文件夹名：actor_asset_map 和 _split_character_actor_relative_path
# 都按文件夹名建索引，取资产名会让下游全对不上。
check("code 取文件夹名而不是资产名", ["Misaka"],
      actor_codes([("/Game/GameActor2D/Misaka", "MISAKA")]))
check("同目录里的其它资产仍要排除", [],
      actor_codes([("/Game/GameActor2D/Misaka", "Misaka_AnimBP")]))
check("子目录里的资产仍要排除", [],
      actor_codes([("/Game/GameActor2D/Misaka/AnimSequences", "Misaka")]))

print("3) 同族：/Game 前缀换算磁盘路径")
check("规范拼写",
      os.path.join("C:", os.sep, "P", "Content", "GameActor2D", "Misaka"),
      module._content_path_to_disk(r"C:\P\CrossingVoid.uproject", "/Game/GameActor2D/Misaka"))
check("小写 /game 也要剥掉前缀（否则算出 Content/game/... 多套一层）",
      os.path.join("C:", os.sep, "P", "Content", "GameActor2D", "Misaka"),
      module._content_path_to_disk(r"C:\P\CrossingVoid.uproject", "/game/GameActor2D/Misaka"))

print("4) 同族：Item_ 前缀的角色道具")


def disk_item_codes(file_names):
    root = tempfile.mkdtemp(prefix="zd-items-")
    try:
        folder = os.path.join(root, "Content", "ITems", "CharItemS")
        os.makedirs(folder)
        for file_name in file_names:
            with io.open(os.path.join(folder, file_name), "wb") as handle:
                handle.write(b"")
        project = os.path.join(root, "CrossingVoid.uproject")
        return [item.get("code", "")
                for item in module._export_character_items_from_disk(project)]
    finally:
        shutil.rmtree(root, ignore_errors=True)


check("规范拼写 Item_Misaka", ["Misaka"], disk_item_codes(["Item_Misaka.uasset"]))
check("ITEM_Misaka 也要认（原来整条丢掉，角色列表里直接没这个人）",
      ["Misaka"], disk_item_codes(["ITEM_Misaka.uasset"]))
check("不带前缀的资产仍要排除", [], disk_item_codes(["Misaka.uasset"]))

print("5) 同族：object path 与清单资产的比对")
asset = {
    "assetName": "Frame0",
    "packageName": "/Game/GameActor2D/Misaka/Material/Click/Frame0",
    "packagePath": "/Game/GameActor2D/Misaka/Material/Click",
    "objectPath": "/Game/GameActor2D/Misaka/Material/Click/Frame0.Frame0",
}
check("完全一致", True,
      module._asset_path_matches(asset, asset["objectPath"]))
check("大小写不同视为同一个资产", True,
      module._asset_path_matches(asset, "/game/gameactor2d/misaka/material/click/Frame0.Frame0"))
check("包路径形式也要认", True,
      module._asset_path_matches(asset, "/Game/GameActor2D/MISAKA/material/Click/Frame0"))
check("真的是别的资产就不认", False,
      module._asset_path_matches(asset, "/Game/GameActor2D/Misaka/Material/Click/Frame1.Frame1"))
check("空值不认", False, module._asset_path_matches(asset, ""))

print()
print("全部通过。" if not failed else "失败 %d 项：%s" % (len(failed), failed))
sys.exit(1 if failed else 0)
