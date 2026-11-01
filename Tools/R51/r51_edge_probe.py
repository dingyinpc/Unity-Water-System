# -*- coding: utf-8 -*-
"""r51_edge_probe.py — 定位"水面域边界硬边"并量化它有多硬

为什么重写：R49/R50 用的是"左上区最大列梯度"，峰值一直落在**画面第 49 列（左边缘暗角）**，
根本不是那条斜边 —— 前几轮的比较全部无效。这里改成：

  1) Canny + HoughLinesP 在指定 ROI 里找**直线**；
  2) 取最长的一条，沿它的**法线方向**采样亮度剖面；
  3) 报三个数：
       step   台阶高度（两侧平台亮度差，灰阶）
       width  过渡宽度（10%→90% 之间跨多少像素）
       ratio  step / width —— 同样高度，跨得越窄越"硬"

判据参考：ratio > 2.0 肉眼就是硬边；< 0.5 已是渐变。

用法：
  python Tools/R51/r51_edge_probe.py --img a.png --roi 0,0,420,200
  python Tools/R51/r51_edge_probe.py --compare before.png after.png --roi 0,0,420,200
"""
import argparse
import os
import sys

import cv2
import numpy as np


def imread_u(p):
    return cv2.imdecode(np.fromfile(p, np.uint8), cv2.IMREAD_COLOR)


def parse_roi(s, shape):
    if not s:
        h, w = shape[:2]
        return 0, 0, w, h
    x0, y0, x1, y1 = [int(v) for v in s.split(",")]
    h, w = shape[:2]
    return max(0, x0), max(0, y0), min(w, x1), min(h, y1)


def find_lines(gray, roi):
    x0, y0, x1, y1 = roi
    crop = gray[y0:y1, x0:x1]
    g = cv2.GaussianBlur(crop, (3, 3), 0)
    g8 = np.clip(g, 0, 255).astype(np.uint8)      # Canny 只吃 CV_8U
    edges = cv2.Canny(g8, 30, 90)
    # 去掉贴边的像素（画面边框/暗角会制造假直线）
    edges[:3, :] = 0; edges[-3:, :] = 0; edges[:, :3] = 0; edges[:, -3:] = 0
    lines = cv2.HoughLinesP(edges, 1, np.pi / 180, threshold=40,
                            minLineLength=min(crop.shape[:2]) // 3, maxLineGap=12)
    out = []
    if lines is not None:
        pts = np.asarray(lines).reshape(-1, 4)     # OpenCV 5 返回 (n,1,4)，旧版 (n,4)
        for l in pts:
            x1_, y1_, x2_, y2_ = [int(v) for v in l]
            length = float(np.hypot(x2_ - x1_, y2_ - y1_))
            out.append((length, x1_, y1_, x2_, y2_))
    out.sort(reverse=True)
    return out, crop


def profile_across(gray, line, half=60, samples=241):
    """沿直线法线取亮度剖面（在世界图坐标里取，避免裁剪边界）"""
    _, x1, y1, x2, y2 = line
    p1 = np.array([x1, y1], np.float32)
    p2 = np.array([x2, y2], np.float32)
    d = p2 - p1
    n = np.array([-d[1], d[0]], np.float32)
    n /= (np.linalg.norm(n) + 1e-6)
    mid = (p1 + p2) * 0.5
    h, w = gray.shape[:2]
    vals = []
    ts = np.linspace(-half, half, samples)
    for t in ts:
        pt = mid + n * t
        x, y = int(round(pt[0])), int(round(pt[1]))
        if 0 <= x < w and 0 <= y < h:
            vals.append(gray[y, x])
        else:
            vals.append(np.nan)
    vals = np.array(vals, np.float32)
    return ts, vals


def step_metrics(ts, vals):
    ok = ~np.isnan(vals)
    ts, vals = ts[ok], vals[ok]
    if len(vals) < 40:
        return None
    k = max(5, len(vals) // 10)
    low = float(np.median(vals[:k]))
    high = float(np.median(vals[-k:]))
    step = abs(high - low)
    # 归一化到 0..1 后找 10% / 90% 的穿越位置
    v = (vals - vals.min()) / (np.ptp(vals) + 1e-6)
    lo, hi = 0.1, 0.9
    i_lo = np.argmax(v >= lo) if np.any(v >= lo) else 0
    i_hi = np.argmax(v >= hi) if np.any(v >= hi) else len(v) - 1
    width = abs(ts[min(i_hi, len(ts) - 1)] - ts[min(i_lo, len(ts) - 1)])
    return step, float(width), step / max(width, 1e-6), low, high


def analyze(path, roi_s, save_overlay=None):
    im = imread_u(path)
    gray = cv2.cvtColor(im, cv2.COLOR_BGR2GRAY).astype(np.float32)
    roi = parse_roi(roi_s, im.shape)
    lines, crop = find_lines(gray, roi)
    if not lines:
        return {"file": os.path.basename(path), "lines": 0}
    best = lines[0]
    # Hough 坐标是 ROI 内的，转回全图
    x0, y0 = roi[0], roi[1]
    best_g = (best[0], best[1] + x0, best[2] + y0, best[3] + x0, best[4] + y0)
    ts, vals = profile_across(gray, best_g)
    m = step_metrics(ts, vals)
    if save_overlay:
        vis = im.copy()
        cv2.line(vis, (best_g[1], best_g[2]), (best_g[3], best_g[4]), (0, 0, 255), 2)
        cv2.imwrite(save_overlay, vis)
    res = {"file": os.path.basename(path), "lines": len(lines),
           "line": (best_g[1], best_g[2], best_g[3], best_g[4]), "len": round(best_g[0], 1)}
    if m:
        res.update(step=round(m[0], 1), width=round(m[1], 1), ratio=round(m[2], 2),
                   low=round(m[3], 1), high=round(m[4], 1))
    return res


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--img")
    ap.add_argument("--compare", nargs=2)
    ap.add_argument("--roi", default="0,0,420,200")
    ap.add_argument("--overlay", default="")
    a = ap.parse_args()

    if a.img:
        r = analyze(a.img, a.roi, a.overlay or None)
        print(r)
        return
    if a.compare:
        r1 = analyze(a.compare[0], a.roi, "Docs/Temp/r51_before_overlay.png")
        r2 = analyze(a.compare[1], a.roi, "Docs/Temp/r51_after_overlay.png")
        print("改前:", r1)
        print("改后:", r2)
        if "ratio" in r1 and "ratio" in r2:
            print("台阶比 %.2f -> %.2f （越低越软）" % (r1["ratio"], r2["ratio"]))
        return
    ap.print_help()


if __name__ == "__main__":
    main()
