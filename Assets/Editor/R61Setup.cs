// R61Setup.cs — R61：提亮 Ocean 天顶（改 DayNightCycle 的颜色曲线，不是材质）
//
// R60 的失败教训：直接改 `Sky_OpenCoast.mat` 的 `_ZenithColor` **完全无效**，
//   因为 Ocean 的天空颜色每帧由 `DayNightCycle.Apply()` 从**场景里的 ColorKey[] 曲线**写入。
//   所以要提亮天顶，只能改曲线。
//
// 做法：只动**白天段**（t ∈ [0.35, 0.70]）的 skyZenith / skyHorizon，
//   按倍率乘（默认 1.35），夜里/黄昏/黎明**一律不碰**（R37/R38 调过的别动）。
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using WaterProject.Logic;

namespace WaterProject.EditorTools
{
    public static class R61Setup
    {
        static void P(string s)
            => File.AppendAllText(Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        static void Scale(bool apply, float k, string tag)
        {
            var dn = Object.FindObjectOfType<DayNightCycle>();
            if (dn == null) { P($"R61/{tag} REFUSED: 场景里没有 DayNightCycle\n"); return; }

            int n = 0;
            n += ScaleCurve(dn.skyZenith, k, apply, "zenith");
            n += ScaleCurve(dn.skyHorizon, k, apply, "horizon");
            EditorUtility.SetDirty(dn);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            P($"R61/{tag} 白天段曲线 ×{k}，动了 {n} 个键\n");
        }

        static int ScaleCurve(ColorKey[] keys, float k, bool apply, string name)
        {
            if (keys == null) return 0;
            int n = 0;
            for (int i = 0; i < keys.Length; i++)
            {
                if (keys[i].t < 0.35f || keys[i].t > 0.70f) continue;
                var c = keys[i].c;
                if (apply)
                {
                    c = new Color(Mathf.Min(c.r * k, 1f), Mathf.Min(c.g * k, 1f), Mathf.Min(c.b * k, 1f), c.a);
                    keys[i].c = c;
                    n++;
                }
                P($"    {name}[{i}] t={keys[i].t:F2} -> ({c.r:F3},{c.g:F3},{c.b:F3})\n");
            }
            return n;
        }

        [MenuItem("Tools/R61/90 白天天顶提亮 ×1.35")]
        public static void Brighten() => Scale(true, 1.35f, "90");

        [MenuItem("Tools/R61/91 白天天顶压暗 ×0.74(还原用)")]
        public static void Dim() => Scale(true, 1f / 1.35f, "91");

        [MenuItem("Tools/R61/93 探针(打印白天段曲线)")]
        public static void Probe()
        {
            var dn = Object.FindObjectOfType<DayNightCycle>();
            if (dn == null) { P("R61/93 REFUSED: 没有 DayNightCycle\n"); return; }
            P($"R61 PROBE time={dn.timeOfDay:F2} cycle={dn.cycleSeconds:F1}\n");
            ScaleCurve(dn.skyZenith, 1f, false, "zenith");
            ScaleCurve(dn.skyHorizon, 1f, false, "horizon");
        }

        // 找"远处那块灰色平板"：列出包围盒很大的渲染器
        [MenuItem("Tools/R61/94 大物体探针")]
        public static void BigObjects()
        {
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                var b = r.bounds;
                float size = Mathf.Max(b.size.x, b.size.z);
                if (size < 300f) continue;
                var m = r.sharedMaterial;
                P($"R61 BIG r={r.name} size=({b.size.x:F0},{b.size.y:F0},{b.size.z:F0}) " +
                  $"center=({b.center.x:F0},{b.center.y:F0},{b.center.z:F0}) " +
                  $"mat={(m != null ? m.name : "null")} shader={(m != null && m.shader != null ? m.shader.name : "-")} " +
                  $"enabled={r.enabled}\n");
            }
        }
    }
}
#endif
