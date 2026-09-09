import datetime
import json
import os
import re
import traceback
import unreal

PROTOCOL_VERSION = 1

# 结果条目分两类，C# 侧必须能区分开。
# 'action' 才是真正执行过的动作，它的 stableId 会被拿去写基线；
# 'diagnostic' 只是流程里的一条诊断信息（比如孤儿序列解绑），
# 它不对应任何动作，既不该计入"已成功 N 个动作"，也不该进基线。
# 之前两类混在同一个 items 数组里，于是所有动作都失败时，那条伪条目
# 仍让 C# 认为"有动作成功过"，"一个都没成的话就抛"这条兜底彻底失效。
ITEM_KIND_ACTION = 'action'
ITEM_KIND_DIAGNOSTIC = 'diagnostic'

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
    value['DetachSequenceObjectPaths'] = get(value, 'detachSequenceObjectPaths', []) or []
    actions = get(value, 'actions', []) or []
    for action in actions:
        for name in ('actionCode', 'baseActionCode', 'formIndex', 'displayName', 'fps', 'blueprintProperty',
                     'blueprintFormSlotIndex', 'animMapsEntryName', 'targetSequencePath', 'targetMaterialFolder',
                     'sequenceAssetName', 'flipbookAssetName', 'legacyNameTokens',
                     'staleAssetObjectPaths', 'hasStaleAssetSelection'):
            action[name] = get(action, name, action.get(name, ''))
        frames = get(action, 'frames', []) or []
        for frame in frames:
            for name in ('index', 'ordinal', 'filePath', 'durationFrames', 'isBlank', 'voiceFileName',
                         'textureAssetName', 'spriteAssetName'):
                frame[name] = get(frame, name, frame.get(name, ''))
        action['frames'] = frames
    value['actions'] = actions
    return value

def _write_progress(path, completed, total, stable_id, message):
    if not path:
        return
    try:
        _write(path, {
            'protocolVersion': PROTOCOL_VERSION,
            'completedCount': completed,
            'totalCount': total,
            'stableId': stable_id,
            'message': message,
            'updatedAt': datetime.datetime.now(datetime.timezone.utc).isoformat(),
        })
    except Exception:
        # 进度只是界面提示，写失败不能影响同步本身。
        pass


def _frame_ordinal(frame, fallback):
    """_load 会把缺失字段填成空串，所以不能依赖 dict.get 的默认值。"""
    value = frame.get('ordinal')
    if value is None or value == '':
        return int(fallback)
    return int(value)


def _frame_ordinal_name(code, ordinal, total):
    """Fallback for plans that predate explicit asset names in the plan.

    Frame numbers start at 0 and are padded to the width of the total frame
    count: 0,1 for single digits, 00,01 for tens, 000,001 for hundreds.
    """
    width = max(1, len(str(max(1, int(total)))))
    return '%s_Frame%s' % (code, str(int(ordinal)).rjust(width, '0'))


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

def _texture_size(texture):
    for names in (('blueprint_get_size_x', 'blueprint_get_size_y'), ('get_size_x', 'get_size_y')):
        try:
            return int(getattr(texture, names[0])()), int(getattr(texture, names[1])())
        except Exception:
            continue
    try:
        imported = texture.get_editor_property('imported_size')
        return int(imported.x), int(imported.y)
    except Exception:
        return 0, 0


def _refresh_existing_sprite(folder, sprite_name, texture, action):
    """Re-point an already existing sprite at the freshly imported texture.

    ZDBridge.CreatePaperSpriteFromTexture returns an existing asset untouched,
    so a sprite created from an older image keeps that image's SourceUV and
    SourceDimension. Writing the properties back runs PostEditChange, which
    rebuilds the sprite's render data for the new size.
    """
    path = folder + '/' + sprite_name
    if not unreal.EditorAssetLibrary.does_asset_exist(path):
        return False
    sprite = unreal.load_asset(path)
    if sprite is None:
        return False
    width, height = _texture_size(texture)
    if width <= 0 or height <= 0:
        return False
    try:
        current_texture = sprite.get_editor_property('source_texture')
        current_dimension = sprite.get_editor_property('source_dimension')
        if current_texture == texture and                 int(current_dimension.x) == width and int(current_dimension.y) == height:
            return False
        sprite.set_editor_property('source_texture', texture)
        sprite.set_editor_property('source_uv', unreal.Vector2D(0.0, 0.0))
        sprite.set_editor_property('source_dimension', unreal.Vector2D(float(width), float(height)))
    except Exception as error:
        unreal.log_warning('SequenceSync: cannot refresh sprite %s: %s' % (path, error))
        return False
    _save(sprite.get_path_name())
    unreal.log('SequenceSync: refreshed sprite %s to %dx%d' % (path, width, height))
    return True


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

def _name_tokens(asset_name):
    """Split an asset name into comparable tokens plus their contiguous joins.

    Matching whole paths with a plain substring test is unsafe: the path also
    contains the character folder, so action 'KO' would match every asset of a
    character named Origin_Ako. Comparing name parts keeps the scope inside the
    action even when the character code happens to contain the token.
    """
    parts = []
    for chunk in re.split(r'[^0-9A-Za-z]+', str(asset_name or '')):
        if chunk:
            parts.extend(part.lower() for part in re.findall(r'[0-9]+|[A-Z]+(?![a-z])|[A-Z]?[a-z]+', chunk) or [chunk.lower()])
    tokens = set()
    for start in range(len(parts)):
        for end in range(start + 1, min(start + 4, len(parts)) + 1):
            tokens.add(''.join(parts[start:end]))
    return tokens


def _asset_name_of(path):
    return _package(path).rsplit('/', 1)[-1]


def _collect_action_assets(action):
    """Decide which existing assets this action should clean up.

    The plan carries the delete rows the user actually ticked in the diff tree,
    and that list is authoritative. Guessing by asset-name tokens both misses
    assets (historic names that do not contain the action token, e.g.
    Material/Ondm/Frame_01) and over-deletes (assets in the canonical folder the
    user deliberately left unticked). Token matching stays only as a fallback
    for plans produced by older builds.
    """
    if action.get('hasStaleAssetSelection') is True:
        selected = action.get('staleAssetObjectPaths')
        paths = [str(path) for path in selected if str(path).strip()] if isinstance(selected, list) else []
        unreal.log('SequenceSync: action=%s cleanup scope=selected count=%d' % (
            action.get('actionCode', ''), len(paths)))
        return list(dict.fromkeys(paths))

    return _collect_action_assets_by_token(action)


def _collect_action_assets_by_token(action):
    """Fallback for plans without an explicit selection: match by asset name parts."""
    material_folder = action['targetMaterialFolder']
    character_root = material_folder.split('/Material/', 1)[0]
    legacy_tokens = {
        str(token).strip().lower()
        for token in (action.get('legacyNameTokens') or [])
        if str(token).strip()
    }
    owned_folders = (material_folder,)
    roots = [
        material_folder,
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
            package_folder = _package(path).rsplit('/', 1)[0]
            if package_folder in owned_folders:
                paths.append(path)
                continue
            if legacy_tokens & _name_tokens(_asset_name_of(path)):
                paths.append(path)
    return list(dict.fromkeys(paths))

def _rename_asset(source_package, target_package, action_code):
    if not unreal.EditorAssetLibrary.rename_asset(source_package, target_package):
        raise RuntimeError('%s: failed to rename %s to %s' % (action_code, source_package, target_package))


def _fixup_redirectors(package_paths, action_code):
    """把改名留下的重定向器解引用并清掉。

    rename_asset 会在旧路径留一个重定向器。大小写对齐要连着改两次名，
    中间那个名字（*__ZDCaseMigration）一旦被引用方记进包里、而重定向器随后被清理，
    这条引用就永久悬空了——实测把 Misaka_AnimBP 的 Play Sequence 节点打断了，
    此后每次导出编辑器都会报 "references an unknown sequence" 并让 commandlet 返回非 0。
    """
    tools = unreal.AssetToolsHelpers.get_asset_tools()
    redirectors = []
    for package in package_paths:
        object_path = '%s.%s' % (package, package.rsplit('/', 1)[-1])
        try:
            obj = unreal.load_object(None, object_path)
        except Exception:
            obj = None
        if obj is not None and isinstance(obj, unreal.ObjectRedirector):
            redirectors.append(obj)

    if not redirectors:
        return

    try:
        tools.fixup_referencers(redirectors)
    except Exception as exc:
        unreal.log_warning('SequenceSync: action=%s fixup_referencers failed: %s' % (action_code, exc))
        return

    for package in package_paths:
        try:
            if unreal.EditorAssetLibrary.does_asset_exist(package):
                unreal.EditorAssetLibrary.delete_asset(package)
        except Exception:
            pass
    unreal.log('SequenceSync: action=%s fixed up %d redirector(s)' % (action_code, len(redirectors)))

def _align_asset_name_case(folder, target_name, action_code):
    """Rename an existing asset whose name differs from the target only by case.

    On Windows Ko.uasset and KO.uasset are the same file, so creating the
    canonical name next to a differently-cased legacy asset would overwrite that
    package and leave an asset whose object name no longer matches its file.
    Renaming through a temporary name keeps the asset, its notifies and its
    references intact while moving it onto the canonical spelling.
    """
    target_package = folder + '/' + target_name
    try:
        candidates = unreal.EditorAssetLibrary.list_assets(folder, recursive=False, include_folder=False)
    except Exception:
        return False
    for path in candidates:
        package = _package(path)
        name = package.rsplit('/', 1)[-1]
        if name == target_name:
            return False
        if name.lower() == target_name.lower():
            temporary_package = target_package + '__ZDCaseMigration'
            _rename_asset(package, temporary_package, action_code)
            _rename_asset(temporary_package, target_package, action_code)
            # 两次改名各留下一个重定向器，中间那个尤其危险：引用方一旦把
            # *__ZDCaseMigration 记进自己的包里，重定向器被清掉后引用就永久悬空。
            _fixup_redirectors([package, temporary_package], action_code)
            unreal.log('SequenceSync: case migration %s -> %s' % (package, target_package))
            return True
    return False


def _align_material_folder_case(material_folder, action_code):
    parent, target_name = material_folder.rsplit('/', 1)
    try:
        entries = unreal.EditorAssetLibrary.list_assets(parent, recursive=False, include_folder=True)
    except Exception:
        return False
    for entry in entries:
        candidate = entry.rstrip('/')
        if not candidate.startswith(parent + '/'):
            continue
        name = candidate[len(parent) + 1:]
        if '/' in name or not name:
            continue
        if name == target_name:
            return False
        if name.lower() == target_name.lower() and unreal.EditorAssetLibrary.does_directory_exist(candidate):
            temporary_folder = material_folder + '__ZDCaseMigration'
            if not unreal.EditorAssetLibrary.rename_directory(candidate, temporary_folder):
                raise RuntimeError('%s: failed to rename folder %s' % (action_code, candidate))
            if not unreal.EditorAssetLibrary.rename_directory(temporary_folder, material_folder):
                raise RuntimeError('%s: failed to rename folder %s' % (action_code, temporary_folder))
            unreal.log('SequenceSync: case migration %s -> %s' % (candidate, material_folder))
            return True
    return False

def _cleanup_old_assets(paths, new_paths, action):
    """清掉这个动作遗留的历史资产。

    删除整体交给 ZDBridge.PurgeAssets：脚本这一侧做不可靠。
    EditorAssetLibrary.delete_asset 只要有引用方就拒删；
    ObjectTools::ForceDeleteObjects 走 FArchiveReplaceObjectRef 只置空硬引用，
    而项目级图集是用 TSoftObjectPtr 挂着每个 Sprite 的（UPaperSpriteAtlas::AtlasSlots），
    强删够不着；Python 连"引用是硬是软"都问不出来——
    FindPackageReferencersForAsset 不返回依赖种类。

    插件不可用时退回普通删除，并如实汇报删不掉的那些。
    """
    new_packages = {_package(path) for path in new_paths if path}
    targets = [path for path in paths
               if _package(path) not in new_packages
               and unreal.EditorAssetLibrary.does_asset_exist(path)]
    code = action.get('actionCode', '')
    if not targets:
        return [], []

    library = getattr(unreal, 'ZDBridgeLibrary', None)
    if library is not None and hasattr(library, 'purge_assets'):
        try:
            report = json.loads(library.purge_assets(list(targets)) or '{}')
        except Exception as exc:
            unreal.log_warning('SequenceSync: action=%s purge bridge failed: %s' % (code, exc))
            report = None
        if report is not None:
            deleted, skipped = [], []
            for item in report.get('items', []):
                path = item.get('objectPath', '')
                if item.get('deleted'):
                    deleted.append(path)
                    continue
                skipped.append(path)
                unreal.log_warning(
                    'SequenceSync: action=%s cannot delete %s; class=%s hard=%s soft=%s error=%s'
                    % (code, path, item.get('assetClass', ''),
                       ', '.join(item.get('hardReferencers') or []) or 'none',
                       ', '.join(item.get('softReferencers') or []) or 'none',
                       item.get('error', '')))
            unreal.log('SequenceSync: action=%s cleanup deleted=%d skipped=%d'
                       % (code, len(deleted), len(skipped)))
            return skipped, deleted

    # 插件不在（旧的工程二进制）时的退路：只能普通删，删不掉就如实报。
    unreal.log_warning('SequenceSync: action=%s ZDBridge.PurgeAssets unavailable; '
                       'falling back to plain delete' % code)
    deleted, skipped = [], []
    for path in targets:
        try:
            unreal.EditorAssetLibrary.delete_asset(path)
        except Exception:
            pass
        if unreal.EditorAssetLibrary.does_asset_exist(path):
            skipped.append(path)
        else:
            deleted.append(path)
    unreal.log('SequenceSync: action=%s cleanup deleted=%d skipped=%d (fallback)'
               % (code, len(deleted), len(skipped)))
    return skipped, deleted

def _sync_action(action):
    code = action['actionCode']
    frames = action.get('frames', [])
    if not frames:
        raise RuntimeError('%s: no frames in toolbox data' % code)
    sequence_path = action['targetSequencePath']
    sequence_name = action.get('sequenceAssetName') or sequence_path.rsplit('/', 1)[-1]
    sequence_folder = sequence_path.rsplit('/', 1)[0]
    if sequence_name == sequence_folder.rsplit('/', 1)[-1]:
        raise RuntimeError('%s: invalid targetSequencePath (asset name repeated as folder): %s' % (code, sequence_path))
    material_folder = action['targetMaterialFolder']
    # 规范命名与历史命名只差大小写时，先把已有资产迁移到规范拼写，
    # 否则在 Windows 上新建资产会直接覆盖同名包文件。
    _align_material_folder_case(material_folder, code)
    _align_asset_name_case(sequence_folder, sequence_name, code)
    old_sequence = unreal.load_asset(sequence_folder + '/' + sequence_name)
    source_asset = _require(action['animMapsPath'], code)
    old_assets = _collect_action_assets(action)
    imported = []
    for ordinal, frame in enumerate(frames):
        if frame.get('isBlank'):
            imported.append(None)
        else:
            texture_name = frame.get('textureAssetName') or _frame_ordinal_name(
                code, _frame_ordinal(frame, ordinal), len(frames))
            _align_asset_name_case(material_folder, texture_name, code)
            imported.append(_import_texture(frame.get('filePath', ''), material_folder, texture_name, code))
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
            sprite_name = frames[index].get('spriteAssetName') or (_frame_ordinal_name(
                code, _frame_ordinal(frames[index], index), len(frames)) + '_Sprite')
            _align_asset_name_case(material_folder, sprite_name, code)
            _refresh_existing_sprite(material_folder, sprite_name, texture, code)
            sprite, _ = _create_sprite_from_texture(texture, material_folder, sprite_name, code)
            # Sprite 是独立的包；不显式保存的话离线 commandlet 退出后不会落盘，
            # Flipbook 会引用到磁盘上并不存在的资产。
            _save(sprite.get_path_name())
            sprites[index] = sprite
    if not any(sprite is not None for sprite in sprites):
        raise RuntimeError('%s: every frame is blank; the flipbook would have no image' % code)
    # 空白帧在 Flipbook 里保留一个 Sprite 为空的关键帧，否则整条序列的时长和节奏都会变短。
    sprite_assets = sprites
    frame_runs = [max(1, int(frame.get('durationFrames', 1) or 1)) for frame in frames]
    flipbook_name = action.get('flipbookAssetName') or code
    _align_asset_name_case(material_folder, flipbook_name, code)
    flipbook, _ = _bridge_call(
        'create_paper_flipbook_from_sprites',
        [sprite_assets, frame_runs, float(action.get('fps', 12)), material_folder, flipbook_name],
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
    # 第五步不再往角色蓝图里写序列槽位——把序列绑到蓝图属于下一步的职责。
    # 计划里仍然带着 blueprintProperty / blueprintFormSlotIndex，留给那一步用。
    new_paths = [texture_path for texture_path in imported if texture_path] + [
        sprite.get_path_name() for sprite in sprites if sprite is not None
    ] + [flipbook.get_path_name(), sequence.get_path_name()]
    skipped, deleted = _cleanup_old_assets(old_assets, new_paths, action)
    return {'actionCode': code, 'sequencePath': sequence.get_path_name(), 'flipbookPath': flipbook.get_path_name(), 'frameCount': len(frames), 'animMapsChange': anim_maps_change, 'deletedAssets': deleted, 'legacyAssetsNotDeleted': skipped}

def _result_message(action_result):
    parts = ['sequence assets synchronized', 'frames=%d' % int(action_result.get('frameCount', 0) or 0)]
    deleted = action_result.get('deletedAssets') or []
    skipped = action_result.get('legacyAssetsNotDeleted') or []
    if deleted:
        parts.append('deleted=%d' % len(deleted))
    if skipped:
        # 删不掉的旧资产必须出现在结果里；否则界面上看不出还有历史资产残留。
        parts.append('legacyAssetsNotDeleted=%d: %s' % (len(skipped), ', '.join(skipped)))
    return ' | '.join(parts)


def _detach_orphan_sequences(paths):
    """把非规范序列从角色动画源上解绑——只解绑，不删资产。

    PaperZD 2.2 的动画源没有 SupportedAnimations 数组：编辑器里那份列表是按序列自身的
    AnimSource 指针反查的，所以"从源里移除"就是清掉那个指针。

    资产刻意保留在盘上。一条串错位置的序列往往仍是有用素材，
    因为它看起来不该在这儿就顺手删掉，代价太大。
    """
    if not paths:
        return [], []
    library = getattr(unreal, 'ZDBridgeLibrary', None)
    if library is None or not hasattr(library, 'detach_sequences_from_animation_source'):
        unreal.log_warning('SequenceSync: ZDBridge.DetachSequencesFromAnimationSource unavailable; '
                           'orphan sequences left bound')
        return [], list(paths)
    try:
        report = json.loads(library.detach_sequences_from_animation_source(list(paths)) or '{}')
    except Exception as exc:
        unreal.log_warning('SequenceSync: detach bridge failed: %s' % exc)
        return [], list(paths)

    detached, failed = [], []
    for item in report.get('items', []):
        path = item.get('objectPath', '')
        if item.get('detached'):
            detached.append(path)
        else:
            failed.append(path)
            unreal.log_warning('SequenceSync: cannot detach %s; class=%s previousSource=%s error=%s'
                               % (path, item.get('assetClass', ''),
                                  item.get('previousSource', ''), item.get('error', '')))
    unreal.log('SequenceSync: orphan sequences detached=%d failed=%d' % (len(detached), len(failed)))
    return detached, failed

def _run_post_sync_export():
    """同步完成后，在同一个编辑器会话里顺手把复扫导出做掉。

    一次第五步同步原本要开三次编辑器：同步前导出、桥接同步、同步后复扫导出。
    实测每次会话 13-15 秒，其中约 9 秒是纯启动开销——复扫要读的就是这个
    已经加载好、而且刚被自己改过的编辑器，再开一次纯属浪费。

    工具箱按清单文件的写入时间判断这一步有没有成功；失败就退回独立导出，
    所以这里只记日志、绝不让异常冒出去打断同步结果的写入。
    """
    script = os.environ.get('ZD_POST_SYNC_EXPORT_SCRIPT', '')
    if not script:
        return False
    if not os.path.isfile(script):
        unreal.log_warning('SequenceSync: post-sync export script missing: %s' % script)
        return False

    try:
        unreal.log('SequenceSync: running post-sync export in the same editor session')
        with open(script, 'r', encoding='utf-8') as handle:
            code = handle.read()
        # 导出脚本是顶层执行的（末尾直接调 _export()），给它一个独立的全局命名空间，
        # 免得两边的同名函数互相覆盖。
        exec(compile(code, script, 'exec'), {'__name__': '__zd_post_sync_export__'})
        return True
    except Exception:
        unreal.log_warning('SequenceSync: post-sync export failed:\n' + traceback.format_exc())
        return False


def _summarize_items(results):
    """把逐条结果聚合成一句顶层结论。

    顶层 succeeded 以前是写死的 True。孤儿序列解绑失败时（ZDBridge 少这个 API，
    或者调用抛了异常）条目里明明写着 succeeded=False，C# 侧却按"整体成功"继续
    走复扫、写基线——失败被彻底盖掉，用户看到的是一次完美的同步。
    """
    failed = [item for item in results if not item.get('succeeded')]
    if not failed:
        return True, ''
    return False, ' | '.join(
        '%s: %s' % (item.get('stableId', ''), item.get('message', ''))
        for item in failed)


def main():
    plan_path = os.environ.get('ZD_SEQUENCE_SYNC_PLAN_PATH', '')
    result_path = os.environ.get('ZD_SEQUENCE_SYNC_RESULT_PATH', '')
    progress_path = os.environ.get('ZD_BRIDGE_PROGRESS_PATH', '')
    plan = _load(plan_path)
    actions = plan.get('actions', [])
    total = len(actions)
    results = []
    try:
        # 只校验 AnimMaps：第五步同步的是序列、帧素材、AnimMaps 映射和语音轨道，
        # 角色蓝图的绑定交给下一步，这里不该因为蓝图状态而失败。
        _require(plan['AnimMapsPath'], 'AnimMaps')
        detach_paths = plan.get('DetachSequenceObjectPaths') or []
        if detach_paths:
            detached, failed = _detach_orphan_sequences(detach_paths)
            results.append({
                'stableId': 'orphan-sequences',
                # 这条不是动作，只是解绑结果的汇报；打上标记好让 C# 把它从
                # succeededActionCodes 里剔掉，否则它会被当成"成功的一个动作"。
                'itemKind': ITEM_KIND_DIAGNOSTIC,
                'succeeded': not failed,
                'message': '非规范序列已从动画源解绑（资产保留）| detached=%d | failed=%d%s' % (
                    len(detached), len(failed),
                    (' | ' + ', '.join(failed)) if failed else ''),
                'objectPath': plan['AnimMapsPath'],
                'originIdentity': '',
                'outputFilePath': '',
            })
        for index, action in enumerate(actions):
            action['animMapsPath'] = plan['AnimMapsPath']
            action['characterCode'] = plan.get('CharacterCode', '')
            code = action.get('actionCode', '')
            _write_progress(progress_path, index, total, code, action.get('displayName', '') or code)
            action_result = _sync_action(action)
            # 结果协议与素材同步保持一致：C# 侧只解析 items，写成 actions 会被静默丢弃。
            results.append({
                'stableId': code,
                'itemKind': ITEM_KIND_ACTION,
                'succeeded': True,
                'message': _result_message(action_result),
                'objectPath': action_result.get('sequencePath', ''),
                'originIdentity': '',
                'outputFilePath': '',
            })
            _write_progress(progress_path, index + 1, total, code, action.get('displayName', '') or code)
        succeeded, failure_message = _summarize_items(results)
        _write(result_path, {
            'protocolVersion': PROTOCOL_VERSION,
            'succeeded': succeeded,
            # 两种失败要分开，别塞进同一个字段：
            # 'items' 表示流程整趟跑完了，只是某些条目没成——没列进失败清单的
            # 条目结果可信，C# 可以照常给它们写基线；
            # 'exception' 表示中途炸了，后面的动作根本没执行过，剩下什么没做要另说。
            'failureKind': '' if succeeded else 'items',
            'errorMessage': failure_message,
            'completedAt': datetime.datetime.now(datetime.timezone.utc).isoformat(),
            'items': results,
        })
    except Exception as error:
        _, failure_message = _summarize_items(results)
        message = str(error)
        if failure_message:
            # 抛异常之前可能已经有条目失败过。只报最后那个异常的话，
            # 前面"孤儿序列没解绑成功"这类线索就没了。
            message = '%s | 另有未成功条目：%s' % (message, failure_message)
        _write(result_path, {
            'protocolVersion': PROTOCOL_VERSION,
            'succeeded': False,
            'failureKind': 'exception',
            'errorMessage': message,
            'completedAt': datetime.datetime.now(datetime.timezone.utc).isoformat(),
            'items': results,
        })
        raise

try:
    main()
    # 结果已经落盘，再做复扫导出；它失败只是让工具箱退回独立导出，不影响同步本身。
    _run_post_sync_export()
except Exception:
    unreal.log_error('Sequence sync failed:\n' + traceback.format_exc())
    raise
