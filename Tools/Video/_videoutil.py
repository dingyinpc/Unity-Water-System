# -*- coding: utf-8 -*-
"""逐帧分析流水线公共工具：unicode 路径安全 IO / index 读取 / 参数解析。"""
import os
import csv
import shutil
import tempfile

# 允许的 ASCII 暂存目录（本项目协作环境批准的临时目录）
_LA = os.environ.get("LOCALAPPDATA")
TEMP_ASCII_DIR = os.path.join(_LA, "Temp", "opencode") if _LA else tempfile.gettempdir()


def is_ascii(s: str) -> bool:
    try:
        s.encode("ascii")
        return True
    except UnicodeEncodeError:
        return False


def ensure_ascii_path(path: str, tag: str = "input"):
    """cv2.VideoCapture 对非 ASCII 路径在部分环境不稳；含中文时先拷到 ASCII 临时路径。
    返回 (可用路径, 临时副本或 None)。"""
    path = os.path.abspath(path)
    if is_ascii(path):
        return path, None
    os.makedirs(TEMP_ASCII_DIR, exist_ok=True)
    ext = os.path.splitext(path)[1] or ".mp4"
    dst = os.path.join(TEMP_ASCII_DIR, f"vpipe_{tag}{ext}")
    shutil.copy2(path, dst)
    return dst, dst


def imread_u(path: str):
    """cv2.imread 不支持中文路径，用 imdecode 兜底。"""
    import cv2
    import numpy as np
    data = np.fromfile(path, dtype=np.uint8)
    if data.size == 0:
        return None
    return cv2.imdecode(data, cv2.IMREAD_COLOR)


def imwrite_u(path: str, img, params=None) -> bool:
    """cv2.imwrite 不支持中文路径，用 imencode + tofile 兜底。"""
    import cv2
    ext = os.path.splitext(path)[1]
    ok, buf = cv2.imencode(ext, img, params or [])
    if not ok:
        return False
    buf.tofile(path)
    return True


def load_index(out_dir: str):
    """读 extract_all_frames.py 的 index.csv → [(frame_idx, time_sec, 绝对路径)]"""
    idx = os.path.join(out_dir, "index.csv")
    rows = []
    with open(idx, "r", encoding="utf-8", newline="") as f:
        rd = csv.reader(f)
        next(rd, None)
        for r in rd:
            if len(r) >= 3:
                rows.append((int(r[0]), float(r[1]), os.path.join(out_dir, r[2].replace("/", os.sep))))
    return rows


def parse_boxes(spec: str):
    """'名:x0,y0,x1,y1;名2:...'（百分比 0-100）→ [(名, x0, y0, x1, y1)]"""
    boxes = []
    if not spec:
        return boxes
    for part in spec.split(";"):
        part = part.strip()
        if not part:
            continue
        name, _, coords = part.partition(":")
        nums = [float(x) for x in coords.split(",")]
        if len(nums) != 4:
            raise ValueError(f"box 需要 4 个百分比坐标: {part}")
        boxes.append((name.strip(), *nums))
    return boxes
