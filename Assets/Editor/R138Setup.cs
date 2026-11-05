// R138Setup.cs — R138 晴空太亮（R136 审计 Top2）
//
// 审计：晴空 p15 我方 69.3 / 参考 34.0（四帧参考极一致）。整幅亮度在参考区间内
// ⇒ 不是曝光，而是"蓝得不够深"。手段：压天顶色（保持色相，只压亮度）。
// 90/90b/90c = 天顶 ×0.75 / ×0.55 / ×0.40；90d 还原
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    internal static class R138Setup
    {
        const string Mat = "Assets/Art/Materials/Sky_IsletTropical.mat";
        static readonly Color Z = new Color(0.01f, 0.42f, 0.80f, 1f);

        static void P(string s) => System.IO.File.AppendAllText(
            System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        static void Zenith(float k, string tag)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(Mat);
            if (m == null) { P("R138 REFUSED: 无 Sky_IsletTropical\n"); return; }
            m.SetColor("_ZenithColor", new Color(Z.r * k, Z.g * k, Z.b * k, 1f));
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);
            if (RenderSettings.skybox == m) { RenderSettings.skybox = null; RenderSettings.skybox = m; }
            P($"R138 {tag}: 天顶 ×{k:F2}\n");
        }

        [MenuItem("Tools/R138/90 天顶 ×0.75")]
        public static void Z75() => Zenith(0.75f, "90");

        [MenuItem("Tools/R138/90b 天顶 ×0.55")]
        public static void Z55() => Zenith(0.55f, "90b");

        [MenuItem("Tools/R138/90c 天顶 ×0.40")]
        public static void Z40() => Zenith(0.40f, "90c");

        [MenuItem("Tools/R138/90d 天顶 还原(×1)")]
        public static void Z100() => Zenith(1f, "90d");

        [MenuItem("Tools/R138/91 探针")]
        public static void Probe()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(Mat);
            if (m == null) { P("R138/91 无材质\n"); return; }
            var c = m.GetColor("_ZenithColor");
            P($"R138/91 zenith=({c.r:F3},{c.g:F3},{c.b:F3})\n");
        }
    }
}
#endif
