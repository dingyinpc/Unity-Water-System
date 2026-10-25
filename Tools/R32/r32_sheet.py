# -*- coding: utf-8 -*-
# r32_sheet.py — 拼「参考帧(上排) vs 改前 vs 改后(下排)」对照图
# 用法: python Tools/R32/r32_sheet.py <out.png> <ref1.jpg> [<ref2.jpg> ...] --ours <before.png> <after.png> [<label>]
# 中文路径一律 cv2.imdecode/imencode 处理（AGENTS 铁律 2）
import sys, os, cv2, numpy as np

def rd(p):
    d = np.fromfile(p, np.uint8)
    img = cv2.imdecode(d, cv2.IMREAD_COLOR)
    if img is None: raise SystemExit(f"read fail: {p}")
    return img

def wr(p, img):
    ok, buf = cv2.imencode('.png', img)
    if not ok: raise SystemExit("encode fail")
    buf.tofile(p)

def label(img, text):
    bar = np.zeros((34, img.shape[1], 3), np.uint8)
    cv2.putText(bar, text, (10, 24), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255,255,255), 2, cv2.LINE_AA)
    return np.vstack([bar, img])

def main():
    args = sys.argv[1:]
    ours_i = args.index("--ours")
    out_path = args[0]
    refs = args[1:ours_i]
    rest = args[ours_i+1:]
    before, after = rest[0], rest[1]
    tag = rest[2] if len(rest) > 2 else "after"

    W = 1280
    rows = []
    top = [label(rd(r), f"REF {os.path.basename(r)}") for r in refs]
    bot = [label(rd(before), "OURS before"), label(rd(after), f"OURS {tag}")]
    def scale_row(imgs):
        scaled = []
        for im in imgs:
            h = int(im.shape[0] * W / im.shape[1])
            scaled.append(cv2.resize(im, (W, h), interpolation=cv2.INTER_AREA))
        h = max(x.shape[0] for x in scaled)
        w = sum(x.shape[1] for x in scaled) + 8 * len(scaled)
        row = np.zeros((h, w, 3), np.uint8)
        x = 0
        for s in scaled:
            row[:s.shape[0], x:x+W] = s; x += W + 8
        return row
    rows.append(scale_row(top))
    rows.append(scale_row(bot))
    mw = max(r.shape[1] for r in rows)      # 单参考帧时行宽不齐 → 右侧补黑对齐
    rows = [np.hstack([r, np.zeros((r.shape[0], mw - r.shape[1], 3), np.uint8)]) for r in rows]
    out = np.vstack(rows)
    wr(out_path, out)
    print("sheet:", out_path, out.shape)

if __name__ == "__main__":
    main()
