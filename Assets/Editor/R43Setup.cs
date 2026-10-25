// R43Setup.cs —— Main 瀑布悬崖（R41 B1：参考的标志性元素）
// 全加法：低模岩壁（复用 BuildFacetedRock 放大）+ 外弓瀑布带 + 落水泡沫盘。
// R43/90 装、90b 拆、99 探针。位置：主塘远端（相机对面的视觉焦点）。
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Collections.Generic;

namespace WaterProject.EditorTools
{
    internal static class R43Setup
    {
        const string MatFall = "Assets/Art/Materials/R43_WaterFall.mat";
        const string MatFoam = "Assets/Art/Materials/R43_FallFoam.mat";

        [MenuItem("Tools/R43/90 Main 瀑布悬崖(装)")]
        static void Install()
        {
            Clear();
            var sh = Shader.Find("WaterProject/WaterFall");
            if (sh == null) { Write("R43 REFUSED: WaterFall shader 未编译\n"); return; }

            var mf = AssetDatabase.LoadAssetAtPath<Material>(MatFall);
            if (mf == null) { mf = new Material(sh) { name = "R43_WaterFall" }; AssetDatabase.CreateAsset(mf, MatFall); }
            mf.SetFloat("_Mode", 0f);
            var mfm = AssetDatabase.LoadAssetAtPath<Material>(MatFoam);
            if (mfm == null) { mfm = new Material(sh) { name = "R43_FallFoam" }; AssetDatabase.CreateAsset(mfm, MatFoam); }
            mfm.SetFloat("_Mode", 1f); mfm.SetFloat("_Speed", 0.8f); mfm.SetFloat("_StreakScale", 7f);

            // ── 岩壁：两三块放大的低模岩叠出 4m 崖 ──（主塘远端 z≈-11 一带）
            var rockMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Ocean_Rock.mat");
            var rocks = new Mesh[3];
            for (int v = 0; v < 3; v++) rocks[v] = WaterProjectSetup.BuildFacetedRock(5 + v, 1.4f);
            PlaceRock(rocks[0], rockMat, "R43_Rock_A", new Vector3(-2.6f, 1.5f, 11.4f), 4.6f, 15f);
            PlaceRock(rocks[1], rockMat, "R43_Rock_B", new Vector3(2.9f, 1.2f, 11.6f), 4.2f, -40f);
            PlaceRock(rocks[2], rockMat, "R43_Rock_C", new Vector3(0.1f, 0.6f, 11.2f), 2.6f, 170f);

            // ── 瀑布带：外弓 ribbon（顶 3.6m → 水面 0.05m），宽 3.2m ──
            var fallGO = new GameObject("R43_Fall");
            fallGO.AddComponent<MeshFilter>().sharedMesh = BuildRibbon(5.4f, 3.55f, 0.5f);
            fallGO.AddComponent<MeshRenderer>().sharedMaterial = mf;
            fallGO.transform.position = new Vector3(0f, 0.05f, 11.05f);

            // ── 落水泡沫盘：水面上的椭圆（3.6×2.4m）──
            var foamGO = new GameObject("R43_FallFoam");
            foamGO.AddComponent<MeshFilter>().sharedMesh = BuildDisc(5.8f, 3.4f);
            foamGO.AddComponent<MeshRenderer>().sharedMaterial = mfm;
            foamGO.transform.position = new Vector3(0f, 0.06f, 10.4f);
            foamGO.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Write("R43 install: 3 rocks + fall ribbon + foam disc\n");
        }

        [MenuItem("Tools/R43/90b Main 瀑布拆除")]
        static void Uninstall() { Clear(); EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene()); Write("R43 uninstall\n"); }

        [MenuItem("Tools/R43/99 探针(只读)")]
        static void Probe()
        {
            int n = 0;
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (go.name.StartsWith("R43_")) n++;
            Write($"R43 PROBE objects={n} scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}\n");
        }

        static void Clear()
        {
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (go != null && go.name.StartsWith("R43_")) Object.DestroyImmediate(go);
        }

        static void PlaceRock(Mesh m, Material mat, string name, Vector3 at, float s, float yaw)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var rr = go.AddComponent<MeshRenderer>(); rr.sharedMaterial = mat;
            rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // v2 教训：大岩投影罩黑全场
            go.transform.position = at;
            go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            go.transform.localScale = Vector3.one * s;
        }

        // 外弓竖带：XZ 平面外弓，V 从底(0)到顶(1)
        static Mesh BuildRibbon(float w, float h, float bow)
        {
            const int segX = 8, segY = 6;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<int>();
            for (int y = 0; y <= segY; y++)
                for (int x = 0; x <= segX; x++)
                {
                    float fx = (float)x / segX, fy = (float)y / segY;
                    float px = (fx - 0.5f) * w;
                    float pz = -bow * Mathf.Sin(fx * Mathf.PI) * (0.6f + 0.4f * fy);  // 中段外弓、顶部略收
                    v.Add(new Vector3(px, fy * h, pz));
                    uv.Add(new Vector2(fx, fy));
                }
            for (int y = 0; y < segY; y++)
                for (int x = 0; x < segX; x++)
                {
                    int a = y * (segX + 1) + x, b = a + 1, c = a + segX + 1, d = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }
            var mesh = new Mesh { name = "R43_FallRibbon" };
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // 椭圆盘（XZ 本地平面，uv 0..1）
        static Mesh BuildDisc(float w, float d)
        {
            const int seg = 40;
            var v = new List<Vector3> { Vector3.zero };
            var uv = new List<Vector2> { new Vector2(0.5f, 0.5f) };
            var tris = new List<int>();
            for (int i = 0; i < seg; i++)
            {
                float a = Mathf.PI * 2f * i / seg;
                v.Add(new Vector3(Mathf.Cos(a) * w * 0.5f, 0, Mathf.Sin(a) * d * 0.5f));
                uv.Add(new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f));
            }
            for (int i = 0; i < seg; i++)
            { tris.Add(0); tris.Add(1 + (i + 1) % seg); tris.Add(1 + i); }
            var mesh = new Mesh { name = "R43_FoamDisc" };
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static void Write(string s)
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s + "\n");
        }
    }
}
#endif
