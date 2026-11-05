// R141Setup.cs — R141 Ocean 礁石"纯黑多边形"
//
// 现状：Ocean_Rock(Ocean_Rock 底色 0.20 / Pebble 0.15，URP/Lit 且**没有贴图**)，
//       Ocean_RockDetail(程序化岩) RockHi/Mid/Lo = 0.30/0.22/0.14 且 FillLight 0.75。
// 结果：在明亮沙滩旁边读成"黑色塑料块"，没有石头的明暗与质感。
// 90 温和提亮 / 90b 强力提亮 / 90c 还原
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    internal static class R141Setup
    {
        const string Rock = "Assets/Art/Materials/Ocean_Rock.mat";
        const string Peb = "Assets/Art/Materials/Ocean_Pebble.mat";
        const string Det = "Assets/Art/Materials/Ocean_RockDetail.mat";

        static void P(string s) => System.IO.File.AppendAllText(
            System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        static void Apply(string tag, float rockK, float pebbleK, float detK, float fill)
        {
            var r = AssetDatabase.LoadAssetAtPath<Material>(Rock);
            var p = AssetDatabase.LoadAssetAtPath<Material>(Peb);
            var d = AssetDatabase.LoadAssetAtPath<Material>(Det);
            if (r != null) { r.SetColor("_Color", new Color(0.20f * rockK, 0.195f * rockK, 0.185f * rockK, 1f)); EditorUtility.SetDirty(r); AssetDatabase.SaveAssetIfDirty(r); }
            if (p != null) { p.SetColor("_Color", new Color(0.15f * pebbleK, 0.14f * pebbleK, 0.125f * pebbleK, 1f)); EditorUtility.SetDirty(p); AssetDatabase.SaveAssetIfDirty(p); }
            if (d != null)
            {
                d.SetColor("_RockHi", new Color(0.30f * detK, 0.301f * detK, 0.310f * detK, 1f));
                d.SetColor("_RockMid", new Color(0.22f * detK, 0.221f * detK, 0.230f * detK, 1f));
                d.SetColor("_RockLo", new Color(0.14f * detK, 0.141f * detK, 0.148f * detK, 1f));
                if (d.HasProperty("_FillLight")) d.SetFloat("_FillLight", fill);
                EditorUtility.SetDirty(d); AssetDatabase.SaveAssetIfDirty(d);
            }
            P($"R141 {tag}: rock×{rockK:F2} pebble×{pebbleK:F2} detail×{detK:F2} fill={fill:F2}\n");
        }

        [MenuItem("Tools/R141/90 礁石=温和提亮")]
        public static void Mild() => Apply("90 温和", 1.55f, 1.60f, 1.35f, 0.95f);

        [MenuItem("Tools/R141/90b 礁石=强力提亮")]
        public static void Strong() => Apply("90b 强力", 1.95f, 2.00f, 1.60f, 1.10f);

        [MenuItem("Tools/R141/90c 礁石=还原")]
        public static void Revert() => Apply("90c 还原", 1f, 1f, 1f, 0.75f);

        // ── 沙滩网格纹：判定是不是沙贴图平铺造成的 ──
        static void SandSet(string key, float v, string tag)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Ocean_Sand.mat");
            if (m == null) { P("R141 REFUSED\n"); return; }
            m.SetFloat(key, v);
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m);
            P($"R141 {tag}: {key}={v:F2}\n");
        }

        [MenuItem("Tools/R141/92 沙贴图=关(判定网格纹来源)")]
        public static void SandTexOff() => SandSet("_SandTexAmount", 0f, "92");

        [MenuItem("Tools/R141/92b 沙贴图=0.40")]
        public static void SandTex40() => SandSet("_SandTexAmount", 0.4f, "92b");

        [MenuItem("Tools/R141/92c 沙贴图=0.75(旧)")]
        public static void SandTex75() => SandSet("_SandTexAmount", 0.75f, "92c");

        static void SandTex(string path, float amt, string tag)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Ocean_Sand.mat");
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (m == null || t == null) { P("R141 REFUSED: 缺材质或贴图\n"); return; }
            m.SetTexture("_SandTex", t);
            m.SetFloat("_SandTexAmount", amt);
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m);
            P($"R141 {tag}: 沙贴图 -> {t.name} amt={amt:F2}\n");
        }

        [MenuItem("Tools/R141/93 沙贴图=高通版(0.75)")]
        public static void Hp75() => SandTex("Assets/Art/Textures/AI/Proc/Sand_Fine2_AI_highpass.png", 0.75f, "93");

        [MenuItem("Tools/R141/93b 沙贴图=高通版(1.0)")]
        public static void Hp100() => SandTex("Assets/Art/Textures/AI/Proc/Sand_Fine2_AI_highpass.png", 1.0f, "93b");

        [MenuItem("Tools/R141/93c 沙贴图=原版还原")]
        public static void HpOff() => SandTex("Assets/Art/Textures/AI/Proc/Sand_Fine2_AI_albedo.png", 0.75f, "93c");

        [MenuItem("Tools/R141/91 探针")]
        public static void Probe()
        {
            var r = AssetDatabase.LoadAssetAtPath<Material>(Rock);
            var p = AssetDatabase.LoadAssetAtPath<Material>(Peb);
            var d = AssetDatabase.LoadAssetAtPath<Material>(Det);
            P($"R141/91 rock={(r != null ? r.GetColor("_Color").r : -1):F2} "
              + $"pebble={(p != null ? p.GetColor("_Color").r : -1):F2} "
              + $"detHi={(d != null ? d.GetColor("_RockHi").r : -1):F2} "
              + $"fill={(d != null && d.HasProperty("_FillLight") ? d.GetFloat("_FillLight") : -1):F2}\n");
        }
    }
}
#endif
