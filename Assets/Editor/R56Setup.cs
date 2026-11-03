// R56Setup.cs — R56 Ocean 观感对齐：云量/云底 + 曝光对比
//
// 背景（R55 查出来的）：
//   · Ocean 相机后处理一直是关的（已修），打开后暴露出两个问题：
//       ① 天空顶部压得过暗；② 云是**灰噪斑**而不是云。
//   · 云斑的真因：`Sky_OpenCoast.mat` 的 `_CloudCover = 0.05`（几乎没有云）⇒
//     只偶尔出现几缕，边缘硬、尺寸小，看着就像噪点。云要成"块"，云量必须够。
//   · 另外 `_CloudBaseDark = 0.75`（R32 为了"云底阴影"提上去的）在 Ocean 这块天上过重。
//
// 本文件只做**可逆**的档位切换：原值写 EditorPrefs，`9xb` 还原。
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WaterProject.EditorTools
{
    public static class R56Setup
    {
        static void P(string s)
            => File.AppendAllText(Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        static Volume GlobalVolume()
        {
            Volume best = null;
            foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
            {
                if (v.profile == null) continue;
                if (v.isGlobal) return v;
                if (best == null) best = v;
            }
            return best;
        }

        // ── 云：云量 0.05 → 0.34，云底暗度 0.75 → 0.45 ──
        [MenuItem("Tools/R56/90 Ocean 云量=0.34 云底暗=0.45")]
        public static void Clouds()
        {
            var sky = RenderSettings.skybox;
            if (sky == null) { P("R56/90 REFUSED: 没有 skybox\n"); return; }
            SetSky(sky, "_CloudCover", 0.34f, "R56.cloudCover");
            SetSky(sky, "_CloudBaseDark", 0.45f, "R56.cloudBaseDark");
            EditorUtility.SetDirty(sky);
            AssetDatabase.SaveAssetIfDirty(sky);
            P($"R56/90 云量 {(sky.HasProperty("_CloudCover") ? sky.GetFloat("_CloudCover") : -1):F2} " +
              $"云底暗 {(sky.HasProperty("_CloudBaseDark") ? sky.GetFloat("_CloudBaseDark") : -1):F2}\n");
        }

        [MenuItem("Tools/R56/90b Ocean 云还原")]
        public static void CloudsRevert()
        {
            var sky = RenderSettings.skybox;
            if (sky == null) return;
            if (EditorPrefs.HasKey("R56.cloudCover")) SetSky(sky, "_CloudCover", EditorPrefs.GetFloat("R56.cloudCover"), "revert");
            if (EditorPrefs.HasKey("R56.cloudBaseDark")) SetSky(sky, "_CloudBaseDark", EditorPrefs.GetFloat("R56.cloudBaseDark"), "revert");
            EditorUtility.SetDirty(sky);
            AssetDatabase.SaveAssetIfDirty(sky);
            P("R56/90b 云参数已还原\n");
        }

        static void SetSky(Material sky, string prop, float v, string key)
        {
            if (!sky.HasProperty(prop)) return;
            if (!EditorPrefs.HasKey(key)) EditorPrefs.SetFloat(key, sky.GetFloat(prop));
            sky.SetFloat(prop, v);
        }

        // ── 曝光/对比：-0.18/12 → -0.05/8 ──
        [MenuItem("Tools/R56/91 Ocean 曝光=-0.05 对比=8")]
        public static void Exposure()
        {
            var v = GlobalVolume();
            if (v == null) { P("R56/91 REFUSED: 没有 Volume\n"); return; }
            if (!v.profile.TryGet<ColorAdjustments>(out var ca)) { P("R56/91 REFUSED: 没有 ColorAdjustments\n"); return; }
            if (!EditorPrefs.HasKey("R56.exp"))
            {
                EditorPrefs.SetFloat("R56.exp", ca.postExposure.value);
                EditorPrefs.SetFloat("R56.con", ca.contrast.value);
                P($"R56/91 原值 exp={ca.postExposure.value:F2} contrast={ca.contrast.value:F1}\n");
            }
            ca.postExposure.Override(-0.05f);
            ca.contrast.Override(8f);
            EditorUtility.SetDirty(v.profile);
            AssetDatabase.SaveAssetIfDirty(v.profile);
            P($"R56/91 曝光 {ca.postExposure.value:F2} 对比 {ca.contrast.value:F1}（场景实例 {v.profile.name}）\n");
        }

        [MenuItem("Tools/R56/91b Ocean 曝光/对比 还原")]
        public static void ExposureRevert()
        {
            var v = GlobalVolume();
            if (v == null) return;
            if (!v.profile.TryGet<ColorAdjustments>(out var ca)) return;
            ca.postExposure.Override(EditorPrefs.GetFloat("R56.exp", -0.18f));
            ca.contrast.Override(EditorPrefs.GetFloat("R56.con", 12f));
            EditorUtility.SetDirty(v.profile);
            AssetDatabase.SaveAssetIfDirty(v.profile);
            P("R56/91b 曝光/对比已还原\n");
        }

        [MenuItem("Tools/R56/92 Ocean 云=柔和档(cover0.12 band0.14 steps28)")]
        public static void CloudsSoft()
        {
            var sky = RenderSettings.skybox;
            if (sky == null) { P("R56/92 REFUSED: 没有 skybox\n"); return; }
            SetSky(sky, "_CloudCover", 0.12f, "R56.cloudCover");
            SetSky(sky, "_CloudBand", 0.14f, "R56.cloudBand");
            SetSky(sky, "_CloudSteps", 28f, "R56.cloudSteps");
            SetSky(sky, "_CloudBaseDark", 0.45f, "R56.cloudBaseDark");
            EditorUtility.SetDirty(sky);
            AssetDatabase.SaveAssetIfDirty(sky);
            P($"R56/92 cover={(sky.HasProperty("_CloudCover") ? sky.GetFloat("_CloudCover") : -1):F2} " +
              $"band={(sky.HasProperty("_CloudBand") ? sky.GetFloat("_CloudBand") : -1):F2} " +
              $"steps={(sky.HasProperty("_CloudSteps") ? sky.GetFloat("_CloudSteps") : -1):F0}\n");
        }

        [MenuItem("Tools/R56/93 回读(云/曝光/对比/相机post)")]
        public static void Probe()
        {
            var sky = RenderSettings.skybox;
            if (sky != null)
                P($"R56 PROBE sky={sky.name} cover={(sky.HasProperty("_CloudCover") ? sky.GetFloat("_CloudCover") : -1):F2} " +
                  $"baseDark={(sky.HasProperty("_CloudBaseDark") ? sky.GetFloat("_CloudBaseDark") : -1):F2} " +
                  $"scale={(sky.HasProperty("_CloudScale") ? sky.GetFloat("_CloudScale") : -1):F2}\n");
            var v = GlobalVolume();
            if (v != null && v.profile.TryGet<ColorAdjustments>(out var ca))
                P($"R56 PROBE volume={v.profile.name} exp={ca.postExposure.value:F2} contrast={ca.contrast.value:F1}\n");
            var cam = Camera.main;
            if (cam != null && cam.TryGetComponent<UniversalAdditionalCameraData>(out var d))
                P($"R56 PROBE cam={cam.name} post={d.renderPostProcessing}\n");
        }
    }
}
#endif
