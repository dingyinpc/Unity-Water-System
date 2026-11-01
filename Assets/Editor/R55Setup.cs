// R55Setup.cs — R55：先"体检"，把光照/曝光/天空的当前值全部打进探针
//
// 背景：用户反馈"现在又脏又暗，之前是亮的"。截图实测亮度 61.9（明亮档约 140）。
// 这说明不是水 shader 单点问题，而是**整场光照被某个参数压暗了**。
// 本文件只读，不改任何东西 —— 先把账对清楚再动手。
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WaterProject.EditorTools
{
    public static class R55Setup
    {
        static void P(string s)
            => File.AppendAllText(Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        // R55：环境光强度（Islet 原始常量是 0.22，被 R08-S2 构图压到了 0.11）
        [MenuItem("Tools/R55/90 环境光=0.22(原始)")]
        public static void Amb022()
        {
            RenderSettings.ambientIntensity = 0.22f;
            P($"R55/90 ambientIntensity -> {RenderSettings.ambientIntensity:F2}\n");
        }

        [MenuItem("Tools/R55/91 环境光=0.34(更亮一档)")]
        public static void Amb034()
        {
            RenderSettings.ambientIntensity = 0.34f;
            P($"R55/91 ambientIntensity -> {RenderSettings.ambientIntensity:F2}\n");
        }

        [MenuItem("Tools/R55/92 环境光=0.11(S2 构图档)")]
        public static void Amb011()
        {
            RenderSettings.ambientIntensity = 0.11f;
            P($"R55/92 ambientIntensity -> {RenderSettings.ambientIntensity:F2}\n");
        }

        [MenuItem("Tools/R55/99 光照体检(只读)")]
        public static void HealthCheck()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("=== R55 光照体检 ").Append(System.DateTime.Now.ToString("HH:mm:ss")).Append(" ===\n");

            sb.Append($"RenderSettings ambientMode={RenderSettings.ambientMode} " +
                      $"ambientIntensity={RenderSettings.ambientIntensity:F3} " +
                      $"ambientLight={RenderSettings.ambientLight} " +
                      $"ambientSky={RenderSettings.ambientSkyColor}\n");
            sb.Append($"  skybox={(RenderSettings.skybox != null ? RenderSettings.skybox.name : "null")} " +
                      $"fog={RenderSettings.fog} density={RenderSettings.fogDensity:F5} color={RenderSettings.fogColor}\n");

            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type != LightType.Directional) continue;
                sb.Append($"  LIGHT {l.name} I={l.intensity:F2} color={l.color} euler={l.transform.eulerAngles} " +
                          $"shadows={l.shadows} isSun={(RenderSettings.sun == l)}\n");
            }

            var cam = Camera.main;
            if (cam != null)
            {
                sb.Append($"  CAM {cam.name} fov={cam.fieldOfView:F1} post={(cam.TryGetComponent<UniversalAdditionalCameraData>(out var d) ? d.renderPostProcessing.ToString() : "?")}\n");
            }

            var sky = RenderSettings.skybox;
            if (sky != null)
            {
                foreach (var k in new[] { "_NightAmount", "_Exposure", "_CloudCover", "_SunGlow", "_SkyDeep", "_StarBright" })
                    if (sky.HasProperty(k)) sb.Append($"    sky.{k}={sky.GetFloat(k):F3}\n");
            }

            // Volume 里的曝光/对比（如果有）
            foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
            {
                sb.Append($"  VOLUME {v.name} weight={v.weight:F2} priority={v.priority} global={v.isGlobal} " +
                          $"profile={(v.profile != null ? v.profile.name : "null")} " +
                          $"shared={(v.sharedProfile != null ? v.sharedProfile.name : "null")} " +
                          $"inst={(v.profile != null && v.sharedProfile != null && v.profile != v.sharedProfile ? "DIFF" : "same")}\n");
                if (v.profile == null) continue;
                if (v.profile.TryGet<ColorAdjustments>(out var ca))
                    sb.Append($"    ColorAdjustments postExposure={ca.postExposure.value:F2} contrast={ca.contrast.value:F1} saturation={ca.saturation.value:F1}\n");
                if (v.profile.TryGet<Vignette>(out var vg))
                    sb.Append($"    Vignette intensity={vg.intensity.value:F2}\n");
            }

            P(sb.ToString());
        }
    }
}
#endif
