// R97Setup.cs — R97：Pond 的"后处理开 + 正常曝光"重标定
//
// 背景：Pond 的 Volume 曝光被写成 **+2.80**（对比 67.8、饱和 −44.1），
//   而相机 `renderPostProcessing=False` —— 也就是说这套调色**从来没生效过**，
//   画面是"裸渲"。R57 曾把后处理打开，结果整屏白（252），于是又关回去。
//
// 正确做法不是"要么全关要么白屏"，而是：**开后处理 + 把曝光拉回正常范围**重标定。
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WaterProject.EditorTools
{
    public static class R97Setup
    {
        static void P(string s)
            => File.AppendAllText(Path.Combine(Application.dataPath, "../Logs/water_probe.txt"), s);

        static VolumeProfile Profile()
        {
            var vols = Object.FindObjectsOfType<Volume>();
            foreach (var v in vols)
            {
                if (v.isGlobal && v.sharedProfile != null) return v.sharedProfile;
            }
            return null;
        }

        static void Grade(float exp, float contrast, float sat, string tag)
        {
            var prof = Profile();
            if (prof == null) { P($"R97/{tag} REFUSED: 没有全局 Volume\n"); return; }

            if (prof.TryGet<ColorAdjustments>(out var ca))
            {
                ca.postExposure.value = exp;
                ca.contrast.value = contrast;
                ca.saturation.value = sat;
                EditorUtility.SetDirty(ca);
            }
            else { P($"R97/{tag} REFUSED: Volume 没有 ColorAdjustments\n"); return; }

            if (!Application.isPlaying)
            {
                // 同时写"场景里那份实例"和"资产"，否则重开场景会被资产覆盖
                AssetDatabase.SaveAssets();
            }
            P($"R97/{tag} 曝光={exp:F2} 对比={contrast:F1} 饱和={sat:F1}（{prof.name}）playing={Application.isPlaying}\n");
        }

        [MenuItem("Tools/R97/90 曝光+1.0 对比16 饱和-10")]
        public static void G90() => Grade(1.0f, 16f, -10f, "90");

        [MenuItem("Tools/R97/91 曝光+1.6 对比16 饱和-10")]
        public static void G91() => Grade(1.6f, 16f, -10f, "91");

        [MenuItem("Tools/R97/92 曝光+2.2 对比16 饱和-10")]
        public static void G92() => Grade(2.2f, 16f, -10f, "92");

        [MenuItem("Tools/R97/93 曝光+2.8 对比16 饱和-10(旧曝光)")]
        public static void G93() => Grade(2.8f, 16f, -10f, "93");

        [MenuItem("Tools/R97/94 探针")]
        public static void Probe()
        {
            var prof = Profile();
            if (prof == null) { P("R97 PROBE 无全局 Volume\n"); return; }
            string s = $"R97 PROBE {prof.name}";
            if (prof.TryGet<ColorAdjustments>(out var ca))
                s += $" exp={ca.postExposure.value:F2} contrast={ca.contrast.value:F1} sat={ca.saturation.value:F1}";
            var cam = Camera.main;
            if (cam != null)
            {
                var data = cam.GetUniversalAdditionalCameraData();
                s += $" camPost={(data != null && data.renderPostProcessing)}";
            }
            P(s + "\n");
        }
    }
}
#endif
