// R129Setup.cs — R129 棕榈叶"去塑料感"
//
// 问题（放大看现状）：叶片是纯饱和绿 + 硬黑条纹，读成塑料。根因是
//   ① 贴图本身饱和 81，再乘绿色 _BaseColor ⇒ 双重饱和
//   ② 贴图 p5=0.33 / p95=0.69，暗部被压得很死 ⇒ 一轮轮黑条
// 90  换成"去色 70% + 抬暗部"的贴图 v2，同时把 _BaseColor 改成低饱和黄绿
// 90b 还原 v1
// 91  只换贴图不换颜色 / 91b 只换颜色不换贴图（定位是谁主导）
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    internal static class R129Setup
    {
        const string Mat = "Assets/Art/Materials/Islet/Islet_PalmLeaf.mat";
        const string TexV2 = "Assets/Art/Textures/AI/Proc/Palm_Frond_AI_flat2.png";
        const string TexV1 = "Assets/Art/Textures/AI/Proc/Palm_Frond_AI_flat.png";

        static void P(string s) => System.IO.File.AppendAllText(
            System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        static Material M()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(Mat);
            if (m == null) P("R129 REFUSED: 无 Islet_PalmLeaf.mat\n");
            return m;
        }

        static void Save(Material m)
        {
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);
        }

        [MenuItem("Tools/R129/90 棕榈叶=去色版(推荐)")]
        public static void LeafV2()
        {
            var m = M(); if (m == null) return;
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(TexV2);
            if (t == null) { P("R129/90 REFUSED: 缺 flat2 贴图\n"); return; }
            m.SetTexture("_BaseMap", t);
            m.SetColor("_BaseColor", new Color(0.46f, 0.60f, 0.30f, 1f));
            Save(m);
            P($"R129/90 叶=flat2 base=(0.46,0.60,0.30)（原 {m.GetColor("_BaseColor")}）\n");
        }

        [MenuItem("Tools/R129/90b 棕榈叶=还原(旧塑料绿)")]
        public static void LeafV1()
        {
            var m = M(); if (m == null) return;
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(TexV1);
            if (t != null) m.SetTexture("_BaseMap", t);
            m.SetColor("_BaseColor", new Color(0.486f, 0.81f, 0.317f, 1f));
            Save(m);
            P("R129/90b 叶已还原 v1\n");
        }

        [MenuItem("Tools/R129/91 只换贴图(颜色保持旧的)")]
        public static void OnlyTex()
        {
            var m = M(); if (m == null) return;
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(TexV2);
            if (t != null) m.SetTexture("_BaseMap", t);
            m.SetColor("_BaseColor", new Color(0.486f, 0.81f, 0.317f, 1f));
            Save(m);
            P("R129/91 只换贴图\n");
        }

        [MenuItem("Tools/R129/91b 只换颜色(贴图保持旧的)")]
        public static void OnlyColor()
        {
            var m = M(); if (m == null) return;
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(TexV1);
            if (t != null) m.SetTexture("_BaseMap", t);
            m.SetColor("_BaseColor", new Color(0.46f, 0.60f, 0.30f, 1f));
            Save(m);
            P("R129/91b 只换颜色\n");
        }

        [MenuItem("Tools/R129/92 探针: 叶片材质")]
        public static void Probe()
        {
            var m = M(); if (m == null) return;
            var t = m.GetTexture("_BaseMap");
            var c = m.GetColor("_BaseColor");
            P($"R129/92 叶: tex={(t != null ? t.name : "null")} base=({c.r:F2},{c.g:F2},{c.b:F2})\n");
        }
    }
}
#endif
