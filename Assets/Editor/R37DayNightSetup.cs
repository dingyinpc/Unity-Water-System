// R37DayNightSetup.cs —— S11 昼夜分时专项（黄昏/夜空全黑的黑盒隔离 + 修复档）
//
// 现象（2026-10-25 实测）：111 黄昏/114 午夜渲染上半天纯黑（行均值 0、max=1），
// 而材质状态正确（地平色=亮橙 0.95/0.42/0.32、moonDir·camFwd=0.95、starBright=1.15）。
// 本文件先做隔离试验（97/98 逐个解除嫌疑），诊断后落修复档（90/91）。
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WaterProject.EditorTools
{
    internal static class R37DayNight
    {
        static Material SkyMat()
        {
            var sky = RenderSettings.skybox;
            return sky;
        }

        [MenuItem("Tools/R37/97 试验:夜因子=0(不改时间)")]
        static void TestNightOff()
        {
            var m = SkyMat();
            if (m != null && m.HasProperty("_NightAmount"))
            { m.SetFloat("_NightAmount", 0f); Write("R37 TEST _NightAmount=0（当前时段渲染对比）\n"); }
        }

        // ── R38 黄昏铺开档：R37 判卷 7/10 的两个扣分点——上部天偏暗 + 水面暖反光弱 ──
        // 绝对值幂等写入（先跑 90 再跑本档；0.50 正午键不碰 ⇒ 白天零回归）
        [MenuItem("Tools/R37/91 黄昏铺开档(天顶暖化+水色暖亮)")]
        static void DuskSpread()
        {
            var dn = Object.FindFirstObjectByType<WaterProject.Logic.DayNightCycle>();
            if (dn == null) { Write("R38 REFUSED: 无 DayNightCycle（开 OceanScene）\n"); return; }
            SetKey(dn.skyZenith, 0.73f, new Color(0.40f, 0.20f, 0.42f));   // 默认 (0.24,0.12,0.36)：上部天暖亮化（0.52 版过冲 天空带 177）
            SetKey(dn.skyZenith, 0.65f, new Color(0.36f, 0.38f, 0.72f));   // 默认 (0.20,0.36,0.76)：向暖过渡
            SetKey(dn.waterShallow, 0.73f, new Color(0.55f, 0.33f, 0.30f)); // 默认 (0.23,0.15,0.19)：水面暖反光主体
            SetKey(dn.waterDeep, 0.73f, new Color(0.13f, 0.09f, 0.16f));    // 默认 (0.06,0.045,0.11)
            dn.Apply(true);
            EditorUtility.SetDirty(dn);
            EditorSceneManager.MarkSceneDirty(dn.gameObject.scene);
            Write("R38 dusk spread: zen0.65/0.73 暖化 + water 0.73 暖亮\n");
        }

        [MenuItem("Tools/R37/91b 黄昏铺开还原(键回默认)")]
        static void DuskSpreadRevert()
        {
            var dn = Object.FindFirstObjectByType<WaterProject.Logic.DayNightCycle>();
            if (dn == null) return;
            SetKey(dn.skyZenith, 0.73f, new Color(0.240f, 0.120f, 0.360f));
            SetKey(dn.skyZenith, 0.65f, new Color(0.200f, 0.360f, 0.760f));
            SetKey(dn.waterShallow, 0.73f, new Color(0.230f, 0.150f, 0.190f));
            SetKey(dn.waterDeep, 0.73f, new Color(0.060f, 0.045f, 0.110f));
            dn.Apply(true);
            EditorUtility.SetDirty(dn);
            EditorSceneManager.MarkSceneDirty(dn.gameObject.scene);
            Write("R38 dusk spread revert\n");
        }

        static void SetKey(WaterProject.Logic.ColorKey[] keys, float t, Color c)
        {
            if (keys == null) return;
            for (int i = 0; i < keys.Length; i++)
                if (Mathf.Abs(keys[i].t - t) < 0.003f)
                { keys[i] = new WaterProject.Logic.ColorKey(t, c); return; }
            Write($"R38 WARN: 找不到键 t={t}\n");
        }

        [MenuItem("Tools/R37/98 试验:云盖=0.05(不改其它)")]
        static void TestCloudOff()
        {
            var m = SkyMat();
            if (m != null && m.HasProperty("_CloudCover"))
            { m.SetFloat("_CloudCover", 0.05f); Write("R37 TEST _CloudCover=0.05\n"); }
        }

        [MenuItem("Tools/R37/98b 试验:恢复云盖0.52+重Apply当前时段")]
        static void TestRestore()
        {
            var m = SkyMat();
            if (m != null && m.HasProperty("_CloudCover")) m.SetFloat("_CloudCover", 0.52f);
            var dn = Object.FindFirstObjectByType<WaterProject.Logic.DayNightCycle>();
            if (dn != null) dn.Apply(true);
            Write("R37 TEST restored cover + re-Apply\n");
        }

        // ── 修复档：黄昏/夜空被后处理链压黑（黑盒定位：关后处理天空复活 0→82）──
        // 三件套：SkyDeep 半回(0.52→0.75，白天天空也过暗的另一半原因) +
        // 曲线夜段加亮(吃 ACES 余量，参考黄昏全图 98 我们 9) + 星亮(1.15 过不了调色)
        [MenuItem("Tools/R37/90 昼夜可见化档(SkyDeep0.75+夜段曲线×2.2+星2.4)")]
        static void FixTier()
        {
            var m = SkyMat();
            if (m != null)
            {
                // SkyDeep 改由 DayNightCycle.Apply 随昼夜驱动（白天 0.52 校准不动，低光 0.75）
                m.SetFloat("_StarBright", 2.4f);
                m.SetFloat("_GalaxyAmount", 0.45f);
                EditorUtility.SetDirty(m);
                AssetDatabase.SaveAssetIfDirty(m);
                Write("R37 FIX sky: SkyDeep=0.75 star=2.4 galaxy=0.45\n");
            }
            var dn = Object.FindFirstObjectByType<WaterProject.Logic.DayNightCycle>();
            if (dn != null)
            {
                BoostNightKeys(dn, 2.2f);
                dn.Apply(true);
                EditorUtility.SetDirty(dn);
                EditorSceneManager.MarkSceneDirty(dn.gameObject.scene);
                Write("R37 FIX curves: 夜段(0.70~1.00∪0.00~0.30)×2.2 + reApply\n");
            }
        }

        [MenuItem("Tools/R37/90b 昼夜可见化还原(BuildDefaults+SkyDeep0.52+星1.15)")]
        static void FixRevert()
        {
            var m = SkyMat();
            if (m != null)
            {
                m.SetFloat("_StarBright", 1.15f);
                m.SetFloat("_GalaxyAmount", 0.30f);
                EditorUtility.SetDirty(m);
                AssetDatabase.SaveAssetIfDirty(m);
            }
            var dn = Object.FindFirstObjectByType<WaterProject.Logic.DayNightCycle>();
            if (dn != null)
            {
                dn.BuildDefaults();
                dn.Apply(true);
                EditorUtility.SetDirty(dn);
                EditorSceneManager.MarkSceneDirty(dn.gameObject.scene);
            }
            Write("R37 FIX revert done\n");
        }

        static void BoostNightKeys(WaterProject.Logic.DayNightCycle dn, float k)
        {
            BoostKeys(dn.skyZenith, k);
            BoostKeys(dn.skyHorizon, k);
        }

        static void BoostKeys(WaterProject.Logic.ColorKey[] keys, float k)
        // ⚠ ColorKey 字段名是 c 不是 color
        {
            if (keys == null) return;
            for (int i = 0; i < keys.Length; i++)
            {
                float t = keys[i].t;
                // 黄昏带 0.70~0.78 用温和档（×2.2 实测过冲：全图 132 vs 参考 98）；
                // 深夜带（≥0.82 或 ≤0.30）保持 ×2.2（夜已对齐参考 42.7 vs 36）
                float kk = (t >= 0.70f && t <= 0.78f) ? Mathf.Lerp(k, 1f, 0.32f) : k;
                if (t >= 0.70f || t <= 0.30f)
                    keys[i] = new WaterProject.Logic.ColorKey(t, keys[i].c * kk);
            }
        }

        [MenuItem("Tools/R37/96 试验:当前相机后处理=关(会话级)")]
        static void TestPostOff()
        {
            var cam = Camera.main;
            if (cam == null) { Write("R37 TEST no main cam\n"); return; }
            var data = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            Write("R37 TEST main cam post -> OFF (session only)\n");
        }

        [MenuItem("Tools/R37/96c 试验:当前相机后处理=开(会话级)")]
        static void TestPostOn()
        {
            var cam = Camera.main;
            if (cam == null) { Write("R37 TEST no main cam"); return; }
            var data = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            Write("R37 TEST main cam post -> ON (session only)");
        }

        [MenuItem("Tools/R37/96b 试验:SkyDeep=1(会话级)")]
        static void TestDeepOff()
        {
            var m = SkyMat();
            if (m != null && m.HasProperty("_SkyDeep")) { m.SetFloat("_SkyDeep", 1f); Write("R37 TEST _SkyDeep=1\n"); }
        }

        [MenuItem("Tools/R37/99 天空材质读数(只读)")]
        static void Probe()
        {
            var m = SkyMat();
            if (m == null) { Write("R37 PROBE skybox=<none>\n"); return; }
            var dn = Object.FindFirstObjectByType<WaterProject.Logic.DayNightCycle>();
            Write($"R37 PROBE sky={m.name} t={(dn != null ? dn.timeOfDay.ToString("F2") : "?")} " +
                  $"night={m.GetFloat("_NightAmount"):F2} moon={m.GetFloat("_MoonAmount"):F2} " +
                  $"cover={m.GetFloat("_CloudCover"):F2} star={m.GetFloat("_StarBright"):F2} " +
                  $"skyDeep={m.GetFloat("_SkyDeep"):F2} sunGlow={m.GetFloat("_SunGlow"):F2}\n");
            Write($"R37 PROBE zen={m.GetColor("_ZenithColor")} hor={m.GetColor("_HorizonColor")}\n");
        }

        static void Write(string s)
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s + "\n");
        }
    }
}
#endif
