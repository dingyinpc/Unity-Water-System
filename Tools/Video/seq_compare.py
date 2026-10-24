# -*- coding: utf-8 -*-
"""seq_compare.py — R1“3 秒逐帧对比”核心工具（《10》§3 协议）

输入我方连帧序列（SeqCapture 产物 f*_t*.jpg）与参考视频任意帧区间，
双方像素同尺（都归一到 432 行高）后按 ROI 计算《10》四动态判据：

  D1 运动能量  ρ_E = mean|Δluma|_ours / mean|Δluma|_ref   通过 0.5~2.0
  D2 连续性    spike = max|Δ−med|/σ（我方需 <3，参考值并列报告）
  D3 特征速度  ρ_v = v_ours / v_ref（相位相关测平移，%画宽/秒口径）
               sky ROI=云速，water ROI=波纹漂移 —— 通过 0.7~1.4
  D4 闪烁主频  ρ_f = f_ours / f_ref（ROI 亮度频谱主峰）     通过 0.6~1.67

产出（--out 目录）：verdict.json / 判据表(stdout) / 曲线叠图.png / 并排联络表.jpg

用法：
  python Tools/Video/seq_compare.py --ours Docs/screenshots/seq_OceanScene_xxx \
      --ref-start 3646 --ref-end 3736 --label Ocean_S13 --out Docs/screenshots/cmp_ocean
"""
import argparse
import glob
import json
import os
import re
import sys

import cv2
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
REF_FRAMES = os.path.join(ROOT, "Docs", "视频逐帧", "f1083870", "frames")

TARGET_H = 432           # 双方统一行高（参考原生 960x432）
GRID_HZ = 30.0           # 亮度序列统一重采样网格
ROIS = {"sky": (0.03, 0.42), "water": (0.48, 0.95), "full": (0.0, 1.0)}
THR = {"E": (0.5, 2.0), "V": (0.7, 1.4), "F": (0.6, 1.67), "spike": 3.0}


def imread_cn(path):
    """中文路径安全读图（AGENTS 铁律 2）。"""
    data = np.fromfile(path, np.uint8)
    img = cv2.imdecode(data, cv2.IMREAD_GRAYSCALE)
    if img is None:
        raise IOError("read fail: " + path)
    return img


def imwrite_cn(path, img):
    ext = os.path.splitext(path)[1] or ".jpg"
    ok, buf = cv2.imencode(ext, img)
    if not ok:
        raise IOError("encode fail: " + path)
    buf.tofile(path)


def load_ours(d):
    pat = re.compile(r"f(\d{6})_t(\d+(?:\.\d+)?)\.jpg$")
    items = []
    for p in glob.glob(os.path.join(d, "*.jpg")):
        m = pat.match(os.path.basename(p))
        if m:
            items.append((int(m.group(1)), float(m.group(2)), p))
    items.sort()
    ts = np.array([t for _, t, _ in items], np.float64)
    ts -= ts[0]                                   # 从 0 起
    imgs = []
    for _, _, p in items:
        g = imread_cn(p)
        g = cv2.resize(g, (TARGET_H * g.shape[1] // g.shape[0], TARGET_H),
                       interpolation=cv2.INTER_AREA)
        imgs.append(g.astype(np.float32))
    return ts, imgs


def load_ref(f0, f1):
    ts, imgs, fr = [], [], []
    for f in range(f0, f1 + 1):
        p = os.path.join(REF_FRAMES, "f%06d.jpg" % f)
        if not os.path.exists(p):
            continue
        g = imread_cn(p)
        g = cv2.resize(g, (TARGET_H * g.shape[1] // g.shape[0], TARGET_H),
                       interpolation=cv2.INTER_AREA)
        imgs.append(g.astype(np.float32))
        ts.append((f - f0) / 30.03)
        fr.append(f)
    return np.array(ts, np.float64), imgs, fr


def roi_motion_energy(imgs, ts, y0, y1):
    """像素级运动能量：相邻帧 ROI 平均|像素差| ÷ 帧间隔 → 每秒均值。
    （|Δmean| 口径会漏掉"均值不变但纹理在闪"的运动——碎闪正是要测的东西；
    参考侧 analyze_frames 的 diff 列就是像素差，同族口径才可比。）"""
    h = imgs[0].shape[0]
    vals = []
    for i in range(len(imgs) - 1):
        a = imgs[i][int(h * y0):int(h * y1), :]
        b = imgs[i + 1][int(h * y0):int(h * y1), :]
        dt = float(ts[i + 1] - ts[i])
        if dt <= 1e-6:
            continue
        vals.append(float(np.abs(a - b).mean()) / dt)
    return float(np.mean(vals)) if vals else 0.0


def roi_luma_series(imgs, y0, y1):
    """逐帧 ROI 平均亮度序列。"""
    out = []
    for g in imgs:
        h = g.shape[0]
        out.append(float(g[int(h * y0):int(h * y1), :].mean()))
    return np.array(out, np.float64)


def to_grid(ts, v, dur):
    """按时间重采样到均匀 30Hz 网格（我方帧间隔不均，必须归一）。"""
    gx = np.arange(0.0, dur, 1.0 / GRID_HZ)
    return np.interp(gx, ts, v)


def flicker_hz(x):
    t = np.arange(len(x), dtype=np.float64)
    k = np.polyfit(t, x, 1)
    d = x - np.polyval(k, t)
    w = d * np.hanning(len(d))
    mag = np.abs(np.fft.rfft(w))
    fr = np.fft.rfftfreq(len(d), d=1.0 / GRID_HZ)
    band = (fr >= 0.5) & (fr <= 8.0)
    if not band.any() or mag[band].max() < 1e-9:
        return 0.0
    return float(fr[band][np.argmax(mag[band])])


CUTS = {32, 4427, 4481, 4550, 4764, 5308}   # analyze_frames 检出的切镜帧（参考侧跨切镜的对必须剔除）


def hotspot_speed(imgs, ts, y0, y1, ref_frames=None, k_hot=3):
    """D3 特征速度：时间差分热点 × 模板匹配，多热点多对帧取中位（%画宽/秒）。
    全幅相位相关会被静景淹没（自证实测 1s 后相关峰 0.8 仍在原点——山/天空
    渐变占了大头，云的贡献推不动 argmax）；改为先找"动了的块"再跟它。
    ref_frames 非空时做切镜剔除。返回 (中位速度%W/s, 成功跟踪数/总尝试)。"""
    h = imgs[0].shape[0]
    ra0, rb0 = int(h * y0), int(h * y1)
    W = imgs[0].shape[1]
    speeds = []
    tries = 0
    i = 0
    while i < len(imgs) - 2:
        j = i + 1
        while j < len(imgs) and ts[j] - ts[i] < 0.85:
            j += 1
        if j >= len(imgs) or ts[j] - ts[i] > 1.4:
            i += 1
            continue
        # 参考侧：跨切镜的配对作废
        if ref_frames is not None:
            span = set(range(ref_frames[i], ref_frames[j] + 1))
            if span & CUTS:
                i = j
                continue
        a = imgs[i][ra0:rb0, :]
        b = imgs[j][ra0:rb0, :]
        dt = float(ts[j] - ts[i])
        d = np.abs(b - a)
        # 扫描块均值差分，取前 k_hot 个相互错开的热点
        cand = []
        for yy in range(6, a.shape[0] - 78, 6):
            for xx in range(6, a.shape[1] - 78, 6):
                cand.append((float(d[yy:yy + 72, xx:xx + 72].mean()), yy, xx))
        cand.sort(reverse=True)
        picked = []
        for v, yy, xx in cand:
            if v < 1.0:
                break
            if all(abs(yy - py) > 60 or abs(xx - px) > 60 for py, px in picked):
                picked.append((yy, xx))
            if len(picked) >= k_hot:
                break
        for yy, xx in picked:
            tries += 1
            tpl = a[yy:yy + 72, xx:xx + 72].astype(np.uint8)
            y1s, x1s = max(0, yy - 44), max(0, xx - 44)
            y2s = min(b.shape[0], yy + 72 + 44)
            x2s = min(b.shape[1], xx + 72 + 44)
            res = cv2.matchTemplate(b[y1s:y2s, x1s:x2s].astype(np.uint8),
                                    tpl, cv2.TM_CCOEFF_NORMED)
            _, mx, _, loc = cv2.minMaxLoc(res)
            if mx < 0.6:
                continue                      # 形变太大，跟踪不可信
            dx = loc[0] + x1s - xx
            dy = loc[1] + y1s - yy
            speeds.append(abs(dx) / W / dt * 100.0)
        i = max(j, i + 4)
    if not speeds:
        return 0.0, (0, tries)
    return float(np.median(speeds)), (len(speeds), tries)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--ours", required=True, help="SeqCapture 输出目录")
    ap.add_argument("--ref-start", type=int, required=True)
    ap.add_argument("--ref-end", type=int, required=True)
    ap.add_argument("--label", default="cmp")
    ap.add_argument("--out", default=None)
    args = ap.parse_args()

    o_ts, o_imgs = load_ours(args.ours)
    r_ts, r_imgs, r_fr = load_ref(args.ref_start, args.ref_end)
    if len(o_imgs) < 40 or len(r_imgs) < 40:
        print("!! 帧数不足（ours=%d ref=%d，需 ≥40）" % (len(o_imgs), len(r_imgs)))
        return 2

    dur = min(float(o_ts[-1]), float(r_ts[-1]))
    verdict = {"label": args.label, "ours_dir": args.ours,
               "ref": [args.ref_start, args.ref_end],
               "ours_frames": len(o_imgs), "ref_frames": len(r_imgs),
               "compare_sec": round(dur, 2), "rois": {}}

    for roi, (y0, y1) in ROIS.items():
        ov = to_grid(o_ts, roi_luma_series(o_imgs, y0, y1), dur)
        rv = to_grid(r_ts, roi_luma_series(r_imgs, y0, y1), dur)
        # D1 用像素级运动能量（每秒口径，帧率无关）
        o_e = roi_motion_energy(o_imgs, o_ts, y0, y1)
        r_e = roi_motion_energy(r_imgs, r_ts, y0, y1)
        rho_e = float(o_e / max(r_e, 1e-9))
        od = np.abs(np.diff(ov))
        rd = np.abs(np.diff(rv))
        # D2 校准注记：90 帧窗口的自然重尾使 max/σ 本底就有 3~4（自证实测
        # 参考对参考 3.3~4.1），"绝对 <3"连参考都过不了 ⇒ 改为"不比参考
        # 自身更尖"（<max(3, 1.2×ref)）——孤立爆闪仍会现形
        o_spk = float(np.max(np.abs(od - np.median(od))) /
                      max(np.std(od), 1e-9))
        r_spk = float(np.max(np.abs(rd - np.median(rd))) /
                      max(np.std(rd), 1e-9))
        o_f, r_f = flicker_hz(ov), flicker_hz(rv)
        rho_f = (o_f / r_f) if (o_f > 0 and r_f > 0) else None
        o_v, (o_hits, o_tries) = hotspot_speed(o_imgs, o_ts, y0, y1)
        r_v, (r_hits, r_tries) = hotspot_speed(r_imgs, r_ts, y0, y1,
                                               ref_frames=r_fr)
        rho_v = (o_v / r_v) if r_v > 1e-6 else None

        row = {
            "D1_ours": round(o_e, 1), "D1_ref": round(r_e, 1),
            "rho_E": round(rho_e, 3),
            "pass_E": THR["E"][0] <= rho_e <= THR["E"][1],
            "D2_spike_ours": round(o_spk, 2), "D2_spike_ref": round(r_spk, 2),
            "pass_D2": o_spk < max(THR["spike"], 1.2 * r_spk),
            "D3_v_ours": round(o_v, 2), "D3_v_ref": round(r_v, 2),
            "rho_v": round(rho_v, 3) if rho_v else None,
            "pass_D3": (rho_v is not None and THR["V"][0] <= rho_v <= THR["V"][1]),
            "D3_hits": [o_hits, r_hits],
            "D4_f_ours": round(o_f, 2), "D4_f_ref": round(r_f, 2),
            "rho_f": round(rho_f, 3) if rho_f else None,
            "pass_D4": (rho_f is not None and THR["F"][0] <= rho_f <= THR["F"][1]),
        }
        verdict["rois"][roi] = row
        print(f"[{roi:>5}] E: {row['D1_ours']}/{row['D1_ref']} ρ={row['rho_E']}"
              f" {'PASS' if row['pass_E'] else 'FAIL'} | "
              f"spk: {row['D2_spike_ours']}/{row['D2_spike_ref']}"
              f" {'PASS' if row['pass_D2'] else 'FAIL'} | "
              f"v: {row['D3_v_ours']}/{row['D3_v_ref']} %W/s"
              f" ρ={row['rho_v']} {'PASS' if row['pass_D3'] else 'FAIL'} | "
              f"f: {row['D4_f_ours']}/{row['D4_f_ref']}Hz ρ={row['rho_f']}"
              f" {'PASS' if row['pass_D4'] else 'FAIL'}")

    out = args.out or os.path.join(os.path.dirname(args.ours.rstrip("/\\")),
                                   "cmp_" + args.label)
    os.makedirs(out, exist_ok=True)
    with open(os.path.join(out, "verdict.json"), "w", encoding="utf-8") as f:
        json.dump(verdict, f, ensure_ascii=False, indent=1)

    # ── 曲线叠图（可选，matplotlib 缺失不致命）──
    try:
        import matplotlib
        matplotlib.use("Agg")
        import matplotlib.pyplot as plt
        fig, axes = plt.subplots(2, 2, figsize=(12, 6))
        gx = np.arange(0.0, dur, 1.0 / GRID_HZ)
        for col, roi in enumerate(["sky", "water"]):
            y0, y1 = ROIS[roi]
            ov = to_grid(o_ts, roi_luma_series(o_imgs, y0, y1), dur)
            rv = to_grid(r_ts, roi_luma_series(r_imgs, y0, y1), dur)
            axes[0][col].plot(gx[:len(ov)], ov, label="ours", lw=0.8)
            axes[0][col].plot(gx[:len(rv)], rv, label="ref", lw=0.8)
            axes[0][col].set_title(f"{roi} luma")
            axes[0][col].legend()
            axes[1][col].plot(np.abs(np.diff(ov)), lw=0.8)
            axes[1][col].plot(np.abs(np.diff(rv)), lw=0.8)
            axes[1][col].set_title(f"{roi} |Δluma|")
        fig.suptitle(args.label)
        fig.tight_layout()
        fig.savefig(os.path.join(out, "curves.png"), dpi=110)
    except Exception as e:
        print("(no plots: %s)" % e)

    # ── 并排联络表：上排我方 / 下排参考，同列同时刻近似 ──
    step = max(1, min(len(o_imgs), len(r_imgs)) // 6)
    rows = []
    for k in range(0, min(len(o_imgs), len(r_imgs)), step):
        a = cv2.resize(o_imgs[k].astype(np.uint8), (768, TARGET_H))
        b = cv2.resize(r_imgs[k].astype(np.uint8), (768, TARGET_H))
        sep = np.full((TARGET_H, 6), 255, np.uint8)
        rows.append(np.hstack([a, sep, b]))
    sheet = np.vstack(rows)
    gap = np.full((6, sheet.shape[1]), 255, np.uint8)
    sheet = np.vstack([rows[0]] + [np.vstack([gap, r]) for r in rows[1:]])
    imwrite_cn(os.path.join(out, "sheet.jpg"), sheet)
    print("-> " + out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
