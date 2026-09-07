import datetime
import json
import os
import re
import traceback
import unreal

SHARED_BUFF_ICON_ROOT = "/Game/AssetMaterial/ImageS/BUFF"
SHARED_BATTLE_EFFECT_ROOT = "/Game/AssetMaterial/Sound/Pvp_Effect"
DEFAULT_TARGET_PATHS = [
    "/Game/AssetMaterial/ImageS/CharaterS",
    SHARED_BUFF_ICON_ROOT,
    SHARED_BATTLE_EFFECT_ROOT,
    "/Game/GameActor2D",
]
BASE_MATERIAL_ROOT = "/Game/AssetMaterial/ImageS/CharaterS"
CHAR_ITEM_ROOT = "/Game/ITems/CharItemS"
CHARACTER_ACTOR_ROOT = "/Game/GameActor2D"
TEAM_SELECT_ROOT = "/Game/UIWidget/2DPvpUI"
TEAM_SELECT_OBJECT_PATH = TEAM_SELECT_ROOT + "/UI_TeamSelect.UI_TeamSelect"
LINK_SKILL_LIBRARY_PATH = "/Game/BaseC/ExCordLibrary/LB_Fucs.LB_Fucs"
DEFAULT_BUFF_ICON_PATH = r"D:\BUFFatk.PNG"
PROGRESS_PATH = os.environ.get("ZD_TOOLBOX_EXPORT_PROGRESS", "")


def _write_progress(message, percent, detail="", is_indeterminate=False):
    if not PROGRESS_PATH:
        return
    try:
        os.makedirs(os.path.dirname(PROGRESS_PATH), exist_ok=True)
        temp_path = PROGRESS_PATH + ".tmp"
        with open(temp_path, "w", encoding="utf-8") as output:
            json.dump({
                "message": message,
                "detail": detail,
                "percent": float(percent),
                "isIndeterminate": bool(is_indeterminate),
                "updatedAt": datetime.datetime.now().isoformat(timespec="seconds"),
            }, output, ensure_ascii=False, indent=2)
        os.replace(temp_path, PROGRESS_PATH)
    except Exception:
        pass

SEQUENCE_ACTION_SPECS = [
    ("ClickSeq", "Click", "点击", "base", ["click"]),
    ("DeathAnim", "Death", "死亡", "base", ["death", "dead"]),
    ("DefAttackSeq", "DefAtk", "守备反击", "base", ["defatk", "defattack"]),
    ("DefeatedAnim", "Defeat", "失败", "base", ["defeat", "defeated"]),
    ("DefSeq", "Defence", "守备防御", "base", ["defence", "defense"]),
    ("DodgeSeq", "Dodge", "守备闪避", "base", ["dodge"]),
    ("", "FlyDown", "坠落", "base", ["flydown"]),
    ("", "Flying", "飞行", "base", ["flying", "fly"]),
    ("", "FlyStart", "击飞", "base", ["flystart"]),
    ("", "Idle", "站街", "base", ["idle"]),
    ("SkillSlot3", "KO", "终结技", "skill", ["ko"]),
    ("", "Land", "落地", "base", ["land"]),
    ("", "Move", "移动", "base", ["move"]),
    ("OnDamageSeq", "OnDamage", "受击", "base", ["ondm", "ondamage"]),
    ("SkillSlot1", "Sk1", "一技能", "skill", ["sk1", "skill1"]),
    ("SkillSlot2", "Sk2", "二技能", "skill", ["sk2", "skill2"]),
    ("", "StandUP", "站起", "base", ["standup", "stand"]),
    ("SkillSlot4", "Sub", "护援技", "skill", ["sub"]),
    ("VictorAnim", "Victory", "胜利", "base", ["victory", "victor"]),
]
SEQUENCE_ACTION_BY_ALIAS = {
    alias: {
        "sourceProperty": property_name,
        "actionCode": action_code,
        "displayName": display_name,
        "category": category,
    }
    for property_name, action_code, display_name, category, aliases in SEQUENCE_ACTION_SPECS
    for alias in aliases
}
SEQUENCE_ACTION_BY_CODE = {
    action_code: {
        "sourceProperty": property_name,
        "actionCode": action_code,
        "displayName": display_name,
        "category": category,
    }
    for property_name, action_code, display_name, category, _ in SEQUENCE_ACTION_SPECS
}


def _load_target_paths():
    raw = os.environ.get("ZD_TOOLBOX_TARGET_PATHS", "")
    if not raw:
        return DEFAULT_TARGET_PATHS
    try:
        values = json.loads(raw)
        return [str(value) for value in values if str(value).strip()]
    except Exception:
        unreal.log_warning("ZDToolbox target path env parse failed; fallback to defaults.")
        return DEFAULT_TARGET_PATHS


def _load_selected_character_codes():
    raw = os.environ.get("ZD_TOOLBOX_SELECTED_CHARACTERS", "")
    if not raw:
        return set()
    try:
        values = json.loads(raw)
        return {str(value).strip() for value in values if str(value).strip()}
    except Exception:
        unreal.log_warning("ZDToolbox selected character env parse failed; fallback to full export.")
        return set()


def _load_export_scope():
    return os.environ.get("ZD_TOOLBOX_EXPORT_SCOPE", "Full").strip() or "Full"


def _load_previous_manifest(path):
    if not path or not os.path.isfile(path):
        return {}
    try:
        with open(path, "r", encoding="utf-8") as source:
            return json.load(source)
    except Exception:
        unreal.log_warning("ZDToolbox previous manifest read failed; selected export will not merge old data.")
        return {}


def _character_code_from_package_path(package_path, root_path):
    package_path = _to_text(package_path).strip().rstrip("/")
    root_path = _to_text(root_path).strip().rstrip("/")
    prefix = root_path + "/"
    if not package_path.lower().startswith(prefix.lower()):
        return ""
    relative = package_path[len(prefix):].strip("/")
    if not relative:
        return ""
    return relative.split("/", 1)[0]


def _asset_character_code(asset_data):
    package_path = asset_data.get("packagePath", "")
    for root_path in DEFAULT_TARGET_PATHS:
        code = _character_code_from_package_path(package_path, root_path)
        if code:
            return code
    return ""


def _entry_character_code(entry):
    return _to_text(entry.get("code", "")).strip()


def _material_scope_kind(package_path, asset_name, asset_class, selected_codes):
    normalized_package = _to_text(package_path).strip().rstrip("/").lower()
    normalized_name = _to_text(asset_name).strip().lower()
    normalized_class = _to_text(asset_class).lower()
    if normalized_package == TEAM_SELECT_ROOT.lower() and normalized_name == "ui_teamselect":
        return "shared-foundation"
    for code in selected_codes:
        base_root = "{}/{}".format(BASE_MATERIAL_ROOT, code).lower()
        actor_root = "{}/{}".format(CHARACTER_ACTOR_ROOT, code).lower()
        if normalized_package == base_root or normalized_package.startswith(base_root + "/"):
            return "base"
        if normalized_package == actor_root + "/sound" or normalized_package.startswith(actor_root + "/sound/"):
            return "sound"
        if normalized_package == actor_root + "/buff" or normalized_package.startswith(actor_root + "/buff/"):
            if "texture" in normalized_class or "objectredirector" in normalized_class:
                return "buff"
        if normalized_package == actor_root and normalized_name in (
                code.lower(),
                "{}_animbp".format(code).lower(),
                "{}_animmaps".format(code).lower()):
            return "foundation"
        if (normalized_package == CHAR_ITEM_ROOT.lower() and
                normalized_name == "item_{}".format(code).lower()):
            return "foundation"
    return ""


def _include_material_scope_asset(asset, selected_codes):
    asset_class = _asset_class(asset)
    kind = _material_scope_kind(
        _to_text(asset.package_path),
        _to_text(asset.asset_name),
        asset_class,
        selected_codes)
    if not kind or "objectredirector" in asset_class.lower():
        return False
    if kind in ("base", "buff"):
        return "texture" in asset_class.lower()
    if kind == "sound":
        return any(value in asset_class.lower() for value in (
            "soundwave",
            "metasoundsource",
            "soundconcurrency"))
    return True


def _is_top_level_asset(asset):
    package_name = _to_text(asset.package_name).strip().rstrip("/")
    asset_name = _to_text(asset.asset_name).strip()
    return bool(package_name and asset_name and package_name.rsplit("/", 1)[-1] == asset_name)


def _manifest_asset_is_material_scope_owned(asset, selected_codes):
    return bool(_material_scope_kind(
        asset.get("packagePath", ""),
        asset.get("assetName", ""),
        asset.get("assetClass", ""),
        selected_codes))


def _merge_material_scope_manifest(previous_manifest, current_manifest, selected_codes):
    if not previous_manifest:
        return current_manifest
    old_assets = previous_manifest.get("assets", [])
    current_manifest["assets"] = [
        asset for asset in old_assets
        if not _manifest_asset_is_material_scope_owned(asset, selected_codes)
    ] + current_manifest.get("assets", [])
    for key in (
            "characterSummaries",
            "characterItems",
            "characterActors",
            "characterSequences",
            "characterBuffs",
            "linkSkillLibrary",
            "supportSkillLibrary"):
        current_manifest[key] = previous_manifest.get(key, current_manifest.get(key))
    return current_manifest


def _merge_selected_manifest(previous_manifest, current_manifest, selected_codes):
    if not selected_codes:
        return current_manifest
    if not previous_manifest:
        return current_manifest

    selected = {code.lower() for code in selected_codes if code}

    def merge_list(key, code_resolver):
        old_values = previous_manifest.get(key, [])
        new_values = current_manifest.get(key, [])
        kept = [
            item for item in old_values
            if _to_text(code_resolver(item)).strip().lower() not in selected
        ]
        kept.extend(new_values)
        return kept

    new_assets = current_manifest.get("assets", [])
    new_asset_paths = {
        _to_text(item.get("objectPath", "")).strip().lower()
        for item in new_assets
    }
    current_manifest["assets"] = [
        item for item in previous_manifest.get("assets", [])
        if (_to_text(_asset_character_code(item)).strip().lower() not in selected and
            _to_text(item.get("objectPath", "")).strip().lower() not in new_asset_paths)
    ] + new_assets
    current_manifest["characterSummaries"] = merge_list("characterSummaries", _entry_character_code)
    current_manifest["characterItems"] = merge_list("characterItems", _entry_character_code)
    current_manifest["characterActors"] = merge_list("characterActors", _entry_character_code)
    current_manifest["characterSequences"] = merge_list("characterSequences", _entry_character_code)
    current_manifest["characterBuffs"] = merge_list("characterBuffs", _entry_character_code)
    for library_key in ("linkSkillLibrary", "supportSkillLibrary"):
        previous_library = previous_manifest.get(library_key, {})
        current_library = current_manifest.get(library_key, {})
        previous_entries = previous_library.get("entries", {}) if isinstance(previous_library, dict) else {}
        current_entries = current_library.get("entries", {}) if isinstance(current_library, dict) else {}
        if isinstance(previous_entries, dict) and isinstance(current_entries, dict):
            merged_entries = dict(previous_entries)
            merged_entries.update(current_entries)
            current_library["entries"] = merged_entries
            current_library["hasData"] = len(merged_entries) > 0
            current_manifest[library_key] = current_library
    return current_manifest


def _to_text(value):
    if value is None:
        return ""
    try:
        return str(value)
    except Exception:
        return ""


_STRUCT_ADDRESS_PATTERN = re.compile(r"\s*\(0x[0-9A-Fa-f]+\)")


def _stable_class_text(value):
    """把资产类名归一成稳定文本。

    UE5 的 asset_class_path 是 TopLevelAssetPath 结构体，str() 出来长这样：
        <Struct 'TopLevelAssetPath' (0x000001C0DE94853C) {package_name: ..., asset_name: "PaperSprite"}>
    里面带对象内存地址，每次导出都不一样。这个值会进 payload 参与内容哈希，
    结果就是同一个资产每次检测都被判成「有变化」：同步前的最终比对永远不通过，
    基线也永远对不上。取 asset_name 才是稳定的类名。
    """
    if value is None:
        return ""
    asset_name = _to_text(getattr(value, "asset_name", ""))
    if asset_name:
        return asset_name
    return _STRUCT_ADDRESS_PATTERN.sub("", _to_text(value)).strip()


def _asset_class(asset_data):
    for attr in ("asset_class_path", "asset_class"):
        if hasattr(asset_data, attr):
            text = _stable_class_text(getattr(asset_data, attr))
            if text:
                return text
    return ""


def _object_path(asset_data):
    try:
        return asset_data.get_soft_object_path().to_string()
    except Exception:
        return "{}.{}".format(_to_text(asset_data.package_name), _to_text(asset_data.asset_name))


def _tags(asset_data):
    try:
        values = asset_data.tags_and_values
        result = {}
        for key in values:
            result[_to_text(key)] = _to_text(values[key])
        return result
    except Exception:
        return {}


def _is_texture_asset(asset_data):
    asset_class = _asset_class(asset_data).lower()
    return "texture" in asset_class


def _is_sound_wave_asset(asset_data):
    return "soundwave" in _asset_class(asset_data).lower()


def _safe_file_name(value):
    invalid = '<>:"/\\|?*'
    result = "".join("_" if ch in invalid or ord(ch) < 32 else ch for ch in _to_text(value)).strip()
    return result or "Asset"


def _package_file_path(package_path_or_name):
    """把 /Game/... 包路径换算成磁盘上的 .uasset 路径。"""
    name = (package_path_or_name or "").split(".", 1)[0]
    if not name.lower().startswith("/game/"):
        return ""
    relative = name[len("/game/"):].replace("/", os.sep)
    return os.path.join(unreal.Paths.project_content_dir(), relative + ".uasset")


def _png_is_up_to_date(output_path, package_name):
    """已有 PNG 比源 .uasset 新，就不必再导一次。

    一次第五步同步要跑三趟 Unreal 导出，每趟都把同一批贴图重新写一遍 PNG
    （AssetExportTask.replace_identical 还是 True，内容相同也照写）。
    绝大多数贴图两趟之间根本没动过，这一步纯属浪费。
    """
    try:
        if not os.path.isfile(output_path):
            return False
        source = _package_file_path(package_name)
        if not source or not os.path.isfile(source):
            return False
        return os.path.getmtime(output_path) >= os.path.getmtime(source)
    except OSError:
        return False


def _export_texture_png(asset_data, export_root):
    package_path = _to_text(asset_data.package_path)
    if package_path.startswith(BASE_MATERIAL_ROOT + "/"):
        source_root = BASE_MATERIAL_ROOT
        output_root_name = "Images"
    elif package_path.startswith(CHARACTER_ACTOR_ROOT + "/") and "/Material/" in package_path:
        source_root = CHARACTER_ACTOR_ROOT
        output_root_name = "SequenceFrames"
    elif package_path.startswith(CHARACTER_ACTOR_ROOT + "/") and "/buff" in package_path.lower():
        source_root = CHARACTER_ACTOR_ROOT
        output_root_name = "BuffIcons"
    else:
        return ""
    if not _is_texture_asset(asset_data):
        return ""

    try:
        asset = asset_data.get_asset()
    except Exception:
        asset = unreal.load_asset(_object_path(asset_data))
    if asset is None:
        return ""

    relative_package = package_path[len(source_root) + 1:]
    folder = os.path.join(export_root, output_root_name, *relative_package.split("/"))
    os.makedirs(folder, exist_ok=True)
    output_path = os.path.join(folder, "{}.png".format(_safe_file_name(asset_data.asset_name)))
    if _png_is_up_to_date(output_path, _to_text(asset_data.package_name)):
        return output_path

    task = unreal.AssetExportTask()
    task.object = asset
    task.filename = output_path
    task.automated = True
    task.replace_identical = True
    task.prompt = False
    task.selected = False
    try:
        task.exporter = unreal.TextureExporterPNG()
    except Exception:
        pass

    if unreal.Exporter.run_asset_export_task(task):
        return output_path if os.path.exists(output_path) else ""
    return ""


def _get_editor_property(obj, name, default=None):
    if obj is None:
        return default
    for candidate in _property_name_candidates(name):
        try:
            return obj.get_editor_property(candidate)
        except Exception:
            pass
        try:
            return getattr(obj, candidate)
        except Exception:
            pass
    return default


def _property_name_candidates(name):
    text = str(name)
    candidates = [text]
    if text:
        candidates.append(text[0].lower() + text[1:])
    snake = []
    for index, char in enumerate(text):
        if char.isupper() and index > 0 and not text[index - 1].isupper():
            snake.append("_")
        snake.append(char.lower())
    candidates.append("".join(snake))

    unique = []
    for candidate in candidates:
        if candidate and candidate not in unique:
            unique.append(candidate)
    return unique


def _text_array(values):
    result = []
    if values is None:
        return result
    try:
        for value in values:
            text = _to_text(value).strip()
            if text:
                result.append(text)
    except Exception:
        pass
    return result


def _bool_array(values):
    result = []
    if values is None:
        return result
    try:
        for value in values:
            result.append(bool(value))
    except Exception:
        pass
    return result


def _int_array(values):
    result = []
    if values is None:
        return result
    try:
        for value in values:
            try:
                result.append(int(value))
            except Exception:
                result.append(0)
    except Exception:
        pass
    return result


def _int_value(value):
    try:
        return int(value)
    except Exception:
        return 0


def _bool_value(value):
    if isinstance(value, bool):
        return value
    if isinstance(value, (int, float)):
        return value != 0
    return _to_text(value).strip().lower() in ("true", "1", "yes")


def _first_property_value(objects, names, default=None):
    for obj in objects:
        if obj is None:
            continue
        for name in names:
            value = _get_editor_property(obj, name, None)
            if value is None:
                continue
            text = _to_text(value).strip()
            if text or isinstance(value, (int, float, bool)):
                return value
    return default


def _int_property_value(objects, names, default=0):
    value = _first_property_value(objects, names, None)
    if value is None:
        return default
    try:
        return int(value)
    except Exception:
        try:
            return int(float(_to_text(value)))
        except Exception:
            return default


def _float_array(values):
    result = []
    if values is None:
        return result
    try:
        for value in values:
            try:
                result.append(float(value))
            except Exception:
                result.append(0.0)
    except Exception:
        pass
    return result


def _object_path_text(value):
    if value is None:
        return ""
    for method_name in ("get_path_name", "get_name"):
        try:
            text = getattr(value, method_name)()
            if text:
                return str(text)
        except Exception:
            pass
    try:
        path = value.get_soft_object_path()
        text = path.to_string()
        if text:
            return text
    except Exception:
        pass
    return _to_text(value)


def _object_path_array(values):
    result = []
    if values is None:
        return result
    try:
        for value in values:
            text = _object_path_text(value).strip()
            result.append(text)
    except Exception:
        pass
    return result


def _enum_text(value):
    text = _to_text(value).strip()
    if "::" in text:
        return text.rsplit("::", 1)[-1]
    if "." in text:
        return text.rsplit(".", 1)[-1]
    return text


def _buff_type_key(value):
    text = _enum_text(value)
    lowered = text.lower()
    if "last" in lowered and "def" in lowered:
        return "ReceiveFinal"
    if "last" in lowered and "atk" in lowered:
        return "SendFinal"
    if "property" in lowered and "def" in lowered:
        return "ReceiveAttr"
    if "property" in lowered and "atk" in lowered:
        return "SendAttr"
    return text


def _buff_gain_key(value):
    text = _enum_text(value)
    lowered = text.lower()
    if "weaken" in lowered or "debuff" in lowered or "reduce" in lowered or "削弱" in lowered:
        return "Weaken"
    return "Addition"


def _buff_priority_key(value):
    text = _enum_text(value)
    lowered = text.lower()
    if "low" in lowered:
        return "Low"
    if "high" in lowered:
        return "High"
    if "urgent" in lowered:
        return "Urgent"
    return "Normal"


def _enum_array(values):
    result = []
    if values is None:
        return result
    try:
        for value in values:
            result.append(_enum_text(value))
    except Exception:
        pass
    return result


def _map_to_string_float_dict(value):
    result = {}
    if value is None:
        return result
    try:
        for key in value:
            result[str(key)] = float(value[key])
        return result
    except Exception:
        pass
    try:
        for key, item in value.items():
            result[str(key)] = float(item)
    except Exception:
        pass
    return result


def _export_skill_rate(rate):
    if rate is None:
        return {"physical": {}, "energy": {}}
    return {
        "physical": _map_to_string_float_dict(_get_editor_property(rate, "PhyLVrate", {})),
        "energy": _map_to_string_float_dict(_get_editor_property(rate, "MagLVrate", {})),
    }


def _export_skill_slot(slot, slot_key="", display_name=""):
    if slot is None:
        return {
            "slotKey": slot_key,
            "displayName": display_name,
            "icons": [],
            "names": [],
            "descriptions": [],
            "pointCosts": [],
            "skillRates": [],
            "autoPriorities": [],
            "skillStates": [],
            "preformTypes": [],
            "preSkillValues": [],
            "skillNames": [],
            "attackCapacities": [],
        }

    skill_rates = []
    try:
        for rate in _get_editor_property(slot, "SkillRate", []):
            skill_rates.append(_export_skill_rate(rate))
    except Exception:
        pass

    return {
        "slotKey": slot_key,
        "displayName": display_name,
        "icons": _object_path_array(_get_editor_property(slot, "Icon", [])),
        "names": _text_array(_get_editor_property(slot, "Name", [])),
        "descriptions": _text_array(_get_editor_property(slot, "Description", [])),
        "pointCosts": _int_array(_get_editor_property(slot, "PointCost", [])),
        "skillRates": skill_rates,
        "autoPriorities": _int_array(_get_editor_property(slot, "AutoPriority", [])),
        "skillStates": _enum_array(_get_editor_property(slot, "SkillState", [])),
        "preformTypes": _enum_array(_get_editor_property(slot, "PreformType", [])),
        "preSkillValues": _float_array(_get_editor_property(slot, "PreSkillValue", [])),
        "skillNames": _text_array(_get_editor_property(slot, "SkillName", [])),
        "attackCapacities": _int_array(_get_editor_property(slot, "AttackCapacity", [])),
    }


def _find_matching_paren(text, open_index):
    depth = 0
    in_quote = False
    escaped = False
    for index in range(open_index, len(text)):
        char = text[index]
        if in_quote:
            if escaped:
                escaped = False
            elif char == "\\":
                escaped = True
            elif char == '"':
                in_quote = False
            continue

        if char == '"':
            in_quote = True
        elif char == "(":
            depth += 1
        elif char == ")":
            depth -= 1
            if depth == 0:
                return index
    return -1


def _extract_tuple_field(text, field_name):
    marker = field_name + "=("
    start = text.find(marker)
    if start < 0:
        return ""
    open_index = start + len(field_name) + 1
    close_index = _find_matching_paren(text, open_index)
    if close_index < 0:
        return ""
    return text[open_index + 1:close_index]


def _split_top_level_entries(text):
    entries = []
    depth = 0
    in_quote = False
    escaped = False
    start = -1
    for index, char in enumerate(text):
        if in_quote:
            if escaped:
                escaped = False
            elif char == "\\":
                escaped = True
            elif char == '"':
                in_quote = False
            continue

        if char == '"':
            in_quote = True
        elif char == "(":
            if depth == 0:
                start = index
            depth += 1
        elif char == ")":
            depth -= 1
            if depth == 0 and start >= 0:
                entries.append(text[start:index + 1])
                start = -1
    return entries


def _quoted_first(text):
    match = re.search(r'"([^"]*)"', text)
    return match.group(1) if match else ""


def _quoted_values(text):
    return re.findall(r'"([^"]*)"', text)


def _text_values_from_export_tuple(text):
    values = []
    index = 0
    while True:
        start = text.find("NSLOCTEXT(", index)
        if start < 0:
            break
        open_index = start + len("NSLOCTEXT")
        close_index = _find_matching_paren(text, open_index)
        if close_index < 0:
            break
        quoted = _quoted_values(text[start:close_index + 1])
        if quoted:
            values.append(quoted[-1])
        index = close_index + 1

    if values:
        return values
    return _quoted_values(text)


def _number_values_from_export_tuple(text, value_type=float):
    values = []
    for match in re.finditer(r"-?\d+(?:\.\d+)?", text):
        try:
            values.append(value_type(match.group(0)))
        except Exception:
            pass
    return values


def _enum_values_from_export_tuple(text):
    values = []
    for part in text.split(","):
        value = part.strip()
        if value:
            values.append(value)
    return values


def _object_paths_from_export_tuple(text):
    paths = []
    for value in _quoted_values(text):
        if "'" in value:
            value = value.split("'", 1)[1].rsplit("'", 1)[0]
        paths.append(value)
    return paths


def _parse_rate_map_from_export_tuple(text):
    result = {}
    for level, value in re.findall(r"\(\s*(\d+)\s*,\s*(-?\d+(?:\.\d+)?)\s*\)", text):
        try:
            result[str(level)] = float(value)
        except Exception:
            pass
    return result


def _skill_rates_from_export_tuple(text):
    rates = []
    index = 0
    while True:
        phy_marker = "PhyLVrate=("
        phy_start = text.find(phy_marker, index)
        if phy_start < 0:
            break
        phy_open = phy_start + len("PhyLVrate=")
        phy_close = _find_matching_paren(text, phy_open)
        if phy_close < 0:
            break

        mag_marker = "MagLVrate=("
        mag_start = text.find(mag_marker, phy_close)
        if mag_start < 0:
            break
        mag_open = mag_start + len("MagLVrate=")
        mag_close = _find_matching_paren(text, mag_open)
        if mag_close < 0:
            break

        rates.append({
            "physical": _parse_rate_map_from_export_tuple(text[phy_open + 1:phy_close]),
            "energy": _parse_rate_map_from_export_tuple(text[mag_open + 1:mag_close]),
        })
        index = mag_close + 1
    return rates


def _export_skill_slot_from_text(slot_text, slot_key="", display_name=""):
    return {
        "slotKey": slot_key,
        "displayName": display_name,
        "icons": _object_paths_from_export_tuple(_extract_tuple_field(slot_text, "Icon")),
        "names": _text_values_from_export_tuple(_extract_tuple_field(slot_text, "Name")),
        "descriptions": _text_values_from_export_tuple(_extract_tuple_field(slot_text, "Description")),
        "pointCosts": _number_values_from_export_tuple(_extract_tuple_field(slot_text, "PointCost"), int),
        "skillRates": _skill_rates_from_export_tuple(_extract_tuple_field(slot_text, "SkillRate")),
        "autoPriorities": _number_values_from_export_tuple(_extract_tuple_field(slot_text, "AutoPriority"), int),
        "skillStates": _enum_values_from_export_tuple(_extract_tuple_field(slot_text, "SkillState")),
        "preformTypes": _enum_values_from_export_tuple(_extract_tuple_field(slot_text, "PreformType")),
        "preSkillValues": _number_values_from_export_tuple(_extract_tuple_field(slot_text, "PreSkillValue"), float),
        "skillNames": _text_values_from_export_tuple(_extract_tuple_field(slot_text, "SkillName")),
        "attackCapacities": _number_values_from_export_tuple(_extract_tuple_field(slot_text, "AttackCapacity"), int),
    }


def _load_asset_object(asset_data):
    try:
        return asset_data.get_asset()
    except Exception:
        return unreal.load_asset(_object_path(asset_data))


def _try_get_default_object(generated_class):
    if generated_class is None:
        return None
    if callable(generated_class):
        try:
            generated_class = generated_class()
        except Exception:
            return None
    try:
        return unreal.get_default_object(generated_class)
    except Exception:
        return None


def _load_generated_class(object_path):
    paths = []
    if object_path:
        package_path = object_path.split(".", 1)[0]
        paths.append(object_path)
        paths.append(package_path)
        if "." in object_path and not object_path.endswith("_C"):
            paths.append(object_path + "_C")
            asset_package, asset_name = object_path.rsplit(".", 1)
            paths.append("{}.{}_C".format(asset_package, asset_name))
        elif not object_path.endswith("_C"):
            paths.append(object_path + "_C")
            paths.append("{}.{}_C".format(package_path, os.path.basename(package_path)))

    for path in paths:
        try:
            generated_class = unreal.EditorAssetLibrary.load_blueprint_class(path)
            if generated_class is not None:
                return generated_class
        except Exception:
            pass
        try:
            generated_class = unreal.load_object(None, path)
            if generated_class is not None:
                return generated_class
        except Exception:
            pass
        try:
            generated_class = unreal.load_class(None, path)
            if generated_class is not None:
                return generated_class
        except Exception:
            pass
    return None


def _default_object(asset, object_path=""):
    generated_class = _get_editor_property(asset, "generated_class")
    if callable(generated_class):
        try:
            generated_class = generated_class()
        except Exception:
            generated_class = None
    obj = _try_get_default_object(generated_class)
    if obj is not None:
        return obj

    generated_class = _load_generated_class(object_path)
    obj = _try_get_default_object(generated_class)
    if obj is not None:
        return obj

    return asset


def _blueprint_generated_class(asset, object_path=""):
    generated_class = _get_editor_property(asset, "generated_class")
    if callable(generated_class):
        try:
            generated_class = generated_class()
        except Exception:
            generated_class = None
    if generated_class is not None:
        return generated_class
    return _load_generated_class(object_path)


def _blueprint_variable_names(asset):
    result = []
    if asset is None:
        return result

    for property_name in ("new_variables", "NewVariables"):
        values = _get_editor_property(asset, property_name)
        if values is None:
            continue
        try:
            for item in values:
                for name_property in ("VarName", "var_name", "VariableName", "variable_name", "Name", "name"):
                    name_value = _get_editor_property(item, name_property)
                    text = _to_text(name_value).strip()
                    if text:
                        result.append(text)
                        break
        except Exception:
            pass

    try:
        function_library = unreal.BlueprintEditorLibrary
        for item in function_library.get_blueprint_variable_list(asset):
            text = _to_text(item).strip()
            if text:
                result.append(text)
    except Exception:
        pass

    unique = []
    for item in result:
        if item and item not in unique:
            unique.append(item)
    return unique


def _buff_value_sources(asset, obj, task_data, object_path):
    sources = [obj, task_data, asset]
    generated_class = _blueprint_generated_class(asset, object_path)
    cdo = _try_get_default_object(generated_class)
    if cdo is not None:
        sources.insert(0, cdo)
    return [source for source in sources if source is not None]


def _buff_int_property_value(asset, sources, names, default=0):
    value = _int_property_value(sources, names, None)
    if value is not None:
        return value

    variable_names = _blueprint_variable_names(asset)
    normalized_names = {name.lower() for requested in names for name in _property_name_candidates(requested)}
    for variable_name in variable_names:
        if variable_name.lower() not in normalized_names:
            continue
        value = _int_property_value(sources, [variable_name], None)
        if value is not None:
            return value
    return default


def _buff_condition_values(obj, key):
    try:
        conditions = obj.get_task_conditions()
    except Exception:
        return None

    normalized_key = _to_text(key).strip().lower()
    for condition in conditions:
        object_text = _object_path_text(condition).lower()
        class_name = ""
        try:
            class_name = _to_text(condition.get_class().get_name()).lower()
        except Exception:
            pass

        if normalized_key == "bcount":
            is_match = "bcount" in object_text or "buff_count" in object_text or "count" in class_name
        elif normalized_key == "bpower":
            is_match = "bpower" in object_text or "buff_power" in object_text or "power" in class_name
        else:
            is_match = normalized_key in object_text or normalized_key in class_name

        if not is_match:
            continue

        return (
            _int_property_value([condition], ["Count"], 0),
            _int_property_value([condition], ["CompletedCount", "CompleteCount"], 0),
        )

    return None


def _load_default_object(object_path):
    asset = None
    try:
        asset = unreal.load_asset(object_path)
    except Exception:
        asset = None
    if asset is not None:
        obj = _default_object(asset, object_path)
        if obj is not None:
            return obj

    generated_class = _load_generated_class(object_path)
    obj = _try_get_default_object(generated_class)
    if obj is not None:
        return obj

    package_path = object_path.split(".", 1)[0] if object_path else ""
    asset_name = object_path.rsplit(".", 1)[-1] if "." in object_path else os.path.basename(package_path)
    cdo_paths = [
        "{}.Default__{}_C".format(package_path, asset_name),
        "{}/Default__{}_C".format(package_path, asset_name),
    ]
    for cdo_path in cdo_paths:
        try:
            obj = unreal.load_object(None, cdo_path)
            if obj is not None:
                return obj
        except Exception:
            pass

    return asset


def _object_property_names(obj):
    if obj is None:
        return []
    names = []
    try:
        for prop in obj.get_class().get_properties():
            name = _to_text(prop.get_name()).strip()
            if name:
                names.append(name)
    except Exception:
        pass
    return names[:40]


def _export_item_data_from_asset(asset, object_path=""):
    if asset is None:
        return None, "asset load returned None"

    obj = _default_object(asset, object_path)
    item_data = _get_editor_property(obj, "ItemData")
    if item_data is None:
        return None, "ItemData property was not found on {}".format(type(obj).__name__)

    char_data = _get_editor_property(item_data, "CharData")
    skill_data = _get_editor_property(char_data, "SkillData", []) if char_data is not None else []
    try:
        skill_data_count = len(skill_data)
    except Exception:
        skill_data_count = 0

    return {
        "hasCharData": char_data is not None,
        "name": _to_text(_get_editor_property(item_data, "Name")),
        "description": _to_text(_get_editor_property(item_data, "Description")),
        "keywords": _text_array(_get_editor_property(item_data, "KeyWords", [])),
        "extraDescription": _text_array(_get_editor_property(item_data, "ExtraDescription", [])),
        "charData": {
            "charShapeNow": _int_value(_get_editor_property(char_data, "CharShapeNow")),
            "charShapeHas": _bool_array(_get_editor_property(char_data, "CharShapeHas", [])),
            "skillNow": _int_array(_get_editor_property(char_data, "SkillNow", [])),
            "skillHave": _bool_array(_get_editor_property(char_data, "SkillHave", [])),
            "skillLevel": _int_array(_get_editor_property(char_data, "SkillLevel", [])),
            "skillDescription": _text_array(_get_editor_property(char_data, "SkillDescription", [])),
            "skillDataCount": skill_data_count,
            "speed": _int_value(_get_editor_property(char_data, "Speed")),
            "health": _int_value(_get_editor_property(char_data, "Health")),
            "attack": _int_value(_get_editor_property(char_data, "Attack")),
            "phyDefense": _int_value(_get_editor_property(char_data, "PhyDefense")),
            "magDefense": _int_value(_get_editor_property(char_data, "MagDefense")),
            "critical": _int_value(_get_editor_property(char_data, "Critical")),
            "criticalC": _int_value(_get_editor_property(char_data, "CriticalC")),
            "synchronize": _int_value(_get_editor_property(char_data, "Synchronize")),
            "anti": _bool_value(_get_editor_property(char_data, "Anti")),
        }
    }, ""


def _export_item_data(asset_data):
    return _export_item_data_from_asset(_load_asset_object(asset_data), _object_path(asset_data))


def _export_team_select():
    try:
        asset = unreal.load_asset(TEAM_SELECT_OBJECT_PATH)
        if asset is None:
            return {
                "objectPath": TEAM_SELECT_OBJECT_PATH,
                "assetClass": "",
                "hasCharVoice": False,
                "readMessage": "asset load returned None",
            }
        obj = _default_object(asset, TEAM_SELECT_OBJECT_PATH)
        char_voice = _get_editor_property(obj, "CharVoice") if obj is not None else None
        return {
            "objectPath": TEAM_SELECT_OBJECT_PATH,
            "assetClass": _loaded_asset_class(asset),
            "hasCharVoice": char_voice is not None,
            "readMessage": "" if char_voice is not None else "CharVoice property was not found",
        }
    except Exception as error:
        return {
            "objectPath": TEAM_SELECT_OBJECT_PATH,
            "assetClass": "",
            "hasCharVoice": False,
            "readMessage": str(error),
        }


def _blueprint_parent_class(asset):
    try:
        generated_class = asset.generated_class()
        parent_class = generated_class.get_super_class() if generated_class is not None else None
        return _object_path(parent_class) if parent_class is not None else ""
    except Exception:
        return ""


def _content_path_to_disk(project_path, content_path):
    if not project_path:
        return ""
    project_dir = os.path.dirname(project_path)
    relative = content_path.strip("/")
    game_prefix = "Game/"
    if relative.startswith(game_prefix):
        relative = relative[len(game_prefix):]
    return os.path.join(project_dir, "Content", *relative.split("/"))


def _scan_registry_path(registry, content_path):
    try:
        registry.scan_paths_synchronous([content_path], force_rescan=True)
    except TypeError:
        try:
            registry.scan_paths_synchronous([content_path], True)
        except Exception:
            pass
    except Exception:
        pass


def _export_character_items_from_disk(project_path):
    disk_root = _content_path_to_disk(project_path, CHAR_ITEM_ROOT)
    if not disk_root or not os.path.isdir(disk_root):
        return []

    items = []
    for file_name in sorted(os.listdir(disk_root)):
        if not file_name.lower().endswith(".uasset"):
            continue
        asset_name = os.path.splitext(file_name)[0]
        if not asset_name.startswith("Item_"):
            continue

        object_path = "{}/{}.{}".format(CHAR_ITEM_ROOT, asset_name, asset_name)
        asset = None
        try:
            asset = unreal.load_asset(object_path)
            item_data, read_message = _export_item_data_from_asset(asset, object_path)
        except Exception:
            unreal.log_warning("ZDToolbox character item disk fallback failed: {}\n{}".format(
                object_path,
                traceback.format_exc()))
            item_data = None
            read_message = "exception while reading disk fallback"

        items.append({
            "code": asset_name[5:],
            "assetName": asset_name,
            "objectPath": object_path,
            "assetClass": _loaded_asset_class(asset),
            "parentClass": _blueprint_parent_class(asset),
            "hasItemData": item_data is not None,
            "readMessage": read_message,
            "itemData": item_data or {},
        })
    return items


def _export_character_items(registry, project_path):
    _scan_registry_path(registry, CHAR_ITEM_ROOT)
    try:
        found = registry.get_assets_by_path(
            unreal.Name(CHAR_ITEM_ROOT),
            recursive=True,
            include_only_on_disk_assets=False)
    except TypeError:
        found = registry.get_assets_by_path(unreal.Name(CHAR_ITEM_ROOT), True)

    items = []
    seen_codes = set()
    for asset in found:
        asset_name = _to_text(asset.asset_name)
        if not asset_name.startswith("Item_"):
            continue

        try:
            item_data, read_message = _export_item_data(asset)
        except Exception:
            unreal.log_warning("ZDToolbox character item export failed: {}\n{}".format(
                _object_path(asset),
                traceback.format_exc()))
            item_data = None
            read_message = "exception while reading asset registry item"

        code = asset_name[5:]
        items.append({
            "code": code,
            "assetName": asset_name,
            "objectPath": _object_path(asset),
            "assetClass": _asset_class(asset),
            "parentClass": _blueprint_parent_class(_load_asset_object(asset)),
            "hasItemData": item_data is not None,
            "readMessage": read_message,
            "itemData": item_data or {},
        })
        seen_codes.add(code.lower())

    for item in _export_character_items_from_disk(project_path):
        code = item.get("code", "").lower()
        if code in seen_codes:
            continue
        items.append(item)
        seen_codes.add(code)

    items.sort(key=lambda item: item.get("code", ""))
    return items


def _build_character_summaries(character_items):
    summaries = []
    for item in character_items:
        code = _to_text(item.get("code", "")).strip()
        item_data = item.get("itemData", {})
        display_name = _to_text(item_data.get("name", "")).strip() if isinstance(item_data, dict) else ""
        if code and display_name:
            summaries.append({
                "code": code,
                "displayName": display_name,
            })
    summaries.sort(key=lambda item: item.get("code", "").lower())
    return summaries


def _export_character_actor_data(asset, object_path, code, asset_name):
    obj = _default_object(asset, object_path)
    target_shape = _int_value(_get_editor_property(obj, "TargetShape", 1))
    slot_specs = [
        ("SkillSlot1", "一技能"),
        ("SkillSlot2", "二技能"),
        ("SkillSlot3", "终结技"),
        ("SkillSlot4", "护援技"),
    ]
    slots = {}
    has_any_slot = False
    for slot_key, display_name in slot_specs:
        slot = _get_editor_property(obj, slot_key)
        if slot is not None:
            has_any_slot = True
        slots[slot_key] = _export_skill_slot(slot, slot_key, display_name)

    return {
        "code": code,
        "assetName": asset_name,
        "objectPath": object_path,
        "hasActorData": has_any_slot,
        "readMessage": "" if has_any_slot else "SkillSlot1-4 properties were not found",
        "targetShape": max(1, target_shape),
        "skillSlots": slots,
    }


def _export_character_actors(manifest_assets):
    actors = []
    for asset_data in manifest_assets:
        package_path = asset_data.get("packagePath", "")
        asset_name = asset_data.get("assetName", "")
        if not package_path.startswith(CHARACTER_ACTOR_ROOT + "/"):
            continue
        relative = package_path[len(CHARACTER_ACTOR_ROOT) + 1:]
        if "/" in relative:
            continue
        if asset_name != relative:
            continue

        object_path = asset_data.get("objectPath", "")
        try:
            asset = unreal.load_asset(object_path)
            actors.append(_export_character_actor_data(asset, object_path, relative, asset_name))
        except Exception:
            unreal.log_warning("ZDToolbox character actor export failed: {}\n{}".format(
                object_path,
                traceback.format_exc()))
            actors.append({
                "code": relative,
                "assetName": asset_name,
                "objectPath": object_path,
                "hasActorData": False,
                "readMessage": "exception while reading character actor",
                "targetShape": 1,
                "skillSlots": {},
            })

    actors.sort(key=lambda item: item.get("code", ""))
    return actors


def _sequence_object_path_array(obj, property_name):
    values = _get_editor_property(obj, property_name, [])
    result = _object_path_array(values)
    return [value for value in result if value]


def _split_character_actor_relative_path(package_path, code):
    prefix = CHARACTER_ACTOR_ROOT + "/" + code + "/"
    if not package_path.startswith(prefix):
        return []
    relative = package_path[len(prefix):].strip("/")
    return [part for part in relative.split("/") if part]


def _sequence_asset_export_item(asset, duration_frames=1):
    return {
        "assetName": asset.get("assetName", ""),
        "assetClass": asset.get("assetClass", ""),
        "packagePath": asset.get("packagePath", ""),
        "objectPath": asset.get("objectPath", ""),
        "exportedFilePath": asset.get("exportedFilePath", ""),
        "durationFrames": max(1, int(duration_frames)),
    }


def _blank_sequence_frame_export_item(duration_frames=1):
    return {
        "assetName": "空白帧",
        "assetClass": "BlankFrame",
        "packagePath": "",
        "objectPath": "",
        "exportedFilePath": "",
        "isBlank": True,
        "durationFrames": max(1, int(duration_frames)),
    }


def _is_export_texture_asset(asset):
    return "texture" in asset.get("assetClass", "").lower()


def _normalize_sequence_name(value):
    text = os.path.splitext(str(value or "").strip())[0]
    return text.replace("-", "_")


def _strip_character_code_prefix(name, code):
    text = _normalize_sequence_name(name)
    normalized_code = str(code or "").replace("-", "_").lower()
    if not normalized_code:
        return text
    normalized_text = text.lower()
    for separator in ("_", "-"):
        prefix = normalized_code + separator
        if normalized_text.startswith(prefix):
            return text[len(prefix):]
    return text


def _parse_sequence_shape_suffix(text):
    value = _normalize_sequence_name(text)
    form_index = 1
    shape_match = re.search(r"(?:_?shape\s*0*(\d+))$", value, re.IGNORECASE)
    if shape_match:
        form_index = max(1, int(shape_match.group(1)))
        value = value[:shape_match.start()].rstrip("_-")
    return value, form_index


def _parse_sequence_trailing_form(text):
    value = _normalize_sequence_name(text)
    suffix_match = re.search(r"(?<!\d)0*([2-9]\d*)$", value)
    if not suffix_match:
        return value, 1
    return value[:suffix_match.start()].rstrip("_-"), max(1, int(suffix_match.group(1)))


def _normalize_sequence_token(value):
    return re.sub(r"[^a-z0-9]", "", str(value or "").lower())


def _known_sequence_identity(base_name, form_index):
    normalized = _normalize_sequence_token(base_name)
    if not normalized:
        return None

    spec = SEQUENCE_ACTION_BY_ALIAS.get(normalized)
    if spec is not None:
        return {
            "actionCode": spec["actionCode"],
            "displayName": spec["displayName"],
            "sourceProperty": spec["sourceProperty"],
            "category": spec["category"],
            "formIndex": form_index,
            "target": "",
        }

    trailing_base_name, trailing_form_index = _parse_sequence_trailing_form(base_name)
    trailing_normalized = _normalize_sequence_token(trailing_base_name)
    spec = SEQUENCE_ACTION_BY_ALIAS.get(trailing_normalized)
    if spec is not None:
        return {
            "actionCode": spec["actionCode"],
            "displayName": spec["displayName"],
            "sourceProperty": spec["sourceProperty"],
            "category": spec["category"],
            "formIndex": trailing_form_index,
            "target": "",
        }

    return None


def _parse_sequence_identity(name, code):
    stripped = _strip_character_code_prefix(name, code)
    base_name, form_index = _parse_sequence_shape_suffix(stripped)
    normalized = _normalize_sequence_token(base_name)
    if not normalized:
        return None

    if normalized.startswith("be12"):
        target = base_name[4:].strip("_-")
        display = "被连携"
        if target:
            display = "被连携：" + target
        return {
            "actionCode": "BE12-" + (target or base_name),
            "displayName": display,
            "sourceProperty": "BE12",
            "category": "link",
            "formIndex": form_index,
            "target": target,
        }

    if normalized.startswith("be"):
        target = base_name[2:].strip("_-")
        display = "被连携"
        if target:
            display = "被连携：" + target
        return {
            "actionCode": "BE-" + (target or base_name),
            "displayName": display,
            "sourceProperty": "BE",
            "category": "link",
            "formIndex": form_index,
            "target": target,
        }

    if normalized.startswith("12"):
        target = base_name[2:].strip("_-")
        display = "连携技"
        if target:
            display = "连携技：" + target
        return {
            "actionCode": "12-" + (target or base_name),
            "displayName": display,
            "sourceProperty": "12",
            "category": "link",
            "formIndex": form_index,
            "target": target,
        }

    known_identity = _known_sequence_identity(base_name, form_index)
    if known_identity is not None:
        return known_identity

    for segment in reversed([part for part in re.split(r"[_\s]+", base_name) if part]):
        segment_base_name, segment_form_index = _parse_sequence_shape_suffix(segment)
        known_identity = _known_sequence_identity(
            segment_base_name,
            segment_form_index if segment_form_index != 1 else form_index)
        if known_identity is not None:
            return known_identity

    return {
        "actionCode": "Other-" + base_name,
        "displayName": "其他序列：" + base_name,
        "sourceProperty": "Other",
        "category": "other",
        "formIndex": form_index,
        "target": "",
    }


def _sequence_identity_key(identity):
    return identity.get("actionCode", "")


def _sequence_bucket_key(identity):
    key = _sequence_identity_key(identity)
    if identity.get("category") == "link":
        return key
    return "{}#{}".format(key, identity.get("formIndex", 1))


def _find_sequence_bucket_key(buckets, identity):
    key = _sequence_bucket_key(identity)
    if key in buckets:
        return key

    if identity.get("category") != "link":
        return key

    source_property = identity.get("sourceProperty", "")
    target = _normalize_sequence_token(identity.get("target", ""))
    matches = []
    for candidate_key, bucket in buckets.items():
        if bucket.get("category") != "link":
            continue
        if bucket.get("sourceProperty", "") != source_property:
            continue
        candidate_target = _normalize_sequence_token(bucket.get("target", ""))
        if not target or not candidate_target or candidate_target.endswith(target) or target.endswith(candidate_target):
            matches.append(candidate_key)

    return matches[0] if len(matches) == 1 else key


def _strip_sequence_material_asset_name(name):
    value = _strip_character_code_prefix(name, "")
    value = re.sub(r"(?:_)?(?:sprite|flipbook)$", "", value, flags=re.IGNORECASE)
    value = re.sub(r"[_-]\d+$", "", value)
    return value.strip("_-")


def _parse_sequence_material_identity(asset, parts, code):
    candidates = []
    asset_name = _strip_sequence_material_asset_name(asset.get("assetName", ""))
    if asset_name:
        candidates.append(asset_name)
    if len(parts) >= 2:
        candidates.append(parts[1])

    unique_candidates = []
    for candidate in candidates:
        if candidate and candidate not in unique_candidates:
            unique_candidates.append(candidate)

    fallback = None
    for candidate in unique_candidates:
        identity = _parse_sequence_identity(candidate, code)
        if identity is None:
            continue
        if identity.get("category") != "other":
            return identity
        if fallback is None:
            fallback = identity
    return fallback


def _find_single_link_bucket_key(buckets, source_property):
    matches = [
        key for key, bucket in buckets.items()
        if bucket.get("category") == "link" and bucket.get("sourceProperty") == source_property
    ]
    return matches[0] if len(matches) == 1 else ""


def _is_legacy_active_link_material_name(asset):
    name = _strip_sequence_material_asset_name(asset.get("assetName", ""))
    token = _normalize_sequence_token(name)
    return bool(re.match(r"^[a-z]+12$", token, re.IGNORECASE))


def _sorted_sequence_frame_assets(assets):
    def sort_key(asset):
        name = asset.get("assetName", "")
        numbers = re.findall(r"\d+", name)
        number = int(numbers[-1]) if numbers else -1
        return (number < 0, number, name.lower())
    return sorted(assets, key=sort_key)


def _object_path_matches_asset(asset, object_path):
    text = str(object_path or "")
    if not text:
        return False
    asset_object_path = asset.get("objectPath", "")
    if text == asset_object_path:
        return True
    asset_name = asset.get("assetName", "")
    package_path = asset.get("packagePath", "")
    return bool(asset_name and package_path and text.endswith("{}/{}.{}".format(package_path, asset_name, asset_name)))


def _asset_path_matches(asset, value):
    text = str(value or "").strip()
    if not text:
        return False
    if _object_path_matches_asset(asset, text):
        return True
    package_name = asset.get("packageName", "")
    package_path = asset.get("packagePath", "")
    asset_name = asset.get("assetName", "")
    if package_name and text == package_name:
        return True
    if package_path and asset_name and text in (
        "{}/{}".format(package_path, asset_name),
        "{}/{}.{}".format(package_path, asset_name, asset_name),
    ):
        return True
    return False


def _find_asset_by_object_path(assets, object_path):
    for asset in assets:
        if _asset_path_matches(asset, object_path):
            return asset
    return None


def _is_export_sprite_asset(asset):
    asset_class = asset.get("assetClass", "").lower()
    asset_name = asset.get("assetName", "").lower()
    return "sprite" in asset_class or asset_name.endswith("_sprite")


def _is_export_flipbook_asset(asset):
    asset_class = asset.get("assetClass", "").lower()
    asset_name = asset.get("assetName", "").lower()
    return "flipbook" in asset_class or "flipbook" in asset_name


def _asset_package_name(asset):
    package_name = asset.get("packageName", "")
    if package_name:
        return package_name
    object_path = asset.get("objectPath", "")
    if "." in object_path:
        return object_path.split(".", 1)[0]
    return object_path


def _asset_registry_dependency_paths(asset):
    package_name = _asset_package_name(asset)
    if not package_name:
        return []

    registry = unreal.AssetRegistryHelpers.get_asset_registry()
    dependency_options = None
    try:
        dependency_options = unreal.AssetRegistryDependencyOptions()
        for property_name in (
            "include_hard_package_references",
            "include_soft_package_references",
            "include_hard_management_references",
            "include_soft_management_references",
            "include_searchable_names",
        ):
            try:
                setattr(dependency_options, property_name, True)
            except Exception:
                pass
    except Exception:
        dependency_options = None

    attempts = []
    if dependency_options is not None:
        attempts.append((unreal.Name(package_name), dependency_options))
        attempts.append((package_name, dependency_options))
    attempts.append((unreal.Name(package_name),))
    attempts.append((package_name,))

    for args in attempts:
        try:
            return [str(value) for value in registry.get_dependencies(*args) if str(value)]
        except Exception:
            pass
    return []


def _unique_assets(assets):
    result = []
    seen = set()
    for asset in assets:
        key = asset.get("objectPath", "") or "{}:{}".format(asset.get("packagePath", ""), asset.get("assetName", ""))
        if not key or key in seen:
            continue
        seen.add(key)
        result.append(asset)
    return result


def _assets_matching_paths(all_assets, paths):
    result = []
    for path in paths:
        asset = _find_asset_by_object_path(all_assets, path)
        if asset is not None:
            result.append(asset)
    return _unique_assets(result)


def _sequence_dependency_assets(sequence_assets, all_assets, max_depth=3):
    result = []
    pending = list(sequence_assets)
    visited = set()
    depth = 0
    while pending and depth < max_depth:
        next_pending = []
        for asset in pending:
            key = asset.get("objectPath", "") or _asset_package_name(asset)
            if not key or key in visited:
                continue
            visited.add(key)
            dependencies = _assets_matching_paths(all_assets, _asset_registry_dependency_paths(asset))
            for dependency in dependencies:
                dependency_key = dependency.get("objectPath", "") or _asset_package_name(dependency)
                if not dependency_key or dependency_key in visited:
                    continue
                result.append(dependency)
                next_pending.append(dependency)
        pending = next_pending
        depth += 1
    return _unique_assets(result)


def _flipbook_paths_from_anim_data_entry(entry):
    paths = []
    for property_name in ("Animation", "animation"):
        animation = _get_editor_property(entry, property_name)
        text = _object_path_text(animation).strip()
        if text:
            paths.append(text)

    for property_name in ("CompositeLayerAnimations", "composite_layer_animations"):
        layers = _get_editor_property(entry, property_name, [])
        try:
            for layer in layers:
                text = _object_path_text(layer).strip()
                if text:
                    paths.append(text)
        except Exception:
            pass
    return paths


def _sequence_anim_data_flipbook_assets(sequence_assets, all_assets):
    paths = []
    for asset in sequence_assets:
        try:
            obj = unreal.load_asset(asset.get("objectPath", ""))
        except Exception:
            obj = None
        if obj is None:
            continue

        for property_name in ("AnimData", "anim_data"):
            anim_data = _get_editor_property(obj, property_name, [])
            try:
                for entry in anim_data:
                    paths.extend(_flipbook_paths_from_anim_data_entry(entry))
            except Exception:
                pass

        for property_name in (
            "Flipbook",
            "Flipbook_DEPRECATED",
            "PaperFlipbook",
            "PaperFlipbook_DEPRECATED",
        ):
            text = _object_path_text(_get_editor_property(obj, property_name)).strip()
            if text:
                paths.append(text)

        for property_name in ("AnimDataSource", "AnimDataSource_DEPRECATED"):
            values = _get_editor_property(obj, property_name, [])
            try:
                for value in values:
                    text = _object_path_text(value).strip()
                    if text:
                        paths.append(text)
            except Exception:
                pass

    return [asset for asset in _assets_matching_paths(all_assets, paths) if _is_export_flipbook_asset(asset)]


def _collect_referenced_object_paths(value, depth=0, seen=None):
    if seen is None:
        seen = set()
    if value is None or depth > 4:
        return []

    value_id = id(value)
    if value_id in seen:
        return []
    seen.add(value_id)

    result = []
    if isinstance(value, str):
        result.extend(re.findall(r"/Game/[A-Za-z0-9_./-]+(?:\.[A-Za-z0-9_]+)?", value))
        return result
    if isinstance(value, (int, float, bool)):
        return result

    text = _object_path_text(value).strip()
    if text.startswith("/Game/"):
        result.append(text)

    try:
        if isinstance(value, dict):
            iterable = list(value.values())
        elif isinstance(value, (list, tuple, set)):
            iterable = list(value)
        else:
            iterable = None
        if iterable is not None:
            for item in iterable:
                result.extend(_collect_referenced_object_paths(item, depth + 1, seen))
            return result
    except Exception:
        pass

    try:
        for prop in value.get_class().get_properties():
            prop_name = _to_text(prop.get_name()).strip()
            if not prop_name:
                continue
            try:
                prop_value = value.get_editor_property(prop_name)
            except Exception:
                continue
            result.extend(_collect_referenced_object_paths(prop_value, depth + 1, seen))
    except Exception:
        pass
    return result


def _sequence_property_assets(sequence_assets, all_assets):
    paths = []
    for asset in sequence_assets:
        try:
            obj = unreal.load_asset(asset.get("objectPath", ""))
        except Exception:
            obj = None
        paths.extend(_collect_referenced_object_paths(obj))
    return _assets_matching_paths(all_assets, paths)


def _same_sequence_name(left, right):
    left_name = _normalize_sequence_token(_strip_sequence_material_asset_name(left.get("assetName", "")))
    right_name = _normalize_sequence_token(_strip_sequence_material_asset_name(right.get("assetName", "")))
    return bool(left_name and right_name and (left_name == right_name or left_name.endswith(right_name) or right_name.endswith(left_name)))


def _sequence_related_flipbook_assets(bucket, all_assets):
    sequence_assets = list(bucket.get("animSequences", []))
    sequence_assets.extend(_assets_matching_paths(all_assets, bucket.get("referencedSequences", [])))
    sequence_assets = _unique_assets(sequence_assets)
    if not sequence_assets:
        return []

    direct_flipbooks = _sequence_anim_data_flipbook_assets(sequence_assets, all_assets)
    if direct_flipbooks:
        # The formal PaperZD sequence's AnimData is the source of truth. Older
        # dependency/property fallbacks can discover stale Flipbooks from prior
        # syncs and inflate one action into multiple frame sets.
        return _unique_assets(direct_flipbooks)

    related_assets = []
    related_assets.extend(_sequence_dependency_assets(sequence_assets, all_assets))
    related_assets.extend(_sequence_property_assets(sequence_assets, all_assets))

    flipbooks = [asset for asset in related_assets if _is_export_flipbook_asset(asset)]
    if flipbooks:
        return _unique_assets(flipbooks)

    # Last-resort compatibility for older PaperZD assets whose registry
    # dependencies are incomplete but whose Flipbook keeps the sequence name.
    fallback = []
    for sequence_asset in sequence_assets:
        for asset in all_assets:
            if _is_export_flipbook_asset(asset) and _same_sequence_name(sequence_asset, asset):
                fallback.append(asset)
    return _unique_assets(fallback)


def _sprite_source_texture_path(sprite):
    if sprite is None:
        return ""
    for property_name in ("source_texture", "SourceTexture"):
        try:
            texture = sprite.get_editor_property(property_name)
            text = _object_path_text(texture).strip()
            if text:
                return text
        except Exception:
            pass
    return ""


def _loaded_asset_class(asset):
    if asset is None:
        return ""
    try:
        return _to_text(asset.get_class().get_name())
    except Exception:
        return ""


def _export_sound_wave(asset_data, export_root):
    package_path = _to_text(asset_data.package_path)
    if not _is_sound_wave_asset(asset_data):
        return ""
    try:
        asset = asset_data.get_asset()
    except Exception:
        asset = unreal.load_asset(_object_path(asset_data))
    if asset is None:
        return ""
    if package_path.startswith(CHARACTER_ACTOR_ROOT + "/"):
        relative_package = package_path[len(CHARACTER_ACTOR_ROOT) + 1:]
        folder = os.path.join(export_root, "Voices", *relative_package.split("/")[:-1])
    elif package_path.startswith(SHARED_BATTLE_EFFECT_ROOT):
        relative_package = package_path[len(SHARED_BATTLE_EFFECT_ROOT):].strip("/")
        folder = os.path.join(export_root, "Shared", "Audio", "BattleEffects", *relative_package.split("/")[:-1])
    else:
        return ""
    os.makedirs(folder, exist_ok=True)
    output_path = os.path.join(folder, "{}.wav".format(_safe_file_name(asset_data.asset_name)))
    task = unreal.AssetExportTask()
    task.object = asset
    task.filename = output_path
    task.automated = True
    task.replace_identical = True
    task.prompt = False
    task.selected = False
    if unreal.Exporter.run_asset_export_task(task):
        return output_path if os.path.exists(output_path) else ""
    return ""


def _flipbook_frames_per_second(asset):
    try:
        value = unreal.load_asset(asset.get("objectPath", "")).get_editor_property("frames_per_second")
        return float(value)
    except Exception:
        return 0.0


def _orphan_animation_sequences(anim_maps_asset, character_code):
    """挂在该角色动画源上、却不在其规范 AnimSequences 目录里的序列。

    PaperZD 2.2 的动画源上没有 SupportedAnimations 数组——编辑器里那份列表是按
    序列自身的 AnimSource 指针反查出来的。所以这类"串进来"的序列可能躺在项目的
    任何角落（实测有一条 /Game/ZDBridgeTest/Test_Sequence），按目录扫描永远看不到，
    必须从动画源这一侧反查。
    """
    if anim_maps_asset is None:
        return []
    library = getattr(unreal, "ZDBridgeLibrary", None)
    if library is None or not hasattr(library, "scan_animation_source"):
        return []
    try:
        report = json.loads(library.scan_animation_source(anim_maps_asset) or "{}")
    except Exception:
        unreal.log_warning("ZDToolbox: scan_animation_source failed for {}".format(character_code))
        return []

    canonical_prefix = "{}/{}/animsequences/".format(CHARACTER_ACTOR_ROOT, character_code).lower()
    orphans = []
    for item in report.get("sequences", []) or []:
        object_path = _to_text(item.get("assetPath"))
        if not object_path or object_path.lower().startswith(canonical_prefix):
            continue
        package_path = object_path.split(".", 1)[0]
        orphans.append({
            "assetName": package_path.rsplit("/", 1)[-1],
            "assetClass": _to_text(item.get("assetClass")),
            "packagePath": package_path,
            "objectPath": object_path,
            "exportedFilePath": "",
        })
    orphans.sort(key=lambda entry: entry["objectPath"].lower())
    return orphans


def _ordered_flipbook_frame_assets(flipbook_assets, texture_assets, all_assets):
    ordered_frames = []
    sprite_paths = set()
    playback_frame_count = 0
    for flipbook_asset in sorted(flipbook_assets, key=lambda item: item.get("assetName", "")):
        flipbook = unreal.load_asset(flipbook_asset.get("objectPath", ""))
        if flipbook is None:
            continue
        try:
            key_frames = flipbook.get_editor_property("key_frames")
        except Exception:
            key_frames = []
        for key_frame in key_frames:
            try:
                sprite = key_frame.get_editor_property("sprite")
            except Exception:
                sprite = None
            try:
                frame_run = max(1, int(key_frame.get_editor_property("frame_run")))
            except Exception:
                frame_run = 1

            # 一个关键帧就是一帧素材，frame_run 只是它停留多久。
            # 以前按 frame_run 把关键帧复制成多份，13 帧带时长的序列会导出成 16 项，
            # 而工具箱侧一帧一项永远是 13：两边条数对不上，多出来的那几项每次检测
            # 都会变成永远处理不掉的差异。播放总长另算，不能挤进帧列表。
            playback_frame_count += frame_run

            if sprite is None:
                ordered_frames.append(_blank_sequence_frame_export_item(frame_run))
                continue

            sprite_text = _object_path_text(sprite)
            if sprite_text:
                sprite_paths.add(sprite_text)
            texture_path = _sprite_source_texture_path(sprite)
            texture_asset = _find_asset_by_object_path(texture_assets, texture_path) or _find_asset_by_object_path(all_assets, texture_path)
            if texture_asset is None:
                continue
            ordered_frames.append(_sequence_asset_export_item(texture_asset, frame_run))
    return ordered_frames, len(sprite_paths), sorted(sprite_paths, key=str.lower), playback_frame_count


def _sequence_sound_notifies(sequence_assets, all_assets, character_code, frames_per_second, frame_count):
    result = []
    fps = frames_per_second if frames_per_second > 0 else 12.0
    character_sound_root = "{}/{}/Sound/".format(CHARACTER_ACTOR_ROOT, character_code).lower()
    for sequence_asset in sequence_assets:
        sequence_object_path = sequence_asset.get("objectPath", "")
        sequence = unreal.load_asset(sequence_object_path)
        if sequence is None:
            continue
        notifies = _get_editor_property(sequence, "AnimNotifies", []) or []
        for notify in notifies:
            try:
                class_name = _to_text(notify.get_class().get_name())
            except Exception:
                class_name = _to_text(type(notify).__name__)
            if "paperzdanimnotify_playsound" not in class_name.lower():
                continue
            sound = _get_editor_property(notify, "Sound")
            sound_object_path = _object_path_text(sound).strip()
            if not sound_object_path:
                continue
            time_seconds = float(_get_editor_property(notify, "Time", 0.0) or 0.0)
            track_index = int(_get_editor_property(notify, "TrackIndex", 0) or 0)
            frame_index = max(0, int(round(time_seconds * fps)))
            if frame_count > 0:
                frame_index = min(frame_index, frame_count - 1)
            matched = _find_asset_by_object_path(all_assets, sound_object_path)
            try:
                sound_class = _to_text(sound.get_class().get_name())
            except Exception:
                sound_class = matched.get("assetClass", "") if matched else ""
            result.append({
                "frameIndex": frame_index,
                "timeSeconds": time_seconds,
                "trackIndex": track_index,
                "soundObjectPath": sound_object_path,
                "soundAssetName": _asset_name_from_object_path(sound_object_path),
                "soundAssetClass": sound_class,
                "exportedFilePath": matched.get("exportedFilePath", "") if matched else "",
                "isCharacterVoice": sound_object_path.lower().startswith(character_sound_root),
                "sequenceObjectPath": sequence_object_path,
            })
    result.sort(key=lambda item: (item["frameIndex"], item["trackIndex"], item["soundObjectPath"]))
    return result


def _new_sequence_action_bucket(identity):
    return {
        "actionCode": identity.get("actionCode", ""),
        "displayName": identity.get("displayName", ""),
        "sourceProperty": identity.get("sourceProperty", ""),
        "category": identity.get("category", "other"),
        "target": identity.get("target", ""),
        "formIndexes": set(),
        "referencedSequences": [],
        "animSequences": [],
        "materialAssets": [],
    }


def _add_unique_text(values, value):
    text = str(value or "").strip()
    if text and text not in values:
        values.append(text)


def _build_sequence_actions(code, obj, actor_asset, actor_asset_map, manifest_assets):
    buckets = {}
    anim_sequence_assets = []
    material_assets = []
    all_actor_assets = actor_asset_map.get(code, [])
    for asset in all_actor_assets:
        parts = _split_character_actor_relative_path(asset.get("packagePath", ""), code)
        if not parts:
            continue
        if parts[0].lower() == "animsequences":
            anim_sequence_assets.append(asset)
        elif parts[0].lower() == "material":
            material_assets.append(asset)

    for property_name, action_code, display_name, category, _ in SEQUENCE_ACTION_SPECS:
        if not property_name:
            continue
        spec = SEQUENCE_ACTION_BY_CODE[action_code]
        identity = {
            "actionCode": action_code,
            "displayName": display_name,
            "sourceProperty": property_name,
            "category": category,
            "formIndex": 1,
            "target": "",
        }
        key = _sequence_bucket_key(identity)
        bucket = buckets.setdefault(key, _new_sequence_action_bucket(identity))
        for sequence_path in _sequence_object_path_array(obj, property_name):
            _add_unique_text(bucket["referencedSequences"], sequence_path)
        if bucket["referencedSequences"]:
            bucket["formIndexes"].add(1)

    for asset in anim_sequence_assets:
        identity = _parse_sequence_identity(asset.get("assetName", ""), code)
        if identity is None:
            continue
        key = _sequence_bucket_key(identity)
        bucket = buckets.setdefault(key, _new_sequence_action_bucket(identity))
        bucket["formIndexes"].add(identity.get("formIndex", 1))
        bucket["animSequences"].append(asset)

    for asset in material_assets:
        parts = _split_character_actor_relative_path(asset.get("packagePath", ""), code)
        if not parts:
            continue
        identity = _parse_sequence_material_identity(asset, parts, code)
        if identity is None:
            continue
        key = _find_sequence_bucket_key(buckets, identity)
        if identity.get("category") == "other" and _is_legacy_active_link_material_name(asset):
            link_key = _find_single_link_bucket_key(buckets, "12")
            if link_key:
                key = link_key
                identity = buckets[link_key]
        bucket = buckets.setdefault(key, _new_sequence_action_bucket(identity))
        bucket["formIndexes"].add(identity.get("formIndex", 1))
        bucket["materialAssets"].append(asset)

    result = []
    for bucket in buckets.values():
        texture_assets = [
            asset for asset in bucket["materialAssets"]
            if _is_export_texture_asset(asset)
        ]
        sprite_assets = [
            asset for asset in bucket["materialAssets"]
            if _is_export_sprite_asset(asset)
        ]
        flipbook_assets = [
            asset for asset in bucket["materialAssets"]
            if _is_export_flipbook_asset(asset)
        ]
        sequence_flipbook_assets = _sequence_related_flipbook_assets(bucket, all_actor_assets)
        playback_flipbook_assets = _unique_assets(sequence_flipbook_assets + flipbook_assets)
        ordered_frames, ordered_sprite_count, ordered_sprite_paths, fallback_playback_frames = _ordered_flipbook_frame_assets(flipbook_assets, texture_assets, all_actor_assets)
        sequence_ordered_frames, sequence_ordered_sprite_count, sequence_ordered_sprite_paths, sequence_playback_frames = _ordered_flipbook_frame_assets(
            sequence_flipbook_assets,
            texture_assets,
            all_actor_assets)
        playback_frame_count = fallback_playback_frames
        if sequence_ordered_frames:
            ordered_frames = sequence_ordered_frames
            ordered_sprite_count = sequence_ordered_sprite_count
            ordered_sprite_paths = sequence_ordered_sprite_paths
            playback_frame_count = sequence_playback_frames
        if not ordered_frames:
            ordered_frames = [_sequence_asset_export_item(asset) for asset in _sorted_sequence_frame_assets(texture_assets)]
        if playback_frame_count <= 0:
            playback_frame_count = len(ordered_frames)
        preview_frames = ordered_frames[:3]
        frames_per_second = _flipbook_frames_per_second(playback_flipbook_assets[0]) if playback_flipbook_assets else 0.0
        sound_notifies = _sequence_sound_notifies(
            bucket["animSequences"],
            all_actor_assets + [
                asset for asset in manifest_assets
                if asset.get("packagePath", "").startswith(SHARED_BATTLE_EFFECT_ROOT)
            ],
            code,
            frames_per_second,
            # 通知定位按播放时间算，要的是展开后的总帧数，不是关键帧条数。
            playback_frame_count)
        has_data = bool(bucket["referencedSequences"] or bucket["animSequences"] or texture_assets or sprite_assets or playback_flipbook_assets)
        result.append({
            "actionCode": bucket["actionCode"],
            "displayName": bucket["displayName"],
            "sourceProperty": bucket["sourceProperty"],
            "category": bucket["category"],
            "target": bucket["target"],
            "hasData": has_data,
            "formIndexes": sorted(bucket["formIndexes"]),
            "referencedSequences": bucket["referencedSequences"],
            "animSequences": [_sequence_asset_export_item(asset) for asset in sorted(bucket["animSequences"], key=lambda item: item.get("assetName", ""))],
            "textureCount": len(ordered_frames),
            "spriteCount": max(len(sprite_assets), ordered_sprite_count),
            "flipbookCount": len(playback_flipbook_assets),
            "flipbookPaths": [asset.get("objectPath", "") for asset in playback_flipbook_assets],
            "orderedSpritePaths": ordered_sprite_paths,
            "framesPerSecond": frames_per_second,
            # 该动作在 Unreal 里实际占用的全部资产（Material 目录 + AnimSequences）。
            # orderedFrames 只反查得到 Flipbook 关键帧用到的贴图，
            # 断了引用的旧 Sprite、旧 Flipbook 不在里面，检测就永远看不到该删的东西。
            "ownedAssets": [
                _sequence_asset_export_item(asset)
                for asset in _unique_assets(list(bucket["materialAssets"]) + list(bucket["animSequences"]))
            ],
            "orderedFrames": ordered_frames,
            "previewFrames": preview_frames,
            "soundNotifies": sound_notifies,
        })

    category_order = {"base": 0, "skill": 1, "link": 2, "other": 3}
    result.sort(key=lambda item: (
        category_order.get(item.get("category", "other"), 9),
        item.get("displayName", ""),
        item.get("actionCode", "")))
    return result


def _export_character_sequences(character_actors, manifest_assets, project_path):
    actor_asset_map = {}
    for asset in manifest_assets:
        package_path = asset.get("packagePath", "")
        if not package_path.startswith(CHARACTER_ACTOR_ROOT + "/"):
            continue
        relative = package_path[len(CHARACTER_ACTOR_ROOT) + 1:]
        code = relative.split("/", 1)[0] if relative else ""
        if not code:
            continue
        actor_asset_map.setdefault(code, []).append(asset)

    sequences = []
    for actor in character_actors:
        code = actor.get("code", "")
        if not code:
            continue

        actor_object_path = actor.get("objectPath", "")
        anim_maps_object_path = "{root}/{code}/{code}_AnimMaps.{code}_AnimMaps".format(
            root=CHARACTER_ACTOR_ROOT,
            code=code)
        anim_maps_asset = unreal.load_asset(anim_maps_object_path)
        actor_asset = unreal.load_asset(actor_object_path) if actor_object_path else None
        obj = _default_object(actor_asset, actor_object_path) if actor_asset is not None else None
        actions = _build_sequence_actions(code, obj, actor_asset, actor_asset_map, manifest_assets)
        anim_sequence_count = sum(len(action.get("animSequences", [])) for action in actions)
        frame_texture_count = sum(int(action.get("textureCount", 0)) for action in actions)
        has_data = anim_maps_asset is not None or anim_sequence_count > 0 or frame_texture_count > 0
        read_message = ""
        if anim_maps_asset is None:
            disk_path = _content_path_to_disk(project_path, "{}/{}/{}_AnimMaps".format(CHARACTER_ACTOR_ROOT, code, code)) + ".uasset"
            read_message = "未找到 AnimMaps 资产：{}".format(disk_path)

        sequences.append({
            "code": code,
            "animMapsObjectPath": anim_maps_object_path,
            "hasAnimMaps": anim_maps_asset is not None,
            "orphanSequences": _orphan_animation_sequences(anim_maps_asset, code),
            "hasData": has_data,
            "readMessage": read_message,
            "animSequenceCount": anim_sequence_count,
            "frameTextureCount": frame_texture_count,
            "actions": actions,
        })

    sequences.sort(key=lambda item: item.get("code", ""))
    return sequences


def _class_name_chain(obj):
    names = []
    try:
        cls = obj.get_class()
    except Exception:
        cls = None
    while cls is not None and len(names) < 16:
        try:
            name = _to_text(cls.get_name()).strip()
        except Exception:
            name = _to_text(cls).strip()
        if name:
            names.append(name)
        try:
            cls = cls.get_super_class()
        except Exception:
            break
    return names


def _is_dream_task_object(obj):
    return any("dreamtask" in name.lower() for name in _class_name_chain(obj))


def _buff_asset_disk_path(project_path, object_path):
    if not project_path or not object_path:
        return ""
    package_path = object_path.split(".", 1)[0]
    return _content_path_to_disk(project_path, package_path) + ".uasset"


def _is_dream_task_buff_by_disk(project_path, object_path):
    disk_path = _buff_asset_disk_path(project_path, object_path)
    if not disk_path or not os.path.isfile(disk_path):
        return False
    try:
        data = open(disk_path, "rb").read()
    except Exception:
        return False
    markers = [
        b"DreamTask",
        b"DreamGameplayTask",
        b"BCount",
        b"BPower",
        b"TaskData",
        b"TaskDisplayName",
        b"TaskDesc",
    ]
    if any(marker in data for marker in markers):
        return True
    try:
        text = data.decode("utf-16le", errors="ignore")
    except Exception:
        return False
    return any(marker.decode("ascii") in text for marker in markers)


def _localized_text(value):
    text = _to_text(value).strip()
    if not text:
        return ""
    if "NSLOCTEXT" in text:
        quoted = _quoted_values(text)
        if quoted:
            return quoted[-1]
    return text


def _export_texture_object_png(texture, object_path, export_root):
    if texture is None:
        return ""

    asset_name = _safe_file_name(_asset_name_from_object_path(object_path) or _to_text(_get_editor_property(texture, "Name")) or "BuffIcon")
    folder = os.path.join(export_root, "BuffIcons", "Direct")
    if object_path.startswith(CHARACTER_ACTOR_ROOT + "/"):
        package_path = object_path.split(".", 1)[0]
        relative_package = package_path[len(CHARACTER_ACTOR_ROOT) + 1:]
        folder = os.path.join(export_root, "BuffIcons", *relative_package.split("/")[:-1])
    os.makedirs(folder, exist_ok=True)
    output_path = os.path.join(folder, "{}.png".format(asset_name))

    task = unreal.AssetExportTask()
    task.object = texture
    task.filename = output_path
    task.automated = True
    task.replace_identical = True
    task.prompt = False
    task.selected = False
    try:
        task.exporter = unreal.TextureExporterPNG()
    except Exception:
        pass

    if unreal.Exporter.run_asset_export_task(task):
        return output_path if os.path.exists(output_path) else ""
    return ""


def _asset_name_from_object_path(object_path):
    text = _to_text(object_path).strip()
    if not text:
        return ""
    if "." in text:
        return text.rsplit(".", 1)[-1]
    return text.rsplit("/", 1)[-1]


def _find_exported_asset_for_object_path(manifest_assets, object_path):
    normalized = _normalize_object_path_text(object_path)
    if not normalized:
        return None
    asset_name = _asset_name_from_object_path(normalized)
    for asset in manifest_assets:
        if _normalize_object_path_text(asset.get("objectPath", "")) == normalized:
            return asset
    for asset in manifest_assets:
        if asset_name and asset.get("assetName", "").lower() == asset_name.lower():
            return asset
    return None


def _normalize_object_path_text(value):
    return _to_text(value).strip().replace("\\", "/")


def _resolve_buff_icon(obj, asset, buff_asset, manifest_assets, export_root):
    task_data = _get_editor_property(obj, "TaskData")
    icon = _get_editor_property(task_data, "TaskIcon") if task_data is not None else None
    icon_object_path = _object_path_text(icon).strip() if icon is not None else ""
    icon_exported_path = ""
    if icon_object_path:
        matched = _find_exported_asset_for_object_path(manifest_assets, icon_object_path)
        if matched is not None:
            icon_exported_path = matched.get("exportedFilePath", "")
        if not icon_exported_path:
            icon_exported_path = _export_texture_object_png(icon, icon_object_path, export_root)

    if not icon_object_path:
        package_path = buff_asset.get("packagePath", "")
        asset_name = buff_asset.get("assetName", "")
        candidates = []
        for candidate in manifest_assets:
            if candidate.get("packagePath", "") != package_path:
                continue
            candidate_name = candidate.get("assetName", "")
            if candidate_name.lower() in (
                "{}_icon".format(asset_name).lower(),
                "{}icon".format(asset_name).lower(),
                "{}-icon".format(asset_name).lower()):
                candidates.append(candidate)
        if not candidates:
            for candidate in manifest_assets:
                if candidate.get("packagePath", "") == package_path and "_icon" in candidate.get("assetName", "").lower():
                    candidates.append(candidate)
        if candidates:
            matched = candidates[0]
            icon_object_path = matched.get("objectPath", "")
            icon_exported_path = matched.get("exportedFilePath", "")

    if not icon_exported_path and os.path.isfile(DEFAULT_BUFF_ICON_PATH):
        icon_object_path = icon_object_path or DEFAULT_BUFF_ICON_PATH
        icon_exported_path = DEFAULT_BUFF_ICON_PATH

    return icon_object_path, _asset_name_from_object_path(icon_object_path), icon_exported_path


def _condition_summary(obj):
    condition = _get_editor_property(obj, "TaskCompletedCondition")
    if condition is None:
        return ""

    parts = []
    for name in ("ConditionDisplayName", "ConditionDesc", "CompletionMode"):
        value = _localized_text(_get_editor_property(condition, name))
        if value:
            parts.append(value)
    for name in ("Count", "CompletedCount", "CompleteCount"):
        value = _get_editor_property(condition, name)
        if value is not None:
            parts.append("{}={}".format(name, _to_text(value)))
    return " / ".join(parts)


def _export_buff_data(buff_asset, manifest_assets, export_root, project_path):
    asset_name = buff_asset.get("assetName", "")
    object_path = buff_asset.get("objectPath", "")
    try:
        asset = unreal.load_asset(object_path)
        obj = _default_object(asset, object_path)
        class_matched = _is_dream_task_object(obj)
        disk_matched = _is_dream_task_buff_by_disk(project_path, object_path)
        if not class_matched and not disk_matched:
            return None

        task_data = _get_editor_property(obj, "TaskData")
        value_sources = _buff_value_sources(asset, obj, task_data, object_path)
        task_name = _localized_text(_first_property_value(value_sources, ["TaskName"], ""))
        display_name = _localized_text(_first_property_value(value_sources, ["TaskDisplayName"], ""))
        description = _localized_text(_first_property_value(value_sources, ["TaskDesc"], ""))
        damage_type = _buff_type_key(_first_property_value(value_sources, ["TaskType", "BuffType", "DamageType"], ""))
        gain_type = _buff_gain_key(_first_property_value([task_data, obj], ["TaskType", "BuffGainType", "GainType"], ""))
        task_priority = _buff_priority_key(_first_property_value(value_sources, ["TaskPriority", "Priority"], ""))
        count_values = _buff_condition_values(obj, "BCount")
        power_values = _buff_condition_values(obj, "BPower")
        count = count_values[0] if count_values is not None else _buff_int_property_value(asset, value_sources, ["BCount", "Count", "Stack", "Stacks"], 0)
        complete_count = count_values[1] if count_values is not None and count_values[1] > 0 else _buff_int_property_value(asset, value_sources, ["CompleteCount", "CompletedCount", "BCompleteCount", "MaxCount", "MaxStack", "MaxStacks"], 0)
        power = power_values[0] if power_values is not None else _buff_int_property_value(asset, value_sources, ["BPower", "Power", "Strength"], 0)
        complete_power = power_values[1] if power_values is not None and power_values[1] > 0 else _buff_int_property_value(asset, value_sources, ["CompletePower", "BCompletePower", "MaxPower", "MaxStrength"], 0)
        icon_object_path, icon_asset_name, icon_exported_file_path = _resolve_buff_icon(
            obj,
            asset,
            buff_asset,
            manifest_assets,
            export_root)
        read_messages = []
        if not display_name and not task_name:
            read_messages.append("名称未读取")
        if not description:
            read_messages.append("说明未读取")
        if not icon_exported_file_path:
            read_messages.append("图标未读取")
        if count_values is None:
            read_messages.append("层数条件未读取")
        if power_values is None:
            read_messages.append("强度条件未读取")
        if not class_matched and disk_matched and not task_name and count_values is None and power_values is None:
            read_messages.append("按 BUFF 资产特征识别")

        return {
            "assetName": asset_name,
            "objectPath": object_path,
            "hasReadableData": True,
            "readMessage": "；".join(read_messages),
            "taskName": task_name or asset_name,
            "displayName": display_name,
            "description": description,
            "damageType": damage_type,
            "gainType": gain_type,
            "taskPriority": task_priority,
            "count": count,
            "completeCount": complete_count,
            "power": power,
            "completePower": complete_power,
            "triggerTiming": "",
            "conditionSummary": _condition_summary(obj),
            "iconObjectPath": icon_object_path,
            "iconAssetName": icon_asset_name,
            "iconExportedFilePath": icon_exported_file_path,
        }
    except Exception:
        unreal.log_warning("ZDToolbox buff export failed: {}\n{}".format(
            object_path,
            traceback.format_exc()))
        return {
            "assetName": asset_name,
            "objectPath": object_path,
            "hasReadableData": False,
            "readMessage": "exception while reading DreamTask buff",
            "taskName": asset_name,
            "displayName": "",
            "description": "",
            "damageType": "",
            "gainType": "",
            "taskPriority": "",
            "count": 0,
            "completeCount": 3,
            "power": 1,
            "completePower": 10,
            "triggerTiming": "",
            "conditionSummary": "",
            "iconObjectPath": "",
            "iconAssetName": "",
            "iconExportedFilePath": DEFAULT_BUFF_ICON_PATH if os.path.isfile(DEFAULT_BUFF_ICON_PATH) else "",
        }


def _export_character_buffs(character_actors, manifest_assets, export_root, project_path):
    asset_map = {}
    for asset in manifest_assets:
        package_path = asset.get("packagePath", "")
        if not package_path.startswith(CHARACTER_ACTOR_ROOT + "/"):
            continue
        relative = package_path[len(CHARACTER_ACTOR_ROOT) + 1:]
        code = relative.split("/", 1)[0] if relative else ""
        if not code:
            continue
        asset_map.setdefault(code, []).append(asset)

    result = []
    for actor in character_actors:
        code = actor.get("code", "")
        if not code:
            continue
        buff_root = "{}/{}/BUFF".format(CHARACTER_ACTOR_ROOT, code)
        buff_assets = []
        for asset in asset_map.get(code, []):
            package_path = asset.get("packagePath", "")
            if package_path.lower() != buff_root.lower():
                continue
            asset_class = asset.get("assetClass", "").lower()
            if "texture" in asset_class:
                continue
            buff_data = _export_buff_data(asset, manifest_assets, export_root, project_path)
            if buff_data is not None:
                buff_assets.append(buff_data)

        buff_assets.sort(key=lambda item: item.get("assetName", ""))
        result.append({
            "code": code,
            "buffFolderObjectPath": buff_root,
            "hasData": len(buff_assets) > 0,
            "readMessage": "" if buff_assets else "未读取到 DreamTask BUFF 蓝图",
            "buffs": buff_assets,
        })

    result.sort(key=lambda item: item.get("code", ""))
    return result


def _map_items(value):
    if value is None:
        return []
    try:
        return list(value.items())
    except Exception:
        pass
    try:
        return [(key, value[key]) for key in value]
    except Exception:
        return []


def _export_link_skill_library():
    object_path = LINK_SKILL_LIBRARY_PATH
    try:
        obj = _load_default_object(object_path)
        skill_map = _get_editor_property(obj, "SubCharName")
        if skill_map is None:
            skill_map = _get_editor_property(obj, "Skill12Maps")
        if skill_map is None:
            obj_type = type(obj).__name__ if obj is not None else "None"
            property_names = _object_property_names(obj)
            return {
                "objectPath": object_path,
                "hasData": False,
                "readMessage": "SubCharName/Skill12Maps property was not found on {}; properties: {}".format(
                    obj_type,
                    ", ".join(property_names)),
                "entries": {},
            }

        entries = {}
        for key, value in _map_items(skill_map):
            support_code = str(key)
            entries[support_code] = {
                "supportCharacterCode": support_code,
                "skill12Index": _int_value(_get_editor_property(value, "Skill12Index")),
                "skillSlot5Data": _export_skill_slot(
                    _get_editor_property(value, "SkillSlot5Data"),
                    "SkillSlot5Data",
                    "连携技"),
            }

        return {
            "objectPath": object_path,
            "hasData": True,
            "readMessage": "",
            "entries": entries,
        }
    except Exception:
        unreal.log_warning("ZDToolbox link skill library export failed: {}\n{}".format(
            object_path,
            traceback.format_exc()))
        return {
            "objectPath": object_path,
            "hasData": False,
            "readMessage": "exception while reading link skill library",
            "entries": {},
        }


def _export_link_skill_library_from_text(project_path):
    disk_path = _content_path_to_disk(project_path, LINK_SKILL_LIBRARY_PATH.split(".", 1)[0])
    disk_path += ".uasset"
    if not disk_path or not os.path.isfile(disk_path):
        return {
            "objectPath": LINK_SKILL_LIBRARY_PATH,
            "hasData": False,
            "readMessage": "LB_Fucs.uasset was not found on disk",
            "entries": {},
        }

    try:
        text = open(disk_path, "rb").read().decode("utf-16le", errors="ignore")
        start = text.find("((\"")
        sub_index = text.find("SubCharName=(", start)
        if start < 0 or sub_index < 0:
            return {
                "objectPath": LINK_SKILL_LIBRARY_PATH,
                "hasData": False,
                "readMessage": "text fallback could not find SubCharName export text",
                "entries": {},
            }

        close_index = _find_matching_paren(text, start)
        if close_index < 0:
            return {
                "objectPath": LINK_SKILL_LIBRARY_PATH,
                "hasData": False,
                "readMessage": "text fallback could not match exported map parentheses",
                "entries": {},
            }

        entries = {}
        for main_entry in _split_top_level_entries(text[start + 1:close_index]):
            main_name = _quoted_first(main_entry)
            sub_text = _extract_tuple_field(main_entry, "SubCharName")
            if not main_name or not sub_text:
                continue
            for sub_entry in _split_top_level_entries(sub_text):
                support_name = _quoted_first(sub_entry)
                slot_text = _extract_tuple_field(sub_entry, "SkillSlot5Data")
                if not support_name or not slot_text:
                    continue
                skill_index = 0
                index_match = re.search(r"Skill12Index\s*=\s*(-?\d+)", sub_entry)
                if index_match:
                    skill_index = int(index_match.group(1))
                key = "{}::{}::{}".format(main_name, support_name, skill_index)
                entries[key] = {
                    "mainCharacterName": main_name,
                    "supportCharacterCode": support_name,
                    "skill12Index": skill_index,
                    "skillSlot5Data": _export_skill_slot_from_text(
                        slot_text,
                        "SkillSlot5Data",
                        "连携技"),
                }

        return {
            "objectPath": LINK_SKILL_LIBRARY_PATH,
            "hasData": len(entries) > 0,
            "readMessage": "" if entries else "text fallback found no link skill entries",
            "entries": entries,
        }
    except Exception:
        unreal.log_warning("ZDToolbox link skill library text fallback failed: {}\n{}".format(
            disk_path,
            traceback.format_exc()))
        return {
            "objectPath": LINK_SKILL_LIBRARY_PATH,
            "hasData": False,
            "readMessage": "exception while parsing link skill library text",
            "entries": {},
        }


def _is_probably_character_name(value):
    text = (value or "").strip()
    if not text:
        return False
    if "[" in text and "]" in text:
        return True
    return text in ("晓古城", "游佐惠美", "桐乃", "黑雪姬", "御坂美琴", "里见莲太郎", "夏娜", "白井黑子", "逢坂大河", "司波达也", "司波深雪", "姬柊雪菜", "初音未来")


def _is_probably_skill_title(value):
    text = (value or "").strip()
    if not text:
        return False
    if len(text) > 30:
        return False
    if "[" in text and "]" in text:
        return False
    if any(marker in text for marker in ("伤害", "技能", "暂无", "测试", "Game/", "AssetMaterial")):
        return False
    if _is_support_field_noise(text):
        return False
    return any("\u4e00" <= char <= "\u9fff" for char in text)


def _is_mojibake_run(value):
    text = (value or "").strip()
    if not text:
        return True
    mojibake_markers = set("愀攀挀椀洀渀漀爀猀琀甀倀匀一嘀栀欀氀砀")
    chinese_count = sum(1 for char in text if "\u4e00" <= char <= "\u9fff")
    mojibake_count = sum(1 for char in text if char in mojibake_markers)
    return chinese_count > 0 and mojibake_count / max(1, chinese_count) > 0.55


def _clean_support_text_value(value):
    text = (value or "").strip("\x00 \t\r\n")
    return text if text and not _is_mojibake_run(text) else ""


def _is_support_field_noise(value):
    text = (value or "").strip()
    if not text:
        return True
    if text in ("伀", "吀", "堀吀", "嬀", "崀", "卿", "鉎", "鉔", "祎", "祧葲", "轗", "聎", "聣", "舀", "縀", "昰桫", "祥該", "嬀耀", "栀卑", "孚匀", "伀崀", "伀开", "孚谀"):
        return True
    if len(text) <= 1 and text != "略":
        return True
    return _is_mojibake_run(text)


def _is_probably_skill_description(value):
    text = (value or "").strip()
    if not text:
        return False
    return any(marker in text for marker in ("物理伤害", "异能伤害", "护援技", "技能触发", "技能结束", "无特殊效果", "暂时没有这个技能"))


def _clean_support_description(value):
    text = (value or "").strip()
    if text.startswith("护援技") and "：" in text:
        return text.split("：", 1)[1].strip()
    return text


def _infer_point_cost_from_description(value):
    text = value or ""
    match = re.search(r"(\d+)\s*费", text)
    if match:
        try:
            return int(match.group(1))
        except Exception:
            pass
    return 0


def _infer_attack_capacity_from_name_or_description(name, description):
    text = "{} {}".format(name or "", description or "")
    if "后排" in text:
        return 3
    if "中排" in text:
        return 2
    if "前排" in text:
        return 1
    if "全体" in text or "群体" in text:
        return 9
    return 0


def _infer_preform_type_from_description(value):
    text = value or ""
    if "闪避" in text:
        return "Dodge"
    if "护盾" in text or "防御" in text:
        return "Defense"
    if "反击" in text:
        return "Attack"
    return "Air"


def _infer_pre_skill_value_from_description(value):
    text = value or ""
    match = re.search(r"(?:闪避|护盾|防御|反击)[（(]\s*(-?\d+(?:\.\d+)?)\s*[）)]", text)
    if match:
        try:
            return float(match.group(1))
        except Exception:
            pass
    return 0.0


def _parse_rates_from_description(value):
    text = value or ""
    physical = {}
    energy = {}
    physical_match = re.search(r"物理伤害【([^】]+)】", text)
    energy_match = re.search(r"异能伤害【([^】]+)】", text)
    for source, target in ((physical_match, physical), (energy_match, energy)):
        if not source:
            continue
        for level, number in re.findall(r"(\d+)\s*[：:，,]\s*(-?\d+(?:\.\d+)?)\s*%", source.group(1)):
            try:
                target[str(level)] = float(number) / 100.0
            except Exception:
                pass
    return {"physical": physical, "energy": energy}


def _append_support_skill(entries, character_name, names, descriptions, source_index):
    if not character_name or not names:
        return

    stage_count = max(len(names), len(descriptions))
    if stage_count <= 0:
        return

    point_costs = []
    rates = []
    auto_priorities = []
    skill_states = []
    preform_types = []
    pre_skill_values = []
    attack_capacities = []
    cleaned_descriptions = []
    true_names = []
    for index in range(stage_count):
        name = names[index] if index < len(names) else names[-1]
        description = descriptions[index] if index < len(descriptions) else ""
        cleaned_description = _clean_support_description(description)
        cleaned_descriptions.append(cleaned_description)
        true_names.append(name)
        point_costs.append(_infer_point_cost_from_description(description))
        rates.append(_parse_rates_from_description(description))
        auto_priorities.append(0)
        skill_states.append("Normal")
        preform_types.append(_infer_preform_type_from_description(description))
        pre_skill_values.append(_infer_pre_skill_value_from_description(description))
        attack_capacities.append(_infer_attack_capacity_from_name_or_description(name, description))

    key = "{}::{}".format(character_name, source_index)
    entries[key] = {
        "supportCharacterName": character_name,
        "supportCharacterCode": "",
        "sourceIndex": source_index,
        "skillSlot4Data": {
            "slotKey": "SkillSlot4",
            "displayName": "护援技",
            "icons": [],
            "names": names,
            "descriptions": cleaned_descriptions,
            "pointCosts": point_costs,
            "skillRates": rates,
            "autoPriorities": auto_priorities,
            "skillStates": skill_states,
            "preformTypes": preform_types,
            "preSkillValues": pre_skill_values,
            "skillNames": true_names,
            "attackCapacities": attack_capacities,
        },
    }


def _append_support_skill_entry(entries, character_name, character_code, icons, names, descriptions, skill_names, point_costs, source_index):
    _append_support_skill(entries, character_name, names, descriptions, source_index)
    key = "{}::{}".format(character_name, source_index)
    entry = entries.get(key)
    if not entry:
        return

    entry["supportCharacterCode"] = character_code or ""
    slot = entry.get("skillSlot4Data", {})
    if icons:
        slot["icons"] = icons
    if skill_names:
        slot["skillNames"] = skill_names
    if point_costs:
        slot["pointCosts"] = point_costs


def _extract_support_readable_runs(text, start, end):
    pattern = re.compile(r"[\u4e00-\u9fffA-Za-z0-9_\[\]\-/.:：，。！？、（）()【】%,•· ]{2,}")
    result = []
    for match in pattern.finditer(text[start:end]):
        value = match.group(0).strip("\x00 ")
        if not value:
            continue
        if not any("\u4e00" <= char <= "\u9fff" for char in value):
            continue
        result.append((start + match.start(), value))
    return result


def _extract_support_readable_runs_from_bytes(data, start, end):
    pattern = re.compile(r"[\u4e00-\u9fffA-Za-z0-9_\[\]\-/.:：，。！？、（）()【】%,•·⌈⌋]{1,}")
    result = []
    for offset in (0, 1):
        chunk = data[start + offset:end]
        text = chunk.decode("utf-16le", errors="ignore")
        for match in pattern.finditer(text):
            value = _clean_support_text_value(match.group(0))
            if not value:
                continue
            if not any("\u4e00" <= char <= "\u9fff" for char in value):
                continue
            result.append((start + offset + match.start() * 2, value))
    result.sort(key=lambda item: (item[0], item[1]))
    return result


def _support_character_aliases(name):
    text = (name or "").strip()
    aliases = []
    if text:
        aliases.append(text)
        without_suffix = re.sub(r"\[[^\]]+\]\s*$", "", text).strip()
        if without_suffix and without_suffix not in aliases:
            aliases.append(without_suffix)
        half_open = re.sub(r"\[[^\]]*$", "", text).strip()
        if half_open and half_open not in aliases:
            aliases.append(half_open)
    return aliases


def _known_support_characters_from_items(character_items):
    characters = []
    seen = set()
    for item in character_items or []:
        try:
            name = item.get("itemData", {}).get("name", "")
            code = item.get("code", "")
        except Exception:
            name = ""
            code = ""
        name = (name or "").strip()
        code = (code or "").strip()
        if not name:
            continue
        key = name.lower()
        if key in seen:
            continue
        seen.add(key)
        characters.append({
            "name": name,
            "code": code,
            "aliases": _support_character_aliases(name),
        })
    for name in ("晓古城", "游佐惠美", "桐乃", "黑雪姬", "御坂美琴", "里见莲太郎", "夏娜", "白井黑子", "逢坂大河", "司波达也", "司波深雪", "姬柊雪菜", "初音未来"):
        key = name.lower()
        if key not in seen:
            seen.add(key)
            characters.append({"name": name, "code": "", "aliases": _support_character_aliases(name)})
    return characters


def _known_character_names_from_items(character_items):
    names = []
    for character in _known_support_characters_from_items(character_items):
        for alias in character.get("aliases", []):
            if alias not in names:
                names.append(alias)
    return names


def _position_name_from_support_description(value):
    text = value or ""
    match = re.search(r"：\s*\d+\s*费\s*[，,]\s*([^，,]+)", text)
    if match:
        return match.group(1).strip()
    for candidate in ("全体攻击", "群体攻击", "后排攻击", "中排攻击", "前排攻击", "前排护盾", "后排驱散", "群体减防"):
        if candidate in text:
            return candidate
    return ""


def _true_name_from_support_description(value):
    text = value or ""
    match = re.match(r"护援技([^：:]+)[：:]", text)
    return match.group(1).strip() if match else ""


def _support_title_candidates(readable, start, end):
    values = []
    for position, value in readable:
        if position < start or position >= end:
            continue
        text = (value or "").strip()
        if not _is_probably_skill_title(text):
            continue
        if len(text) < 3:
            continue
        values.append((position, text))
    return values


def _support_field_range(data, segment_start, segment_end, field_name):
    marker = field_name.encode("utf-16le")
    position = data.find(marker, segment_start, segment_end)
    if position < 0:
        return -1, -1
    value_start = position + len(marker)
    return value_start, segment_end


def _support_field_values(data, segment_start, segment_end, field_name, next_field_names):
    start, _ = _support_field_range(data, segment_start, segment_end, field_name)
    if start < 0:
        return []
    end = segment_end
    for next_field_name in next_field_names:
        next_marker = next_field_name.encode("utf-16le")
        next_position = data.find(next_marker, start, segment_end)
        if next_position >= 0:
            end = min(end, next_position)
    readable = _extract_support_readable_runs_from_bytes(data, start, end)
    values = []
    for _, value in readable:
        text = _clean_support_text_value(value)
        if not text:
            continue
        if _is_support_field_noise(text):
            continue
        if field_name in ("Name", "SkillName"):
            if not _is_probably_skill_title(text):
                continue
        elif field_name == "Description":
            if text != "略" and not _is_probably_skill_description(text):
                continue
            if text in ("NSLOCTEXT", "None"):
                continue
        if text not in values:
            values.append(text)
    return values


def _support_icon_values(data, segment_start, segment_end):
    start, _ = _support_field_range(data, segment_start, segment_end, "Icon")
    if start < 0:
        return []
    name_position = data.find("Name".encode("utf-16le"), start, segment_end)
    end = name_position if name_position >= 0 else segment_end
    text = data[start:end].decode("utf-16le", errors="ignore")
    icons = []
    for match in re.finditer(r"/Game/[A-Za-z0-9_\-/]+(?:\.[A-Za-z0-9_\-]+)?", text):
        value = match.group(0).strip()
        if value and value not in icons:
            icons.append(value)
    return icons


def _support_point_costs_from_segment(data, segment_start, segment_end):
    start, _ = _support_field_range(data, segment_start, segment_end, "PointCost")
    if start < 0:
        return []
    next_position = data.find("SkillRate".encode("utf-16le"), start, segment_end)
    end = next_position if next_position >= 0 else segment_end
    chunk = data[start:end]
    values = []
    for index in range(0, max(0, len(chunk) - 3)):
        value = int.from_bytes(chunk[index:index + 4], "little", signed=True)
        if 0 <= value <= 200 and value not in values:
            values.append(value)
    if values and values[0] == 0 and len(values) > 1:
        values = values[1:]
    return values[:6]


def _support_candidate_score(names, descriptions, skill_names):
    score = 0
    if names:
        score += 10
    if skill_names:
        score += 10
    if descriptions:
        score += 5
    joined = "\n".join(descriptions)
    if "护援技" in joined:
        score += 80
    if "物理伤害" in joined or "异能伤害" in joined:
        score += 60
    if any(description == "略" for description in descriptions):
        score += 4
    if any("暂时没有这个技能" in description for description in descriptions):
        score -= 30
    return score


def _parse_support_skill_library_from_bytes(data, character_items=None):
    start_marker = "获取Sub的技能".encode("utf-16le")
    sub_start = data.find(start_marker)
    if sub_start < 0:
        return {}, "byte fallback could not find Sub function text"
    skill_section_marker = "2D技能".encode("utf-16le")
    skill_section_start = data.find(skill_section_marker, sub_start)
    if skill_section_start > sub_start:
        sub_start = skill_section_start

    characters = _known_support_characters_from_items(character_items)
    character_hits = []
    for character in characters:
        for alias in character.get("aliases", []):
            if not alias:
                continue
            encoded = alias.encode("utf-16le")
            index = sub_start
            while True:
                position = data.find(encoded, index)
                if position < 0:
                    break
                character_hits.append((position, character, alias))
                index = position + 1

    character_hits.sort(key=lambda item: item[0])
    candidates_by_name = {}
    for hit_index, (position, character, alias) in enumerate(character_hits):
        if alias != character.get("name") and character.get("name"):
            encoded_full_name = character.get("name", "").encode("utf-16le")
            if data.find(encoded_full_name, position, min(len(data), position + 16)) != position:
                continue

        segment_end = len(data)
        for next_position, _, _ in character_hits[hit_index + 1:]:
            if next_position > position:
                segment_end = next_position
                break

        segment_limit = min(segment_end, position + 5000)
        names = _support_field_values(
            data,
            position,
            segment_limit,
            "Name",
            ["Description", "PointCost", "SkillRate", "AutoPriority", "SkillState", "PreformType", "PreSkillValue", "SkillName", "AttackCapacity"])
        descriptions = _support_field_values(
            data,
            position,
            segment_limit,
            "Description",
            ["PointCost", "SkillRate", "AutoPriority", "SkillState", "PreformType", "PreSkillValue", "SkillName", "AttackCapacity"])
        skill_names = _support_field_values(
            data,
            position,
            segment_limit,
            "SkillName",
            ["AttackCapacity"])
        icons = _support_icon_values(data, position, segment_limit)
        if not names and descriptions:
            names = [_position_name_from_support_description(description) for description in descriptions]
            names = [name for name in names if name]
        if not skill_names:
            skill_names = [_true_name_from_support_description(description) for description in descriptions]
            skill_names = [name for name in skill_names if name]

        stage_count = max(len(names), len(descriptions), len(skill_names))
        if stage_count <= 0:
            continue
        if not descriptions and not skill_names:
            continue

        names = names[:stage_count]
        descriptions = descriptions[:stage_count]
        skill_names = skill_names[:stage_count]
        while len(names) < stage_count:
            names.append(skill_names[len(names)] if len(names) < len(skill_names) else names[-1] if names else "护援技")
        while len(descriptions) < stage_count:
            descriptions.append("")
        while len(skill_names) < stage_count:
            skill_names.append(_true_name_from_support_description(descriptions[len(skill_names)]) or names[len(skill_names)])
        icons = icons[:stage_count]
        while icons and len(icons) < stage_count:
            icons.append(icons[-1])

        key_name = character.get("name", "")
        score = _support_candidate_score(names, descriptions, skill_names)
        existing = candidates_by_name.get(key_name)
        if existing is not None and existing.get("score", 0) >= score:
            continue
        candidates_by_name[key_name] = {
            "score": score,
            "character": character,
            "names": names,
            "descriptions": descriptions,
            "skill_names": skill_names,
            "icons": icons,
            "point_costs": _support_point_costs_from_segment(data, position, segment_limit),
        }

    entries = {}
    for source_index, (key_name, candidate) in enumerate(candidates_by_name.items()):
        character = candidate.get("character", {})
        _append_support_skill_entry(
            entries,
            key_name,
            character.get("code", ""),
            candidate.get("icons", []),
            candidate.get("names", []),
            candidate.get("descriptions", []),
            candidate.get("skill_names", []),
            candidate.get("point_costs", []),
            source_index)

    return entries, "" if entries else "byte fallback found no Sub support entries"


def _parse_support_skill_library_from_runs(text, character_items=None):
    sub_start = text.find("获取Sub的技能")
    link_start = text.find("((\"", text.find("SubCharName=("))
    if sub_start < 0:
        return {}, "text fallback could not find Sub function text"
    if link_start < 0:
        link_start = len(text)

    readable = _extract_support_readable_runs(text, sub_start, link_start)
    known_names = _known_character_names_from_items(character_items)
    known_positions = [
        (position, value)
        for position, value in readable
        if value in known_names
    ]
    description_positions = [
        (position, value)
        for position, value in readable
        if value.startswith("护援技") and "物理伤害" in value
    ]

    entries = {}
    grouped = {}
    for position, description in description_positions:
        next_names = [(name_position, name) for name_position, name in known_positions if name_position > position]
        if not next_names:
            continue
        character_position, character_name = next_names[0]
        if character_position - position > 900:
            continue

        previous_description_positions = [desc_position for desc_position, _ in description_positions if desc_position < position]
        title_start = max([character_position] + previous_description_positions[-1:])
        title_candidates = _support_title_candidates(readable, title_start, position)
        title = title_candidates[-1][1] if title_candidates else ""
        inferred_position = _position_name_from_support_description(description)
        inferred_true_name = _true_name_from_support_description(description)
        if not inferred_true_name:
            continue
        title = inferred_true_name

        group = grouped.setdefault(character_name, {"names": [], "descriptions": []})
        group["names"].append(inferred_position or title)
        group["descriptions"].append(description)

    for source_index, (character_name, group) in enumerate(grouped.items()):
        _append_support_skill(
            entries,
            character_name,
            group.get("names", []),
            group.get("descriptions", []),
            source_index)

    return entries, "" if entries else "text fallback found no Sub support entries"


def _export_support_skill_library_from_text(project_path, character_items=None):
    disk_path = _content_path_to_disk(project_path, LINK_SKILL_LIBRARY_PATH.split(".", 1)[0])
    disk_path += ".uasset"
    if not disk_path or not os.path.isfile(disk_path):
        return {
            "objectPath": LINK_SKILL_LIBRARY_PATH,
            "hasData": False,
            "readMessage": "LB_Fucs.uasset was not found on disk",
            "entries": {},
        }

    try:
        data = open(disk_path, "rb").read()
        entries, read_message = _parse_support_skill_library_from_bytes(data, character_items)
        if not entries:
            text = data.decode("utf-16le", errors="ignore")
            entries, read_message = _parse_support_skill_library_from_runs(text, character_items)
        return {
            "objectPath": LINK_SKILL_LIBRARY_PATH,
            "hasData": len(entries) > 0,
            "readMessage": read_message,
            "entries": entries,
        }
    except Exception:
        unreal.log_warning("ZDToolbox support skill library text fallback failed: {}\n{}".format(
            disk_path,
            traceback.format_exc()))
        return {
            "objectPath": LINK_SKILL_LIBRARY_PATH,
            "hasData": False,
            "readMessage": "exception while parsing Sub support skill library text",
            "entries": {},
        }


def _export():
    manifest_path = os.environ.get("ZD_TOOLBOX_EXPORT_MANIFEST", "")
    if not manifest_path:
        raise RuntimeError("ZD_TOOLBOX_EXPORT_MANIFEST is empty.")

    project_path = os.environ.get("ZD_TOOLBOX_PROJECT_PATH", "")
    target_paths = _load_target_paths()
    selected_codes = _load_selected_character_codes()
    selected_code_keys = {code.lower() for code in selected_codes}
    export_scope = _load_export_scope()
    material_scope = export_scope.lower() in ("charactermaterials", "normalization") and bool(selected_codes)
    sequence_scope = export_scope.lower() == "charactersequences" and bool(selected_codes)
    previous_manifest = _load_previous_manifest(manifest_path) if selected_codes else {}
    export_root = os.path.dirname(manifest_path)
    registry = unreal.AssetRegistryHelpers.get_asset_registry()
    if material_scope:
        asset_scan_entries = []
        for code in sorted(selected_codes):
            actor_root = "{}/{}".format(CHARACTER_ACTOR_ROOT, code)
            asset_scan_entries.extend([
                ("{}/{}".format(BASE_MATERIAL_ROOT, code), True),
                (actor_root + "/Sound", True),
                (actor_root + "/BUFF", True),
                (actor_root, False),
            ])
        asset_scan_entries.append((CHAR_ITEM_ROOT, False))
        asset_scan_entries.append((TEAM_SELECT_ROOT, False))
        scan_paths = [
            path for path, recursive in asset_scan_entries
            if recursive or path in (CHAR_ITEM_ROOT, TEAM_SELECT_ROOT)
        ]
    elif sequence_scope:
        asset_scan_entries = [
            ("{}/{}/".format(CHARACTER_ACTOR_ROOT, code).rstrip("/"), True)
            for code in sorted(selected_codes)
        ]
        asset_scan_entries.append((CHAR_ITEM_ROOT, False))
        scan_paths = [path for path, _ in asset_scan_entries]
    else:
        asset_scan_entries = [(path, True) for path in target_paths]
        scan_paths = list(target_paths) + [CHAR_ITEM_ROOT]
    for index, target_path in enumerate(scan_paths):
        _write_progress(
            "Unreal 正在扫描内容目录...",
            48 + index / max(1, len(scan_paths)) * 8,
            "扫描目录 {}/{}：{}".format(index + 1, len(scan_paths), target_path),
            True)
        _scan_registry_path(registry, target_path)
    assets = []
    for target_index, (target_path, recursive) in enumerate(asset_scan_entries):
        try:
            found = registry.get_assets_by_path(
                unreal.Name(target_path),
                recursive=recursive,
                include_only_on_disk_assets=False)
        except TypeError:
            found = registry.get_assets_by_path(unreal.Name(target_path), recursive)
        found = [asset for asset in found if _is_top_level_asset(asset)]
        if material_scope and target_path != TEAM_SELECT_ROOT:
            found = [asset for asset in found if _include_material_scope_asset(asset, selected_codes)]
        elif sequence_scope:
            found = [asset for asset in found if "/sound/" not in _to_text(asset.package_path).lower() and "/buff/" not in _to_text(asset.package_path).lower()]
        elif selected_codes and target_path not in (SHARED_BUFF_ICON_ROOT, SHARED_BATTLE_EFFECT_ROOT, TEAM_SELECT_ROOT):
            found = [
                asset for asset in found
                if _character_code_from_package_path(_to_text(asset.package_path), target_path).lower() in selected_code_keys
            ]
        found_count = len(found)
        for asset_index, asset in enumerate(found):
            if asset_index % 10 == 0 or asset_index == found_count - 1:
                _write_progress(
                    "Unreal 正在导出素材预览...",
                    56 + (target_index + asset_index / max(1, found_count)) / max(1, len(asset_scan_entries)) * 14,
                    "目录 {}/{}：{}；资产 {}/{}：{}".format(
                        target_index + 1,
                        len(asset_scan_entries),
                        target_path,
                        asset_index + 1,
                        found_count,
                        _to_text(asset.asset_name)),
                    False)
            exported_file_path = ""
            try:
                exported_file_path = _export_texture_png(asset, export_root)
            except Exception:
                unreal.log_warning("ZDToolbox texture preview export failed: {}\n{}".format(
                    _object_path(asset),
                    traceback.format_exc()))
            if not exported_file_path:
                try:
                    exported_file_path = _export_sound_wave(asset, export_root)
                except Exception:
                    unreal.log_warning("ZDToolbox voice export failed: {}\n{}".format(
                        _object_path(asset),
                        traceback.format_exc()))

            assets.append({
                "assetName": _to_text(asset.asset_name),
                "assetClass": _asset_class(asset),
                "packageName": _to_text(asset.package_name),
                "packagePath": _to_text(asset.package_path),
                "objectPath": _object_path(asset),
                "sourceRoot": target_path,
                "exportedFilePath": exported_file_path,
                "tags": _tags(asset)
            })

    selected_detail = "选中角色：{}".format("、".join(sorted(selected_codes))) if selected_codes else "全部角色"
    if material_scope:
        _write_progress("Unreal 正在整理角色素材清单...", 88, selected_detail, True)
        character_items = []
        character_actors = []
        character_sequences = []
        character_buffs = []
        link_skill_library = {}
        support_skill_library = {}
    elif sequence_scope:
        _write_progress("Unreal 正在整理角色序列清单...", 88, selected_detail, True)
        character_items = _export_character_items(registry, project_path)
        character_items = [
            item for item in character_items
            if _to_text(item.get("code", "")).strip().lower() in selected_code_keys
        ]
        character_actors = _export_character_actors(assets)
        character_sequences = _export_character_sequences(character_actors, assets, project_path)
        character_buffs = []
        link_skill_library = {}
        support_skill_library = {}
    else:
        _write_progress("Unreal 正在读取 St3 角色信息...", 72, "扫描角色道具蓝图：{}；{}".format(CHAR_ITEM_ROOT, selected_detail), True)
        character_items = _export_character_items(registry, project_path)
        if selected_codes:
            character_items = [
                item for item in character_items
                if _to_text(item.get("code", "")).strip().lower() in selected_code_keys
            ]
        _write_progress("Unreal 正在读取 St4 技能信息...", 76, "读取角色蓝图 SkillSlot1-4。", True)
        character_actors = _export_character_actors(assets)
        _write_progress("Unreal 正在读取 St5 序列帧...", 80, "读取 AnimMaps、AnimSequences、Flipbook 帧顺序。", True)
        character_sequences = _export_character_sequences(character_actors, assets, project_path)
        _write_progress("Unreal 正在读取 St6-BUFF...", 84, "扫描每个角色的 BUFF 文件夹和 DreamTask 蓝图。", True)
        character_buffs = _export_character_buffs(character_actors, assets, export_root, project_path)
        _write_progress("Unreal 正在读取连携与护援函数库...", 88, LINK_SKILL_LIBRARY_PATH, True)
        link_skill_library = _export_link_skill_library()
        if not link_skill_library.get("hasData"):
            fallback_library = _export_link_skill_library_from_text(project_path)
            if fallback_library.get("hasData"):
                fallback_library["readMessage"] = "text fallback: " + link_skill_library.get("readMessage", "")
                link_skill_library = fallback_library
        support_skill_library = _export_support_skill_library_from_text(project_path, character_items)
    _write_progress("Unreal 正在整理导出清单...", 92, "排序资产、写入 characters.json。", True)
    assets.sort(key=lambda item: (
        item.get("sourceRoot", ""),
        item.get("packagePath", ""),
        item.get("assetName", "")))
    manifest = {
        "schemaVersion": 2,
        "generatedAt": datetime.datetime.now().isoformat(timespec="seconds"),
        "projectPath": project_path,
        "targets": target_paths,
        "assets": assets,
        "characterSummaries": _build_character_summaries(character_items),
        "summaryGeneratedAt": datetime.datetime.now().isoformat(timespec="seconds"),
        "characterItems": character_items,
        "characterActors": character_actors,
        "characterSequences": character_sequences,
        "characterBuffs": character_buffs,
        "linkSkillLibrary": link_skill_library,
        "supportSkillLibrary": support_skill_library,
        "teamSelect": _export_team_select(),
    }
    manifest = (_merge_material_scope_manifest(previous_manifest, manifest, selected_codes)
                if material_scope
                else _merge_selected_manifest(previous_manifest, manifest, selected_codes))
    os.makedirs(os.path.dirname(manifest_path), exist_ok=True)
    with open(manifest_path, "w", encoding="utf-8") as output:
        json.dump(manifest, output, ensure_ascii=False, indent=2)
    _write_progress(
        "Unreal 导出清单已写入。",
        100,
        "资产 {} 个，角色物品 {} 个，角色蓝图 {} 个，序列 {} 组，BUFF {} 组。".format(
            len(assets),
            len(character_items),
            len(character_actors),
            len(character_sequences),
            len(character_buffs)),
        False)
    unreal.log("ZDToolbox export manifest written: {} ({} assets, {} character items, {} character actors, {} character sequences, {} character buff sets, {} support entries, {} link entries)".format(
        manifest_path,
        len(assets),
        len(character_items),
        len(character_actors),
        len(character_sequences),
        len(character_buffs),
        len(support_skill_library.get("entries", {})),
        len(link_skill_library.get("entries", {}))))


try:
    _export()
except Exception:
    unreal.log_error("ZDToolbox export failed:\n{}".format(traceback.format_exc()))
    raise
