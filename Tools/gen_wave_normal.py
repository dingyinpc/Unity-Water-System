"""自产一张**无缝平铺**的水面波纹法线贴图。

为什么要自己生成而不下载：项目规则要求每个外部素材登记出处与授权，
自产资产直接把这条风险消掉（授权 = 我们自己）。

为什么它能无缝平铺：高度场是若干个**整数频率**方向正弦的和
    h(x,y) = Σ A_i · sin(2π(kx_i·x + ky_i·y) + φ_i),  kx,ky ∈ 整数
x、y 走满一个 [0,1) 周期时每一项都回到原值 ⇒ 四边天然对齐，
不需要任何"边缘融合"补丁。

为什么频率这样撒：真实水面波高的谱近似无标度（小波多、大波少）。
这里用 `A ∝ k^-1.6` 加权，并把 k 限制在 [3, 42]：
  · k<3 的超长波交给 shader 里已有的 `_SwellAmp` 几何涌，不占贴图精度；
  · k>42 会超过 512² 的采样能力，出来是噪点不是波。

法线由解析导数直接给，不做差分：
    n = normalize(-dh/dx, -dh/dy, 1)
解析导数没有有限差分的高频衰减，小波的法线强度不会被削。
"""
import math
import os
import sys

import numpy as np
from PIL import Image

SIZE = 512
SEED = 20260922
SLOPE_SCALE = float(sys.argv[1]) if len(sys.argv) > 1 else 1.0
OUT = sys.argv[2] if len(sys.argv) > 2 else None

rng = np.random.default_rng(SEED)

# 波数取整数才能无缝；方向任取
KMIN, KMAX = 3, 42
rows = []
n_waves = 260
for _ in range(n_waves):
    k = int(rng.integers(KMIN, KMAX + 1))
    ang = rng.uniform(0.0, math.tau)
    kx = int(round(k * math.cos(ang)))
    ky = int(round(k * math.sin(ang)))
    if kx == 0 and ky == 0:
        continue
    amp = (float(np.hypot(kx, ky)) ** -1.6) * rng.uniform(0.55, 1.45)
    ph = rng.uniform(0.0, math.tau)
    rows.append((kx, ky, amp, ph))

xs = np.arange(SIZE, dtype=np.float64) / SIZE
gx, gy = np.meshgrid(xs, xs)              # gx 沿 x、gy 沿 y

dhx = np.zeros_like(gx)
dhy = np.zeros_like(gx)
for kx, ky, amp, ph in rows:
    w = math.tau * (kx * gx + ky * gy) + ph
    s = np.sin(w)
    dhx += amp * (math.tau * kx) * s
    dhy += amp * (math.tau * ky) * s

dhx *= SLOPE_SCALE
dhy *= SLOPE_SCALE

# ── 幅度归一 ──
# 直接求和出来的斜率量级完全不可控：amp∝k^-1.6 再乘上导数里的 2πk，
# 有效贡献是 k^-0.6，260 个波叠下来 p50 斜率能到 14（法线倾斜 86°，等于糊成一团）。
# 这里统一缩放到**目标 RMS 斜率** 0.18 ≈ 10°，那才是水面毛细波的典型坡度。
TARGET_RMS = 0.18
rms_now = float(np.sqrt(np.mean(dhx * dhx + dhy * dhy)))
norm = TARGET_RMS / max(rms_now, 1e-9)
dhx *= norm
dhy *= norm
SLOPE_SCALE *= norm

# ── 无缝性的**正确**判据 ──
# ⚠ 第一版这里比的是"第 0 列 vs 第 511 列"，那是**相邻两个采样点**，
#   本来就不该相等 —— 判据写错，把一张合格的图判成 FAIL。
#   真正的周期条件是 f(u=1, v) == f(u=0, v)，得把 u=1.0 这个点单独算出来比。
uu = np.linspace(0.0, 1.0, SIZE + 1)
def slope_at(u_vals, v_vals):
    ax = np.zeros((len(v_vals), len(u_vals)))
    ay = np.zeros_like(ax)
    for kx, ky, amp, ph in rows:
        w = math.tau * (kx * u_vals[None, :] + ky * v_vals[:, None]) + ph
        s = np.sin(w)
        ax += amp * (math.tau * kx) * s
        ay += amp * (math.tau * ky) * s
    return ax * SLOPE_SCALE, ay * SLOPE_SCALE

vv = np.arange(SIZE, dtype=np.float64) / SIZE
a_first, b_first = slope_at(np.array([0.0]), vv)
a_last,  b_last  = slope_at(np.array([1.0]), vv)
seam_u = float(max(np.abs(a_first - a_last).max(), np.abs(b_first - b_last).max()))
a_bot, b_bot = slope_at(uu[:SIZE], np.array([0.0]))
a_top, b_top = slope_at(uu[:SIZE], np.array([1.0]))
seam_v = float(max(np.abs(a_bot - a_top).max(), np.abs(b_bot - b_top).max()))

nz = np.sqrt(1.0 + dhx * dhx + dhy * dhy)
nx = -dhx / nz
ny = -dhy / nz
nz = 1.0 / nz

rgb = np.stack([(nx * 0.5 + 0.5), (ny * 0.5 + 0.5), (nz * 0.5 + 0.5)], axis=-1)
rgb = np.clip(rgb * 255.0, 0.0, 255.0).astype(np.uint8)

im = Image.fromarray(rgb, "RGB")

# ── 判据：不靠眼睛 ──
# ① 平均法线要回到 +Z（贴图不能自带整体倾斜，否则水面会朝一个方向歪）
mean_n = rgb.reshape(-1, 3).astype(np.float64).mean(axis=0)
mean_err = float(np.hypot(mean_n[0] - 127.5, mean_n[1] - 127.5))
# ② 斜率分布：给一个可核对的量，避免"看着有细节其实幅度是 0"
slope = np.hypot(dhx, dhy)
print("WRAP   |f(1,v)-f(0,v)|max=%.2e  |f(u,1)-f(u,0)|max=%.2e   (整数波数 ⇒ 应为机器精度)"
      % (seam_u, seam_v))
print("FLAT   mean(R,G)=%.1f,%.1f (期望≈127.5) err=%.2f" % (mean_n[0], mean_n[1], mean_err))
print("SLOPE  p50=%.3f p90=%.3f max=%.3f   waves=%d   最终缩放=%.4f"
      % (np.percentile(slope, 50), np.percentile(slope, 90), slope.max(),
         len(rows), SLOPE_SCALE))
ok = seam_u < 1e-9 and seam_v < 1e-9 and mean_err < 3.0 \
     and 0.10 < float(np.percentile(slope, 90)) < 0.40
print("CRITERIA", "OK" if ok else "FAIL")

if OUT:
    os.makedirs(os.path.dirname(OUT), exist_ok=True) if os.path.dirname(OUT) else None
    im.save(OUT)
    print("WROTE", OUT, os.path.getsize(OUT), "bytes")
else:
    print("（没给输出路径，只做了校验）")
