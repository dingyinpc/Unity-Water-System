// R33ContentSetup.cs —— 内容资产轮（R31 清单：Main/Pond 水下植被石头 + Pond 深绿水色/光束 + Main 能见度/反射）
//
// 与 R32（纯参数）不同：本轮**往水底放东西**。全部程序化生成（免资产，G6 路线）：
//   · 水草：世界坐标合并网格（一 draw call），WaterPlant.shader 顶点摇摆；
//     落点用 Physics.Raycast 探当前**真实**池底（不依赖某一代菜单的解析式——
//     Main 的池底被 20/1c/1310 几代菜单重塑过，解析式必过期）。
//   · 石头：复用 WaterProjectSetup.BuildFacetedRock（internal，同程序集可直接调）。
//   · 成丛分布：参考的水草是**一丛丛**长的，不是均匀草坪（R31 帧级对照结论）。
// 生成物挂在 "R33_Flora" 根节点下，90b/91b 一键拆除（内容可逆）。

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Collections.Generic;

namespace WaterProject.EditorTools
{
    internal static class R33ContentSetup
    {
        const string MatFolder = "Assets/Art/Materials";
        const string MeshFolder = "Assets/Art/Meshes";

        // ── 水草合并网格生成 ──────────────────────────────
        // 每株：4 段竖条带，根宽→梢尖，随机朝向/前倾；顶点色 r=每株明度变体
        static void AddBlade(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs,
                             List<Color> cols, List<int> tris, System.Random rng,
                             Vector3 root, float height, float width)
        {
            int segs = 4;
            float yaw = (float)rng.NextDouble() * Mathf.PI * 2f;
            float lean = ((float)rng.NextDouble() - 0.4f) * 0.55f;   // 前倾（偏向正的多）
            float leanPh = (float)rng.NextDouble() * Mathf.PI * 2f;
            var leanDir = new Vector3(Mathf.Cos(leanPh), 0, Mathf.Sin(leanPh)) * lean;
            var right = new Vector3(Mathf.Cos(yaw + Mathf.PI * 0.5f), 0, Mathf.Sin(yaw + Mathf.PI * 0.5f));
            float variant = 0.82f + (float)rng.NextDouble() * 0.36f;
            int baseIdx = verts.Count;

            for (int s = 0; s <= segs; s++)
            {
                float t = (float)s / segs;
                float y = root.y + height * t;
                // 前倾随高度增大（t²），梢往 leanDir 偏
                Vector3 c = root + Vector3.up * (height * t) + leanDir * (t * t);
                float w = Mathf.Lerp(width, 0.004f, t * t);           // 梢部收尖
                verts.Add(c - right * w); verts.Add(c + right * w);
                var n = Vector3.Cross(right, Vector3.up);
                norms.Add(n); norms.Add(n);
                uvs.Add(new Vector2(0, t)); uvs.Add(new Vector2(1, t));
                cols.Add(new Color(variant, 0, 0, 1)); cols.Add(new Color(variant, 0, 0, 1));
            }
            for (int s = 0; s < segs; s++)
            {
                int a = baseIdx + s * 2, b = a + 1, c = a + 2, d = a + 3;
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(b); tris.Add(c); tris.Add(d);
            }
        }

        // 水下碰撞体保障：盆底/池底网格当年没挂 collider（AttachExistingMesh 不加），
        // 水面自己反而有（会挡 raycast）。给"边界跨水下带"的网格就地补 MeshCollider，
        // 采样时用 RaycastAll + 深度门跳过水面命中。
        static void EnsureUnderwaterColliders()
        {
            int added = 0;
            foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                var go = mr.gameObject;
                if (go.name.Contains("Water") || go.name.StartsWith("R33_")) continue;
                var b = mr.bounds;
                if (b.max.y > -0.25f && b.min.y < -1.1f && go.GetComponent<MeshCollider>() == null)
                {
                    var mf = go.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    var mc = go.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    added++;
                }
            }
            if (added > 0)
            {
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                Write($"R33 colliders: {added} 个水下网格补了 MeshCollider（随场景保存）\n");
            }
        }

        // 深度命中：所有命中里挑"低于 -minDepth 的最近一个"（水面 y≈0.02 的命中被深度门跳过）
        static bool FloorHit(Vector3 from, float minDepth, out Vector3 pt)
        {
            pt = Vector3.zero;
            var hits = Physics.RaycastAll(from, Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (h.point.y < -minDepth && h.point.y > -3.5f && !h.collider.name.Contains("Water"))
                { pt = h.point; return true; }
            }
            return false;
        }

        // 在水域里撒点：raycast 打真实池底，够深（<-minDepth）才收
        static List<Vector3> SampleFloor(System.Random rng, float rMax, int tries,
                                         float minDepth, float maxRMin)
        {
            var pts = new List<Vector3>();
            for (int i = 0; i < tries; i++)
            {
                float th = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = Mathf.Lerp(maxRMin, rMax, (float)rng.NextDouble());
                var p = new Vector3(Mathf.Cos(th) * r, 3f, Mathf.Sin(th) * r);
                if (FloorHit(p, minDepth, out var hit)) pts.Add(hit);
            }
            return pts;
        }

        static GameObject BuildFlora(string sceneTag, float rMax, int clumpCount,
                                     int clumpBlades, int singles, float hMin, float hMax)
        {
            EnsureUnderwaterColliders();
            var rng = new System.Random(20261025 + (sceneTag == "Pond" ? 1 : 2));
            var verts = new List<Vector3>(); var norms = new List<Vector3>();
            var uvs = new List<Vector2>(); var cols = new List<Color>(); var tris = new List<int>();

            // 丛心：从池底采样点里挑；丛内再在半径 0.3~0.8m 里 raycast 定每株根
            var anchors = SampleFloor(rng, rMax, clumpCount * 6, 0.28f, 0.8f);
            int clumpsMade = 0, blades = 0;
            for (int k = 0; k < anchors.Count && clumpsMade < clumpCount; k++)
            {
                if (k % 2 != 0 && clumpsMade > clumpCount / 2) continue;   // 丛心也散开点
                var a = anchors[k];
                float cr = 0.30f + (float)rng.NextDouble() * 0.5f;
                int want = clumpBlades / 2 + (int)(rng.NextDouble() * clumpBlades);
                int got = 0;
                for (int b = 0; b < want * 3 && got < want; b++)
                {
                    float th = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float rr = cr * Mathf.Sqrt((float)rng.NextDouble());
                    var p = a + new Vector3(Mathf.Cos(th) * rr, 3f, Mathf.Sin(th) * rr);
                    if (!FloorHit(p, 0.22f, out var hit)) continue;
                    float depth = -hit.y;
                    // 高度：不戳出水面（梢留 4cm 余量），深处长一点
                    float h = Mathf.Min(Mathf.Lerp(hMin, hMax, (float)rng.NextDouble()),
                                        depth - 0.04f);
                    if (h < 0.08f) continue;
                    AddBlade(verts, norms, uvs, cols, tris, rng,
                             hit, h, 0.022f + (float)rng.NextDouble() * 0.02f);
                    got++; blades++;
                }
                if (got > 0) clumpsMade++;
            }
            // 散株：均匀低密度铺底
            var singlesPts = SampleFloor(rng, rMax, singles * 3, 0.24f, 0.6f);
            for (int k = 0; k < singlesPts.Count && k < singles; k++)
            {
                float depth = -singlesPts[k].y;
                float h = Mathf.Min(Mathf.Lerp(hMin * 0.7f, hMax * 0.7f, (float)rng.NextDouble()),
                                    depth - 0.04f);
                if (h < 0.08f) continue;
                AddBlade(verts, norms, uvs, cols, tris, rng,
                         singlesPts[k], h, 0.02f + (float)rng.NextDouble() * 0.016f);
                blades++;
            }

            var mesh = new Mesh { name = "R33_" + sceneTag + "Flora" };
            mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uvs);
            mesh.SetColors(cols); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();

            var matPath = MatFolder + "/R33_WaterPlant.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                var sh = Shader.Find("WaterProject/WaterPlant");
                if (sh == null) { Write("R33 " + sceneTag + " REFUSED: WaterPlant shader 未编译\n"); return null; }
                mat = new Material(sh) { name = "R33_WaterPlant" };
                AssetDatabase.CreateAsset(mat, matPath);
            }

            var meshPath = MeshFolder + "/R33_" + sceneTag + "Flora.asset";
            AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.CreateAsset(mesh, meshPath);

            var root = new GameObject("R33_Flora");
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            root.AddComponent<MeshRenderer>().sharedMaterial = mat;
            root.transform.SetSiblingIndex(0);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Write($"R33 {sceneTag} flora: {blades} blades / {clumpsMade} clumps, mesh {verts.Count / 2} quads\n");
            return root;
        }

        // 水下石头（半埋）：复用岩体生成器
        static void BuildRocks(string sceneTag, float rMax, int count, System.Random rng)
        {
            var rockMat = AssetDatabase.LoadAssetAtPath<Material>(
                sceneTag == "Pond" ? MatFolder + "/Pond_Rock.mat" : MatFolder + "/Ocean_Rock.mat");
            var rocks = new Mesh[3];
            for (int v = 0; v < 3; v++) rocks[v] = WaterProjectSetup.BuildFacetedRock(v, 1f);
            var pts = SampleFloor(rng, rMax, count * 5, 0.30f, 0.7f);
            int made = 0;
            for (int k = 0; k < pts.Count && made < count; k++)
            {
                var s = 0.16f + (float)rng.NextDouble() * 0.24f;
                var go = new GameObject("R33_Rock_" + made);
                go.AddComponent<MeshFilter>().sharedMesh = rocks[k % 3];
                go.AddComponent<MeshRenderer>().sharedMaterial = rockMat;
                // 半埋：中心沉到地面下 30%，顶部露 40% 左右
                go.transform.position = pts[k] + new Vector3(0, s * 0.22f, 0);
                go.transform.rotation = Quaternion.Euler(
                    (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 360f, 0);
                go.transform.localScale = Vector3.one * s;
                made++;
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Write($"R33 {sceneTag} rocks: {made}\n");
        }

        static void ClearFlora()
        {
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (go != null && (go.name == "R33_Flora" || go.name.StartsWith("R33_Rock_")))
                    Object.DestroyImmediate(go);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Write("R33 flora cleared\n");
        }

        // ═══════════════════ 菜单 ═══════════════════
        [MenuItem("Tools/R33/90 Pond 水下植被+石头")]
        static void PondFlora()
        {
            if (Application.isPlaying) { Write("R33 REFUSED: Play 模式先退\n"); return; }
            ClearFlora();
            BuildFlora("Pond", 6.6f, 12, 55, 60, 0.28f, 0.55f);
            BuildRocks("Pond", 6.0f, 9, new System.Random(2026102511));
        }

        [MenuItem("Tools/R33/90b Pond 水下内容拆除")]
        static void PondFloraClear() => ClearFlora();

        [MenuItem("Tools/R33/91 Main 水下植被+石头")]
        static void MainFlora()
        {
            if (Application.isPlaying) { Write("R33 REFUSED: Play 模式先退\n"); return; }
            ClearFlora();
            BuildFlora("Main", 11.5f, 16, 60, 90, 0.30f, 0.62f);
            BuildRocks("Main", 10.5f, 12, new System.Random(2026102522));
        }

        [MenuItem("Tools/R33/91b Main 水下内容拆除")]
        static void MainFloraClear() => ClearFlora();

        // ── Pond 深绿水色 + 光束档（R31 Pond#1/#3）──
        [MenuItem("Tools/R33/92 Pond 深绿水色+光束档")]
        static void PondTier()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(MatFolder + "/Water_Pond.mat");
            if (m == null) { Write("R33 REFUSED: Water_Pond.mat 不存在\n"); return; }
            m.SetColor("_ShallowColor", new Color(0.105f, 0.375f, 0.300f, 1f));  // 旧 (0.150,0.430,0.400) 偏浅青
            m.SetColor("_DeepColor", new Color(0.014f, 0.115f, 0.105f, 1f));     // 旧 (0.020,0.150,0.160)
            m.SetFloat("_Absorb", 0.62f);          // 旧 0.9：植被透过 73% 水色盖没——降吸收才看得清茎叶（R31 Pond#4）
            m.SetFloat("_ScatterGain", 2.6f);       // 旧 1.4：光束（2.0 判读弱、2.2 仍弱，终档 2.6）
            m.SetFloat("_ScatterStrength", 0.75f);  // 旧 0.35：束/影对比度（有界混合，无发光板风险）
            m.SetFloat("_ScatterSteps", 14f);       // 旧 10：光程采样更细，束形连续
            AssetDatabase.SaveAssetIfDirty(m);
            Write("R33 Pond tier: 深绿水色 + absorb0.62 + scatter 2.2/0.65/14\n");
        }

        [MenuItem("Tools/R33/92b Pond 还原档")]
        static void PondRevert()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(MatFolder + "/Water_Pond.mat");
            if (m == null) return;
            m.SetColor("_ShallowColor", new Color(0.150f, 0.430f, 0.400f, 1f));
            m.SetColor("_DeepColor", new Color(0.020f, 0.150f, 0.160f, 1f));
            m.SetFloat("_Absorb", 0.9f);
            m.SetFloat("_ScatterGain", 1.4f); m.SetFloat("_ScatterStrength", 0.35f); m.SetFloat("_ScatterSteps", 10f);
            AssetDatabase.SaveAssetIfDirty(m);
            Write("R33 Pond revert done\n");
        }

        // ── Main 能见度 + 反射连续性档（R31 Main#1/#2/#4）──
        [MenuItem("Tools/R33/93 Main 能见度+反射档")]
        static void MainTier()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(MatFolder + "/Water_Advanced.mat");
            if (m == null) { Write("R33 REFUSED: Water_Advanced.mat 不存在\n"); return; }
            m.SetFloat("_Absorb", 0.68f);          // 旧 0.9：水下能见度（植被要能看穿）
            m.SetFloat("_SSRSteps", 36f);          // 旧 24：反射断裂变连续（R31 Main#1）
            m.SetFloat("_SSRMaxDist", 60f);        // 旧 40：更长的命中距离
            m.SetFloat("_SSRThickness", 0.7f);     // 旧 0.5：厚度容差放宽，碎块易连
            AssetDatabase.SaveAssetIfDirty(m);
            Write("R33 Main tier: absorb 0.68 + SSR 36/60/0.7\n");
        }

        [MenuItem("Tools/R33/93b Main 还原档")]
        static void MainRevert()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(MatFolder + "/Water_Advanced.mat");
            if (m == null) return;
            m.SetFloat("_Absorb", 0.9f); m.SetFloat("_SSRSteps", 24f);
            m.SetFloat("_SSRMaxDist", 40f); m.SetFloat("_SSRThickness", 0.5f);
            AssetDatabase.SaveAssetIfDirty(m);
            Write("R33 Main revert done\n");
        }

        [MenuItem("Tools/R33/99 探针回读(只读)")]
        static void Probe()
        {
            var pond = AssetDatabase.LoadAssetAtPath<Material>(MatFolder + "/Water_Pond.mat");
            if (pond != null)
                Write($"R33 PROBE pond shallow={pond.GetColor("_ShallowColor")} scatter " +
                      $"{pond.GetFloat("_ScatterGain")}/{pond.GetFloat("_ScatterStrength")}/{pond.GetFloat("_ScatterSteps")}\n");
            var adv = AssetDatabase.LoadAssetAtPath<Material>(MatFolder + "/Water_Advanced.mat");
            if (adv != null)
                Write($"R33 PROBE main absorb={adv.GetFloat("_Absorb")} ssr " +
                      $"{adv.GetFloat("_SSRSteps")}/{adv.GetFloat("_SSRMaxDist")}/{adv.GetFloat("_SSRThickness")}\n");
            int flora = 0, rocks = 0;
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            {
                if (go.name == "R33_Flora") flora++;
                if (go.name.StartsWith("R33_Rock_")) rocks++;
            }
            Write($"R33 PROBE objects flora={flora} rocks={rocks} (编辑模式场景对象)\n");
        }

        static void Write(string s)
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s + "\n");
        }
    }
}
#endif
