# -*- coding: utf-8 -*-
"""逐帧量化分析（逐帧分析第 2 步）—— 对【每一帧】计算指标，产出时间序列。
用法:
  python Tools/Video/analyze_frames.py <抽取输出目录>
      [--boxes "名:x0,y0,x1,y1;名2:..."]  百分比坐标框（0-100），每帧统计框内均值 RGB
      [--hue]                            统计色相分桶占比（白/暗/暖/黄/绿/青/蓝/紫）
      [--cut-thresh 12]                  切镜判定阈值（灰度平均绝对差 0-255）
      [--fps 30]                         index.csv 缺时间时的兜底帧率
输入:
  <抽取输出目录> 为 extract_all_frames.py 的 --out（含 frames/ 与 index.csv）
产出（<抽取输出目录>/analysis/）:
  metrics.csv   每帧一行的全部指标
  plots.png     时间序列图（亮度 / 帧间差+切镜 / 全局RGB / 亮部占比 / 各框RGB / 色相占比）
  cuts.json     切镜列表（帧号 / 时间 / 差异值）
  summary.txt   摘要（时长、亮度统计、Top10 差异帧、切镜数）
"""
import sys
import os
import csv
import json
import argparse
import numpy as np
import cv2

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _videoutil import load_index, imread_u, parse_boxes  # noqa: E402

DIFF_W = 480          # 帧间差在缩到 480 宽的灰度图上算（快且抗噪）
BRIGHT_T = 230        # "亮部"阈值（luma > 230）


def hue_fractions(hsv):
    """OpenCV HSV（H 0-179）：返回各分桶占比（%），各自独立统计。"""
    H = hsv[..., 0]
    S = hsv[..., 1]
    V = hsv[..., 2]
    tot = float(H.size)
    white = (S < 30) & (V >= 190)
    dark = (V < 40)
    base = ~white & ~dark
    masks = {
        "white": white,
        "dark": dark,
        "warm": base & ((H <= 22) | (H >= 162)),
        "yellow": base & (H >= 23) & (H <= 35),
        "green": base & (H >= 36) & (H <= 85),
        "cyan": base & (H >= 86) & (H <= 105),
        "blue": base & (H >= 106) & (H <= 130),
        "purple": base & (H >= 131) & (H <= 161),
    }
    return {k: float(m.sum()) / tot * 100.0 for k, m in masks.items()}


def load_font(size):
    from PIL import ImageFont
    for p in (r"C:\Windows\Fonts\msyh.ttc", r"C:\Windows\Fonts\msyhl.ttc",
              r"C:\Windows\Fonts\simhei.ttf", r"C:\Windows\Fonts\arial.ttf"):
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, size)
            except Exception:
                pass
    return ImageFont.load_default()


def draw_panel(d, box, times, series, ymin, ymax, title, font_t, font_s, fixed_y=False):
    """series: [(np.array, (r,g,b), label)]；NaN 处断线。"""
    from PIL import ImageColor
    x0, y0, x1, y1 = box
    d.rectangle([x0, y0, x1, y1], fill=(250, 250, 252), outline=(110, 110, 120))
    d.text((x0 + 8, y0 + 5), title, fill=(15, 15, 25), font=font_t)
    tmin, tmax = float(times[0]), float(times[-1])
    if tmax <= tmin:
        tmax = tmin + 1e-6
    allv = [v[np.isfinite(v)] for v, _, _ in series]
    if not fixed_y and allv:
        vmax = max((float(v.max()) for v in allv if v.size), default=0.0)
        vmin = min((float(v.min()) for v in allv if v.size), default=0.0)
        ymax = max(vmax * 1.08, ymin + 1e-6)
        ymin = min(vmin, ymax - 1e-6)
    rng = max(ymax - ymin, 1e-9)

    def ty(v):
        return y1 - 8 - (v - ymin) / rng * (y1 - y0 - 30)

    def tx(t):
        return x0 + 56 + (t - tmin) / (tmax - tmin) * (x1 - x0 - 66)

    # 横网格 + y 刻度
    for i in range(5):
        v = ymin + (ymax - ymin) * i / 4.0
        yy = ty(v)
        d.line([x0 + 52, yy, x1 - 6, yy], fill=(226, 226, 232))
        lab = f"{v:.0f}" if abs(v) >= 1 else f"{v:.2f}"
        d.text((x0 + 6, yy - 7), lab, fill=(95, 95, 105), font=font_s)
    # x 刻度（秒）
    for i in range(7):
        t = tmin + (tmax - tmin) * i / 6.0
        xx = tx(t)
        d.line([xx, y0 + 26, xx, y1 - 8], fill=(238, 238, 242))
        d.text((xx - 14, y1 - 8), f"{t:.1f}s", fill=(95, 95, 105), font=font_s)
    # 曲线 + 图例
    lx = x1 - 10
    for v, color, label in series:
        pts = [(tx(t), ty(float(vv))) for t, vv in zip(times, v) if np.isfinite(vv)]
        if len(pts) >= 2:
            d.line(pts, fill=color, width=2)
        elif len(pts) == 1:
            px, py = pts[0]
            d.ellipse([px - 2, py - 2, px + 2, py + 2], fill=color)
        tw = d.textlength(label, font=font_s)
        d.line([lx - tw - 22, y0 + 10, lx - tw - 8, y0 + 10], fill=color, width=3)
        d.text((lx - tw - 4, y0 + 4), label, fill=(40, 40, 50), font=font_s)
        lx -= tw + 34


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("out_dir")
    ap.add_argument("--boxes", default=None, help="'名:x0,y0,x1,y1;...' 百分比坐标")
    ap.add_argument("--hue", action="store_true")
    ap.add_argument("--cut-thresh", type=float, default=12.0)
    ap.add_argument("--fps", type=float, default=30.0)
    a = ap.parse_args()

    items = load_index(a.out_dir)
    if not items:
        print("ERROR: index.csv 为空或缺失——先跑 extract_all_frames.py")
        sys.exit(1)
    boxes = parse_boxes(a.boxes)
    fps = 0.0
    if len(items) >= 2 and items[1][1] > items[0][1]:
        fps = (items[1][1] - items[0][1]) and 1.0 / (items[1][1] - items[0][1])
    if not fps or fps < 1:
        fps = a.fps
    print(f"输入 {len(items)} 帧；fps≈{fps:.3f}；框 {len(boxes)} 个；hue={a.hue}")

    os.makedirs(os.path.join(a.out_dir, "analysis"), exist_ok=True)
    adir = os.path.join(a.out_dir, "analysis")

    rows = []
    cuts = []
    top_diffs = []
    last_cut_f = -10 ** 9
    prev_small = None
    W = H = None

    import time
    t0 = time.time()
    for k, (fi, t, path) in enumerate(items):
        img = imread_u(path)
        if img is None:
            print(f"WARN: 读图失败 f{fi}")
            continue
        if W is None:
            H, W = img.shape[:2]
        gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
        if gray.shape[1] > DIFF_W:
            sc = DIFF_W / gray.shape[1]
            small = cv2.resize(gray, (DIFF_W, max(1, int(gray.shape[0] * sc))), interpolation=cv2.INTER_AREA)
        else:
            small = gray
        diff = float("nan") if prev_small is None or prev_small.shape != small.shape else \
            float(np.abs(small.astype(np.float32) - prev_small.astype(np.float32)).mean())
        prev_small = small

        luma = float(gray.mean())
        b, g, r = (float(v) for v in img.reshape(-1, 3).mean(axis=0))
        bright = float((gray > BRIGHT_T).mean() * 100.0)

        row = {"frame": fi, "time": t, "luma": luma, "r": r, "g": g, "b": b,
               "diff": diff, "bright": bright}
        for (name, x0, y0, x1, y1) in boxes:
            xs, xe = int(x0 / 100 * W), int(x1 / 100 * W)
            ys, ye = int(y0 / 100 * H), int(y1 / 100 * H)
            crop = img[max(0, ys):max(1, ye), max(0, xs):max(1, xe)]
            cb, cg, cr = (float(v) for v in crop.reshape(-1, 3).mean(axis=0))
            row[f"box_{name}_r"], row[f"box_{name}_g"], row[f"box_{name}_b"] = cr, cg, cb
        if a.hue:
            hsv = cv2.cvtColor(img, cv2.COLOR_BGR2HSV)
            for hn, hv in hue_fractions(hsv).items():
                row[f"hue_{hn}"] = hv
        rows.append(row)

        if np.isfinite(diff):
            top_diffs.append((diff, fi, t))
            if diff > a.cut_thresh and (fi - last_cut_f) >= fps * 0.5:
                cuts.append({"frame": fi, "time": round(t, 3), "diff": round(diff, 2)})
                last_cut_f = fi
        if (k + 1) % 200 == 0:
            print(f"... {k + 1}/{len(items)} 帧（{time.time() - t0:.0f}s）")

    # ---- metrics.csv ----
    cols = list(rows[0].keys())
    with open(os.path.join(adir, "metrics.csv"), "w", newline="", encoding="utf-8") as f:
        wr = csv.DictWriter(f, fieldnames=cols)
        wr.writeheader()
        for row in rows:
            wr.writerow({k: (f"{v:.4f}" if isinstance(v, float) else v) for k, v in row.items()})

    # ---- cuts.json ----
    top_diffs.sort(reverse=True)
    with open(os.path.join(adir, "cuts.json"), "w", encoding="utf-8") as f:
        json.dump({"fps": round(fps, 3), "threshold": a.cut_thresh, "cuts": cuts}, f,
                  ensure_ascii=False, indent=2)

    # ---- plots.png ----
    try:
        from PIL import Image, ImageDraw
        times = np.array([r["time"] for r in rows])
        font_t, font_s = load_font(20), load_font(14)
        panels = []
        panels.append((f"亮度 luma（0-255）", [(np.array([r["luma"] for r in rows]), (30, 90, 200), "luma")], True))
        dmax = max((r["diff"] for r in rows if np.isfinite(r["diff"])), default=1.0)
        panels.append((f"帧间差 diff（阈值 {a.cut_thresh:g}；红线=切镜）",
                       [(np.array([r["diff"] for r in rows]), (200, 60, 30), "diff")], False))
        panels.append(("全局 RGB", [(np.array([r["r"] for r in rows]), (200, 40, 40), "R"),
                                    (np.array([r["g"] for r in rows]), (40, 160, 40), "G"),
                                    (np.array([r["b"] for r in rows]), (40, 60, 200), "B")], True))
        panels.append(("亮部占比 bright>230（%）", [(np.array([r["bright"] for r in rows]), (160, 130, 20), "bright%")], False))
        for (name, *_rest) in boxes:
            panels.append((f"框【{name}】RGB", [(np.array([r[f"box_{name}_r"] for r in rows]), (200, 40, 40), "R"),
                                               (np.array([r[f"box_{name}_g"] for r in rows]), (40, 160, 40), "G"),
                                               (np.array([r[f"box_{name}_b"] for r in rows]), (40, 60, 200), "B")], True))
        if a.hue:
            hcols = {"white": (220, 220, 220), "dark": (60, 60, 60), "warm": (230, 120, 40),
                     "yellow": (220, 200, 30), "green": (60, 170, 60), "cyan": (40, 190, 190),
                     "blue": (40, 80, 220), "purple": (150, 60, 200)}
            panels.append(("色相分桶占比（%）",
                           [(np.array([r[f"hue_{k2}"] for r in rows]), hcols[k2], k2) for k2 in hcols], False))
        PW, PH, GAP, TOP = 1600, 210, 16, 34
        img = Image.new("RGB", (PW, TOP + len(panels) * (PH + GAP)), (255, 255, 255))
        d = ImageDraw.Draw(img)
        y0 = TOP
        for title, series, fixed255 in panels:
            draw_panel(d, (20, y0, PW - 20, y0 + PH), times, series,
                       0.0, 255.0 if fixed255 else max(dmax * 1.1, 30.0), title, font_t, font_s,
                       fixed_y=fixed255)
            if "帧间差" in title:
                for cd in cuts:
                    x0, y1 = 20, y0 + PH
                    tmin, tmax = float(times[0]), float(times[-1])
                    xx = x0 + 56 + (cd["time"] - tmin) / max(tmax - tmin, 1e-6) * (PW - 40 - 66)
                    d.line([xx, y0 + 26, xx, y1 - 8], fill=(220, 40, 40))
            y0 += PH + GAP
        img.save(os.path.join(adir, "plots.png"))
        print("plots.png 已生成")
    except Exception as e:
        print(f"WARN: 画图失败（CSV 不受影响）: {e}")

    # ---- summary.txt ----
    lumas = np.array([r["luma"] for r in rows])
    imax = int(np.argmax(lumas))
    imin = int(np.argmin(lumas))
    lines = [
        f"帧数: {len(rows)} | 时长: {rows[-1]['time']:.2f}s | fps≈{fps:.3f} | 分辨率: {W}x{H}",
        f"亮度 luma: min {lumas.min():.1f} (f{rows[imin]['frame']}) / mean {lumas.mean():.1f} / max {lumas.max():.1f} (f{rows[imax]['frame']})",
        f"切镜数: {len(cuts)}（阈值 {a.cut_thresh:g}，间隔≥0.5s）",
        "Top10 帧间差异（帧号 @ 时间 s：值）:",
    ]
    for dv, df, dt in top_diffs[:10]:
        lines.append(f"  f{df} @ {dt:.2f}s : {dv:.1f}")
    if cuts:
        lines.append("切镜列表:")
        for cd in cuts:
            lines.append(f"  f{cd['frame']} @ {cd['time']:.2f}s : {cd['diff']:.1f}")
    with open(os.path.join(adir, "summary.txt"), "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    print("\n".join(lines[:4]))
    print(f"DONE → {adir}")


if __name__ == "__main__":
    main()
