#!/usr/bin/env python3
"""灌木颜色探针：用**颜色无关**的植物掩码量正常渲染下的叶片分布。

为什么要有这个脚本：上一轮我把"绿植太锐/太白"归因成"该暗一点"，用的掩码是
`g>r and g>b` —— 而"压暗"恰好会让像素掉出这个掩码。量具跟着被测物一起变，
于是那次改动的效果被掩码自己吃掉了（灌木在画面里消失，指标却几乎不动）。
这版的掩码来自 `Tree_Leaf.shader` 的 `_Dbg=5` 常亮品红通道（它在 MixFog 之前
return，所以不受雾影响），只跟"这个像素有没有植物片元赢下深度测试"有关。

掩码一次定死、之后复用：只有**几何/风参数**变了才要重出 on 帧；
改颜色不该让掩码变化 —— 这正是它存在的理由。

用法：
  python Tools/foliage_probe.py mask      <on.png> <out_npy>            # 定掩码
  python Tools/foliage_probe.py measure   <off.png> <npy> [标签]         # 量分布
"""
import sys
import numpy as np
from PIL import Image

# vegbox 由菜单99 从场景数据算出（不是从截图上画的框 —— 那个错已经踩过三次）
VEGBOX = (0.558, 0.418, 0.858, 0.552)

# 参考帧实测的"岛上绿色像素"分布，和色标卡上的目标值，放一起当两把尺
REF = {'p25': (0, 92, 20), 'med': (34, 128, 23), 'p75': (139, 182, 96)}
PAL = {'lit': (50, 112, 71), 'dark': (26, 80, 45)}


def crop(img):
    a = np.asarray(Image.open(img).convert('RGB')).astype(int)
    h, w, _ = a.shape
    x0, y0, x1, y1 = VEGBOX
    return a[int(y0 * h):int(y1 * h), int(x0 * w):int(x1 * w)]


def do_mask(on_png, out_npy):
    a = crop(on_png)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    m = (r > 120) & (b > 120) & (g < r * 0.55) & (g < b * 0.55)
    np.save(out_npy, m)
    print('掩码 %s  shape=%s  占vegbox %.1f%%' % (out_npy, m.shape, 100 * m.mean()))


def do_measure(off_png, npy, tag):
    a = crop(off_png)
    m = np.load(npy)
    if m.shape != a.shape[:2]:
        sys.exit('掩码形状 %s 与图 %s 不符 —— 重出掩码' % (m.shape, a.shape[:2]))
    P = a[m]
    q = lambda p: tuple(int(round(np.percentile(P[:, i], p))) for i in range(3))
    lum = P.mean(1)
    clip = (P[:, 1] >= 254).mean()
    near_black = (lum < 25).mean()
    print('[%s] n=%d  p25%s 中位%s p75%s  p10%s p90%s' %
          (tag, len(P), q(25), q(50), q(75), q(10), q(90)))
    print('     亮度 p25/中位/p75=%.0f/%.0f/%.0f   G_clip=%.1f%%  近黑=%.1f%%  饱和中位=%.3f' %
          (np.percentile(lum, 25), np.median(lum), np.percentile(lum, 75),
           100 * clip, 100 * near_black,
           np.median((P.max(1) - P.min(1)) / np.maximum(P.max(1), 1))))
    print('     参考         p25(0,92,20) 中位(34,128,23) p75(139,182,96)')
    print('     色标 lit(50,112,71) dark(26,80,45)')
    # 偏离量按"该往哪走"给，不给加权总分：总分藏方向
    d = np.array(q(50)) - np.array(REF['med'])
    print('     中位-参考中位 = (%+d,%+d,%+d)   R/G=%.2f (参考 %.2f)' %
          (d[0], d[1], d[2], q(50)[0] / max(q(50)[1], 1), REF['med'][0] / REF['med'][1]))


if __name__ == '__main__':
    if len(sys.argv) < 4:
        print(__doc__)
        sys.exit(1)
    cmd = sys.argv[1]
    if cmd == 'mask':
        do_mask(sys.argv[2], sys.argv[3])
    elif cmd == 'measure':
        do_measure(sys.argv[2], sys.argv[3], sys.argv[4] if len(sys.argv) > 4 else 'run')
    else:
        sys.exit('未知命令 ' + cmd)
