// R79Setup.cs — R79：Ocean 调色校准（朝参考三项收敛）
//
// 参考（f000060 裁 UI）：亮度 123.3 / 对比 51.2 / 饱和 124.3
// Ocean 当前：          135.1 / 45.2 / 92.4
// ⇒ 需要：压亮度、提对比、提饱和。
// 注意：Ocean 的 Volume 是**场景内实例**（inst=DIFF），改资产文件无效，必须改实例。
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WaterProject.EditorTools
{
    public static class R79Setup
    {
        static void P(string s)
            => File.AppendAllText(Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        static Volume Pick()
        {
            foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
                if (v.isGlobal && v.profile != null) return v;
            return null;
        }

        static void Apply(float exposure, float contrast, float sat, string tag)
        {
            var v = Pick();
            if (v == null) { P($"R79/{tag} REFUSED: 没有 Global Volume\n"); return; }
            // ⚠ R57 教训：Volume 可能是"场景实例"，场景重载时会从 sharedProfile(资产) 重建
            //   ⇒ 只写实例，重开场景就白改。这里**两个都写**。
            foreach (var prof in new[] { v.profile, v.sharedProfile })
            {
                if (prof == null) continue;
                if (!prof.TryGet<ColorAdjustments>(out var ca)) continue;
                ca.postExposure.Override(exposure);
                ca.contrast.Override(contrast);
                ca.saturation.Override(sat);
                EditorUtility.SetDirty(prof);
                if (AssetDatabase.Contains(prof)) AssetDatabase.SaveAssetIfDirty(prof);
                P($"     写入 {prof.name}\n");
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            P($"R79/{tag} 曝光{exposure} 对比{contrast} 饱和{sat}（实例 {v.profile.name}）\n");
        }

        [MenuItem("Tools/R79/90 档A 曝光-0.30 对比18 饱和-5")]
        public static void A() => Apply(-0.30f, 18f, -5f, "90");

        [MenuItem("Tools/R79/91 档B 曝光-0.45 对比20 饱和+0")]
        public static void B() => Apply(-0.45f, 20f, 0f, "91");

        [MenuItem("Tools/R79/92 档C 曝光-0.20 对比16 饱和-10")]
        public static void C() => Apply(-0.20f, 16f, -10f, "92");

        [MenuItem("Tools/R79/93 本场景相机后处理=开")]
        public static void PostOn()
        {
            int n = 0;
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                var d = cam.GetComponent<UniversalAdditionalCameraData>();
                if (d == null) d = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
                if (!d.renderPostProcessing) { d.renderPostProcessing = true; EditorUtility.SetDirty(d); n++; }
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            P($"R79/93 本场景 post 开了 {n} 台\n");
        }
        [MenuItem("Tools/R79/94 曝光=-0.55(压爆白) 对比24 饱和-14")]
        public static void PoolDarker() => Apply(-0.55f, 24f, -14f, "94");
        [MenuItem("Tools/R79/99 探针")]
        public static void Probe()
        {
            var v = Pick();
            if (v == null) { P("R79/99 没有 Volume\n"); return; }
            if (v.profile.TryGet<ColorAdjustments>(out var ca))
                P($"R79 PROBE {v.profile.name} exp={ca.postExposure.value:F2} contrast={ca.contrast.value:F1} sat={ca.saturation.value:F1}\n");
        }
    }
}
#endif
