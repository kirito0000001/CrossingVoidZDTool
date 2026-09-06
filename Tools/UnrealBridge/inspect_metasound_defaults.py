import json
import unreal

path = '/Game/GameActor2D/Misaka/Sound/Misaka_OnDM'
asset = unreal.EditorAssetLibrary.load_asset(path)
out = {'path': path, 'asset': str(asset), 'class': str(asset.get_class().get_name()) if asset else ''}
if asset:
    for name in ('RootMetasoundDocument', 'ConcurrencySet'):
        try:
            value = asset.get_editor_property(name)
            out[name] = str(value)
        except Exception as exc:
            out[name] = 'ERROR: ' + str(exc)
    try:
        document = asset.get_editor_property('RootMetasoundDocument')
        out['documentExport'] = document.export_text()
    except Exception as exc:
        out['documentExportError'] = str(exc)
    try:
        subsystem = unreal.get_engine_subsystem(unreal.MetaSoundBuilderSubsystem)
        out['subsystem'] = str(subsystem)
        builder = subsystem.find_builder_of_document(asset)
        out['builderBefore'] = str(builder)
        if builder is None:
            subsystem.register_source_builder(asset)
            builder = subsystem.find_builder_of_document(asset)
        out['builderAfter'] = str(builder)
    except Exception as exc:
        out['builderError'] = str(exc)
with open('C:/CrossingVoid/Intermediate/ZDToolboxBridge/inspect-metasound-defaults-result.json','w',encoding='utf-8') as f:
    json.dump(out,f,ensure_ascii=False,indent=2)
unreal.log('MetaSound inspect: ' + json.dumps(out,ensure_ascii=False))
