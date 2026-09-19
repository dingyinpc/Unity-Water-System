// 审计：菜单52 的新盆底/台地改变后，石头与荷叶是否还贴合地面
// 用法: node Tools/audit_basin_placement.mjs [probePath]
import fs from 'node:fs';

const path = process.argv[2] || 'Logs/water_probe.txt';
const raw = fs.readFileSync(path, 'binary').replace(/\u0000/g, '');

// ---- 与 WaterProjectSetup.cs 菜单52 / ShoreR 保持一致 ----
const Depth = 1.30, Rim = 0.02, Pow = 1.55;
const hash11 = v => { const x = Math.sin(v * 12.9898) * 43758.5453; return x - Math.floor(x); };
const wobA = (th, seed) =>
  0.050 * Math.sin(2 * th + hash11(seed * 3.1) * 6.2832) +
  0.032 * Math.sin(5 * th + hash11(seed * 7.7) * 6.2832) +
  0.019 * Math.sin(9 * th + hash11(seed * 13.3) * 6.2832);
const shoreR = th => {
  const w1 = 0.88 + 0.12 * Math.sin(th * 2 + 1.3);
  const w2 = 0.09 * Math.sin(th * 4 + 0.6);
  const w3 = 0.07 * Math.sin(th * 7 + 2.4);
  return Math.max(14, 20.5 * (w1 + w2 + w3));
};
const clamp01 = t => Math.max(0, Math.min(1, t));

// 盆底 + 三圈台地的合成地面高度
const bands = [[0.34, 0.46], [0.56, 0.68], [0.76, 0.88]];
function groundAt(x, z) {
  const r = Math.hypot(x, z);
  const th = Math.atan2(z, x);
  const rEdge = Math.max(12, shoreR(th) * 0.6) - 0.05;
  const t = clamp01((r - 0.3) / (rEdge - 0.3));
  const te = clamp01(t + wobA(th, 1.7 + t * 2) * 0.85);
  let y = -Depth + (Depth + Rim) * Math.pow(te, Pow);
  for (let k = 0; k < bands.length; k++) {
    if (t >= bands[k][0] && t <= bands[k][1]) {
      const tb = clamp01(t + wobA(th, 1.7 + t * 2) * 0.85);
      y = Math.max(y, -Depth + (Depth + Rim) * Math.pow(tb, Pow) + 0.055 + 0.075 * tb);
    }
  }
  return { y, r, rEdge, t, outside: r > rEdge };
}

// ---- 解析探针里最后一整块渲染器列表 ----
const lines = raw.split('\n');
const start = lines.reduce((acc, l, i) => (/renderers reaching/.test(l) ? i : acc), -1);
if (start < 0) { console.error('probe block not found'); process.exit(1); }

const re = /^(\S+)\s+c=\(\s*(-?[\d.]+),\s*(-?[\d.]+),\s*(-?[\d.]+)\)\s+ext=\(\s*(-?[\d.]+),\s*(-?[\d.]+),\s*(-?[\d.]+)\)/;
const objs = [];
for (let i = start; i < lines.length; i++) {
  const m = lines[i].match(re);
  if (!m) continue;
  objs.push({
    name: m[1], cx: +m[2], cy: +m[3], cz: +m[4], ex: +m[5], ey: +m[6], ez: +m[7],
  });
}

const want = /^(Boulder_|SmallLily_|SmallLotus_|SandBlob_)/;
const rows = objs.filter(o => want.test(o.name));

let float = [], buried = [], onShore = [];
for (const o of rows) {
  const g = groundAt(o.cx, o.cz);
  const bottom = o.cy - o.ey, top = o.cy + o.ey;
  const clearance = bottom - g.y;
  const rec = { name: o.name, r: g.r.toFixed(1), t: g.t.toFixed(2), ground: g.y.toFixed(2), bottom: bottom.toFixed(2), top: top.toFixed(2), clr: clearance.toFixed(2), outside: g.outside };
  if (g.outside) onShore.push(rec);
  else if (clearance > 0.14) float.push(rec);
  else if (top < g.y - 0.02) buried.push(rec);
}

const show = arr => arr.forEach(r => console.log(
  `  ${r.name.padEnd(18)} r=${String(r.r).padStart(5)} t=${r.t} ground=${String(r.ground).padStart(6)} bot=${String(r.bottom).padStart(6)} top=${String(r.top).padStart(6)} clr=${String(r.clr).padStart(6)}`));

console.log(`objects parsed: ${rows.length} (Boulder/Lily/Lotus/SandBlob)`);
console.log(`\n--- 悬空（底面高于地面 >0.14m）: ${float.length} ---`); show(float);
console.log(`\n--- 完全埋进盆底: ${buried.length} ---`); show(buried);
console.log(`\n--- 在水面范围外（落在干沙滩上，r>rEdge）: ${onShore.length} ---`); show(onShore);
