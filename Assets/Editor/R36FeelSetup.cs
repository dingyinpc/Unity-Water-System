// R36FeelSetup.cs —— 观感急救轮（用户批词：水的流动感/云/画质/云的流动）
//
// 诊断结论（2026-10-25 实测）：
//   · 云：FFT 序列 3.17s 互相关位移 **0px**（风参数在、shader 接了 _Time.y——待大风速实验分辨“太慢”还是“断线”）
//   · 画质：URP MSAA=1（关）、无任何 AA —— 几何/水线全锯齿
//   · 水流动感：Ocean _SwellSpeed 0.45（浪爬行）；Pond 微光漂移 R30 被砍半
// 本文件 = 三档修复 + 只读探针（96 号）。
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WaterProject.EditorTools
{
    internal static class R36FeelSetup
    {
        const string MatFolder = "Assets/Art/Materials";

        // ── ① 云的流动：把云层风速提一个量级（云在 900~2000m 高，15m/s 的角速度肉眼读不出）──
        [MenuItem("Tools/R36/90 云流动档(风×5)")]
        static void CloudTier()
        {
            foreach (var p in new[] { MatFolder + "/Sky_OpenCoast.mat", MatFolder + "/FFT/FFT_Sky.mat" })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (m == null) continue;
                m.SetFloat("_CloudWindSpeed", 100f);           // 固定值（幂等）：R36b 果冻修复后平移当主角
                AssetDatabase.SaveAssetIfDirty(m);
                Write($"R36 cloud {m.name}: wind -> 100\n");
            }
            var it = AssetDatabase.LoadAssetAtPath<Material>(MatFolder + "/Sky_IsletTropical.mat");
            if (it != null && it.HasProperty("_CloudDrift"))
            {
                it.SetVector("_CloudDrift", new Vector4(7f, 0f, 3f, 0f));    // R39 回基础档：×3 把 Islet sky E 抬 9×超带(R39 隔离实验实锤,Islet 云本来没被批)
                AssetDatabase.SaveAssetIfDirty(it);
                Write("R36 cloud IsletTropical: drift -> (21,0,9)\n");
            }
        }

        [MenuItem("Tools/R36/90b 云流动还原")]
        static void CloudRevert()
        {
            foreach (var (p, w) in new[] { (MatFolder + "/Sky_OpenCoast.mat", 15f), (MatFolder + "/FFT/FFT_Sky.mat", 15f) })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (m != null && m.HasProperty("_CloudWindSpeed"))
                { m.SetFloat("_CloudWindSpeed", w); AssetDatabase.SaveAssetIfDirty(m); }
            }
            var it = AssetDatabase.LoadAssetAtPath<Material>(MatFolder + "/Sky_IsletTropical.mat");
            if (it != null && it.HasProperty("_CloudDrift"))
            { it.SetVector("_CloudDrift", new Vector4(7f, 0f, 3f, 0f)); AssetDatabase.SaveAssetIfDirty(it); }
            Write("R36 cloud revert done\n");
        }

        // ── ② 水的流动感：Ocean 浪速回正常 + Pond 微光漂移恢复 ──
        [MenuItem("Tools/R36/91 水流动感档")]
        static void FlowTier()
        {
            var oc = AssetDatabase.LoadAssetAtPath<Material>(MatFolder + "/Ocean_Water.mat");
            if (oc != null)
            {
                oc.SetFloat("_SwellSpeed", 0.95f);        // 旧 0.45：涌浪爬行；回接近实速
                oc.SetFloat("_DetailFlow", 0.30f);        // 旧 0.16：皮肤纹理流动可读
                oc.SetFloat("_SwashSpeed", 0.20f);        // 旧 0.13：冲滩节奏加快
                AssetDatabase.SaveAssetIfDirty(oc);
            }
            var pd = AssetDatabase.LoadAssetAtPath<Material>(MatFolder + "/Water_Pond.mat");
            if (pd != null && pd.HasProperty("_ShimmerDrift"))
            {
                // R30 曾把潭亮斑漂移砍半；恢复并略提（若有该参数——探针会回读确认）
                pd.SetFloat("_ShimmerDrift", 1.6f);
                AssetDatabase.SaveAssetIfDirty(pd);
            }
            Write("R36 flow tier: ocean swell 0.95 / detailFlow 0.30 / swash 0.20\n");
        }

        [MenuItem("Tools/R36/91b 水流动感还原")]
        static void FlowRevert()
        {
            var oc = AssetDatabase.LoadAssetAtPath<Material>(MatFolder + "/Ocean_Water.mat");
            if (oc != null)
            {
                oc.SetFloat("_SwellSpeed", 0.45f); oc.SetFloat("_DetailFlow", 0.16f); oc.SetFloat("_SwashSpeed", 0.13f);
                AssetDatabase.SaveAssetIfDirty(oc);
            }
            var pd = AssetDatabase.LoadAssetAtPath<Material>(MatFolder + "/Water_Pond.mat");
            if (pd != null && pd.HasProperty("_ShimmerDrift"))
            { pd.SetFloat("_ShimmerDrift", 1f); AssetDatabase.SaveAssetIfDirty(pd); }
            Write("R36 flow revert done\n");
        }

        // ── ③ 画质：URP 开 MSAA 4x（现在全项目零 AA）──
        [MenuItem("Tools/R36/92 画质档(MSAA 4x)")]
        static void QualityTier()
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(
                "Assets/Settings/URP/WaterURPAsset.asset");
            if (asset == null) { Write("R36 REFUSED: WaterURPAsset 不存在\n"); return; }
            var so = new SerializedObject(asset);
            so.FindProperty("m_MSAA").intValue = 4;       // 1=off
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            Write("R36 quality: MSAA -> 4x\n");
        }

        [MenuItem("Tools/R36/92b 画质还原(MSAA off)")]
        static void QualityRevert()
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(
                "Assets/Settings/URP/WaterURPAsset.asset");
            if (asset == null) return;
            var so = new SerializedObject(asset);
            so.FindProperty("m_MSAA").intValue = 1;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            Write("R36 quality: MSAA -> off\n");
        }

        // ── 探针：当前场景天空/云参数/相机 AA ──
        [MenuItem("Tools/R36/96 天空与画质探针(只读)")]
        static void Probe()
        {
            var sb = new System.Text.StringBuilder();
            var sky = RenderSettings.skybox;
            sb.Append($"R36 PROBE scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name} " +
                      $"skybox={(sky != null ? sky.name + "/" + sky.shader.name : "<none>")}\n");
            if (sky != null)
            {
                if (sky.HasProperty("_CloudWindSpeed"))
                    sb.Append($"  cloudWind={sky.GetFloat("_CloudWindSpeed")} dir={sky.GetFloat("_CloudWindDir")} " +
                              $"cover={sky.GetFloat("_CloudCover")}\n");
                if (sky.HasProperty("_CloudDrift"))
                    sb.Append($"  cloudDrift={sky.GetVector("_CloudDrift")}\n");
            }
            var cam = Camera.main;
            if (cam != null)
            {
                var cd = cam.GetUniversalAdditionalCameraData();
                sb.Append($"  cam={cam.name} {cam.pixelWidth}x{cam.pixelHeight} aa={cd.antialiasing}\n");
            }
            var asset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            sb.Append($"  rp={(asset != null ? asset.name : "<none>")}\n");
            Write(sb.ToString());
        }

        static void Write(string s)
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s + "\n");
        }
    }
}
#endif
