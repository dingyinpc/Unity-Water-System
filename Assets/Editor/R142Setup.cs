// R142Setup.cs — R142 礁石"黑色多面体"（URP/Lit 没贴图、平面着色）
//
// Ocean_Rock / Ocean_Pebble 是 URP/Lit 且**没有贴图**（_BaseMap 空槽），
// 所以只剩低模的面片明暗 —— 读成"黑色多面体"。
// 这轮先不换几何，先把**玄武岩 albedo + normal** 挂上去，让平面里长出石头质感。
// 90 接上贴图(中性底) / 90b 只接法线 / 90c 还原
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    internal static class R142Setup
    {
        static readonly string[] Mats =
        {
            "Assets/Art/Materials/Ocean_Rock.mat",
            "Assets/Art/Materials/Ocean_Pebble.mat",
        };
        const string Alb = "Assets/Art/Textures/AI/Proc/Rock_Basalt_AI_albedo.png";
        const string Nrm = "Assets/Art/Textures/AI/Proc/Rock_Basalt_AI_normal.png";

        static void P(string s) => System.IO.File.AppendAllText(
            System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        static void Apply(string tag, bool doAlb, bool doNrm, float tiling, Color baseCol, float smooth)
        {
            var a = AssetDatabase.LoadAssetAtPath<Texture2D>(Alb);
            var n = AssetDatabase.LoadAssetAtPath<Texture2D>(Nrm);
            int cnt = 0;
            foreach (var path in Mats)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) continue;
                m.SetTexture("_BaseMap", doAlb ? a : null);
                if (doAlb) m.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
                m.SetTexture("_BumpMap", doNrm ? n : null);
                if (doNrm) { m.SetTextureScale("_BumpMap", new Vector2(tiling, tiling)); m.SetFloat("_BumpScale", 1.0f); }
                m.SetColor("_BaseColor", baseCol);
                m.SetFloat("_Smoothness", smooth);
                EditorUtility.SetDirty(m);
                AssetDatabase.SaveAssetIfDirty(m);
                cnt++;
            }
            P($"R142 {tag}: alb={doAlb} nrm={doNrm} tiling={tiling} 写入 {cnt} 张\n");
        }

        [MenuItem("Tools/R142/90 礁石=albedo+法线(中性底)")]
        public static void Both() => Apply("90", true, true, 3f, new Color(0.42f, 0.41f, 0.39f, 1f), 0.15f);

        [MenuItem("Tools/R142/90b 礁石=只接法线")]
        public static void OnlyNrm() => Apply("90b", false, true, 3f, new Color(0.42f, 0.41f, 0.39f, 1f), 0.15f);

        [MenuItem("Tools/R142/90c 礁石=还原")]
        public static void Revert() => Apply("90c", false, false, 1f, new Color(0.20f, 0.195f, 0.185f, 1f), 0.3f);

        [MenuItem("Tools/R142/91 探针")]
        public static void Probe()
        {
            foreach (var path in Mats)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) continue;
                var t = m.GetTexture("_BaseMap");
                var n = m.GetTexture("_BumpMap");
                var c = m.GetColor("_BaseColor");
                P($"R142/91 {System.IO.Path.GetFileName(path)}: base={(t != null ? t.name : "null")} "
                  + $"bump={(n != null ? n.name : "null")} color=({c.r:F2},{c.g:F2},{c.b:F2})\n");
            }
        }
    }
}
#endif
