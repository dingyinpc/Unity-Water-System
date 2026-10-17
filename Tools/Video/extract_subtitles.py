# -*- coding: utf-8 -*-
"""字幕卡提取 v3：白字掩码签名分段 + 段内时间中值出卡 + 相似合并。
用法: python Tools/Video/extract_subtitles.py <抽取输出目录>
      [--thr 0.08] [--per-strip 10]
产出: <抽取输出目录>/subtitles/card_XXXX_fYYYYYY_ZZ.Zs.png （白字黑底卡，1.3x，时间中值去噪）
      <抽取输出目录>/subtitles/strip_XX.jpg （总览条，供转写）
      <抽取输出目录>/subtitles/segments.csv （段号/起止帧/起止秒/代表文件）
原理: 字幕为白色文字+黑描边、屏幕空间固定（不随镜头移动）。
     ① 逐帧白掩码密度签名(160x12) 分段（THR 超阈值=新段）；
     ② 每段取 16 帧做逐像素时间中值 → 流动背景/水花被抹掉、静止文字保留；
     ③ 中值掩码<1% = 空白段跳过；相邻段掩码相似(MAE<0.03)且间隔<1.5s = 同文字合并。
"""
import sys
import os
import csv
import argparse
import numpy as np
import cv2

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _videoutil import load_index, imread_u, imwrite_u  # noqa: E402

UPS = 1.3


def white_mask(band):
    b, g, r = band[..., 0], band[..., 1], band[..., 2]
    return (np.minimum(np.minimum(b, g), r) > 150)


def clean_mask(m, min_area=3, max_area=700):
    """连通域过滤：保留笔画级小块（文字），剔除大团块（亮背景）与散点（噪点）。"""
    n, lab, stats, _ = cv2.connectedComponentsWithStats(m.astype(np.uint8), connectivity=8)
    out = np.zeros_like(m, dtype=bool)
    for k in range(1, n):
        ar = stats[k, cv2.CC_STAT_AREA]
        if min_area <= ar <= max_area:
            out |= (lab == k)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("out_dir")
    ap.add_argument("--y0", type=int, default=330)
    ap.add_argument("--y1", type=int, default=432)
    ap.add_argument("--x0", type=int, default=130)
    ap.add_argument("--x1", type=int, default=910)
    ap.add_argument("--thr", type=float, default=0.08)
    ap.add_argument("--min-dur", type=int, default=4)
    ap.add_argument("--per-strip", type=int, default=10)
    ap.add_argument("--samples", type=int, default=16)
    a = ap.parse_args()

    items = load_index(a.out_dir)
    if not items:
        print("ERROR: index.csv 为空——先跑 extract_all_frames.py")
        sys.exit(1)
    fps = 30.0
    if len(items) >= 2 and items[1][1] > items[0][1]:
        fps = 1.0 / (items[1][1] - items[0][1])
    path_of = {fi: p for (fi, t, p) in items}
    fps30 = fps

    print(f"扫描 {len(items)} 帧；字幕区 x[{a.x0}:{a.x1}] y[{a.y0}:{a.y1}]；THR={a.thr}；fps≈{fps:.2f}")

    # ---------- ① 逐帧签名分段 ----------
    segs = []
    cur = None
    cur_sig = None
    for (fi, t, p) in items:
        img = imread_u(p)
        if img is None:
            continue
        band = img[a.y0:a.y1, a.x0:a.x1]
        sig = cv2.resize(white_mask(band).astype(np.float32), (160, 12), interpolation=cv2.INTER_AREA)
        if cur is None:
            cur = [fi, fi]
            cur_sig = sig
            continue
        d = float(np.abs(sig - cur_sig).mean())
        if d > a.thr:
            if cur[1] - cur[0] >= a.min_dur:
                segs.append((cur[0], cur[1]))
            cur = [fi, fi]
            cur_sig = sig
        else:
            cur[1] = fi
    if cur is not None and cur[1] - cur[0] >= a.min_dur:
        segs.append((cur[0], cur[1]))
    print(f"① 逐帧分段: {len(segs)} 段 → 时间中值出卡…")

    # ---------- ② 段内时间中值 → 卡 ----------
    cards = []   # dict: f0,f1,mask,small,img
    for (f0, f1) in segs:
        dur = f1 - f0 + 1
        s0 = f0 + max(2, dur // 5)
        s1 = f1
        if s1 < s0:
            s0 = s1 = f0
        idxs = np.linspace(s0, s1, min(a.samples, s1 - s0 + 1)).astype(int)
        stack = []
        for k in idxs:
            im2 = imread_u(path_of.get(int(k), ""))
            if im2 is not None:
                stack.append(im2[a.y0:a.y1, a.x0:a.x1])
        if not stack:
            continue
        med = np.median(np.stack(stack, 0), axis=0).astype(np.uint8)
        m = clean_mask(white_mask(med))
        density = float(m.mean()) * 100.0
        if density < 0.5:                       # 空白段
            continue
        small = cv2.resize(m.astype(np.float32), (160, 12), interpolation=cv2.INTER_AREA)
        cards.append({"f0": f0, "f1": f1, "mask": m, "small": small, "med": med})

    # ---------- ③ 相似合并 ----------
    merged = []
    for c in cards:
        if merged and (c["f0"] - merged[-1]["f1"]) <= int(fps * 1.5) and \
                float(np.abs(c["small"] - merged[-1]["small"]).mean()) < 0.03:
            merged[-1]["f1"] = c["f1"]
            continue
        merged.append(dict(c))
    print(f"② 时间中值出卡 {len(cards)} 张 → ③ 相似合并后 {len(merged)} 张")

    # ---------- 保存 ----------
    sdir = os.path.join(a.out_dir, "subtitles")
    os.makedirs(sdir, exist_ok=True)
    saved = []
    with open(os.path.join(sdir, "segments.csv"), "w", newline="", encoding="utf-8") as f:
        wr = csv.writer(f)
        wr.writerow(["seg", "f_start", "f_end", "t_start", "t_end", "rep_file", "density%"])
        for i, c in enumerate(merged, 1):
            rf = c["f0"] + max(3, (c["f1"] - c["f0"]) // 3)
            rf = min(rf, c["f1"])
            big = np.where(c["mask"], 255, 0).astype(np.uint8)
            big = cv2.resize(big, None, fx=UPS, fy=UPS, interpolation=cv2.INTER_CUBIC)
            big = cv2.cvtColor(big, cv2.COLOR_GRAY2BGR)
            name = f"card_{i:04d}_f{c['f0']:06d}_{c['f0']/fps30:.1f}s.png"
            imwrite_u(os.path.join(sdir, name), big)
            wr.writerow([i, c["f0"], c["f1"], f"{c['f0']/fps30:.2f}", f"{c['f1']/fps30:.2f}",
                         name, f"{float(c['mask'].mean())*100:.2f}"])
            saved.append((i, c["f0"], c["f1"], big))

    strip_n = 0
    for s in range(0, len(saved), a.per_strip):
        chunk = saved[s:s + a.per_strip]
        w = chunk[0][3].shape[1]
        bar = 26
        H = len(chunk) * (chunk[0][3].shape[0] + bar + 4) + 8
        canvas = np.full((H, w, 3), 25, np.uint8)
        y = 6
        for (i, f0, f1, big) in chunk:
            cv2.putText(canvas, f"#{i}  {f0/fps30:.1f}s-{f1/fps30:.1f}s", (8, y + 19),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.62, (0, 220, 255), 1, cv2.LINE_AA)
            y += bar
            canvas[y:y + big.shape[0], 0:big.shape[1]] = big
            y += big.shape[0] + 4
        strip_n += 1
        oname = os.path.join(sdir, f"strip_{strip_n:02d}.jpg")
        imwrite_u(oname, canvas, [int(cv2.IMWRITE_JPEG_QUALITY), 92])
        print(f"  {oname}  （卡 {chunk[0][0]} ~ {chunk[-1][0]}）")
    print("DONE")


if __name__ == "__main__":
    main()
