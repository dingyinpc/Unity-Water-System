"""量"远处是不是太锐"：边缘高频 + 彩度 + 雾的实际贡献。

用法: python Tools/far_sharp.py <我的截图> <参考帧>

三个数各自回答一个问题：
  边缘HF   = |二阶差分| 均值。高 = 边缘硬、有"锐化感"；低 = 软。
  彩度p50/p90 = 远处植被的饱和度分布。参考里远处应该被空气透视**拉向天蓝**
              （彩度下降、亮度上升），如果我们的彩度反而更高，就是缺这层。
  雾贡献    = 用场景里真实的 fog 公式算该距离上雾到底吃了多少。
              这一项是**解释**为什么前两项会偏，不是又一个观感猜测。
"""
import math
import sys

from PIL import Image


def pct(sorted_vals, p):
    """安全取百分位：p∈[0,1)，越界夹到最后一个元素。"""
    if not sorted_vals:
        return 0.0
    i = min(len(sorted_vals) - 1, int(len(sorted_vals) * p))
    return sorted_vals[i]


def stat(path, x0, y0, x1, y1):
    """只在**植被像素**上量。

    ⚠ 为什么不直接用固定矩形：构图一变（这次是钉死 16:9 把岛整体挪低），
      矩形就会悄悄跑到**天空**上，而天空又亮又不太饱和 —— 实测第一版
      量出"亮度 p50=236、彩度 0.369"，看着像结论，其实量的是天。
      所以这里按"绿通道最大"筛像素，让探针自己找植被，
      并且把**样本数报出来**：样本太少就直接判定不可信，不硬算平均。
    """
    im = Image.open(path).convert("RGB")
    W, H = im.size
    a, b = int(x0 * W), int(x1 * W)
    c, d = int(y0 * H), int(y1 * H)
    px = im.load()

    def is_leaf(x, y):
        r, g, bl = px[x, y]
        return g >= r and g >= bl          # 绿是最大通道 ⇒ 植被

    s = 0.0
    n = 0
    for y in range(c + 1, d - 1):
        for x in range(a + 1, b - 1):
            # 二阶差分要用到 x-1 和 x+1，三个都得是植被，
            # 否则"叶/天交界"那条边会被当成高频细节混进来（那才是真的假锐度）
            if is_leaf(x, y) and is_leaf(x - 1, y) and is_leaf(x + 1, y):
                r0 = px[x - 1, y]; r1 = px[x, y]; r2 = px[x + 1, y]
                l = lambda t: 0.299 * t[0] + 0.587 * t[1] + 0.114 * t[2]
                s += abs(l(r0) - 2 * l(r1) + l(r2))
                n += 1

    sat = []
    lum = []
    for y in range(c, d, 2):
        for x in range(a, b, 2):
            if not is_leaf(x, y):
                continue
            r, gg, bb = px[x, y]
            mx, mn = max(r, gg, bb), min(r, gg, bb)
            sat.append(0.0 if mx == 0 else (mx - mn) / float(mx))
            lum.append(mx)
    sat.sort()
    lum.sort()
    return (s / max(1, n), pct(sat, 0.50), pct(sat, 0.90), pct(lum, 0.50),
            n, len(sat))


if len(sys.argv) < 3:
    print(__doc__)
    sys.exit(2)

print("%-22s %-9s %-9s %-9s %-8s %s"
      % ("区域(只统计植被像素)", "边缘HF", "彩度p50", "彩度p90", "亮度p50", "边缘样本/颜色样本"))
m = stat(sys.argv[1], 0.50, 0.40, 0.95, 0.60)
print("%-20s %-9.2f %-9.3f %-9.3f %-8.0f %d / %d" % ("我 远处植被", *m))
r = stat(sys.argv[2], 0.60, 0.36, 0.98, 0.60)
print("%-20s %-9.2f %-9.3f %-9.3f %-8.0f %d / %d" % ("参考 远处植被", *r))
print()
if m[4] < 500 or r[4] < 500:
    print("⚠ 样本太少（我 %d / 参考 %d），下面的倍数不可信" % (m[4], r[4]))
print("边缘HF 我/参考 = %.2f 倍   彩度p50 我/参考 = %.2f 倍   亮度p50 我/参考 = %.2f 倍"
      % (m[0] / max(1e-6, r[0]), m[1] / max(1e-6, r[1]), m[3] / max(1e-6, r[3])))
print()
print("雾在该距离上实际吃掉多少（RenderSettings: ExponentialSquared, density=0.00085）")
for dist in (100, 150, 300, 600, 1200, 2000):
    f = 1.0 - math.exp(-((0.00085 * dist) ** 2))
    print("   d=%5dm  fogFactor = %5.1f%%" % (dist, 100.0 * f))
