r""""树冠能不能数清叶片" 的量具。

为什么要有这个文件：验收"冠子像不像椰子树"这件事，我上一轮是靠眼睛看的，
于是得到一句没法复用的话（"读成一团饱和绿西兰花"）。眼睛的问题有两个：
  ① 不可比 —— 改完再截图，我记不住上一版到底糊到什么程度；
  ② 会被颜色骗 —— 一团均匀绿和一圈分明的绿，缩到 200 px 时"看着都是绿的"。

所以把它拆成三个**能在两版之间比大小**的数（都在最大那团绿色叶冠的包围盒里量）：

  solidity  绿像素 / 包围盒面积。
            实心圆盘 → 接近 1；能看见天空缝的羽状冠 → 0.35~0.55。
            这是"西兰花 vs 棕榈"最硬的一条判据，因为它量的就是**缝**。
  luma_spread  叶冠内部亮度的 p90-p15。
            整团只有一个颜色 → 很小；受光面/叶背分开 → 大。
            对应 shader 里新加的 _UnderColor / _TipLift 两项。
  hue_spread  叶冠内部 (G-R) 的 p90-p15。
            量的不是明暗而是**色相分层**（黄绿梢 vs 墨绿老叶）。

用法:
    python Tools/judge_palm_crown.py <截图.png> [--box x0 y0 x1 y1]   # 归一化 0~1
不给 --box 时自动取画面里最大的那一团绿色连通域（默认场景里只有椰树冠是这个量级）。
"""
import sys

import numpy as np
from PIL import Image

# 绿色判定：G 同时压过 R 和 B。
# ⚠ G-B 的余量必须**大**：第一版用 6，结果把画面底部那片浅绿松石水
#   (≈140,235,225，G-B 只有 10) 当成了"最大的一团叶子"，量出来的是水不是冠。
#   植被的 G-B 实测在 90 以上，海水哪怕再浅也不超过 25 ⇒ 定位级取 20（见下）。
# ── 两级掩码，**不能共用一套阈值** ──
# 上一版"定位树冠"和"在树冠里量明暗"共用一个严格绿掩码，于是有个致命的反号：
# 把冠子下部压暗 ⇒ 那些像素掉出掩码 ⇒ 样本里只剩亮部 ⇒ luma_spread 反而变小。
# **量具会把改进读成退步**（实测 33 → 10，而画面并没有变平）。
# 拆两级：定位用"肯定是植被"的那级，测量用"只要还偏绿"的更宽那级 ——
# 测量级不设亮度下限，否则暗部又被排除，等于没拆。
G_MIN_OVER_R = 8      # 定位用
G_MIN_OVER_B = 20     # 定位用的 G-B 余量仍要大：浅绿松石水 ≈(140,235,225) 只有 10
LOC_MIN_G    = 25
LEAF_OVER_R  = 4      # 测量用：墨绿老叶 (12,16,10) 也得留在样本里
LEAF_OVER_B  = 6

# ── 判据从哪来：不是拍脑袋，是**把参考帧本身过一遍这个工具**量出来的 ──
#   掩码拆成两级之后（见上），必须**重新量一遍参考帧**，旧数不再可比。
#   python Tools/judge_palm_crown.py Docs/参考视频帧/参考1.png --box 0.19 0.08 0.42 0.35
#     -> solidity=0.52  luma_spread=145  hue_spread=48
#   python Tools/judge_palm_crown.py Docs/参考视频帧/参考1.png --box 0.27 0.12 0.44 0.36
#     -> solidity=0.59  luma_spread=151  hue_spread=43
# 对照我这边的同一把尺：改前 solidity=0.57（**本来就在区间内**）、luma_spread=14。
# 所以"冠子像不像椰树"缺的从来不是天空缝，是**对比度**（更前面一层是**受光**）——
# 这条结论只有量出来才会认，靠看只会一直去改几何。
SOLID_LO, SOLID_HI = 0.40, 0.70          # 参考 0.52~0.59，留两边余量
LUMA_MIN  = 95                           # 参考 145~151 的约 2/3（ACES/压缩差异）
HUE_REF   = 45                           # 参考 43~48，只报不判


def green_arr(a):
    """a: (h,w,3) int16。定位：找"哪一团是树冠"。"""
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    return (g - r >= G_MIN_OVER_R) & (g - b >= G_MIN_OVER_B) & (g > LOC_MIN_G)


def leaf_arr(a):
    """测量：包围盒内哪些像素算叶子。不看亮度，暗叶必须留在样本里。"""
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    return (g - r >= LEAF_OVER_R) & (g - b >= LEAF_OVER_B)


def green_mask(im):
    return green_arr(np.asarray(im.convert("RGB")).astype(np.int16))


def largest_blob(mask):
    """最大连通域的 label id（4 邻域 BFS）。
    先降采样 2 倍：羽叶之间本来就有几像素的天空缝，全分辨率会把一片叶拆成十几个碎片，
    于是"最大那团"会小得离谱 —— 量的就不是冠子了。"""
    h, w = mask.shape
    s = 2
    small = mask[: h // s * s, : w // s * s].reshape(h // s, s, w // s, s).any(axis=(1, 3))
    n0, n1 = small.shape
    lab = np.zeros((n0, n1), dtype=np.int32)
    cur = 0
    best_id, best_size = 0, 0
    for i in range(n0):
        for j in range(n1):
            if not small[i, j] or lab[i, j]:
                continue
            cur += 1
            lab[i, j] = cur
            size = 0
            stack = [(i, j)]
            while stack:
                y, x = stack.pop()
                size += 1
                for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    ny, nx = y + dy, x + dx
                    if 0 <= ny < n0 and 0 <= nx < n1 and small[ny, nx] and not lab[ny, nx]:
                        lab[ny, nx] = cur
                        stack.append((ny, nx))
            if size > best_size:
                best_size, best_id = size, cur
    return lab, best_id, best_size, s


def metrics(im, box, label):
    a = np.asarray(im.convert("RGB")).astype(np.int16)
    H, W = a.shape[0], a.shape[1]
    x0, y0, x1, y1 = box
    x0, y0 = max(0, int(x0 * W)), max(0, int(y0 * H))
    x1, y1 = min(W, int(x1 * W)), min(H, int(y1 * H))
    sub = a[y0:y1, x0:x1]
    if sub.size == 0:
        print("%-14s 空区域" % label)
        return None
    gm = leaf_arr(sub)
    lm = green_arr(sub)
    n = int(gm.sum())
    if n < 50:
        print("%-14s 叶像素只有 %d —— 冠子没进画面" % (label, n))
        return None
    r, g, b = sub[..., 0], sub[..., 1], sub[..., 2]
    lum = (0.299 * r + 0.587 * g + 0.114 * b)[gm]
    hue = (g - r)[gm]
    solidity = n / float(sub.shape[0] * sub.shape[1])
    ls = float(np.percentile(lum, 90) - np.percentile(lum, 15))
    hs = float(np.percentile(hue, 90) - np.percentile(hue, 15))
    ok_s = SOLID_LO <= solidity <= SOLID_HI
    ok_l = ls >= LUMA_MIN
    print("%-14s box=(%.3f,%.3f,%.3f,%.3f) px=%-6d(定位%d) "
          "solidity=%.2f %s  luma_spread=%3.0f %s  hue_spread=%3.0f(参考%d)"
          % (label, x0 / float(W), y0 / float(H), x1 / float(W), y1 / float(H), n,
             int(lm.sum()),
             solidity, "OK" if ok_s else ("太实" if solidity > SOLID_HI else "太散"),
             ls, "OK" if ok_l else "偏平", hs, HUE_REF))
    return dict(solidity=solidity, luma_spread=ls, hue_spread=hs, px=n)


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    im = Image.open(sys.argv[1]).convert("RGB")
    W, H = im.size
    box = None
    if "--box" in sys.argv:
        i = sys.argv.index("--box")
        box = tuple(float(v) for v in sys.argv[i + 1:i + 5])
    print("图 %s  %dx%d" % (sys.argv[1], W, H))
    if box is None:
        lab, best_id, best_size, s = largest_blob(green_mask(im))
        if best_id == 0:
            print("画面里一个绿像素都没有 —— 椰树没进画面")
            return 1
        ys, xs = np.where(lab == best_id)
        box = (int(xs.min()) * s / float(W), int(ys.min()) * s / float(H),
               (int(xs.max()) + 1) * s / float(W), (int(ys.max()) + 1) * s / float(H))
        print("自动定位：绿连通域 %d 个，最大那个占 %d 格(2x2 降采样) -> box=(%.3f,%.3f,%.3f,%.3f)"
              % (lab.max(), best_size, box[0], box[1], box[2], box[3]))
    m = metrics(im, box, "crown")
    if m is None:
        return 1
    print("\n判据（来自参考1 实测 solidity=%.2f~%.2f / luma_spread=%d~%d / hue=%d~%d）"
          % (0.52, 0.59, 145, 151, 43, 48))
    good = SOLID_LO <= m["solidity"] <= SOLID_HI and m["luma_spread"] >= LUMA_MIN
    print("结论：" + ("PASS" if good else "FAIL"))
    return 0 if good else 1


if __name__ == "__main__":
    sys.exit(main())
