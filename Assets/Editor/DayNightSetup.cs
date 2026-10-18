// DayNightSetup.cs — 昼夜系统（P1-1）的编辑器菜单：Tools/Ocean/110~116
//
// 唯一真源纪律：时间与曲线参数由本菜单写入组件；材质颜色由组件在运行时驱动，
// 不再在别处手改天空/水材质（否则下次 Apply 就被冲掉 —— 《05》"参数两个写入点"）。
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using WaterProject.Logic;

namespace WaterProject.EditorTools
{
    public static class DayNightSetup
    {
        const string MatFolder = "Assets/Art/Materials";
        const string SkyMatPath = MatFolder + "/Sky_OpenCoast.mat";
        const string WaterMatPath = MatFolder + "/Ocean_Water.mat";

        static void WriteProbe(string s)
            => File.AppendAllText(Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        // ═════════════════════════ 110 ═════════════════════════
        [MenuItem("Tools/Ocean/110 建昼夜系统(组件+曲线默认值)")]
        public static void Build()
        {
            var go = GameObject.Find("DayNightCycle");
            if (go == null) go = new GameObject("DayNightCycle");
            var dn = go.GetComponent<DayNightCycle>() ?? go.AddComponent<DayNightCycle>();
            dn.BuildDefaults();

            var lights = Object.FindObjectsOfType<Light>();
            foreach (var l in lights)
            {
                if (l.type != LightType.Directional) continue;
                if (dn.sun == null) dn.sun = l;
                else if (dn.moon == null && l != dn.sun) dn.moon = l;
            }
            // 没有月亮灯就造一盏（与太阳反向；方向由组件每帧按时间算）
            if (dn.moon == null)
            {
                var mg = new GameObject("Moon Light");
                var ml = mg.AddComponent<Light>();
                ml.type = LightType.Directional;
                ml.color = new Color(0.62f, 0.72f, 0.95f);
                ml.intensity = 0f;
                ml.shadows = LightShadows.None;
                dn.moon = ml;
            }
            dn.skyMat = AssetDatabase.LoadAssetAtPath<Material>(SkyMatPath);
            dn.waterMat = AssetDatabase.LoadAssetAtPath<Material>(WaterMatPath);
            dn.timeOfDay = 0.35f;
            dn.Apply(false);
            EditorUtility.SetDirty(dn);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
            WriteProbe($"MENU110 ok: sun={(dn.sun ? dn.sun.name : "null")} moon={(dn.moon ? dn.moon.name : "null")} "
                     + $"sky={(dn.skyMat ? "ok" : "null")} water={(dn.waterMat ? "ok" : "null")} t={dn.timeOfDay:F2}\n");
            // 曲线自检：证明曲线真的有键、真的算出颜色
            WriteProbe($"  曲线自检: zenith keys={(dn.skyZenith == null ? -1 : dn.skyZenith.Length)} "
                     + $"eval(0.5)={DayNightCycle.Eval(dn.skyZenith, 0.5f)} horizon eval(0.73)={DayNightCycle.Eval(dn.skyHorizon, 0.73f)}\n");
        }

        static void SetTime(float t, string tag)
        {
            var dn = Object.FindObjectOfType<DayNightCycle>();
            if (dn == null) { WriteProbe($"MENU{tag} REFUSED: 场景里没有 DayNightCycle（先跑菜单110）\n"); return; }
            dn.timeOfDay = t; dn.cycleSeconds = 0f; dn.Apply(true);
            EditorUtility.SetDirty(dn);
            // 回读探针：证明"改的真的生效了"（《05》"先验证观测通道"）
            string s = $"MENU{tag} 时间 -> {t:F2}（0午夜/0.25日出/0.5正午/0.75黄昏）";
            if (dn.skyMat != null)
                s += $" | 天空 顶{dn.skyMat.GetColor("_ZenithColor")} 地平{dn.skyMat.GetColor("_HorizonColor")} 云{dn.skyMat.GetColor("_CloudColor")}";
            if (dn.sun != null) s += $" | 太阳 强度{dn.sun.intensity:F2} 仰角{dn.sun.transform.eulerAngles.x:F1}";
            if (dn.moon != null) s += $" | 月亮 强度{dn.moon.intensity:F2}";
            WriteProbe(s + "\n");
        }

        [MenuItem("Tools/Ocean/111 时间=黄昏(0.73)")] public static void TimeDusk()     => SetTime(0.73f, "111");
        [MenuItem("Tools/Ocean/112 时间=日出(0.28)")] public static void TimeSunrise()  => SetTime(0.28f, "112");
        [MenuItem("Tools/Ocean/113 时间=正午(0.50)")] public static void TimeNoon()     => SetTime(0.50f, "113");
        [MenuItem("Tools/Ocean/114 时间=午夜(0.00)")] public static void TimeMidnight() => SetTime(0.00f, "114");

        [MenuItem("Tools/Ocean/115 昼夜自动循环 开/关(60s/圈)")]
        public static void ToggleCycle()
        {
            var dn = Object.FindObjectOfType<DayNightCycle>();
            if (dn == null) { WriteProbe("MENU115 REFUSED: 没有 DayNightCycle\n"); return; }
            dn.cycleSeconds = dn.cycleSeconds > 0f ? 0f : 60f;
            EditorUtility.SetDirty(dn);
            WriteProbe($"MENU115 cycleSeconds -> {dn.cycleSeconds:F0}（0=停）\n");
        }
    }
}
#endif
