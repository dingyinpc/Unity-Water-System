// R130Setup.cs — R130 礁石"绿土豆"（苔覆盖封顶）
//
// 现状：mossW = 顶点色R(=1) × 噪声 ⇒ 整块石头被涂成 _MossColor，岩灰看不见。
// 90/90b/90c 苔覆盖 = 0.15 / 0.30 / 0.60；90d 还原 1.0（旧）
// 91 岩色提升档（_RockLo/Mid/Hi 一起抬）
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    internal static class R130Setup
    {
        static readonly string[] Mats =
        {
            "Assets/Art/Materials/Islet/Islet_Rock_Splat.mat",
            "Assets/Art/Materials/Islet_ReefRockDetail.mat",   // Reef_*/R47_Rock_* 用的是这一张（不在 Islet/ 下）
            "Assets/Art/Materials/Islet_ReefRock.mat",
            "Assets/Art/Materials/Islet/Islet_Rock_Hi.mat",
            "Assets/Art/Materials/Islet/Islet_Rock_Mid.mat",
            "Assets/Art/Materials/Islet/Islet_Rock_Lo.mat",
        };

        static void P(string s) => System.IO.File.AppendAllText(
            System.IO.Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        static void ForEach(System.Action<Material> f, string tag)
        {
            int n = 0;
            foreach (var path in Mats)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) continue;
                if (!m.HasProperty("_MossAmount")) continue;
                f(m);
                EditorUtility.SetDirty(m);
                AssetDatabase.SaveAssetIfDirty(m);
                n++;
            }
            P($"R130 {tag}: 写入 {n} 张岩石材质\n");
        }

        [MenuItem("Tools/R130/90 苔覆盖=0.15")]
        public static void Moss15() => ForEach(m => m.SetFloat("_MossAmount", 0.15f), "90 0.15");

        [MenuItem("Tools/R130/90b 苔覆盖=0.30")]
        public static void Moss30() => ForEach(m => m.SetFloat("_MossAmount", 0.30f), "90b 0.30");

        [MenuItem("Tools/R130/90c 苔覆盖=0.60")]
        public static void Moss60() => ForEach(m => m.SetFloat("_MossAmount", 0.60f), "90c 0.60");

        [MenuItem("Tools/R130/90d 苔覆盖=1.0(旧)")]
        public static void Moss100() => ForEach(m => m.SetFloat("_MossAmount", 1f), "90d 1.0");

        [MenuItem("Tools/R130/91 岩色提亮(Hi/Mid/Lo +30%)")]
        public static void RockBrighter()
        {
            ForEach(m =>
            {
                m.SetColor("_RockHi", new Color(0.60f, 0.59f, 0.565f, 1f));
                m.SetColor("_RockMid", new Color(0.49f, 0.48f, 0.46f, 1f));
                m.SetColor("_RockLo", new Color(0.365f, 0.358f, 0.34f, 1f));
            }, "91 岩色提亮");
        }

        [MenuItem("Tools/R130/91b 岩色还原")]
        public static void RockColorBack()
        {
            ForEach(m =>
            {
                m.SetColor("_RockHi", new Color(0.46f, 0.455f, 0.435f, 1f));
                m.SetColor("_RockMid", new Color(0.38f, 0.375f, 0.355f, 1f));
                m.SetColor("_RockLo", new Color(0.28f, 0.275f, 0.26f, 1f));
            }, "91b 岩色还原");
        }

        [MenuItem("Tools/R130/93 岩贴图=关(旧)")]
        public static void RockTexOff() => ForEach(m => m.SetFloat("_RockTexAmount", 0f), "93 贴图关");

        [MenuItem("Tools/R130/93b 岩贴图=0.5")]
        public static void RockTex50() => ForEach(m => m.SetFloat("_RockTexAmount", 0.5f), "93b 贴图0.5");

        [MenuItem("Tools/R130/93c 岩贴图=1.0")]
        public static void RockTex100() => ForEach(m => m.SetFloat("_RockTexAmount", 1f), "93c 贴图1.0");

        [MenuItem("Tools/R130/93d 岩贴图=0.5 且更细(0.45/m)")]
        public static void RockTexFine()
        {
            ForEach(m => { m.SetFloat("_RockTexAmount", 0.5f); m.SetFloat("_RockTexScale", 0.45f); }, "93d 细0.45");
        }

        [MenuItem("Tools/R130/94 岩石贴图=接上玄武岩(0.5)")]
        public static void RockTexAssign()
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/Art/Textures/AI/Proc/Rock_Basalt_AI_albedo.png");
            if (tex == null) { P("R130/94 REFUSED: 没有 Rock_Basalt_AI_albedo.png\n"); return; }
            ForEach(m =>
            {
                m.SetTexture("_RockTex", tex);
                m.SetFloat("_RockTexAmount", 0.5f);
                m.SetFloat("_RockTexScale", 0.18f);
            }, "94 玄武岩0.5");
        }

        [MenuItem("Tools/R130/94b 岩石贴图=接上玄武岩(1.0)")]
        public static void RockTexAssignFull()
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/Art/Textures/AI/Proc/Rock_Basalt_AI_albedo.png");
            if (tex == null) { P("R130/94b REFUSED\n"); return; }
            ForEach(m =>
            {
                m.SetTexture("_RockTex", tex);
                m.SetFloat("_RockTexAmount", 1f);
                m.SetFloat("_RockTexScale", 0.18f);
            }, "94b 玄武岩1.0");
        }

        [MenuItem("Tools/R130/92 探针: 岩石材质")]
        public static void Probe()
        {
            var sb = new System.Text.StringBuilder("R130/92 岩石材质\n");
            foreach (var path in Mats)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { sb.Append($"  {System.IO.Path.GetFileName(path)}: 不存在\n"); continue; }
                sb.Append($"  {System.IO.Path.GetFileName(path)}:")
                  .Append(m.HasProperty("_MossAmount") ? $" moss={m.GetFloat("_MossAmount"):F2}" : " moss=n/a")
                  .Append(m.HasProperty("_RockMid") ? $" mid=({m.GetColor("_RockMid").r:F2})" : "")
                  .Append("\n");
            }
            P(sb.ToString());
        }
    }
}
#endif
