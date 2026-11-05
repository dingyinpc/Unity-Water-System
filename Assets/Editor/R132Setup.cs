// R132Setup.cs — R132 远处灌木剪影发黑（反照率只有 4%）
//
// Islet_ScrubLeaf._BaseColor = (0.032, 0.069, 0.044) —— 反照率 ~4%，背光直接归零，
// 远看就是地平线上一整条纯黑（实测 p10 = 0）。
// 90/90b/90c 反照率 ×2 / ×3 / ×4；90d 还原
// 91 崖体填充光（_FillLight）0.9 → 1.3
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    internal static class R132Setup
    {
        const string Scrub = "Assets/Art/Materials/Islet/Islet_ScrubLeaf.mat";
        static readonly Color Base = new Color(0.032f, 0.069f, 0.044f, 1f);

        static void P(string s) => System.IO.File.AppendAllText(
            System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        static void ScrubMul(float k, string tag)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(Scrub);
            if (m == null) { P("R132 REFUSED: 无 Islet_ScrubLeaf\n"); return; }
            m.SetColor("_BaseColor", new Color(Base.r * k, Base.g * k, Base.b * k, 1f));
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);
            P($"R132 {tag}: scrub base ×{k:F1}\n");
        }

        [MenuItem("Tools/R132/90 灌木反照率 ×2")]
        public static void Scrub2() => ScrubMul(2f, "90 ×2");

        [MenuItem("Tools/R132/90b 灌木反照率 ×3")]
        public static void Scrub3() => ScrubMul(3f, "90b ×3");

        [MenuItem("Tools/R132/90c 灌木反照率 ×4")]
        public static void Scrub4() => ScrubMul(4f, "90c ×4");

        [MenuItem("Tools/R132/90d 灌木还原(×1)")]
        public static void Scrub1() => ScrubMul(1f, "90d ×1");

        [MenuItem("Tools/R132/90e 灌木反照率 ×5")]
        public static void Scrub5() => ScrubMul(5f, "90e ×5");

        [MenuItem("Tools/R132/90f 灌木反照率 ×6")]
        public static void Scrub6() => ScrubMul(6f, "90f ×6");

        [MenuItem("Tools/R132/91 崖体填充光 +40%")]
        public static void CliffFill()
        {
            int n = 0;
            foreach (var path in new[] {
                "Assets/Art/Materials/Islet/Islet_Rock_Splat.mat",
                "Assets/Art/Materials/Islet_ReefRockDetail.mat" })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null || !m.HasProperty("_FillLight")) continue;
                m.SetFloat("_FillLight", 1.26f);
                EditorUtility.SetDirty(m);
                AssetDatabase.SaveAssetIfDirty(m);
                n++;
            }
            P($"R132/91 填充光 1.26，写入 {n} 张\n");
        }

        [MenuItem("Tools/R132/91b 崖体填充光还原")]
        public static void CliffFillBack()
        {
            foreach (var path in new[] {
                "Assets/Art/Materials/Islet/Islet_Rock_Splat.mat",
                "Assets/Art/Materials/Islet_ReefRockDetail.mat" })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null || !m.HasProperty("_FillLight")) continue;
                m.SetFloat("_FillLight", 0.9f);
                EditorUtility.SetDirty(m);
                AssetDatabase.SaveAssetIfDirty(m);
            }
            P("R132/91b 填充光还原 0.9\n");
        }

        [MenuItem("Tools/R132/92 探针: 灌木/岩石")]
        public static void Probe()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(Scrub);
            var r = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Islet_ReefRockDetail.mat");
            P($"R132/92 scrub=({m.GetColor("_BaseColor").r:F3},{m.GetColor("_BaseColor").g:F3}) "
              + $"fill={(r != null && r.HasProperty("_FillLight") ? r.GetFloat("_FillLight") : -1):F2}\n");
        }
    }
}
#endif
