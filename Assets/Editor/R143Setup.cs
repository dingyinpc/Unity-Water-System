// R143Setup.cs — R143 程序化高模岩石（替换低模平面着色的"黑色多面体"）
//
// 现状：礁石用的是 `IsletRockSplat` 材质的低模网格，几十个三角面 ⇒ 屏幕上一块块明显平面。
// 材料再好也救不了"面数"，所以这轮换**网格**：
//   二十面体 → 细分 N 次 → 沿球心做多倍频噪声位移 → 压扁 → 重算法线
// 每个物体按自己原来的包围盒生成（保持大小/比例），并记录原网格以便还原。
//
// 90 生成高模岩石 / 90b 还原 / 91 探针统计
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    internal static class R143Setup
    {
        const string MeshDir = "Assets/Art/Meshes/R143_Rocks";
        const string KeyPrefix = "R143.orig.";

        static void P(string s) => System.IO.File.AppendAllText(
            System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        static bool IsRock(Renderer r)
        {
            var m = r.sharedMaterial;
            return m != null && m.shader != null && m.shader.name == "WaterProject/IsletRockSplat";
        }

        [MenuItem("Tools/R143/91 探针: 程序化岩物体")]
        public static void Probe()
        {
            var sb = new System.Text.StringBuilder("R143/91 程序化岩物体\n");
            int n = 0;
            foreach (var r in Object.FindObjectsOfType<Renderer>())
            {
                if (!IsRock(r)) continue;
                var mf = r.GetComponent<MeshFilter>();
                var mesh = mf != null ? mf.sharedMesh : null;
                sb.Append($"  {r.gameObject.name,-22} verts={(mesh != null ? mesh.vertexCount : -1),6}")
                  .Append($" tris={(mesh != null ? mesh.triangles.Length / 3 : -1),6}")
                  .Append($" size=({r.bounds.size.x:F1},{r.bounds.size.y:F1},{r.bounds.size.z:F1})\n");
                n++;
            }
            sb.Append($"  合计 {n} 个\n");
            P(sb.ToString());
        }

        [MenuItem("Tools/R143/90 生成高模岩石(当前场景)")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(MeshDir))
                AssetDatabase.CreateFolder("Assets/Art/Meshes", "R143_Rocks");

            int built = 0, skipped = 0;
            foreach (var r in Object.FindObjectsOfType<Renderer>())
            {
                if (!IsRock(r)) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) { skipped++; continue; }

                var old = mf.sharedMesh;
                string key = KeyPrefix + r.gameObject.name;
                if (!EditorPrefs.HasKey(key))
                    EditorPrefs.SetString(key, AssetDatabase.GetAssetPath(old));

                // 用**原始**包围盒（局部）确定比例
                var b = old.bounds;
                var size = b.size;
                int seed = r.gameObject.name.GetHashCode();
                var mesh = BuildRock(seed, size, 3);
                mesh.name = "R143_" + r.gameObject.name;

                string path = $"{MeshDir}/R143_{Sanitize(r.gameObject.name)}.asset";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(mesh, path);
                mf.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                EditorUtility.SetDirty(mf);
                built++;
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            P($"R143/90 生成 {built} 个高模岩石，跳过 {skipped}\n");
        }

        [MenuItem("Tools/R143/90b 还原低模")]
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
                string path = EditorPrefs.GetString(key);
                var src = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (src != null) { mf.sharedMesh = src; EditorUtility.SetDirty(mf); n++; }
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            P($"R143/90b 还原 {n} 个\n");
        }

        static string Sanitize(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        // ── 二十面体细分 + 噪声位移 ──
        static Mesh BuildRock(int seed, Vector3 size, int subdiv)
        {
            // 二十面体
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var v = new List<Vector3>
            {
                new Vector3(-1,  t, 0), new Vector3( 1,  t, 0), new Vector3(-1, -t, 0), new Vector3( 1, -t, 0),
                new Vector3( 0, -1, t), new Vector3( 0,  1, t), new Vector3( 0, -1,-t), new Vector3( 0,  1,-t),
                new Vector3( t,  0,-1), new Vector3( t,  0, 1), new Vector3(-t,  0,-1), new Vector3(-t,  0, 1),
            };
            for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;

            var f = new List<int>
            {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11,
                1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9,
                4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1,
            };

            for (int s = 0; s < subdiv; s++)
            {
                var nf = new List<int>(f.Count * 4);
                var cache = new Dictionary<long, int>();
                for (int i = 0; i < f.Count; i += 3)
                {
                    int a = f[i], b = f[i + 1], c = f[i + 2];
                    int ab = Mid(v, cache, a, b), bc = Mid(v, cache, b, c), ca = Mid(v, cache, c, a);
                    nf.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                f = nf;
            }

            // 噪声位移（径向）
            var verts = new Vector3[v.Count];
            for (int i = 0; i < v.Count; i++)
            {
                var n = v[i];
                float d1 = VNoise3(n * 1.7f + new Vector3(seed * 0.013f, seed * 0.021f, seed * 0.017f));
                float d2 = VNoise3(n * 4.3f + new Vector3(3.1f, 7.7f, 1.3f));
                float d3 = VNoise3(n * 9.1f + new Vector3(11.3f, 2.9f, 5.5f));
                // R143 第二版：第一版 0.30/0.14/0.06 太猛，石头变成"爆米花"。
                //   改成低频大形 + 很轻的中高频，保住原来的轮廓与棱角。
                float r = 1f + 0.14f * (d1 - 0.5f) + 0.05f * (d2 - 0.5f) + 0.015f * (d3 - 0.5f);
                verts[i] = n * r;
            }

            // 按原包围盒缩放（保持各轴比例），并把最低点对齐到 -size.y/2
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var p in verts) { minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y); }
            float span = Mathf.Max(maxY - minY, 1e-3f);
            for (int i = 0; i < verts.Length; i++)
            {
                var p = verts[i];
                verts[i] = new Vector3(
                    p.x * size.x * 0.5f,
                    (p.y - minY) / span * size.y - size.y * 0.5f,   // 底对齐到 -h/2
                    p.z * size.z * 0.5f);
            }

            var mesh = new Mesh { name = "R143_Rock" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts;
            mesh.triangles = f.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static int Mid(List<Vector3> v, Dictionary<long, int> cache, int a, int b)
        {
            long key = a < b ? ((long)a << 32) + b : ((long)b << 32) + a;
            if (cache.TryGetValue(key, out int idx)) return idx;
            v.Add(((v[a] + v[b]) * 0.5f).normalized);
            cache[key] = v.Count - 1;
            return v.Count - 1;
        }

        // 3D 值噪声（简化版）
        static float VNoise3(Vector3 p)
        {
            int xi = Mathf.FloorToInt(p.x), yi = Mathf.FloorToInt(p.y), zi = Mathf.FloorToInt(p.z);
            float xf = p.x - xi, yf = p.y - yi, zf = p.z - zi;
            float u = xf * xf * (3 - 2 * xf), w = yf * yf * (3 - 2 * yf), q = zf * zf * (3 - 2 * zf);
            float c000 = H(xi, yi, zi), c100 = H(xi + 1, yi, zi);
            float c010 = H(xi, yi + 1, zi), c110 = H(xi + 1, yi + 1, zi);
            float c001 = H(xi, yi, zi + 1), c101 = H(xi + 1, yi, zi + 1);
            float c011 = H(xi, yi + 1, zi + 1), c111 = H(xi + 1, yi + 1, zi + 1);
            float x00 = Mathf.Lerp(c000, c100, u), x10 = Mathf.Lerp(c010, c110, u);
            float x01 = Mathf.Lerp(c001, c101, u), x11 = Mathf.Lerp(c011, c111, u);
            return Mathf.Lerp(Mathf.Lerp(x00, x10, w), Mathf.Lerp(x01, x11, w), q);
        }

        static float H(int x, int y, int z)
        {
            unchecked
            {
                int n = x * 374761393 + y * 668265263 + z * 1274126177;
                n = (n ^ (n >> 13)) * 1274126177;
                return ((n ^ (n >> 16)) & 0x7fffffff) / (float)0x7fffffff;
            }
        }
    }
}
#endif
