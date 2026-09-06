import json
import os
import unreal

result_path = os.environ['ZD_BRIDGE_TEST_RESULT_PATH']
texture = unreal.load_asset('/Game/GameActor2D/Misaka/Material/Sk1/Sk1_Frame001')
if texture is None:
    raise RuntimeError('test texture not found')
bridge = unreal.ZDBridgeLibrary
folder = '/Game/ZDBridgeTest'
sprite, sprite_error = bridge.create_paper_sprite_from_texture(texture, folder, 'Frame001_Sprite')
if sprite is None:
    raise RuntimeError('sprite failed: ' + str(sprite_error))
flipbook, flipbook_error = bridge.create_paper_flipbook_from_sprites([sprite], [1], 12.0, folder, 'Test_Flipbook')
if flipbook is None:
    raise RuntimeError('flipbook failed: ' + str(flipbook_error))
source = unreal.load_asset('/Game/GameActor2D/Misaka/Misaka_AnimMaps')
if source is None:
    raise RuntimeError('test AnimMaps source not found')
sequence, sequence_error = bridge.create_paper_zd_sequence(flipbook, source, folder, 'Test_Sequence')
if sequence is None:
    raise RuntimeError('sequence failed: ' + str(sequence_error))
result = {
    'sprite': sprite.get_path_name(),
    'flipbook': flipbook.get_path_name(),
    'sequence': sequence.get_path_name(),
}
with open(result_path, 'w', encoding='utf-8') as output:
    json.dump(result, output, ensure_ascii=False, indent=2)
unreal.log('ZDBridge asset test: ' + json.dumps(result, ensure_ascii=False))
