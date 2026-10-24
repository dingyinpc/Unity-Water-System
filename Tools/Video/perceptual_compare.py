# -*- coding: utf-8 -*-
"""perceptual_compare.py — 感官级复刻量具（R19：把"画面质感"变成可测的量）

用户判断：此前是功能+动态判据复刻；"精细程度/噪点/感官"未系统复刻。
本工具四把尺（对齐到同尺寸后计算）：

  S1 结构相似度 SSIM     —— 整体结构感官重合度（1=逐结构相同）
  S2 噪声频谱斜率         —— 径向功率谱 log-log 斜率：自然图像约 -2~-3；
                             越平(-1以内)=颗粒/噪点重，越陡(<-3.5)=CG光滑
  S3 微对比分布           —— 8×8 块局部标准差的分布（细节密度/颗粒感的代理）
  S4 高频能量占比         —— 拉普拉斯能量/总能量：锐度与颗粒的综合

用法：
  python Tools/Video/perceptual_compare.py --ours <seq目录或单帧> --ref <帧号或路径>
"""
import argparse
import glob
import os
import re

import cv2
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
REF_DIR = os.path.join(ROOT, "Docs", "视频逐帧", "f1083870", "frames")


def load_ours(spec):
    if os.path.isdir(spec):
        fs = sorted(glob.glob(os.path.join(spec, "*.jpg")))
        return cv2.imdecode(np.fromfile(fs[len(fs) // 2], np.uint8), cv2.IMREAD_GRAYSCALE)
    return cv2.imdecode(np.fromfile(spec, np.uint8), cv2.IMREAD_GRAYSCALE)


def load_ref(frame_no):
    p = os.path.join(REF_DIR, "f%06d.jpg" % frame_no)
    return cv2.imdecode(np.fromfile(p, np.uint8), cv2.IMREAD_GRAYSCALE)


def to_size(img, w, h):
    return cv2.resize(img, (w, h), interpolation=cv2.INTER_AREA).astype(np.float64)


# ── S1 SSIM（标准公式，8x8 窗、全图滑动）──
def ssim(a, b):
    C1, C2 = (0.01 * 255) ** 2, (0.03 * 255) ** 2
    mu_a = cv2.GaussianBlur(a, (11, 11), 1.5)
    mu_b = cv2.GaussianBlur(b, (11, 11), 1.5)
    sa = cv2.GaussianBlur(a * a, (11, 11), 1.5) - mu_a * mu_a
    sb = cv2.GaussianBlur(b * b, (11, 11), 1.5) - mu_b * mu_b
    sab = cv2.GaussianBlur(a * b, (11, 11), 1.5) - mu_a * mu_b
    m = (2 * mu_a * mu_b + C1) * (2 * sab + C2)
    d = (mu_a ** 2 + mu_b ** 2 + C1) * (sa + sb + C2)
    return float((m / d).mean())


# ── S2 径向功率谱斜率 ──
def spec_slope(img):
    f = np.fft.fftshift(np.fft.fft2(img - img.mean()))
    p = np.abs(f) ** 2
    h, w = img.shape
    y, x = np.indices((h, w))
    r = np.sqrt((x - w / 2) ** 2 + (y - h / 2) ** 2).astype(int)
    # 1/4 以上频率才有统计意义；径向平均
    tbin = np.bincount(r.ravel(), p.ravel())
    nr = np.bincount(r.ravel())
    prof = tbin / np.maximum(nr, 1)
    freq = np.arange(len(prof))
    sel = (freq > 5) & (freq < min(h, w) / 2) & (prof > 0)
    if sel.sum() < 10:
        return 0.0
    sl = np.polyfit(np.log(freq[sel]), np.log(prof[sel]), 1)[0]
    return float(sl)


# ── S3 微对比（8×8 局部 std 分布：p50/p90）──
def micro_contrast(img):
    h, w = img.shape
    b = img[:h // 8 * 8, :w // 8 * 8].reshape(h // 8, 8, w // 8, 8)
    stds = b.transpose(0, 2, 1, 3).reshape(-1, 64).std(axis=1)
    return float(np.median(stds)), float(np.percentile(stds, 90))


# ── S4 高频能量占比 ──
def hf_ratio(img):
    lap = cv2.Laplacian(img, cv2.CV_64F)
    return float((lap ** 2).sum() / max((img ** 2).sum(), 1e-9))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--ours", required=True)
    ap.add_argument("--ref", type=int, required=True)
    ap.add_argument("--label", default="perceptual")
    a = ap.parse_args()

    oa = load_ours(a.ours)
    rb = load_ref(a.ref)
    H = 432
    oa = to_size(oa, int(H * oa.shape[1] / oa.shape[0]), H)
    rb = to_size(rb, int(H * rb.shape[1] / rb.shape[0]), H)
    W = min(oa.shape[1], rb.shape[1])
    oa, rb = oa[:, :W], rb[:, :W]

    s1 = ssim(oa, rb)
    sl_o, sl_r = spec_slope(oa), spec_slope(rb)
    mc_o, mc_r = micro_contrast(oa), micro_contrast(rb)
    hf_o, hf_r = hf_ratio(oa), hf_ratio(rb)

    print(f"[{a.label}]")
    print(f"  S1 SSIM 结构:   {s1:.3f}   (0=无关 1=逐结构相同；>0.85=感官近似)")
    print(f"  S2 频谱斜率:    我 {sl_o:.2f} vs 参 {sl_r:.2f}   (越平=颗粒重，越陡=CG光滑)")
    print(f"  S3 微对比 p50/p90: 我 {mc_o[0]:.1f}/{mc_o[0+1]:.1f} vs 参 {mc_r[0]:.1f}/{mc_r[1]:.1f}")
    print(f"  S4 高频占比:    我 {hf_o:.4f} vs 参 {hf_r:.4f}   ({hf_o/max(hf_r,1e-9):.2f}×)")


if __name__ == "__main__":
    main()
