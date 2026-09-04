import datetime
import json
import os
import traceback

import unreal


PROTOCOL_VERSION = 1
DIRECTION_PUBLISH = 0
DIRECTION_IMPORT = 1
OP_ADD = 0
OP_UPDATE = 1
OP_RENAME = 2
OP_DELETE = 3
OP_CONSOLIDATE = 4
MODULE_BASE_MATERIALS = 1
MODULE_SEQUENCE_FRAMES = 3
MODULE_VOICES = 5


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


def _write_progress(path, completed, total, stable_id, message):
    _write_json_atomic(path, {
        "protocolVersion": PROTOCOL_VERSION,
        "completedCount": completed,
        "totalCount": total,
        "stableId": stable_id,
        "message": message,
        "updatedAt": datetime.datetime.now(datetime.timezone.utc).isoformat(),
    })


def _package_path(object_path):
    return object_path.split(".", 1)[0] if object_path else ""


def _asset_name(object_path):
    package_path = _package_path(object_path)
    return package_path.rsplit("/", 1)[-1] if package_path else ""


def _save_asset(package_path, only_if_dirty=False):
    if not package_path or not unreal.EditorAssetLibrary.does_asset_exist(package_path):
        return
    if not unreal.EditorAssetLibrary.save_asset(package_path, only_if_is_dirty=only_if_dirty):
        raise RuntimeError("failed to save Unreal asset: " + package_path)


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
                "include_hard_management_references"):
            try:
                setattr(options, name, True)
            except Exception:
                pass
        return options


def _referencers(package_path):
    try:
        registry = unreal.AssetRegistryHelpers.get_asset_registry()
        return [str(value) for value in registry.get_referencers(
            package_path,
            _dependency_options())]
    except Exception:
        return []


def _rename_asset(source_object_path, target_object_path):
    source_package = _package_path(source_object_path)
    target_package = _package_path(target_object_path)
    if not source_package or not target_package:
        raise RuntimeError("rename operation is missing an object path")
    if source_package.lower() == target_package.lower():
        return source_object_path
    if not unreal.EditorAssetLibrary.does_asset_exist(source_package):
        raise RuntimeError("source asset does not exist: " + source_package)
    if unreal.EditorAssetLibrary.does_asset_exist(target_package):
        raise RuntimeError("rename target already exists: " + target_package)
    target_folder = target_package.rsplit("/", 1)[0]
    if not unreal.EditorAssetLibrary.does_directory_exist(target_folder):
        if not unreal.EditorAssetLibrary.make_directory(target_folder):
            raise RuntimeError("failed to create asset folder: " + target_folder)
    if not unreal.EditorAssetLibrary.rename_asset(source_package, target_package):
        raise RuntimeError("failed to rename asset: " + source_package)
    _save_asset(target_package)
    return target_package + "." + _asset_name(target_package)


def _import_file(source_file_path, target_object_path):
    if not source_file_path or not os.path.isfile(source_file_path):
        raise RuntimeError("source material file does not exist: " + str(source_file_path))
    target_package = _package_path(target_object_path)
    target_name = _asset_name(target_object_path)
    target_folder = target_package.rsplit("/", 1)[0] if "/" in target_package else ""
    if not target_folder or not target_name:
        raise RuntimeError("publish operation has no target Unreal object path")
    task = unreal.AssetImportTask()
    task.filename = source_file_path
    task.destination_path = target_folder
    task.destination_name = target_name
    task.automated = True
    task.replace_existing = True
    task.replace_existing_settings = True
    task.save = True
    unreal.AssetToolsHelpers.get_asset_tools().import_asset_tasks([task])
    imported_paths = [str(path) for path in task.imported_object_paths]
    if not imported_paths and not unreal.EditorAssetLibrary.does_asset_exist(target_package):
        raise RuntimeError("Unreal did not import the material: " + source_file_path)
    _save_asset(target_package)
    return imported_paths[0] if imported_paths else target_package + "." + target_name


def _consolidate_asset(source_object_path, target_object_path):
    source_package = _package_path(source_object_path)
    target_package = _package_path(target_object_path)
    if not source_package or not target_package:
        raise RuntimeError("consolidate operation is missing an object path")
    if source_package.lower() == target_package.lower():
        return target_object_path
    source_asset = unreal.EditorAssetLibrary.load_asset(source_package)
    target_asset = unreal.EditorAssetLibrary.load_asset(target_package)
    if source_asset is None:
        raise RuntimeError("redirect source asset does not exist: " + source_package)
    if target_asset is None:
        raise RuntimeError("redirect target asset does not exist: " + target_package)
    referencer_packages = _referencers(source_package)
    result = unreal.EditorAssetLibrary.consolidate_assets(target_asset, [source_asset])
    if result is False:
        raise RuntimeError("failed to redirect asset references: " + source_package)
    _save_asset(target_package)
    for referencer_package in referencer_packages:
        _save_asset(referencer_package, only_if_dirty=True)
    return target_package + "." + _asset_name(target_package)


def _export_asset(plan_path, operation):
    object_path = _get(operation, "SourceObjectPath", "sourceObjectPath", default="")
    package_path = _package_path(object_path)
    asset = unreal.EditorAssetLibrary.load_asset(package_path)
    if asset is None:
        raise RuntimeError("cannot load Unreal asset: " + package_path)
    stable_id = _get(operation, "StableId", "stableId", default="asset")
    module = int(_get(operation, "Module", "module", default=0))
    output_folder = os.path.join(os.path.dirname(plan_path), "Imported")
    os.makedirs(output_folder, exist_ok=True)
    extension = ".wav" if module == MODULE_VOICES else ".png"
    if module not in (MODULE_BASE_MATERIALS, MODULE_SEQUENCE_FRAMES, MODULE_VOICES):
        extension = ".json"
    output_path = os.path.join(output_folder, stable_id.replace(":", "_") + extension)
    if extension == ".json":
        payload = _get(operation, "PayloadJson", "payloadJson", default="{}")
        try:
            payload_value = json.loads(payload)
        except Exception:
            payload_value = {"rawPayload": payload}
        _write_json_atomic(output_path, payload_value)
        return output_path
    task = unreal.AssetExportTask()
    task.object = asset
    task.filename = output_path
    task.automated = True
    task.prompt = False
    task.replace_identical = True
    if not unreal.Exporter.run_asset_export_task(task) or not os.path.isfile(output_path):
        raise RuntimeError("failed to export Unreal asset: " + package_path)
    return output_path


def _execute_publish(operation):
    kind = int(_get(operation, "Kind", "kind", default=OP_UPDATE))
    source_object_path = _get(operation, "SourceObjectPath", "sourceObjectPath", default="")
    target_object_path = _get(operation, "TargetObjectPath", "targetObjectPath", default="")
    source_file_path = _get(operation, "SourceFilePath", "sourceFilePath", default="")
    object_path = source_object_path
    if kind == OP_CONSOLIDATE:
        return _consolidate_asset(source_object_path, target_object_path)
    if kind == OP_DELETE:
        package_path = _package_path(source_object_path)
        if not package_path or not unreal.EditorAssetLibrary.delete_asset(package_path):
            raise RuntimeError("failed to delete Unreal asset: " + package_path)
        return source_object_path
    if kind == OP_RENAME:
        object_path = _rename_asset(source_object_path, target_object_path)
    elif target_object_path:
        object_path = target_object_path
    if source_file_path:
        object_path = _import_file(source_file_path, object_path)
    elif kind in (OP_ADD, OP_UPDATE):
        raise RuntimeError("semantic Unreal property updates require a mapped template property")
    return object_path


def _execute():
    plan_path = os.environ.get("ZD_BRIDGE_PLAN_PATH", "")
    progress_path = os.environ.get("ZD_BRIDGE_PROGRESS_PATH", "")
    result_path = os.environ.get("ZD_BRIDGE_RESULT_PATH", "")
    if not plan_path or not os.path.isfile(plan_path):
        raise RuntimeError("ZD_BRIDGE_PLAN_PATH is invalid")
    if not progress_path or not result_path:
        raise RuntimeError("progress and result paths are required")
    with open(plan_path, "r", encoding="utf-8") as source:
        plan = json.load(source)
    if int(_get(plan, "ProtocolVersion", "protocolVersion", default=0)) != PROTOCOL_VERSION:
        raise RuntimeError("unsupported execution plan protocol")
    direction = int(_get(plan, "Direction", "direction", default=DIRECTION_PUBLISH))
    operations = list(_get(plan, "Operations", "operations", default=[]))
    results = []
    total = len(operations)
    for index, operation in enumerate(operations):
        stable_id = _get(operation, "StableId", "stableId", default="")
        display_name = _get(operation, "DisplayName", "displayName", default=stable_id)
        _write_progress(progress_path, index, total, stable_id, "Processing " + display_name)
        try:
            if direction == DIRECTION_PUBLISH:
                object_path = _execute_publish(operation)
                output_path = ""
            else:
                object_path = _get(operation, "SourceObjectPath", "sourceObjectPath", default="")
                output_path = _export_asset(plan_path, operation)
            results.append({
                "stableId": stable_id,
                "succeeded": True,
                "message": "completed",
                "objectPath": object_path,
                "originIdentity": "",
                "outputFilePath": output_path,
            })
        except Exception as error:
            results.append({
                "stableId": stable_id,
                "succeeded": False,
                "message": str(error),
                "objectPath": "",
                "originIdentity": "",
                "outputFilePath": "",
            })
        _write_progress(progress_path, index + 1, total, stable_id, "Completed " + display_name)
    succeeded = all(item["succeeded"] for item in results)
    _write_json_atomic(result_path, {
        "protocolVersion": PROTOCOL_VERSION,
        "succeeded": succeeded,
        "errorMessage": "" if succeeded else "one or more operations failed",
        "completedAt": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "items": results,
    })
    if not succeeded:
        raise RuntimeError("one or more Unreal bridge operations failed")


try:
    _execute()
except Exception:
    unreal.log_error("ZD Bridge execution failed:\n" + traceback.format_exc())
    raise
