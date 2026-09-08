"""在打开着的 Unreal 编辑器里定位一批资产。

只在编辑器已经开着时有意义：离线启一个无头编辑器再让它「定位」到某个资产，
既慢又没人看得见，所以工具箱那边只走在线路径调用这个脚本。

结果写进 ZD_BROWSE_RESULT，工具箱据此提示定位到了几个、哪些没找到。
"""

import json
import os
import traceback

import unreal


def _text(value):
    if value is None:
        return ""
    try:
        return str(value)
    except Exception:
        return ""


def _package_path(object_path):
    """定位要的是包路径，去掉 `.资产名` 那一段。"""
    text = _text(object_path).strip()
    if not text:
        return ""
    dot = text.find(".")
    return text[:dot] if dot > 0 else text


def _run():
    result_path = os.environ.get("ZD_BROWSE_RESULT", "")
    raw_paths = os.environ.get("ZD_BROWSE_OBJECT_PATHS", "[]")
    result = {
        "protocolVersion": 1,
        "succeeded": False,
        "errorMessage": "",
        "browsedCount": 0,
        "missingPaths": [],
    }

    try:
        requested = json.loads(raw_paths) if raw_paths else []
        packages = []
        missing = []
        for object_path in requested:
            package = _package_path(object_path)
            if not package:
                continue
            # 定位之前先确认资产真的存在，否则编辑器只会静默地什么都不做，
            # 界面上看起来就像按钮没反应。
            if unreal.EditorAssetLibrary.does_asset_exist(package):
                packages.append(package)
            else:
                missing.append(package)

        if packages:
            unreal.EditorAssetLibrary.sync_browser_to_objects(packages)

        result["browsedCount"] = len(packages)
        result["missingPaths"] = missing
        result["succeeded"] = True
    except Exception:
        result["errorMessage"] = traceback.format_exc()
        unreal.log_error("ZDToolbox browse assets failed:\n" + result["errorMessage"])

    if result_path:
        os.makedirs(os.path.dirname(result_path), exist_ok=True)
        with open(result_path, "w", encoding="utf-8") as handle:
            json.dump(result, handle, ensure_ascii=False, indent=2)


_run()
