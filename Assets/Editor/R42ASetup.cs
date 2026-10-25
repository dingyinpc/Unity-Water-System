// R42ASetup.cs —— A 组参数一轮收（R41 审计的 A1~A7）
// A1 云体积(底再深) A2 太阳光晕(Apply 公式抬高正午底值) A3 Pond 过曝回落
// A4 Main 水色蓝移 A5 Pool 高频焦散档(与大块档互切) A6 Ocean 近岸透沙 A7 FFT 高光离散
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WaterProject.EditorTools
{
    internal static class R42ASetup
    {
        const string M = "Assets/Art/Materials";

        // A1+A2：天空（Ocean/FFT 两块 Sky_Coast）
        [MenuItem("Tools/R42/90 天空档(云底0.75+太阳光晕底值)")]
        static void SkyTier()
        {
            foreach (var p in new[] { M + "/Sky_OpenCoast.mat", M + "/FFT/FFT_Sky.mat" })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (m == null) continue;
                m.SetFloat("_CloudBaseDark", 0.75f);   // R32 定 0.5；盲判"体积感不足"再进一档
                AssetDatabase.SaveAssetIfDirty(m);
            }
            // 太阳光晕：DayNightCycle.Apply 在 |elev|>45° 时把 _SunGlow 压到 0.15——
            // 正午太阳消失的主因。抬高底值（正午 0.15→0.40，低角不变）。
            var dn = Object.FindFirstObjectByType<WaterProject.Logic.DayNightCycle>();
            if (dn != null)
            {
                dn.sunGlowFloor = 0.40f;      // 新字段（DayNightCycle 同步加了）
                dn.Apply(true);
                EditorUtility.SetDirty(dn);
                EditorSceneManager.MarkSceneDirty(dn.gameObject.scene);
            }
            Write("R42 sky tier: baseDark=0.75 + sunGlowFloor=0.40\n");
        }

        [MenuItem("Tools/R42/90b 天空还原(0.5+无底值)")]
        static void SkyRevert()
        {
            foreach (var p in new[] { M + "/Sky_OpenCoast.mat", M + "/FFT/FFT_Sky.mat" })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (m != null) { m.SetFloat("_CloudBaseDark", 0.5f); AssetDatabase.SaveAssetIfDirty(m); }
            }
            var dn = Object.FindFirstObjectByType<WaterProject.Logic.DayNightCycle>();
            if (dn != null)
            {
                dn.sunGlowFloor = 0.15f; dn.Apply(true);
                EditorUtility.SetDirty(dn); EditorSceneManager.MarkSceneDirty(dn.gameObject.scene);
            }
            Write("R42 sky revert\n");
        }

        // A3：Pond 过曝回落（R33 散射冲到 0.75 的回调）
        [MenuItem("Tools/R42/91 Pond 抗过曝档")]
        static void PondTier()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(M + "/Water_Pond.mat");
            if (m == null) return;
            m.SetFloat("_ScatterStrength", 0.50f);   // R33 0.75→0.50（盲判"大片纯白"）
            m.SetFloat("_ScatterGain", 2.0f);        // 2.6→2.0
            AssetDatabase.SaveAssetIfDirty(m);
            Write("R42 pond tier: scatter 2.0/0.50\n");
        }

        [MenuItem("Tools/R42/91b Pond 还原(2.6/0.75)")]
        static void PondRevert()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(M + "/Water_Pond.mat");
            if (m != null)
            { m.SetFloat("_ScatterStrength", 0.75f); m.SetFloat("_ScatterGain", 2.6f); AssetDatabase.SaveAssetIfDirty(m); }
            Write("R42 pond revert\n");
        }

        // A4：Main 水色蓝移（灰绿→蓝亮）
        [MenuItem("Tools/R42/92 Main 水色蓝移档")]
        static void MainTier()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(M + "/Water_Advanced.mat");
            if (m == null) return;
            m.SetColor("_ShallowColor", new Color(0.24f, 0.55f, 0.62f, 1f));   // 旧见探针（灰绿系）
            m.SetColor("_DeepColor", new Color(0.030f, 0.105f, 0.185f, 1f));
            AssetDatabase.SaveAssetIfDirty(m);
            Write("R42 main tier: 水色蓝移\n");
        }

        [MenuItem("Tools/R42/92b Main 水色还原")]
        static void MainRevert()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(M + "/Water_Advanced.mat");
            if (m != null)
            {
                m.SetColor("_ShallowColor", new Color(0.200f, 0.520f, 0.480f, 1f));   // R42 探针实读基线
                m.SetColor("_DeepColor", new Color(0.078f, 0.340f, 0.430f, 1f));
                AssetDatabase.SaveAssetIfDirty(m);
            }
            Write("R42 main revert\n");
        }

        // A5：Pool 高频焦散丝线档（与 R32 大块档互切；R32 档=92b 的还原）
        [MenuItem("Tools/R42/93 Pool 高频焦散丝线档")]
        static void PoolFine()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(M + "/Water_Pool.mat");
            if (m == null) return;
            m.SetFloat("_CausticsScale", 0.85f);     // R32 大块 0.34 的另一端（细密丝线）
            m.SetFloat("_CausticDepthMix", 0.25f);
            m.SetFloat("_CausticSharp", 2.6f);
            m.SetFloat("_CausticsStrength", 0.50f);
            AssetDatabase.SaveAssetIfDirty(m);
            Write("R42 pool fine caustic tier\n");
        }

        [MenuItem("Tools/R42/93b Pool 还原大块档(R32)")]
        static void PoolRevert()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(M + "/Water_Pool.mat");
            if (m != null)
            {
                m.SetFloat("_CausticsScale", 0.34f); m.SetFloat("_CausticDepthMix", 0.60f);
                m.SetFloat("_CausticSharp", 3.5f); m.SetFloat("_CausticsStrength", 0.62f);
                AssetDatabase.SaveAssetIfDirty(m);
            }
            Write("R42 pool revert (R32 big-block)\n");
        }

        // A6：Ocean 近岸透沙
        [MenuItem("Tools/R42/94 Ocean 近岸透沙档")]
        static void OceanTier()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(M + "/Ocean_Water.mat");
            if (m == null) return;
            m.SetColor("_ShallowColor", new Color(0.34f, 0.68f, 0.62f, 1f));   // 旧 (0.15,0.56,0.58) 提亮偏青
            m.SetFloat("_Absorb", 0.12f);                                        // 旧 0.16
            AssetDatabase.SaveAssetIfDirty(m);
            Write("R42 ocean tier: 近岸透沙\n");
        }

        [MenuItem("Tools/R42/94b Ocean 还原")]
        static void OceanRevert()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(M + "/Ocean_Water.mat");
            if (m != null)
            {
                m.SetColor("_ShallowColor", new Color(0.150f, 0.560f, 0.580f, 1f));
                m.SetFloat("_Absorb", 0.30f);   // R42 探针实读基线 0.30（非 shader 默认 0.16）
                AssetDatabase.SaveAssetIfDirty(m);
            }
            Write("R42 ocean revert\n");
        }

        // A7：FFT 高光离散化（锐化+提密）
        [MenuItem("Tools/R42/95 FFT 高光离散档")]
        static void FftTier()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(M + "/FFT/Water_FFT.mat");
            if (m == null) return;
            m.SetFloat("_SpecPow", 640f);     // 旧 320：耀斑收紧成点
            m.SetFloat("_SpecInt", 2.4f);     // 旧 2.6：收亮度防糊带
            AssetDatabase.SaveAssetIfDirty(m);
            Write("R42 fft spec tier: 640/2.4\n");
        }

        [MenuItem("Tools/R42/95b FFT 还原(320/2.6)")]
        static void FftRevert()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(M + "/FFT/Water_FFT.mat");
            if (m != null) { m.SetFloat("_SpecPow", 320f); m.SetFloat("_SpecInt", 2.6f); AssetDatabase.SaveAssetIfDirty(m); }
            Write("R42 fft revert\n");
        }

        [MenuItem("Tools/R42/99 探针(只读)")]
        static void Probe()
        {
            foreach (var (p, tag) in new[] {
                (M + "/Water_Pond.mat", "pond"), (M + "/Water_Advanced.mat", "main"),
                (M + "/Water_Pool.mat", "pool"), (M + "/Ocean_Water.mat", "ocean"),
                (M + "/FFT/Water_FFT.mat", "fft") })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (m == null) continue;
                string s = $"R42 PROBE {tag}";
                if (m.HasProperty("_ShallowColor")) s += $" shallow={m.GetColor("_ShallowColor")}";
                if (m.HasProperty("_DeepColor")) s += $" deep={m.GetColor("_DeepColor")}";
                if (m.HasProperty("_Absorb")) s += $" absorb={m.GetFloat("_Absorb")}";
                if (m.HasProperty("_ScatterStrength")) s += $" scat={m.GetFloat("_ScatterGain")}/{m.GetFloat("_ScatterStrength")}";
                if (m.HasProperty("_CausticsScale")) s += $" caus={m.GetFloat("_CausticsScale")}/{m.GetFloat("_CausticDepthMix")}/{m.GetFloat("_CausticSharp")}/{m.GetFloat("_CausticsStrength")}";
                if (m.HasProperty("_SpecPow")) s += $" spec={m.GetFloat("_SpecPow")}/{m.GetFloat("_SpecInt")}";
                Write(s + "\n");
            }
        }

        static void Write(string s)
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s + "\n");
        }
    }
}
#endif
