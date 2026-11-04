#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
texai.py — AI 生图贴图的自动后处理流水线

用法：
    python Tools/TexAI/texai.py --in Docs/Temp/nb --out Assets/Art/Textures/AI/Proc
    python Tools/TexAI/texai.py --in <目录或单张图> --out <目录> --mode card

自动判定模式：
    · 画面里"接近纯白"的像素 > 45%  ⇒ 当成**植被卡**（单株白底）：抠 alpha + 去白边 + 裁边
    · 否则                          ⇒ 当成**图案贴图**：查无缝 → 需要时修 → 生成法线图

产出：
    <名>_albedo.png    图案：贴图本体（PNG，无 JPEG 压缩痕）
    <名>_normal.png    图案：由高度（亮度）推导的法线图
    <名>_rgba.png      植被卡：带 alpha 的图，四周留 2% 安全边

设计原则：
    1) **先量再接**：无缝程度用"接缝比"量化（≈1 就是连续），不靠肉眼。
    2) **法线自己算**：AI 直接生成 normal 不可靠，从亮度反推高度再 Sobel 是可控的。
    3) **不做无损外的事**：分辨率不够就如实报告，不偷偷放大（糊了也看不出来才更糟）。
"""
import argparse
import os
import sys

import numpy as np

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', 'Video'))
from _videoutil import imread_u  # noqa: E402  项目统一的"能读中文路径"读图

import cv2  # noqa: E402


def seam_ratio(g):
    """接缝比：边缘差 / 内部相邻差。≈1 连续；>1.6 肉眼可见接缝。"""
    lr = float(np.abs(g[:, 0] - g[:, -1]).mean())
    tb = float(np.abs(g[0, :] - g[-1, :]).mean())
    inner_c = float(np.abs(np.diff(g, axis=1)).mean()) + 1e-6
    inner_r = float(np.abs(np.diff(g, axis=0)).mean()) + 1e-6
    return lr / inner_c, tb / inner_r


def make_seamless(img, band_frac=0.12):
    """
    错位混合法（GIMP "Make Seamless" 同思路）：
      1) 把图滚动半幅 —— 原来的左右/上下接缝被挪到画面正中；
      2) 在正中那一圈用余弦羽化的"模糊版"覆盖，接缝就没了。

    ⚠ 只对"接缝比 > 1.6"的图调用。本来就连续的图不要动 —— 动一次就白损失一圈细节。
    ⚠ 代价：正中会有一道略糊的十字带。所以**优先让 AI 直接出无缝**，这条只是兜底。
    """
    h, w = img.shape[:2]
    s = np.roll(img, (h // 2, w // 2), axis=(0, 1)).astype(np.float32)

    b = max(8, int(min(h, w) * band_frac))
    t = np.linspace(-1.0, 1.0, b, dtype=np.float32)
    ramp = 0.5 + 0.5 * np.cos(np.pi * t)          # 中间 1，两端 0

    my = np.zeros((h, 1), np.float32)
    mx = np.zeros((1, w), np.float32)
    cy, cx = h // 2, w // 2

    y0 = max(0, cy - b // 2); y1 = min(h, y0 + b)
    x0 = max(0, cx - b // 2); x1 = min(w, x0 + b)
    my[y0:y1, 0] = ramp[:y1 - y0]
    mx[0, x0:x1] = ramp[:x1 - x0]

    m = np.maximum(my, mx)[:, :, None]
    blur = cv2.GaussianBlur(s, (0, 0), max(1.0, b * 0.45))
    out = s * (1.0 - m) + blur * m
    return np.clip(out, 0, 255).astype(np.uint8)


def to_normal(gray, strength=2.0):
    """由高度图（灰度）算切线空间法线图。"""
    h = cv2.GaussianBlur(gray.astype(np.float32) / 255.0, (0, 0), 1.2)
    gx = cv2.Sobel(h, cv2.CV_32F, 1, 0, ksize=3) * strength
    gy = cv2.Sobel(h, cv2.CV_32F, 0, 1, ksize=3) * strength
    nz = np.ones_like(gx)
    n = np.dstack([-gx, -gy, nz])
    n /= np.linalg.norm(n, axis=2, keepdims=True) + 1e-6
    out = (n * 0.5 + 0.5) * 255.0
    return out.astype(np.uint8)


def process_tile(img, out_dir, stem, strength=2.0):
    g = img.astype(np.float32).mean(2)
    lr, tb = seam_ratio(g)
    fixed = False
    if max(lr, tb) > 1.6:
        img = make_seamless(img)
        g = img.astype(np.float32).mean(2)
        lr2, tb2 = seam_ratio(g)
        fixed = True
        print(f'  [修] 接缝比 {lr:.2f}/{tb:.2f} -> {lr2:.2f}/{tb2:.2f}（已错位混合）')
        lr, tb = lr2, tb2
    else:
        print(f'  [过] 接缝比 {lr:.2f}/{tb:.2f}（本身可平铺，不动）')

    alb = os.path.join(out_dir, f'{stem}_albedo.png')
    nrm = os.path.join(out_dir, f'{stem}_normal.png')
    cv2.imwrite(alb, img)
    if len(img.shape) == 3:
        gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
    else:
        gray = img
    cv2.imwrite(nrm, to_normal(gray, strength))
    print(f'  [出] {os.path.basename(alb)}  {os.path.basename(nrm)}')
    return dict(stem=stem, kind='tile', seam_lr=lr, seam_tb=tb, fixed=fixed)


def process_card(img, out_dir, stem, thresh=238, despill=0.55):
    """白底单株 → 带 alpha。alpha = 偏离白色的程度；同时去掉白边（despill）。"""
    h, w = img.shape[:2]
    f = img.astype(np.float32)
    mn = f.min(2)
    # 白底纯度 / 主体占比
    white_frac = float((mn > thresh - 2).mean() * 100)

    a = np.clip((thresh - mn) / (thresh * 0.35), 0, 1) ** 0.75
    a = (a * 255).astype(np.uint8)

    # 去白边：用 alpha 边缘一圈的白色做抑色（简单版：按 alpha 提升饱和度）
    b, g, r = f[:, :, 0], f[:, :, 1], f[:, :, 2]
    lum = 0.114 * b + 0.587 * g + 0.299 * r
    sat_boost = 1.0 + despill * (1.0 - a.astype(np.float32) / 255.0) * 0.0  # 占位，保持简单
    # 真正的 despill：边缘像素向"去白"方向拉（白底混进 alpha 半透明区会让边缘发灰）
    edge = ((a > 8) & (a < 240))
    for c in range(3):
        ch = f[:, :, c]
        ch = np.where(edge, ch - (255.0 - ch) * 0.12, ch)
        f[:, :, c] = ch
    f = np.clip(f, 0, 255)

    rgba = np.dstack([f.astype(np.uint8), a])

    # 裁到内容外接框 + 2% 安全边
    ys, xs = np.where(a > 10)
    if len(xs) > 0:
        x0, x1 = xs.min(), xs.max()
        y0, y1 = ys.min(), ys.max()
        pad = int(max(x1 - x0, y1 - y0) * 0.02) + 2
        x0 = max(0, x0 - pad); y0 = max(0, y0 - pad)
        x1 = min(w - 1, x1 + pad); y1 = min(h - 1, y1 + pad)
        rgba = rgba[y0:y1 + 1, x0:x1 + 1]

    out = os.path.join(out_dir, f'{stem}_rgba.png')
    cv2.imwrite(out, rgba)
    cov = float((a > 10).mean() * 100)
    print(f'  [出] {os.path.basename(out)}  白底 {white_frac:.1f}%  主体 {cov:.1f}%  尺寸 {rgba.shape[1]}x{rgba.shape[0]}')
    return dict(stem=stem, kind='card', white=white_frac, coverage=cov)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--in', dest='src', required=True)
    ap.add_argument('--out', dest='dst', required=True)
    ap.add_argument('--mode', default='auto', choices=['auto', 'tile', 'card'])
    ap.add_argument('--normal-strength', type=float, default=2.0)
    ap.add_argument('--thresh', type=float, default=238.0, help='白底判定阈值')
    args = ap.parse_args()

    os.makedirs(args.dst, exist_ok=True)
    files = []
    if os.path.isdir(args.src):
        for n in sorted(os.listdir(args.src)):
            if n.lower().endswith(('.png', '.jpg', '.jpeg', '.webp')):
                files.append(os.path.join(args.src, n))
    else:
        files = [args.src]

    if not files:
        print('没有找到图片'); return 1

    results = []
    print(f'处理 {len(files)} 张 → {args.dst}')
    for p in files:
        img = imread_u(p)
        if img is None:
            print(f'  [跳过] 读不了 {p}'); continue
        stem = os.path.splitext(os.path.basename(p))[0]
        mn = img.min(2)
        white_frac = float((mn > args.thresh - 2).mean() * 100)
        mode = args.mode
        if mode == 'auto':
            mode = 'card' if white_frac > 45.0 else 'tile'
        print(f'- {stem}  ({img.shape[1]}x{img.shape[0]})  判定={mode}')
        if mode == 'card':
            results.append(process_card(img, args.dst, stem, args.thresh))
        else:
            results.append(process_tile(img, args.dst, stem, args.normal_strength))

    print('\n=== 汇总 ===')
    for r in results:
        if r['kind'] == 'tile':
            print(f"{r['stem']:<22} tile  接缝 {r['seam_lr']:.2f}/{r['seam_tb']:.2f}  {'已修' if r['fixed'] else '原样'}")
        else:
            print(f"{r['stem']:<22} card  白底 {r['white']:.1f}%  主体 {r['coverage']:.1f}%")
    return 0


if __name__ == '__main__':
    sys.exit(main())
