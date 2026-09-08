"""Diagnose how a character's Hurt MetaSound stores its Wave Asset / Weights arrays.

Run inside the target Unreal Editor (Output Log -> Python), then read the printed
JSON. It reports both storage locations so a mismatch is obvious:

  * editorGraphDefaults - the UMetasoundEditorGraphMemberDefault*Array.Defaults
    mirror. That property is declared Transient, so it is expected to be empty
    in a fresh process even when the asset holds values.
  * builderLiteral - the frontend document reached through the editor builder.
    This is the authoritative store the toolbox compares against.

Set ZD_INSPECT_METASOUND_PATH to inspect another character.
"""

import json
import os

import unreal

ASSET_PATH = os.environ.get(
    "ZD_INSPECT_METASOUND_PATH",
    "/Game/GameActor2D/Misaka/Sound/Misaka_OnDM")
INPUT_NAMES = ("Wave Asset", "Weights")
VARIABLE_NAMES = ("Wave Asset", "WaveAsset", "Weights")


def _text(value):
    try:
        return str(value)
    except Exception as error:
        return "ERROR: " + str(error)


def _export_text(value):
    if value is None:
        return ""
    try:
        return str(value.export_text())
    except Exception as error:
        return "ERROR: " + str(error)


def _first(value):
    return value[0] if isinstance(value, tuple) else value


def _editor_graph_defaults(asset, out):
    """Read the transient editor mirror the old detection path relied on."""
    import re

    try:
        document_text = asset.get_editor_property("RootMetasoundDocument").export_text()
    except Exception as error:
        out["editorGraphError"] = str(error)
        return
    for class_name, label in (
            ("MetasoundEditorGraphMemberDefaultObjectArray", "Wave Asset"),
            ("MetasoundEditorGraphMemberDefaultFloatArray", "Weights")):
        matches = re.findall(class_name + r"'([^']+)'", document_text)
        record = {"matches": len(matches)}
        if len(matches) == 1:
            try:
                member = unreal.load_object(None, matches[0])
                defaults = list(member.get_editor_property("Defaults") or [])
                record["defaultPages"] = len(defaults)
                record["firstPage"] = _export_text(defaults[0]) if defaults else ""
            except Exception as error:
                record["error"] = str(error)
        out.setdefault("editorGraphDefaults", {})[label] = record


def main():
    out = {"assetPath": ASSET_PATH}
    asset = unreal.EditorAssetLibrary.load_asset(ASSET_PATH)
    out["asset"] = _text(asset)
    if asset is None:
        unreal.log_error("MetaSound inspect: asset not found: " + ASSET_PATH)
        return

    out["class"] = _text(asset.get_class().get_name())
    _editor_graph_defaults(asset, out)

    try:
        editor_subsystem = unreal.get_editor_subsystem(unreal.MetaSoundEditorSubsystem)
        builder = _first(editor_subsystem.find_or_begin_building(asset))
    except Exception as error:
        out["builderError"] = str(error)
        builder = None
    out["builder"] = _text(builder)

    if builder is not None:
        try:
            out["graphInputNames"] = [str(name) for name in _first(builder.get_graph_input_names())]
        except Exception as error:
            out["graphInputNamesError"] = str(error)
        for name in INPUT_NAMES:
            try:
                literal = _first(builder.get_graph_input_default(name))
                out.setdefault("builderInputLiteral", {})[name] = _export_text(literal)
            except Exception as error:
                out.setdefault("builderInputLiteral", {})[name] = "ERROR: " + str(error)
        # Wave Asset / Weights 是 graph variable 而不是 graph input，
        # 工具箱的检测就是靠这里读取权威值。
        for name in VARIABLE_NAMES:
            try:
                literal = _first(builder.get_graph_variable_default(name))
                out.setdefault("builderVariableLiteral", {})[name] = _export_text(literal)
            except Exception as error:
                out.setdefault("builderVariableLiteral", {})[name] = "ERROR: " + str(error)

    try:
        subsystem = unreal.get_engine_subsystem(unreal.MetaSoundBuilderSubsystem)
        out["sampleObjectArrayLiteral"] = _export_text(
            _first(subsystem.create_object_array_meta_sound_literal([None])))
        out["sampleFloatArrayLiteral"] = _export_text(
            _first(subsystem.create_float_array_meta_sound_literal([0.3, 0.1])))
    except Exception as error:
        out["builderSubsystemError"] = str(error)

    # ZDBridge 里新增的 graph variable 写入函数；插件重新编译之前不会存在。
    bridge = getattr(unreal, "ZDBridgeLibrary", None)
    out["zdBridgeLoaded"] = bridge is not None
    out["zdBridgeVariableSetter"] = bool(
        bridge is not None and getattr(bridge, "set_meta_sound_graph_variable_default", None))

    text = json.dumps(out, ensure_ascii=False, indent=2)
    unreal.log("MetaSound inspect: " + text)
    try:
        folder = os.path.join(unreal.Paths.project_intermediate_dir(), "ZDToolboxBridge")
        os.makedirs(folder, exist_ok=True)
        output_path = os.environ.get(
            "ZD_INSPECT_METASOUND_OUTPUT",
            os.path.join(folder, "inspect-metasound-defaults.json"))
        with open(output_path, "w", encoding="utf-8") as output:
            output.write(text)
        unreal.log("MetaSound inspect written to: " + os.path.abspath(output_path))
    except Exception as error:
        unreal.log_warning("MetaSound inspect could not write result file: " + str(error))


main()
