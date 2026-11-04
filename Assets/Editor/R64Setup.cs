// R64Setup.cs — R64 屏幕拾取：没有碰撞体也能"点选"物体
//
// 问题：Ocean 远处那块"灰白平板"试遍了雾/远崖/近崖/沙滩都不是（R63），
//   而 OceanScene 里**没有碰撞体**，`Physics.Raycast` 用不了。
// 办法：用 `Renderer.bounds.IntersectRay` —— 拿软件包围盒做射线相交，
//   不依赖碰撞体，能直接告出"这个屏幕点上有哪些物体、谁最近"。
//
// 用法：改 `Points` 里的归一化坐标（0~1，左下为原点），菜单跑一次看探针。
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    public static class R64Setup
    {
        static void P(string s)
            => File.AppendAllText(Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        // 归一化屏幕点（0~1，左下原点）。默认取"平板"所在的几个点。
        static readonly Vector2[] Points =
        {
            new Vector2(0.50f, 0.55f),   // R105：Pool 中间那个黑色人影
            new Vector2(0.50f, 0.72f),   // R105：Pool 上方那个球
            new Vector2(0.46f, 0.815f),   // R103：Main 中间那块黑巨石
            new Vector2(0.30f, 0.72f),    // R103：黑巨石左侧
            new Vector2(0.15f, 0.22f),
            new Vector2(0.85f, 0.28f),
            new Vector2(0.50f, 0.15f),
            new Vector2(0.24f, 0.18f),
            new Vector2(0.90f, 0.30f),
            new Vector2(0.86f, 0.24f),
            new Vector2(0.20f, 0.92f),
            new Vector2(0.45f, 0.92f),
            new Vector2(0.70f, 0.92f),
            new Vector2(0.45f, 0.82f),
            new Vector2(0.45f, 0.72f),
            new Vector2(0.45f, 0.60f),
        };

        // ── 给 OceanHeadland 换"远景陆地"专用材质（高度渐变 + 斑驳 + 雾）──
        [MenuItem("Tools/R64/91 远景陆地=专用材质(高度渐变)")]
        public static void FarLandMat()
        {
            var go = GameObject.Find("OceanHeadland");
            var r = go != null ? go.GetComponent<Renderer>() : null;
            if (r == null) { P("R64/91 REFUSED: 找不到 OceanHeadland\n"); return; }
            if (r.sharedMaterial != null && !EditorPrefs.HasKey("R64.origMat"))
                EditorPrefs.SetString("R64.origMat", r.sharedMaterial.name);

            const string p = "Assets/Art/Materials/Ocean_FarLand.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null)
            {
                var sh = Shader.Find("WaterProject/DistantLand");
                if (sh == null) { P("R64/91 REFUSED: 找不到 DistantLand shader\n"); return; }
                m = new Material(sh) { name = "Ocean_FarLand" };
                AssetDatabase.CreateAsset(m, p);
            }
            r.sharedMaterial = m;
            EditorUtility.SetDirty(r);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            P($"R64/91 OceanHeadland -> {m.name}（原 {EditorPrefs.GetString("R64.origMat", "?")}）\n");
        }

        [MenuItem("Tools/R64/91b 远景陆地材质还原")]
        public static void FarLandRevert()
        {
            var go = GameObject.Find("OceanHeadland");
            var r = go != null ? go.GetComponent<Renderer>() : null;
            if (r == null) { P("R64/91b REFUSED\n"); return; }
            string old = EditorPrefs.GetString("R64.origMat", "");
            if (string.IsNullOrEmpty(old)) { P("R64/91b 无存档\n"); return; }
            foreach (var g in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Art/Materials" }))
            {
                var mm = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g));
                if (mm != null && mm.name == old) { r.sharedMaterial = mm; break; }
            }
            EditorUtility.SetDirty(r);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            P($"R64/91b 材质还原 -> {old}\n");
        }
        // ── 给 OceanHeadland 的网格加起伏（把"平顶平板"变成"丘陵轮廓"）──
        //   材质只能改颜色，改变不了"平顶"这个几何事实（R63/R64 已验证）。
        //   做法：读原 Mesh → 按世界 XZ 的低频噪声抬降顶点 → 重算法线 → 存成新 Mesh 资产。
        //   只抬"高处"的顶点（低处/水线附近不动），避免戳穿沙滩。
        [MenuItem("Tools/R64/92 Ocean 崖体加起伏(网格置换)")]
        public static void DisplaceHeadland()
        {
            var go = GameObject.Find("OceanHeadland");
            var mf = go != null ? go.GetComponent<MeshFilter>() : null;
            if (mf == null || mf.sharedMesh == null) { P("R64/92 REFUSED: 找不到网格\n"); return; }

            var src = mf.sharedMesh;
            if (!src.isReadable) { P("R64/92 REFUSED: 网格不可读（需在导入设置里开 Read/Write）\n"); return; }
            if (!EditorPrefs.HasKey("R64.origMesh"))
                EditorPrefs.SetString("R64.origMesh", AssetDatabase.GetAssetPath(src) + "|" + src.name);

            var vs = src.vertices;
            var minY = float.MaxValue; var maxY = float.MinValue;
            foreach (var v in vs) { if (v.y < minY) minY = v.y; if (v.y > maxY) maxY = v.y; }
            float hRange = Mathf.Max(maxY - minY, 1f);
            P($"R64/92 原网格 verts={vs.Length} y=[{minY:F1},{maxY:F1}]\n");

            float hf = 0.0f; int moved = 0;
            for (int i = 0; i < vs.Length; i++)
            {
                float h01 = Mathf.InverseLerp(minY, maxY, vs[i].y);
                if (h01 < 0.35f) continue;                       // 低处不动，免得戳穿沙滩
                float wx = vs[i].x, wz = vs[i].z;
                float n = Mathf.PerlinNoise(wx * 0.012f + 13.7f, wz * 0.012f + 5.1f) - 0.5f;
                float n2 = Mathf.PerlinNoise(wx * 0.045f + 71.3f, wz * 0.045f + 22.9f) - 0.5f;
                float sy = Mathf.Max(go.transform.lossyScale.y, 1e-3f);   // 网格是归一化的，位移要按世界米换算
                float ampLocal = 9f / sy;                            // 目标：世界约 ±9 米起伏
                float d = (n * 2.0f + n2 * 0.8f) * ampLocal * Mathf.SmoothStep(0f, 1f, (h01 - 0.35f) / 0.65f);
                vs[i].y += d;
                hf += d; moved++;
            }

            var copy = Object.Instantiate(src);
            copy.name = src.name + "_Displaced";
            copy.vertices = vs;
            copy.RecalculateNormals();
            copy.RecalculateBounds();

            const string p = "Assets/Art/Meshes/OceanHeadland_Displaced.asset";
            System.IO.Directory.CreateDirectory("Assets/Art/Meshes");
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(p);
            if (old != null) AssetDatabase.DeleteAsset(p);
            AssetDatabase.CreateAsset(copy, p);

            mf.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(p);
            EditorUtility.SetDirty(mf);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            P($"R64/92 已置换：moved={moved}/{vs.Length} 平均抬升 {(moved > 0 ? hf / moved : 0):F2}m -> {p}\n");
        }

        [MenuItem("Tools/R64/92b Ocean 崖体网格还原")]
        public static void DisplaceRevert()
        {
            var go = GameObject.Find("OceanHeadland");
            var mf = go != null ? go.GetComponent<MeshFilter>() : null;
            if (mf == null) { P("R64/92b REFUSED\n"); return; }
            string key = EditorPrefs.GetString("R64.origMesh", "");
            if (string.IsNullOrEmpty(key)) { P("R64/92b 无存档\n"); return; }
            string path = key.Split('|')[0];
            var src = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (src != null) { mf.sharedMesh = src; P($"R64/92b 网格还原 -> {src.name}\n"); }
            else P($"R64/92b 需要手动恢复：{key}\n");
            EditorUtility.SetDirty(mf);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        }
        [MenuItem("Tools/R64/90 屏幕拾取(打印候选物体)")]
        public static void Pick()
        {
            var cam = Camera.main;
            if (cam == null) { P("R64/90 REFUSED: 没有 Main Camera\n"); return; }

            var renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            foreach (var pt in Points)
            {
                var ray = cam.ViewportPointToRay(new Vector3(pt.x, pt.y, 0f));
                var hits = new List<(float dist, string name, string mat)>();
                foreach (var r in renderers)
                {
                    if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                    if (r.bounds.IntersectRay(ray, out float d))
                        hits.Add((d, r.name, r.sharedMaterial != null ? r.sharedMaterial.name : "-"));
                }
                hits.Sort((a, b) => a.dist.CompareTo(b.dist));
                P($"R64 PICK ndc=({pt.x:F2},{pt.y:F2}) hits={hits.Count}\n");
                for (int i = 0; i < Mathf.Min(hits.Count, 5); i++)
                    P($"    [{i}] {hits[i].name}  mat={hits[i].mat}  dist={hits[i].dist:F0}m\n");
            }
        }
    }
}
#endif
