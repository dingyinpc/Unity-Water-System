// R66Setup.cs — R66：把 OceanHeadland 换成"程序化山脊"网格
//
// R63/R64 已证明：材质、雾、顶点位移都改不掉"平顶板"的观感 —— 因为原网格本身就是一块平顶体。
// 本文件做真正的替换：
//   1) 先把原网格**存成资产**（原网格是程序化的、没有资产路径，不备份就没法回滚）；
//   2) 用噪声生成"山脊 + 海岬"形状（外缘贴水、中间起脊、随机起伏）；
//   3) 赋给 MeshFilter，保持 transform 不变（尺寸/位置沿用）。
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    public static class R66Setup
    {
        const string MeshDir = "Assets/Art/Meshes";
        const string BackupPath = MeshDir + "/OceanHeadland_Original.asset";

        static void P(string s)
            => File.AppendAllText(Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        [MenuItem("Tools/R66/93 探针(变换/网格尺度)")]
        public static void Probe()
        {
            var go = GameObject.Find("OceanHeadland");
            if (go == null) { P("R66/93 REFUSED: 找不到 OceanHeadland\n"); return; }
            var mf = go.GetComponent<MeshFilter>();
            var mr = go.GetComponent<MeshRenderer>();
            var m = mf != null ? mf.sharedMesh : null;
            P($"R66 PROBE transform pos={go.transform.position} euler={go.transform.eulerAngles} " +
              $"lossyScale={go.transform.lossyScale}\n");
            if (m != null)
                P($"R66 PROBE mesh={m.name} verts={m.vertexCount} bounds={m.bounds} readable={m.isReadable}\n");
            if (mr != null) P($"R66 PROBE bounds(renderer)={mr.bounds}\n");
        }

        [MenuItem("Tools/R66/90 Ocean 崖体=程序化山脊")]
        public static void BuildRidge()
        {
            var go = GameObject.Find("OceanHeadland");
            var mf = go != null ? go.GetComponent<MeshFilter>() : null;
            if (mf == null || mf.sharedMesh == null) { P("R66/90 REFUSED: 找不到网格\n"); return; }

            Directory.CreateDirectory(MeshDir);

            // ① 备份原网格（只做一次）
            if (!EditorPrefs.HasKey("R66.origMesh"))
            {
                var orig = mf.sharedMesh;
                var bak = Object.Instantiate(orig);
                bak.name = "OceanHeadland_Original";
                var existed = AssetDatabase.LoadAssetAtPath<Mesh>(BackupPath);
                if (existed != null) AssetDatabase.DeleteAsset(BackupPath);
                AssetDatabase.CreateAsset(bak, BackupPath);
                EditorPrefs.SetString("R66.origMesh", BackupPath);
                P($"R66/90 已备份原网格 -> {BackupPath}\n");
            }

            var src = AssetDatabase.LoadAssetAtPath<Mesh>(BackupPath);
            if (src == null) { P("R66/90 REFUSED: 备份失败\n"); return; }

            var b = src.bounds;
            var mesh = BuildRidgeMesh(b, 72);
            const string p = MeshDir + "/OceanHeadland_Ridge.asset";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(p);
            if (old != null) AssetDatabase.DeleteAsset(p);
            AssetDatabase.CreateAsset(mesh, p);

            mf.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(p);
            EditorUtility.SetDirty(mf);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            P($"R66/90 新网格 verts={mesh.vertexCount} bounds={mesh.bounds} -> {p}\n");
        }

        [MenuItem("Tools/R66/90b Ocean 崖体网格还原")]
        public static void Restore()
        {
            var go = GameObject.Find("OceanHeadland");
            var mf = go != null ? go.GetComponent<MeshFilter>() : null;
            if (mf == null) { P("R66/90b REFUSED\n"); return; }
            string p = EditorPrefs.GetString("R66.origMesh", BackupPath);
            var src = AssetDatabase.LoadAssetAtPath<Mesh>(p);
            if (src == null) { P($"R66/90b 备份不存在: {p}\n"); return; }
            mf.sharedMesh = src;
            EditorUtility.SetDirty(mf);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            P($"R66/90b 已还原 -> {p}\n");
        }

        // ── 程序化山脊：外缘贴水（y→0）、中间起脊、长轴拉长、噪声起伏 ──
        static Mesh BuildRidgeMesh(Bounds srcBounds, int n)
        {
            // 沿用原网格的局部包围盒（transform 已有 579×55×484 的缩放）
            float hx = srcBounds.extents.x, hz = srcBounds.extents.z;
            float y0 = srcBounds.min.y;                 // 底面（贴水）
            float height = Mathf.Max(srcBounds.size.y, 0.5f);

            var verts = new List<Vector3>(n * n);
            var uvs = new List<Vector2>(n * n);
            var tris = new List<int>((n - 1) * (n - 1) * 6);

            for (int j = 0; j < n; j++)
            {
                float v = j / (float)(n - 1);
                for (int i = 0; i < n; i++)
                {
                    float u = i / (float)(n - 1);
                    float x = (u - 0.5f) * 2f * hx;
                    float z = (v - 0.5f) * 2f * hz;

                    // 归一化椭圆半径（长轴 x 拉长 1.35）
                    float rx = Mathf.Abs(x) / (hx * 1.35f);
                    float rz = Mathf.Abs(z) / (hz);
                    float r = Mathf.Sqrt(rx * rx + rz * rz);

                    float edge = Mathf.SmoothStep(1f, 0.15f, r);          // 外缘→0
                    float ridge = Mathf.Sin(Mathf.Clamp01(1f - r) * Mathf.PI * 1.2f);
                    float nx = Mathf.PerlinNoise(u * 5.5f + 11.3f, v * 5.5f + 7.9f) - 0.5f;
                    float nz = Mathf.PerlinNoise(u * 13f + 31.1f, v * 13f + 2.7f) - 0.5f;

                    float h = (ridge * 0.75f + nx * 0.55f + nz * 0.22f) * edge;
                    h = Mathf.Clamp01(h);

                    verts.Add(new Vector3(x, y0 + h * height, z));
                    uvs.Add(new Vector2(u, v));
                }
            }

            for (int j = 0; j < n - 1; j++)
                for (int i = 0; i < n - 1; i++)
                {
                    int a = j * n + i, b2 = a + 1, c = a + n, d = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b2);
                    tris.Add(b2); tris.Add(c); tris.Add(d);
                }

            var m = new Mesh { name = "OceanHeadland_Ridge" };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
#endif
