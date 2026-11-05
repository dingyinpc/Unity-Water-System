// R144Setup.cs — R144 用 Blender 生成的有棱角石头替换低模礁石
//
// 每个物体按**原网格包围盒**做逐轴缩放 + 居中平移，烘焙进新网格
// ⇒ 位置、大小、朝向全部保持不变，只有形状变了。
// 90 替换 / 90b 还原 / 91 探针
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    internal static class R144Setup
    {
        const string FbxDir = "Assets/Art/Meshes/Rocks";
        const string OutDir = "Assets/Art/Meshes/RocksBaked";
        const string KeyPrefix = "R144.orig.";

        static void P(string s) => System.IO.File.AppendAllText(
            System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        static bool IsRock(Renderer r)
        {
            var m = r.sharedMaterial;
            return m != null && m.shader != null && m.shader.name == "WaterProject/IsletRockSplat";
        }

        static List<Mesh> LoadRockMeshes()
        {
            var list = new List<Mesh>();
            foreach (var path in AssetDatabase.FindAssets("t:Model", new[] { FbxDir }))
            {
                var p = AssetDatabase.GUIDToAssetPath(path);
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p))
                    if (o is Mesh m) list.Add(m);
            }
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return list;
        }

        [MenuItem("Tools/R144/90 礁石=换成 Blender 石头")]
        public static void Replace()
        {
            var rocks = LoadRockMeshes();
            if (rocks.Count == 0) { P("R144 REFUSED: 没有找到石头 FBX（先跑 Tools/Blender/gen_rocks.py）\n"); return; }
            if (!AssetDatabase.IsValidFolder(OutDir))
                AssetDatabase.CreateFolder("Assets/Art/Meshes", "RocksBaked");

            // FBX 导入参数：忽略文件自带比例、烘轴转换（与椰树同一条经验）
            foreach (var path in AssetDatabase.FindAssets("t:Model", new[] { FbxDir }))
            {
                var p = AssetDatabase.GUIDToAssetPath(path);
                var mi = AssetImporter.GetAtPath(p) as ModelImporter;
                if (mi == null) continue;
                if (mi.useFileScale || !mi.bakeAxisConversion)
                {
                    mi.useFileScale = false;
                    mi.globalScale = 1f;
                    mi.bakeAxisConversion = true;
                    mi.SaveAndReimport();
                }
            }
            rocks = LoadRockMeshes();

            int n = 0, idx = 0;
            foreach (var r in Object.FindObjectsOfType<Renderer>())
            {
                if (!IsRock(r)) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;

                var old = mf.sharedMesh;
                string key = KeyPrefix + r.gameObject.name;
                if (!EditorPrefs.HasKey(key))
                    EditorPrefs.SetString(key, AssetDatabase.GetAssetPath(old));

                var src = rocks[idx % rocks.Count]; idx++;

                var baked = BakeToBounds(src, old.bounds, r.gameObject.name);
                string outPath = $"{OutDir}/R144_{r.gameObject.name}.asset";
                AssetDatabase.DeleteAsset(outPath);
                AssetDatabase.CreateAsset(baked, outPath);
                mf.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(outPath);
                EditorUtility.SetDirty(mf);
                n++;
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            P($"R144/90 替换 {n} 个礁石（候选石头 {rocks.Count} 块）\n");
        }

        [MenuItem("Tools/R144/90b 礁石=还原低模")]
        public static void Restore()
        {
            int n = 0;
            foreach (var r in Object.FindObjectsOfType<Renderer>())
            {
                if (!IsRock(r)) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null) continue;
                string key = KeyPrefix + r.gameObject.name;
                if (!EditorPrefs.HasKey(key)) continue;
                var src = AssetDatabase.LoadAssetAtPath<Mesh>(EditorPrefs.GetString(key));
                if (src != null) { mf.sharedMesh = src; EditorUtility.SetDirty(mf); n++; }
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            P($"R144/90b 还原 {n} 个\n");
        }

        // 把 src 的顶点逐轴缩放到 targetBounds 的尺寸，并把中心对齐
        //
        // ⚠ **关键：高度轴必须转到 Z**。`Islet_Rock_Splat` 用 `IN.posZ` 当"高度"分三档灰，
        //   而 Blender 导出的（bakeAxisConversion）高度在 **Y** ⇒ 分档全错、整块发白（R144 的教训）。
        //   这里先绕 X 轴转 -90°（(x,y,z) → (x, z, -y)），让高度落到 Z，再按原包围盒逐轴缩放。
        static Mesh BakeToBounds(Mesh src, Bounds target, string name)
        {
            var sv = src.vertices;
            var rot = new Vector3[sv.Length];
            var rb = new Bounds();
            for (int i = 0; i < sv.Length; i++)
            {
                var v = sv[i];
                var p = new Vector3(v.x, v.z, -v.y);     // 高度 Y → Z
                rot[i] = p;
                rb.Encapsulate(p);
            }

            float sx = rb.size.x > 1e-5f ? target.size.x / rb.size.x : 1f;
            float sy = rb.size.y > 1e-5f ? target.size.y / rb.size.y : 1f;
            float sz = rb.size.z > 1e-5f ? target.size.z / rb.size.z : 1f;

            var outV = new Vector3[rot.Length];
            for (int i = 0; i < rot.Length; i++)
            {
                var p = rot[i] - rb.center;
                outV[i] = new Vector3(p.x * sx + target.center.x,
                                      p.y * sy + target.center.y,
                                      p.z * sz + target.center.z);
            }

            var m = new Mesh { name = "R144_" + name };
            m.indexFormat = src.indexFormat;
            m.vertices = outV;
            m.triangles = src.triangles;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        [MenuItem("Tools/R144/91 探针")]
        public static void Probe()
        {
            var rocks = LoadRockMeshes();
            var sb = new System.Text.StringBuilder($"R144/91 石头候选 {rocks.Count} 块\n");
            foreach (var m in rocks)
                sb.Append($"  {m.name}: verts={m.vertexCount} tris={m.triangles.Length / 3} size={m.bounds.size}\n");
            P(sb.ToString());
        }
    }
}
#endif
