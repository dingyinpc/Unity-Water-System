"""把巨岩那块裁出来放大，并逐列打印色相 —— 分清"条纹是材质"还是"条纹是法线"。

判据：
  · 若各竖条 **色相(H)差很大** ⇒ 是**材质槽**被按经线切开了（建模/导入侧的锅）。
  · 若各竖条 **色相几乎一致、只有明度(V)差** ⇒ 才是**法线/受光**的锅。
"""
import sys
import colorsys
from PIL import Image

im = Image.open(sys.argv[1]).convert("RGB")
W, H = im.size
x0, y0, x1, y1 = [int(v) for v in sys.argv[2:6]]
crop = im.crop((x0, y0, x1, y1))
crop.resize((crop.width * 4, crop.height * 4), Image.NEAREST).save(
    "Docs/screenshots/_crop_monolith.png")

px = im.load()
n = 10
print("逐列（左→右均分 %d 段）中位 H/S/V 与亮度：" % n)
for k in range(n):
    a = x0 + (x1 - x0) * k // n
    b = x0 + (x1 - x0) * (k + 1) // n
    cols = []
    for x in range(a, b):
        for y in range(y0, y1):
            r, g, bl = px[x, y]
            cols.append((r, g, bl))
    cols.sort(key=lambda c: 0.299 * c[0] + 0.587 * c[1] + 0.114 * c[2])
    m = cols[len(cols) // 2]
    h, s, v = colorsys.rgb_to_hsv(m[0] / 255.0, m[1] / 255.0, m[2] / 255.0)
    print("  段%d x[%3d,%3d)  rgb%3d,%3d,%3d   H=%4.1f  S=%4.2f  V=%4.2f"
          % (k, a, b, m[0], m[1], m[2], h * 360.0, s, v))
print("放大图 -> Docs/screenshots/_crop_monolith.png")
