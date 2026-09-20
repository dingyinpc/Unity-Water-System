"""按区域平均采样一张截图，用来判断天空/云/海/滩的亮度关系。

用法: python Tools/sample_regions.py <png> [x0,y0,x1,y1 ...]
不给区域时给一组默认（比例坐标，适配任意分辨率）。
"""
import sys
from PIL import Image

DEFAULT = [
    ("sky_top_left",     0.05, 0.05, 0.25, 0.15),
    ("sky_top_right",    0.75, 0.05, 0.95, 0.15),
    ("sky_mid",          0.35, 0.22, 0.65, 0.32),
    ("sky_low_horizon",  0.30, 0.40, 0.70, 0.46),
    ("sea_far",          0.30, 0.50, 0.70, 0.55),
    ("sea_mid",          0.20, 0.60, 0.80, 0.68),
    ("surf_line",        0.10, 0.70, 0.90, 0.76),
    ("beach_wet",        0.20, 0.82, 0.80, 0.88),
    ("beach_dry",        0.20, 0.92, 0.80, 0.99),
]


def main():
    path = sys.argv[1]
    im = Image.open(path).convert("RGB")
    w, h = im.size
    px = im.load()
    print(f"{path}  {w}x{h}")
    regions = DEFAULT
    if len(sys.argv) > 2:
        a = list(map(float, sys.argv[2:]))
        regions = [(f"r{i}", a[i * 4], a[i * 4 + 1], a[i * 4 + 2], a[i * 4 + 3])
                   for i in range(len(a) // 4)]
    for name, x0, y0, x1, y1 in regions:
        X0, Y0, X1, Y1 = int(x0 * w), int(y0 * h), int(x1 * w), int(y1 * h)
        n = sr = sg = sb = 0
        for y in range(Y0, max(Y0 + 1, Y1)):
            for x in range(X0, max(X0 + 1, X1)):
                r, g, b = px[x, y]
                sr += r; sg += g; sb += b; n += 1
        if n == 0:
            continue
        mx = max(sr, sg, sb) / n
        print(f"  {name:<18} ({sr/n:6.1f},{sg/n:6.1f},{sb/n:6.1f})  lum={0.2126*sr/n+0.7152*sg/n+0.0722*sb/n:6.1f}")


if __name__ == "__main__":
    main()
