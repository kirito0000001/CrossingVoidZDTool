r"""核对工具箱的 Python 脚本用到的 `unreal.*` 名字在这个引擎上真的存在。

为什么要这个：2026-09-17 同步整批序列时炸在
`module 'unreal' has no attribute 'ObjectRedirector'` —— 那行代码是照着「UE 应该有这个类」
写出来的，而 UObjectRedirector 从来没暴露到 Python。异常又把整批同步打断了，
排查只能靠人回忆自己勾了哪几个动作。

「引擎里应该有这么个名字」这种想当然的代价太大，所以把它变成一次可以随时重跑的对账：

    UnrealEditor-Cmd.exe <项目>.uproject -run=pythonscript ^
        -script=<工具目录>\Tools\UnrealBridge\probe_unreal_python_bindings.py

结果写到 `ZD_PYTHON_BINDING_PROBE_OUT`（默认 %TEMP%\\unreal-python-bindings.json）：

    {
      "engineVersion": "5.8.2-...",
      "generatedAt": "...",
      "moduleNames": ["Actor", "AssetExportTask", ...],      # dir(unreal) 去掉下划线开头的
      "scripts": {
        "<脚本路径>": {"names": [...], "unknown": [...], "deprecated": [...]}
      }
    }

`unknown` 非空就是脚本里有编出来的名字 —— 要么改脚本，要么确认引擎版本。
工具箱里的 `Tools/UnrealBridge/unreal_python_bindings.json` 是这个探针的产物摘要，
回归套件拿它当守卫（见「Python 绑定名对账」用例）。
"""

import datetime
import json
import os
import re
import traceback

import unreal

OUT_ENV = 'ZD_PYTHON_BINDING_PROBE_OUT'
SCRIPTS_ENV = 'ZD_PYTHON_BINDING_PROBE_SCRIPTS'
EXTRA_NAMES_ENV = 'ZD_PYTHON_BINDING_PROBE_NAMES'

# 只认「unreal 点一个标识符」这种直取名字。`getattr(unreal, '名字')` 那种动态写法不在这里管，
# 它本来就是为了「拿不到也不要炸」才那么写的。
NAME_PATTERN = re.compile(r'unreal\.([A-Za-z_][A-Za-z0-9_]*)')


def _script_paths():
    configured = os.environ.get(SCRIPTS_ENV, '')
    if configured:
        return [path for path in configured.split(';') if path.strip()]

    # 没配就自己找：脚本所在目录的上一级（Tools/）里所有 .py。
    tools_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    found = []
    for folder, dirnames, filenames in os.walk(tools_root):
        dirnames[:] = [name for name in dirnames if name != '__pycache__']
        for filename in filenames:
            if filename.endswith('.py'):
                found.append(os.path.join(folder, filename))
    return sorted(found)


def _names_in_script(path):
    try:
        with open(path, 'r', encoding='utf-8') as handle:
            text = handle.read()
    except Exception:
        unreal.log_warning('BindingProbe: cannot read %s' % path)
        return []
    return sorted(set(NAME_PATTERN.findall(text)))


def main():
    module_names = sorted(name for name in dir(unreal) if not name.startswith('_'))
    report = {
        'engineVersion': _engine_version(),
        'generatedAt': datetime.datetime.now(datetime.timezone.utc).isoformat(),
        'moduleNames': module_names,
        'scripts': {},
    }

    for path in _script_paths():
        names = _names_in_script(path)
        report['scripts'][path] = {
            'names': names,
            'unknown': [name for name in names if not hasattr(unreal, name)],
        }

    # 也可以直接把一批名字丢进来问（不经过脚本扫描），便于核对 C# 侧或手写的清单。
    extra = [name for name in os.environ.get(EXTRA_NAMES_ENV, '').split(';') if name.strip()]
    report['extraNames'] = {name: hasattr(unreal, name) for name in extra}

    unknown_total = sum(len(entry['unknown']) for entry in report['scripts'].values())
    out_path = os.environ.get(OUT_ENV, '') or os.path.join(
        os.environ.get('TEMP', '.'), 'unreal-python-bindings.json')
    with open(out_path, 'w', encoding='utf-8') as handle:
        json.dump(report, handle, ensure_ascii=False, indent=2)

    unreal.log('BindingProbe: engine=%s scripts=%d unknown=%d out=%s'
               % (report['engineVersion'], len(report['scripts']), unknown_total, out_path))
    for path, entry in report['scripts'].items():
        if entry['unknown']:
            unreal.log_warning('BindingProbe: %s 用了不存在的名字：%s'
                               % (path, ', '.join(entry['unknown'])))


def _engine_version():
    try:
        return unreal.SystemLibrary.get_engine_version()
    except Exception:
        # 不再退回引擎版本号那几个常量（unreal 的 ENGINE_ 系列）：5.8 的 Python 里没有它们，
        # 而这个探针本身也要经得起「按名字对账」这道检查。
        return ''


try:
    main()
except Exception:
    unreal.log_error('BindingProbe failed:\n' + traceback.format_exc())
    raise
