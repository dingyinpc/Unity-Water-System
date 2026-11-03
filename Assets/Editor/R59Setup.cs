// R59Setup.cs — R59：域边界"沙底返色淡出"的 A/B 开关
//
// 缺陷链条（R53 逐项可视化定案）：
//   vd 在域边界 1 像素跳变 → 但深度两侧都饱和，改深度无效；
//   sandMix / waterBody / fres 都平滑 → 差在**底部折射采样**（域内采到沙底=亮，域外=深水）。
// 修法：让"沙底返色"在靠边 `_EdgeFade` 米内淡出（shader 里 edgeFade）。
// 本文件只切这个参数，便于同会话 A/B。
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    public static class R59Setup
    {
        static void P(string s)
            => File.AppendAllText(Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        static int SetFade(float v, string tag)
        {
            var seen = new HashSet<Material>();
            int n = 0;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                var m = r.sharedMaterial;
                if (m == null || !seen.Add(m) || !m.HasProperty("_EdgeFade")) continue;
                m.SetFloat("_EdgeFade", v);
                EditorUtility.SetDirty(m);
                n++;
                P($"     {tag}: {m.name} _EdgeFade={v}\n");
            }
            P($"MENU R59/{tag} 沙底淡出 -> {v}（{n} 个材质）\n");
            return n;
        }

        [MenuItem("Tools/R59/90 域边界沙底淡出=关(旧行为, 仅A/B用)")]
        public static void Off() => SetFade(0.001f, "90");

        [MenuItem("Tools/R59/91 域边界沙底淡出=60m")]
        public static void On60() => SetFade(60f, "91");

        [MenuItem("Tools/R59/92 域边界沙底淡出=100m")]
        public static void On100() => SetFade(100f, "92");

        [MenuItem("Tools/R59/93 探针")]
        public static void Probe()
        {
            var seen = new HashSet<Material>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                var m = r.sharedMaterial;
                if (m == null || !seen.Add(m) || !m.HasProperty("_EdgeFade")) continue;
                P($"R59 PROBE {m.name} edgeFade={m.GetFloat("_EdgeFade")}\n");
            }
        }
    }
}
#endif
