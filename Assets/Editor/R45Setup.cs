// R45Setup.cs —— 一轮三小件（R41 B 组收尾）
// ① Ocean 沙岩：沙粒开（Style/207 复用）+ 圆润岩换网格（fbm 位移球，替换 Ocean_Rock/Cliff 使用者）
// ② 云缘碎边：Sky_Coast 侵蚀 0.22→0.38（shader 已改，本菜单只留档）
// ③ 瀑布水雾：Main 瀑布底粒子系统
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Collections.Generic;

namespace WaterProject.EditorTools
{
    internal static class R45Setup
    {
        const string SmoothRock = "Assets/Art/Meshes/R45_RockSmooth.asset";
        const string MistMat = "Assets/Art/Materials/R45_Mist.mat";

        // ── ① 圆润岩：把场景里用 Ocean_Rock/Ocean_Cliff 材质的渲染器换成 fbm 位移球 ──
        [MenuItem("Tools/R45/90 Ocean 圆润岩换装")]
        static void SmoothRocks()
        {
            var mesh = BuildSmoothRock();
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(SmoothRock);
            if (old == null) AssetDatabase.CreateAsset(mesh, SmoothRock);
            else { AssetDatabase.SaveAssetIfDirty(old); mesh = old; }
            // ⚠ 简化：每次重建资产太重，第二次起复用（首跑生成）
            var use = AssetDatabase.LoadAssetAtPath<Mesh>(SmoothRock) ?? mesh;

            int n = 0;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                var mn = r.sharedMaterial != null ? r.sharedMaterial.name : "";
                bool isRockMat = mn.StartsWith("Ocean_") &&
                    !mn.Contains("Water") && !mn.Contains("Sand") && !mn.Contains("Wet");
                if (!isRockMat) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || mf.sharedMesh.name == use.name) continue;
                mf.sharedMesh = use; n++;
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Write($"R45 smooth rocks swapped={n}\n");
        }

        // ── ③ 瀑布水雾粒子 ──
        [MenuItem("Tools/R45/91 Main 瀑布水雾(装)")]
        static void Mist()
        {
            var old = GameObject.Find("R43_Mist");
            if (old != null) Object.DestroyImmediate(old);

            var mat = AssetDatabase.LoadAssetAtPath<Material>(MistMat);
            if (mat == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (sh == null) { Write("R45 REFUSED: URP 粒子 shader 缺\n"); return; }
                mat = new Material(sh) { name = "R45_Mist" };
                AssetDatabase.CreateAsset(mat, MistMat);
            }
            mat.SetColor("_BaseColor", new Color(0.92f, 0.96f, 0.98f, 0.35f));
            mat.SetFloat("_Surface", 1f);           // Transparent
            mat.SetFloat("_Blend", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 3080;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

            var go = new GameObject("R43_Mist");
            var ps = go.AddComponent<ParticleSystem>();
            var mm = ps.GetComponent<ParticleSystemRenderer>();
            mm.sharedMaterial = mat;

            var main = ps.main;
            main.loop = true; main.playOnAwake = true;
            main.duration = 3f; main.startLifetime = 1.6f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.7f, 1.7f);
            main.startColor = new Color(0.95f, 0.97f, 0.99f, 0.32f);
            main.gravityModifier = -0.06f;          // 微微上飘
            main.maxParticles = 160;
            main.scalingMode = ParticleSystemScalingMode.Local;

            var em = ps.emission;
            em.rateOverTime = 55f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 2.2f;
            shape.arc = 360f;

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.y = new ParticleSystem.MinMaxCurve(0.6f, 1.3f);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                AnimationCurve.EaseInOut(0f, 0.35f, 1f, 1.6f));

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.55f, 0.25f),
                        new GradientAlphaKey(0.25f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            go.transform.position = new Vector3(0f, 0.3f, 10.6f);   // 瀑布落水处（R43 foam 同源）
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Write("R45 mist installed\n");
        }

        [MenuItem("Tools/R45/91b Main 瀑布水雾(拆)")]
        static void MistOff()
        {
            var go = GameObject.Find("R43_Mist");
            if (go != null) Object.DestroyImmediate(go);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Write("R45 mist removed\n");
        }

        [MenuItem("Tools/R45/99 探针(只读)")]
        static void Probe()
        {
            int smooth = 0;
            var use = AssetDatabase.LoadAssetAtPath<Mesh>(SmoothRock);
            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
                if (use != null && mf.sharedMesh == use) smooth++;
            Write($"R45 PROBE smoothRockUsers={smooth} mist={(GameObject.Find("R43_Mist") != null)} " +
                  $"grain={(AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Ocean_Sand.mat")?.GetFloat("_GrainAmount"))}\n");
        }

        // fbm 位移球（圆润低模岩）：UV 球 2 细分 + 三频径向噪声
        static Mesh BuildSmoothRock()
        {
            const int segU = 18, segV = 12;
            var v = new List<Vector3>();
            for (int y = 0; y <= segV; y++)
            {
                float ph = Mathf.PI * y / segV;
                for (int x = 0; x <= segU; x++)
                {
                    float th = Mathf.PI * 2f * x / segU;
                    Vector3 d = new Vector3(Mathf.Sin(ph) * Mathf.Cos(th), Mathf.Cos(ph), Mathf.Sin(ph) * Mathf.Sin(th));
                    // 三频径向噪声：大形态压扁 + 中频鼓包 + 细频糙面
                    float n = 0.62f
                            + 0.22f * Mathf.PerlinNoise(d.x * 1.7f + 3.1f, d.z * 1.7f - d.y * 1.1f)
                            + 0.10f * Mathf.PerlinNoise(d.x * 4.3f - 7.7f, d.y * 4.3f + d.z * 2.9f)
                            + 0.04f * Mathf.PerlinNoise(d.x * 9.1f, d.z * 9.1f + d.y * 5.3f);
                    n *= 1f - 0.30f * d.y * d.y;                 // 顶略压扁
                    v.Add(d * n);
                }
            }
            var tris = new List<int>();
            for (int y = 0; y < segV; y++)
                for (int x = 0; x < segU; x++)
                {
                    int a = y * (segU + 1) + x, b = a + 1;
                    int c = a + segU + 1, dd = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(dd);
                }
            var mesh = new Mesh { name = "R45_RockSmooth" };
            mesh.SetVertices(v); mesh.SetTriangles(tris, 0);
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
