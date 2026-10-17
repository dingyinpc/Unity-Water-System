# -*- coding: utf-8 -*-
"""分段逐帧量测（检测报告专用）—— 对【指定时间区间】做逐帧区域统计。

用法:
  python Tools/Video/segment_report.py <抽取输出目录> --start 56 --end 78 --name P0-1_S9
      [--boxes "名:x0,y0,x1,y1;名2:..."]  百分比坐标框（0-100，逐帧读图算区域量）
  [--no-plot]

输入:
  <抽取输出目录> = extract_all_frames.py 的 --out（含 frames/ 与 index.csv），
  若存在 analysis/metrics.csv 则复用其全片逐帧指标。

产出（<抽取输出目录>/analysis/）:
  seg_<name>.csv    区间内每帧一行：全局 luma/rgb/diff/bright + 各框 R/G/B/白沫占比/区域运动
  seg_<name>.png    区间时间序列图（有 --boxes 时含各框曲线）
  seg_<name>.txt    摘要（帧数/时长/均值/峰值帧/白沫与运动统计 + 结论提示）

约定:
  · 字幕带（烧录字幕）默认排除：底部 y>78% 区域不计入"白沫"判定之外的框；
    框坐标由调用方自行避开字幕带。
  · 白沫判定：区域内 luma>200 的像素占比（%），再叠加 min(B,G,R)>150 的"安全白"占比。
"""
import sys
import os
import csv
import argparse
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _videoutil import load_index, imread_u, parse_boxes  # noqa: E402


def region_metrics(img, box, prev_gray_region):
    """img: BGR；box: (名,x0%,y0%,x1%,y1%)；返回 (r,g,b,white%,safeWhite%,motion)。"""
    import cv2
    H, W = img.shape[:2]
    _, x0, y0, x1, y1 = box
    xs, xe = int(x0 / 100 * W), int(x1 / 100 * W)
    ys, ye = int(y0 / 100 * H), int(y1 / 100 * H)
    crop = img[max(0, ys):max(1, ye), max(0, xs):max(1, xe)]
    b, g, r = (float(v) for v in crop.reshape(-1, 3).mean(axis=0))
    gray = cv2.cvtColor(crop, cv2.COLOR_BGR2GRAY)
    white = float((gray > 200).mean() * 100.0)
    mn = crop.min(axis=2)
    safe = float((mn > 150).mean() * 100.0)
    motion = float("nan") if prev_gray_region is None or prev_gray_region.shape != gray.shape \
        else float(np.abs(gray.astype(np.float32) - prev_gray_region.astype(np.float32)).mean())
    return r, g, b, white, safe, motion, gray


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("out_dir")
    ap.add_argument("--start", type=float, required=True)
    ap.add_argument("--end", type=float, required=True)
    ap.add_argument("--name", required=True)
    ap.add_argument("--boxes", default=None)
    ap.add_argument("--no-plot", action="store_true")
    a = ap.parse_args()

    items = load_index(a.out_dir)
    rng = [(fi, t, p) for (fi, t, p) in items if a.start <= t <= a.end]
    if not rng:
        print("ERROR: 区间内没有帧")
        sys.exit(1)
    boxes = parse_boxes(a.boxes)
    adir = os.path.join(a.out_dir, "analysis")
    os.makedirs(adir, exist_ok=True)
    print(f"{a.name}: 区间 {a.start}-{a.end}s → {len(rng)} 帧；框 {len(boxes)} 个")

    rows = []
    prev_regions = {b[0]: None for b in boxes}
    import time
    t0 = time.time()
    for k, (fi, t, path) in enumerate(rng):
        img = imread_u(path)
        if img is None:
            continue
        import cv2
        gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
        luma = float(gray.mean())
        b, g, r = (float(v) for v in img.reshape(-1, 3).mean(axis=0))
        bright = float((gray > 230).mean() * 100.0)
        row = {"frame": fi, "time": round(t, 4), "luma": round(luma, 3),
               "r": round(r, 3), "g": round(g, 3), "b": round(b, 3), "bright": round(bright, 3)}
        for box in boxes:
            name = box[0]
            rr, gg, bb, white, safe, motion, gray_r = region_metrics(img, box, prev_regions[name])
            prev_regions[name] = gray_r
            row[f"{name}_r"] = round(rr, 2)
            row[f"{name}_g"] = round(gg, 2)
            row[f"{name}_b"] = round(bb, 2)
            row[f"{name}_white"] = round(white, 3)
            row[f"{name}_safe"] = round(safe, 3)
            row[f"{name}_motion"] = round(motion, 3)
        rows.append(row)
        if (k + 1) % 300 == 0:
            print(f"... {k + 1}/{len(rng)} 帧（{time.time() - t0:.0f}s）")

    cols = list(rows[0].keys())
    csv_path = os.path.join(adir, f"seg_{a.name}.csv")
    with open(csv_path, "w", newline="", encoding="utf-8") as f:
        wr = csv.DictWriter(f, fieldnames=cols)
        wr.writeheader()
        wr.writerows(rows)
    print(f"CSV → {csv_path}")

    # ---- 摘要 ----
    arr = {c: np.array([row[c] for row in rows], dtype=float) for c in cols if c not in ("frame",)}
    fr = np.array([row["frame"] for row in rows])
    lines = [f"【{a.name}】区间 {a.start:.1f}-{a.end:.1f}s 帧 {len(rows)} 张（f{fr[0]}–f{fr[-1]}）"]
    lines.append(f"  全局: luma 均 {arr['luma'].mean():.1f} / R {arr['r'].mean():.1f} G {arr['g'].mean():.1f} B {arr['b'].mean():.1f}"
                 f" / 高亮占比 {arr['bright'].mean():.2f}%")
    i = int(np.argmax(arr["luma"]))
    lines.append(f"  最亮帧: f{fr[i]} @ {arr['time'][i]:.2f}s luma {arr['luma'][i]:.1f}")
    for box in boxes:
        n = box[0]
        lines.append(f"  框[{n}] RGB 均 ({arr[n+'_r'].mean():.0f},{arr[n+'_g'].mean():.0f},{arr[n+'_b'].mean():.0f})"
                     f" / 白沫>200 均 {arr[n+'_white'].mean():.2f}% (峰 {arr[n+'_white'].max():.1f}% @ f{fr[int(np.argmax(arr[n+'_white']))]})"
                     f" / 安全白 均 {arr[n+'_safe'].mean():.2f}%"
                     f" / 运动 均 {np.nanmean(arr[n+'_motion']):.2f}")
    txt_path = os.path.join(adir, f"seg_{a.name}.txt")
    with open(txt_path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    print("\n".join(lines))
    print(f"TXT → {txt_path}")

    # ---- 图 ----
    if not a.no_plot and boxes:
        try:
            from PIL import Image, ImageDraw
            from analyze_frames import draw_panel, load_font
            times = arr["time"]
            font_t, font_s = load_font(20), load_font(14)
            series = []
            panels = [("亮度 luma", [(arr["luma"], (30, 90, 200), "luma")], True),
                      ("全局 RGB", [(arr["r"], (200, 40, 40), "R"), (arr["g"], (40, 160, 40), "G"),
                                    (arr["b"], (40, 60, 200), "B")], True)]
            for box in boxes:
                n = box[0]
                panels.append((f"框[{n}] 白沫% (>200)", [(arr[n + "_white"], (40, 40, 40), "white%")], False))
                panels.append((f"框[{n}] 区域运动", [(np.nan_to_num(arr[n + "_motion"]), (200, 90, 30), "motion")], False))
            PW, PH, GAP, TOP = 1600, 200, 14, 34
            img = Image.new("RGB", (PW, TOP + len(panels) * (PH + GAP)), (255, 255, 255))
            d = ImageDraw.Draw(img)
            y = TOP
            for title, ss, fixed in panels:
                draw_panel(d, (20, y, PW - 20, y + PH), times, ss, 0.0, 255.0 if fixed else
                           max(float(np.nanmax(ss[0][0])) * 1.1, 1.0), title, font_t, font_s, fixed_y=fixed)
                y += PH + GAP
            img.save(os.path.join(adir, f"seg_{a.name}.png"))
            print(f"PNG → {os.path.join(adir, f'seg_{a.name}.png')}")
        except Exception as e:
            print(f"WARN: 画图失败: {e}")


if __name__ == "__main__":
    main()
