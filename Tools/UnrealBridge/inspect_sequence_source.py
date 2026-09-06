import json
import os
import unreal

result_path = os.environ['ZD_BRIDGE_TEST_RESULT_PATH']
sequence = unreal.load_asset('/Game/ZDBridgeTest/Test_Sequence')
source = unreal.load_asset('/Game/GameActor2D/Misaka/Misaka_AnimMaps')
value = {'sequence': None, 'source': None}
for label, obj in (('sequence', sequence), ('source', source)):
    if obj is None:
        value[label] = {'missing': True}
        continue
    item = {'path': obj.get_path_name(), 'class': obj.get_class().get_name(), 'properties': {}}
    for name in ('anim_source', 'AnimSource', 'display_name', 'DisplayName'):
        try:
            v = obj.get_editor_property(name)
            item['properties'][name] = str(v)
        except Exception as exc:
            item['properties'][name] = 'ERR:' + str(exc)
    value[label] = item
unreal.log('ZDBridge inspect sequence/source: ' + json.dumps(value, ensure_ascii=False))
with open(result_path, 'w', encoding='utf-8') as output:
    json.dump(value, output, ensure_ascii=False, indent=2)
