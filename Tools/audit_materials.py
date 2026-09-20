#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
audit_materials.py —— 全场景 / 全材质合理化检测

为什么要有这个脚本：本项目反复栽在同一类问题上，而它们全部是**机器可查**的——
  ① 同一个 (材质, 属性) 被两个菜单写，链尾那个静默赢（《05》C-4：菜单48 设 E28、
     菜单54 又设 E14，我因此连续几轮怀疑"提环境光没用"）
  ①b 其中带"诊断/探针"字样的菜单是有意覆写，只登记
  ② 值只在 .mat 上手调、没回写菜单 ⇒ 下次整链重建必被冲掉（回写铁律）
  ③ SetFloat/SetColor 打在一个 shader 里根本不存在的名字上 ⇒ **静默无操作**
  ⑤ Properties{} 声明了但没进 UnityPerMaterial ⇒ SRP Batcher 判不兼容
  ⑥ 程序化网格资产前缀撞车（Pool*/Pond*/Ocean*）⇒ 互相覆盖且两边都"能渲染"
  ⑦ 场景引用的材质 guid 找不到资产
  ⑧ 属性只在 cbuffer 里、不在 Properties{} 里 ⇒ **不会序列化进 .mat**，
     重开工程后全部归零（海岸线 10 个参数就是这么丢的）
  ⑨ 同一 shader 连同其工程内 include 开了两个 UnityPerMaterial 块
     ⇒ fxc 直接拒绝编译，画面一片品红（把共享 include 改成同名块时踩到的）

用法：python Tools/audit_materials.py [--verbose]
退出码非 0 表示有 ERROR 级问题。
⚠ 本脚本只看源文件与 .mat，**不驱动 Unity 编译**。shader 到底编得过编不过，
   用 Tools/check_shader_errors.sh（它读 Logs/shadercompiler-*.log 的增量）。
"""
import io
import os
import re
import sys
import glob
import collections

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
VERBOSE = '--verbose' in sys.argv

# ── 重建链：菜单号 -> 它所属的那条线（多写入点只在"同一条链"内才算冲突）──
CHAINS = {
    'WaterProject': {'entry': 57, 'range': range(0, 60)},
    'WaterPond':    {'entry': 64, 'range': range(60, 80)},
    'Ocean':        {'entry': 84, 'range': range(80, 90)},
}


def line_of(text, pos):
    return text.count('\n', 0, pos) + 1


# ═══════════════ 1) 解析 shader：Properties / cbuffer / 函数内声明 ═══════════════
TYPE_RE = r'float4x4|float4|float3|float2|half4|half3|half|float|int|uint'


# 第⑤项已人工定性、确认不改的两处。留着条目而不是删掉，是为了下次跑还能看见理由。
KNOWN_5 = {
    'WaterProject/InteractionBlit': ('_ScreenParams_placeholder',
        '全屏 Blit 用的 [HideInInspector] 占位属性，不走 SRP Batcher，无代价'),
    'WaterProject/SkySimpleBlue': ('_GroundColor,_HorizonColor,_ZenithColor',
        'CGPROGRAM + UnityCG.cginc 的内置管线天空盒，只当 RenderSettings.skybox 用，'
        '一帧 1 个 draw，套 cbuffer 没有收益且会牵动 Main/潭两条已验收基线'),
}


def decls(body):
    """从声明串里抠出标识符名。必须吃下数组写法 float4 _Waves[MAX_WAVES];
    —— 早先字符类里没有方括号，于是 _Waves 被当成"没进 cbuffer"，
    第⑤项报了个假阳性。数组本来就是能进 cbuffer 的。"""
    for d in re.finditer(r'(?:%s)\s+([\w,\[\]()\s]+);' % TYPE_RE, body):
        for v in d.group(1).split(','):
            v = re.sub(r'\[.*?\]', '', v).strip()
            if v:
                yield v


def strip_comments(t):
    """去掉 // 行注释和 /* */ 块注释，但双引号里的内容原样保留。

    不剥注释会怎样：OceanShore.hlsl 头部为了"别再试第四种放法"把
    CBUFFER_START(UnityPerMaterial) 这个 token 原话抄进了注释里，
    第⑨项于是把两个海岸 shader 都报成"开了两个块"——假阳性。
    本项目所有声明类检查都应该是**语法**检查，不是字符串出现次数检查。
    """
    out, i, n = [], 0, len(t)
    while i < n:
        c = t[i]
        if c == '"':
            j = i + 1
            while j < n:
                if t[j] == '\\':
                    j += 2
                    continue
                if t[j] == '"':
                    j += 1
                    break
                j += 1
            out.append(t[i:j])
            i = j
        elif t.startswith('//', i):
            j = t.find('\n', i)
            i = n if j < 0 else j          # 保留换行，行号不乱
        elif t.startswith('/*', i):
            j = t.find('*/', i + 2)
            j = n if j < 0 else j + 2
            out.append('\n' * t.count('\n', i, j))
            i = j
        else:
            out.append(c)
            i += 1
    return ''.join(out)


def parse_shader(path):
    t = strip_comments(io.open(path, encoding='utf-8').read())
    name = re.search(r'Shader\s+"([^"]+)"', t)
    props, ptypes = [], {}
    m = re.search(r'Properties\s*\{(.*?)\n\s*\}', t, re.S)
    if m:
        for p in re.finditer(r'(\w+)\s*\(\s*"[^"]*"\s*(?:,\s*(\w+))?\s*[,)]', m.group(1)):
            props.append(p.group(1))
            ptypes[p.group(1)] = p.group(2) or ''
    cbu = []
    for b in re.finditer(r'CBUFFER_START\((\w+)\)(.*?)CBUFFER_END', t, re.S):
        cbu += list(decls(b.group(2)))
    # 全局 uniform（cbuffer 之外）
    outside = re.sub(r'CBUFFER_START\(\w+\).*?CBUFFER_END', '', t, flags=re.S)
    glo = list(decls(outside))
    includes = [os.path.normpath(os.path.join(os.path.dirname(path), i))
                for i in re.findall(r'#include\s+"([^"]+\.hlsl)"', t)
                if not i.startswith('Packages/')]
    return {'path': path, 'name': name.group(1) if name else os.path.basename(path),
            'props': props, 'ptypes': ptypes, 'cbuffer': cbu, 'global': glo,
            'upm': len(re.findall(r'CBUFFER_START\(\s*UnityPerMaterial\s*\)', t)),
            'includes': includes}


SHADERS = {}
GUID2SHADER = {}
for sp in glob.glob(os.path.join(ROOT, 'Assets/**/*.shader'), recursive=True):
    info = parse_shader(sp)
    # 递归展开工程内 include（OceanShore.hlsl 就是靠这个被看到的）
    seen, stack = set(), list(info['includes'])
    while stack:
        ip = stack.pop()
        if ip in seen or not os.path.isfile(ip):
            continue
        seen.add(ip)
        sub = parse_shader(ip)
        info['cbuffer'] += sub['cbuffer']
        info['global'] += sub['global']
        info['ptypes'].update(sub['ptypes'])
        info['upm'] += sub['upm']
        stack += sub['includes']
    SHADERS[info['name']] = info
    meta = sp + '.meta'
    if os.path.isfile(meta):
        g = re.search(r'guid: ([0-9a-f]{32})', io.open(meta, encoding='utf-8').read())
        if g:
            GUID2SHADER[g.group(1)] = info['name']

# ═══════════════ 2) 解析材质 ═══════════════
MATS = {}
for mp in glob.glob(os.path.join(ROOT, 'Assets/**/*.mat'), recursive=True):
    t = io.open(mp, encoding='utf-8').read()
    nm = re.search(r'm_Name:\s*(.+)', t)
    sd = re.search(r'm_Shader:\s*\{fileID: \d+, guid: ([0-9a-f]{32})', t)
    floats, colors, vecs, tex = {}, {}, {}, {}
    # 逐行状态机：Unity 的 m_SavedProperties 段落顺序会变、m_TexEnvs 也可能为空行，
    # 用一个大正则去"抓两段之间"很容易整段漏掉（上一版就是这么误报的）。
    section = None
    for ln in t.split('\n'):
        s = ln.strip()
        if s.startswith('m_Floats:'):
            section = 'f'
            continue
        if s.startswith('m_Colors:'):
            section = 'c'
            continue
        if s.startswith('m_Vector4f:') or s.startswith('m_Ints:'):
            section = 'v' if s.startswith('m_Vector4f') else None
            continue
        if s.startswith('m_TexEnvs:'):
            section = 't'
            continue
        # 贴图槽的内部字段——必须在"m_xxx: 就结束本段"之前处理，
        # 否则第一个 m_Texture: 就把 section 清了，后面的 _BumpMap 全丢
        if s.startswith('m_Texture:') or s.startswith('m_Scale:') \
                or s.startswith('m_Offset:') or s.startswith('- name:'):
            continue
        if re.match(r'm_\w+:', s) or s.startswith('--- '):
            section = None
            continue
        if section == 't':
            # 实际格式是 `- _BaseMap:` 这种键式，不是 `- name: _BaseMap`
            tk = re.match(r'-\s+(\w+):\s*$', s)
            if tk:
                tex[tk.group(1)] = True
            continue
        if not s.startswith('- '):
            continue
        kv = re.match(r'-\s+(\w+):\s*(-?[\d.eE+]+)\s*$', s)
        if kv and section == 'f':
            floats[kv.group(1)] = float(kv.group(2))
            continue
        col = re.match(r'-\s+(\w+):\s*\{(.*)\}\s*$', s)
        if col:
            nums = []
            for part in col.group(2).split(','):
                p = part.strip()
                if ':' in p:
                    p = p.split(':', 1)[1].strip()
                try:
                    nums.append(float(p))
                except ValueError:
                    nums = []
                    break
            if len(nums) in (3, 4):
                tup = tuple(nums) + ((1.0,) if len(nums) == 3 else ())
                if section == 'c':
                    colors[col.group(1)] = tup
                else:
                    vecs[col.group(1)] = tup + (0.0,) * (4 - len(tup))
    guid = sd.group(1) if sd else None
    MATS[os.path.relpath(mp, ROOT).replace('\\', '/')] = {
        'name': (nm.group(1).strip() if nm else os.path.basename(mp)),
        'shader_guid': guid,
        'shader': GUID2SHADER.get(guid, '(未解析 guid %s)' % guid),
        'floats': floats, 'colors': colors, 'vecs': vecs, 'tex': tex}

# ═══════════════ 3) 解析菜单里的 SetXxx，按"材质变量名 -> 材质资产"归属 ═══════════════
# 策略：在每个 [MenuItem] 函数体内找 `XxxMaterial()` / `GetMat("名")` / `GetLitMat("名")`
# 这类构造点，把该函数体内所有 SetFloat/SetColor/SetVector 归到它操作的材质上。
MENU_RE = re.compile(r'\[MenuItem\("Tools/(\w+)/(\d+)\s+([^"]*)"\)\]')
SET_RE = re.compile(r'(\w+)\.(Set(?:Float|Color|Vector|Int|Texture)?\w*)\(\s*"(\w+)"\s*,(.*?)[;,\n]', re.S)

writers = collections.defaultdict(list)   # (matPath, prop) -> [(menu, line, value)]
dead_targets = []                        # Set 到一个 shader 里不存在的属性

for cs in glob.glob(os.path.join(ROOT, 'Assets/Editor/*.cs')):
    src = io.open(cs, encoding='utf-8').read()
    lines = src.split('\n')
    # 切分成"方法块"：按 MenuItem 起点到下一个 MenuItem / 文件尾
    marks = [(m.start(), m.group(1), m.group(2), m.group(3))
             for m in MENU_RE.finditer(src)]
    # 非菜单的私有 helper（材质构造函数等）也算块起点
    helper = [(m.start(), 'helper', '', m.group(1))
              for m in re.finditer(r'\bstatic\s+(?:Material|void|Mesh|int|bool|UnityEngine\.GameObject)\s+(\w+)\s*\(', src)]
    # 但"紧跟在某个 MenuItem 后面的 static 方法"就是该菜单自己的方法声明，
    # 再切一刀会把方法体归给没有链号的 helper 块，菜单号就丢了
    helper = [h for h in helper
              if not any(mk[0] < h[0] and h[0] - mk[0] < 300 for mk in marks)]
    blocks = sorted(marks + helper)
    for i, (start, chain, num, label) in enumerate(blocks):
        end = blocks[i + 1][0] if i + 1 < len(blocks) else len(src)
        body = src[start:end]
        # ── 材质归属：轻量数据流，不靠"块里出现过哪个 .mat 名字" ──
        # 同一个方法常常还会 LoadAssetAtPath 别的材质（例如菜单34 在同一个方法里
        # 操作内置 Default-Skybox），按名字猜会把 SetColor 记到错误的材质上。
        bind = {}
        # 本块自己在构造哪个材质（从方法名 XxxMaterial 反推，或从块内唯一的 .mat 字面量）
        own = None
        hm = re.match(r'\s*(?:static\s+)?Material\s+(\w+)\s*\(', body)
        if hm:
            key = hm.group(1).lower().replace('material', '').replace('_', '')
            cand = [p for p in MATS
                    if MATS[p]['name'].lower().replace('_', '') in key]
            if len(cand) == 1:
                own = cand[0]
        for b in re.finditer(
                r'(?:var|Material)\s+(\w+)\s*=\s*([^;\n]*?)(?:;|\n)', body):
            var, rhs = b.group(1), b.group(2)
            m2 = re.search(r'Get(?:Lit)?Mat\(\s*"(\w+)"', rhs) \
                or re.search(r'/(\w+)\.mat"', rhs)
            if m2:
                cand = [p for p in MATS if MATS[p]['name'] == m2.group(1)]
                if cand:
                    bind[var] = cand[0]
            elif re.search(r'\w+[Mm]aterial\s*\(\s*\)', rhs) and own:
                bind[var] = own          # `var m = OceanWaterMaterial();`
        # helper 形参叫 mat/m 的，由调用点决定，这里不猜 —— 宁可漏报也不误报
        for m in SET_RE.finditer(body):
            var = m.group(1)
            prop = m.group(3)
            val = ' '.join(m.group(4).split())[:46]
            ln = line_of(src, start + m.start())
            tag = '%s/%s %s' % (chain, num, label) if num else label
            matpath = bind.get(var)          # 已经是 MATS 的键（资产路径）
            if not matpath or matpath not in MATS:
                continue
            writers[(matpath, prop)].append((tag, ln, val))
            sh = MATS[matpath]['shader']
            if sh in SHADERS:
                known = set(SHADERS[sh]['props']) | set(SHADERS[sh]['cbuffer']) \
                    | set(SHADERS[sh]['global'])
                if prop not in known:
                    dead_targets.append((matpath, prop, tag, ln))

# ═══════════════ 4) 报告 ═══════════════
E = W = I = 0


def head(s):
    print('\n' + '=' * 78 + '\n' + s + '\n' + '=' * 78)


def fmt(v):
    if isinstance(v, tuple):
        return '(' + ', '.join('%.4g' % x for x in v) + ')'
    if isinstance(v, float):
        return '%.6g' % v
    return str(v)


# ④ 未序列化：菜单写了、但 .mat 里没有
head('④ 菜单写了、但 .mat 里查不到该属性 ⇒ 重开工程后归零（最危险的一类）')
unser = collections.defaultdict(set)
for (mp, prop), ws in writers.items():
    m = MATS[mp]
    if (prop not in m['floats'] and prop not in m['colors']
            and prop not in m['vecs'] and prop not in m['tex']
            and prop + '_ST' not in m['vecs']):
        unser[mp].add((prop, ws[0][0]))
for mp in sorted(unser):
    print('  %s  [shader %s]' % (mp, MATS[mp]['shader']))
    for prop, menu in sorted(unser[mp]):
        print('      %-16s <- %s' % (prop, menu))
    E += 1

# ③ 打到不存在的属性上
head('③ SetXxx 的目标在 shader 里根本不存在 ⇒ 静默无操作')
seen = set()
for mp, prop, tag, ln in dead_targets:
    k = (mp, prop)
    if k in seen:
        continue
    seen.add(k)
    print('  %s .%s  <- %s (%d 行)' % (mp, prop, tag, ln))
    W += 1
if not seen:
    print('  (无)')

# ── 解析每条重建链的菜单顺序，用来标出"链尾谁赢" ──
# 键用 MenuItem 标签里的 chain 名（Tools/WaterProject/... 里的 WaterProject），
# 这样写入者标签 "WaterProject/48 xxx" 可以直接反查。
CHAIN_ORDER = {}
for cs in glob.glob(os.path.join(ROOT, 'Assets/Editor/*.cs')):
    src = io.open(cs, encoding='utf-8').read()
    entry = re.search(r'\[MenuItem\("Tools/(\w+)/(\d+)\s+[^"]*整链', src)
    if not entry:
        continue
    chain = entry.group(1)
    i = src.index(entry.group(0))
    nxt = src.find('[MenuItem(', i + 10)
    body = src[i:nxt if nxt > 0 else len(src)]
    # 链体里每一步写成 KeyValuePair("60 骨架", BuildPondShell) 这样的字面量
    order = {int(n): pos for pos, n in enumerate(re.findall(r'"(\d{2})\s', body))}
    if order:
        CHAIN_ORDER[chain] = order


def rank_of(tag):
    """菜单在该链中的步序；越小越先跑，越大的赢。取不到返回 None。"""
    m = re.match(r'(\w+)/(\d{2})\s', tag)
    if not m:
        return None
    return CHAIN_ORDER.get(m.group(1), {}).get(int(m.group(2)))

# ① 多写入点
head('① 同一 (材质, 属性) 被多个【正式】菜单写 ⇒ 链尾静默胜出（真 bug 类）')
DIAG_RE = re.compile(r'诊断|探针|调试|标记|归位|红盒')
real_multi, diag_multi = [], []
for (mp, prop), ws in sorted(writers.items()):
    tags = {w[0] for w in ws}
    if len(tags) <= 1:
        continue
    formal = {x for x in tags if not DIAG_RE.search(x)}
    (diag_multi if len(formal) <= 1 else real_multi).append((mp, prop, ws))
for mp, prop, ws in real_multi:
    print('  %s .%s' % (mp, prop))
    for tag, ln, val in ws:
        r = rank_of(tag)
        print('      %-32s 链内第 %s 步  L%-5d %s'
              % (tag, r if r is not None else '?', ln, val))
    rk = [(t, rank_of(t)) for t in sorted({w[0] for w in ws})]
    known = [(t, r) for t, r in rk if r is not None]
    if known:
        top = max(r for _, r in known)
        print('      → 按链序赢的是: %s' % ', '.join(t for t, r in known if r == top))
if not real_multi:
    print('  (无)')

head('①b 含诊断/探针菜单的覆写（有意为之，跑完整链即恢复，仅登记）')
for mp, prop, ws in diag_multi:
    print('  %-46s .%-16s  写入者 %d 个' % (mp, prop, len({w[0] for w in ws})))
if not diag_multi:
    print('  (无)')

# ② 磁盘值 != 菜单写的值
head('② .mat 磁盘值 与 菜单写入值 不一致 ⇒ 下次整链重建会静默改回')
shp = re.compile(r'new Color\(([^)]*)\)')
shv = re.compile(r'new Vector\(([^)]*)\)')
shf = re.compile(r'^([\d.]+)f?$')
drift = 0
for (mp, prop), ws in sorted(writers.items()):
    m = MATS[mp]
    disk = m['floats'].get(prop, m['colors'].get(prop, m['vecs'].get(prop)))
    if disk is None:
        continue
    tag, ln, val = ws[-1]
    c = shp.search(val)
    v = shv.search(val)
    f = shf.match(val.strip())
    want = None
    if c:
        want = tuple(float(x.rstrip('f')) for x in c.group(1).split(','))
        want = want + (1.0,) * (4 - len(want))
    elif v:
        want = tuple(float(x.rstrip('f')) for x in v.group(1).split(','))
        want = want + (0.0,) * (4 - len(want))
    elif f:
        want = float(f.group(1))
    if want is None:
        continue
    if isinstance(want, float):
        same = isinstance(disk, float) and abs(disk - want) < 1e-4
    else:
        same = isinstance(disk, tuple) and len(disk) == len(want) \
            and all(abs(a - b) < 1e-4 for a, b in zip(disk, want))
    if not same:
        drift += 1
        print('  %s .%s\n      磁盘 %-34s 菜单 %-34s  (%s L%d)'
              % (mp, prop, fmt(disk), fmt(want), tag, ln))
if not drift:
    print('  (无)')

# ⑤ Properties 未进 cbuffer（贴图/数组本来就不能进 cbuffer，排除掉才是真信号）
head('⑤ Properties{} 声明了但没进任何 CBUFFER ⇒ SRP Batcher 判不兼容')
for name, s in sorted(SHADERS.items()):
    if not s['props']:
        continue
    cb = set(s['cbuffer']) | set(s['global'])
    miss = [p for p in s['props']
            if p not in cb and s['ptypes'].get(p) not in ('2D', 'Cube', 'Int')]
    if not miss:
        continue
    known = KNOWN_5.get(name)
    if known and set(miss) == set(known[0].split(',')):
        print('  %-34s %-42s 已定性：%s' % (name, ', '.join(sorted(miss)), known[1]))
        continue
    print('  %-34s %s' % (name, ', '.join(sorted(set(miss)))))
    I += 1

# ⑥ 跨链网格路径冲突（这才是历史事故的真实形态）
head('⑥ 两条重建链往【同一个网格资产路径】写 ⇒ 后跑的静默换掉前一个的几何')
FILE2CHAIN = {'WaterProjectSetup.cs': 'Main', 'WaterPondSetup.cs': 'Pond',
              'WaterOceanSetup.cs': 'Ocean', 'TreeAssetSetup.cs': 'Trees'}
mesh_owners = collections.defaultdict(set)
for cs in glob.glob(os.path.join(ROOT, 'Assets/Editor/*.cs')):
    ch = FILE2CHAIN.get(os.path.basename(cs), os.path.basename(cs))
    src = io.open(cs, encoding='utf-8').read()
    for mm in re.finditer(r'MeshFolder\s*\+\s*"/([\w\-]+)\.asset"', src):
        mesh_owners[mm.group(1)].add(ch)
    for mm in re.finditer(r'Art/Meshes/([\w\-]+)\.asset', src):
        mesh_owners[mm.group(1)].add(ch)
clash = {k: v for k, v in mesh_owners.items() if len(v) > 1}
if clash:
    for k, v in sorted(clash.items()):
        print('  ! %-24s 被 %s 两条链同时写' % (k, ' + '.join(sorted(v))))
        E += 1
else:
    print('  (无跨链重名)')
pref = collections.defaultdict(list)
for a in glob.glob(os.path.join(ROOT, 'Assets/Art/Meshes/*.asset')):
    b = os.path.basename(a)[:-6]
    key = next((k for k in ('Pool', 'Pond', 'Ocean', 'Sea', 'Beach') if b.startswith(k)), '(其它)')
    pref[key].append(b)
print('\n  前缀分布（附带信息）：')
for k in sorted(pref):
    print('    %-8s %2d 个: %s' % (k, len(pref[k]), ', '.join(sorted(pref[k])[:10])
                                   + (' …' if len(pref[k]) > 10 else '')))

# ⑧ cbuffer 有、Properties 没有 ⇒ 不会序列化进 .mat（本次检测的核心一条）
head('⑧ CBUFFER 里声明了、但 Properties{} 里没有 ⇒ SetFloat 写了也不落盘，重开工程归零')
crit = 0
for name, s in sorted(SHADERS.items()):
    if not s['props']:
        continue
    cb = set(s['cbuffer'])
    un = sorted(x for x in cb - set(s['props'])
                if not (x.endswith('_ST') and x[:-3] in s['props']))
    if un:
        print('  %-34s %s' % (name, ', '.join(un)))
        crit += 1
if not crit:
    print('  (无)')
else:
    print('\n  说明：这类 uniform 只存在于 cbuffer，Unity 的材质序列化器只写 Properties{}')
    print('  里列出的项。菜单 SetFloat 在**当前会话**里生效，所以看起来一切正常；')
    print('  重开工程 / 域重载后全部退回默认值。要么补进 Properties{}，要么改成')
    print('  每次加载时由菜单重设（后者需要 [InitializeOnLoad]，本项目没有）。')

# ⑦ 场景引用完整性
head('⑦ 场景引用的材质 guid 是否都存在')
guid2mat = {}
for mp in MATS:
    g = re.search(r'guid: ([0-9a-f]{32})', io.open(os.path.join(ROOT, mp + '.meta'),
                                                   encoding='utf-8').read())
    if g:
        guid2mat[g.group(1)] = mp
missing = 0
for sc in glob.glob(os.path.join(ROOT, 'Assets/Scenes/*.unity')):
    t = io.open(sc, encoding='utf-8').read()
    refs = set(re.findall(r'm_Materials:.*?guid: ([0-9a-f]{32})', t, re.S))
    refs |= set(re.findall(r'm_Shader: \{fileID: \d+, guid: ([0-9a-f]{32})', t))
    bad = [r for r in refs if r not in guid2mat and r not in GUID2SHADER]
    print('  %-40s 材质引用 %2d 个，未解析 %d' % (os.path.basename(sc), len(refs), len(bad)))
    for b in bad[:8]:
        print('      ! guid %s 在 Assets 下找不到对应资产（可能是包内/内置）' % b)
    missing += len(bad)

# ⑨ 一个编译单元里开了两个 UnityPerMaterial ⇒ fxc 直接拒绝编译
head('⑨ 同一 shader（含其工程内 include）出现多个 UnityPerMaterial 块 ⇒ 编译必失败')
dup = 0
for name, s in sorted(SHADERS.items()):
    if s['upm'] > 1:
        print('  ! %-34s 开了 %d 个 UnityPerMaterial 块' % (name, s['upm']))
        dup += 1
if dup:
    E += dup
    print('\n  fxc/DXC 报 "Duplicate constant buffer declaration: UnityPerMaterial"，')
    print('  整个 shader 编译失败，画面上一片品红（255,0,255）。同名 cbuffer 不会被合并。')
    print('  共享 include 只有两种正确写法：')
    print('    · 什么 uniform 都不声明，靠宿主先声明再 include（本项目 OceanShore.hlsl）；')
    print('    · 用**另一个名字**，如 URP 自己的 UnityBuiltinPerMaterial。')
    print('  注意第二种会牺牲 SRP Batcher 兼容性，见第⑤项。')
else:
    print('  (无)')

head('汇总')
print('  ERROR %d   WARN %d   INFO %d' % (E, W, I))
print('  材质 %d 个 / 自定义 shader %d 个 / 菜单写入点 %d 处'
      % (len(MATS), len(SHADERS), sum(len(v) for v in writers.values())))
sys.exit(1 if E else 0)
