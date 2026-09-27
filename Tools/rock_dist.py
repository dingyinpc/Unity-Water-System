#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""裸岩分布探针 —— 量 p50/p75/p90 三点，而不是只报中位数。

为什么要单独一把尺（《05》I-*：「别拿单点中位说话」）：
  `Docs/参考视频帧/海岛目标色板.json → island.sea_rock_dist` 记的那次事故是：
  把三档岩色反照率抬到 **p50 命中** (7,33,64) 之后，p75 冲到 (14,100,192)、
  p90 冲到 (18,139,255)——B 通道顶格，画面变荧光青，比"太黑"更糟。
  单点中位看不出这件事：它命中了，但**跨度**（p90/p50）从 3.3 涨到了更远去。
  ⇒ 岩的判据必须是三点分布 + p90/p50 这个跨度比值。

判别式**与色板自身一致**（不是这里另立的）：
  暗(lum<150) 且 B>=G  → 裸岩 bare
  暗(lum<150) 且 G>B  → 苔   moss
  亮(lum>=150)         → 亮爆 hot（该往暗调多少看这个比例）

用法：
  python Tools/rock_dist.py <截图.png> <x0> <y0> <x1> <y1>
  python Tools/rock_dist.py <截图.png> --box 0.654,0.177,0.827,0.560
  python Tools/rock_dist.py --selftest          # 先验尺子：合成一块已知分布的图
退出码：0 = 尺度自检通过且量到了；1 = 自检失败或框内裸岩不足。
"""
import sys
import numpy as np
from PIL import Image

LUM_MAX = 150.0
MIN_PIX = 200

# 目标（Docs/参考视频帧/海岛目标色板.json → island.sea_rock_dist）
TGT = {"p50": (8, 35, 59), "p75": (12, 45, 72), "p90": (33, 89, 104)}
TGT_RATIO_G = 2.54          # bare_range_p90_over_p50_G
TGT_MOSS_PCT = 32           # moss_share_screen_pct


def lum(rgb):
    return 0.2126 * rgb[..., 0] + 0.7152 * rgb[..., 1] + 0.0722 * rgb[..., 2]


def classify(a):
    """返回 (bare, moss, hot) 三个布尔掩码。判别式与色板一致。"""
    l = lum(a)
    dark = l < LUM_MAX
    b_ge_g = a[..., 2] >= a[..., 1]
    return dark & b_ge_g, dark & (~b_ge_g), ~dark


def pct_of(a, mask, p):
    sel = a[mask]
    if len(sel) == 0:
        return None
    return tuple(int(round(np.percentile(sel[:, i], p))) for i in range(3))


def measure(a):
    bare, moss, hot = classify(a)
    n = a.shape[0] * a.shape[1]
    out = {"n": n, "bare_n": int(bare.sum()), "moss_n": int(moss.sum()),
           "hot_n": int(hot.sum())}
    for tag, p in (("p50", 50), ("p75", 75), ("p90", 90)):
        out[tag] = pct_of(a, bare, p)
    out["bare_pct"] = 100.0 * out["bare_n"] / max(1, n)
    out["moss_pct"] = 100.0 * out["moss_n"] / max(1, n)
    out["hot_pct"] = 100.0 * out["hot_n"] / max(1, n)
    if out["p50"] and out["p50"][1] > 0 and out["p90"]:
        out["ratio_G"] = out["p90"][1] / out["p50"][1]
    else:
        out["ratio_G"] = None
    return out


def report(m, title, verbose=True):
    print("== %s ==" % title)
    if m["bare_n"] < MIN_PIX:
        print("  裸岩像素只有 %d（< %d）—— 框挪了、或岩被过滤器判成别的了；"
              "**不是「没有岩石」**" % (m["bare_n"], MIN_PIX))
        return 1
    for tag in ("p50", "p75", "p90"):
        v = m[tag]
        d = max(abs(v[i] - TGT[tag][i]) for i in range(3))
        print("  bare %s (%3d,%3d,%3d)   目标(%3d,%3d,%3d)  偏 %3d"
              % (tag, v[0], v[1], v[2], TGT[tag][0], TGT[tag][1], TGT[tag][2], d))
    if m["ratio_G"]:
        flag = "OK" if m["ratio_G"] <= TGT_RATIO_G + 0.15 else "跨度偏宽 <<<"
        print("  跨度 p90/p50 (G) = %.2f   目标 %.2f   %s" % (m["ratio_G"], TGT_RATIO_G, flag))
    mflag = "OK" if abs(m["moss_pct"] - TGT_MOSS_PCT) <= 8 else "偏离 <<<"
    print("  苔占比 %.0f%%  目标 %d%%  %s   亮爆 %.0f%%   裸岩占比 %.0f%%  (n=%d)"
          % (m["moss_pct"], TGT_MOSS_PCT, mflag, m["hot_pct"], m["bare_pct"], m["n"]))
    if verbose:
        score = sum(abs(m[t][i] - TGT[t][i]) for t in ("p50", "p75", "p90") for i in range(3))
        print("  三点合分（本工具定义：3 点 × 3 通道 |Δ| 之和）= %d" % score)
    return 0


def selftest():
    """合成一块已知分布的岩石：B>G>R 冷调、亮度分层。量的值必须能复现构造值。"""
    rng = np.random.default_rng(7)
    rows = []
    # 三层，各 1/3 像素：暗 / 中 / 亮 —— 期望 p50 落在中层、p90 落在亮层
    for c in [(6, 30, 46), (10, 42, 62), (30, 88, 100)]:
        rows.append(np.tile(np.array(c, dtype=np.float64), (1000, 1)))
    a = np.concatenate(rows).reshape(60, 50, 3)
    a = a + rng.normal(0, 0.4, a.shape)          # 少量抖动，不该改变分位
    a = np.clip(a, 0, 255)
    m = measure(a)
    ok = (m["bare_n"] == 3000 and m["p50"] == (10, 42, 62)
          and m["p90"] == (30, 88, 100) and m["moss_n"] == 0)
    print("[selftest] n=%d 裸岩=%d 苔=%d" % (m["n"], m["bare_n"], m["moss_n"]))
    print("[selftest] p50=%s (期望 (10,42,62))  p90=%s (期望 (30,88,100))"
          % (m["p50"], m["p90"]))
    print("[selftest] 结果: %s" % ("OK" if ok else "FAIL —— 尺子有问题，别用它调参"))
    # 反向自检：把苔色送进来必须被判成苔而不是裸岩
    moss = np.tile(np.array([23, 76, 62], dtype=np.float64), (30, 30, 1))
    mm = measure(moss)
    rev = mm["moss_n"] == 900 and mm["bare_n"] == 0
    print("[selftest] 反向（苔色 (23,76,62) 应判成苔）: %s" % ("OK" if rev else "FAIL"))
    return 0 if (ok and rev) else 1


def main():
    if "--selftest" in sys.argv:
        return selftest()
    if len(sys.argv) < 3:
        print(__doc__)
        return 2
    path = sys.argv[1]
    if "--box" in sys.argv:
        f = sys.argv[sys.argv.index("--box") + 1].split(",")
        x0, y0, x1, y1 = (float(v) for v in f[:4])
    else:
        x0, y0, x1, y1 = (float(v) for v in sys.argv[2:6])
    im = Image.open(path).convert("RGB")
    w, h = im.size
    a = np.asarray(im, dtype=np.float64)[int(y0 * h):int(y1 * h),
                                         int(x0 * w):int(x1 * w)]
    m = measure(a)
    print("图 %s  %dx%d   框 (%.3f,%.3f)-(%.3f,%.3f)  取到 %dx%d"
          % (path, w, h, x0, y0, x1, y1, a.shape[1], a.shape[0]))
    return report(m, "裸岩分布")


if __name__ == "__main__":
    sys.exit(main())