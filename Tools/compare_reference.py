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

# 个别带**不能横跨整幅**。本构图里压在地平线上的那两条（天空侧 horizon_haze、
# 水侧 far_at_horizon）右半段是**远岛 + 白沙嘴**，横向一放开就把白沙和浪花混进来：
# 实测同一条带两帧之间能从 (141,189,191) 跳到 (45,237,195)，
# 也就是它根本不在量"开阔海天霾"，而是在量那座岛。
#
# ⚠ 但两条带**不能共用同一个窗口** —— 地平线上下压着的东西不一样：
#   · horizon_haze（地平线**上方**）：左三分之一是干净开阔天，
#     收进来之后读数从伪中性 (183,185,179) 变回正常蓝 (127,177,219)。
#   · far_at_horizon（地平线**下方**）：左三分之一正好盖住**左边那座小沙洲
#     和它的青绿礁石**，量出来是 (33,171,139) 饱和绿 —— 那根本不是海。
#     开阔水面只在两座陆地中间那道缝里，所以取 0.38~0.62。
BAND_X = {
    "horizon_haze":   (0.05, 0.36),
    "far_at_horizon": (0.38, 0.62),
    # ── 第 12 轮自迭代（2026-10-04）：构图长大后的三处窗口修正 ──
    #   · sky_mid：分级加宽后的巨岩（screenbox x0.617-0.894）伸进这条带，
    #     全宽 p15 量到的是岩石 (8,67,41)——收进左半开阔天。
    #   · lagoon_mid / shallow_over_sand：中景沙脊压在带内（暖沙 (215,220,212)
    #     混进水的 p50），礁石群在左下——窗口取两陆之间的开阔水 x0.30-0.55。
    #   ⚠ 目标值随窗口失效：用**同一窗口量参考1 自己**重测（"拿同一把尺量
    #     参考帧"），见 TARGET_OVERRIDE。
    # ⚠ sky_mid/sunlit_top/shadow_base **没有**公共窗口：两构图的树/岩位置不对齐
    #   （我们的椰树 x0.20-0.33，参考的椰树 x0.15-0.45——避开了它的就撞上我们的）。
    #   这三个带维持全宽原目标，"含椰树像素"记为已知污染，看趋势不看绝对值。
    "lagoon_mid":         (0.30, 0.55),
    "shallow_over_sand":  (0.30, 0.55),
}

# 窗口收窄后的目标 = 参考1 同窗同分位实测值（原色板值是全宽口径，不再可比）。
# 量法与 main() 同一套：亮度排序取该分位像素的完整 RGB。
TARGET_OVERRIDE = {
    "lagoon_mid":         (57, 198, 212),
    "shallow_over_sand":  (113, 157, 163),
}


def lum(c):
    return 0.299 * c[0] + 0.587 * c[1] + 0.114 * c[2]


def pick(vals, p):
    """按亮度排序后取第 p 分位那颗像素的完整 RGB —— 见模块头 ② 。"""
    if not vals:
        return None
    s = sorted(vals, key=lum)
    return s[min(len(s) - 1, int(len(s) * p))]


def content_top(im, thresh=60.0):
    """画幅顶部**暗色 UI 条**下沿的位置（比例）。天空带从这里起算，不从 0 起算。

    为什么必须有：参考帧是抖音视频截图，顶上带一条播放器/标题 UI。
    实测它的几何是（1920×1080 那张）：
        第 0~40 行  行均亮度 219~245   ← **是亮的**，不是黑边！
        第 41~110 行 行均亮度 ≈ 31     ← 暗的中性灰 UI 条，占画幅高 3.8%~10.2%
    而 `sky_upper` 取的是顶带里**最暗 15%** —— 那条 UI 条正好占顶带的 10.5%，
    于是分位数整个落在它身上，参考帧自己量出 **(31,31,31)** 纯灰，
    对目标 (65,174,224) 偏差 193。所谓"天空不合格"其实是量具把 UI 当天采了。

    ⚠ 第一版这里写成"从第 0 行往下找第一行不够亮的"，**测不出来**：
      第 0 行亮度 235，直接返回 0。暗条是**夹在中间**的一段，不是顶上的黑边。
      所以改成"在顶部 15% 里找最深的那一行暗行，从它下沿起算"。
      没有暗行就返回 0，正常截图不受影响。
    """
    w, h = im.size
    px = im.load()
    xs = list(range(int(0.05 * w), int(0.95 * w), max(1, w // 60)))
    last_dark = -1
    for y in range(int(0.15 * h)):
        s = sum(lum(px[x, y]) for x in xs) / float(max(1, len(xs)))
        if s < thresh:
            last_dark = y
    if last_dark < 0:
        return 0.0
    return min(0.15, (last_dark + 6) / float(h))


def is_ui_grey(c):
    """截图里的**播放器/标题 UI**：暗、而且三通道几乎完全相等。

    为什么不用"把整条带往下挪"来躲它：实测参考帧顶部几何是
        第 0~40 行 均亮 219~245  ← **真内容**（很亮的天/云，sunlit_top 目标就是
                                     接近纯白的 (228,241,250)，正好来自这一段）
        第 41~110 行 均亮 ≈31    ← 中性灰 UI 条
    所以从 110 行起算会把真内容一起扔了（实测 sunlit_top 偏差从 16 恶化到 63）。
    改成**按像素剔**：又暗、R/G/B 又几乎相等的，不可能是天也不可能是海。
    """
    r, g, b = c
    return max(r, g, b) < 70 and (max(r, g, b) - min(r, g, b)) <= 6


def region(im, y0, y1, xa=0.05, xb=0.95):
    w, h = im.size
    y0 = min(max(y0, 0.0), 0.9995)
    y1 = min(max(y1, y0 + 0.0005), 1.0)
    a, b = int(y0 * h), max(int(y0 * h) + 1, int(y1 * h))
    rows = max(1, min(60, b - a))
    stride = max(1, (b - a) // rows)
    px = im.load()
    x0, x1 = int(xa * w), int(xb * w)
    out = []
    for y in range(a, b, stride):
        for x in range(x0, x1, 2):
            c = px[x, y]
            if is_ui_grey(c):
                continue          # 剔掉截图里的中性灰 UI 条，窗口本身不动
            out.append(c)
    return out


def measure(im, horizon):
    H = im.size[1]
    res = {}
    # 天空带从"内容顶缘"起算。
    # ⚠ 这条结论**翻过一次**，记清楚免得再翻：
    #   一开始以为"顶部第 0~40 行均亮 235~245 ⇒ 是真内容（亮天）"，
    #   于是改成"只按像素剔中性灰"、窗口不动。
    #   后来逐行查**通道极差**才发现那是错的：
    #     第   0~37 行  (244,244,244)…(254,254,253)  极差 ≤0.8  ← **白**字栏/留白
    #     第  38~105 行 (15,15,15)…(55,55,55)        极差 ≤1.8  ← 深灰 UI 条
    #     第 110 行起  (120,185,207)                 极差 87    ← 这才开始有颜色
    #   即顶部约 105 行（9.7% 画幅高）**整段是无色 chrome**，
    #   亮度高不等于有内容。旧目标 sunlit_top (228,241,250) 本身就是从 chrome 量的。
    #   ⇒ 窗口要跳过 chrome（content_top），同时 is_ui_grey 按像素剔作为第二道保险。
    ct = content_top(im)
    span = max(0.001, horizon - ct)
    for name, a, b, p in SKY_BANDS:
        xa, xb = BAND_X.get(name, (0.05, 0.95))
        v = region(im, ct + a * span, ct + b * span, xa, xb)
        if len(v) >= 40:
            res[name] = pick(v, p)
    for name, a, b, p in WATER_BANDS:
        xa, xb = BAND_X.get(name, (0.05, 0.95))
        v = region(im, horizon + a * (1 - horizon), horizon + b * (1 - horizon), xa, xb)
        if len(v) >= 40:
            res[name] = pick(v, p)
    return res


def probe_box(im, box, lum_max=150, b_rej=55):
    """量一个**具名矩形**，返回 (p25, 中位, p75, 保留比例)。

    为什么不能复用 SKY_BANDS 那套横带：海蚀巨岩是一个**局部物体**，它在屏幕上的
    位置由机位和地形共同决定，不是一条贯穿画面的带。拿整幅横带去量，框到的
    大部分是海和云 —— 报回来的数根本不属于岩石。

    为什么两条过滤缺一不可：框里必然混进云（比岩亮）和海（比岩蓝）。
      · lum_max 剔掉云和晒亮的沙；
      · `B - R > b_rej` 剔掉海水 —— 青绿的水 B 比 R 高一两百，
        而岩/苔无论多暗都是 **G ≥ B** 或至少 B-R 很小，这一条就能分开。
    两条规则**同时**用在参考帧和我的截图上，这样两边才是同一把尺。
    参考帧的框和自己的框坐标不必相同（物体本来就不在同一处），但规则必须相同。
    """
    w, h = im.size
    x0, y0, x1, y1 = box
    px = im.load()
    a0, a1 = int(x0 * w), int(x1 * w)
    b0, b1 = int(y0 * h), int(y1 * h)
    vals = []
    for y in range(b0, b1):
        for x in range(a0, a1):
            c = px[x, y]
            if lum(c) > lum_max:
                continue
            if c[2] - c[0] > b_rej:
                continue
            vals.append(c)
    if len(vals) < 120:
        return None
    s = sorted(vals, key=lum)
    return (s[int(len(s) * 0.25)], s[len(s) // 2], s[int(len(s) * 0.75)],
            len(vals) / float(max(1, (a1 - a0) * (b1 - b0))))


def targets():
    spec = json.load(open(SPEC, encoding="utf-8"))
    t = {}
    for grp in ("sky", "cloud", "water", "island"):
        for k, v in spec[grp].items():
            if not k.startswith("_"):
                t[k] = v
    t.update(TARGET_OVERRIDE)      # 收窗后的同尺重测目标（见 BAND_X 注释）
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


def parse_probes(argv):
    """--probe sea_rock,0.75,0.37,0.91,0.51  可给多次。"""
    out = []
    for i, a in enumerate(argv):
        if a == "--probe":
            f = argv[i + 1].split(",")
            out.append((f[0], tuple(float(v) for v in f[1:5])))
    return out


def report_probes(im, probes, tgt, lum_max):
    print("\n── 具名矩形探针（同一套过滤规则同时量参考帧与自己的截图）──")
    for name, box in probes:
        r = probe_box(im, box, lum_max)
        if r is None:
            print("  %-12s 框%s  通过过滤的像素不足 120 —— 框挪了或太亮，**不是「没有岩石」"
                  % (name, tuple(round(b, 3) for b in box)))
            continue
        p25, med, p75, kept = r
        t = tgt.get(name)
        d = max(abs(med[i] - t[i]) for i in range(3)) if t else -1
        print("  %-12s 保留%3.0f%%  p25(%3d,%3d,%3d) 中位(%3d,%3d,%3d) p75(%3d,%3d,%3d)%s"
              % (name, kept * 100, p25[0], p25[1], p25[2],
                 med[0], med[1], med[2], p75[0], p75[1], p75[2],
                 "" if not t else "  目标(%3d,%3d,%3d) 偏 %d" % (t[0], t[1], t[2], d)))
    return 0


def main():
    if "--selftest" in sys.argv:
        return selftest()
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    im = Image.open(sys.argv[1]).convert("RGB")
    hor = 0.58
    lum_max = 150
    for i, a in enumerate(sys.argv):
        if a == "--horizon":
            hor = float(sys.argv[i + 1])
        if a == "--lum":
            lum_max = int(sys.argv[i + 1])
    probes = parse_probes(sys.argv)
    tgt = targets()
    if probes:
        return report_probes(im, probes, tgt, lum_max)
    got = measure(im, hor)
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
