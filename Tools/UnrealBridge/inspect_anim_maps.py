import json
import os
import traceback
import unreal

out_path = os.environ.get('ZD_INSPECT_RESULT_PATH', r'C:/CrossingVoid/Intermediate/ZDToolboxBridge/inspect-result.json')
root = '/Game/GameActor2D/Misaka'
result = {'root': root, 'succeeded': False}
def text(value):
    try:
        return value.get_path_name() if value else ''
    except Exception:
        return str(value)
def inspect_asset(path):
    obj = unreal.load_asset(path)
    if obj is None:
        return {'path': path, 'missing': True}
    item = {'path': path, 'class': obj.get_class().get_name()}
    for name in ['anim_data', 'anim_notifies', 'anim_source', 'paper_flipbook', 'flipbook', 'category', 'directional_sequence', 'key_frames', 'frames_per_second']:
        try:
            value = obj.get_editor_property(name)
            entry = {'name': name, 'type': type(value).__name__, 'text': str(value)[:1000]}
            try:
                values = list(value)
            except Exception:
                values = []
            if values:
                entry['items'] = []
                for element in values[:5]:
                    detail = {'type': type(element).__name__, 'dir': [x for x in dir(element) if not x.startswith('__')]}
                    for field in ['animation', 'composite_layer_animations', 'mirror_mode', 'mirrored_key_frames', 'vertical_mirrored_key_frames', 'notify', 'time']:
                        try:
                            detail[field] = str(element.get_editor_property(field))[:1000]
                        except Exception:
                            pass
                    entry['items'].append(detail)
            item[name] = entry
        except Exception as exc:
            item[name + 'Error'] = str(exc)
    return item
try:
    result['animMaps'] = inspect_asset(root + '/Misaka_AnimMaps.Misaka_AnimMaps')
    result['assets'] = []
    for path in unreal.EditorAssetLibrary.list_assets(root + '/AnimSequences', recursive=True, include_folder=False)[:5]:
        result['assets'].append(inspect_asset(path))
    for class_path in ['/Script/Paper2DEditor.PaperSpriteFactory', '/Script/Paper2DEditor.PaperFlipbookFactory', '/Script/PaperZD.PaperZDAnimSequence_Flipbook']:
        try:
            result.setdefault('classes', []).append({'path': class_path, 'class': str(unreal.load_class(None, class_path))})
        except Exception as exc:
            result.setdefault('classes', []).append({'path': class_path, 'error': str(exc)})
    result['succeeded'] = True
except Exception as exc:
    result['error'] = str(exc)
    result['traceback'] = traceback.format_exc()
finally:
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    with open(out_path, 'w', encoding='utf-8') as f:
        json.dump(result, f, ensure_ascii=False, indent=2)
    unreal.log('ZD AnimMaps inspection result written to ' + out_path)
    if not result['succeeded']:
        unreal.log_error(result.get('traceback', result.get('error', 'inspection failed')))
