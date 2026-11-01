# -*- coding: utf-8 -*-
"""r47_measure.py — R47 三项修复的客观量具

三个指标，各自对应一件修复：

  speckle  白点密度（/1000px）
           测"亮闪走样"：孤立的高亮像素（> μ+3σ，且 8 邻域多数更暗）数量。
           R47 修的是**距离淡出**，所以远景区密度应显著下降。

  seam     竖缝强度（列梯度尖峰比）
           测 FFT/水面贴图接缝：逐列求平均 |dI/dx|，取最大值 / 中位数。
           纯噪声图约 1.2~1.5；有 1px 接缝会 >2.5。

  moire    规则性（自相关次峰）
           测沙洲"扇贝纹"：在给定区域内对行方向做自相关，取 lag∈[15,80] 的
           最大次峰 / lag=0 峰。随机噪声 ≈0；规则摩尔纹可到 0.3+。

用法：
  python Tools/R47/r47_measure.py --img a.png --box 0,0,900,388
  python Tools/R47/r47_measure.py --compare before.png after.png --box 0,0,900,388
"""
import argparse
import os
import sys

import cv2
import numpy as np

sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "Video"))


def imread_u(p):
    return cv2.imdecode(np.fromfile(p, np.uint8), cv2.IMREAD_COLOR)


def parse_box(s, shape):
    if not s:
        h, w = shape[:2]
        return 0, 0, w, h
    x0, y0, x1, y1 = [int(v) for v in s.split(",")]
    h, w = shape[:2]
    return max(0, x0), max(0, y0), min(w, x1), min(h, y1)


def speckle(gray):
    """孤立亮点数 / 1000 px（高通残差法）

    ⚠ 第一版用 μ+3σ 做阈值：实测 mean 146/std 51 ⇒ 阈值 299 > 255，**永远命中 0**。
    改用高通残差：g - 高斯模糊(g)，超过 18 灰阶的算"点"。
    """
    g = gray.astype(np.float32)
    hp = g - cv2.GaussianBlur(g, (0, 0), 3.0)
    hot = hp > 18.0
    return float(hot.sum()) / g.size * 1000.0


def hardness(gray):
    """孤立度：热点里"几乎没有热点邻居"的比例（1=纯单像素噪点，0=成团）

    为什么需要它：参考片的颗粒是**成团的软噪**（压缩/胶片），我们的走样是
    **1px 硬点**。单看密度会误导（实测我方 25 < 参考 53，但观感我方更"脏"）。
    """
    g = gray.astype(np.float32)
    hp = g - cv2.GaussianBlur(g, (0, 0), 3.0)
    hot = hp > 18.0
    if hot.sum() == 0:
        return 0.0
    k = np.ones((3, 3), np.uint8)
    nb = cv2.filter2D(hot.astype(np.uint8), -1, k) - hot.astype(np.uint8)
    # ⚠ 分母必须是"热点"，分子也必须是"热点里邻居≤1的"——漏掉 & hot 会算出 >100%
    return float(((nb <= 1) & hot).sum()) / max(int(hot.sum()), 1)


def seam(gray):
    """列梯度尖峰比：max(列均 |dx|) / median"""
    g = gray.astype(np.float32)
    dx = np.abs(np.diff(g, axis=1))
    col = dx.mean(axis=0)
    med = float(np.median(col)) + 1e-6
    return float(col.max()) / med, int(col.argmax())


def moire(gray):
    """行方向自相关的次峰（lag 15~80）——**先高通**，否则被画面大尺度梯度带偏"""
    g = gray.astype(np.float32)
    g = g - cv2.GaussianBlur(g, (0, 0), 6.0)
    g = g - g.mean()
    if g.std() < 1e-3:
        return 0.0
    row = g.mean(axis=0)
    row = row - row.mean()
    ac = np.correlate(row, row, mode="full")[len(row) - 1:]
    if ac[0] <= 1e-6:
        return 0.0
    ac = ac / ac[0]
    seg = ac[15:81]
    return float(seg.max()) if len(seg) else 0.0


def measure(path, box):
    im = imread_u(path)
    x0, y0, x1, y1 = parse_box(box, im.shape)
    crop = im[y0:y1, x0:x1]
    gray = cv2.cvtColor(crop, cv2.COLOR_BGR2GRAY)
    s, col = seam(gray)
    return {
        "speckle": speckle(gray),
        "hardness": hardness(gray),
        "seam_ratio": s,
        "seam_col": col,
        "moire": moire(gray),
        "mean": float(gray.mean()),
        "std": float(gray.std()),
    }


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--img")
    ap.add_argument("--compare", nargs=2)
    ap.add_argument("--box", default="")
    a = ap.parse_args()

    if a.img:
        m = measure(a.img, a.box)
        print(f"{os.path.basename(a.img)}  speckle={m['speckle']:.2f}/1000px  hard={m['hardness']:.2f}  "
              f"seam={m['seam_ratio']:.2f}@{m['seam_col']}  moire={m['moire']:.3f}  "
              f"mean={m['mean']:.1f} std={m['std']:.1f}")
        return

    if a.compare:
        b, c = measure(a.compare[0], a.box), measure(a.compare[1], a.box)
        print(f"{'指标':<12}{'改前':>12}{'改后':>12}{'变化':>12}")
        for k, label, fmt in [("speckle", "白点密度", "{:.2f}"), ("hardness", "点硬度", "{:.2f}"),
                              ("seam_ratio", "竖缝尖峰", "{:.2f}"),
                              ("moire", "摩尔纹", "{:.3f}"), ("mean", "亮度", "{:.1f}"), ("std", "对比", "{:.1f}")]:
            vb, vc = b[k], c[k]
            chg = (vc - vb) / (abs(vb) + 1e-9) * 100
            print(f"{label:<12}{fmt.format(vb):>12}{fmt.format(vc):>12}{chg:>11.1f}%")
        return

    ap.print_help()


if __name__ == "__main__":
    main()
