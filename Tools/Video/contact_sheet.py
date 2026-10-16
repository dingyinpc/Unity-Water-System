# -*- coding: utf-8 -*-
"""逐帧接触表（逐帧分析第 3 步）—— 把帧按网格拼成大图（快速纵览 / 给 AI 看图）。
用法:
  python Tools/Video/contact_sheet.py <抽取输出目录>
      [--step 30] [--cols 6] [--rows 5] [--tile 360] [--start 秒] [--end 秒]
说明:
  --step  每多少帧取 1 帧上表（默认≈每秒 1 帧，按 index.csv 推算）
  --tile  单帧宽度（像素，默认 360）
产出: <抽取输出目录>/sheets/sheet_01.jpg ...
"""
import sys
import os
import argparse
import numpy as np
import cv2

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _videoutil import load_index, imread_u  # noqa: E402


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("out_dir")
    ap.add_argument("--step", type=int, default=0, help="取帧间隔（默认≈1 秒）")
    ap.add_argument("--cols", type=int, default=6)
    ap.add_argument("--rows", type=int, default=5)
    ap.add_argument("--tile", type=int, default=360)
    ap.add_argument("--start", type=float, default=None)
    ap.add_argument("--end", type=float, default=None)
    a = ap.parse_args()

    items = load_index(a.out_dir)
    if not items:
        print("ERROR: index.csv 为空或缺失——先跑 extract_all_frames.py")
        sys.exit(1)
    fps = 30.0
    if len(items) >= 2 and items[1][1] > items[0][1]:
        fps = 1.0 / (items[1][1] - items[0][1])
    step = a.step if a.step > 0 else max(1, int(round(fps)))

    sel = [(fi, t, p) for (fi, t, p) in items
           if (a.start is None or t >= a.start) and (a.end is None or t <= a.end)]
    sel = sel[::step]
    print(f"共 {len(items)} 帧；每 {step} 帧取 1 → {len(sel)} 张上表")

    sdir = os.path.join(a.out_dir, "sheets")
    os.makedirs(sdir, exist_ok=True)

    per_sheet = a.cols * a.rows
    gap = 6
    sheet_i = 0
    for s in range(0, len(sel), per_sheet):
        chunk = sel[s:s + per_sheet]
        first = imread_u(chunk[0][2])
        if first is None:
            print(f"WARN: 读图失败 {chunk[0][2]}")
            continue
        ih = int(a.tile * first.shape[0] / first.shape[1])
        nrows = max(1, (len(chunk) + a.cols - 1) // a.cols)
        W = a.cols * a.tile + (a.cols + 1) * gap
        Hh = nrows * ih + (nrows + 1) * gap
        canvas = np.full((Hh, W, 3), 255, np.uint8)
        for i, (fi, t, p) in enumerate(chunk):
            r, c = divmod(i, a.cols)
            img = imread_u(p)
            if img is None:
                continue
            tile = cv2.resize(img, (a.tile, ih), interpolation=cv2.INTER_AREA)
            y = gap + r * (ih + gap)
            x = gap + c * (a.tile + gap)
            canvas[y:y + ih, x:x + a.tile] = tile
            lab = f"f{fi} {t:.1f}s"
            cv2.putText(canvas, lab, (x + 6, y + ih - 8), cv2.FONT_HERSHEY_SIMPLEX, 0.62,
                        (0, 0, 0), 3, cv2.LINE_AA)
            cv2.putText(canvas, lab, (x + 6, y + ih - 8), cv2.FONT_HERSHEY_SIMPLEX, 0.62,
                        (255, 255, 255), 1, cv2.LINE_AA)
        sheet_i += 1
        name = os.path.join(sdir, f"sheet_{sheet_i:02d}.jpg")
        from _videoutil import imwrite_u
        params = [int(cv2.IMWRITE_JPEG_QUALITY), 90]
        if not imwrite_u(name, canvas, params):
            print(f"WARN: 写图失败 {name}")
        else:
            print(f"  {name}  （f{chunk[0][0]} ~ f{chunk[-1][0]}）")
    print(f"DONE: {sheet_i} 张接触表 → {sdir}")


if __name__ == "__main__":
    main()
