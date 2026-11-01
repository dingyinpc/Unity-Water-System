// R47IsletContent.cs — R47 ④：给 Islet 补"礁石 + 珊瑚"（参考 S0 俯视里最显眼的两类内容）
//
// 为什么要补：R46 把机位换成参考同款的俯视后，Islet 只剩"青色水面 + 一条米色沙洲"，
//   参考里同机位能看到：礁石（深灰圆石）、珊瑚（绿/红/紫团）、水底焦散。
//   其中礁石/珊瑚属于**内容**（不是参数能调出来的），所以单独一件。
//
// 做法：全部程序化、零外部资源。
//   · 石头：复用 WaterProjectSetup.BuildFacetedRock（R33 在 Pond/Main 用过）
//   · 珊瑚：2~3 个压扁的球叠成丛，三种配色材质（绿/红/紫）
//   · 落点：从高空向下 Raycast 打 TerrainCollider，按**水深**筛选落点
//       石头 → 沙洲边/极浅水（y ∈ [-1.2, 1.2]）
//       珊瑚 → 浅水（y ∈ [-2.4, -0.15]）
//   · 全部挂在 R47_Reef 下，90b 一键拆（可逆）
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WaterProject.EditorTools
{
    public static class R47IsletContent
    {
        const string Root = "R47_Reef";
        const string MatDir = "Assets/Art/Materials/Islet";

        static void P(string s)
            => File.AppendAllText(Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        [MenuItem("Tools/R47/90 Islet 礁石+珊瑚(可逆)")]
        public static void Build()
        {
            if (Application.isPlaying) { P("R47/90 REFUSED: 先退 Play（场景改动不能在播放态做）\n"); return; }
            Clear(quiet: true);

            var root = new GameObject(Root);
            var rng = new System.Random(20261101);
            var rockMat = GetReefRockMat();
            if (rockMat == null) { P("R47/90 REFUSED: 建不出礁石材质\n"); Object.DestroyImmediate(root); return; }

            var rockMesh = new Mesh[3];
            for (int v = 0; v < 3; v++) rockMesh[v] = WaterProjectSetup.BuildFacetedRock(v, 1f);

            // ── 石头：沙洲边与极浅水 ──
            var rockPts = Sample(26, rng, 4f, 95f, 0.10f, 3.0f);
            for (int i = 0; i < rockPts.Count; i++)
            {
                float s = 0.30f + (float)rng.NextDouble() * 1.10f;
                var go = new GameObject("R47_Rock_" + i);
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = rockMesh[i % 3];
                go.AddComponent<MeshRenderer>().sharedMaterial = rockMat;
                go.transform.position = rockPts[i] + Vector3.up * (s * 0.20f);
                go.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                go.transform.localScale = Vector3.one * s;
            }

            // ── 珊瑚：浅水里的彩色团 ──
            var coralCols = new[]
            {
                new Color(0.16f, 0.44f, 0.28f),   // 藻绿
                new Color(0.62f, 0.22f, 0.24f),   // 珊瑚红
                new Color(0.36f, 0.24f, 0.55f),   // 紫
            };
            var coralMats = new Material[3];
            for (int i = 0; i < 3; i++) coralMats[i] = GetCoralMat(i, coralCols[i]);
            // R48：珊瑚从"球堆"换成**带枝杈的程序化网格**（球堆在俯视就是彩色小点）
            var coralMeshes = new Mesh[3];
            for (int v = 0; v < 3; v++) coralMeshes[v] = BuildCoralMesh(v, new System.Random(500 + v));
            var coralPts = Sample(14, rng, 6f, 80f, -0.80f, -0.05f);
            if (coralPts.Count == 0)
                P("R47/90 WARN: 珊瑚落点 0 个（水面下没有探测到可行走的地形？）\n");

            for (int i = 0; i < coralPts.Count; i++)
            {
                var go = new GameObject("R47_Coral_" + i);
                go.transform.SetParent(root.transform, false);
                go.transform.position = coralPts[i];
                float s = 0.8f + (float)rng.NextDouble() * 0.9f;
                go.transform.localScale = new Vector3(s, s, s);
                go.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                go.AddComponent<MeshFilter>().sharedMesh = coralMeshes[i % 3];
                go.AddComponent<MeshRenderer>().sharedMaterial = coralMats[i % 3];
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            P($"R47/90 Islet 内容: 石头 {rockPts.Count} 个 / 珊瑚 {coralPts.Count} 丛（根节点 {Root}）\n");
        }

        [MenuItem("Tools/R47/90b Islet 礁石+珊瑚 拆除")]
        public static void RemoveMenu() => Clear(quiet: false);

        static void Clear(bool quiet)
        {
            var go = GameObject.Find(Root);
            int n = 0;
            foreach (var g in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (g != null && g.name.StartsWith("R47_Rock_")) n++;
            if (go != null) Object.DestroyImmediate(go);
            foreach (var g in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (g != null && g.name.StartsWith("R47_Coral_")) Object.DestroyImmediate(g);
            if (!quiet) { EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene()); P($"R47/90b 已拆除（石头 {n} 个）\n"); }
        }

        // R48：礁石不再用 Ocean_Rock（那套是"崖体 splat"用的，在这个光照下渲染成纯黑），
        //   改成一个朴素 URP/Lit 灰褐色，至少能看出明暗面。
        static Material GetReefRockMat()
        {
            string p = MatDir + "/Islet_ReefRock.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m != null) return m;
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) { P("R48 WARN: 找不到 URP/Lit\n"); return null; }
            m = new Material(sh) { name = "Islet_ReefRock" };
            m.SetColor("_BaseColor", new Color(0.33f, 0.31f, 0.28f));
            m.SetFloat("_Smoothness", 0.22f);
            m.SetFloat("_Metallic", 0f);
            AssetDatabase.CreateAsset(m, p);
            P("R48 新建礁石材质 Islet_ReefRock.mat\n");
            return m;
        }

        // 程序化珊瑚：4~6 根**带锥度的枝杈**从同一点向外上生长（球堆在俯视只是彩色点）。
        static Mesh BuildCoralMesh(int variant, System.Random rng)
        {
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var tris = new List<int>();

            int branches = 4 + variant;
            for (int b = 0; b < branches; b++)
            {
                float ang = (b / (float)branches) * Mathf.PI * 2f + (float)rng.NextDouble() * 0.6f;
                float tilt = 0.42f + (float)rng.NextDouble() * 0.42f;
                Vector3 dir = new Vector3(Mathf.Cos(ang) * Mathf.Sin(tilt),
                                          Mathf.Cos(tilt),
                                          Mathf.Sin(ang) * Mathf.Sin(tilt)).normalized;
                float len = 0.55f + (float)rng.NextDouble() * 0.55f;
                float r0 = 0.075f, r1 = 0.018f;

                Vector3 right = Vector3.Cross(dir, Vector3.forward);
                if (right.sqrMagnitude < 1e-4f) right = Vector3.right;
                right.Normalize();
                Vector3 fwd = Vector3.Cross(dir, right).normalized;

                const int rings = 4, seg = 6;
                int baseIdx = verts.Count;
                for (int i = 0; i <= rings; i++)
                {
                    float tt = i / (float)rings;
                    Vector3 c = dir * (len * tt);
                    float r = Mathf.Lerp(r0, r1, tt);
                    for (int s = 0; s < seg; s++)
                    {
                        float a = s / (float)seg * Mathf.PI * 2f;
                        Vector3 off = right * (Mathf.Cos(a) * r) + fwd * (Mathf.Sin(a) * r);
                        verts.Add(c + off);
                        norms.Add((off.normalized + dir * 0.25f).normalized);
                    }
                }
                for (int i = 0; i < rings; i++)
                    for (int s = 0; s < seg; s++)
                    {
                        int a0 = baseIdx + i * seg + s;
                        int a1 = baseIdx + i * seg + (s + 1) % seg;
                        int b0 = a0 + seg, b1 = a1 + seg;
                        tris.Add(a0); tris.Add(b0); tris.Add(a1);
                        tris.Add(a1); tris.Add(b0); tris.Add(b1);
                    }
            }

            var m = new Mesh { name = "Coral_" + variant };
            m.SetVertices(verts);
            m.SetNormals(norms);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        static Material GetCoralMat(int i, Color c)
        {
            string p = $"{MatDir}/Islet_Coral_{i}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m != null) return m;
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) { P("R47 WARN: 找不到 URP/Lit，珊瑚将无材质\n"); return null; }
            m = new Material(sh) { name = $"Islet_Coral_{i}" };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", 0.18f);
            AssetDatabase.CreateAsset(m, p);
            return m;
        }

        // 落点：优先用 **Terrain.SampleHeight**（IsletScene 的地形**没有挂在 PhysX 上的碰撞体**，
        //   第一次用 Physics.Raycast 打点，60 次全部落空 ⇒ 石头/珊瑚都是 0 个）。
        //   没有 ActiveTerrain 时才退回 Raycast（Pond/Main 那类有 MeshCollider 的场景）。
        static List<Vector3> Sample(int want, System.Random rng, float rMin, float rMax, float yMin, float yMax)
        {
            var list = new List<Vector3>();
            var terr = Terrain.activeTerrain;
            int guard = want * 60;
            for (int k = 0; k < guard && list.Count < want; k++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = Mathf.Lerp(rMin, rMax, Mathf.Sqrt((float)rng.NextDouble()));
                float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
                Vector3 p;
                if (terr != null)
                {
                    float y = terr.SampleHeight(new Vector3(x, 0f, z)) + terr.transform.position.y;
                    p = new Vector3(x, y, z);
                }
                else
                {
                    if (!Physics.Raycast(new Vector3(x, 90f, z), Vector3.down, out var hit, 200f)) continue;
                    p = hit.point;
                }
                if (p.y >= yMin && p.y <= yMax) list.Add(p);
            }
            return list;
        }
    }
}
#endif
