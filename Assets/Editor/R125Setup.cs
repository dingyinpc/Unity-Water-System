// R125Setup.cs — R125 结构级双修（用户指令：把"①FFT 网格方块 ②Ocean 水面点阵"合成一轮做）
//
// 90  FFT 环形 LOD 网格（近密远疏）——把"贴水面平视时一格几百像素"的方块消掉
// 90b FFT 均匀网格还原（A/B 用）
// 91  Ocean 亮闪关（_SparkleAmt=0，A/B 用）
// 91b Ocean 亮闪开（_SparkleAmt=1）
// 92  探针：打印当前水面网格的格子尺寸分布
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace WaterProject.EditorTools
{
    internal static class R125Setup
    {
        const string FFTMeshPath = "Assets/Art/Meshes/FFT_WaterGrid.asset";
        const string OceanMat = "Assets/Art/Materials/Ocean_Water.mat";

        static void P(string s) => System.IO.File.AppendAllText(
            System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        // ── 网格填充（就地改资产，GUID 不变 ⇒ 场景引用不会断，A/B 只是重填一次）──
        // warpA = 0 → 均匀网格；warpA>0 → 以 center 为中心，中心格距 ×warpA，边缘 ×(3-2·warpA)
        static void FillGrid(Mesh mesh, int seg, float size, float warpA, Vector2 center)
        {
            int n = seg + 1;
            var v = new Vector3[n * n];
            var uv = new Vector2[n * n];
            float half = size * 0.5f;

            for (int z = 0; z < n; z++)
            {
                float uz = (float)z / seg * 2f - 1f;
                float wz = Warp(uz, warpA) * half + center.y;
                for (int x = 0; x < n; x++)
                {
                    float ux = (float)x / seg * 2f - 1f;
                    float wx = Warp(ux, warpA) * half + center.x;
                    v[z * n + x] = new Vector3(wx, 0f, wz);
                    uv[z * n + x] = new Vector2((float)x / seg, (float)z / seg);
                }
            }

            int[] idx = new int[seg * seg * 6];
            int w = 0;
            for (int z = 0; z < seg; z++)
                for (int x = 0; x < seg; x++)
                {
                    int a = z * n + x, b = a + 1, c = a + n, d = c + 1;
                    idx[w++] = a; idx[w++] = c; idx[w++] = b;
                    idx[w++] = b; idx[w++] = c; idx[w++] = d;
                }

            mesh.Clear();
            mesh.indexFormat = IndexFormat.UInt32;   // >65535 顶点必须 32 位索引
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(idx, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var pad = mesh.bounds;
            pad.Expand(new Vector3(0f, 8f, 0f));     // 顶点会被浪推走
            mesh.bounds = pad;

            // 格子尺寸统计（沿中心一行取相邻两点距离）
            float minStep = float.MaxValue, maxStep = 0f;
            for (int x = 1; x < n; x++)
            {
                float d = Vector3.Distance(v[x], v[x - 1]);
                if (d < minStep) minStep = d;
                if (d > maxStep) maxStep = d;
            }
            P($"R125 grid seg={seg} size={size} warp={warpA:F2} verts={v.Length} 格距 {minStep:F2}~{maxStep:F2} m\n");
        }

        static float Warp(float u, float a)
        {
            if (a <= 0f) return u;
            float s = Mathf.Sign(u), t = Mathf.Abs(u);
            return s * (a * t + (1f - a) * t * t * t);
        }

        static Mesh EnsureMesh()
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(FFTMeshPath);
            if (mesh == null)
            {
                // 场景里没有网格资产就先建一个（正常 FFT/90 已建过）
                if (!AssetDatabase.IsValidFolder("Assets/Art/Meshes"))
                    AssetDatabase.CreateFolder("Assets/Art", "Meshes");
                mesh = new Mesh { name = "FFT_WaterGrid" };
                AssetDatabase.CreateAsset(mesh, FFTMeshPath);
            }
            return mesh;
        }

        static Vector2 CamXZ()
        {
            var cam = Camera.main;
            if (cam == null) return Vector2.zero;
            return new Vector2(cam.transform.position.x, cam.transform.position.z);
        }

        [MenuItem("Tools/R125/90 FFT 环形LOD网格(近密远疏)")]
        public static void FFTRingLod()
        {
            if (UnityEngine.Application.isPlaying) P("R125/90 注意：在 Play 里换网格（允许，下一帧生效）\n");
            var mesh = EnsureMesh();
            // 384 段 + warp 0.22：中心 0.34m（原来均匀 2.34m，密 7 倍），边缘 4.1m
            FillGrid(mesh, 384, 600f, 0.22f, CamXZ());
            EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssetIfDirty(mesh);
            P($"R125/90 已换成环形 LOD 网格（中心=相机 XZ {CamXZ()})\n");
        }

        [MenuItem("Tools/R125/90b FFT 均匀网格还原")]
        public static void FFTUniform()
        {
            if (UnityEngine.Application.isPlaying) P("R125/90b 注意：在 Play 里换网格（允许，下一帧生效）\n");
            var mesh = EnsureMesh();
            FillGrid(mesh, 256, 600f, 0f, Vector2.zero);
            EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssetIfDirty(mesh);
            P("R125/90b 已还原均匀网格 256×256 / 600m\n");
        }

        static Material Ocean()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(OceanMat);
            if (m == null) P("R125 REFUSED: 无 Ocean_Water.mat\n");
            return m;
        }

        static void SetF(string key, float v)
        {
            var m = Ocean();
            if (m == null) return;
            m.SetFloat(key, v);
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);
            P($"R125 Ocean_Water.{key} = {v:F1}\n");
        }

        [MenuItem("Tools/R125/91 Ocean 亮闪=旧路径(A/B)")]
        public static void SparkleOld() => SetF("_SparkleLOD", 0f);

        [MenuItem("Tools/R125/91b Ocean 亮闪=新路径(A/B)")]
        public static void SparkleNew() => SetF("_SparkleLOD", 1f);

        [MenuItem("Tools/R125/94 Ocean 水面细节=LOD淡出(推荐)")]
        public static void DetailLodOn() => SetF("_DetailLOD", 1f);

        [MenuItem("Tools/R125/94b Ocean 水面细节=旧路径(A/B)")]
        public static void DetailLodOff() => SetF("_DetailLOD", 0f);

        [MenuItem("Tools/R125/95 Ocean 水面细节=柔档(-40%)")]
        public static void DetailSoft()
        {
            var m = Ocean(); if (m == null) return;
            m.SetFloat("_DetailStrength", 0.18f);
            m.SetFloat("_RippleStrength", 0.06f);
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m);
            P("R125/95 细节 0.30/0.10 -> 0.18/0.06\n");
        }

        [MenuItem("Tools/R125/95b Ocean 水面细节=原档还原")]
        public static void DetailHard()
        {
            var m = Ocean(); if (m == null) return;
            m.SetFloat("_DetailStrength", 0.30f);
            m.SetFloat("_RippleStrength", 0.10f);
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m);
            P("R125/95b 细节还原 0.30/0.10\n");
        }

        [MenuItem("Tools/R125/93 Ocean 亮闪全关(隔离用)")]
        public static void SparkleOff() => SetF("_SparkleAmt", 0f);

        [MenuItem("Tools/R125/93b Ocean 亮闪全开(还原)")]
        public static void SparkleOn() => SetF("_SparkleAmt", 1f);

        // ── 96：一次性关掉所有"程序化细节"来源（定位碎点到底是谁）──
        [MenuItem("Tools/R125/96 关掉全部程序化细节(隔离)")]
        public static void KillAllDetail()
        {
            var m = Ocean(); if (m == null) return;
            m.SetFloat("_ShimmerAmt", 0f);
            m.SetFloat("_SparkleAmt", 0f);
            m.SetFloat("_DetailStrength", 0f);
            m.SetFloat("_RippleStrength", 0f);
            m.SetFloat("_FoamHistStrength", 0f);
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m);
            P("R125/96 已关: Shimmer/Sparkle/Detail/Ripple/FoamHist\n");
        }

        [MenuItem("Tools/R125/96b 还原全部程序化细节")]
        public static void RestoreAllDetail()
        {
            var m = Ocean(); if (m == null) return;
            m.SetFloat("_ShimmerAmt", 1f);
            m.SetFloat("_SparkleAmt", 1f);
            m.SetFloat("_DetailStrength", 0.30f);
            m.SetFloat("_RippleStrength", 0.10f);
            m.SetFloat("_FoamHistStrength", 0.6f);
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m);
            P("R125/96b 已还原全部程序化细节\n");
        }

        [MenuItem("Tools/R125/97 只关水面微光(Shimmer)")]
        public static void KillShimmer() => SetF("_ShimmerAmt", 0f);

        [MenuItem("Tools/R125/97b 水面微光还原")]
        public static void RestoreShimmer() => SetF("_ShimmerAmt", 1f);

        [MenuItem("Tools/R125/98 只关泡沫历史")]
        public static void KillFoamHist() => SetF("_FoamHistStrength", 0f);

        [MenuItem("Tools/R125/98b 泡沫历史还原")]
        public static void RestoreFoamHist() => SetF("_FoamHistStrength", 0.6f);

        [MenuItem("Tools/R125/89 泡沫历史衰减=(40,200) 默认")]
        public static void FoamRangeFar() { var m = Ocean(); if (m == null) return; m.SetVector("_FoamHistRange", new Vector4(40, 200, 0, 0)); EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m); P("R125/89 _FoamHistRange=(40,200)\n"); }

        [MenuItem("Tools/R125/89b 泡沫历史衰减=(25,90) 强")]
        public static void FoamRangeNear() { var m = Ocean(); if (m == null) return; m.SetVector("_FoamHistRange", new Vector4(25, 90, 0, 0)); EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m); P("R125/89b _FoamHistRange=(25,90)\n"); }

        [MenuItem("Tools/R125/89c 泡沫历史衰减=关(旧路径)")]
        public static void FoamLodOff() => SetF("_FoamHistLOD", 0f);

        [MenuItem("Tools/R125/89d 泡沫历史衰减=开")]
        public static void FoamLodOn() => SetF("_FoamHistLOD", 1f);

        [MenuItem("Tools/R125/99 泡沫历史 bias=0.50")]
        public static void FoamBias50() => SetF("_FoamHistBias", 0.50f);

        [MenuItem("Tools/R125/99b 泡沫历史 bias=0.65")]
        public static void FoamBias65() => SetF("_FoamHistBias", 0.65f);

        [MenuItem("Tools/R125/99c 泡沫历史 bias=0.30(还原)")]
        public static void FoamBias30() => SetF("_FoamHistBias", 0.30f);

        [MenuItem("Tools/R125/99d 泡沫历史强度=0.35")]
        public static void FoamStr35() => SetF("_FoamHistStrength", 0.35f);

        [MenuItem("Tools/R125/92 探针: FFT 网格格子尺寸")]
        public static void ProbeGrid()
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(FFTMeshPath);
            if (mesh == null) { P("R125/92 无 FFT 网格资产\n"); return; }
            var vs = mesh.vertices;
            int n = Mathf.RoundToInt(Mathf.Sqrt(vs.Length));
            // 取网格正中间那一行（z = 中心）看格距沿 x 的分布
            int row = (n / 2) * n;
            float min = float.MaxValue, max = 0f; float s = 0f;
            for (int x = 1; x < n; x++)
            {
                float d = Vector3.Distance(vs[row + x], vs[row + x - 1]);
                min = Mathf.Min(min, d); max = Mathf.Max(max, d); s += d;
            }
            P($"R125/92 FFT 网格 {n}×{n} 顶点={vs.Length} 中心行格距 min={min:F3} max={max:F3} 平均={s / (n - 1):F3} m\n");
        }
    }
}
#endif
