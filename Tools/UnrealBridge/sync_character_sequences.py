import datetime
import json
import os
import traceback
import unreal

PROTOCOL_VERSION = 1

def _write(path, value):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    temp = path + '.tmp'
    with open(temp, 'w', encoding='utf-8') as f:
        json.dump(value, f, ensure_ascii=False, indent=2)
    os.replace(temp, path)

def _load(path):
    with open(path, 'r', encoding='utf-8') as f:
        value = json.load(f)
    protocol = value.get('protocolVersion', value.get('ProtocolVersion', 0))
    if int(protocol) != PROTOCOL_VERSION:
        raise RuntimeError('unsupported sequence sync plan protocol: %s' % protocol)
    # Accept both camelCase (current) and PascalCase (plans produced by older builds).
    def get(item, name, default=None):
        return item.get(name, item.get(name[0].upper() + name[1:], default))
    value['CharacterCode'] = get(value, 'characterCode', '')
    value['CharacterBlueprintPath'] = get(value, 'characterBlueprintPath', '')
    value['AnimMapsPath'] = get(value, 'animMapsPath', '')
    actions = get(value, 'actions', []) or []
    for action in actions:
        for name in ('actionCode', 'displayName', 'fps', 'blueprintProperty', 'animMapsEntryName', 'targetSequencePath', 'targetMaterialFolder'):
            action[name] = get(action, name, action.get(name, ''))
        frames = get(action, 'frames', []) or []
        for frame in frames:
            for name in ('index', 'filePath', 'durationFrames', 'isBlank', 'voiceFileName'):
                frame[name] = get(frame, name, frame.get(name, ''))
        action['frames'] = frames
    value['actions'] = actions
    return value

def _package(path):
    return path.split('.', 1)[0]

def _require(path, action):
    asset = unreal.load_asset(path)
    if asset is None:
        raise RuntimeError('%s: asset not found: %s' % (action, path))
    return asset

def _save(path):
    if not unreal.EditorAssetLibrary.save_asset(_package(path), only_if_is_dirty=False):
        raise RuntimeError('failed to save asset: ' + path)

def _asset_class(name):
    try:
        return unreal.load_class(None, name)
    except Exception:
        return None

def _create_asset(name, folder, class_names, factory_names, action):
    tools = unreal.AssetToolsHelpers.get_asset_tools()
    asset_class = next((value for value in (_asset_class(item) for item in class_names) if value), None)
    factory_class = next((value for value in (_asset_class(item) for item in factory_names) if value), None)
    if asset_class is None or factory_class is None:
        raise RuntimeError('%s: PaperZD factory API unavailable (classes=%s)' % (action, ','.join(class_names)))
    factory = unreal.new_object(factory_class)
    asset = tools.create_asset(name, folder, asset_class, factory)
    if asset is None:
        raise RuntimeError('%s: failed to create asset %s/%s' % (action, folder, name))
    return asset

def _create_sequence(name, folder, source, action):
    """Create a PaperZD sequence through the project's editor factory.

    PaperZD's factory normally opens a source picker. Supplying TargetAnimSource
    before create_asset keeps this operation unattended and makes the created
    sequence a PaperZDAnimSequence_Flipbook instead of a generic UObject.
    """
    sequence_class = _asset_class('/Script/PaperZD.PaperZDAnimSequence_Flipbook')
    factory_class = _asset_class('/Script/PaperZDEditor.PaperZDAnimSequenceFactory')
    if sequence_class is None or factory_class is None:
        raise RuntimeError('%s: PaperZDAnimSequence_Flipbook or its editor factory is unavailable' % action)
    factory = unreal.new_object(factory_class)
    configured = False
    for property_name in ('target_anim_source', 'TargetAnimSource'):
        try:
            setattr(factory, property_name, source)
            configured = True
            break
        except Exception:
            try:
                factory.set_editor_property(property_name, source)
                configured = True
                break
            except Exception:
                pass
    if not configured:
        raise RuntimeError('%s: PaperZD sequence factory has no writable TargetAnimSource property' % action)
    sequence = unreal.AssetToolsHelpers.get_asset_tools().create_asset(name, folder, sequence_class, factory)
    if sequence is None:
        raise RuntimeError('%s: failed to create PaperZD sequence %s/%s' % (action, folder, name))
    return sequence

def _set_first(obj, names, value, action):
    for name in names:
        try:
            obj.set_editor_property(name, value)
            return name
        except Exception:
            pass
    raise RuntimeError('%s: no writable property among %s' % (action, ','.join(names)))

def _get_first(obj, names, action):
    for name in names:
        try:
            return name, obj.get_editor_property(name)
        except Exception:
            pass
    raise RuntimeError('%s: no readable property among %s' % (action, ','.join(names)))

def _capture_notification_state(sequence):
    """Read notification-related properties without changing the sequence."""
    state = {}
    for name in ('notifies', 'notify_tracks', 'anim_notifies', 'animation_notifies'):
        try:
            value = sequence.get_editor_property(name)
            state[name] = repr(value)
        except Exception:
            pass
    return state

def _assert_notification_state_unchanged(sequence, before, action):
    if not before:
        unreal.log('SequenceSync: notification properties are not exposed; no notification mutation was requested (%s)' % action)
        return
    after = _capture_notification_state(sequence)
    changed = [name for name, value in before.items() if after.get(name) != value]
    if changed:
        raise RuntimeError('%s: animation notification state changed while updating AnimData: %s' % (action, ','.join(changed)))

def _append_anim_maps_sequence(source, sequence, action_name):
    """Mirror PaperZD's AnimMaps Supported Animations '+' operation.

    PaperZD versions expose this as an array property with slightly different
    Python names. In the common build the array contains sequence references;
    in some builds it is a struct array whose writable animation member is
    handled by the fallback below.
    """
    candidates = ('supported_animations', 'SupportedAnimations', 'animations', 'Animations')
    for property_name in candidates:
        try:
            entries = list(source.get_editor_property(property_name) or [])
        except Exception:
            continue
        for index, entry in enumerate(entries):
            if entry == sequence:
                return 'existing', property_name
            for field_name in ('animation', 'Animation', 'sequence', 'Sequence'):
                try:
                    current = entry.get_editor_property(field_name)
                    if current == sequence:
                        return 'existing', property_name
                except Exception:
                    pass
        # The editor's '+' button appends a new element. For object-reference
        # arrays the element is the sequence itself, which is the usual
        # PaperZDAnimationSource_Flipbook representation.
        try:
            source.set_editor_property(property_name, entries + [sequence])
            return 'created', property_name
        except Exception:
            pass
    raise RuntimeError("%s: AnimMaps Supported Animations array is not writable; expected the editor '+' operation" % action_name)

def _select_assets(assets):
    """Focus the Content Browser on assets when the Python API supports it.

    EditorUtilityLibrary exposes reading the selection, not setting it. Merely
    syncing the browser is therefore not equivalent to the user's multi-select
    operation and must not be reported as a successful selection.
    """
    try:
        unreal.EditorAssetLibrary.sync_browser_to_objects(
            [asset.get_path_name() for asset in assets])
        unreal.log_warning(
            'SequenceSync: Content Browser was focused, but Python cannot set its selection')
    except Exception:
        return False
    return False


def _bridge_class():
    return getattr(unreal, 'ZDBridgeLibrary', None)


def _bridge_call(method_name, args, action):
    bridge = _bridge_class()
    if bridge is None:
        raise RuntimeError('%s: ZDBridge plugin is not loaded in Unreal Editor' % action)
    method = getattr(bridge, method_name, None)
    if method is None:
        raise RuntimeError('%s: ZDBridge function is unavailable: %s' % (action, method_name))
    try:
        result = method(*args)
    except Exception as error:
        raise RuntimeError('%s: ZDBridge.%s failed: %s' % (action, method_name, error))
    values = result if isinstance(result, tuple) else (result,)
    error_text = next((str(value) for value in values[1:] if isinstance(value, str) and value), '')
    if error_text:
        raise RuntimeError('%s: %s' % (action, error_text))
    primary = values[0] if values else None
    if primary is None or primary is False:
        raise RuntimeError('%s: ZDBridge.%s returned no asset' % (action, method_name))
    return primary, values


def _create_sprite_from_texture(texture, folder, name, action):
    sprite, _ = _bridge_call('create_paper_sprite_from_texture', [texture, folder, name], action)
    unreal.log('SequenceSync: sprite method=ZDBridge texture=%s' % texture.get_path_name())
    return sprite, 'ZDBridge'

def _import_texture(source, folder, name, action):
    if not source or not os.path.isfile(source):
        raise RuntimeError('%s: frame source does not exist: %s' % (action, source))
    task = unreal.AssetImportTask()
    task.filename = source
    task.destination_path = folder
    task.destination_name = name
    task.automated = True
    task.replace_existing = True
    task.replace_existing_settings = True
    task.save = True
    unreal.AssetToolsHelpers.get_asset_tools().import_asset_tasks([task])
    if not task.imported_object_paths:
        raise RuntimeError('%s: texture import failed: %s' % (action, source))
    return str(task.imported_object_paths[0])

def _collect_action_assets(action):
    """Collect current and legacy assets for this action before replacement.

    The target folder alone is not enough: older builds used different action
    folder spellings (for example OnDamage/Ondm and Defatk/DefAtk), so stale
    sprites can remain outside the current target path.
    """
    material_folder = action['targetMaterialFolder']
    character_root = material_folder.split('/Material/', 1)[0]
    action_code = str(action.get('actionCode', '')).strip()
    aliases = {
        'ondamage': ('ondamage', 'ondm'),
        'defence': ('defence', 'defense'),
        'flydown': ('flydown',),
        'flystart': ('flystart',),
        'standup': ('standup', 'stand_up'),
    }.get(action_code.lower(), (action_code.lower(),))
    roots = [
        material_folder,
        _package(action['targetSequencePath']),
        character_root + '/Material',
        character_root + '/AnimSequences',
    ]
    paths = []
    for folder in roots:
        try:
            candidates = unreal.EditorAssetLibrary.list_assets(folder, recursive=True, include_folder=False)
        except Exception:
            candidates = []
        for path in candidates:
            lowered = path.lower()
            if folder in (material_folder, _package(action['targetSequencePath'])) or any(alias and alias in lowered for alias in aliases):
                paths.append(path)
    return list(dict.fromkeys(paths))

def _cleanup_old_assets(paths, new_paths, action):
    new_packages = {_package(path) for path in new_paths if path}
    skipped = []
    deleted_paths = []
    ordered_paths = sorted(
        paths,
        key=lambda path: (
            0 if '/AnimSequences/' in path else
            1 if not path.lower().endswith('_sprite.' + path.rsplit('.', 1)[-1].lower()) and '/Material/' in path else
            2 if '_Sprite.' in path else
            3,
            path.lower()))
    for path in ordered_paths:
        if _package(path) in new_packages:
            continue
        if not unreal.EditorAssetLibrary.does_asset_exist(path):
            continue
        asset = unreal.load_asset(path)
        deleted = False
        try:
            deleted_ok = bool(unreal.AssetToolsHelpers.get_asset_tools().delete_assets([asset], show_confirmation=False))
        except Exception:
            deleted_ok = False
        if not deleted_ok:
            try:
                deleted_ok = bool(unreal.EditorAssetLibrary.delete_asset(path))
            except Exception:
                deleted_ok = False
        if not deleted_ok:
            # Unreal refuses deletion while an external asset still references it.
            # Keep the sync result successful but expose the exact legacy assets.
            skipped.append(path)
        else:
            deleted_paths.append(path)
    unreal.log('SequenceSync: action=%s cleanup deleted=%d skipped=%d' % (action.get('actionCode', ''), len(deleted_paths), len(skipped)))
    return skipped, deleted_paths

def _sync_action(action):
    code = action['actionCode']
    frames = action.get('frames', [])
    if not frames:
        raise RuntimeError('%s: no frames in toolbox data' % code)
    sequence_path = action['targetSequencePath']
    sequence_name = sequence_path.rsplit('/', 1)[-1]
    sequence_folder = sequence_path.rsplit('/', 1)[0]
    if sequence_name == sequence_folder.rsplit('/', 1)[-1]:
        raise RuntimeError('%s: invalid targetSequencePath (asset name repeated as folder): %s' % (code, sequence_path))
    old_sequence = unreal.load_asset(sequence_path)
    source_asset = _require(action['animMapsPath'], code)
    old_assets = _collect_action_assets(action)
    imported = []
    for frame in frames:
        if frame.get('isBlank'):
            imported.append(None)
        else:
            imported.append(_import_texture(frame.get('filePath', ''), action['targetMaterialFolder'], '%s_Frame%03d' % (code, int(frame['index'])), code))
    sprites = [None] * len(frames)
    textures = []
    for index, texture_path in enumerate(imported):
        if texture_path is None:
            continue
        textures.append((_require(texture_path, code), index))
    # Do not call EditorAssetLibrary.sync_browser_to_objects here. That API
    # opens or focuses the Content Browser and crashes commandlet/remote runs
    # where SlateApplication is not available. Asset creation is independent
    # of Content Browser selection, so keep the sync unattended and headless.
    if textures:
        for texture, index in textures:
            sprite, _ = _create_sprite_from_texture(
                texture,
                action['targetMaterialFolder'],
                '%s_Frame%03d_Sprite' % (code, int(frames[index]['index'])),
                code)
            sprites[index] = sprite
    sprite_assets = [sprite for sprite in sprites if sprite is not None]
    frame_runs = [max(1, int(frames[index].get('durationFrames', 1))) for index, sprite in enumerate(sprites) if sprite is not None]
    flipbook, _ = _bridge_call(
        'create_paper_flipbook_from_sprites',
        [sprite_assets, frame_runs, float(action.get('fps', 12)), action['targetMaterialFolder'], code],
        code)
    _save(flipbook.get_path_name())
    sequence = old_sequence
    notification_state = _capture_notification_state(sequence) if sequence is not None else {}
    if sequence is None:
        sequence, _ = _bridge_call(
            'create_paper_zd_sequence',
            [flipbook, source_asset, sequence_folder, sequence_name],
            code)
    else:
        # Existing sequences still receive the new Flipbook through the public
        # PaperZD property; new sequences are initialized by the C++ factory.
        data_source = unreal.PaperZDFlipbookAnimDataSource()
        data_source.set_editor_property('animation', flipbook)
        sequence.set_editor_property('anim_data', [data_source])
        _assert_notification_state_unchanged(sequence, notification_state, code)
    anim_maps_change = None
    if action.get('animMapsEntryName'):
        # The bridge performs the same Supported Animations '+' operation and
        # handles the concrete array element type in C++ reflection.
        status, _ = _bridge_call(
            'ensure_sequence_animation_source',
            [source_asset, sequence],
            code)
        anim_maps_change = str(status)
        if anim_maps_change.startswith('error:'):
            raise RuntimeError('%s: %s' % (code, anim_maps_change))
        _save(source_asset.get_path_name())
    sequence.modify()
    _save(sequence.get_path_name())
    if action.get('blueprintProperty'):
        blueprint = _require(action['characterBlueprintPath'], code)
        _set_first(blueprint, [action['blueprintProperty']], sequence, code)
    sequence.modify()
    _save(sequence.get_path_name())
    new_paths = [texture_path for texture_path in imported if texture_path] + [
        sprite.get_path_name() for sprite in sprites if sprite is not None
    ] + [flipbook.get_path_name(), sequence.get_path_name()]
    skipped, deleted = _cleanup_old_assets(old_assets, new_paths, action)
    return {'actionCode': code, 'sequencePath': sequence.get_path_name(), 'flipbookPath': flipbook.get_path_name(), 'frameCount': len(frames), 'animMapsChange': anim_maps_change, 'deletedAssets': deleted, 'legacyAssetsNotDeleted': skipped}

def main():
    plan_path = os.environ.get('ZD_SEQUENCE_SYNC_PLAN_PATH', '')
    result_path = os.environ.get('ZD_SEQUENCE_SYNC_RESULT_PATH', '')
    plan = _load(plan_path)
    results = []
    try:
        _require(plan['CharacterBlueprintPath'], 'character blueprint')
        _require(plan['AnimMapsPath'], 'AnimMaps')
        for action in plan.get('actions', []):
            action['animMapsPath'] = plan['AnimMapsPath']
            action['characterBlueprintPath'] = plan['CharacterBlueprintPath']
            action['characterCode'] = plan.get('CharacterCode', '')
            action_result = _sync_action(action)
            results.append({'actionCode': action['actionCode'], 'succeeded': True, 'message': 'sequence assets synchronized', 'deletedAssets': action_result.get('deletedAssets', []), 'legacyAssetsNotDeleted': action_result.get('legacyAssetsNotDeleted', [])})
        _write(result_path, {'protocolVersion': 1, 'succeeded': True, 'completedAt': datetime.datetime.now(datetime.timezone.utc).isoformat(), 'actions': results})
    except Exception as error:
        _write(result_path, {'protocolVersion': 1, 'succeeded': False, 'errorMessage': str(error), 'completedAt': datetime.datetime.now(datetime.timezone.utc).isoformat(), 'actions': results})
        raise

try:
    main()
except Exception:
    unreal.log_error('Sequence sync failed:\n' + traceback.format_exc())
    raise
