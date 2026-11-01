// R49Setup.cs — R49：水面"域边界黑楔"的 A/B 与探针
//
// 缺陷：俯视时岛左上方有一个**硬边暗区**（不是黑，RGB 73,149,132，周围 112,200,180）。
// 根因：水深用 `_IsletFloorHRect` 的烘焙高度场，**足印之外直接 clamp 到边缘值** ⇒
//   域内浅水 / 域外深水在一条直线上瞬间跳变。
// 修法：边界外侧 `_DomainFade` 米内把 vd 渐变到 `_DeepDepth`（shader 内实现）。
// 本文件的菜单只为**同会话 A/B** 服务：把 `_DomainFade` 设成 0.001 = 旧行为（硬边）。
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    public static class R49Setup
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
                if (m == null || !seen.Add(m) || !m.HasProperty("_DomainFade")) continue;
                m.SetFloat("_DomainFade", v);
                EditorUtility.SetDirty(m);
                n++;
                P($"     {tag}: {m.name} _DomainFade={v} deepDepth={(m.HasProperty("_DeepDepth") ? m.GetFloat("_DeepDepth") : -1)}\n");
            }
            P($"MENU R49/{tag} 域外过渡 -> {v}（{n} 个材质）\n");
            return n;
        }

        [MenuItem("Tools/R49/90 域外过渡=关(硬边, 仅A/B用)")]
        public static void FadeOff() => SetFade(0.001f, "90");

        [MenuItem("Tools/R49/90b 域外过渡=开(35m)")]
        public static void FadeOn() => SetFade(35f, "90b");

        // 诊断：_DebugMode 1 = 直接输出水深（saturate(vd/6)）。用来判断"暗区是不是深度跳变"。
        static void SetDebug(float v, string tag)
        {
            var seen = new HashSet<Material>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                var m = r.sharedMaterial;
                if (m == null || !seen.Add(m) || !m.HasProperty("_DebugMode")) continue;
                m.SetFloat("_DebugMode", v);
                EditorUtility.SetDirty(m);
                P($"MENU R49/{tag} _DebugMode={v}（{m.name}）\n");
            }
        }

        [MenuItem("Tools/R49/94 诊断:水深可视化")]
        public static void DebugDepth() => SetDebug(1f, "94");

        [MenuItem("Tools/R49/94b 诊断:恢复")]
        public static void DebugOff() => SetDebug(0f, "94b");

        // 二分定位：把 IsletWater 的 Renderer 关掉，看"暗楔"还在不在。
        //   还在 ⇒ 不是水面画的（地形/天空盒/雾）；消失 ⇒ 是水面深度。
        static void SetWaterVisible(bool on, string tag)
        {
            int n = 0;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                var m = r.sharedMaterial;
                if (m == null || m.shader == null || m.shader.name != "WaterProject/Water_Lagoon") continue;
                r.enabled = on;
                n++;
                P($"     {tag}: {r.name} renderer.enabled={on}\n");
            }
            P($"MENU R49/{tag} 水面渲染 {on}（{n} 个）\n");
        }

        [MenuItem("Tools/R49/96 诊断:隐藏水面")]
        public static void HideWater() => SetWaterVisible(false, "96");

        [MenuItem("Tools/R49/96b 诊断:显示水面")]
        public static void ShowWater() => SetWaterVisible(true, "96b");

        [MenuItem("Tools/R49/93 探针(域外过渡/深水深度/足印)")]
        public static void Probe()
        {
            // 先把场景里**所有水**列出来（R49 用关水面做二分时发现：关掉 IsletWater 画面不变）
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                var m = r.sharedMaterial;
                if (m == null || m.shader == null) continue;
                if (!m.shader.name.Contains("Water") && !r.name.ToLower().Contains("water")) continue;
                var b = r.bounds;
                P($"R49 WATER r={r.name} shader={m.shader.name} mat={m.name} enabled={r.enabled} " +
                  $"center=({b.center.x:F0},{b.center.y:F1},{b.center.z:F0}) size=({b.size.x:F0},{b.size.z:F0})\n");
            }
            var rect = Shader.GetGlobalVector("_IsletFloorHRect");
            P($"R49 PROBE GLOBAL _IsletFloorHRect={rect}  _IsletFloorHH={Shader.GetGlobalFloat("_IsletFloorHH")}\n");
            var seen = new HashSet<Material>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                var m = r.sharedMaterial;
                if (m == null || !seen.Add(m) || !m.HasProperty("_DomainFade")) continue;
                P($"R49 PROBE {m.name} domainFade={m.GetFloat("_DomainFade")} " +
                  $"deepDepth={(m.HasProperty("_DeepDepth") ? m.GetFloat("_DeepDepth").ToString("F1") : "-")} " +
                  $"floorRect={(m.HasProperty("_IsletFloorHRect") ? m.GetVector("_IsletFloorHRect").ToString() : "-")}\n");
            }
        }
    }
}
#endif
