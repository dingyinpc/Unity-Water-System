"""从抖音截帧量**竖向颜色剖面**。

为什么不是固定框、也不是主色聚类：
  · 固定框 —— 6 张构图差很远（参考5 大半是天空），"这条框是树叶"会采到蓝天；
  · 主色聚类 —— 天蓝和水青本来就接近，12 个色心会把两层糊成一个，
    报出来每个簇都"覆盖 y=0.05~0.85"，等于没测到东西。

而这六帧全是**水平分层**的场景：天顶 → 云 → 地平雾 → 远水 → 近水 → 沙。
所以正确的测量是"每一横条的中位色"，它同时就是天空盒和水体该给的竖直梯度规格。

中位数而不是均值：抗住横穿画面的椰子树、礁石、播放三角。
另外 p20/p80 一起给：同一条带里"云亮面 vs 云暗面""浅滩 vs 深水"的跨度就出来了。

UI 屏蔽（抖音视频截图特有的三样）：右侧图标列、底部作者名+标签+进度条、正中间播放三角。

用法: python Tools/sample_reference.py [png ...]
"""
import glob
import os
import sys

from PIL import Image

STRIPS = 14
# (名字, 归一化 x0, x1)  —— 横向取哪些列。默认躲开右侧图标列
XSPAN = (0.02, 0.885)
UI_ROWS = [(0.855, 1.0)]           # 底部字幕/进度条
CENTER = (0.43, 0.57, 0.40, 0.545)  # 播放三角 x0,x1,y0,y1


def video_rect(im):
    g = im.convert("L")
    w, h = g.size
    px = g.load()
    thr = 28

    def longest(vals):
        best = cur = bstart = cstart = 0
        for i, v in enumerate(vals):
            if v > thr:
                if cur == 0:
                    cstart = i
                cur += 1
                if cur > best:
                    best, bstart = cur, cstart
            else:
                cur = 0
        return bstart, bstart + best

    colb = [sum(px[x, y] for y in range(0, h, 7)) / (h // 7) for x in range(w)]
    rowb = [sum(px[x, y] for x in range(0, w, 7)) / (w // 7) for y in range(h)]
    x0, x1 = longest(colb)
    y0, y1 = longest(rowb)
    return x0, y0, x1, y1


def pct(vals, p):
    vals = sorted(vals)
    return vals[min(len(vals) - 1, int(len(vals) * p))]


def profile(path):
    im = Image.open(path).convert("RGB")
    X0, Y0, X1, Y1 = video_rect(im)
    W, H = X1 - X0, Y1 - Y0
    px = im.load()
    print("\n=== %s  视频区 %dx%d ===" % (os.path.basename(path), W, H))
    print("  条  y      p20            中位            p80          跨度")
    for i in range(STRIPS):
        a, b = i / STRIPS, (i + 1) / STRIPS
        y0, y1 = Y0 + int(a * H), Y0 + int(b * H)
        skip = any(u0 <= (a + b) / 2 <= u1 for u0, u1 in UI_ROWS)
        if skip:
            continue
        cs = []
        cx0, cx1 = X0 + int(XSPAN[0] * W), X0 + int(XSPAN[1] * W)
        cy0, cy1 = X0 + int(CENTER[0] * W), X0 + int(CENTER[1] * W)
        for y in range(y0, y1, 2):
            rel = (y - Y0) / H
            incenter = CENTER[2] <= rel <= CENTER[3]
            for x in range(cx0, cx1, 2):
                if incenter and cy0 <= x <= cy1:
                    continue
                cs.append(px[x, y])
        if len(cs) < 50:
            continue
        p20 = [pct([c[k] for c in cs], 0.20) for k in range(3)]
        med = [pct([c[k] for c in cs], 0.50) for k in range(3)]
        p80 = [pct([c[k] for c in cs], 0.80) for k in range(3)]
        lum = [0.299 * r + 0.587 * g + 0.114 * b for r, g, b in cs]
        spread = pct(lum, 0.9) - pct(lum, 0.1)
        print("  %2d  %.2f  (%3d,%3d,%3d)  (%3d,%3d,%3d)  (%3d,%3d,%3d)  %5.0f"
              % (i, (a + b) / 2, *p20, *med, *p80, spread))


def main():
    args = sys.argv[1:] or sorted(glob.glob(
        os.path.join(os.path.dirname(__file__), "..", "Docs", "参考视频帧", "参考[1-6].png")))
    for p in args:
        profile(p)


if __name__ == "__main__":
    main()
