#!/usr/bin/env python3
"""img_ab.py — 两张图 A/B 对比（阈值像素占比 + 区域均值 + 差异热图）。

用法:
  python Tools/img_ab.py A.png B.png [--thresh 8] [--heat out.png] [--region x,y,w,h]

为什么要它：本项目"效果到底有没有生效"一律靠**关/开两张冻结截图**做差，
差多少像素、差在哪，必须给数（20.6% 那种），不能靠眼睛。
中文路径用 np.fromfile + cv2.imdecode（cv2.imread 读不了中文路径）。
"""
import argparse
import numpy as np
import cv2


def imread_u(path):
    data = np.fromfile(path, dtype=np.uint8)
    return cv2.imdecode(data, cv2.IMREAD_COLOR)


def imwrite_u(path, img):
    ok, buf = cv2.imencode(".png", img)
    if not ok:
        raise RuntimeError("encode failed: " + path)
    buf.tofile(path)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("a")
    ap.add_argument("b")
    ap.add_argument("--thresh", type=float, default=8.0,
                    help="逐像素最大通道差 > 此值算'变了'（0-255）")
    ap.add_argument("--heat", default=None, help="差异热图输出路径")
    ap.add_argument("--region", default=None, help="只统计区域 x,y,w,h")
    args = ap.parse_args()

    A = imread_u(args.a)
    B = imread_u(args.b)

    print(f"A {args.a}")
    print(f"  shape={A.shape}")
    print(f"  mean RGB = {A[..., ::-1].reshape(-1, 3).mean(0).round(2)}")
    print(f"B {args.b}")
    print(f"  shape={B.shape}")
    print(f"  mean RGB = {B[..., ::-1].reshape(-1, 3).mean(0).round(2)}")

    if A.shape != B.shape:
        print("!! 尺寸不同，无法逐像素比较")
        return

    diff = np.abs(A.astype(np.int16) - B.astype(np.int16)).max(axis=2)
    roi = np.ones_like(diff, dtype=bool)
    if args.region:
        x, y, w, h = [int(v) for v in args.region.split(",")]
        roi[:] = False
        roi[y:y + h, x:x + w] = True

    d = diff[roi]
    changed = d > args.thresh
    print("--- 差异 ---")
    print(f"比较像素数 = {d.size}")
    print(f"阈值 {args.thresh:g} 下变化像素 = {int(changed.sum())} ({100.0 * changed.mean():.2f}%)")
    print(f"最大通道差 = {int(d.max())}  平均通道差 = {d.mean():.3f}")
    # 分档
    for t in (4, 8, 16, 32, 64):
        print(f"  差>{t:3d}: {100.0 * (d > t).mean():5.2f}%")

    if args.heat:
        heat = np.clip(diff * 4, 0, 255).astype(np.uint8)
        hm = cv2.applyColorMap(heat, cv2.COLORMAP_JET)
        hm[~roi] = (hm[~roi] * 0.25).astype(np.uint8)
        imwrite_u(args.heat, hm)
        print(f"热图 -> {args.heat}")


if __name__ == "__main__":
    main()
