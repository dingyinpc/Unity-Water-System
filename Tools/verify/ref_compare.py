# -*- coding: utf-8 -*-
"""ref_compare.py — 我方六场景 vs 参考片帧 并排对照 + 简指标

参考片只有一段（热带海岸/环礁），所以不是"一一对应"，而是：
  · Islet / Ocean → 参考 S0 俯视段（f000060）
  · Main / Pond   → 参考中段水面（f001500）
  · 天空           → 参考黄昏段（f003200）

输出：Docs/screenshots/refcmp_<scene>.png（上=我方，下=参考），并打印指标。
"""
import os
import sys

import cv2
import numpy as np

sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "Video"))
from _videoutil import imread_u  # noqa: E402

REF_DIR = "Docs/视频逐帧/f1083870/frames"
UI = (120, 58, 842, 440)   # 参考帧裁掉抖音 UI


def load_ref(frame_no):
    p = os.path.join(REF_DIR, "f%06d.jpg" % frame_no)
    im = imread_u(p)
    x0, y0, x1, y1 = UI
    return im[y0:y1, x0:x1]


def metrics(im):
    g = cv2.cvtColor(im, cv2.COLOR_BGR2GRAY).astype(np.float32)
    hsv = cv2.cvtColor(im, cv2.COLOR_BGR2HSV)
    return g.mean(), g.std(), hsv[:, :, 1].mean(), (g < 40).mean() * 100


def ssim(a, b):
    a = cv2.cvtColor(cv2.resize(a, (b.shape[1], b.shape[0])), cv2.COLOR_BGR2GRAY).astype(np.float64)
    b = cv2.cvtColor(b, cv2.COLOR_BGR2GRAY).astype(np.float64)
    m1, m2 = a.mean(), b.mean()
    v1, v2, c = a.var(), b.var(), ((a - m1) * (b - m2)).mean()
    C1, C2 = (0.01 * 255) ** 2, (0.03 * 255) ** 2
    return ((2 * m1 * m2 + C1) * (2 * c + C2)) / ((m1 * m1 + m2 * m2 + C1) * (v1 + v2 + C2))


PAIRS = [
    ("Islet", "Docs/screenshots/verify_Islet.png", 60),
    ("Ocean", "Docs/screenshots/verify_Ocean.png", 60),
    ("Main",  "Docs/screenshots/verify_Main.png", 1500),
    ("Pond",  "Docs/screenshots/verify_Pond.png", 1500),
    ("Pool",  "Docs/screenshots/verify_Pool.png", 1500),
    ("FFT",   "Docs/screenshots/verify_FFT.png", 1500),
]


def main():
    print("%-7s %-24s %-24s %s" % ("场景", "我方(亮度/对比/饱和/近黑)", "参考(亮度/对比/饱和/近黑)", "SSIM"))
    for name, path, frame in PAIRS:
        if not os.path.exists(path):
            print("%-7s 缺图 %s" % (name, path))
            continue
        ours = imread_u(path)
        ref = load_ref(frame)
        mo, mr = metrics(ours), metrics(ref)
        s = ssim(ours, ref)
        print("%-7s %5.1f/%5.1f/%5.1f/%4.1f%%      %5.1f/%5.1f/%5.1f/%4.1f%%      %.3f"
              % (name, mo[0], mo[1], mo[2], mo[3], mr[0], mr[1], mr[2], mr[3], s))

        h, w = ref.shape[:2]
        small = cv2.resize(ours, (w, h), interpolation=cv2.INTER_AREA)
        sheet = np.vstack([small, np.full((6, w, 3), 255, np.uint8), ref])
        cv2.putText(sheet, "OURS %s" % name, (8, 22), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 0, 255), 2)
        cv2.putText(sheet, "REF f%06d" % frame, (8, h + 28), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 0, 255), 2)
        cv2.imwrite("Docs/screenshots/refcmp_%s.png" % name, sheet)


if __name__ == "__main__":
    main()
