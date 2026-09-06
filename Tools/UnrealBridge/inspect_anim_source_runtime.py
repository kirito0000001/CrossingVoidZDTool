import json
import os
import unreal
source = unreal.load_asset('/Game/GameActor2D/Misaka/Misaka_AnimMaps')
value = {'class': source.get_class().get_name(), 'properties': [], 'dir': [name for name in dir(source) if 'anim' in name.lower() or 'sequence' in name.lower()]}
for name in ('supported_animations', 'SupportedAnimations', 'animations', 'Animations', 'animation_sequences', 'AnimationSequences', 'registered_animations', 'RegisteredAnimations'):
    try:
        entries = source.get_editor_property(name)
        value['properties'].append({'name': name, 'type': str(type(entries)), 'count': len(entries), 'entries': [str(entry) for entry in entries[:5]]})
    except Exception as exc:
        value['properties'].append({'name': name, 'error': str(exc)})
with open(os.environ['ZD_BRIDGE_PROBE_RESULT_PATH'], 'w', encoding='utf-8') as output:
    json.dump(value, output, ensure_ascii=False, indent=2)
unreal.log('AnimSource runtime: ' + json.dumps(value, ensure_ascii=False))
