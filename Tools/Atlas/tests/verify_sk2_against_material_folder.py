# 用真实 Sk2 素材目录验证图集出图数量。
#
# 这是「不跑工具箱、不改环境」的独立核对：直接照 AtlasManifestWriter 的规则
# 数一遍素材目录，把结果和图集工具真跑出来的产物对比。
#
# 用法：
#   <内置python> Tools/Atlas/tests/verify_sk2_against_material_folder.py
import json
import os
import subprocess
import sys
import tempfile

FRAMES_FOLDER = r"D:\NewData\CrossingVoidZDProject\Draft\Misaka\ZDMaterial\Sk2\Frames"
SEQUENCE_JSON = r"D:\NewData\CrossingVoidZDProject\Draft\Misaka\ZDMaterial\Sk2\sequence.json"
ATLAS_SCRIPT = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "ue_atlas.py")


def scan_png(folder):
    """复刻 AtlasManifestWriter.EnumerateSourceImages：只认 .png、不递归、按文件名升序。"""
    if not os.path.isdir(folder):
        return []
    names = [n for n in os.listdir(folder)
             if n.lower().endswith(".png") and os.path.isfile(os.path.join(folder, n))]
    return sorted(names, key=str.lower)


def main():
    print("素材目录：%s" % FRAMES_FOLDER)
    if not os.path.isdir(FRAMES_FOLDER):
        print("!! 目录不存在，跳过")
        return 1

    pngs = scan_png(FRAMES_FOLDER)
    print("目录里的 PNG 张数: %d" % len(pngs))

    # 序列清单里的条目数（只用来对照，不参与计算）
    seq_count = None
    if os.path.isfile(SEQUENCE_JSON):
        with open(SEQUENCE_JSON, encoding="utf-8-sig") as f:
            seq = json.load(f)
        seq_count = len(seq.get("Frames", []))
        print("序列清单条目数: %d" % seq_count)
        if seq_count is not None and seq_count != len(pngs):
            print("   -> 序列比素材多 %d 条（复用），图集**不**跟随序列"
                  % (seq_count - len(pngs)))

    # 按同一规则生成清单，交给图集工具真跑一遍
    frames = []
    for i, name in enumerate(pngs):
        frames.append({
            "file": os.path.join(FRAMES_FOLDER, name),
            "name": "Sk2_Frame%s_Sprite" % str(i).zfill(2 if len(pngs) >= 10 else 1),
            "index": i + 1,
        })

    work = tempfile.mkdtemp(prefix="atlas_verify_")
    manifest_path = os.path.join(work, "_atlas_manifest.json")
    report_path = os.path.join(work, "_atlas_report.json")
    with open(manifest_path, "w", encoding="utf-8") as f:
        json.dump({"atlas": "Misaka_Sk2", "mode": "pack", "frames": frames}, f,
                  ensure_ascii=False, indent=2)

    print("\n调用图集工具…")
    proc = subprocess.run(
        [sys.executable, ATLAS_SCRIPT, "--manifest", manifest_path,
         "--report", report_path, "-o", work, "--name", "Misaka_Sk2"],
        capture_output=True, text=True, encoding="utf-8")
    print(proc.stdout.strip())
    if proc.stderr.strip():
        print("stderr: " + proc.stderr.strip())

    if not os.path.isfile(report_path):
        print("!! 图集工具没有写出 report")
        return 1
    with open(report_path, encoding="utf-8") as f:
        report = json.load(f)

    print("\n=== 核对 ===")
    ok = True
    checks = [
        ("图集出图张数 == 素材目录 PNG 张数", report.get("frameCount"), len(pngs)),
    ]
    for label, actual, expected in checks:
        flag = "OK " if actual == expected else "BAD"
        if actual != expected:
            ok = False
        print("  [%s] %s: %s（期望 %s）" % (flag, label, actual, expected))

    seq_path = os.path.join(work, "Misaka_Sk2_sequence.json")
    if os.path.isfile(seq_path):
        with open(seq_path, encoding="utf-8") as f:
            seq_out = json.load(f)
        sprites = seq_out.get("frames", [])
        print("  [%s] _sequence.json 的框数: %d（期望 %d）"
              % ("OK " if len(sprites) == len(pngs) else "BAD",
                 len(sprites), len(pngs)))
        if len(sprites) != len(pngs):
            ok = False
    print("  产物目录: %s" % work)
    print("\n结论：%s" % ("图集按素材库出图，符合预期" if ok else "不符合预期，需检查"))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
