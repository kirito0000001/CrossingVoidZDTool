import json
import os
import unreal

result_path = os.environ.get('ZD_BRIDGE_PROBE_RESULT_PATH', '')
bridge = getattr(unreal, 'ZDBridgeLibrary', None)
methods = []
if bridge is not None:
    for name in dir(bridge):
        if 'paper' in name.lower() or 'animation' in name.lower() or 'supported' in name.lower():
            methods.append(name)
assets = []
for path in unreal.EditorAssetLibrary.list_assets('/Game/GameActor2D/Misaka', recursive=True, include_folder=False):
    if 'Frame' in path or 'AnimMaps' in path:
        assets.append(path)
value = {
    'bridgeLoaded': bridge is not None,
    'bridgeClass': str(bridge) if bridge is not None else '',
    'methods': methods,
    'candidateAssets': assets[:30],
}
with open(result_path, 'w', encoding='utf-8') as output:
    json.dump(value, output, ensure_ascii=False, indent=2)
unreal.log('ZDBridge probe: ' + json.dumps(value, ensure_ascii=False))
