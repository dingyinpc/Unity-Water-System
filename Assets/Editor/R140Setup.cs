// R140Setup.cs — R140 Ocean 沙滩偏橙（反照率饱和度过高）
//
// 实测（自动筛沙像素）：
//   参考 f880  平均 BGR (183.5, 202.7, 223.9) 饱和 40.5   —— 浅米白
//   我方 Ocean 平均 BGR ( 41.4, 130.6, 182.1) 饱和 140.6  —— 橙黄
// 根因：Beach_Sand 的 _DryColor (0.360,0.285,0.180) 本身就是高饱和暖色，
//       再乘暖阳光 + 暖贴图 ⇒ 三重叠加。
// 90  中性档 A（0.55,0.51,0.47） / 90b B（0.66,0.61,0.56） / 90c C（0.75,0.70,0.64）
// 90d 还原（0.36,0.285,0.18）
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    internal static class R140Setup
    {
        static readonly string[] Mats =
        {
            "Assets/Art/Materials/Ocean_Sand.mat",
            "Assets/Art/Materials/Sand_Shore.mat",
            "Assets/Art/Materials/Sand_Wet.mat",
            "Assets/Art/Materials/Sand_Damp.mat",
        };

        static void P(string s) => System.IO.File.AppendAllText(
            System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        static void Preset(string tag, Color dry, Color wet)
        {
            int n = 0;
            foreach (var path in Mats)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) continue;
                bool any = false;
                if (m.HasProperty("_DryColor")) { m.SetColor("_DryColor", dry); any = true; }
                if (m.HasProperty("_WetColor")) { m.SetColor("_WetColor", wet); any = true; }
                if (!any) continue;
                EditorUtility.SetDirty(m);
                AssetDatabase.SaveAssetIfDirty(m);
                n++;
            }
            P($"R140 {tag}: dry={dry} wet={wet} 写入 {n} 张\n");
        }

        [MenuItem("Tools/R140/90 沙滩=中性 A")]
        public static void A() => Preset("90 A", new Color(0.55f, 0.51f, 0.47f, 1f), new Color(0.30f, 0.28f, 0.26f, 1f));

        [MenuItem("Tools/R140/90b 沙滩=中性 B")]
        public static void B() => Preset("90b B", new Color(0.66f, 0.61f, 0.56f, 1f), new Color(0.36f, 0.335f, 0.31f, 1f));

        [MenuItem("Tools/R140/90c 沙滩=中性 C")]
        public static void C() => Preset("90c C", new Color(0.75f, 0.70f, 0.64f, 1f), new Color(0.42f, 0.39f, 0.36f, 1f));

        [MenuItem("Tools/R140/90d 沙滩=还原(旧橙)")]
        public static void R() => Preset("90d 还原", new Color(0.360f, 0.285f, 0.180f, 1f), new Color(0.098f, 0.076f, 0.050f, 1f));

        [MenuItem("Tools/R140/90e 沙滩=中性 D")]
        public static void D() => Preset("90e D", new Color(0.42f, 0.39f, 0.36f, 1f), new Color(0.26f, 0.24f, 0.22f, 1f));

        [MenuItem("Tools/R140/90f 沙滩=中性 E")]
        public static void E() => Preset("90f E", new Color(0.48f, 0.45f, 0.42f, 1f), new Color(0.29f, 0.27f, 0.25f, 1f));

        [MenuItem("Tools/R140/91 探针")]
        public static void Probe()
        {
            var sb = new System.Text.StringBuilder("R140/91 沙滩材质\n");
            foreach (var path in Mats)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) continue;
                var d = m.HasProperty("_DryColor") ? m.GetColor("_DryColor") : Color.clear;
                sb.Append($"  {System.IO.Path.GetFileName(path)}: dry=({d.r:F2},{d.g:F2},{d.b:F2})")
                  .Append($" texAmt={(m.HasProperty("_SandTexAmount") ? m.GetFloat("_SandTexAmount") : -1):F2}\n");
            }
            P(sb.ToString());
        }
    }
}
#endif
