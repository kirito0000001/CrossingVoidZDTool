import datetime
import hashlib
import json
import os
import traceback

import unreal


PROTOCOL_VERSION = 1
DEFAULT_ROOTS = [
    "/Game/GameActor2D",
    "/Game/AssetMaterial/ImageS/CharaterS",
    "/Game/ITems/CharItemS",
]
MODULE_CHARACTER_INFO = 0
MODULE_BASE_MATERIALS = 1
MODULE_SKILLS = 2
MODULE_SEQUENCE_FRAMES = 3
MODULE_BUFFS = 4
MODULE_VOICES = 5


def _text(value):
    if value is None:
        return ""
    try:
        return str(value)
    except Exception:
        return ""


def _property(value, *names):
    for name in names:
        try:
            result = getattr(value, name)
            if callable(result):
                result = result()
            text = _text(result).strip()
            if text:
                return text
        except Exception:
            pass
    return ""


def _write_json_atomic(path, value):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    temporary_path = path + ".tmp"
    with open(temporary_path, "w", encoding="utf-8") as output:
        json.dump(value, output, ensure_ascii=False, indent=2)
    os.replace(temporary_path, path)


def _load_roots():
    raw = os.environ.get("ZD_BRIDGE_SCAN_ROOTS", "")
    if not raw:
        return list(DEFAULT_ROOTS)
    try:
        roots = [str(item).rstrip("/") for item in json.loads(raw) if str(item).strip()]
        return roots or list(DEFAULT_ROOTS)
    except Exception:
        return list(DEFAULT_ROOTS)


def _dependency_options():
    try:
        return unreal.AssetRegistryDependencyOptions(
            include_soft_package_references=True,
            include_hard_package_references=True,
            include_searchable_names=False,
            include_soft_management_references=True,
            include_hard_management_references=True,
        )
    except Exception:
        options = unreal.AssetRegistryDependencyOptions()
        for name in (
            "include_soft_package_references",
            "include_hard_package_references",
            "include_soft_management_references",
            "include_hard_management_references",
        ):
            try:
                setattr(options, name, True)
            except Exception:
                pass
        return options


def _get_links(registry, method_name, package_name, options):
    try:
        method = getattr(registry, method_name)
        return sorted({_text(value) for value in method(package_name, options) if _text(value)})
    except Exception:
        return []


def _object_path(asset_data):
    path = _property(asset_data, "get_soft_object_path", "soft_object_path", "object_path")
    if path:
        return path
    package_name = _property(asset_data, "package_name")
    asset_name = _property(asset_data, "asset_name")
    return "%s.%s" % (package_name, asset_name) if package_name and asset_name else package_name


def _asset_class(asset_data):
    # asset_class_path 是 TopLevelAssetPath 结构体，str() 出来带对象内存地址，
    # 每次扫描都不一样；这个值会写进 assetClass 参与内容比对，必须取稳定的 asset_name。
    for name in ("asset_class_path", "asset_class"):
        try:
            raw = getattr(asset_data, name)
        except Exception:
            continue
        value = _text(getattr(raw, "asset_name", "")).strip() or _text(raw).strip()
        if not value:
            continue
        if "." in value:
            value = value.rsplit(".", 1)[-1]
        return value
    return ""


def _package_disk_files(package_name):
    if not package_name.lower().startswith("/game/"):
        return []
    relative_name = package_name[6:].replace("/", os.sep)
    base_path = os.path.join(unreal.Paths.project_content_dir(), relative_name)
    return [
        path
        for path in (base_path + ".uasset", base_path + ".uexp", base_path + ".ubulk")
        if os.path.isfile(path)
    ]


def _package_hash(package_name, object_path, dependencies, referencers):
    digest = hashlib.sha256()
    files = _package_disk_files(package_name)
    for path in files:
        with open(path, "rb") as source:
            while True:
                block = source.read(1024 * 1024)
                if not block:
                    break
                digest.update(block)
    if not files:
        digest.update(object_path.encode("utf-8"))
        digest.update("\n".join(dependencies).encode("utf-8"))
        digest.update("\n".join(referencers).encode("utf-8"))
    return digest.hexdigest().upper()


def _module_for(asset_data, object_path):
    lowered_path = object_path.lower()
    lowered_class = _asset_class(asset_data).lower()
    if "soundwave" in lowered_class or "/sound/" in lowered_path:
        return MODULE_VOICES
    if "paperzd" in lowered_class or "paperflipbook" in lowered_class or "papersprite" in lowered_class:
        return MODULE_SEQUENCE_FRAMES
    if "/buff/" in lowered_path or "_buff" in lowered_path:
        return MODULE_BUFFS
    if "texture" in lowered_class or "material" in lowered_class:
        return MODULE_BASE_MATERIALS
    if "skill" in lowered_path:
        return MODULE_SKILLS
    return MODULE_CHARACTER_INFO


def _package_guid(asset_data):
    guid = _property(asset_data, "package_guid", "package_flags")
    if guid and guid.strip("0-{}") != "":
        return guid
    return ""


def _assets_under_roots(registry, roots):
    result = {}
    for root in roots:
        try:
            assets = registry.get_assets_by_path(root, recursive=True)
        except Exception:
            assets = []
        for asset_data in assets:
            package_name = _property(asset_data, "package_name")
            if package_name:
                result[package_name.lower()] = asset_data
    return result


def _discover_character_packages(registry, assets_by_package, roots, character_code):
    lowered_code = character_code.lower()
    selected = {
        package_key
        for package_key, asset_data in assets_by_package.items()
        if lowered_code in package_key
        or lowered_code in _property(asset_data, "asset_name").lower()
        or lowered_code in _object_path(asset_data).lower()
    }
    options = _dependency_options()
    frontier = set(selected)
    for _ in range(2):
        next_frontier = set()
        for package_key in frontier:
            asset_data = assets_by_package.get(package_key)
            if asset_data is None:
                continue
            package_name = _property(asset_data, "package_name")
            links = _get_links(registry, "get_dependencies", package_name, options)
            links += _get_links(registry, "get_referencers", package_name, options)
            for linked_package in links:
                linked_key = linked_package.lower()
                if linked_key in assets_by_package and linked_key not in selected:
                    if any(linked_key.startswith(root.lower() + "/") for root in roots):
                        selected.add(linked_key)
                        next_frontier.add(linked_key)
        frontier = next_frontier
        if not frontier:
            break
    return selected, options


def _scan_plugin_sequences(source_path):
    if not source_path:
        return [], {"enabled": False, "sourcePath": "", "sequenceCount": 0, "excludedGeneratedSequenceCount": 0}
    source = unreal.EditorAssetLibrary.load_asset(source_path)
    if source is None or not hasattr(unreal, "ZDBridgeLibrary"):
        return [], {"enabled": False, "sourcePath": source_path, "sequenceCount": 0, "excludedGeneratedSequenceCount": 0}
    raw = unreal.ZDBridgeLibrary.scan_animation_source(source)
    payload = json.loads(str(raw))
    if payload.get("error"):
        raise RuntimeError("ZDBridge 动画扫描失败：%s" % payload["error"])
    plugin_items = []
    for item in payload.get("sequences", []):
        name = _text(item.get("name"))
        object_path = _text(item.get("assetPath"))
        plugin_items.append({
            "syncId": "",
            "parentStableId": "",
            "module": MODULE_SEQUENCE_FRAMES,
            "displayName": name,
            "contentHash": hashlib.sha256(json.dumps(item, ensure_ascii=False, sort_keys=True).encode("utf-8")).hexdigest().upper(),
            "payloadJson": json.dumps({"source": "ZDBridge.AnimationSourceScan", **item}, ensure_ascii=False, sort_keys=True, separators=(",", ":")),
            "objectPath": object_path,
            "packageName": object_path.split(".", 1)[0],
            "originIdentity": "object:" + object_path.lower(),
            "assetClass": _text(item.get("assetClass")),
            "normalizedName": name,
            "referencers": [],
            "dependencies": [],
        })
    return plugin_items, {
        "enabled": True,
        "sourcePath": source_path,
        "sequenceCount": int(payload.get("sequenceCount", len(plugin_items))),
        "excludedGeneratedSequenceCount": int(payload.get("excludedGeneratedSequenceCount", 0)),
        "protocolName": payload.get("protocolName", "ZDBridge.AnimationSourceScan"),
        "protocolVersion": int(payload.get("protocolVersion", 2)),
    }


def _scan():
    character_code = os.environ.get("ZD_BRIDGE_CHARACTER_CODE", "").strip()
    output_path = os.environ.get("ZD_BRIDGE_SCAN_OUTPUT", "").strip()
    project_path = os.environ.get("ZD_BRIDGE_PROJECT_PATH", "").strip()
    if not character_code:
        raise RuntimeError("ZD_BRIDGE_CHARACTER_CODE is required")
    if not output_path:
        raise RuntimeError("ZD_BRIDGE_SCAN_OUTPUT is required")

    roots = _load_roots()
    registry = unreal.AssetRegistryHelpers.get_asset_registry()
    unreal.log('[ZD Bridge Scan] stage=asset_registry_ready character=%s mode=nonblocking' % character_code)
    assets_by_package = _assets_under_roots(registry, roots)
    unreal.log('[ZD Bridge Scan] stage=discover_packages_start character=%s rootAssets=%d' % (character_code, len(assets_by_package)))
    selected_packages, options = _discover_character_packages(
        registry,
        assets_by_package,
        roots,
        character_code,
    )
    unreal.log('[ZD Bridge Scan] stage=discover_packages_ready character=%s packages=%d' % (character_code, len(selected_packages)))

    items = []
    for package_key in sorted(selected_packages):
        asset_data = assets_by_package[package_key]
        package_name = _property(asset_data, "package_name")
        object_path = _object_path(asset_data)
        asset_name = _property(asset_data, "asset_name") or object_path.rsplit("/", 1)[-1].split(".", 1)[0]
        dependencies = _get_links(registry, "get_dependencies", package_name, options)
        referencers = _get_links(registry, "get_referencers", package_name, options)
        package_guid = _package_guid(asset_data)
        origin_identity = package_guid or ("object:" + object_path.lower())
        payload = {
            "assetClass": _asset_class(asset_data),
            "packageName": package_name,
            "dependencies": dependencies,
            "referencers": referencers,
        }
        items.append({
            "syncId": "",
            "parentStableId": "",
            "module": _module_for(asset_data, object_path),
            "displayName": asset_name,
            "contentHash": _package_hash(package_name, object_path, dependencies, referencers),
            "payloadJson": json.dumps(payload, ensure_ascii=False, sort_keys=True, separators=(",", ":")),
            "objectPath": object_path,
            "packageName": package_name,
            "originIdentity": origin_identity,
            "assetClass": _asset_class(asset_data),
            "normalizedName": asset_name,
            "referencers": referencers,
            "dependencies": dependencies,
        })

    unreal.log('[ZD Bridge Scan] stage=asset_items_ready character=%s items=%d' % (character_code, len(items)))
    animation_source_path = os.environ.get(
        "ZD_BRIDGE_ANIMATION_SOURCE_PATH",
        "/Game/GameActor2D/%s/%s_AnimMaps" % (character_code, character_code),
    ).strip()
    unreal.log('[ZD Bridge Scan] stage=plugin_sequence_start source=%s' % animation_source_path)
    plugin_items, plugin_scan = _scan_plugin_sequences(animation_source_path)
    unreal.log('[ZD Bridge Scan] stage=plugin_sequence_ready sequences=%d excluded=%d' % (len(plugin_items), plugin_scan.get('excludedGeneratedSequenceCount', 0)))
    if plugin_scan.get("enabled"):
        # 插件扫描只负责正式 AnimSequence；不能把 Material 下旧 Sprite、Flipbook
        # 一并从标准资产扫描结果删除，否则第五步永远看不到需要清理的旧资产。
        items = [item for item in items if not (
            item["module"] == MODULE_SEQUENCE_FRAMES and
            "/animsequences/" in item.get("objectPath", "").lower()
        )]
        items.extend(plugin_items)

    unreal.log('[ZD Bridge Scan] stage=write_result path=%s items=%d' % (output_path, len(items)))
    _write_json_atomic(output_path, {
        "protocolVersion": PROTOCOL_VERSION,
        "characterCode": character_code,
        "projectPath": project_path,
        "generatedAt": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "sequenceScan": plugin_scan,
        "items": items,
    })
    unreal.log("ZD Bridge scan completed: %s assets" % len(items))


try:
    _scan()
except Exception:
    unreal.log_error("ZD Bridge scan failed:\n" + traceback.format_exc())
    raise
