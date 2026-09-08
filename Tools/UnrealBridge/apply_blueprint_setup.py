"""第六步「蓝图置入」：把工具箱的目标值比对进角色蓝图和 2DInfor 数据表。

两种模式共用同一份请求载荷：
    Scan  只读，逐字段给出「当前值 / 目标值 / 是否有差异」；
    Apply 只处理 selectedStableIds 里的字段，写完保存。

差异比对留在这里而不是放到 C#，是因为写入本来就必须在这一侧完成——
FText 要保留原有的命名空间和键、图标是 TSoftObjectPtr、状态是枚举。
让读、比、写共用同一套取值和归一化，就不会出现「界面说没差异、写进去却变了」。
"""

import datetime
import json
import os
import traceback

import unreal


PROTOCOL_VERSION = 1

_PROGRESS_PATH = os.environ.get("ZD_BLUEPRINT_SETUP_PROGRESS", "")


def _progress(message, percent, detail="", indeterminate=False):
    """写一份进度快照给工具箱轮询。

    这一步在虚幻侧要跑十几秒，不回报的话工具箱的进度条整段静止，
    看起来跟卡死没区别。写失败就算了——进度回报不该影响主流程。
    """
    if not _PROGRESS_PATH:
        return
    try:
        os.makedirs(os.path.dirname(_PROGRESS_PATH), exist_ok=True)
        with open(_PROGRESS_PATH, "w", encoding="utf-8") as handle:
            json.dump({
                "message": message,
                "detail": detail,
                "percent": float(percent),
                "isIndeterminate": bool(indeterminate),
            }, handle, ensure_ascii=False)
    except Exception:
        pass

CHARACTER_ACTOR_ROOT = "/Game/GameActor2D"
TABLE_ROOT = "/Game/AssetMaterial/ExcelTexts/2DInfor"
SUB_SKILL_TABLE = TABLE_ROOT + "/2DSubSkill"
COMBO_TABLE = TABLE_ROOT + "/12SkInfor"
SUPPORT_IMAGE_TABLE = TABLE_ROOT + "/SupImage"

GROUP_NAMES = {
    "onset": "对局设置",
    "sequence": "动作序列",
    "component": "组件默认值",
    "SkillSlot1": "一技能",
    "SkillSlot2": "二技能",
    "SkillSlot3": "终结技",
    "SubSkill": "护援技",
    "Combo": "连携技",
    "supportImage": "护援头像",
}

# 技能结构 FSkillData2D 的并列字段：(请求里的键, 结构体属性名, 展示名, 取值类型)
SKILL_FIELDS = [
    ("names", "Name", "技能名字", "text"),
    ("skillNames", "SkillName", "技能真名", "text"),
    ("descriptions", "Description", "技能介绍", "text"),
    ("iconObjectPaths", "Icon", "技能图标", "object"),
    ("pointCosts", "PointCost", "Point 消耗", "int"),
    ("attackCapacities", "AttackCapacity", "攻击容量", "int"),
    ("autoPriorities", "AutoPriority", "自动优先级", "int"),
    ("skillStates", "SkillState", "技能状态", "enum"),
    ("preformTypes", "PreformType", "守备类型", "enum"),
    ("preSkillValues", "PreSkillValue", "守备数值", "float"),
    ("skillRates", "SkillRate", "等级倍率", "rate"),
]

RATE_LEVELS = ("1", "2", "3", "4", "5")

# 与 C# 的 UnrealBlueprintSetupStatus 一一对应，按整数下发，
# 免得再给结果加一个字符串枚举转换器。
STATUS_UNCHANGED = 0
STATUS_PENDING = 1
STATUS_ERROR = 2


# ---------------------------------------------------------------- 基础工具

def _text(value):
    if value is None:
        return ""
    try:
        return str(value)
    except Exception:
        return ""


def _object_path(value):
    """对象 -> 裸对象路径。None 和空引用统一成空串，便于和目标值比较。"""
    if value is None:
        return ""
    for method in ("get_path_name", "to_string"):
        try:
            text = getattr(value, method)()
            if text and text != "None":
                return _text(text)
        except Exception:
            continue
    return ""


def _load_object(object_path):
    if not object_path:
        return None
    try:
        return unreal.load_object(None, object_path)
    except Exception:
        return None


def _normalize_object_path(value):
    """`/Script/Engine.Texture2D'/Game/A.A'` 和 `/Game/A.A` 视为同一个东西。"""
    text = _text(value).strip()
    if not text or text == "None":
        return ""
    if text.endswith("'") and "'" in text[:-1]:
        text = text[text.index("'") + 1:-1]
    return text


# ---------------------------------------------------------------- 对象引用

def _object_key(value):
    """对象引用用来比较的形式：剥掉类名前缀，再统一大小写。

    Unreal 解析资产路径本来就不区分大小写，
    /Game/A/Defatk.Defatk 和 /Game/A/DefAtk.DefAtk 指的是同一个资产。
    以前这里按字符串原样比，于是资产名大小写和规范不一致的角色
    （老资产叫 Defatk、规范名是 DefAtk）会得到一条永远消不掉的待写入项：
    写入其实成功了，复查时又因为大小写判成有差异。

    资产改名是第二步规整素材、第五步序列同步的事；
    第六步只负责把引用写进蓝图，引用指到同一个资产就算到位。
    """
    return _normalize_object_path(value).casefold()


def _object_values_match(current_values, target_values):
    if len(current_values) != len(target_values):
        return False
    return all(_object_key(a) == _object_key(b)
               for a, b in zip(current_values, target_values))


def _missing_object_targets(target_values):
    """目标资产在工程里根本不存在时，挑出来。

    写一个加载不到的路径，属性最终会变成 None：写入「成功」了，
    复查却还是空——界面上就是一条按了也没反应的待写入项。
    与其假装写得进去，不如直接报错告诉用户先去补素材。
    """
    missing = []
    for value in target_values:
        path = _normalize_object_path(value)
        if path and _load_object(path) is None and path not in missing:
            missing.append(path)
    return missing


# ---------------------------------------------------------------- 结果条目

class Report(object):
    def __init__(self):
        self.items = []

    def add(self, stable_id, group_key, display_name, target_path, target_field,
            current_values, target_values, writable=True, error="", value_kind="text"):
        """记一条字段对照。

        current/target 都用列表表达「按形态并列」；单值字段传单元素列表，
        界面那边按元素个数决定铺成标签还是显示一行。

        value_kind 传 "object" 的字段按资产引用处理：比较不分大小写，
        并且会先确认目标资产真的存在。
        """
        current_values = [_text(value) for value in current_values]
        target_values = [_text(value) for value in target_values]
        if not error and writable and value_kind == "object":
            missing = _missing_object_targets(target_values)
            if missing:
                error = "目标资产不存在：" + "、".join(missing) + \
                        "。请先完成第三步同步素材，把它发布到工程里。"
        if error:
            status = STATUS_ERROR
        elif not writable:
            status = STATUS_UNCHANGED
        elif value_kind == "object":
            status = (STATUS_UNCHANGED if _object_values_match(current_values, target_values)
                      else STATUS_PENDING)
        else:
            status = STATUS_UNCHANGED if current_values == target_values else STATUS_PENDING
        self.items.append({
            "stableId": stable_id,
            "groupKey": group_key,
            "groupName": GROUP_NAMES.get(group_key, group_key),
            "displayName": display_name,
            "targetPath": target_path,
            "targetField": target_field,
            "currentSummary": "、".join(value for value in current_values if value),
            "targetSummary": "、".join(value for value in target_values if value),
            "currentValues": current_values,
            "targetValues": target_values,
            "status": status,
            "errorMessage": error,
        })

# ---------------------------------------------------------------- 数据表读写

def _read_table_row(table_path, row_name):
    """读某张表里的一行，返回行内容字典；行不存在返回 None。

    走 ZDBridge.ReadDataTableRow 而不是 DataTable.export_to_json_string：
    整表导出会把行名字段（默认就叫 "Name"）当作 FieldToSkip 传给 WriteStruct，
    而 FSkillData2D 恰好也有一个 Name 属性，于是每一行的「技能名字」导出来
    永远是空数组。拿它比对，这个字段会被永远判成待写入，写进去了也看不出变化
    ——护援技的技能名字就是这么一直同步不掉的。

    按行读还和 UpsertDataTableRow 的写入共用同一套转换器，
    读回来的形状和写进去的形状天然对得上。
    """
    if not table_path or not row_name:
        return None
    try:
        report = json.loads(_text(unreal.ZDBridgeLibrary.read_data_table_row(
            table_path, unreal.Name(row_name))) or "{}")
    except Exception:
        unreal.log_warning("ZDToolbox blueprint setup table read failed:\n" + traceback.format_exc())
        return None
    if not report.get("ok"):
        unreal.log_warning("ZDToolbox blueprint setup table read failed: {} {}".format(
            table_path, report.get("error", "")))
        return None
    return report.get("row") if report.get("found") else None


def _table_skill_values(row, request_key, json_key, form_count):
    """从数据表行里取出某个技能字段的当前值，转成和目标值同一种文本。

    行是按属性名读的，键名可能是 PascalCase 也可能被转成 camelCase，两种都认。
    """
    values = _row_field(row, json_key)
    values = values if isinstance(values, list) else []
    if request_key in ("descriptions", "names", "skillNames"):
        return [_decode_nsloctext(item) for item in values]
    if request_key == "iconObjectPaths":
        return [_normalize_object_path(item) for item in values]
    if request_key == "skillRates":
        return [_format_rate_from_json(item) for item in values]
    if request_key == "preSkillValues":
        return ["%g" % float(item or 0) for item in values]
    return [_text(item) for item in values]


def _row_field(row, json_key):
    """按属性名取值。FJsonObjectConverter 默认输出 camelCase，原名也留一手。"""
    if not isinstance(row, dict):
        return None
    if json_key in row:
        return row[json_key]
    camel = json_key[:1].lower() + json_key[1:]
    if camel in row:
        return row[camel]
    lowered = {key.lower(): value for key, value in row.items()}
    return lowered.get(json_key.lower())


def _decode_nsloctext(value):
    """NSLOCTEXT("ns", "key", "文本") -> 文本。只用于比对和展示。"""
    text = _text(value)
    if not text.startswith("NSLOCTEXT("):
        return text
    parts = []
    current = ""
    in_quote = False
    escaped = False
    for char in text[len("NSLOCTEXT("):]:
        if in_quote:
            if escaped:
                current += char
                escaped = False
            elif char == "\\":
                escaped = True
            elif char == '"':
                in_quote = False
                parts.append(current)
                current = ""
            else:
                current += char
        elif char == '"':
            in_quote = True
    return parts[2] if len(parts) >= 3 else text


def _format_rate_from_json(value):
    """把导出 JSON 里的 SkillRate 压成 `物 1:0.06/2:0.15 异 ...` 这种可比文本。"""
    if not isinstance(value, dict):
        return ""
    physical = {}
    energy = {}
    for key, target in (("PhyLVrate", physical), ("MagLVrate", energy)):
        source = _row_field(value, key)
        if not isinstance(source, dict):
            continue
        for level, entry in source.items():
            # 裸映射是 {"1": 0.06}；老的整表导出会给映射值套一层属性名。
            if isinstance(entry, dict):
                numbers = [item for item in entry.values() if isinstance(item, (int, float))]
                target[_text(level)] = float(numbers[0]) if numbers else 0.0
            elif isinstance(entry, (int, float)):
                target[_text(level)] = float(entry)
    return _format_rate(physical, energy)


def _format_rate(physical, energy):
    def part(label, values):
        return label + " " + "/".join(
            "%s:%g" % (level, float(values.get(level, 0) or 0)) for level in RATE_LEVELS)
    return part("物", physical or {}) + "  " + part("异", energy or {})


# ---------------------------------------------------------------- 蓝图 CDO 读写

def _character_cdo(code):
    generated = _load_object("{}/{}/{}.{}_C".format(CHARACTER_ACTOR_ROOT, code, code, code))
    if generated is None:
        return None
    try:
        return unreal.get_default_object(generated)
    except Exception:
        return None


def _cdo_skill_values(cdo, slot_key, request_key, property_name, value_kind):
    """从蓝图技能槽读出某字段的当前值。"""
    try:
        slot = cdo.get_editor_property(slot_key)
    except Exception:
        return []
    if slot is None:
        return []
    try:
        values = list(slot.get_editor_property(property_name) or [])
    except Exception:
        return []
    if value_kind == "text":
        return [_text(item) for item in values]
    if value_kind == "object":
        return [_object_path(item) for item in values]
    if value_kind == "enum":
        return [_enum_name(item) for item in values]
    if value_kind == "int":
        return [_text(int(item)) for item in values]
    if value_kind == "float":
        return ["%g" % float(item) for item in values]
    if value_kind == "rate":
        result = []
        for item in values:
            result.append(_format_rate(
                _rate_map(item, "PhyLVrate"), _rate_map(item, "MagLVrate")))
        return result
    return [_text(item) for item in values]


def _rate_map(rate, property_name):
    try:
        source = rate.get_editor_property(property_name)
    except Exception:
        return {}
    result = {}
    try:
        for key in source:
            result[_text(key)] = float(source[key])
    except Exception:
        pass
    return result


def _enum_name(value):
    """<E2DSkillType.NORMAL: 1> -> Normal，与请求里的 Unreal 枚举名对齐。"""
    text = _text(value).strip()
    if text.startswith("<") and text.endswith(">"):
        text = text[1:-1].strip()
        colon = text.rfind(":")
        if colon >= 0 and text[colon + 1:].strip().lstrip("-").isdigit():
            text = text[:colon].strip()
    if "::" in text:
        text = text.rsplit("::", 1)[-1]
    elif "." in text:
        text = text.rsplit(".", 1)[-1]
    # Python 侧的枚举名是全大写，请求里用的是 Unreal 的 PascalCase。
    return text.capitalize() if text.isupper() else text


# ---------------------------------------------------------------- 扫描

def _scan(request, report):
    code = _text(request.get("characterCode"))
    _progress("正在读取角色蓝图...", 10, code)
    row_name = _text(request.get("characterName"))
    blueprint_path = "{}/{}/{}.{}".format(CHARACTER_ACTOR_ROOT, code, code, code)
    cdo = _character_cdo(code)

    if cdo is None:
        report.add("bp.blueprint", "onset", "角色蓝图", blueprint_path, "",
                   [""], [blueprint_path], writable=False,
                   error="未能读取角色蓝图的类默认对象：" + blueprint_path)
        return

    # --- 对局设置
    for stable_id, property_name, display_name, target in (
            ("bp.icon1p", "Icon1P", "1P 主战头像", _text(request.get("icon1PObjectPath"))),
            ("bp.icon2p", "Icon2P", "2P 主战头像", _text(request.get("icon2PObjectPath")))):
        current = _object_path(_safe_property(cdo, property_name))
        report.add(stable_id, "onset", display_name, blueprint_path, property_name,
                   [current], [target], value_kind="object")

    anti = "true" if request.get("anti") else "false"
    current_anti = "true" if _safe_property(cdo, "Anti") else "false"
    report.add("bp.anti", "onset", "异能角色", blueprint_path, "Anti",
               [current_anti], [anti])

    # --- 动作序列
    _progress("正在比对动作序列...", 35)
    for binding in request.get("sequences", []) or []:
        property_name = _text(binding.get("propertyName"))
        targets = [_text(item) for item in binding.get("objectPaths", []) or []]
        current = [_object_path(item) for item in (_safe_property(cdo, property_name) or [])]
        # 只比对工具箱负责的形态数；蓝图里多出来的槽位不属于本次同步范围。
        current = (current + [""] * len(targets))[:len(targets)]
        report.add("bp.seq." + property_name, "sequence",
                   _text(binding.get("displayName")) or property_name,
                   blueprint_path, property_name, current, targets, value_kind="object")

    # --- 组件默认值
    _scan_component(report, cdo, blueprint_path,
                    "bp.component.animbp", "AnimationComponent", "AnimInstanceClass",
                    "动画蓝图", _text(request.get("animInstanceClassObjectPath")))
    _scan_component(report, cdo, blueprint_path,
                    "bp.component.idle", "Sprite", "SourceFlipbook",
                    "站街 Flipbook", _text(request.get("idleFlipbookObjectPath")))

    # --- 技能
    _progress("正在比对技能与数据表...", 60)
    sub_skill_row = _table_row_for(SUB_SKILL_TABLE, row_name)
    combo_row = _table_row_for(COMBO_TABLE, row_name)
    for payload in request.get("skills", []) or []:
        slot_key = _text(payload.get("slotKey"))
        if slot_key.startswith("SkillSlot"):
            _scan_blueprint_skill(report, cdo, blueprint_path, slot_key, payload)
        elif slot_key == "SubSkill":
            _scan_table_skill(report, SUB_SKILL_TABLE, sub_skill_row, row_name, slot_key, payload, "")
        elif slot_key == "Combo":
            partner = _text(payload.get("partnerName"))
            partner_row = None
            partners = _row_field(combo_row, "SubCharName")
            if isinstance(partners, dict) and partner in partners:
                entry = partners[partner]
                partner_row = _row_field(entry, "SkillSlot5Data") if isinstance(entry, dict) else None
            _scan_table_skill(report, COMBO_TABLE, partner_row, row_name, slot_key, payload, partner)

    # --- 护援头像
    _progress("正在比对护援头像...", 88)
    support_row = _table_row_for(SUPPORT_IMAGE_TABLE, row_name)
    for stable_id, json_key, display_name, target in (
            ("table.supportImage.sub1p", "Sub1P", "1P 护援头像", _text(request.get("support1PObjectPath"))),
            ("table.supportImage.sub2p", "Sub2P", "2P 护援头像", _text(request.get("support2PObjectPath")))):
        current = _normalize_object_path(_row_field(support_row, json_key))
        report.add(stable_id, "supportImage", display_name,
                   SUPPORT_IMAGE_TABLE, json_key, [current], [target],
                   value_kind="object")


def _safe_property(obj, property_name):
    try:
        return obj.get_editor_property(property_name)
    except Exception:
        return None


def _scan_component(report, cdo, blueprint_path, stable_id, component_name, property_name, display_name, target):
    component = _safe_property(cdo, component_name)
    if component is None:
        report.add(stable_id, "component", display_name, blueprint_path,
                   component_name + "." + property_name, [""], [target], writable=False,
                   error="角色蓝图上没有 " + component_name + " 组件")
        return
    current = _object_path(_safe_property(component, property_name))
    report.add(stable_id, "component", display_name, blueprint_path,
               component_name + "." + property_name, [current], [target],
               value_kind="object")


def _table_row_for(table_path, row_name):
    return _read_table_row(table_path, row_name)


def _scan_blueprint_skill(report, cdo, blueprint_path, slot_key, payload):
    for request_key, property_name, display_name, value_kind in SKILL_FIELDS:
        targets = _target_skill_values(payload, request_key, value_kind)
        current = _cdo_skill_values(cdo, slot_key, request_key, property_name, value_kind)
        current = (current + [""] * len(targets))[:len(targets)] if targets else current
        report.add("bp.skill.%s.%s" % (slot_key, property_name), slot_key,
                   display_name, blueprint_path, "%s.%s" % (slot_key, property_name),
                   current, targets, value_kind=value_kind)


def _scan_table_skill(report, table_path, row, row_name, slot_key, payload, partner):
    suffix = ("." + partner) if partner else ""
    for request_key, json_key, display_name, value_kind in SKILL_FIELDS:
        targets = _target_skill_values(payload, request_key, value_kind)
        current = _table_skill_values(row, request_key, json_key, len(targets)) if row else []
        current = (current + [""] * len(targets))[:len(targets)] if targets else current
        group_key = slot_key
        name = display_name if not partner else "%s · %s" % (partner, display_name)
        report.add("table.%s%s.%s" % (slot_key, suffix, json_key), group_key,
                   name, table_path, "%s%s.%s" % (row_name, suffix, json_key),
                   current, targets, value_kind=value_kind)


def _target_skill_values(payload, request_key, value_kind):
    values = payload.get(request_key, []) or []
    if value_kind == "rate":
        return [_format_rate(item.get("physical"), item.get("energy"))
                if isinstance(item, dict) else "" for item in values]
    if value_kind == "float":
        return ["%g" % float(item or 0) for item in values]
    if value_kind == "int":
        return [_text(int(item or 0)) for item in values]
    return [_text(item) for item in values]


# ---------------------------------------------------------------- 应用

def _apply(request, selected, applied, saved, failures):
    """按勾选写入。只动被选中的字段，其余原样不碰。"""
    if not selected:
        return

    code = _text(request.get("characterCode"))
    cdo = _character_cdo(code)
    blueprint_path = "{}/{}/{}.{}".format(CHARACTER_ACTOR_ROOT, code, code, code)
    touched_blueprint = False

    if cdo is not None:
        for stable_id, property_name, request_key in (
                ("bp.icon1p", "Icon1P", "icon1PObjectPath"),
                ("bp.icon2p", "Icon2P", "icon2PObjectPath")):
            if stable_id in selected and _set_property(
                    cdo, property_name, _load_object(_text(request.get(request_key)))):
                applied.append(stable_id)
                touched_blueprint = True

        if "bp.anti" in selected and _set_property(cdo, "Anti", bool(request.get("anti"))):
            applied.append("bp.anti")
            touched_blueprint = True

        for binding in request.get("sequences", []) or []:
            property_name = _text(binding.get("propertyName"))
            stable_id = "bp.seq." + property_name
            if stable_id not in selected:
                continue
            sequences = [_load_object(_text(item)) for item in binding.get("objectPaths", []) or []]
            if _set_property(cdo, property_name, sequences):
                applied.append(stable_id)
                touched_blueprint = True

        for stable_id, component_name, property_name, request_key in (
                ("bp.component.animbp", "AnimationComponent", "AnimInstanceClass", "animInstanceClassObjectPath"),
                ("bp.component.idle", "Sprite", "SourceFlipbook", "idleFlipbookObjectPath")):
            if stable_id not in selected:
                continue
            component = _safe_property(cdo, component_name)
            target = _load_object(_text(request.get(request_key)))
            if component is not None and _set_property(component, property_name, target):
                applied.append(stable_id)
                touched_blueprint = True

        for payload in request.get("skills", []) or []:
            slot_key = _text(payload.get("slotKey"))
            if not slot_key.startswith("SkillSlot"):
                continue
            fields = _selected_skill_json(payload, selected, "bp.skill.%s." % slot_key)
            if not fields:
                continue
            # 技能槽里的名字和介绍是 FText，交给桥接插件写，才能沿用原有的本地化键。
            report = _bridge_json(unreal.ZDBridgeLibrary.apply_json_to_struct_property(
                cdo, unreal.Name(slot_key), json.dumps(fields, ensure_ascii=False)))
            if report.get("ok"):
                applied.extend("bp.skill.%s.%s" % (slot_key, name)
                               for name in report.get("writtenFields", []))
                touched_blueprint = True
            else:
                message = _text(report.get("error", "")) or "未知原因"
                failures["bp.skill.%s" % slot_key] = message
                unreal.log_error("ZDToolbox blueprint setup skill write failed: {} {}".format(
                    slot_key, message))

    if touched_blueprint and _save_asset(blueprint_path):
        saved.append(blueprint_path)

    _apply_tables(request, selected, applied, saved)


def _bridge_json(text):
    try:
        return json.loads(_text(text) or "{}")
    except Exception:
        return {"ok": False, "error": _text(text)}


def _selected_skill_json(payload, selected, prefix):
    """挑出被勾选的技能字段，转成 ZDBridge 认识的 JSON 形状。"""
    fields = {}
    for request_key, json_key, _display, value_kind in SKILL_FIELDS:
        if prefix + json_key not in selected:
            continue
        fields[json_key] = _json_skill_values(payload, request_key, value_kind)
    return fields


def _json_skill_values(payload, request_key, value_kind):
    """
    目标值 -> FJsonObjectConverter 认识的形状。

    倍率这里要写成 {"1": 0.06} 的裸映射：数据表导出时写的是
    {"1": {"PhyLVrate": 0.06}}（导出器给映射值套了一层属性名），
    但写入走的是属性转换器，认的是裸值。读写两边形状不同，别看混。
    """
    values = payload.get(request_key, []) or []
    if value_kind == "rate":
        result = []
        for item in values:
            item = item if isinstance(item, dict) else {}
            result.append({
                "PhyLVrate": {level: float((item.get("physical") or {}).get(level, 0) or 0)
                              for level in RATE_LEVELS},
                "MagLVrate": {level: float((item.get("energy") or {}).get(level, 0) or 0)
                              for level in RATE_LEVELS},
            })
        return result
    if value_kind == "float":
        return [float(item or 0) for item in values]
    if value_kind == "int":
        return [int(item or 0) for item in values]
    return [_text(item) for item in values]


def _set_property(obj, property_name, value):
    try:
        obj.set_editor_property(property_name, value)
        return True
    except Exception:
        unreal.log_warning("ZDToolbox blueprint setup write failed: {}\n{}".format(
            property_name, traceback.format_exc()))
        return False


def _save_asset(object_path):
    package_path = object_path.split(".", 1)[0]
    try:
        return bool(unreal.EditorAssetLibrary.save_asset(package_path, only_if_is_dirty=False))
    except Exception:
        unreal.log_warning("ZDToolbox blueprint setup save failed: {}\n{}".format(
            package_path, traceback.format_exc()))
        return False


def _apply_tables(request, selected, applied, saved):
    """
    数据表按行写入，走 ZDBridge.UpsertDataTableRow。

    不能用 fill_from_json_string：FSkillData2D 自带 Name 属性，和数据表的
    行名字段同名，整表回灌会把行名覆盖成技能名字数组，27 行护援技会一次全废。
    Python 侧也没有单行写入的 API（DataTableFunctionLibrary 只有删行和整表回灌），
    所以逐行写入放在桥接插件里做。
    """
    row_name = _text(request.get("characterName"))
    if not row_name:
        return

    # 同一张表可能被多条载荷写到（连携技一个搭档一条），先按表聚合再一次写完。
    payloads = {}
    for payload in request.get("skills", []) or []:
        slot_key = _text(payload.get("slotKey"))
        if slot_key == "SubSkill":
            fields = _selected_skill_json(payload, selected, "table.SubSkill.")
            if fields:
                payloads.setdefault(SUB_SKILL_TABLE, {}).update(fields)
                _remember(applied, "table.SubSkill.", fields)
        elif slot_key == "Combo":
            partner = _text(payload.get("partnerName"))
            fields = _selected_skill_json(payload, selected, "table.Combo.%s." % partner)
            if not fields:
                continue
            combo = payloads.setdefault(COMBO_TABLE, {}).setdefault("SubCharName", {})
            combo[partner] = {
                "Skill12Index": int(payload.get("skill12Index", 0) or 0),
                "SkillSlot5Data": fields,
            }
            _remember(applied, "table.Combo.%s." % partner, fields)

    support_fields = {}
    for stable_id, json_key, request_key in (
            ("table.supportImage.sub1p", "Sub1P", "support1PObjectPath"),
            ("table.supportImage.sub2p", "Sub2P", "support2PObjectPath")):
        if stable_id in selected:
            support_fields[json_key] = _text(request.get(request_key))
            applied.append(stable_id)
    if support_fields:
        payloads[SUPPORT_IMAGE_TABLE] = support_fields

    for table_path, fields in payloads.items():
        report = _bridge_json(unreal.ZDBridgeLibrary.upsert_data_table_row(
            table_path, unreal.Name(row_name), json.dumps(fields, ensure_ascii=False)))
        if report.get("ok"):
            saved.append(table_path)
        else:
            unreal.log_error("ZDToolbox blueprint setup table write failed: {} {}".format(
                table_path, report.get("error", "")))


def _remember(applied, prefix, fields):
    applied.extend(prefix + name for name in fields)


# ---------------------------------------------------------------- 入口

def _reconcile_applied(report, applied, failures):
    """拿复查结果给写入结论纠偏，顺便把失败原因贴到对应条目上。"""
    claimed = set(applied)
    confirmed = []
    for item in report.items:
        stable_id = item["stableId"]
        reason = ""
        for prefix, message in failures.items():
            if stable_id == prefix or stable_id.startswith(prefix + "."):
                reason = message
                break
        if item["status"] == STATUS_PENDING and (stable_id in claimed or reason):
            item["status"] = STATUS_ERROR
            item["errorMessage"] = (
                "写入没有生效：" + reason if reason
                else "写入已执行，复查时这一项仍与目标不一致。")
        elif stable_id in claimed:
            confirmed.append(stable_id)
    return confirmed


def _run():
    request_path = os.environ.get("ZD_BLUEPRINT_SETUP_REQUEST", "")
    result_path = os.environ.get("ZD_BLUEPRINT_SETUP_RESULT", "")
    if not request_path or not result_path:
        raise RuntimeError("ZD_BLUEPRINT_SETUP_REQUEST/RESULT is empty.")

    with open(request_path, "r", encoding="utf-8") as handle:
        request = json.load(handle)

    result = {
        "protocolVersion": PROTOCOL_VERSION,
        "succeeded": False,
        "errorMessage": "",
        "characterCode": _text(request.get("characterCode")),
        "completedAt": "",
        "items": [],
        "appliedStableIds": [],
        "savedAssets": [],
    }

    try:
        applied = []
        saved = []
        failures = {}
        _progress("正在准备蓝图置入...", 2, _text(request.get("characterCode")), True)
        if _text(request.get("mode")).lower() == "apply":
            _apply(request, {_text(value) for value in request.get("selectedStableIds", []) or []},
                   applied, saved, failures)
        # 应用之后再扫一遍，界面上直接看到写入后的状态。
        _progress("正在复查写入结果...", 92)
        report = Report()
        _scan(request, report)
        # 以复查结果为准核对一遍：写入报告说写了、复查却还是有差异的，
        # 不许再算进「已写入」。否则界面会同时显示「已写入 2 项」和
        # 「待写入 2 项」，两个数字自相矛盾，用户根本没法判断到底成没成。
        applied = _reconcile_applied(report, applied, failures)
        result["items"] = report.items
        result["appliedStableIds"] = applied
        result["savedAssets"] = saved
        result["succeeded"] = True
        _progress("蓝图置入完成。", 100)
    except Exception:
        result["errorMessage"] = traceback.format_exc()
        unreal.log_error("ZDToolbox blueprint setup failed:\n" + result["errorMessage"])

    result["completedAt"] = datetime.datetime.now().astimezone().isoformat(timespec="seconds")
    os.makedirs(os.path.dirname(result_path), exist_ok=True)
    with open(result_path, "w", encoding="utf-8") as handle:
        json.dump(result, handle, ensure_ascii=False, indent=2)


_run()
