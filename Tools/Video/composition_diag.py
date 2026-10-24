# composition_diag.py — R24 构图轮诊断：分段径向谱比值 + 近黑占比 + 三分区亮度
#
# 回答三个问题（每场景 vs 各自参考帧）：
#   ① 低频超额在哪一段（r 10~160 = 大尺度构图，320+ = 颗粒/细节）；
#   ② 近黑(0-40)占比差距（R08 结论：参考 36%，我们 4~7%，缺大暗块）；
#   ③ 画面上/中/下三带的亮度结构差（定位"哪里太亮"）。
# 用法：python composition_diag.py --still <png> --ref <frameNo> [--tag 名字]
import argparse, sys
import cv2
import numpy as np


def load_ours(p):
    return cv2.imdecode(np.fromfile(p, np.uint8), cv2.IMREAD_GRAYSCALE).astype(np.float32)


def load_ref(n):
    p = f"Docs/视频逐帧/f1083870/frames/f{n:06d}.jpg"
    return cv2.imdecode(np.fromfile(p, np.uint8), cv2.IMREAD_GRAYSCALE).astype(np.float32)


def radial_profile(img):
    f = np.fft.fftshift(np.fft.fft2(img - img.mean()))
    p = np.abs(f) ** 2
    h, w = img.shape
    y, x = np.indices((h, w))
    r = np.sqrt((x - w / 2) ** 2 + (y - h / 2) ** 2).astype(int)
    tbin = np.bincount(r.ravel(), p.ravel())
    nr = np.bincount(r.ravel())
    return tbin / np.maximum(nr, 1)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--still", required=True)
    ap.add_argument("--ref", type=int, required=True)
    ap.add_argument("--tag", default="")
    a = ap.parse_args()

    ours, ref = load_ours(a.still), load_ref(a.ref)
    for name, img in (("我方", ours), ("参考", ref)):
        pass
    po, pr = radial_profile(ours), radial_profile(ref)
    tag = a.tag or a.still
    print(f"== {tag} (ref f{a.ref:06d}) ==")
    bands = [(10, 20), (20, 40), (40, 80), (80, 160), (160, 320), (320, 540)]
    line = "  谱段比(我/参): "
    for lo, hi in bands:
        ratio = np.sqrt(po[lo:hi].mean() / max(pr[lo:hi].mean(), 1e-9))
        line += f"r{lo}-{hi}={ratio:.2f} "
    print(line)
    for name, img in (("我方", ours), ("参考", ref)):
        dark40 = float((img < 40).mean()) * 100
        dark60 = float((img < 60).mean()) * 100
        h = img.shape[0]
        thirds = [img[: h // 3], img[h // 3 : 2 * h // 3], img[2 * h // 3 :]]
        tm = "/".join(f"{t.mean():.0f}" for t in thirds)
        print(f"  {name}: 近黑40={dark40:.1f}% 近黑60={dark60:.1f}% mean={img.mean():.0f} "
              f"上/中/下={tm}")


if __name__ == "__main__":
    main()
