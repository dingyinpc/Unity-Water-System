// R39IsleSetup.cs —— Islet 远景岛剪影（R31 Islet#4：参考右侧地平线有一座低矮深蓝灰剪影岛）
//
// 全部**加法**（新 GO/新资产，不碰既有对象）：即使岩型线将来合并场景冲突，
// 走"对方场景优先 + 重跑本菜单"即可恢复（菜单是唯一写入点的项目纪律）。
// 形态定标（参考 f0062/f0166）：贴地平线、占屏宽 3~4%、高约 1°、深蓝灰、轮廓模糊。
// 207 片头机位 (52,8.5,72) yaw215° → 主视线右侧 ≈ (-2700, 0, 1400) 方向放主岛；
// 更远处再放一座更小的次岛做纵深。
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Collections.Generic;

namespace WaterProject.EditorTools
{
    internal static class R39IsleSetup
    {
        const string MatPath = "Assets/Art/Materials/R39_IsleSilhouette.mat";
        const string MeshPath = "Assets/Art/Meshes/R39_DistantIsle.asset";

        [MenuItem("Tools/R39/90 Islet 远景岛剪影(装)")]
        static void Install()
        {
            // 旧物拆除（幂等）
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (go != null && (go.name == "R39_DistantIsle" || go.name.StartsWith("R39_Isle_")))
                    Object.DestroyImmediate(go);

            // 材质：Unlit 深蓝灰（远景剪影不需要光照层次；受场景雾自动融进地平线）
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (mat == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Unlit");
                mat = new Material(sh) { name = "R39_IsleSilhouette" };
                AssetDatabase.CreateAsset(mat, MatPath);
            }
            mat.SetColor("_BaseColor", new Color(0.23f, 0.29f, 0.35f));   // 深蓝灰（R31 定标）

            // 网格：脊线山丘 —— 底部椭圆轮廓 + 顶部起伏脊线，低模
            var mesh = BuildIsleMesh(new System.Random(2026102539));
            AssetDatabase.DeleteAsset(MeshPath);
            AssetDatabase.CreateAsset(mesh, MeshPath);

            // 主岛：207 机位右侧远处；次岛：更远更小（纵深点缀）
            SpawnIsle("R39_Isle_Main", new Vector3(-2700f, 0f, 1400f), 1.0f, 35f);
            SpawnIsle("R39_Isle_Far", new Vector3(1750f, 0f, -3050f), 0.6f, 55f);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Write("R39 install: 2 isles (main right-of-view / far depth) + silhouette mat\n");
        }

        [MenuItem("Tools/R39/90b Islet 远景岛拆除")]
        static void Uninstall()
        {
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (go != null && (go.name == "R39_DistantIsle" || go.name.StartsWith("R39_Isle_")))
                    Object.DestroyImmediate(go);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Write("R39 uninstall done\n");
        }

        // ── R40② Islet 低太阳暖光档（R31 #5：参考片头太阳低垂暖光 vs 我们正午侧光）──
        [MenuItem("Tools/R39/91 Islet 低太阳暖光档")]
        static void LowSun()
        {
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional && l.name != "MainPlanarCam")
                {
                    l.transform.rotation = Quaternion.Euler(24f, 235f, 0f);   // 低垂侧逆光
                    l.color = new Color(1.0f, 0.84f, 0.64f);                  // 暖金
                    l.intensity = 2.6f;
                    EditorUtility.SetDirty(l);
                }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Write("R40 low sun: elev24 yaw235 warm\n");
        }

        [MenuItem("Tools/R39/91b Islet 低太阳还原(75/白)")]
        static void LowSunRevert()
        {
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional)
                {
                    l.transform.rotation = Quaternion.Euler(75f, 235f, 0f);
                    l.color = new Color(1f, 0.96f, 0.88f); l.intensity = 2.9f;
                    EditorUtility.SetDirty(l);
                }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Write("R40 low sun revert\n");
        }

        [MenuItem("Tools/R39/99 探针(只读)")]
        static void Probe()
        {
            int n = 0;
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (go.name.StartsWith("R39_Isle_")) n++;
            Write($"R39 PROBE isles={n} scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}\n");
        }

        static void SpawnIsle(string name, Vector3 at, float scale, float yaw)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh =
                AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            go.AddComponent<MeshRenderer>().sharedMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            go.transform.position = at;
            go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            go.transform.localScale = new Vector3(scale, scale * (0.8f + scale * 0.3f), scale);
        }

        // 低模脊线山丘：椭圆底轮廓 + 沿长轴起伏的脊点 + 底裙边（"坐在线上"）
        static Mesh BuildIsleMesh(System.Random rng)
        {
            const int seg = 14;
            var verts = new List<Vector3> { Vector3.zero };   // [0] 峰点
            var tris = new List<int>();

            for (int i = 0; i < seg; i++)                     // [1..seg] 脊点
            {
                float a = Mathf.PI * 2f * i / seg;
                float x = Mathf.Cos(a), z = Mathf.Sin(a) * 0.55f;   // 底椭圆 1.0×0.55
                float ridge = Mathf.Clamp01(1f - Mathf.Abs(x) * 0.9f);
                float h = 0.28f + ridge * 0.72f + ((float)rng.NextDouble() - 0.5f) * 0.10f;
                verts.Add(new Vector3(x, h, z));
            }
            int skirt = seg + 1;                              // [seg+1..2seg] 底点
            for (int i = 0; i < seg; i++)
                verts.Add(new Vector3(verts[1 + i].x, -0.02f, verts[1 + i].z));

            for (int i = 0; i < seg; i++)
            {
                int a = 1 + i, b = 1 + (i + 1) % seg;
                int a2 = skirt + i, b2 = skirt + (i + 1) % seg;
                tris.Add(0); tris.Add(b); tris.Add(a);       // 峰扇面
                tris.Add(a); tris.Add(b2); tris.Add(a2);     // 底裙
                tris.Add(a); tris.Add(b); tris.Add(b2);
            }

            var mesh = new Mesh { name = "R39_DistantIsle" };
            mesh.SetVertices(verts); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
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
