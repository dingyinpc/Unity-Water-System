# gen_splash_wav.py — 程序化合成水花音效（零外部素材）
# 结构 = 白噪声"哗"（快攻指数衰减）+ 低频"咚"（扫频正弦，球/人入水的腔音）
# 16bit 单声道 44.1k，0.9s，峰值归一 0.85
import wave, struct, math, random

SR = 44100
DUR = 0.9
N = int(SR * DUR)

random.seed(20261005)
samples = []
lp = 0.0
for i in range(N):
    t = i / SR
    # 白噪声"哗"：60ms 快攻 + 指数衰减 6.5/s
    env = math.exp(-t * 6.5) * min(1.0, t * 60.0)
    n = random.uniform(-1, 1)
    lp += 0.35 * (n - lp)                      # 一阶低通，越往后越闷
    splash = lp * env
    # 低频"咚"：180→80Hz 下滑正弦，衰减 9/s（入水腔音）
    f = 180.0 - 100.0 * (t / DUR)
    thump = math.sin(2 * math.pi * f * t) * math.exp(-t * 9.0) * 0.5
    samples.append(splash * 0.75 + thump * 0.45)

peak = max(abs(min(samples)), max(samples))
scale = 0.85 / peak

out = r"E:\Unity xianmu\Unity3D风景的项目立项\Assets\Audio\Pool_Splash.wav"
import os
os.makedirs(os.path.dirname(out), exist_ok=True)
with wave.open(out, "wb") as w:
    w.setnchannels(1)
    w.setsampwidth(2)
    w.setframerate(SR)
    w.writeframes(b"".join(
        struct.pack("<h", int(max(-1.0, min(1.0, v * scale)) * 32767))
        for v in samples))
print("wav written:", out, N, "frames, peak", round(peak, 3))
