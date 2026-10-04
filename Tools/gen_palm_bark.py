#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""椰树树干环纹贴图生成器（用户 2026-10-04："树干太光滑，真实的那种树应该有一圈圈的纹理"）。

为什么程序生成：项目总则"素材全部自产，许可证=原创"；环纹是低频结构，
公式足够，不需要照片。

设计（对齐 build_palm.py 的 UV 契约）：
  · 树干 UV: u = 绕干一圈(0..1)，v = 高度×3（一个 v 单位 ≈ 2.2m 干身）
  · 真实椰树叶痕间距约 0.15~0.30m ⇒ 每个 v 单位（2.2m）摆 10 圈环，
    贴图纵向往复 10 组"沟+棱"，v 方向首尾同值 ⇒ Repeat 无缝。
  · u 方向用整数频正弦 ⇒ 绕干一圈天然无缝。
  · 输出的是**亮度乘子**（均值 1.0），材质 _BaseColor(棕) 负责颜色 ——
    这样调色只动材质，不动贴图。

输出：Assets/Art/Textures/Islet/T_PalmBark_Rings.png（512²，sRGB，Repeat）
"""
import math
import random
from PIL import Image

S = 512
RINGS_PER_TILE = 10          # 每个 v 单位的叶痕圈数
random.seed(20261004)

# 每圈环的随机形态参数（沟深/沟宽/棱高），首尾两圈同参保证 v 无缝
rings = []
for k in range(RINGS_PER_TILE):
    rings.append(dict(
        center=(k + random.uniform(0.35, 0.65)) / RINGS_PER_TILE,   # 沟中心 v
        groove=random.uniform(0.10, 0.16) / RINGS_PER_TILE,         # 沟半宽(v)
        depth=random.uniform(0.55, 0.80),                           # 沟暗度
        ridge=random.uniform(0.06, 0.14) / RINGS_PER_TILE,          # 棱半宽
        lift=random.uniform(0.10, 0.22),                            # 棱亮度
        ph=random.uniform(0, 2 * math.pi),                          # 波曲相位
        wob=random.uniform(0.004, 0.010),                           # 波曲幅度(v)
    ))

# 低频纵向色斑（老叶痕的纵向愈合条）：u 方向 2/3/5 频正弦，天然无缝
streaks = [(f, random.uniform(0.04, 0.09), random.uniform(0, 2 * math.pi))
           for f in (2, 3, 5, 8)]

# 细噪声：两倍频叠加，幅度小
noise = [(f, random.uniform(0.015, 0.035), random.uniform(0, 2 * math.pi))
         for f in (16, 23, 31)]

px = Image.new("L", (S, S))
buf = [0.0] * (S * S)
for y in range(S):
    v = y / S
    for x in range(S):
        u = x / S
        lum = 1.0
        # 纵向愈合条（零均值：亮度乘子契约要求均值≈1）
        for f, a, ph in streaks:
            lum -= a * math.sin(2 * math.pi * f * u + ph)
        # 细噪声
        for f, a, ph in noise:
            lum += a * math.sin(2 * math.pi * f * (u * 1.7 + v * 0.6) + ph)
        # 叶痕环：高斯沟 + 紧随的亮棱（真实叶痕是"上棱下沟"）
        for r in rings:
            vc = r["center"] + r["wob"] * math.sin(2 * math.pi * (2.0 * u) + r["ph"])
            dv = v - vc
            # 环绕半周期取最短路径（贴图 v 重复时环不会在接缝断开）
            if dv > 0.5: dv -= 1.0
            if dv < -0.5: dv += 1.0
            g = math.exp(-(dv / r["groove"]) ** 2)
            d = dv + 0.55 * r["groove"] * 2          # 棱在沟上方
            rg = math.exp(-(d / r["ridge"]) ** 2)
            lum -= r["depth"] * g
            lum += r["lift"] * rg
        buf[y * S + x] = lum

# 亮度乘子契约：全场归一到均值 1.0（沟的净变暗由整体电平补偿）
pix = px.load()
mean = sum(buf) / len(buf)
for i in range(len(buf)):
    pix[i % S, i // S] = max(0, min(255, int(255 * max(0.30, min(1.35, buf[i] / mean)))))

px.save(r"Assets/Art/Textures/Islet/T_PalmBark_Rings.png")

# 分布自检（学 rock_dist 的"先验尺子"）：均值≈1、有环峰谷。
# ⚠ 峰谷必须查**归一化前的浮点场**——8-bit L 通道上限恒为 1.0，查像素永远 false。
mn, mx = min(buf) / mean, max(buf) / mean
print(f"T_PalmBark_Rings 512x512  lum mean≈1.0  min={mn:.3f} max={mx:.3f} (pre-clamp)")
assert mx > 1.15 and mn < 0.6, "必须有可读的棱(>1.15)与沟(<0.6)"
print("self-check ok")
