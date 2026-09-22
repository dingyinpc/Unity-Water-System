#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""岩石框的**无过滤**探针 —— 补 compare_reference.probe_box 的一个盲区。

为什么还要一把尺：
  probe_box 为了把云和海从岩石框里剔掉，用了两条过滤（lum>150 剔亮、B-R>55 剔海）。
  这套规则拿来比"参考帧 vs 我"是公平的，因为两边同一把尺。
  但当我的岩石**本身**就亮且青的时候，它会被自己的尺子判成"海"而剔掉 ——
  于是"存活像素中位 (15,33,23)"看着挺接近目标 (13,44,57)，其实只是
  **剩下那 20% 恰好是不亮的那部分**。拿这个数去调参会越调越亮。
  （这就是"好得离谱 = 仪器坏了"的另一个变体：自我选择的掩码。）

所以这里把同一个框**不加过滤**再量一遍，并且把两条规则的淘汰率分开报出来：
  · 原始 p25/中位/p75  → 岩石真实亮度和色相方向
  · %亮爆(lum>150)     → 该往暗调多少
  · %偏水(B-R>55)      → 该往灰绿调多少
  · 色相方向计数       → B>G>R(冷) / G>B>R(灰绿) 各占多少

用法：
  python Tools/rock_probe.py <图> <x0> <y0> <x1> <y1>
"""
import sys
from PIL import Image

LUM_MAX = 150.0
B_REJ   = 55.0


def lum(c):
    return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2]


def pct(vals, p):
    s = sorted(vals, key=lum)
    return s[min(len(s) - 1, int(len(s) * p))]


def main():
    if len(sys.argv) < 6:
        print(__doc__)
        return 2
    path, x0, y0, x1, y1 = sys.argv[1], *[float(v) for v in sys.argv[2:6]]
    im = Image.open(path).convert("RGB")
    w, h = im.size
    a0, a1 = int(x0 * w), int(x1 * w)
    b0, b1 = int(y0 * h), int(y1 * h)
    px = im.load()

    vals = []
    hot = watery = 0
    cool_b = cool_g = 0
    for y in range(b0, b1):
        for x in range(a0, a1):
            c = px[x, y]
            vals.append(c)
            if lum(c) > LUM_MAX:
                hot += 1
            if c[2] - c[0] > B_REJ:
                watery += 1
            if c[2] > c[1] > c[0]:
                cool_b += 1
            elif c[1] > c[2] and c[1] > c[0]:
                cool_g += 1

    n = max(1, len(vals))
    p25, med, p75 = pct(vals, .25), pct(vals, .5), pct(vals, .75)
    print("框 (%.3f,%.3f)-(%.3f,%.3f)  像素 %d" % (x0, y0, x1, y1, n))
    print("  无过滤      p25(%3d,%3d,%3d)  中位(%3d,%3d,%3d)  p75(%3d,%3d,%3d)"
          % (*p25, *med, *p75))
    print("  亮爆 lum>%.0f : %5.1f%%   <- 太高就该压暗" % (LUM_MAX, 100.0 * hot / n))
    print("  偏水 B-R>%.0f : %5.1f%%   <- 太高就该往灰绿走" % (B_REJ, 100.0 * watery / n))
    print("  色相方向      B>G>R 冷 %5.1f%%   G>B>R 灰绿 %5.1f%%"
          % (100.0 * cool_b / n, 100.0 * cool_g / n))
    print("  中位亮度 %.0f  中位 B-R %d" % (lum(med), med[2] - med[0]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
