r"""把「引用指向改名中间名」的悬空引用修回来。

2026-09-18 的事故现场：大小写迁移 `Standup` → `StandUP` 连着改两次名，中间名是
`StandUP__ZDCaseMigration`。`consolidate_assets` 把引用方（Misaka_AnimBP 里的
Play Sequence 节点）在**内存里**搬回了规范名，但命令行走完没保存，而重定向器已经删了 ——
于是 AnimBP 里留下一个指向中间名的引用，编辑器此后每次都报
`Play Sequence references an unknown sequence`，commandlet 也一直退 1。

修法：把规范名再走一次「改名 → 临时名 → 规范名」，让那个中间名重新有重定向器，
再用 consolidate 把引用方搬回规范名，**然后把脏包存盘**，最后清掉重定向器。

用法（离线，不要开着编辑器）：

    UnrealEditor-Cmd.exe <项目>.uproject -run=pythonscript ^
        -script=<工具目录>\Tools\UnrealBridge\repair_dangling_sequence_reference.py

要修的资产列表由 `ZD_REPAIR_SEQUENCES` 指定，格式 `角色代号:动作代号`，分号分隔，
默认 `Misaka:StandUP`。结果写 `ZD_REPAIR_RESULT`（默认 %TEMP%\zd-sequence-reference-repair.json）。
"""

import datetime
import json
import os
import traceback

import unreal

SEQUENCES_ENV = 'ZD_REPAIR_SEQUENCES'
RESULT_ENV = 'ZD_REPAIR_RESULT'
DEFAULT_SEQUENCES = 'Misaka:StandUP'
TEMP_SUFFIX = '__ZDCaseMigration'


def _split_target(entry):
    code, _, action = str(entry).partition(':')
    code = code.strip()
    action = action.strip()
    if not code or not action:
        return None
    return code, action


def _object_path(package):
    return '%s.%s' % (package, package.rsplit('/', 1)[-1])


def _character_root(code):
    return '/Game/GameActor2D/%s' % code


def _save_dirty(character_root, label):
    try:
        saved = unreal.EditorAssetLibrary.save_directory(
            character_root, only_if_is_dirty=True, recursive=True)
    except Exception as exc:
        unreal.log_warning('SeqRefRepair: %s save_directory failed: %s' % (label, exc))
        return False
    unreal.log('SeqRefRepair: %s saved dirty packages -> %s' % (label, saved))
    return bool(saved)


def _repair(code, action):
    character_root = _character_root(code)
    target = '%s/AnimSequences/%s' % (character_root, action)
    temporary = target + TEMP_SUFFIX
    report = {
        'characterCode': code,
        'action': action,
        'targetPackage': target,
        'steps': [],
        'succeeded': False,
    }
    if not unreal.EditorAssetLibrary.does_asset_exist(target):
        report['error'] = '目标序列不存在：%s' % target
        return report

    # 1) 先挪到「悬空引用指望的那个名字」上，让那条引用重新能解析。
    if unreal.EditorAssetLibrary.does_asset_exist(temporary):
        unreal.EditorAssetLibrary.delete_asset(temporary)
    if not unreal.EditorAssetLibrary.rename_asset(target, temporary):
        report['error'] = '第一次改名失败：%s → %s' % (target, temporary)
        return report
    report['steps'].append('rename -> %s' % temporary)

    # 2) 挪回规范名。这一步在临时名留下一个重定向器 —— 引用方正指着它。
    if not unreal.EditorAssetLibrary.rename_asset(temporary, target):
        report['error'] = '第二次改名失败：%s → %s' % (temporary, target)
        return report
    report['steps'].append('rename -> %s' % target)

    target_asset = unreal.load_asset(target)
    if target_asset is None:
        report['error'] = '改完名之后取不到资产：%s' % target
        return report

    temporary_redirector = unreal.load_object(None, _object_path(temporary))
    if temporary_redirector is None:
        # 取名这一步本身就会把引用方改写回规范名（rename_asset 会重定向引用），
        # 而且命令行走完时脏包已经落盘 —— 实测 Misaka_AnimBP 就是这样被修好的。
        # 这时没有重定向器可 consolidate，不算失败：**存盘 + 收尾**就行。
        report['steps'].append('no redirector left; referencers rewritten by rename')
        report['savedDirtyPackages'] = _save_dirty(character_root, '%s/%s' % (code, action))
        report['succeeded'] = True
        return report

    # 3) 把引用方搬回规范名。
    try:
        consolidated = unreal.EditorAssetLibrary.consolidate_assets(
            target_asset, [temporary_redirector])
    except Exception as exc:
        report['error'] = 'consolidate_assets 失败：%s' % exc
        return report
    report['steps'].append('consolidate -> %s (%s)' % (target, consolidated))
    if not consolidated:
        report['error'] = 'consolidate_assets 返回假，引用方没搬成功'
        return report

    # 4) **存盘**：命令行走完不会自动保存，不存等于没修。
    report['savedDirtyPackages'] = _save_dirty(character_root, '%s/%s' % (code, action))

    # 5) 收掉残留的重定向器（consolidate 通常已经删掉了）。
    remaining = []
    for package in (temporary, target):
        obj = None
        try:
            obj = unreal.load_object(None, _object_path(package))
        except Exception:
            obj = None
        if obj is not None and obj.get_class().get_name() == 'ObjectRedirector':
            remaining.append(package)
            try:
                unreal.EditorAssetLibrary.delete_asset(package)
            except Exception:
                pass
    report['steps'].append('cleanup redirectors: %s' % (remaining or 'none'))
    _save_dirty(character_root, '%s/%s cleanup' % (code, action))
    report['succeeded'] = True
    return report


def main():
    entries = [entry for entry in (os.environ.get(SEQUENCES_ENV) or DEFAULT_SEQUENCES).split(';') if entry.strip()]
    results = []
    failures = 0
    for entry in entries:
        parsed = _split_target(entry)
        if parsed is None:
            unreal.log_warning('SeqRefRepair: 看不懂的条目 %s' % entry)
            failures += 1
            continue
        unreal.log('SeqRefRepair: repairing %s' % entry)
        report = _repair(*parsed)
        results.append(report)
        if not report.get('succeeded'):
            failures += 1
            unreal.log_error('SeqRefRepair: %s 失败：%s' % (entry, report.get('error', '')))

    out_path = os.environ.get(RESULT_ENV, '') or os.path.join(
        os.environ.get('TEMP', '.'), 'zd-sequence-reference-repair.json')
    with open(out_path, 'w', encoding='utf-8') as handle:
        json.dump({
            'generatedAt': datetime.datetime.now(datetime.timezone.utc).isoformat(),
            'entries': results,
            'failures': failures,
        }, handle, ensure_ascii=False, indent=2)
    unreal.log('SeqRefRepair: done failures=%d out=%s' % (failures, out_path))


try:
    main()
except Exception:
    unreal.log_error('SeqRefRepair failed:\n' + traceback.format_exc())
    raise
