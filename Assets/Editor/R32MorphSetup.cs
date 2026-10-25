// R32MorphSetup.cs —— 参数形态轮（R31 帧级扫描的执行档）
//
// 背景：R31 对照七组帧判出的是**形态学差距**——浪尖白沫不可见、泡沫斑点化
// 而非沿浪线成带、浪乱短不成长排、焦散细密不成大块、云底无暗影。
// 本轮全部用**现成系统**的参数/权重修正，配套 shader 侧三处小改：
//   · FFTOcean.compute：Jacobian 计入 choppy + 起沫阈值 0.85→0.96
//   · Water_Ocean：白帽挂中浪(74m)波峰相位成带；碎浪沫线 pow 2.2→3.2；咬边方差降
//   · Sky_Coast：新增 _CloudBaseDark（云底暗度，0=旧行为零回归）
// 本文件是这些档的**唯一写入点**：应用/还原都走菜单，全部回读进探针。
//
// ⚠ 还原档写回的"旧值"取自 2026-10-25 改前 .mat 序列化现值（见各方法内注释）。

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WaterProject.Logic;

namespace WaterProject.EditorTools
{
    internal static class R32MorphSetup
    {
        const string MatFolder = "Assets/Art/Materials";

        // ── FFT：风程拉长（主导波长大档）+ choppy 尖峰 + 白沫增益 + 海色微亮 ──
        [MenuItem("Tools/R32/90 FFT 形态档(长涌+尖峰+白沫+海色)")]
        static void FftTier()
        {
            var sim = Object.FindFirstObjectByType<FFTOceanSim>();
            if (sim != null)
            {
                sim.enabled = true;        // ⚠ 首版 90 在编辑模式跑过旧 Init（_cs=null 自禁用），
                                            //   enabled=false 被存进场景——这里强制扶起来
                sim.windSpeed = 12.5f;   // 风程 Lw=V²/g：8.3→15.9m，主导波长约 ×2（参考浪圆润大尺度）
                sim.choppy = 1.35f;      // 浪峰更尖（compute 端 J 已同步计 choppy → 起沫跟着来）
                sim.foamScale = 1.45f;   // 白沫增益（阈值 compute 侧 0.98；片元端还有噪声侵蚀压覆盖）
                sim.ampScale = 780f;     // 风程变大能量变足，总幅从 985 压回，波高与旧档接近
                sim.Init();              // h0 依赖风速，必须重生成
                EditorSceneManager.MarkSceneDirty(sim.gameObject.scene);
                Write($"R32 FFT sim: wind={sim.windSpeed} choppy={sim.choppy} foam={sim.foamScale} amp={sim.ampScale}\n");
                Write("R32 FFT stats: " + sim.ReadStats() + "\n");
            }
            else Write("R32 FFT sim: FFTOceanSim NOT FOUND（先开 FFTScene）\n");

            var m = AssetDatabase.LoadAssetAtPath<Material>(MatFolder + "/FFT/Water_FFT.mat");
            if (m != null)
            {
                m.SetColor("_ScatterColor", new Color(0.06f, 0.50f, 0.48f, 1f));  // 旧 (0.05,0.42,0.40) 偏暗灰
                m.SetColor("_DeepColor", new Color(0.02f, 0.13f, 0.18f, 1f));     // 旧 (0.015,0.09,0.13)
                m.SetFloat("_SpecInt", 2.6f);                                       // 旧 2.2
                AssetDatabase.SaveAssetIfDirty(m);
                Write("R32 FFT mat: scatter/deep/spec 已写\n");
            }
            var fc = AssetDatabase.LoadAssetAtPath<Material>(MatFolder + "/FFT/FFT_Caustics.mat");
            if (fc != null)
            {
                fc.SetFloat("_CausticPow", 1.2f);    // 旧 1.5：曲率网更宽更软=大块化
                fc.SetFloat("_CausticScale", 380f);  // 旧 300：亮度补回
                AssetDatabase.SaveAssetIfDirty(fc);
                Write("R32 FFT floor caustic: pow/scale 已写\n");
            }
        }

        [MenuItem("Tools/R32/90b FFT 还原档")]
        static void FftRevert()
        {
            var sim = Object.FindFirstObjectByType<FFTOceanSim>();
            if (sim != null)
            {
                sim.windSpeed = 9f; sim.choppy = 1f; sim.foamScale = 1f; sim.ampScale = 985f;
                sim.Init();
                EditorSceneManager.MarkSceneDirty(sim.gameObject.scene);
                Write($"R32 FFT revert sim: wind=9 choppy=1 foam=1 amp=985\n");
            }
            var m = AssetDatabase.LoadAssetAtPath<Material>(MatFolder + "/FFT/Water_FFT.mat");
            if (m != null)
            {
                m.SetColor("_ScatterColor", new Color(0.05f, 0.42f, 0.40f, 1f));
                m.SetColor("_DeepColor", new Color(0.015f, 0.09f, 0.13f, 1f));
                m.SetFloat("_SpecInt", 2.2f);
                AssetDatabase.SaveAssetIfDirty(m);
            }
            var fc = AssetDatabase.LoadAssetAtPath<Material>(MatFolder + "/FFT/FFT_Caustics.mat");
            if (fc != null)
            {
                fc.SetFloat("_CausticPow", 1.5f); fc.SetFloat("_CausticScale", 300f);
                AssetDatabase.SaveAssetIfDirty(fc);
            }
            Write("R32 FFT revert done\n");
        }

        // ── Ocean：泡沫沿浪线成带 + 长直规则浪 ──
        //（白帽带状化的"公式"改动在 Water_Ocean.shader，本档只给权重）
        [MenuItem("Tools/R32/91 Ocean 泡沫带状+长直浪档")]
        static void OceanTier()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(MatFolder + "/Ocean_Water.mat");
            if (m == null) { Write("R32 Ocean: Ocean_Water.mat NOT FOUND\n"); return; }
            m.SetFloat("_SwellScale", 1.45f);  // 旧 1：λ 150/74/24 → 218/107/35，浪线拉长
            m.SetFloat("_WindAlign", 0.94f);   // 旧 0.8：三浪方向集中 ⇒ 浪线平行长直
            m.SetFloat("_SwellAmp", 1.6f);     // 旧 1：波长变长后补一点峰高（陡度别塌）
            m.SetFloat("_Whitecap", 0.9f);     // 旧 0.2：带状白帽（挂 74m 波峰相位）——0.55 实测仍读不出条带，推到 0.9
            m.SetFloat("_BreakFoam", 1.0f);    // 旧 0.95：碎浪线更实
            m.SetFloat("_Steep", 0.68f);       // 旧 0.62：峰线更清晰
            AssetDatabase.SaveAssetIfDirty(m);
            Write("R32 Ocean tier: scale=1.45 align=0.94 amp=1.6 whitecap=0.55 breakFoam=1.0 steep=0.68\n");
        }

        [MenuItem("Tools/R32/91b Ocean 还原档")]
        static void OceanRevert()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(MatFolder + "/Ocean_Water.mat");
            if (m == null) { Write("R32 Ocean: Ocean_Water.mat NOT FOUND\n"); return; }
            m.SetFloat("_SwellScale", 1f); m.SetFloat("_WindAlign", 0.8f); m.SetFloat("_SwellAmp", 1f);
            m.SetFloat("_Whitecap", 0.2f); m.SetFloat("_BreakFoam", 0.95f); m.SetFloat("_Steep", 0.62f);
            AssetDatabase.SaveAssetIfDirty(m);
            Write("R32 Ocean revert done\n");
        }

        // ── Pool：焦散大块稀疏化（守住 R26 干净亮水，只动尺度形状）──
        [MenuItem("Tools/R32/92 Pool 焦散大块档")]
        static void PoolTier()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(MatFolder + "/Water_Pool.mat");
            if (m == null) { Write("R32 Pool: Water_Pool.mat NOT FOUND\n"); return; }
            m.SetFloat("_CausticsScale", 0.34f);    // 旧 0.55：格子放大 ~1.6×
            m.SetFloat("_CausticDepthMix", 0.60f);  // 旧 0.35：往大斑端偏（参考大块不规则）
            m.SetFloat("_CausticSharp", 3.5f);      // 旧 3.0：块间对比拉开（不脏的前提）
            m.SetFloat("_CausticsStrength", 0.62f); // 旧 0.55：块少了，单块亮一点补能量
            AssetDatabase.SaveAssetIfDirty(m);
            Write("R32 Pool tier: scale=0.34 mix=0.60 sharp=3.5 strength=0.62\n");
        }

        [MenuItem("Tools/R32/92b Pool 还原档")]
        static void PoolRevert()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(MatFolder + "/Water_Pool.mat");
            if (m == null) { Write("R32 Pool: Water_Pool.mat NOT FOUND\n"); return; }
            m.SetFloat("_CausticsScale", 0.55f); m.SetFloat("_CausticDepthMix", 0.35f);
            m.SetFloat("_CausticSharp", 3f); m.SetFloat("_CausticsStrength", 0.55f);
            AssetDatabase.SaveAssetIfDirty(m);
            Write("R32 Pool revert done\n");
        }

        // ── 云底阴影：Sky_Coast 新参 _CloudBaseDark（两块天空材质同写）──
        [MenuItem("Tools/R32/93 云底阴影档(两块天空材质)")]
        static void SkyTier()
        {
            foreach (var p in new[] { MatFolder + "/Sky_OpenCoast.mat", MatFolder + "/FFT/FFT_Sky.mat" })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (m == null) { Write($"R32 Sky: {p} NOT FOUND\n"); continue; }
                if (!m.HasProperty("_CloudBaseDark")) { Write($"R32 Sky: {p} 无 _CloudBaseDark（shader 未编译？）\n"); continue; }
                m.SetFloat("_CloudBaseDark", 0.5f);
                AssetDatabase.SaveAssetIfDirty(m);
                Write($"R32 Sky {m.name}: _CloudBaseDark=0.8\n");
            }
        }

        [MenuItem("Tools/R32/93b 云底阴影还原")]
        static void SkyRevert()
        {
            foreach (var p in new[] { MatFolder + "/Sky_OpenCoast.mat", MatFolder + "/FFT/FFT_Sky.mat" })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (m == null) continue;
                if (m.HasProperty("_CloudBaseDark")) { m.SetFloat("_CloudBaseDark", 0f); AssetDatabase.SaveAssetIfDirty(m); }
                Write($"R32 Sky {m.name}: _CloudBaseDark=0\n");
            }
        }

        // ── 只读探针：全部 R32 参数现值 ──
        [MenuItem("Tools/R32/99 探针回读(只读)")]
        static void Probe()
        {
            var sim = Object.FindFirstObjectByType<FFTOceanSim>();
            Write(sim != null
                ? $"R32 PROBE sim wind={sim.windSpeed} choppy={sim.choppy} foam={sim.foamScale} amp={sim.ampScale}\n"
                : "R32 PROBE sim <none>\n");

            var ow = AssetDatabase.LoadAssetAtPath<Material>(MatFolder + "/Ocean_Water.mat");
            if (ow != null)
                Write($"R32 PROBE ocean scale={ow.GetFloat("_SwellScale")} align={ow.GetFloat("_WindAlign")} " +
                      $"amp={ow.GetFloat("_SwellAmp")} whitecap={ow.GetFloat("_Whitecap")} " +
                      $"breakFoam={ow.GetFloat("_BreakFoam")} steep={ow.GetFloat("_Steep")}\n");

            var pw = AssetDatabase.LoadAssetAtPath<Material>(MatFolder + "/Water_Pool.mat");
            if (pw != null)
                Write($"R32 PROBE pool caustic scale={pw.GetFloat("_CausticsScale")} mix={pw.GetFloat("_CausticDepthMix")} " +
                      $"sharp={pw.GetFloat("_CausticSharp")} strength={pw.GetFloat("_CausticsStrength")} speed={pw.GetFloat("_CausticsSpeed")}\n");

            foreach (var p in new[] { MatFolder + "/Sky_OpenCoast.mat", MatFolder + "/FFT/FFT_Sky.mat" })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (m != null)
                    Write($"R32 PROBE sky {m.name} baseDark={(m.HasProperty("_CloudBaseDark") ? m.GetFloat("_CloudBaseDark").ToString() : "NO-PROP")}\n");
            }
        }

        // ── 白沫定标：_Grad 场的 J/foam 分布（Play 中跑，数据才是活的）──
        [MenuItem("Tools/R32/98 Grad场统计(J/泡沫分布,白沫定标)")]
        static void GradStats()
        {
            var sim = Object.FindFirstObjectByType<FFTOceanSim>(FindObjectsInactive.Include);
            if (sim == null) { Write("R32 GRAD sim <none>\n"); return; }
            Write($"R32 GRAD state obj={sim.name} active={sim.gameObject.activeInHierarchy} enabled={sim.enabled} " +
                  $"instance={(FFTOceanSim.Instance != null ? "alive" : "null")} cs={(sim.computeShader != null ? "ok" : "null")}\n");
            Write("R32 GRAD " + sim.ReadGradStats() + "\n");
        }

        static void Write(string s)
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s + "\n");
        }
    }
}
#endif
