# -*- coding: utf-8 -*-
"""dynamic_baseline.py — R1 参考侧动态基准表（《10》§3 协议）

从 Docs/视频逐帧/f1083870/analysis/metrics.csv（5568 帧 @30.03fps 逐帧指标）
按 15 个章节段落聚合四判据的参考值（全画面口径，供报告对照与粗定位；
正式判定由 seq_compare.py 双方像素同尺重算）。

产出: Docs/视频逐帧/f1083870/analysis/dynamic_baseline.csv
用法: python Tools/Video/dynamic_baseline.py
"""
import csv
import os
import sys

import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ANALYSIS = os.path.join(ROOT, "Docs", "视频逐帧", "f1083870", "analysis")
METRICS = os.path.join(ANALYSIS, "metrics.csv")
OUT = os.path.join(ANALYSIS, "dynamic_baseline.csv")

# 08 文档章节表（秒）——《10》§1 的同一张表
SEGMENTS = [
    ("S0_片头演示",   0.0,   5.0),
    ("S1_FFT海洋",    5.0,  16.0),
    ("S2_水平位移",  16.0,  20.0),
    ("S3_SSR",       20.0,  30.0),
    ("S4_水下折射",  30.0,  33.0),
    ("S5_浪峰增强",  33.0,  37.0),
    ("S6_水底散射",  37.0,  42.0),
    ("S7_单层焦散",  42.0,  46.0),
    ("S8_多尺度焦散", 46.0, 56.0),
    ("S9_近岸水流",  56.0,  78.0),
    ("S10_岸边泡沫", 78.0, 100.0),
    ("S11_昼夜",    100.0, 115.0),
    ("S12_天空渐变", 113.0, 121.0),
    ("S13_体积云",   121.5, 137.0),
    ("S14_云色云影", 137.0, 170.0),
    ("S15_远景雾",   170.0, 185.4),
]


def load_metrics():
    rows = []
    with open(METRICS, "r", encoding="utf-8") as f:
        for r in csv.DictReader(f):
            rows.append(r)
    return rows


def flicker_hz(luma, fps):
    """亮度时间序列去趋势后的频谱主峰（0.4~8Hz）——碎闪节奏。"""
    t = np.arange(len(luma), dtype=np.float64)
    k = np.polyfit(t, luma, 1)
    x = luma - np.polyval(k, t)
    x = x * np.hanning(len(x))
    mag = np.abs(np.fft.rfft(x))
    freqs = np.fft.rfftfreq(len(x), d=1.0 / fps)
    band = (freqs >= 0.4) & (freqs <= 8.0)
    if not band.any() or mag[band].max() < 1e-6:
        return 0.0
    return float(freqs[band][np.argmax(mag[band])])


def main():
    rows = load_metrics()
    frame = np.array([int(r["frame"]) for r in rows])
    time = np.array([float(r["time"]) for r in rows])
    luma = np.array([float(r["luma"]) for r in rows])
    diff = np.array([float(r["diff"]) if r["diff"] != "nan" else np.nan for r in rows])
    fps = 30.03

    out_rows = []
    for name, t0, t1 in SEGMENTS:
        m = (time >= t0) & (time < t1)
        d = diff[m]
        d = d[~np.isnan(d)]
        if len(d) < 10:
            continue
        med, std = float(np.median(d)), float(np.std(d))
        spike = float(np.max(np.abs(d - med)) / std) if std > 1e-9 else 0.0
        out_rows.append({
            "segment": name,
            "f_start": int(frame[m][0]), "f_end": int(frame[m][-1]),
            "n": int(m.sum()),
            "luma_mean": round(float(np.mean(luma[m])), 2),
            # D1 运动能量：相邻帧差均值（全画面，0-255 口径）
            "d1_mean_diff": round(float(np.mean(d)), 3),
            "d1_med_diff": round(med, 3),
            # D2 连续性：孤立尖峰倍数
            "d2_spike": round(spike, 2),
            # D4 闪烁主频 Hz
            "d4_flicker_hz": round(flicker_hz(luma[m].astype(np.float64), fps), 2),
        })

    with open(OUT, "w", encoding="utf-8-sig", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(out_rows[0].keys()))
        w.writeheader()
        w.writerows(out_rows)

    print(f"-> {OUT}  ({len(out_rows)} 段)")
    for r in out_rows:
        print(f"{r['segment']:<14} f{r['f_start']}-{r['f_end']} n={r['n']:<4}"
              f" luma={r['luma_mean']:<7} D1={r['d1_mean_diff']:<7}"
              f" spike={r['d2_spike']:<6} flicker={r['d4_flicker_hz']}Hz")


if __name__ == "__main__":
    sys.exit(main())
