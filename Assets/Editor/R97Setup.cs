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


        // R107：查"巨大白色光斑"——先把 Bloom 关掉，看它是不是放大器

        // R111：温和 Bloom —— 只让"太阳"这类真正超过 1.3 的高光发光，不把整幅画面糊掉

        // R112：Ocean 的"平"—— 对比/饱和各提一档（参考 51.2 / 124.3，我们是 40.8 / 107）
        [MenuItem("Tools/R112/90 Ocean 对比26 饱和+2")]
        public static void OceanGradeA() => Grade(-0.30f, 26f, 2f, "R112-90");

        [MenuItem("Tools/R112/91 Ocean 对比32 饱和+8")]
        public static void OceanGradeB() => Grade(-0.35f, 32f, 8f, "R112-91");

        [MenuItem("Tools/R112/92 Ocean 对比20 饱和-2(原档)")]
        public static void OceanGradeBack() => Grade(-0.30f, 18f, -5f, "R112-92");

        [MenuItem("Tools/R111/90 Bloom 温和档(阈值1.30/强度0.30)")]
        public static void BloomGentle()
        {
            var prof = Profile();
            if (prof == null) { P("R111/90 REFUSED: 无全局 Volume\n"); return; }
            if (prof.TryGet<Bloom>(out var b))
            {
                b.intensity.value = 0.30f;
                b.threshold.value = 1.30f;
                b.scatter.value = 0.65f;
                EditorUtility.SetDirty(b);
                P($"R111/90 Bloom 阈值 1.30 / 强度 0.30（{prof.name}）\n");
            }
            else P("R111/90 没有 Bloom override\n");
        }

        [MenuItem("Tools/R111/91 探针(Bloom)")]
        public static void BloomProbe()
        {
            var prof = Profile();
            if (prof == null) { P("R111/91 无 Volume\n"); return; }
            if (prof.TryGet<Bloom>(out var b))
                P($"R111 PROBE bloom={b.intensity.value:F2} thr={b.threshold.value:F2} scatter={b.scatter.value:F2} vol={prof.name}\n");
            else P($"R111 PROBE {prof.name} 无 Bloom\n");
        }

        [MenuItem("Tools/R107/90 Bloom=关")]
        public static void BloomOff()
        {
            var prof = Profile();
            if (prof == null) { P("R107/90 REFUSED: 无全局 Volume\n"); return; }
            if (prof.TryGet<Bloom>(out var b)) { b.intensity.value = 0f; EditorUtility.SetDirty(b); P("R107/90 Bloom intensity -> 0\n"); }
            else P("R107/90 Volume 没有 Bloom override\n");
        }

        [MenuItem("Tools/R107/91 Bloom=0.6(参考档)")]
        public static void BloomOn()
        {
            var prof = Profile();
            if (prof == null) { P("R107/91 REFUSED: 无全局 Volume\n"); return; }
            if (prof.TryGet<Bloom>(out var b)) { b.intensity.value = 0.6f; EditorUtility.SetDirty(b); P("R107/91 Bloom intensity -> 0.6\n"); }
        }

        [MenuItem("Tools/R107/92 探针(Bloom/ToneMapping/其它)")]
        public static void ProbePost()
        {
            var prof = Profile();
            if (prof == null) { P("R107/92 无全局 Volume\n"); return; }
            string s2 = $"R107 PROBE {prof.name}";
            if (prof.TryGet<Bloom>(out var b))     s2 += $" bloom={b.intensity.value:F2}(thr={b.threshold.value:F2})";
            if (prof.TryGet<Tonemapping>(out var t)) s2 += $" tonemap={t.mode.value}";
            if (prof.TryGet<ColorAdjustments>(out var ca)) s2 += $" exp={ca.postExposure.value:F2}";
            if (prof.TryGet<WhiteBalance>(out var wb)) s2 += $" temp={wb.temperature.value:F0}";
            P(s2 + "\n");
        }

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
