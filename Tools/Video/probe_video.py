# -*- coding: utf-8 -*-
"""视频元数据探针（逐帧分析第 0 步）。
用法: python Tools/Video/probe_video.py <视频文件>
输出: fps / 总帧数 / 时长 / 分辨率 / 编解码器（JSON）
"""
import sys
import os
import json
import cv2

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _videoutil import ensure_ascii_path  # noqa: E402


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)
    p = sys.argv[1]
    if not os.path.isfile(p):
        print(f"ERROR: 找不到文件 {p}")
        sys.exit(1)

    src, tmp = ensure_ascii_path(p, tag="probe")
    cap = cv2.VideoCapture(src)
    if not cap.isOpened():
        print(f"ERROR: cv2 无法打开 {p}（编解码器不支持？）")
        sys.exit(2)

    fps = cap.get(cv2.CAP_PROP_FPS) or 0.0
    n = int(cap.get(cv2.CAP_PROP_FRAME_COUNT) or 0)
    w = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH) or 0)
    h = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT) or 0)
    fourcc = int(cap.get(cv2.CAP_PROP_FOURCC) or 0)
    cc = "".join(chr((fourcc >> (8 * i)) & 0xFF) for i in range(4)).strip("\x00")
    rot = cap.get(cv2.CAP_PROP_ORIENTATION_META) if hasattr(cv2, "CAP_PROP_ORIENTATION_META") else 0
    cap.release()

    info = {
        "file": os.path.abspath(p),
        "size_mb": round(os.path.getsize(p) / 1e6, 2),
        "fps": round(fps, 3),
        "frames": n,
        "duration_sec": round(n / fps, 2) if fps else None,
        "width": w,
        "height": h,
        "fourcc": cc,
        "orientation_meta": rot,
    }
    print(json.dumps(info, ensure_ascii=False, indent=2))
    if tmp:
        print(f"（注：源路径含非 ASCII 字符，解码走临时副本 {tmp}）")


if __name__ == "__main__":
    main()
