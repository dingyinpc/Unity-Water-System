// R134Setup.cs — R134 棕榈树干"纯黑"（反照率 0.23 + 背光）
//
// 207 机位实测：树干是纯黑剪影（R123 做过 AI 树皮但看不见）。真实棕榈树干在逆光下
// 是"深棕+边缘透光"，不是纯黑。先把 _BaseColor 抬一档看剪影是否变成"深棕"。
// 90/90b/90c  ×1.5 / ×2 / ×2.5；90d 还原
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    internal static class R134Setup
    {
        const string Mat = "Assets/Art/Materials/Islet/Islet_PalmBark.mat";
        static readonly Color Base = new Color(0.230f, 0.170f, 0.128f, 1f);

        static void P(string s) => System.IO.File.AppendAllText(
            System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        static void Mul(float k, string tag)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(Mat);
            if (m == null) { P("R134 REFUSED: 无 Islet_PalmBark\n"); return; }
            m.SetColor("_BaseColor", new Color(Base.r * k, Base.g * k, Base.b * k, 1f));
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);
            P($"R134 {tag}: bark base ×{k:F1}\n");
        }

        [MenuItem("Tools/R134/90 树干反照率 ×1.5")]
        public static void Bark15() => Mul(1.5f, "90 ×1.5");

        [MenuItem("Tools/R134/90b 树干反照率 ×2")]
        public static void Bark20() => Mul(2f, "90b ×2");

        [MenuItem("Tools/R134/90c 树干反照率 ×2.5")]
        public static void Bark25() => Mul(2.5f, "90c ×2.5");

        [MenuItem("Tools/R134/90d 树干还原(×1)")]
        public static void Bark10() => Mul(1f, "90d ×1");

        [MenuItem("Tools/R134/91 探针: 树干")]
        public static void Probe()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(Mat);
            if (m == null) { P("R134/91 无材质\n"); return; }
            var c = m.GetColor("_BaseColor");
            P($"R134/91 bark=({c.r:F3},{c.g:F3},{c.b:F3})\n");
        }
    }
}
#endif
