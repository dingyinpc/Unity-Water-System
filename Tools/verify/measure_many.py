# -*- coding: utf-8 -*-
"""measure_many.py — 批量量测（配合 verify_scenes.ps1）

输出：亮度 / 对比(std) / 饱和 / 近黑占比(0-25)。
并可选 `--baseline <json>` 做突变报警（|变化| > 35% 标记 SUSPECT）。

用法：
  python Tools/verify/measure_many.py Docs/screenshots/verify_Main.png ...
  python Tools/verify/measure_many.py --write-baseline Tools/verify/baseline.json a.png b.png
"""
import argparse
import json
import os
import sys

import cv2
import numpy as np


def imread_u(p):
    return cv2.imdecode(np.fromfile(p, np.uint8), cv2.IMREAD_COLOR)


def stat(path):
    im = imread_u(path)
    if im is None:
        return None
    g = cv2.cvtColor(im, cv2.COLOR_BGR2GRAY).astype(np.float32)
    hsv = cv2.cvtColor(im, cv2.COLOR_BGR2HSV)
    return {
        "luma": float(g.mean()),
        "contrast": float(g.std()),
        "sat": float(hsv[:, :, 1].mean()),
        "near_black": float((g < 25).mean() * 100.0),
        "blown": float((g > 250).mean() * 100.0),
        "size": list(im.shape[:2]),
    }


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("images", nargs="+")
    ap.add_argument("--baseline", default="")
    ap.add_argument("--write-baseline", default="")
    a = ap.parse_args()

    base = {}
    if a.baseline and os.path.exists(a.baseline):
        base = json.load(open(a.baseline, encoding="utf-8-sig"))

    print(f"{'场景':<10}{'亮度':>8}{'对比':>8}{'饱和':>8}{'近黑%':>8}{'爆白%':>8}   判定")
    out = {}
    for p in a.images:
        name = os.path.basename(p).replace("verify_", "").replace(".png", "")
        s = stat(p)
        if s is None:
            print(f"{name:<10}  读不到文件 {p}")
            continue
        out[name] = s
        verdict = ""
        if name in base:
            for k, th in (("luma", 0.35), ("contrast", 0.35), ("sat", 0.35)):
                b = base[name].get(k, 0)
                if b > 1e-6 and abs(s[k] - b) / b > th:
                    verdict += f" ⚠{k} {b:.0f}->{s[k]:.0f}"
        print(f"{name:<10}{s['luma']:>8.1f}{s['contrast']:>8.1f}{s['sat']:>8.1f}{s['near_black']:>8.2f}{s['blown']:>8.2f}   {verdict or 'OK'}")

    if a.write_baseline:
        old = {}
        if os.path.exists(a.write_baseline):
            old = json.load(open(a.write_baseline, encoding="utf-8-sig"))
        old.update(out)
        json.dump(old, open(a.write_baseline, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
        print(f"\n基线已写入 {a.write_baseline}")


if __name__ == "__main__":
    main()
