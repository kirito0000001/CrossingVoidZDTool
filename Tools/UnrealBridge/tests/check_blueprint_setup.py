"""第六步《蓝图置入》的比较与纠偏逻辑自检。

    python Tools/UnrealBridge/tests/check_blueprint_setup.py

覆盖四个曾经真实踩到的坑：
1. 资产引用按字符串比大小写 -> 老资产叫 Defatk、规范名 DefAtk 时，
   写入其实成功了，复查却永远判成待写入，条目消不掉；
2. 目标资产不在工程里时照样报「写入成功」，属性实际被写成空，
   界面上表现为按了没反应、错误计数还是 0；
3. 写入报告和复查结果各说各话，界面同时显示「已写入 2 项」和
   「待写入 2 项」，用户无从判断到底成没成；
4. 条目一条条都写成功了、蓝图却没保存下来：复查读的是同一个进程里的 CDO，
   值当然已经等于目标，于是整趟报「已写入并复查 N 项，错误 0」，
   而离线跑的时候进程一退出改动全丢。

不进虚幻，unreal 用桩顶上。

模块顶层会调用 _run()，所以把最后那行摘掉再 exec；unreal 用桩顶上。
"""
import io
import sys
import types

import os

# 中文要经管道回到回归套件里。标准输出的编码跟着代码页走，不是 936 的机器上
# print 一句中文就 UnicodeEncodeError，退出码 1——代码明明是对的，整套回归却红了。
# 3.7 起有 reconfigure；更老的解释器（或者 stdout 被换成别的对象）退回包装流。
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
                 "apply_blueprint_setup.py")
src = io.open(P, encoding="utf-8").read().replace("\n_run()\n", "\n")

EXISTING = {
    "/game/gameactor2d/misaka/animsequences/defatk.defatk",
    "/game/assetmaterial/images/charaters/misaka/misaka-skillicon-1.misaka-skillicon-1",
}

unreal = types.ModuleType("unreal")
unreal.log_error = lambda *a, **k: None
unreal.log_warning = lambda *a, **k: None
unreal.load_object = lambda outer, path: (
    object() if path.casefold() in EXISTING else None)
sys.modules["unreal"] = unreal

module = types.ModuleType("step6")
module.__dict__["__name__"] = "step6"
exec(compile(src, P, "exec"), module.__dict__)

Report = module.Report
STATUS_UNCHANGED, STATUS_PENDING, STATUS_ERROR = (
    module.STATUS_UNCHANGED, module.STATUS_PENDING, module.STATUS_ERROR)

failed = []


def check(name, expected, actual):
    ok = expected == actual
    print(("  PASS " if ok else "  FAIL ") + name)
    if not ok:
        print("        期望 %r，实际 %r" % (expected, actual))
        failed.append(name)


SEQ = "/Game/GameActor2D/Misaka/AnimSequences/"
IMG = "/Game/AssetMaterial/ImageS/CharaterS/Misaka/"

print("1) 守备反击：资产真名 Defatk，规范名 DefAtk —— 同一个资产，不该判成差异")
r = Report()
r.add("bp.seq.DefAttackSeq", "sequence", "守备反击", "/bp", "DefAttackSeq",
      [SEQ + "Defatk.Defatk"], [SEQ + "DefAtk.DefAtk"], value_kind="object")
check("大小写不同视为无差异", STATUS_UNCHANGED, r.items[0]["status"])
check("不该报错", "", r.items[0]["errorMessage"])

print("2) 换成真正指错资产，仍要判成待写入")
r = Report()
r.add("bp.seq.DefAttackSeq", "sequence", "守备反击", "/bp", "DefAttackSeq",
      [SEQ + "Idle.Idle"], [SEQ + "DefAtk.DefAtk"], value_kind="object")
check("指向别的资产 = 待写入", STATUS_PENDING, r.items[0]["status"])

print("3) 终结技图标：目标资产不在工程里，不许假装能写")
r = Report()
r.add("bp.skill.SkillSlot3.Icon", "SkillSlot3", "技能图标", "/bp", "SkillSlot3.Icon",
      [""], [IMG + "Misaka-SkillIcon-4.Misaka-SkillIcon-4"], value_kind="object")
check("缺资产 = 错误态", STATUS_ERROR, r.items[0]["status"])
check("错误里点名缺的资产", True,
      "Misaka-SkillIcon-4" in r.items[0]["errorMessage"])
check("错误里指出该跑第三步", True, "第三步" in r.items[0]["errorMessage"])

print("4) 资产在，就正常判待写入")
r = Report()
r.add("bp.skill.SkillSlot1.Icon", "SkillSlot1", "技能图标", "/bp", "SkillSlot1.Icon",
      [""], [IMG + "Misaka-SkillIcon-1.Misaka-SkillIcon-1"], value_kind="object")
check("资产存在 = 待写入", STATUS_PENDING, r.items[0]["status"])

print("5) 文本字段照旧区分大小写")
r = Report()
r.add("bp.skill.SkillSlot1.Name", "SkillSlot1", "技能名字", "/bp", "SkillSlot1.Name",
      ["超电磁炮"], ["超电磁砲"], value_kind="text")
check("文本不同 = 待写入", STATUS_PENDING, r.items[0]["status"])

print("6) 写入报告说写了、复查还是有差异 —— 不许算进已写入")
r = Report()
r.add("bp.seq.X", "sequence", "某序列", "/bp", "X",
      [SEQ + "Idle.Idle"], [SEQ + "DefAtk.DefAtk"], value_kind="object")
confirmed = module._reconcile_applied(r, ["bp.seq.X"], {})
check("不计入已写入", [], confirmed)
check("降级成错误态", STATUS_ERROR, r.items[0]["status"])
check("说明写入没生效", True, "写入" in r.items[0]["errorMessage"])

print("7) C++ 报的失败原因要贴到条目上")
r = Report()
r.add("bp.skill.SkillSlot3.Icon", "SkillSlot3", "技能图标", "/bp", "SkillSlot3.Icon",
      [""], [IMG + "Misaka-SkillIcon-1.Misaka-SkillIcon-1"], value_kind="object")
confirmed = module._reconcile_applied(
    r, [], {"bp.skill.SkillSlot3": "failed to convert JSON value for Icon"})
check("失败原因透出来", True,
      "failed to convert JSON value for Icon" in r.items[0]["errorMessage"])
check("不计入已写入", [], confirmed)

print("8) 真的写成功了就照常计入")
r = Report()
r.add("bp.seq.Y", "sequence", "某序列", "/bp", "Y",
      [SEQ + "DefAtk.DefAtk"], [SEQ + "DefAtk.DefAtk"], value_kind="object")
check("计入已写入", ["bp.seq.Y"], module._reconcile_applied(r, ["bp.seq.Y"], {}))

print("9) 条目都写成功了、蓝图却没保存下来 —— 不许报成功")


class _WritableCdo(object):
    """写什么都收下的假 CDO：模拟「每一条字段都写成功了」。"""

    def set_editor_property(self, name, value):
        return None


save_calls = []


def _failing_save(object_path):
    save_calls.append(object_path)
    return False


_original_cdo = module._character_cdo
_original_save = module._save_asset
module._character_cdo = lambda code: _WritableCdo()
module._save_asset = _failing_save
try:
    applied, saved, failures, fatals = [], [], {}, []
    module._apply({"characterCode": "Misaka", "anti": True},
                  {"bp.anti"}, applied, saved, failures, fatals)
finally:
    module._character_cdo = _original_cdo
    module._save_asset = _original_save

check("条目本身写成功了", ["bp.anti"], applied)
check("保存确实试过一次", 1, len(save_calls))
check("没有资产落盘", [], saved)
check("保存失败记进 failures", True, bool(failures))
check("整体判错", True, bool(fatals))

# 复查读的是同一个进程里的 CDO，值已经等于目标——它天然判成「无差异」，
# 所以这条失败只能靠 failures 认出来，不能指望条目状态。
r = Report()
r.add("bp.anti", "onset", "异能角色", "/bp", "Anti", ["true"], ["true"])
check("复查看不出差异", STATUS_UNCHANGED, r.items[0]["status"])
confirmed = module._reconcile_applied(r, applied, failures)
check("不计入已写入", [], confirmed)
check("降级成错误态", STATUS_ERROR, r.items[0]["status"])
check("说明改动没落盘", True, "落盘" in r.items[0]["errorMessage"])

print()
print("全部通过。" if not failed else "失败 %d 项：%s" % (len(failed), failed))
sys.exit(1 if failed else 0)
