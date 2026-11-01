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
            var rockMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Ocean_Rock.mat");
            if (rockMat == null) { P("R47/90 REFUSED: 找不到 Ocean_Rock.mat\n"); Object.DestroyImmediate(root); return; }

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
            var coralPts = Sample(14, rng, 6f, 80f, -0.80f, -0.05f);
            if (coralPts.Count == 0)
                P("R47/90 WARN: 珊瑚落点 0 个（水面下没有探测到可行走的地形？）\n");

            for (int i = 0; i < coralPts.Count; i++)
            {
                var grp = new GameObject("R47_Coral_" + i);
                grp.transform.SetParent(root.transform, false);
                grp.transform.position = coralPts[i];
                var mat = coralMats[i % 3];
                int lobes = 2 + (i % 2);
                for (int k = 0; k < lobes; k++)
                {
                    var lobe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Object.DestroyImmediate(lobe.GetComponent<Collider>());
                    lobe.name = "lobe";
                    lobe.transform.SetParent(grp.transform, false);
                    float s = 0.55f + (float)rng.NextDouble() * 0.55f;
                    lobe.transform.localScale = new Vector3(s, s * 0.55f, s);
                    lobe.transform.localPosition = new Vector3(
                        ((float)rng.NextDouble() - 0.5f) * 0.8f,
                        k * 0.18f + 0.1f,
                        ((float)rng.NextDouble() - 0.5f) * 0.8f);
                    lobe.GetComponent<MeshRenderer>().sharedMaterial = mat;
                }
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
