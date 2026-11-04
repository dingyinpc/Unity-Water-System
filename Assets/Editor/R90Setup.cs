// R90Setup.cs — R90：云边缘抖动可逆 A/B（白噪 vs 交错梯度噪声 IGN）
//
// 背景：Sky_Coast 的云步进起点原本用 Hash21(像素坐标) —— 逐像素白噪抖动。
//   在云的边缘（密度接近阈值处），相邻像素的起始相位完全不同 ⇒ 密度估计差一步
//   ⇒ 视觉上是"椒盐点"。IGN 的取值在局部是渐进的，同样去条带但读起来是"细颗粒"。
//
// ⚠ 跨会话比不了云：天空吃 _Time.y，两次 Play 的云相位不同（AGENTS 第 8 条）。
//   所以这里做成**运行时开关**，同一次冻结会话里切 0/1 各截一张，才是有效 A/B。
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WaterProject.EditorTools
{
    public static class R90Setup
    {
        static void P(string s)
            => File.AppendAllText(Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        static void SetSky(string prop, float v, string tag)
        {
            var sky = RenderSettings.skybox;
            if (sky == null || !sky.HasProperty(prop))
            {
                P($"R90/{tag} REFUSED: 没有 {prop}（sky={(sky == null ? "null" : sky.name)}）\n");
                return;
            }
            float before = sky.GetFloat(prop);
            sky.SetFloat(prop, v);
            EditorUtility.SetDirty(sky);
            if (Application.isPlaying)
                P($"R90/{tag} {prop} {before:F2} -> {v:F2}（仅运行时，不落盘）\n");
            else
            {
                AssetDatabase.SaveAssetIfDirty(sky);   // 只存这一个，避免 SaveAssets 刷掉别的脏资产
                P($"R90/{tag} {prop} {before:F2} -> {v:F2}（已存材质 {sky.name}）\n");
            }
        }

        [MenuItem("Tools/R90/90 云抖动=IGN(细颗粒)")]
        public static void JitterIgn() => SetSky("_CloudJitterIGN", 1f, "90");

        [MenuItem("Tools/R90/90b 云抖动=白噪(旧)")]
        public static void JitterHash() => SetSky("_CloudJitterIGN", 0f, "90b");

        [MenuItem("Tools/R90/91 探针(云抖动/步数/云量)")]
        public static void Probe()
        {
            var sky = RenderSettings.skybox;
            if (sky == null) { P("R90 PROBE sky=null\n"); return; }
            float F(string p) => sky.HasProperty(p) ? sky.GetFloat(p) : -999f;
            P($"R90 PROBE sky={sky.name} jitterIGN={F("_CloudJitterIGN"):F2} " +
              $"steps={F("_CloudSteps"):F1} cover={F("_CloudCover"):F2} " +
              $"playing={Application.isPlaying}\n");
        }
    }
}
#endif
