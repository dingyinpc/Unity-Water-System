"""量自己渲的图，对着参考帧目标值打偏差。

闭环为什么必须存在：参考帧的色板是【显示空间】的数，材质给的是【线性反照率】，
中间隔着 ACES + postExposure + 雾 + 抖音压缩。所以"把材质设成 (3,118,200)"
完全不等于"屏幕上出现 (3,118,200)"。只有用同一套取样法量自己的截图才可比。

取样模型（前两版都写错过，记在这免得再犯）：

  ① 云是**散布**在天空里的，不能给它单开一条横带 —— 那样必然和天顶带重叠，
     同一片像素被当成两个目标量，两边的数都不可信。
     正确做法：**同一条带取不同分位** —— p15 ≈ 云缝里的纯天蓝，p90 ≈ 云顶。

  ② 分位**不能按 R/G/B 各自独立取**。三个通道各自排第 15% 会来自三颗不同的像素，
     拼出来的是一个画面里根本不存在的颜色（实测会把暗像素的 R 配上亮像素的 B）。
     必须先按**亮度**给像素排序，再取那一颗像素的完整 RGB。

用法:
    python Tools/compare_reference.py <我的截图.png> [--horizon 0.58]
    python Tools/compare_reference.py --selftest      # 先验尺子本身
"""
import json
import os
import sys

from PIL import Image

SPEC = os.path.join(os.path.dirname(__file__), "..", "Docs", "参考视频帧", "海岛目标色板.json")

# (目标键, 带起, 带止, 亮度分位)
# 天空带：0=画顶, 1=地平线；水带：0=地平线, 1=画底
SKY_BANDS = [
    ("sky_upper",     0.00, 0.45, 0.15),   # 平视画幅里最高的一段（仰角 ~22°），不是真天顶
    ("sky_mid",       0.30, 0.72, 0.15),
    ("sunlit_top",    0.00, 0.55, 0.90),
    ("shadow_base",   0.35, 0.82, 0.45),
    ("horizon_haze",  0.86, 1.00, 0.60),
]
WATER_BANDS = [
    ("far_at_horizon",    0.00, 0.14, 0.50),
    ("lagoon_deep",       0.10, 0.34, 0.22),
    ("lagoon_mid",        0.30, 0.62, 0.50),
    ("shallow_over_sand", 0.60, 0.96, 0.78),
]


def lum(c):
    return 0.299 * c[0] + 0.587 * c[1] + 0.114 * c[2]


def pick(vals, p):
    """按亮度排序后取第 p 分位那颗像素的完整 RGB —— 见模块头 ② 。"""
    if not vals:
        return None
    s = sorted(vals, key=lum)
    return s[min(len(s) - 1, int(len(s) * p))]


def region(im, y0, y1):
    w, h = im.size
    y0 = min(max(y0, 0.0), 0.9995)
    y1 = min(max(y1, y0 + 0.0005), 1.0)
    a, b = int(y0 * h), max(int(y0 * h) + 1, int(y1 * h))
    rows = max(1, min(60, b - a))
    stride = max(1, (b - a) // rows)
    px = im.load()
    x0, x1 = int(0.05 * w), int(0.95 * w)
    out = []
    for y in range(a, b, stride):
        for x in range(x0, x1, 2):
            out.append(px[x, y])
    return out


def measure(im, horizon):
    H = im.size[1]
    res = {}
    for name, a, b, p in SKY_BANDS:
        v = region(im, a * horizon, b * horizon)
        if len(v) >= 40:
            res[name] = pick(v, p)
    for name, a, b, p in WATER_BANDS:
        v = region(im, horizon + a * (1 - horizon), horizon + b * (1 - horizon))
        if len(v) >= 40:
            res[name] = pick(v, p)
    return res


def targets():
    spec = json.load(open(SPEC, encoding="utf-8"))
    t = {}
    for grp in ("sky", "cloud", "water", "island"):
        for k, v in spec[grp].items():
            if not k.startswith("_"):
                t[k] = v
    return t


def selftest():
    """验的是**取样管道本身**，不是"p90 到底像不像云顶"。

    合成一张"每一行的颜色 = f(行号)"的图，于是任意横带的亮度分位对应哪一行
    是**解析可知**的：直接算出该带第 p 分位应当落在哪一行、什么颜色，再比工具
    读回来的是什么。偏差应为 0。

    为什么不能拿参考帧自测：参考帧带抖音黑边和右侧图标，整幅坐标与视频区坐标
    差一截，探针会集体错位 —— 量出来的偏差全是取样几何的问题，不是画面的问题。
    （上一版就是往黑底上画几条细带，宽带的分位自然读到黑底，报了 214 的假偏差。）
    """
    W, H, hor = 400, 800, 0.58
    im = Image.new("RGB", (W, H))
    px = im.load()
    # 行色 = 亮度随 y 单调上升，保证"按亮度排第 p 分位" == "按 y 排第 p 分位"
    for y in range(H):
        c = int(255 * y / (H - 1))
        for x in range(W):
            px[x, y] = (c, c, c)

    def expect(y0, y1, p):
        n = int((y1 - y0) * H)
        row = int(y0 * H + min(n - 1, int(n * p)))
        c = int(255 * row / (H - 1))
        return (c, c, c)

    worst = 0
    for name, a, b, p in SKY_BANDS:
        got = pick(region(im, a * hor, b * hor), p)
        exp = expect(a * hor, b * hor, p)
        d = max(abs(got[i] - exp[i]) for i in range(3))
        worst = max(worst, d)
        print("  %-20s 带[%.2f,%.2f] p%.2f  期望%3d 实得%3d  偏 %d"
              % (name, a * hor, b * hor, p, exp[0], got[0], d))
    for name, a, b, p in WATER_BANDS:
        y0 = hor + a * (1 - hor)
        y1 = hor + b * (1 - hor)
        got = pick(region(im, y0, y1), p)
        exp = expect(y0, y1, p)
        d = max(abs(got[i] - exp[i]) for i in range(3))
        worst = max(worst, d)
        print("  %-20s 带[%.2f,%.2f] p%.2f  期望%3d 实得%3d  偏 %d"
              % (name, y0, y1, p, exp[0], got[0], d))

    # 通道混叠专项：造一张"暗像素=纯蓝、亮像素=纯红"的图。
    # 若分位按通道独立取，会拼出 (亮R, 暗G, 暗B) 这种画面里不存在的颜色；
    # 按亮度整颗取，结果必定是纯蓝或纯红之一。
    im2 = Image.new("RGB", (200, 200))
    p2 = im2.load()
    for y in range(200):
        for x in range(200):
            p2[x, y] = (0, 0, 255) if y < 100 else (255, 0, 0)
    q = pick(region(im2, 0.0, 1.0, ), 0.5)
    pure = q in ((0, 0, 255), (255, 0, 0))
    print("\n  通道不混叠（读回应为纯蓝或纯红）: %s -> %s" % (q, "OK" if pure else "FAIL"))
    print("取样管道最大偏差 %d -> %s" % (worst, "OK" if worst <= 3 else "尺子有问题"))
    return 0 if (worst <= 3 and pure) else 1


def main():
    if "--selftest" in sys.argv:
        return selftest()
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    im = Image.open(sys.argv[1]).convert("RGB")
    hor = 0.58
    for i, a in enumerate(sys.argv):
        if a == "--horizon":
            hor = float(sys.argv[i + 1])
    tgt, got = targets(), measure(im, hor)
    print("图 %s  地平线 y=%.2f" % (os.path.basename(sys.argv[1]), hor))
    print("%-20s %-15s %-15s %s" % ("探针", "目标", "实测", "偏差"))
    bad = 0
    for name in [n for n, *_ in SKY_BANDS + WATER_BANDS]:
        if name not in tgt or name not in got:
            continue
        t, v = tgt[name], got[name]
        d = max(abs(v[i] - t[i]) for i in range(3))
        if d > 55:
            bad += 1
        print("%-20s (%3d,%3d,%3d)  (%3d,%3d,%3d)  %3d %s"
              % (name, t[0], t[1], t[2], v[0], v[1], v[2], d,
                 "OK" if d <= 28 else ("~ " if d <= 55 else "!!")))
    print("\n超差(>55) 探针 %d 个" % bad)
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
