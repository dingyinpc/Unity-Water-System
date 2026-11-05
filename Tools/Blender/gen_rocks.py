# gen_rocks.py — 用 Blender 生成"有棱角的石头"
#
# 思路：岩石的"硬"来自**平面与锐边**。
#   ① 在球内撒随机点 → 取**凸包** ⇒ 天然是一块由平面组成的石头（不是球加噪声）
#   ② 细分一次 → 面不再完整，再沿法线加一点点噪声 ⇒ 崩口/风化
#   ③ Bevel 修饰器（很小的宽度）⇒ 棱边上有细高光，不会像塑料切角
#   ④ 平面着色（flat shading）
#
# 用法：blender -b -P gen_rocks.py
import bpy, bmesh, random, os, math
from mathutils import Vector, noise

OUT = r"E:\Unity xianmu\Unity3D风景的项目立项\Assets\Art\Meshes\Rocks"
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)

# (种子, 点数, 高度压扁, 细分次数, 噪声幅度, 倒角宽度)
SPECS = [
    (11, 34, 0.72, 1, 0.020, 0.012),
    (23, 42, 0.55, 1, 0.016, 0.010),
    (37, 28, 0.90, 1, 0.022, 0.013),
    (49, 52, 0.48, 1, 0.014, 0.009),
    (61, 38, 0.66, 1, 0.018, 0.011),
    (73, 32, 1.00, 1, 0.020, 0.012),
]

def make_rock(name, seed, npts, flat, subdiv, disp, bevel):
    random.seed(seed)
    bm = bmesh.new()
    verts = []
    for _ in range(npts):
        v = Vector((random.gauss(0, 1), random.gauss(0, 1), random.gauss(0, 1)))
        if v.length < 1e-6:
            v = Vector((0, 0, 1))
        v.normalize()
        r = 0.88 + 0.12 * random.random()   # 收紧：更饱满，不做碎石英
        v *= r
        v.z *= flat
        verts.append(bm.verts.new(v))

    bm.verts.ensure_lookup_table()
    bmesh.ops.convex_hull(bm, input=verts)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')

    # 细分 → 崩口
    if subdiv > 0:
        bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=subdiv, use_grid_fill=True)
        bm.normal_update()
        for v in bm.verts:
            n = noise.noise(v.co * 2.7 + Vector((seed, seed * 0.7, seed * 1.3)))
            v.co += v.normal * n * disp

    bm.normal_update()

    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.polygons.foreach_set("use_smooth", [False] * len(me.polygons))
    me.update()

    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)

    # 倒角：让棱边有细高光
    m = ob.modifiers.new("Bevel", 'BEVEL')
    m.width = bevel
    m.segments = 1
    m.limit_method = 'ANGLE'
    m.angle_limit = math.radians(30)

    # 归一化：落地在 z=0，水平居中，最大边≈1
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    bpy.ops.object.modifier_apply(modifier=m.name)

    co = [v.co for v in ob.data.vertices]
    minx = min(c.x for c in co); maxx = max(c.x for c in co)
    miny = min(c.y for c in co); maxy = max(c.y for c in co)
    minz = min(c.z for c in co); maxz = max(c.z for c in co)
    cx = (minx + maxx) * 0.5; cy = (miny + maxy) * 0.5
    s = 1.0 / max(maxx - minx, maxy - miny, maxz - minz, 1e-6)
    for v in ob.data.vertices:
        v.co.x = (v.co.x - cx) * s
        v.co.y = (v.co.y - cy) * s
        v.co.z = (v.co.z - minz) * s
    ob.data.update()
    ob.select_set(False)
    return ob

for i, spec in enumerate(SPECS, start=1):
    name = "R143_Rock_%02d" % i
    ob = make_rock(name, *spec)

    # 只导出这一块
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob

    path = os.path.join(OUT, name + ".fbx")
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        global_scale=1.0,
        apply_unit_scale=False,      # 保持 1 单位 = 1 米，避免 cm/m 换算
        apply_scale_options='FBX_SCALE_NONE',
        bake_space_transform=True,
        mesh_smooth_type='FACE',
        use_mesh_modifiers=True,
    )
    print("EXPORT", path, "verts", len(ob.data.vertices), "polys", len(ob.data.polygons))

print("DONE")
