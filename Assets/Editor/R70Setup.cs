// R70Setup.cs — R70：Pond 水面"大块灰斑"逐项排除
//
// 症状：PondScene 水面有大片灰蓝色斑块（像脏抹布），不像水。
// 候选（按可疑度）：平面反射平面、折射偏移、体散射 RayMarch、细节法线。
// 本文件把每一项逐个关掉，配合 Seq/95 采集做 A/B。
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    public static class R70Setup
    {
        static void P(string s)
            => File.AppendAllText(Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        static void Set(string prop, float v, string tag)
        {
            var seen = new HashSet<Material>();
            int n = 0;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                var m = r.sharedMaterial;
                if (m == null || !seen.Add(m) || !m.HasProperty(prop)) continue;
                if (!EditorPrefs.HasKey("R70." + prop)) EditorPrefs.SetFloat("R70." + prop, m.GetFloat(prop));
                m.SetFloat(prop, v);
                EditorUtility.SetDirty(m);
                n++;
            }
            P($"R70/{tag} {prop} = {v}（{n} 个材质）\n");
        }

        [MenuItem("Tools/R70/90 平面反射=0")]
        public static void PlanarOff() => Set("_PlanarStrength", 0f, "90");

        [MenuItem("Tools/R70/91 折射=0")]
        public static void RefractOff() => Set("_RefractStrength", 0f, "91");

        [MenuItem("Tools/R70/92 体散射=0(模式也关)")]
        public static void ScatterOff()
        {
            Set("_ScatterMode", 0f, "92");
            Set("_ScatterStrength", 0f, "92");
        }

        [MenuItem("Tools/R70/93 细节法线=0")]
        public static void DetailOff() => Set("_DetailStrength", 0f, "93");

        [MenuItem("Tools/R70/94 全部还原")]
        public static void Restore()
        {
            foreach (var p in new[] { "_PlanarStrength", "_RefractStrength", "_ScatterMode", "_ScatterStrength", "_DetailStrength", "_ScatterSteps", "_ScatterMaxDist" })
                if (EditorPrefs.HasKey("R70." + p)) Set(p, EditorPrefs.GetFloat("R70." + p), "94");
            P("R70/94 已还原\n");
        }

        [MenuItem("Tools/R70/96 体散射步数=24")]
        public static void ScatterSteps24() => Set("_ScatterSteps", 24f, "96");

        [MenuItem("Tools/R70/97 体散射光程=3m")]
        public static void ScatterDist3() => Set("_ScatterMaxDist", 3f, "97");

        [MenuItem("Tools/R70/98 体散射步数+光程(24/3m)")]
        public static void ScatterBoth()
        {
            Set("_ScatterSteps", 24f, "98");
            Set("_ScatterMaxDist", 3f, "98");
        }
        [MenuItem("Tools/R70/99a 散射强度=0.35")]
        public static void ScatterStr035() => Set("_ScatterStrength", 0.35f, "99a");

        [MenuItem("Tools/R70/99b 散射强度=0.25")]
        public static void ScatterStr025() => Set("_ScatterStrength", 0.25f, "99b");
        [MenuItem("Tools/R70/95 探针(读当前值)")]
        public static void Probe()
        {
            var seen = new HashSet<Material>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                var m = r.sharedMaterial;
                if (m == null || !seen.Add(m) || !m.HasProperty("_PlanarStrength")) continue;
                P($"R70 PROBE {m.name} planar={m.GetFloat("_PlanarStrength"):F2} " +
                  $"refract={m.GetFloat("_RefractStrength"):F2} " +
                  $"scatterMode={m.GetFloat("_ScatterMode"):F0} scatterStr={m.GetFloat("_ScatterStrength"):F2} " +
                  $"detail={m.GetFloat("_DetailStrength"):F2}\n");
            }
        }
    }
}
#endif
