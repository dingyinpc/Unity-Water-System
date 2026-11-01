// R54Setup.cs — R54：径向加深（按离岛距离给水加深）的 A/B 开关
//
// 目的：盖掉 `LandHeight` 末尾 RimFalloff 造成的"放射状硬坎"（R53 定位结论）。
// 径向函数天生平滑、不可能产生直线边，所以它能把那条边"摊平"。
// 默认 `_RadialDepth = 0`（关），本文件只负责同会话 A/B 与标定。
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    public static class R54Setup
    {
        static void P(string s)
            => File.AppendAllText(Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        static int SetRadial(float depth, string tag)
        {
            var seen = new HashSet<Material>();
            int n = 0;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                var m = r.sharedMaterial;
                if (m == null || !seen.Add(m) || !m.HasProperty("_RadialDepth")) continue;
                m.SetFloat("_RadialDepth", depth);
                if (m.HasProperty("_RadialCenter")) m.SetVector("_RadialCenter", new Vector4(0f, 0f, 0f, 0f));
                EditorUtility.SetDirty(m);
                n++;
                P($"     {tag}: {m.name} radialDepth={depth} shallow={(m.HasProperty("_RadialShallow") ? m.GetFloat("_RadialShallow") : -1)} " +
                  $"deep={(m.HasProperty("_RadialDeep") ? m.GetFloat("_RadialDeep") : -1)}\n");
            }
            P($"MENU R54/{tag} 径向加深 -> {depth}（{n} 个材质）\n");
            return n;
        }

        [MenuItem("Tools/R54/90 径向加深=关(0)")]
        public static void Off() => SetRadial(0f, "90");

        [MenuItem("Tools/R54/91 径向加深=12m")]
        public static void On12() => SetRadial(12f, "91");

        [MenuItem("Tools/R54/92 径向加深=20m")]
        public static void On20() => SetRadial(20f, "92");

        [MenuItem("Tools/R54/93 探针")]
        public static void Probe()
        {
            var seen = new HashSet<Material>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                var m = r.sharedMaterial;
                if (m == null || !seen.Add(m) || !m.HasProperty("_RadialDepth")) continue;
                P($"R54 PROBE {m.name} depth={m.GetFloat("_RadialDepth")} " +
                  $"shallow={m.GetFloat("_RadialShallow")} deep={m.GetFloat("_RadialDeep")}\n");
            }
        }
    }
}
#endif
