# -*- coding: utf-8 -*-
"""调试图配色占比 —— 把"调试可视化图"按色相分类统计占比（检测报告用）。
用法: python Tools/Video/viz_frac.py <图1> [图2 ...]
输出: 每图一行：尺寸 / blue% orange% cyan% red% dark% 其余
用途: 对比"我方的干湿/水位调试图"与"参考视频调试图"的配色结构（f1700 / f1850）。
"""
import sys
import os
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _videoutil import imread_u  # noqa: E402


def fracs(path):
    import cv2
    img = imread_u(path)
    if img is None:
        return None
    hsv = cv2.cvtColor(img, cv2.COLOR_BGR2HSV)
    H, S, V = hsv[..., 0].astype(np.int32), hsv[..., 1].astype(np.int32), hsv[..., 2].astype(np.int32)
    tot = float(H.size)
    sat = S > 70
    blue   = sat & (H >= 100) & (H <= 135)
    cyan   = sat & (H > 82) & (H < 100) & (V > 140)
    orange = sat & (H >= 3) & (H <= 28)
    red    = sat & (((H < 3) | (H > 168)) & (V > 120))
    dark   = V < 45
    other = ~(blue | cyan | orange | red | dark)
    return (img.shape[1], img.shape[0],
            blue.mean() * 100, orange.mean() * 100, cyan.mean() * 100,
            red.mean() * 100, dark.mean() * 100, other.mean() * 100)


for p in sys.argv[1:]:
    r = fracs(p)
    if r is None:
        print(f"{os.path.basename(p)}: 读图失败")
        continue
    w, h, b, o, c, rd, d, ot = r
    print(f"{os.path.basename(p):28s} {w}x{h}  blue {b:5.1f}%  orange {o:5.1f}%  cyan {c:5.2f}%  red {rd:5.1f}%  dark {d:5.1f}%  other {ot:5.1f}%")
