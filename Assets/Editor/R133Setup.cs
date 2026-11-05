// R133Setup.cs — R133 Islet 天空"黑色小点"（地平线附近最密）
//
// 猜：主步进数不足 ⇒ 一条几万米的射线只用 32 步，步距几百米，
//    每个像素"命中/不命中"云体的结果随机翻转 ⇒ 天空上撒一层深色小点。
// 90/90b  _Steps 64 / 32（A/B）
// 91/91b  _MarchPasses 4 / 3（相位平均再加一档）
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    internal static class R133Setup
    {
        const string Mat = "Assets/Art/Materials/Sky_IsletTropical.mat";

        static void P(string s) => System.IO.File.AppendAllText(
            System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        static void Set(string key, float v, string tag)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(Mat);
            if (m == null) { P("R133 REFUSED: 无 Sky_IsletTropical\n"); return; }
            if (!m.HasProperty(key)) { P($"R133 REFUSED: 材质没有 {key}\n"); return; }
            m.SetFloat(key, v);
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);
            if (RenderSettings.skybox == m) { RenderSettings.skybox = null; RenderSettings.skybox = m; }
            P($"R133 {tag}: {key}={v:F0}\n");
        }

        [MenuItem("Tools/R133/90 天空步进=64")]
        public static void Steps64() => Set("_Steps", 64f, "90 步进64");

        [MenuItem("Tools/R133/90b 天空步进=32(旧)")]
        public static void Steps32() => Set("_Steps", 32f, "90b 步进32");

        [MenuItem("Tools/R133/90c 天空步进=48")]
        public static void Steps48() => Set("_Steps", 48f, "90c 步进48");

        [MenuItem("Tools/R133/91 相位平均=4")]
        public static void Passes4() => Set("_MarchPasses", 4f, "91 平均4");

        [MenuItem("Tools/R133/91b 相位平均=3(旧)")]
        public static void Passes3() => Set("_MarchPasses", 3f, "91b 平均3");

        [MenuItem("Tools/R133/93 云量=0(诊断:小点是否来自云)")]
        public static void Cover0() => Set("_CloudCover", 0f, "93 云量0");

        [MenuItem("Tools/R133/93b 云量=0.30(还原)")]
        public static void Cover30() => Set("_CloudCover", 0.30f, "93b 云量0.30");

        [MenuItem("Tools/R133/94 密度下限=0.03")]
        public static void Floor3() => Set("_DensityFloor", 0.03f, "94 0.03");

        [MenuItem("Tools/R133/94b 密度下限=0.06")]
        public static void Floor6() => Set("_DensityFloor", 0.06f, "94b 0.06");

        [MenuItem("Tools/R133/94c 密度下限=0(旧)")]
        public static void Floor0() => Set("_DensityFloor", 0f, "94c 0");

        [MenuItem("Tools/R133/95 阴影步数=8")]
        public static void Shade8() => Set("_ShadeSteps", 8f, "95 阴影8");

        [MenuItem("Tools/R133/95b 阴影步数=12")]
        public static void Shade12() => Set("_ShadeSteps", 12f, "95b 阴影12");

        [MenuItem("Tools/R133/95c 阴影步数=4(旧)")]
        public static void Shade4() => Set("_ShadeSteps", 4f, "95c 阴影4");

        [MenuItem("Tools/R133/92 探针")]
        public static void Probe()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(Mat);
            if (m == null) { P("R133/92 无材质\n"); return; }
            P($"R133/92 steps={m.GetFloat("_Steps"):F0} passes={m.GetFloat("_MarchPasses"):F0} "
              + $"shade={m.GetFloat("_ShadeSteps"):F0}\n");
        }
    }
}
#endif
