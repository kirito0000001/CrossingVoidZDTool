import json
import os
import unreal

source_path = os.environ.get("ZD_SCAN_ANIMATION_SOURCE", "/Game/GameActor2D/Misaka/Misaka_AnimMaps")
protocol_version = 2
result_path = os.environ.get("ZD_SCAN_RESULT_PATH", "")
source = unreal.EditorAssetLibrary.load_asset(source_path)
raw = unreal.ZDBridgeLibrary.scan_animation_source(source)
try:
    result = json.loads(str(raw))
    result["protocolVersion"] = protocol_version
    result["sourcePath"] = source_path
    result.setdefault("sequences", [])
    result.setdefault("sequenceCount", len(result["sequences"]))
    result.setdefault("excludedGeneratedSequenceCount", 0)
except Exception as exc:
    result = {"protocolVersion": protocol_version, "sourcePath": source_path, "error": "invalid plugin scan response: " + str(exc), "raw": str(raw)}
if result_path:
    folder = os.path.dirname(result_path)
    if folder:
        os.makedirs(folder, exist_ok=True)
    with open(result_path, "w", encoding="utf-8") as output:
        json.dump(result, output, ensure_ascii=False, indent=2)
unreal.log("ZD Bridge animation source scan: " + json.dumps(result, ensure_ascii=False))
