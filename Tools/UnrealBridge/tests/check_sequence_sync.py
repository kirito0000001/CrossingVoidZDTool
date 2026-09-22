"""第五步《序列同步》结果协议的自检。

    python Tools/UnrealBridge/tests/check_sequence_sync.py

覆盖两个真实踩到的坑：
1. 顶层 succeeded 写死 True —— 孤儿序列解绑失败（ZDBridge 少这个 API，或者调用
   抛了异常）时条目里明明是 succeeded=False，整体却报成功，C# 侧照常走复扫、
   写基线，这条失败被彻底盖掉；
2. 'orphan-sequences' 这条伪条目跟真实动作条目混在同一个 items 数组里 ——
   C# 的 succeededActionCodes 把它一并收进去，于是"一个动作都没成功就抛"这条
   兜底失效：所有真实动作都炸了也不抛，界面还报"已成功 1 个动作并写入基线"，
   而 'orphan-sequences' 会被当成动作码写进基线。

模块顶层会调用 main()，所以把那一段摘掉再 exec；unreal 用桩顶上。
"""
import io
import json
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

P = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                 "sync_character_sequences.py")
src = io.open(P, encoding="utf-8").read()
# 模块最后那段 try: main() 是脚本入口，exec 的时候不能让它跑起来。
RUNNER = "\ntry:\n    main()\n"
cut = src.find(RUNNER)
if cut < 0:
    raise SystemExit("没找到模块末尾的 main() 入口，自检需要先摘掉它")
src = src[:cut]

unreal = types.ModuleType("unreal")
unreal.log = lambda *a, **k: None
unreal.log_warning = lambda *a, **k: None
unreal.log_error = lambda *a, **k: None


def is_asset_path(path):
    """`/Game/X/Y.Y` 是资产，`/Game/X/Y` 是文件夹。

    这个区别必须让桩也认：特效层踩过一次「把材质文件夹当资产 load」，
    而桩要是对什么路径都返回一个对象，那条错误就永远露不出来。
    """
    return "." in str(path or "").rsplit("/", 1)[-1]


unreal.load_asset = lambda path: object() if is_asset_path(path) else None
sys.modules["unreal"] = unreal

module = types.ModuleType("step5")
module.__dict__["__name__"] = "step5"
exec(compile(src, P, "exec"), module.__dict__)

failed = []


MISSING = "<缺这个字段>"


def field(result, name, index=None):
    """取结果字段；取不到就返回哨兵，好让自检把全部失败项一次报完。"""
    if index is None:
        return result.get(name, MISSING)
    items = result.get("items", [])
    if index >= len(items):
        return MISSING
    return items[index].get(name, MISSING)


def check(name, expected, actual):
    ok = expected == actual
    print(("  PASS " if ok else "  FAIL ") + name)
    if not ok:
        print("        期望 %r，实际 %r" % (expected, actual))
        failed.append(name)


class _DetachBridge(object):
    """假的 ZDBridgeLibrary：只实现解绑这一个函数。"""

    def __init__(self, detached=(), errored=()):
        self._detached = list(detached)
        self._errored = list(errored)

    def detach_sequences_from_animation_source(self, paths):
        items = []
        for path in paths:
            items.append({
                "objectPath": path,
                "detached": path not in self._errored,
                "assetClass": "PaperZDAnimSequence_Flipbook",
                "previousSource": "",
                "error": "" if path not in self._errored else "still referenced",
            })
        return json.dumps({"items": items})


def run_sync(action_codes, detach_paths, bridge, sync_action, flow_break=False,
             actions=None, plan_overrides=None):
    """跑一趟 main()，把落盘的结果文件读回来。

    结果协议是这一步唯一的对外契约，所以自检读的是真正写出去的 JSON，
    而不是函数返回值——写错字段名这类问题只有读文件才看得见。

    flow_break=True 模拟「流程里任意一处非动作级的故障」（进度写入炸了、
    写结果炸了…）：逐动作的异常是被单独兜住的，流程级异常另走一条路，
    两者留下的线索不一样，所以两条路都要能跑。

    sync_action=None 表示**不替换** _sync_action，用脚本自己那一条：
    特效层就是靠这个真跑一遍（它是唯一会碰引擎的地方）。
    actions 直接给整个动作列表（特效层要带图集矩形这类细节）。
    """
    root = tempfile.mkdtemp(prefix="zd-seq-sync-")
    try:
        plan_path = os.path.join(root, "plan.json")
        result_path = os.path.join(root, "result.json")
        plan = {
            "protocolVersion": 1,
            "characterCode": "Misaka",
            "characterBlueprintPath": "/Game/GameActor2D/Misaka/Misaka.Misaka",
            "animMapsPath": "/Game/GameActor2D/Misaka/Misaka_AnimMaps.Misaka_AnimMaps",
            "detachSequenceObjectPaths": list(detach_paths),
            "actions": actions if actions is not None
            else [{"actionCode": code, "displayName": code} for code in action_codes],
        }
        if plan_overrides:
            plan.update(plan_overrides)
        with io.open(plan_path, "w", encoding="utf-8") as handle:
            handle.write(json.dumps(plan, ensure_ascii=False))

        if bridge is None:
            # ZDBridge 没装/是旧二进制：解绑这一步做不了。
            if hasattr(unreal, "ZDBridgeLibrary"):
                delattr(unreal, "ZDBridgeLibrary")
        else:
            unreal.ZDBridgeLibrary = bridge

        os.environ["ZD_SEQUENCE_SYNC_PLAN_PATH"] = plan_path
        os.environ["ZD_SEQUENCE_SYNC_RESULT_PATH"] = result_path
        os.environ["ZD_BRIDGE_PROGRESS_PATH"] = ""

        original_sync_action = module._sync_action
        original_write_progress = module._write_progress
        if sync_action is not None:
            module._sync_action = sync_action
        if flow_break:
            def explode(*_args, **_kwargs):
                raise RuntimeError("flow broke before the action loop finished")
            module._write_progress = explode
        raised = ""
        try:
            module.main()
        except Exception as error:
            raised = str(error)
        finally:
            if sync_action is not None:
                module._sync_action = original_sync_action
            module._write_progress = original_write_progress

        with io.open(result_path, encoding="utf-8") as handle:
            return json.load(handle), raised
    finally:
        shutil.rmtree(root, ignore_errors=True)


def ok_action(action):
    return {
        "actionCode": action["actionCode"],
        "sequencePath": "/Game/GameActor2D/Misaka/AnimSequences/%s.%s" % (
            action["actionCode"], action["actionCode"]),
        "flipbookPath": "",
        "frameCount": 3,
        "animMapsChange": "created",
        "deletedAssets": [],
        "legacyAssetsNotDeleted": [],
    }


def boom_action(action):
    raise RuntimeError("%s: ZDBridge.create_paper_flipbook_from_sprites failed" % action["actionCode"])


def package_of(path):
    """`/Game/X/Y.Y` → `/Game/X/Y`（和脚本里的 _package 同一件事）。"""
    return str(path or "").split(".", 1)[0]


class _Named(object):
    def __init__(self, name):
        self._name = name

    def get_name(self):
        return self._name


class _FakeAsset(object):
    """够 _sync_effect_layer 用的一只假资产：路径、属性袋、贴图尺寸。"""

    def __init__(self, path, texture_size=None):
        self.path = path
        self.texture_size = texture_size
        self.properties = {}

    def get_path_name(self):
        return self.path

    def get_name(self):
        return self.path.rsplit("/", 1)[-1].split(".")[0]

    def get_class(self):
        return _Named("PaperSprite")

    def modify(self):
        pass

    def get_editor_property(self, name):
        return self.properties.get(name)

    def set_editor_property(self, name, value):
        self.properties[name] = value

    def blueprint_get_size_x(self):
        return self.texture_size[0]

    def blueprint_get_size_y(self):
        return self.texture_size[1]


class _Vector2D(object):
    def __init__(self, x, y):
        self.x = x
        self.y = y


class _AssetImportTask(object):
    def __init__(self):
        self.filename = ""
        self.destination_path = ""
        self.destination_name = ""
        self.automated = False
        self.replace_existing = False
        self.replace_existing_settings = False
        self.save = False
        self.imported_object_paths = []


class _EffectEngine(object):
    """特效层同步要碰的那几样虚幻 API 的桩。

    为什么值得搭这一套：这条路上两个真实的坑 —— 把材质文件夹当资产 load、
    计划里没带图集矩形 —— **都只在真跑一次的时候**才会露出来，读代码看不出来。
    """

    def __init__(self):
        self.assets = {}
        self.load_calls = []
        self.folder_loads = []
        self.sprite_requests = []
        self.flipbook_requests = []
        self.purged = []
        self.imported_files = []
        self.bridge = self

    # ---- 资产登记 / 加载

    def seed(self, path, texture_size=None):
        asset = _FakeAsset(path, texture_size)
        # 键一律用小写：虚幻的资产路径在 Windows 上就是大小写不敏感的，
        # 桩要是逐字节比，就把「路径大小写不一致」这类真问题遮掉了。
        self.assets[package_of(path).lower()] = asset
        return asset

    def load_asset(self, path):
        self.load_calls.append(path)
        if not is_asset_path(path):
            # 文件夹不是资产。真的引擎在这里就是返回 None。
            self.folder_loads.append(path)
            return None
        key = package_of(path).lower()
        return self.assets.get(key) or _FakeAsset(path)

    # ---- ZDBridgeLibrary

    def create_paper_sprite_from_texture(self, texture, folder, name):
        self.sprite_requests.append(name)
        return self.seed("%s/%s.%s" % (folder, name, name))

    def create_paper_flipbook_from_sprites(self, sprites, frame_runs, fps, folder, name):
        self.flipbook_requests.append({
            "name": name,
            "sprites": list(sprites),
            "frameRuns": list(frame_runs),
            "fps": fps,
        })
        return self.seed("%s/%s.%s" % (folder, name, name))

    def purge_assets(self, paths):
        items = []
        for path in paths:
            self.purged.append(path)
            self.assets.pop(package_of(path).lower(), None)
            items.append({"objectPath": path, "deleted": True, "assetClass": "PaperSprite"})
        return json.dumps({"items": items})

    # ---- 装 / 卸

    def install(self):
        engine = self

        class _EditorAssetLibrary(object):
            def does_asset_exist(self, path):
                return package_of(path).lower() in engine.assets

            def list_assets(self, folder, recursive=False, include_folder=False):
                return []

            def does_directory_exist(self, path):
                return False

            def save_asset(self, path, only_if_is_dirty=True):
                return True

            def delete_asset(self, path):
                engine.assets.pop(package_of(path).lower(), None)
                return True

        class _AssetTools(object):
            def import_asset_tasks(self, tasks):
                for task in tasks:
                    if not os.path.isfile(task.filename):
                        continue
                    path = "%s/%s.%s" % (
                        task.destination_path, task.destination_name, task.destination_name)
                    engine.imported_files.append(task.filename)
                    engine.seed(path, texture_size=(64, 64))
                    task.imported_object_paths = [path]

        class _AssetToolsHelpers(object):
            @staticmethod
            def get_asset_tools():
                return _AssetTools()

        unreal.load_asset = self.load_asset
        unreal.EditorAssetLibrary = _EditorAssetLibrary()
        unreal.AssetImportTask = _AssetImportTask
        unreal.AssetToolsHelpers = _AssetToolsHelpers
        unreal.Vector2D = _Vector2D
        return self


def succeeded_action_codes(result, filter_diagnostics):
    """照抄 C# 侧 MainWindow.UnrealSync.Publish.cs 里 succeededActionCodes 的算法。

    filter_diagnostics=False 是现在 C# 的样子；True 是这次要求它改成的样子。
    两种都算一遍，才能说清楚"Python 打了标记、C# 不用就仍然是错的"。
    """
    codes = []
    for item in result.get("items", []):
        if not item.get("succeeded"):
            continue
        if not str(item.get("stableId", "")).strip():
            continue
        if filter_diagnostics and item.get("itemKind", "action") != "action":
            continue
        codes.append(item["stableId"])
    return codes


ORPHAN = "/Game/ZDBridgeTest/Test_Sequence.Test_Sequence"

print("1) 缺陷 1：ZDBridge 缺解绑 API，动作全成功 —— 顶层不许报成功")
result, raised = run_sync(["Click"], [ORPHAN], None, ok_action)
check("顶层判失败", False, field(result, "succeeded"))
check("是条目失败，不是流程异常", "items", field(result, "failureKind"))
check("错误里点名解绑那条", True, "orphan-sequences" in result.get("errorMessage", ""))
check("动作条目本身仍是成功", True, field(result, "succeeded", 1))
check("main() 不抛（结果要能落盘给 C# 读）", "", raised)

print("2) 解绑成功、动作也成功 —— 才算整体成功")
result, raised = run_sync(["Click", "Sk1"], [ORPHAN], _DetachBridge(detached=[ORPHAN]), ok_action)
check("顶层判成功", True, field(result, "succeeded"))
check("没有失败种类", "", field(result, "failureKind"))
check("错误消息为空", "", field(result, "errorMessage"))

print("3) 缺陷 2：所有真实动作都失败，只剩解绑那条伪条目")
result, raised = run_sync(["Click", "Sk1"], [ORPHAN], _DetachBridge(detached=[ORPHAN]), boom_action)
items = result.get("items", [])
check("解绑那条 + 两个失败的动作用条目", 3, len(items))
check("伪条目标成 diagnostic", "diagnostic", field(result, "itemKind", 0))
# 这一条正是缺陷 2 的核心：不过滤的话 C# 认为"有一个动作成功了"，
# 于是不抛异常，还会把 'orphan-sequences' 当动作码写进基线。
check("不过滤 = C# 误以为成功了一个动作", ["orphan-sequences"],
      succeeded_action_codes(result, filter_diagnostics=False))
check("按 itemKind 过滤后一个动作都没成", [],
      succeeded_action_codes(result, filter_diagnostics=True))
# 逐动作的异常是被单独兜住的（add4ac0 起）：一个动作炸了不该让后面的陪葬，
# 更要紧的是**必须点名**。所以这里是条目级失败，不是流程被异常打断。
check("流程跑完了，是条目失败", "items", field(result, "failureKind"))
check("没有动作成功时不抛（结果照常落盘，C# 侧按 itemKind 判完再抛）", "", raised)
error_message = result.get("errorMessage", "")
check("两个失败动作都点了名", True,
      all(code in error_message for code in ("Click", "Sk1")))

print("4) 真实动作条目要标成 action，别让 C# 把它一起滤掉")
result, raised = run_sync(["Click"], [], None, ok_action)
check("动作条目标成 action", "action", field(result, "itemKind", 0))
check("过滤后动作还在", ["Click"], succeeded_action_codes(result, filter_diagnostics=True))
check("没有解绑任务时整体成功", True, field(result, "succeeded"))

print("5) 异常与条目失败是两回事，两段线索都要留下")
result, raised = run_sync(["Click"], [ORPHAN], None, ok_action, flow_break=True)
check("失败种类是异常", "exception", field(result, "failureKind"))
check("留下了异常本身", True, "flow broke" in result.get("errorMessage", ""))
check("也留下了解绑失败", True, "orphan-sequences" in result.get("errorMessage", ""))
check("main() 照旧把异常抛出去", True, bool(raised))
check("异常时条目仍如实落盘", 1, len(result.get("items", [])))

EFFECT_FOLDER = "/Game/GameActor2D/Misaka/Material/Sk2"
EFFECT_ATLAS = "Misaka_Sk2_Effect"
STALE_EFFECT_SPRITE = "%s/Sk2_Effect_Frame01_Sprite.Sk2_Effect_Frame01_Sprite" % EFFECT_FOLDER
# 待删名单里**故意混进一条大小写不同的 Flipbook 路径**（工具箱侧曾经发出来的就是
# 归一化过的小写路径）。引擎认它就是要重建的那只 Flipbook；逐字节比的清理会把自己
# 刚建好的资产删掉，所以这条必须活下来。
STALE_EFFECT_FLIPBOOK_LOWER = "%s/sk2_effect_flipbook.sk2_effect_flipbook" % EFFECT_FOLDER


def effect_plan_action(atlas_image_path, blank_ordinals=()):
    """一条特效层计划项，字段名和 C# 写出来的 JSON 一样（camelCase）。

    4 个输出帧、其中 2 号是空帧 —— 空帧在 Flipbook 里要留成 None 关键帧，
    时间照占，所以这里正好盯住"空帧有没有被跳过"这件事。
    """
    sprite_names = {
        ordinal: "Sk2_Effect_Frame%02d_Sprite" % (ordinal - 1)
        for ordinal in (1, 2, 3, 4)
        if ordinal not in blank_ordinals
    }
    frames = []
    source_images = []
    for ordinal in (1, 2, 3, 4):
        is_blank = ordinal in blank_ordinals
        frames.append({
            "index": ordinal,
            "ordinal": ordinal - 1,
            "filePath": "" if is_blank else "D:/frames/Sk2_Effect_%04d.png" % ordinal,
            "durationFrames": 1,
            "isBlank": is_blank,
            "voiceFileName": "",
            "sourceImageIndex": 0,
            "spriteAssetName": "" if is_blank else sprite_names[ordinal],
        })
        if is_blank:
            continue
        source_images.append({
            "index": len(source_images) + 1,
            "spriteAssetName": sprite_names[ordinal],
            "filePath": frames[-1]["filePath"],
            "atlasName": EFFECT_ATLAS,
            "atlasImagePath": atlas_image_path,
            "atlasMaterialFolder": EFFECT_FOLDER,
            "createSprite": True,
            # 图集矩形：少了它 Python 侧会报 "atlas rect ... is empty"。
            "x": 0,
            "y": (len(source_images)) * 40,
            "width": 32,
            "height": 40,
            "rotated": False,
            "trimmed": True,
            "trimOriginX": 4,
            "trimOriginY": 6,
            "sourceImageWidth": 40,
            "sourceImageHeight": 52,
        })
        frames[-1]["sourceImageIndex"] = len(source_images)
    return {
        "actionCode": "Sk2_Effect",
        "baseActionCode": "Sk2",
        "formIndex": 1,
        "displayName": "Sk2 · 特效",
        # 计划里的帧率**已经是**动作帧率 × 倍数，Python 侧不再自己乘。
        "fps": 24,
        "targetMaterialFolder": EFFECT_FOLDER,
        "flipbookAssetName": "Sk2_Effect_Flipbook",
        "isEffectLayer": True,
        "hasStaleAssetSelection": True,
        "staleAssetObjectPaths": [STALE_EFFECT_SPRITE, STALE_EFFECT_FLIPBOOK_LOWER],
        "frames": frames,
        "sourceImages": source_images,
    }


print("6) 特效层：不替换 _sync_action，真跑一遍")
effect_root = tempfile.mkdtemp(prefix="zd-effect-")
try:
    atlas_image = os.path.join(effect_root, EFFECT_ATLAS + ".png")
    with io.open(atlas_image, "wb") as handle:
        handle.write(b"\x89PNG\r\n\x1a\n" + b"\x00" * 32)

    engine = _EffectEngine().install()
    engine.seed(STALE_EFFECT_SPRITE)
    result, raised = run_sync(
        [], [], engine.bridge, None,
        actions=[effect_plan_action(atlas_image, blank_ordinals=(2,))])
    check("没抛异常", "", raised)
    check("顶层判成功", True, field(result, "succeeded"))
    # 计划项是动作：C# 才会把它算进「这一轮执行过的动作」，特效能进基线。
    check("条目算作动作", "action", field(result, "itemKind", 0))
    message = field(result, "message", 0)
    check("消息里认得出是特效层", True, "effect layer synchronized" in message)
    check("帧数照实报", True, "frames=4" in message)
    # 这两个是这条路上最容易被漏掉的：文件夹被当资产 load、矩形没带。
    check("没把材质文件夹当资产加载", [], engine.folder_loads)
    check("图集贴图导进来了", [atlas_image], engine.imported_files)
    check("精灵按**输出帧编号**建，空帧不建",
          ["Sk2_Effect_Frame00_Sprite", "Sk2_Effect_Frame02_Sprite", "Sk2_Effect_Frame03_Sprite"],
          engine.sprite_requests)
    check("Flipbook 只建一次", 1, len(engine.flipbook_requests))
    flipbook = engine.flipbook_requests[0] if engine.flipbook_requests else {}
    check("空帧传成 None（时间照占、什么都不画）",
          [True, False, True, True],
          [sprite is not None for sprite in flipbook.get("sprites", [])])
    check("每输出帧占一格", [1, 1, 1, 1], flipbook.get("frameRuns"))
    check("帧率用计划里的（动作帧率 × 2）", 24.0, flipbook.get("fps"))
    check("这一层自己的旧资产清掉了", [STALE_EFFECT_SPRITE], engine.purged)
    # 大小写不敏感这条：待删名单里那条小写 Flipbook 路径指的就是刚建的这只，
    # 清理必须认出来（否则会连自己一起删，然后读它就报"实例为空"）。
    check("没把刚重建的 Flipbook 当旧资产删掉", True,
          ("%s/Sk2_Effect_Flipbook" % EFFECT_FOLDER).lower() in engine.assets)
finally:
    shutil.rmtree(effect_root, ignore_errors=True)
    unreal.load_asset = lambda path: object() if is_asset_path(path) else None

print()
print("全部通过。" if not failed else "失败 %d 项：%s" % (len(failed), failed))
sys.exit(1 if failed else 0)
