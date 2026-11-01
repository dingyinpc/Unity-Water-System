// R47Setup.cs — R47 的三个 A/B 开关（**同会话**验收用，不是产品功能）
//
// 为什么要这几个菜单：
//   R47 的修法（距离淡出）改的是"参数默认值"，没有它就无法在同一会话里做出
//   "改前/改后"两帧 —— 跨会话比会因为云/浪相位差伪造差异（铁律 8）。
//   所以这里提供"关掉淡出 = 旧行为"和"恢复淡出 = 新行为"两个开关。
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    public static class R47Setup
    {
        static void P(string s)
            => File.AppendAllText(Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        // 每个 shader 的"参考档"淡出距离（近, 远）——必须与 shader 里的默认值一致
        static readonly (string shader, Vector2 fade)[] Presets =
        {
            ("WaterProject/Water_Ocean",    new Vector2(60f, 260f)),
            ("WaterProject/Water_Lagoon",   new Vector2(35f, 150f)),
            ("WaterProject/Water_Advanced", new Vector2(45f, 200f)),
        };

        static int Apply(bool off, string tag)
        {
            var seen = new HashSet<Material>();
            int n = 0;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                var m = r.sharedMaterial;
                if (m == null || !seen.Add(m) || !m.HasProperty("_SparkleFade")) continue;
                Vector2 v = new Vector2(0f, 999999f);          // off = 远处也不淡出（旧行为）
                if (!off)
                    foreach (var p in Presets)
                        if (m.shader != null && m.shader.name == p.shader) { v = p.fade; break; }
                m.SetVector("_SparkleFade", new Vector4(v.x, v.y, 0f, 0f));
                EditorUtility.SetDirty(m);
                n++;
                P($"     {tag}: {m.name} _SparkleFade=({v.x},{v.y})\n");
            }
            P($"MENU{tag} 亮闪距离淡出 {(off ? "关(旧行为)" : "开(参考档)")}，改了 {n} 个材质\n");
            return n;
        }

        [MenuItem("Tools/R47/91 亮闪淡出=关(旧行为, 仅A/B用)")]
        public static void FadeOff() => Apply(true, "R47/91");

        [MenuItem("Tools/R47/91b 亮闪淡出=开(参考档)")]
        public static void FadeOn() => Apply(false, "R47/91b");

        // 诊断用：把淡出区间设成 (0,1) ⇒ **所有距离都淡到 0 = 亮闪彻底关掉**。
        // 用来判断"画面里的白点到底是不是 Sparkle 产生的"（R47 第一次 A/B 就是靠它证伪的）。
        [MenuItem("Tools/R47/94 实验:亮闪=全关(诊断)")]
        public static void SparkleKill()
        {
            var seen = new HashSet<Material>();
            int n = 0;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                var m = r.sharedMaterial;
                if (m == null || !seen.Add(m) || !m.HasProperty("_SparkleFade")) continue;
                m.SetVector("_SparkleFade", new Vector4(0f, 1f, 0f, 0f));
                EditorUtility.SetDirty(m);
                n++;
            }
            P($"MENU R47/94 亮闪全关（{n} 个材质）\n");
        }

        // ── 白点二分定位用的一次性开关（Lagoon 的三个高频来源）──
        // 背景：R47 第一轮 A/B 发现"把 Sparkle 全关"画面**逐位相同** ⇒ 白点另有来源。
        // 这三个开关就是拿来逐个排除的；95d 还原。
        static float _savGlitter, _savCap, _savFoam; static bool _hasSaved;

        static void SetProp(string prop, float v, string tag)
        {
            int n = 0;
            var seen = new HashSet<Material>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                var m = r.sharedMaterial;
                if (m == null || !seen.Add(m) || !m.HasProperty(prop)) continue;
                if (!_hasSaved)
                {
                    _savGlitter = m.HasProperty("_GlitterStrength") ? m.GetFloat("_GlitterStrength") : -1f;
                    _savCap     = m.HasProperty("_CapStrength")     ? m.GetFloat("_CapStrength")     : -1f;
                    _savFoam    = m.HasProperty("_FoamStrength")    ? m.GetFloat("_FoamStrength")    : -1f;
                    _hasSaved = true;
                }
                m.SetFloat(prop, v);
                EditorUtility.SetDirty(m);
                n++;
            }
            P($"MENU R47/{tag} {prop}={v}（{n} 个材质）\n");
        }

        [MenuItem("Tools/R47/95 实验:碎闪=0")]
        public static void GlitterOff() => SetProp("_GlitterStrength", 0f, "95");

        [MenuItem("Tools/R47/95b 实验:毛细=0")]
        public static void CapOff() => SetProp("_CapStrength", 0f, "95b");

        [MenuItem("Tools/R47/95c 实验:泡沫=0")]
        public static void FoamOff() => SetProp("_FoamStrength", 0f, "95c");

        [MenuItem("Tools/R47/95d 实验:还原三个参数")]
        public static void RestoreLagoon()
        {
            if (!_hasSaved) { P("R47/95d 无存档\n"); return; }
            if (_savGlitter >= 0f) SetProp("_GlitterStrength", _savGlitter, "95d");
            if (_savCap >= 0f)     SetProp("_CapStrength",     _savCap,     "95d");
            if (_savFoam >= 0f)    SetProp("_FoamStrength",    _savFoam,    "95d");
            _hasSaved = false;
            P("R47/95d 已还原\n");
        }

        // ── 碎闪距离淡出 的 A/B 开关（同会话验收用）──
        static int SetGlitterFade(Vector2 v, string tag)
        {
            int n = 0;
            var seen = new HashSet<Material>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                var m = r.sharedMaterial;
                if (m == null || !seen.Add(m) || !m.HasProperty("_GlitterFade")) continue;
                m.SetVector("_GlitterFade", new Vector4(v.x, v.y, 0f, 0f));
                EditorUtility.SetDirty(m);
                n++;
                P($"     {tag}: {m.name} _GlitterFade=({v.x},{v.y})\n");
            }
            P($"MENU R47/{tag} 碎闪淡出 -> ({v.x},{v.y})（{n} 个材质）\n");
            return n;
        }

        [MenuItem("Tools/R47/96 碎闪淡出=关(旧行为, 仅A/B用)")]
        public static void GlitterFadeOff() => SetGlitterFade(new Vector2(0f, 999999f), "96");

        [MenuItem("Tools/R47/96b 碎闪淡出=开(参考档)")]
        public static void GlitterFadeOn() => SetGlitterFade(new Vector2(18f, 75f), "96b");

        // 只读探针：确认每个水面材质现在用的是哪一档
        [MenuItem("Tools/R47/93 探针(亮闪淡出/沙粒/珊瑚材质)")]
        public static void Probe()
        {
            var seen = new HashSet<Material>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                var m = r.sharedMaterial;
                if (m == null || !seen.Add(m)) continue;
                bool interesting = m.HasProperty("_SparkleFade") || m.HasProperty("_GrainAmount");
                if (!interesting) continue;
                P($"R47 PROBE mat={m.name} shader={m.shader.name} " +
                  $"sparkleFade={(m.HasProperty("_SparkleFade") ? m.GetVector("_SparkleFade").ToString() : "-")} " +
                  $"grain={(m.HasProperty("_GrainAmount") ? m.GetFloat("_GrainAmount").ToString("F2") : "-")} " +
                  $"renderer={r.name}\n");
            }

            // 场景里的碰撞体普查（礁石/珊瑚打点失败时先看这个）
            int tc = 0, mc = 0;
            foreach (var t in Object.FindObjectsByType<TerrainCollider>(FindObjectsSortMode.None)) tc++;
            foreach (var c in Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None)) mc++;
            int hits = 0; float ymin = 9999f, ymax = -9999f;
            var rng = new System.Random(7);
            for (int k = 0; k < 60; k++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float rr = Mathf.Lerp(4f, 95f, Mathf.Sqrt((float)rng.NextDouble()));
                var o = new Vector3(Mathf.Cos(a) * rr, 90f, Mathf.Sin(a) * rr);
                if (Physics.Raycast(o, Vector3.down, out var hit, 200f))
                {
                    hits++;
                    if (hit.point.y < ymin) ymin = hit.point.y;
                    if (hit.point.y > ymax) ymax = hit.point.y;
                }
            }
            P($"R47 PROBE colliders terrain={tc} mesh={mc} | 60 次竖打点命中 {hits} 次" +
              (hits > 0 ? $"，落点 y ∈ [{ymin:F2}, {ymax:F2}]" : "（**打不到地面**）") + "\n");

            int rocks = 0, corals = 0;
            foreach (var g in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            {
                if (g.name.StartsWith("R47_Rock_")) rocks++;
                else if (g.name.StartsWith("R47_Coral_")) corals++;
            }
            P($"R47 PROBE content rocks={rocks} corals={corals} scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}\n");
        }
    }
}
#endif
