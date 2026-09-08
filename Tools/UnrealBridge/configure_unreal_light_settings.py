import datetime
import copy
import json
import os
import re
import sys
import traceback

import unreal


PROTOCOL_VERSION = 1
_PROGRESS_PATH = os.environ.get("ZD_LIGHT_CONFIG_PROGRESS", "")


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


STATUS_UNCHANGED = 0
STATUS_PENDING = 1
STATUS_ERROR = 2

VOICE_CATEGORY_LABELS = {
    "Formation": "入队语音",
    "Click": "点击语音",
    "Hurt": "受击语音",
    "Death": "死亡语音",
    "Defeat": "失败语音",
    "Victory": "胜利语音",
    "Skill1": "一技能语音",
    "Skill2": "二技能语音",
    "Ultimate": "终结技语音",
    "Support": "护援技语音",
    "Combo": "连携技语音",
    "Other": "待分配语音",
}


def _get(value, *names, default=None):
    for name in names:
        if name in value:
            return value[name]
    return default


def _write_json_atomic(path, value):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    temporary_path = path + ".tmp"
    with open(temporary_path, "w", encoding="utf-8") as output:
        json.dump(value, output, ensure_ascii=False, indent=2)
    os.replace(temporary_path, path)


def _snake_case(name):
    return re.sub(r"(?<!^)(?=[A-Z])", "_", name).lower()


def _property_candidates(name):
    result = []
    for candidate in (name, _snake_case(name), name.lower()):
        if candidate not in result:
            result.append(candidate)
    return result


def _read_property(value, name, default=None):
    if value is None:
        return default
    for candidate in _property_candidates(name):
        try:
            return value.get_editor_property(candidate)
        except Exception:
            pass
    return default


def _write_property(value, name, property_value):
    last_error = None
    for candidate in _property_candidates(name):
        try:
            value.set_editor_property(candidate, property_value)
            return
        except Exception as error:
            last_error = error
    raise RuntimeError("property is not writable: {} ({})".format(name, last_error))


def _package_path(object_path):
    return str(object_path or "").split(".", 1)[0]


def _object_path(value):
    if value is None:
        return ""
    try:
        return str(value.get_path_name())
    except Exception:
        return str(value)


def _normalized_path(value):
    return _object_path(value).strip().replace("\\", "/").lower()


def _asset_class_name(asset):
    if asset is None:
        return ""
    try:
        return str(asset.get_class().get_name())
    except Exception:
        return str(asset.get_class())


def _load_asset(object_path, label, expected_class=""):
    package_path = _package_path(object_path)
    if not package_path:
        raise RuntimeError("{}缺少目标路径".format(label))
    asset = unreal.EditorAssetLibrary.load_asset(package_path)
    if asset is None:
        raise RuntimeError("未找到{}：{}".format(label, object_path))
    actual_class = _asset_class_name(asset)
    if expected_class and expected_class.lower() not in actual_class.lower():
        raise RuntimeError("{}类型错误：应为 {}，当前为 {}".format(label, expected_class, actual_class))
    return asset


def _default_object(asset, object_path):
    try:
        generated_class = asset.generated_class()
        if generated_class is not None:
            return unreal.get_default_object(generated_class)
    except Exception:
        pass
    package_path = _package_path(object_path)
    asset_name = package_path.rsplit("/", 1)[-1]
    for candidate in (
            "{}.Default__{}_C".format(package_path, asset_name),
            "{}/Default__{}_C".format(package_path, asset_name)):
        try:
            result = unreal.load_object(None, candidate)
            if result is not None:
                return result
        except Exception:
            pass
    raise RuntimeError("无法读取蓝图默认对象：{}".format(object_path))


def _text(value):
    return "" if value is None else str(value)


def _text_list(value):
    try:
        return [str(item) for item in value]
    except Exception:
        return []


def _path_list(value):
    try:
        return [_object_path(item) for item in value]
    except Exception:
        return []


def _same_path(left, right):
    return _normalized_path(left) == _normalized_path(right)


def _same_paths(left, right):
    return len(left) == len(right) and all(_same_path(a, b) for a, b in zip(left, right))


def _summary(value, maximum=160):
    if isinstance(value, list):
        if not value:
            return "空"
        string_values = [item for item in value if isinstance(item, str)]
        if len(string_values) == len(value) and not any(
                item.replace("\\", "/").startswith("/Game/") for item in string_values):
            return "、".join(string_values)
        if len(value) <= 3:
            return "、".join(_object_path(item) for item in value)
        return "{} 项：{} ... {}".format(len(value), _object_path(value[0]), _object_path(value[-1]))
    text = _text(value).replace("\r", " ").replace("\n", " ")
    # 第四步界面会自行换行；保留完整字段，避免把角色介绍等内容截断。
    return text


def _voice_display_value(object_path):
    normalized = str(object_path or "").replace("\\", "/")
    category = "语音"
    segments = normalized.split("/")
    if len(segments) >= 2:
        category = VOICE_CATEGORY_LABELS.get(segments[-2], category)
    return "{} | {}".format(normalized, category)


def _entry(stable_id, group, display_name, target_path, target_field,
           source_summary, current_value=None, target_value=None, equal=None, error=""):
    status = STATUS_ERROR if error else STATUS_UNCHANGED if equal else STATUS_PENDING
    current_values = []
    target_values = []
    if isinstance(current_value, list):
        current_values = [_object_path(item) for item in current_value]
    if isinstance(target_value, list):
        target_values = [_object_path(item) for item in target_value]
    return {
        "stableId": stable_id,
        "groupName": group,
        "displayName": display_name,
        "targetPath": target_path,
        "targetField": target_field,
        "sourceSummary": source_summary,
        "currentSummary": _summary(current_value),
        "targetSummary": _summary(target_value),
        "currentValues": current_values,
        "targetValues": target_values,
        "status": status,
        "errorMessage": error,
    }


def _error_entry(stable_id, group, display_name, target_path, target_field, source_summary, error):
    return _entry(
        stable_id, group, display_name, target_path, target_field,
        source_summary, error=str(error))


def _mapping_to_dict(value):
    if value is None:
        return {}
    try:
        return dict(value)
    except Exception:
        result = {}
        try:
            for key in value.keys():
                result[str(key)] = value[key]
        except Exception:
            pass
        return result


def _prepare_metasound(asset):
    try:
        subsystem = unreal.get_engine_subsystem(unreal.MetaSoundBuilderSubsystem)
        builder = subsystem.find_builder_of_document(asset)
        if builder is None:
            subsystem.register_source_builder(asset)
    except Exception:
        pass


def _meta_member(document_text, class_name, label):
    matches = re.findall(class_name + r"'([^']+)'", document_text)
    if len(matches) != 1:
        raise RuntimeError("MetaSound 必须包含唯一的{}默认值对象，当前找到 {} 个".format(label, len(matches)))
    try:
        return unreal.load_object(None, matches[0])
    except Exception:
        return None


def _member_page(member, label, allow_empty=False):
    if member is None:
        raise RuntimeError("MetaSound 缺少{}默认值对象".format(label))
    defaults = list(_read_property(member, "Defaults", []))
    if not defaults:
        if allow_empty:
            return [], None
        raise RuntimeError("MetaSound 的{}没有 Default 页面".format(label))
    return defaults, defaults[0]


def _clone_struct(value):
    """Clone an Unreal reflected struct without assuming a wrapper-specific copy method."""
    try:
        return value.copy()
    except Exception:
        try:
            return copy.copy(value)
        except Exception as error:
            raise RuntimeError("无法复制 Unreal 默认值结构：{}".format(error))


def _literal_object_path(literal):
    return _object_path(_read_property(literal, "Object"))


# 这两个是 MetaSound 的 graph variable，不是 graph input：
# 该资产的 GetGraphInputNames 只返回 UE.Source.OnPlay。
WAVE_VARIABLE_NAMES = ("Wave Asset", "WaveAsset")
WEIGHTS_VARIABLE_NAMES = ("Weights",)


def _meta_builder(asset):
    """Return the document builder for a MetaSound asset, or None.

    The editor graph member objects only mirror the document: their Defaults
    property is declared Transient, so in a fresh headless process it is empty
    even when the asset on disk holds the values. The frontend document reached
    through the builder is the authoritative store for both reading and writing.
    """
    try:
        subsystem = unreal.get_editor_subsystem(unreal.MetaSoundEditorSubsystem)
    except Exception:
        return None
    if subsystem is None:
        return None
    try:
        result = subsystem.find_or_begin_building(asset)
    except Exception:
        return None
    builder = result[0] if isinstance(result, tuple) else result
    return builder or None


def _builder_subsystem():
    try:
        return unreal.get_engine_subsystem(unreal.MetaSoundBuilderSubsystem)
    except Exception:
        return None


def _first_value(result):
    return result[0] if isinstance(result, tuple) else result


def _graph_variable_literal(builder, names):
    """Read a graph variable default, trying each accepted spelling."""
    for name in names:
        try:
            literal = _first_value(builder.get_graph_variable_default(name))
        except Exception:
            continue
        text = _literal_text(literal)
        # 变量不存在时引擎返回 Type=None 的空字面量，不能当成“当前值为空”。
        if text and "Type=None" not in text:
            return literal
    return None


def _literal_text(literal):
    if literal is None:
        return ""
    try:
        return str(literal.export_text())
    except Exception:
        return ""


def _literal_group(text, field):
    """Extract one bracketed field from a literal export, tolerating nested parens."""
    marker = field + "=("
    start = text.find(marker)
    if start < 0:
        return None
    index = start + len(marker)
    depth = 1
    while index < len(text) and depth > 0:
        if text[index] == "(":
            depth += 1
        elif text[index] == ")":
            depth -= 1
            if depth == 0:
                return text[start + len(marker):index]
        index += 1
    return None


def _literal_object_paths(literal):
    """Best-effort display list. Correctness of the diff never depends on this."""
    group = _literal_group(_literal_text(literal), "AsUObject")
    if group is None:
        return []
    values = []
    for item in group.split(","):
        item = item.strip()
        if not item or item.lower() == "none":
            values.append("")
            continue
        # 元素形如 "/Script/Engine.SoundWave'/Game/.../Misaka-Hurt-01.Misaka-Hurt-01'"：
        # 真实路径在内层单引号里，先匹配双引号会连类名一起取到。
        quoted = re.search(r"'([^']+)'", item) or re.search(r'"([^"]+)"', item)
        values.append(_normalized_path(quoted.group(1) if quoted else item))
    return values


def _literal_floats(literal):
    group = _literal_group(_literal_text(literal), "AsFloat")
    if group is None:
        return []
    values = []
    for item in group.split(","):
        item = item.strip()
        if not item:
            continue
        try:
            values.append(float(item))
        except ValueError:
            continue
    return values


def _build_wave_literal(subsystem, hurt_paths):
    """Index 0 stays empty; the toolbox Hurt voices follow in ascending order."""
    sounds = [None]
    for index, path in enumerate(hurt_paths, start=1):
        sounds.append(_load_asset(path, "受击语音 #{}".format(index), "SoundWave"))
    return _first_value(subsystem.create_object_array_meta_sound_literal(sounds))


def _build_weight_literal(subsystem, hurt_count):
    weights = [0.3] + [0.1] * hurt_count
    return _first_value(subsystem.create_float_array_meta_sound_literal(weights))


def _same_literal(left, right):
    """Compare two literals produced by the same engine serializer.

    Comparing exported text avoids parsing the literal's internals, so the
    difference verdict stays correct even if the export format changes.
    """
    left_text = _literal_text(left)
    right_text = _literal_text(right)
    return bool(left_text) and left_text == right_text


def _set_graph_variable_default(asset, names, literal):
    """Write a graph variable default through ZDBridge.

    Returns the variable name that was written, or None when the plugin function
    is unavailable so the caller can fall back to the editor graph mirror.
    """
    bridge = getattr(unreal, "ZDBridgeLibrary", None)
    method = getattr(bridge, "set_meta_sound_graph_variable_default", None) if bridge else None
    if method is None:
        return None
    last_error = ""
    for name in names:
        error = str(method(asset, name, literal) or "")
        if not error:
            return name
        last_error = error
    raise RuntimeError(last_error or "ZDBridge 未能写入 MetaSound 变量默认值")


def _read_meta_arrays(asset, asset_path):
    """Legacy fallback: read the editor graph mirror when no builder is available."""
    _prepare_metasound(asset)
    document = _read_property(asset, "RootMetasoundDocument")
    try:
        document_text = document.export_text()
    except Exception:
        document_text = ""
    if "WaveAsset" not in document_text or "Weights" not in document_text:
        raise RuntimeError("MetaSound 必须保留 Wave Asset 和 Weights 两个变量")
    object_member = _meta_member(
        document_text,
        "MetasoundEditorGraphMemberDefaultObjectArray",
        "Wave Asset")
    float_member = _meta_member(
        document_text,
        "MetasoundEditorGraphMemberDefaultFloatArray",
        "Weights")
    _, object_page = _member_page(object_member, "Wave Asset", allow_empty=True)
    _, float_page = _member_page(float_member, "Weights", allow_empty=True)
    object_values = list(_read_property(object_page, "Value", [])) if object_page is not None else []
    float_values = [float(value) for value in _read_property(float_page, "Value", [])] if float_page is not None else []
    return object_member, float_member, object_values, float_values


# 引擎没有向脚本暴露 SetGraphVariableDefault，写入只能经由编辑器图镜像；
# 而该镜像是 Transient 的，只有资产在 MetaSound 编辑器里打开过才会被回填。
MIRROR_REQUIRED_HINT = (
    "受击 MetaSound 的{}默认值当前不可写：编辑器图缓存为空。"
    "请在 Unreal 中打开 {} 这个 MetaSound 后再应用基础配置。")


def _set_meta_object_array(member, current_values, object_paths, asset_path=""):
    try:
        defaults, page = _member_page(member, "Wave Asset")
    except RuntimeError:
        raise RuntimeError(MIRROR_REQUIRED_HINT.format("受击语音数组", asset_path))
    if not current_values:
        raise RuntimeError(MIRROR_REQUIRED_HINT.format("受击语音数组", asset_path))
    blank = _clone_struct(current_values[0])
    _write_property(blank, "Object", None)
    values = [blank]
    for index, object_path in enumerate(object_paths, start=1):
        sound = _load_asset(object_path, "受击语音 #{}".format(index), "SoundWave")
        literal = _clone_struct(blank)
        _write_property(literal, "Object", sound)
        values.append(literal)
    updated_page = page
    _write_property(updated_page, "Value", values)
    defaults[0] = updated_page
    member.modify()
    _write_property(member, "Defaults", defaults)


def _set_meta_float_array(member, weights, asset_path=""):
    try:
        defaults, page = _member_page(member, "Weights")
    except RuntimeError:
        raise RuntimeError(MIRROR_REQUIRED_HINT.format("受击语音权重", asset_path))
    updated_page = page
    _write_property(updated_page, "Value", [float(value) for value in weights])
    defaults[0] = updated_page
    member.modify()
    _write_property(member, "Defaults", defaults)


def _collect_context(request):
    context = {}
    context["team_asset"] = _load_asset(_get(request, "TeamSelectObjectPath", "teamSelectObjectPath"), "角色入队语音 UI", "WidgetBlueprint")
    context["team_cdo"] = _default_object(context["team_asset"], _get(request, "TeamSelectObjectPath", "teamSelectObjectPath"))
    context["item_asset"] = _load_asset(_get(request, "ItemObjectPath", "itemObjectPath"), "角色 Item", "Blueprint")
    context["item_cdo"] = _default_object(context["item_asset"], _get(request, "ItemObjectPath", "itemObjectPath"))
    context["item_data"] = _read_property(context["item_cdo"], "ItemData")
    if context["item_data"] is None:
        raise RuntimeError("角色 Item 缺少 ItemData")
    context["char_data"] = _read_property(context["item_data"], "CharData")
    if context["char_data"] is None:
        raise RuntimeError("角色 Item 缺少 ItemData.CharData")
    context["meta_asset"] = _load_asset(_get(request, "MetaSoundObjectPath", "metaSoundObjectPath"), "角色受击 MetaSound", "MetaSoundSource")
    return context


def _append_meta_error_entries(request, entries, error):
    """Keep MetaSound diagnostics visible even when another foundation asset fails."""
    meta_path = _get(request, "MetaSoundObjectPath", "metaSoundObjectPath", default="")
    for stable_id, display, field in (
            ("meta.concurrency", "受击 MetaSound 并发", "MetaSoundSource.ConcurrencySet"),
            ("meta.waves", "受击语音数组", "Wave Asset:WaveAsset:Array"),
            ("meta.weights", "受击语音权重", "Weights:Float:Array")):
        entries.append(_error_entry(
            stable_id, "受击 MetaSound", display, meta_path, field,
            "工具箱 Hurt 语音", error))


def _build_meta_entries(request):
    """Scan MetaSound independently so unrelated Item/UI errors cannot hide it."""
    meta_path = _get(request, "MetaSoundObjectPath", "metaSoundObjectPath", default="")
    meta_asset = _load_asset(meta_path, "角色受击 MetaSound", "MetaSoundSource")
    hurt_concurrency_path = _get(request, "HurtConcurrencyObjectPath", "hurtConcurrencyObjectPath", default="")
    hurt_concurrency = _load_asset(hurt_concurrency_path, "受击并发", "SoundConcurrency")
    current_concurrency = sorted(_path_list(_read_property(meta_asset, "ConcurrencySet", [])))
    entries = [_entry(
        "meta.concurrency", "受击 MetaSound", "受击 MetaSound 并发", meta_path,
        "MetaSoundSource.ConcurrencySet", "当前角色 Con_Ondm",
        current_concurrency, [hurt_concurrency_path],
        _same_paths(current_concurrency, [hurt_concurrency_path]))]
    hurt_paths = list(_get(request, "HurtVoiceObjectPaths", "hurtVoiceObjectPaths", default=[]))
    target_waves = [""] + hurt_paths
    target_weights = [0.3] + [0.1] * len(hurt_paths)
    builder = _meta_builder(meta_asset)
    subsystem = _builder_subsystem()
    target_wave_literal = _build_wave_literal(subsystem, hurt_paths) if subsystem is not None else None
    target_weight_literal = _build_weight_literal(subsystem, len(hurt_paths)) if subsystem is not None else None
    # 只有当字面量能导出文本时才走文档比较；导不出来就没有可比对象，退回旧路径，
    # 否则两边都是空串会把每次检测都判成待设置。
    if builder is not None and _literal_text(target_wave_literal) and _literal_text(target_weight_literal):
        # 权威来源是 MetaSound 的前端文档；编辑器图成员上的 Defaults 是 Transient 的，
        # 无头进程里恒为空，按它比较会把已经同步好的数组永远判成待设置。
        current_wave_literal = _graph_variable_literal(builder, WAVE_VARIABLE_NAMES)
        current_weight_literal = _graph_variable_literal(builder, WEIGHTS_VARIABLE_NAMES)
        current_waves = _literal_object_paths(current_wave_literal)
        float_values = _literal_floats(current_weight_literal)
        waves_equal = _same_literal(current_wave_literal, target_wave_literal)
        weights_equal = _same_literal(current_weight_literal, target_weight_literal)
    else:
        object_member, float_member, object_values, float_values = _read_meta_arrays(meta_asset, meta_path)
        current_waves = [_literal_object_path(value) for value in object_values]
        for index, path in enumerate(hurt_paths, start=1):
            _load_asset(path, "受击语音 #{}".format(index), "SoundWave")
        waves_equal = _same_paths(current_waves, target_waves)
        weights_equal = len(float_values) == len(target_weights) and all(
            abs(left - right) < 0.0001 for left, right in zip(float_values, target_weights))
    entries.append(_entry(
        "meta.waves", "受击 MetaSound", "受击语音数组", meta_path,
        "Wave Asset:WaveAsset:Array", "Hurt 按编号升序，索引 0 留空",
        current_waves, target_waves, waves_equal))
    entries.append(_entry(
        "meta.weights", "受击 MetaSound", "受击语音权重", meta_path,
        "Weights:Float:Array", "索引 0 为 0.3，其余为 0.1",
        [str(value) for value in float_values], [str(value) for value in target_weights], weights_equal))
    return entries


def _build_entries(request):
    entries = []
    character_name = _get(request, "CharacterName", "characterName", default="")
    team_path = _get(request, "TeamSelectObjectPath", "teamSelectObjectPath", default="")
    team_voice_path = _get(request, "TeamVoiceObjectPath", "teamVoiceObjectPath", default="")
    item_path = _get(request, "ItemObjectPath", "itemObjectPath", default="")
    meta_path = _get(request, "MetaSoundObjectPath", "metaSoundObjectPath", default="")
    try:
        context = _collect_context(request)
    except Exception as error:
        message = str(error)
        entries.append(_error_entry(
            "foundation.assets", "依赖检查", "基础配置依赖", item_path,
            "资产和默认对象", "第一步至第三步产物", message))
        try:
            entries.extend(_build_meta_entries(request))
        except Exception as meta_error:
            _append_meta_error_entries(request, entries, meta_error)
        return entries

    try:
        team_voice = _load_asset(team_voice_path, "编队语音", "SoundWave")
        mapping = _mapping_to_dict(_read_property(context["team_cdo"], "CharVoice"))
        current_voice = next((value for key, value in mapping.items() if str(key) == character_name), None)
        entries.append(_entry(
            "team.voice", "角色入队语音", "编队语音", team_path,
            'Default__UI_TeamSelect_C.CharVoice["{}"]'.format(character_name),
            "Formation 中编号最小的语音", _object_path(current_voice), team_voice_path,
            _same_path(current_voice, team_voice)))
    except Exception as error:
        entries.append(_error_entry(
            "team.voice", "角色入队语音", "编队语音", team_path,
            "Default__UI_TeamSelect_C.CharVoice", "Formation 中编号最小的语音", error))

    item_data = context["item_data"]
    char_data = context["char_data"]
    item_specs = [
        ("item.name", "角色名称", "ItemData.Name", "CharacterInfo.Name",
         _text(_read_property(item_data, "Name")), character_name),
        ("item.description", "角色介绍", "ItemData.Description", "CharacterInfo.Description",
         _text(_read_property(item_data, "Description")), _get(request, "Description", "description", default="")),
        ("item.keywords", "角色关键词", "ItemData.KeyWords", "CharacterInfo.KeywordTags",
         _text_list(_read_property(item_data, "KeyWords", [])), list(_get(request, "Keywords", "keywords", default=[]))),
        ("item.count", "初始数量", "ItemData.Count", "固定值 1",
         int(_read_property(item_data, "Count", 0)), 1),
        ("item.max-count", "最大堆叠数量", "ItemData.MaxCount", "固定值 1",
         int(_read_property(item_data, "MaxCount", 0)), 1),
        ("item.speed", "速度", "ItemData.CharData.Speed", "CharacterInfo.Speed",
         int(_read_property(char_data, "Speed", 0)), int(_get(request, "Speed", "speed", default=0))),
        ("item.health", "生命值", "ItemData.CharData.Health", "CharacterInfo.Health",
         int(_read_property(char_data, "Health", 0)), int(_get(request, "Health", "health", default=0))),
        ("item.attack", "攻击力", "ItemData.CharData.Attack", "CharacterInfo.Attack",
         int(_read_property(char_data, "Attack", 0)), int(_get(request, "Attack", "attack", default=0))),
        ("item.phy-defense", "物理防御", "ItemData.CharData.PhyDefense", "CharacterInfo.PhysicalDefense",
         int(_read_property(char_data, "PhyDefense", 0)), int(_get(request, "PhysicalDefense", "physicalDefense", default=0))),
        ("item.mag-defense", "异能防御", "ItemData.CharData.MagDefense", "CharacterInfo.EnergyDefense",
         int(_read_property(char_data, "MagDefense", 0)), int(_get(request, "EnergyDefense", "energyDefense", default=0))),
        ("item.critical", "暴击率", "ItemData.CharData.Critical", "CharacterInfo.CriticalRate",
         int(_read_property(char_data, "Critical", 0)), int(_get(request, "CriticalRate", "criticalRate", default=0))),
        ("item.critical-damage", "暴击伤害", "ItemData.CharData.CriticalC", "CharacterInfo.CriticalDamage",
         int(_read_property(char_data, "CriticalC", 0)), int(_get(request, "CriticalDamage", "criticalDamage", default=0))),
        ("item.passive", "被动介绍", "ItemData.CharData.SkillDescription", "CharacterInfo.PassiveSkills",
         _text_list(_read_property(char_data, "SkillDescription", [])), list(_get(request, "PassiveSkills", "passiveSkills", default=[]))),
    ]
    for stable_id, display_name, field, source, current, target in item_specs:
        entries.append(_entry(
            stable_id, "Item 配置", display_name, item_path, field, source,
            current, target, current == target))

    reference_specs = [
        ("item.icon", "道具图标", "ItemData.Icon", "ItemIcon 编号 1", "ItemIconObjectPath", "itemIconObjectPath", item_data, "Icon", "Texture2D"),
        ("item.shape-icons", "形态头像", "ItemData.CharData.CharShapeIcon", "Icon 按编号升序", "ShapeIconObjectPaths", "shapeIconObjectPaths", char_data, "CharShapeIcon", "Texture2D"),
        ("item.shape-portraits", "形态立绘", "ItemData.CharData.CharShapeGroup", "MorphPortrait 按编号升序", "ShapePortraitObjectPaths", "shapePortraitObjectPaths", char_data, "CharShapeGroup", "Texture2D"),
        ("item.shape-complete", "形态完整立绘", "ItemData.CharData.CharShapeComplete", "FullMorphPortrait 按编号升序", "ShapeCompleteObjectPaths", "shapeCompleteObjectPaths", char_data, "CharShapeComplete", "Texture2D"),
    ]
    shape_targets = []
    for spec in reference_specs:
        stable_id, display_name, field, source, pascal, camel, owner, prop, expected_class = spec
        raw_target = _get(request, pascal, camel, default=[] if "Paths" in pascal else "")
        targets = list(raw_target) if isinstance(raw_target, list) else [raw_target]
        try:
            if not targets or any(not value for value in targets):
                raise RuntimeError("工具箱缺少{}规范素材".format(display_name))
            for index, target in enumerate(targets, start=1):
                _load_asset(target, "{} #{}".format(display_name, index), expected_class)
            current = _path_list(_read_property(owner, prop, [])) if len(targets) > 1 or "Paths" in pascal else [_object_path(_read_property(owner, prop))]
            entries.append(_entry(
                stable_id, "Item 配置", display_name, item_path, field, source,
                current, targets, _same_paths(current, targets)))
            if "shape-" in stable_id:
                shape_targets.append(targets)
        except Exception as error:
            entries.append(_error_entry(stable_id, "Item 配置", display_name, item_path, field, source, error))

    if len(shape_targets) == 3 and len({len(values) for values in shape_targets}) != 1:
        message = "Icon、MorphPortrait、FullMorphPortrait 数量必须一致，当前为 {}".format(
            " / ".join(str(len(values)) for values in shape_targets))
        for item in entries:
            if item["stableId"] in ("item.shape-icons", "item.shape-portraits", "item.shape-complete"):
                item["status"] = STATUS_ERROR
                item["errorMessage"] = message

    try:
        item_type_path = _get(request, "ItemTypeObjectPath", "itemTypeObjectPath", default="")
        item_type = _load_asset(item_type_path, "角色物品类型")
        current_type = _object_path(_read_property(item_data, "Type"))
        entries.append(_entry(
            "item.type", "Item 配置", "角色物品类型", item_path, "ItemData.Type",
            "固定 Chara_Type", current_type, item_type_path, _same_path(current_type, item_type)))
    except Exception as error:
        entries.append(_error_entry(
            "item.type", "Item 配置", "角色物品类型", item_path, "ItemData.Type", "固定 Chara_Type", error))

    try:
        entries.extend(_build_meta_entries(request))
    except Exception as error:
        _append_meta_error_entries(request, entries, error)

    try:
        talk_concurrency_path = _get(request, "TalkConcurrencyObjectPath", "talkConcurrencyObjectPath", default="")
        talk_concurrency = _load_asset(talk_concurrency_path, "普通语音并发", "SoundConcurrency")
        talk_paths = list(_get(request, "TalkVoiceObjectPaths", "talkVoiceObjectPaths", default=[]))
        incorrect = []
        talk_assets = []
        for path in talk_paths:
            sound = _load_asset(path, "普通角色语音", "SoundWave")
            talk_assets.append(sound)
            current = _path_list(_read_property(sound, "ConcurrencySet", []))
            if not _same_paths(current, [talk_concurrency_path]):
                incorrect.append(path)
        talk_labels = [_voice_display_value(path) for path in talk_paths]
        entries.append(_entry(
            "voice.talk-concurrency", "SoundWave 并发", "普通角色语音并发",
            "/Game/GameActor2D/{}/Sound".format(_get(request, "CharacterCode", "characterCode", default="")),
            "非 Hurt SoundWave.ConcurrencySet", "非 Hurt 语音统一使用当前角色 Con_Talk",
            "正确 {}/{} 项".format(len(talk_paths) - len(incorrect), len(talk_paths)),
            talk_labels, len(incorrect) == 0))
        context["talk_assets"] = talk_assets
        context["talk_concurrency"] = talk_concurrency
    except Exception as error:
        entries.append(_error_entry(
            "voice.talk-concurrency", "SoundWave 并发", "普通角色语音并发",
            "/Game/GameActor2D/{}/Sound".format(_get(request, "CharacterCode", "characterCode", default="")),
            "非 Hurt SoundWave.ConcurrencySet", "非 Hurt 语音统一使用当前角色 Con_Talk", error))

    return entries


def _apply_team(request, changed_assets):
    team_path = _get(request, "TeamSelectObjectPath", "teamSelectObjectPath")
    asset = _load_asset(team_path, "角色入队语音 UI", "WidgetBlueprint")
    cdo = _default_object(asset, team_path)
    mapping = _mapping_to_dict(_read_property(cdo, "CharVoice"))
    character_name = _get(request, "CharacterName", "characterName")
    mapping[character_name] = _load_asset(
        _get(request, "TeamVoiceObjectPath", "teamVoiceObjectPath"), "编队语音", "SoundWave")
    cdo.modify()
    _write_property(cdo, "CharVoice", mapping)
    changed_assets.append(asset)


def _apply_item(request, selected, changed_assets):
    item_ids = sorted(stable_id for stable_id in selected if stable_id.startswith("item."))
    if not item_ids:
        return [], {}
    item_path = _get(request, "ItemObjectPath", "itemObjectPath")
    try:
        asset = _load_asset(item_path, "角色 Item", "Blueprint")
        cdo = _default_object(asset, item_path)
        item_data = _read_property(cdo, "ItemData")
        char_data = _read_property(item_data, "CharData")
        if item_data is None or char_data is None:
            raise RuntimeError("角色 Item 缺少 ItemData 或 CharData")
    except Exception as error:
        return [], {stable_id: str(error) for stable_id in item_ids}
    item_values = {
        "item.name": ("Name", _get(request, "CharacterName", "characterName")),
        "item.description": ("Description", _get(request, "Description", "description")),
        "item.keywords": ("KeyWords", list(_get(request, "Keywords", "keywords", default=[]))),
        "item.count": ("Count", 1),
        "item.max-count": ("MaxCount", 1),
    }
    char_values = {
        "item.speed": ("Speed", int(_get(request, "Speed", "speed", default=0))),
        "item.health": ("Health", int(_get(request, "Health", "health", default=0))),
        "item.attack": ("Attack", int(_get(request, "Attack", "attack", default=0))),
        "item.phy-defense": ("PhyDefense", int(_get(request, "PhysicalDefense", "physicalDefense", default=0))),
        "item.mag-defense": ("MagDefense", int(_get(request, "EnergyDefense", "energyDefense", default=0))),
        "item.critical": ("Critical", int(_get(request, "CriticalRate", "criticalRate", default=0))),
        "item.critical-damage": ("CriticalC", int(_get(request, "CriticalDamage", "criticalDamage", default=0))),
        "item.passive": ("SkillDescription", list(_get(request, "PassiveSkills", "passiveSkills", default=[]))),
    }
    shape_values = {
        "item.shape-icons": ("CharShapeIcon", "ShapeIconObjectPaths", "shapeIconObjectPaths", "形态头像"),
        "item.shape-portraits": ("CharShapeGroup", "ShapePortraitObjectPaths", "shapePortraitObjectPaths", "形态立绘"),
        "item.shape-complete": ("CharShapeComplete", "ShapeCompleteObjectPaths", "shapeCompleteObjectPaths", "形态完整立绘"),
    }
    applied = []
    errors = {}
    for stable_id in item_ids:
        try:
            if stable_id in item_values:
                name, value = item_values[stable_id]
                _write_property(item_data, name, value)
            elif stable_id in char_values:
                name, value = char_values[stable_id]
                _write_property(char_data, name, value)
            elif stable_id == "item.icon":
                _write_property(item_data, "Icon", _load_asset(
                    _get(request, "ItemIconObjectPath", "itemIconObjectPath"), "道具图标", "Texture2D"))
            elif stable_id == "item.type":
                _write_property(item_data, "Type", _load_asset(
                    _get(request, "ItemTypeObjectPath", "itemTypeObjectPath"), "角色物品类型"))
            elif stable_id in shape_values:
                property_name, request_name, request_camel, label = shape_values[stable_id]
                paths = _get(request, request_name, request_camel, default=[])
                _write_property(char_data, property_name, [
                    _load_asset(path, label, "Texture2D") for path in paths
                ])
            else:
                raise RuntimeError("不支持的 Item 配置项：{}".format(stable_id))
            applied.append(stable_id)
        except Exception as error:
            errors[stable_id] = str(error)
    if applied:
        try:
            _write_property(item_data, "CharData", char_data)
            cdo.modify()
            _write_property(cdo, "ItemData", item_data)
            changed_assets.append(asset)
        except Exception as error:
            for stable_id in applied:
                errors[stable_id] = str(error)
            applied = []
    return applied, errors


def _apply_meta(request, selected, changed_assets):
    meta_ids = sorted(stable_id for stable_id in selected if stable_id.startswith("meta."))
    if not meta_ids:
        return [], {}
    meta_path = _get(request, "MetaSoundObjectPath", "metaSoundObjectPath")
    try:
        asset = _load_asset(meta_path, "角色受击 MetaSound", "MetaSoundSource")
    except Exception as error:
        return [], {stable_id: str(error) for stable_id in meta_ids}
    applied = []
    errors = {}
    if "meta.concurrency" in selected:
        try:
            concurrency = _load_asset(
                _get(request, "HurtConcurrencyObjectPath", "hurtConcurrencyObjectPath"), "受击并发", "SoundConcurrency")
            asset.modify()
            _write_property(asset, "ConcurrencySet", {concurrency})
            applied.append("meta.concurrency")
        except Exception as error:
            errors["meta.concurrency"] = str(error)
    hurt_paths = list(_get(request, "HurtVoiceObjectPaths", "hurtVoiceObjectPaths", default=[]))
    subsystem = _builder_subsystem()
    object_member = float_member = None
    object_values = []
    mirror_loaded = False

    def _ensure_mirror():
        """Editor graph mirror fallback: only load it when ZDBridge is unavailable."""
        nonlocal object_member, float_member, object_values, mirror_loaded
        if not mirror_loaded:
            object_member, float_member, object_values, _ = _read_meta_arrays(asset, meta_path)
            mirror_loaded = True

    if "meta.waves" in selected:
        try:
            written = None
            if subsystem is not None:
                written = _set_graph_variable_default(
                    asset, WAVE_VARIABLE_NAMES, _build_wave_literal(subsystem, hurt_paths))
            if written is None:
                _ensure_mirror()
                _set_meta_object_array(object_member, object_values, hurt_paths, meta_path)
            applied.append("meta.waves")
        except Exception as error:
            errors["meta.waves"] = str(error)
    if "meta.weights" in selected:
        try:
            written = None
            if subsystem is not None:
                written = _set_graph_variable_default(
                    asset, WEIGHTS_VARIABLE_NAMES, _build_weight_literal(subsystem, len(hurt_paths)))
            if written is None:
                _ensure_mirror()
                _set_meta_float_array(float_member, [0.3] + [0.1] * len(hurt_paths), meta_path)
            applied.append("meta.weights")
        except Exception as error:
            errors["meta.weights"] = str(error)
    if applied:
        changed_assets.append(asset)
    return applied, errors


def _apply_talk_concurrency(request, changed_assets):
    concurrency = _load_asset(
        _get(request, "TalkConcurrencyObjectPath", "talkConcurrencyObjectPath"), "普通语音并发", "SoundConcurrency")
    for path in _get(request, "TalkVoiceObjectPaths", "talkVoiceObjectPaths", default=[]):
        sound = _load_asset(path, "普通角色语音", "SoundWave")
        current = _path_list(_read_property(sound, "ConcurrencySet", []))
        if _same_paths(current, [_object_path(concurrency)]):
            continue
        sound.modify()
        _write_property(sound, "ConcurrencySet", {concurrency})
        changed_assets.append(sound)


def _save_changed_assets(assets):
    unique = {}
    for asset in assets:
        unique[_normalized_path(asset)] = asset
    values = list(unique.values())
    if not values:
        return []
    saved = False
    try:
        saved = bool(unreal.EditorAssetLibrary.save_loaded_assets(values, only_if_is_dirty=True))
    except Exception:
        saved = False
    if not saved:
        for asset in values:
            package_path = _package_path(_object_path(asset))
            if not unreal.EditorAssetLibrary.save_asset(package_path, only_if_is_dirty=True):
                raise RuntimeError("保存 Unreal 资产失败：{}".format(package_path))
    return [_object_path(asset) for asset in values]


def _execute(request):
    _progress("正在准备基础配置...", 2, "", True)
    selected = set(_get(request, "SelectedStableIds", "selectedStableIds", default=[]))
    applied = []
    saved_assets = []
    execution_errors = {}
    if str(_get(request, "Mode", "mode", default="Scan")).lower() == "apply" and selected:
        changed_assets = []
        if "team.voice" in selected:
            try:
                _apply_team(request, changed_assets)
                applied.append("team.voice")
            except Exception as error:
                execution_errors["team.voice"] = str(error)
        _progress("正在写入 Item 配置...", 25)
        item_ids = sorted(stable_id for stable_id in selected if stable_id.startswith("item."))
        if item_ids:
            item_applied, item_errors = _apply_item(request, selected, changed_assets)
            applied.extend(item_applied)
            execution_errors.update(item_errors)
        _progress("正在写入 MetaSound 配置...", 45)
        meta_ids = sorted(stable_id for stable_id in selected if stable_id.startswith("meta."))
        if meta_ids:
            meta_applied, meta_errors = _apply_meta(request, selected, changed_assets)
            applied.extend(meta_applied)
            execution_errors.update(meta_errors)
        if "voice.talk-concurrency" in selected:
            try:
                _apply_talk_concurrency(request, changed_assets)
                applied.append("voice.talk-concurrency")
            except Exception as error:
                execution_errors["voice.talk-concurrency"] = str(error)
        _progress("正在保存改动的资产...", 70)
        try:
            saved_assets = _save_changed_assets(changed_assets)
        except Exception as error:
            for stable_id in selected:
                execution_errors.setdefault(stable_id, str(error))
            applied = []

    _progress("正在复查基础配置...", 85)
    entries = _build_entries(request)
    for item in entries:
        if item["stableId"] in execution_errors and item["status"] != STATUS_UNCHANGED:
            item["status"] = STATUS_ERROR
            item["errorMessage"] = execution_errors[item["stableId"]]
    selected_results = [item for item in entries if item["stableId"] in selected]
    has_errors = any(item["status"] == STATUS_ERROR for item in entries)
    succeeded = not has_errors and len(selected_results) == len(selected) and all(
        item["status"] == STATUS_UNCHANGED for item in selected_results)
    mode = str(_get(request, "Mode", "mode", default="Scan")).lower()
    error_message = ""
    if has_errors:
        error_message = "基础配置存在结构或资产错误"
    elif mode == "apply" and not succeeded:
        error_message = "部分基础配置在复扫后仍未达到目标值"
    return {
        "protocolVersion": PROTOCOL_VERSION,
        "succeeded": succeeded,
        "errorMessage": error_message,
        "characterCode": _get(request, "CharacterCode", "characterCode", default=""),
        "completedAt": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "items": entries,
        "appliedStableIds": applied,
        "savedAssets": saved_assets,
    }


request_path = os.environ.get("ZD_LIGHT_CONFIG_REQUEST", "")
result_path = os.environ.get("ZD_LIGHT_CONFIG_RESULT", "")
try:
    if not request_path or not os.path.isfile(request_path):
        raise RuntimeError("ZD_LIGHT_CONFIG_REQUEST is invalid")
    if not result_path:
        raise RuntimeError("ZD_LIGHT_CONFIG_RESULT is empty")
    with open(request_path, "r", encoding="utf-8") as source:
        request_value = json.load(source)
    if int(_get(request_value, "ProtocolVersion", "protocolVersion", default=0)) != PROTOCOL_VERSION:
        raise RuntimeError("unsupported light configuration protocol")
    _write_json_atomic(result_path, _execute(request_value))
    unreal.log("ZD light configuration completed: " + result_path)
except Exception:
    # str(error) 只有一行，界面上拿到「'NoneType' object has no attribute ...」
    # 根本看不出是哪一段配置塌的。完整回溯要进结果文件，第六步 _run() 就是这么写的。
    details = traceback.format_exc()
    unreal.log_error("ZD light configuration failed:\n" + details)
    # 结果路径为空、或者结果文件本身写不出去时，工具箱只剩进程输出可看，
    # 所以回溯同时往 stderr 抄一份；否则那边只会显示一句
    # 「没有生成结果文件。退出码：0」——一次失败长得和一次成功一模一样。
    # stderr 的编码不归我们管，抄不过去也不能盖住真正的异常。
    try:
        sys.stderr.write("ZD light configuration failed:\n" + details + "\n")
        sys.stderr.flush()
    except Exception:
        pass
    if result_path:
        try:
            _write_json_atomic(result_path, {
                "protocolVersion": PROTOCOL_VERSION,
                "succeeded": False,
                "errorMessage": details,
                "characterCode": "",
                "completedAt": datetime.datetime.now(datetime.timezone.utc).isoformat(),
                "items": [],
                "appliedStableIds": [],
                "savedAssets": [],
            })
        except Exception:
            # 连结果文件都写不出去（目录没了、盘满了）——这才是最需要说清楚的一种失败。
            unreal.log_error(
                "ZD light configuration result could not be written:\n" + traceback.format_exc())
    # 必须重抛：吞掉异常的话退出码恒为 0，工具箱那边一个失败和一次成功
    # 从退出码上分不出来。结果文件正常写出时不受影响——那一侧只认结果文件。
    raise
