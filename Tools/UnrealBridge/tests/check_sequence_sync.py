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
unreal.load_asset = lambda path: object()
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


def run_sync(action_codes, detach_paths, bridge, sync_action):
    """跑一趟 main()，把落盘的结果文件读回来。

    结果协议是这一步唯一的对外契约，所以自检读的是真正写出去的 JSON，
    而不是函数返回值——写错字段名这类问题只有读文件才看得见。
    """
    root = tempfile.mkdtemp(prefix="zd-seq-sync-")
    try:
        plan_path = os.path.join(root, "plan.json")
        result_path = os.path.join(root, "result.json")
        with io.open(plan_path, "w", encoding="utf-8") as handle:
            handle.write(json.dumps({
                "protocolVersion": 1,
                "characterCode": "Misaka",
                "characterBlueprintPath": "/Game/GameActor2D/Misaka/Misaka.Misaka",
                "animMapsPath": "/Game/GameActor2D/Misaka/Misaka_AnimMaps.Misaka_AnimMaps",
                "detachSequenceObjectPaths": list(detach_paths),
                "actions": [{"actionCode": code, "displayName": code} for code in action_codes],
            }, ensure_ascii=False))

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
        module._sync_action = sync_action
        raised = ""
        try:
            module.main()
        except Exception as error:
            raised = str(error)
        finally:
            module._sync_action = original_sync_action

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
check("只写出了伪条目", 1, len(result.get("items", [])))
check("伪条目标成 diagnostic", "diagnostic", field(result, "itemKind", 0))
# 这一条正是缺陷 2 的核心：不过滤的话 C# 认为"有一个动作成功了"，
# 于是不抛异常，还会把 'orphan-sequences' 当动作码写进基线。
check("不过滤 = C# 误以为成功了一个动作", ["orphan-sequences"],
      succeeded_action_codes(result, filter_diagnostics=False))
check("按 itemKind 过滤后一个动作都没成", [],
      succeeded_action_codes(result, filter_diagnostics=True))
check("流程是被异常打断的", "exception", field(result, "failureKind"))

print("4) 真实动作条目要标成 action，别让 C# 把它一起滤掉")
result, raised = run_sync(["Click"], [], None, ok_action)
check("动作条目标成 action", "action", field(result, "itemKind", 0))
check("过滤后动作还在", ["Click"], succeeded_action_codes(result, filter_diagnostics=True))
check("没有解绑任务时整体成功", True, field(result, "succeeded"))

print("5) 异常与条目失败是两回事，两段线索都要留下")
result, raised = run_sync(["Click"], [ORPHAN], None, boom_action)
check("失败种类是异常", "exception", field(result, "failureKind"))
check("留下了异常本身", True, "create_paper_flipbook_from_sprites" in result.get("errorMessage", ""))
check("也留下了解绑失败", True, "orphan-sequences" in result.get("errorMessage", ""))
check("main() 照旧把异常抛出去", True, bool(raised))

print()
print("全部通过。" if not failed else "失败 %d 项：%s" % (len(failed), failed))
sys.exit(1 if failed else 0)
