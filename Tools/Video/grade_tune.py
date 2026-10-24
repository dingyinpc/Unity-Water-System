# -*- coding: utf-8 -*-
"""grade_tune.py — G5 场景调色对表迭代器

输入：场景连拍目录（取中间帧算三指标）+ 参考帧号 + 探针里的当前调色值
（GRADE cur scene e s c），输出到 Docs/Temp/grade.json 的该场景行。
判据带：±25%（0.75~1.333）。
用法：
  python Tools/Video/grade_tune.py --scene OceanScene --seq <dir> --ref 3646 3736
"""
import argparse
import glob
import math
import os
import re

import cv2
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
PROBE = os.path.join(ROOT, "Logs", "water_probe.txt")
JSON = os.path.join(ROOT, "Docs", "Temp", "grade.json")


def static3(img):
    h = img.shape[0]
    g = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
    s = cv2.cvtColor(img, cv2.COLOR_BGR2HSV)[:, :, 1]
    return g[h // 4:].mean(), g[h // 4:].std(), s[h // 4:].mean()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--scene", required=True)
    ap.add_argument("--seq", required=True)
    ap.add_argument("--ref", type=int, nargs=2, required=True)
    a = ap.parse_args()

    fs = sorted(glob.glob(os.path.join(a.seq, "*.jpg")))
    ours = cv2.imdecode(np.fromfile(fs[len(fs) // 2], np.uint8), cv2.IMREAD_COLOR)
    ref = cv2.imdecode(np.fromfile(
        os.path.join(ROOT, "Docs/视频逐帧/f1083870/frames/f%06d.jpg" % ((a.ref[0] + a.ref[1]) // 2)),
        np.uint8), cv2.IMREAD_COLOR)
    o, r = static3(ours), static3(ref)

    # 当前调色值（595 探针）
    cur = (0.0, 0.0, 0.0)
    for ln in open(PROBE, encoding="utf-8", errors="ignore").read().splitlines():
        m = re.match(r"GRADE cur (\S+) ([\d.-]+) ([\d.-]+) ([\d.-]+)", ln)
        if m and m.group(1) == a.scene:
            cur = (float(m.group(2)), float(m.group(3)), float(m.group(4)))
    # 无 ColorAdjustments 的场景从 190 档起点
    if cur == (0.0, 0.0, 0.0):
        cur = (-0.30, -30.0, 16.0)

    band = lambda x, y: 0.75 <= x / max(y, 1) <= 1.333
    ok = [band(o[i], r[i]) for i in range(3)]
    # 增量（阻尼 0.8，收敛防过冲）
    d_exp = 0.8 * math.log2(max(r[0], 1) / max(o[0], 1))
    d_sat = 0.8 * (r[2] / max(o[2], 1) - 1) * 100
    d_con = 0.8 * (r[1] / max(o[1], 1) - 1) * 40
    new = (cur[0] + d_exp, cur[1] + d_sat, cur[2] + d_con)

    os.makedirs(os.path.dirname(JSON), exist_ok=True)
    lines = []
    if os.path.exists(JSON):
        lines = [l for l in open(JSON, encoding="utf-8").read().splitlines()
                 if l.split(" ")[0] != a.scene]
    lines.append("%s %.2f %.1f %.1f" % (a.scene, new[0], new[1], new[2]))
    with open(JSON, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")

    print(f"{a.scene}: 我/参 亮度 {o[0]:.0f}/{r[0]:.0f}{'✓' if ok[0] else '✗'}"
          f" 对比 {o[1]:.0f}/{r[1]:.0f}{'✓' if ok[1] else '✗'}"
          f" 饱和 {o[2]:.0f}/{r[2]:.0f}{'✓' if ok[2] else '✗'}")
    print(f"  调色: {cur} → ({new[0]:.2f}, {new[1]:.1f}, {new[2]:.1f}) 已写 grade.json")


if __name__ == "__main__":
    main()
