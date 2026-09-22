"""天空"糊 / 颗粒 / 过饱和"三选一的量具。

三种观感在像素上是三件不同的事，可以分开量：

  · **颗粒感（noise）** = 相邻像素高频能量**偏高**，而且是各向同性的细碎抖动。
    体云 raymarch 取样数不够、或 dither 模板没滤掉，就是这个。
  · **模糊（blur）** = 高频能量**偏低**，边缘过渡带宽。
    Bloom 的 scatter 太大、或云本身被大半径插值，会吃掉高频。
  · **过饱和（saturation）** = 彩度分布的**上尾太长**（大片接近 1 的 S），
    和清晰度无关，但会让人把"刺眼"描述成"糊"。

所以两个数就够分开它们：
  ① 高频能量 = |二阶差分| 的均值（越大越糙，越小越糊）
  ② 彩度 p50/p90（越大越艳）
再各量一次**参考帧的同一条带**当基准 —— 没有基准，"高频 3.2"这个数没有意义。
"""
import sys
from PIL import Image


def stats(path, xa, ya, xb, yb):
    im = Image.open(path).convert("L")
    W, H = im.size
    px = im.load()
    x0, x1 = int(xa * W), int(xb * W)
    y0, y1 = int(ya * H), int(yb * H)

    # 高频：横向二阶差分 |p[x-1] - 2p[x] + p[x+1]|
    hi = 0.0
    n = 0
    for y in range(y0 + 1, y1 - 1, 2):
        for x in range(x0 + 1, x1 - 1, 2):
            hi += abs(px[x - 1, y] - 2 * px[x, y] + px[x + 1, y])
            n += 1
    hf = hi / max(1, n)

    # 彩度：另开一张 RGB 数 S 的分布
    rgb = Image.open(path).convert("RGB")
    rp = rgb.load()
    ss = []
    for y in range(y0, y1, 3):
        for x in range(x0, x1, 3):
            r, g, b = rp[x, y]
            mx, mn = max(r, g, b), min(r, g, b)
            ss.append(0.0 if mx == 0 else (mx - mn) / mx)
    ss.sort()
    s50 = ss[len(ss) // 2]
    s90 = ss[int(len(ss) * 0.90)]
    return hf, s50, s90


# 天空带：画幅顶部到地平线上方一点（避开云底和地平线）
BAND = (0.02, 0.06, 0.98, 0.42)
print("%-30s %-10s %-8s %-8s" % ("来源", "高频能量", "S p50", "S p90"))
for label, path in (("我的截图", sys.argv[1]), ("参考1", sys.argv[2])):
    hf, s50, s90 = stats(path, *BAND)
    print("%-28s %-10.2f %-8.3f %-8.3f" % (label, hf, s50, s90))
