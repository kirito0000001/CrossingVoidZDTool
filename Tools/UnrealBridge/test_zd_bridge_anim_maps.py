import json
import os
import unreal

result_path = os.environ['ZD_BRIDGE_TEST_RESULT_PATH']
source = unreal.load_asset('/Game/GameActor2D/Misaka/Misaka_AnimMaps')
sequence = unreal.load_asset('/Game/ZDBridgeTest/Test_Sequence')
if source is None or sequence is None:
    raise RuntimeError('AnimMaps or test sequence not found')
unreal.log('ZDBridge AnimMaps inputs: source=%s source_class=%s sequence=%s sequence_class=%s' % (source.get_path_name(), source.get_class().get_name(), sequence.get_path_name(), sequence.get_class().get_name()))
status, error = '', ''
try:
    status = str(unreal.ZDBridgeLibrary.ensure_sequence_animation_source(source, sequence))
except Exception as exc:
    error = str(exc)
if status.startswith('error:'):
    error = status
    status = ''
if not status:
    raise RuntimeError('AnimMaps update failed: ' + error)
unreal.EditorAssetLibrary.save_asset('/Game/GameActor2D/Misaka/Misaka_AnimMaps', only_if_is_dirty=False)
unreal.EditorAssetLibrary.save_asset('/Game/ZDBridgeTest/Test_Sequence', only_if_is_dirty=False)
with open(result_path, 'w', encoding='utf-8') as output:
    json.dump({'success': True, 'status': status, 'error': error}, output, ensure_ascii=False, indent=2)
unreal.log('ZDBridge AnimMaps test: success=True status=%s error=%s' % (status, error))
