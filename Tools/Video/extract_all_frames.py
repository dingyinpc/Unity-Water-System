# -*- coding: utf-8 -*-
"""全帧抽取（逐帧分析第 1 步）—— 把视频的【每一帧】导出为图片，不抽样。
用法:
  python Tools/Video/extract_all_frames.py <视频> --out <输出目录>
      [--format jpg|png] [--quality 95] [--start 秒] [--end 秒] [--resize WxH]
产出:
  <输出目录>/frames/f000001.jpg ...（帧号 6 位补零）
  <输出目录>/index.csv   （frame_idx,time_sec,file）
说明:
  默认 JPG q95（体积小、色彩统计差异可忽略）；需要像素级无损时 --format png，
  或只对某区间重抽 png：--format png --start 10.0 --end 12.0
"""
import sys
import os
import csv
import time
import argparse
import cv2

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _videoutil import ensure_ascii_path, imwrite_u  # noqa: E402


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("video")
    ap.add_argument("--out", required=True)
    ap.add_argument("--format", choices=["jpg", "png"], default="jpg")
    ap.add_argument("--quality", type=int, default=95, help="JPG 质量（默认 95）")
    ap.add_argument("--start", type=float, default=0.0, help="起始秒（默认 0）")
    ap.add_argument("--end", type=float, default=None, help="结束秒（默认片尾）")
    ap.add_argument("--resize", default=None, help="缩放 WxH（默认原尺寸）")
    a = ap.parse_args()

    src, tmp = ensure_ascii_path(a.video, tag="input")
    cap = cv2.VideoCapture(src)
    if not cap.isOpened():
        print(f"ERROR: 无法打开 {a.video}")
        sys.exit(2)
    fps = cap.get(cv2.CAP_PROP_FPS) or 30.0
    if a.start > 0:
        cap.set(cv2.CAP_PROP_POS_MSEC, a.start * 1000.0)
    base = int(round(cap.get(cv2.CAP_PROP_POS_FRAMES) or 0))
    if a.start > 0 and base == 0:
        base = int(round(a.start * fps))  # 部分解码器不支持 POS_FRAMES，退回按时长估算

    fdir = os.path.join(a.out, "frames")
    os.makedirs(fdir, exist_ok=True)
    params = [int(cv2.IMWRITE_JPEG_QUALITY), a.quality] if a.format == "jpg" else []
    ext = "." + a.format
    rw = rh = None
    if a.resize:
        rw, rh = (int(x) for x in a.resize.lower().split("x"))

    t0, n = time.time(), 0
    with open(os.path.join(a.out, "index.csv"), "w", newline="", encoding="utf-8") as f:
        wr = csv.writer(f)
        wr.writerow(["frame_idx", "time_sec", "file"])
        while True:
            ok, frame = cap.read()
            if not ok:
                break
            fi = base + n
            t = fi / fps
            if a.end is not None and t > a.end + 1e-9:
                break
            if rw:
                frame = cv2.resize(frame, (rw, rh), interpolation=cv2.INTER_AREA)
            name = f"f{fi:06d}{ext}"
            if not imwrite_u(os.path.join(fdir, name), frame, params):
                print(f"WARN: 写图失败 {name}")
            wr.writerow([fi, f"{t:.4f}", f"frames/{name}"])
            n += 1
            if n % 200 == 0:
                print(f"... {n} 帧（{time.time() - t0:.0f}s）")
    cap.release()

    dt = time.time() - t0
    rate = n / dt if dt else 0
    print(f"DONE: {n} 帧 → {fdir}")
    print(f"      {dt:.1f}s / {rate:.0f} 帧每秒；index.csv 已写入 {a.out}")
    if tmp:
        print(f"（注：源路径含非 ASCII，解码走临时副本 {tmp}）")


if __name__ == "__main__":
    main()
